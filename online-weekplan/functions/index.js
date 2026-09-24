const { onRequest } = require("firebase-functions/v2/https");
const { initializeApp } = require("firebase-admin/app");
const { getAuth } = require("firebase-admin/auth");
const { getFirestore, FieldValue } = require("firebase-admin/firestore");
const crypto = require("crypto");

initializeApp();
const db = getFirestore();

function cors(res) {
  res.set("Access-Control-Allow-Origin", "*");
  res.set("Access-Control-Allow-Headers", "Content-Type, Authorization");
  res.set("Access-Control-Allow-Methods", "POST, OPTIONS");
}

function fail(res, status, message) {
  res.status(status).json({ error: message });
}

exports.login = onRequest({ region: "europe-west1" }, async (req, res) => {
  cors(res);
  if (req.method === "OPTIONS") return res.status(204).send("");
  if (req.method !== "POST") return fail(res, 405, "Method not allowed.");

  try {
    const companyCode = String(req.body?.companyCode || "").trim().toUpperCase();
    const username = String(req.body?.username || "").trim().toLowerCase();
    const password = String(req.body?.password || "");
    if (!companyCode || !username || !password)
      return fail(res, 400, "Firmen-Code, Benutzername und Passwort erforderlich.");

    const companySnap = await db.collection("companies")
      .where("companyCode", "==", companyCode)
      .limit(1)
      .get();
    if (companySnap.empty) return fail(res, 401, "Anmeldung nicht möglich.");

    const companyDoc = companySnap.docs[0];
    const companyId = companyDoc.id;
    const companyData = companyDoc.data();
    if (companyData.isActive === false) return fail(res, 401, "Anmeldung nicht möglich.");

    const snap = await companyDoc.ref.collection("authUsers")
      .where("usernameNormalized", "==", username)
      .limit(1)
      .get();

    // Same generic error for unknown user and wrong password.
    if (snap.empty) return fail(res, 401, "Anmeldung nicht möglich.");

    const data = snap.docs[0].data();
    if (data.isActive !== true || !data.passwordHash || !data.passwordSalt)
      return fail(res, 401, "Anmeldung nicht möglich.");

    const expected = Buffer.from(data.passwordHash, "base64");
    const salt = Buffer.from(data.passwordSalt, "base64");
    const actual = crypto.pbkdf2Sync(password, salt, 150000, expected.length, "sha256");
    if (expected.length !== actual.length || !crypto.timingSafeEqual(expected, actual))
      return fail(res, 401, "Anmeldung nicht möglich.");

    const uid = "solutioncompakt-" + companyId + "-" + String(data.sourceUserId);
    const customToken = await getAuth().createCustomToken(uid, {
      role: data.role || "Beobachter",
      username: data.username || username,
      displayName: data.displayName || data.username || username,
      sourceUserId: data.sourceUserId,
      companyId,
      companyCode
    });

    res.json({ customToken });
  } catch (error) {
    console.error(error);
    fail(res, 500, "Anmeldung konnte nicht verarbeitet werden.");
  }
});

exports.publishWeekPlan = onRequest({ region: "europe-west1", timeoutSeconds: 120 }, async (req, res) => {
  cors(res);
  if (req.method === "OPTIONS") return res.status(204).send("");
  if (req.method !== "POST") return fail(res, 405, "Method not allowed.");

  try {
    const authHeader = String(req.headers.authorization || "");
    if (!authHeader.startsWith("Bearer ")) return fail(res, 401, "Authentifizierung erforderlich.");
    const claims = await getAuth().verifyIdToken(authHeader.slice("Bearer ".length));
    if (claims.role !== "Administrator") return fail(res, 403, "Administrator erforderlich.");

    const snapshot = req.body;
    if (!snapshot || snapshot.schemaVersion !== "1.1" || !snapshot.weekId ||
        !snapshot.companyId || !snapshot.companyCode ||
        !Array.isArray(snapshot.entries) || !Array.isArray(snapshot.productionSlots)) {
      return fail(res, 400, "Ungültiges Wochenplan-Paket.");
    }
    if (String(snapshot.companyId) !== String(claims.companyId) ||
        String(snapshot.companyCode).toUpperCase() !== String(claims.companyCode || "").toUpperCase()) {
      return fail(res, 403, "Wochenplan gehört zu einer anderen Firma.");
    }

    const weekRef = db.collection("companies").doc(String(claims.companyId))
      .collection("weekPlans").doc(String(snapshot.weekId));
    await weekRef.set({
      schemaVersion: snapshot.schemaVersion,
      companyId: snapshot.companyId,
      companyCode: snapshot.companyCode,
      weekId: snapshot.weekId,
      isoYear: snapshot.isoYear,
      isoWeek: snapshot.isoWeek,
      weekStart: snapshot.weekStart,
      weekEnd: snapshot.weekEnd,
      companyName: snapshot.companyName || "SolutionCompakt",
      siteName: snapshot.siteName || "",
      preparedAtUtc: snapshot.preparedAtUtc || null,
      preparedBy: snapshot.preparedBy || "",
      publishedAt: FieldValue.serverTimestamp(),
      publishedBy: claims.username || claims.uid,
      assignmentCount: snapshot.entries.length,
      productionSlotCount: snapshot.productionSlots.length
    }, { merge: true });

    await replaceCollection(weekRef.collection("entries"), snapshot.entries);
    await replaceCollection(weekRef.collection("productionSlots"), snapshot.productionSlots);

    res.json({ ok: true, weekId: snapshot.weekId });
  } catch (error) {
    console.error(error);
    fail(res, 500, "Wochenplan konnte nicht veröffentlicht werden.");
  }
});

async function replaceCollection(collectionRef, items) {
  const existing = await collectionRef.listDocuments();
  let operations = [];

  for (const ref of existing) operations.push({ type: "delete", ref });
  for (const item of items) {
    const id = String(item.id || "").trim();
    if (!id || id.includes("/")) throw new Error("Invalid item id.");
    operations.push({ type: "set", ref: collectionRef.doc(id), data: item });
  }

  while (operations.length) {
    const batch = db.batch();
    for (const op of operations.splice(0, 450)) {
      if (op.type === "delete") batch.delete(op.ref);
      else batch.set(op.ref, op.data);
    }
    await batch.commit();
  }
}
