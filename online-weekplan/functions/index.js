const { onRequest } = require("firebase-functions/v2/https");
const { initializeApp } = require("firebase-admin/app");
const { getAuth } = require("firebase-admin/auth");
const { getFirestore, FieldValue, Timestamp } = require("firebase-admin/firestore");
const crypto = require("crypto");

initializeApp();
const db = getFirestore();
const OWNER_EMAIL = "irajet.ramadani@gmail.com";

function cors(res) {
  res.set("Access-Control-Allow-Origin", "*");
  res.set("Access-Control-Allow-Headers", "Content-Type, Authorization");
  res.set("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
}

function fail(res, status, message) {
  res.status(status).json({ error: message });
}

function sha256(value) {
  return crypto.createHash("sha256").update(String(value || ""), "utf8").digest("hex");
}

function validInstallationId(value) {
  return /^[A-Za-z0-9_-]{20,100}$/.test(String(value || ""));
}

function validSecret(value) {
  return /^[A-Za-z0-9_-]{30,120}$/.test(String(value || ""));
}

function toIso(value) {
  if (!value) return null;
  if (typeof value.toDate === "function") return value.toDate().toISOString();
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

async function requireOwner(req, res) {
  const authHeader = String(req.headers.authorization || "");
  if (!authHeader.startsWith("Bearer ")) {
    fail(res, 401, "Authentifizierung erforderlich.");
    return null;
  }
  try {
    const claims = await getAuth().verifyIdToken(authHeader.slice("Bearer ".length));
    const email = String(claims.email || "").trim().toLowerCase();
    if (email !== OWNER_EMAIL || claims.email_verified !== true) {
      fail(res, 403, "Keine Berechtigung für die Lizenzverwaltung.");
      return null;
    }
    return claims;
  } catch {
    fail(res, 401, "Anmeldung ist ungültig oder abgelaufen.");
    return null;
  }
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

    const companyRef = db.collection("companies").doc(String(claims.companyId || ""));
    const companyDoc = await companyRef.get();
    if (!companyDoc.exists || companyDoc.data().isActive === false)
      return fail(res, 403, "Firma ist nicht aktiv.");

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

    const weekRef = companyRef.collection("weekPlans").doc(String(snapshot.weekId));
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

exports.licenseRequest = onRequest({ region: "europe-west1" }, async (req, res) => {
  cors(res);
  if (req.method === "OPTIONS") return res.status(204).send("");
  if (req.method !== "POST") return fail(res, 405, "Method not allowed.");

  try {
    const installationId = String(req.body?.installationId || "").trim();
    const secret = String(req.body?.secret || "").trim();
    const companyName = String(req.body?.companyName || "").trim();
    const contactEmail = String(req.body?.email || "").trim().toLowerCase();
    const appVersion = String(req.body?.appVersion || "").trim();

    if (!validInstallationId(installationId) || !validSecret(secret))
      return fail(res, 400, "Ungültige Installationskennung.");
    if (companyName.length < 2 || !contactEmail.includes("@"))
      return fail(res, 400, "Firma und gültige E-Mail-Adresse sind erforderlich.");

    const ref = db.collection("licenses").doc(installationId);
    const snap = await ref.get();
    const secretHash = sha256(secret);

    if (snap.exists) {
      const current = snap.data();
      if (current.secretHash && current.secretHash !== secretHash)
        return fail(res, 401, "Diese Installation konnte nicht bestätigt werden.");

      const preservedStatus = ["active", "suspended"].includes(String(current.status || "").toLowerCase())
        ? current.status
        : "pending";

      await ref.set({
        companyName,
        contactEmail,
        status: preservedStatus,
        lastAppVersion: appVersion,
        requestedAt: FieldValue.serverTimestamp(),
        updatedAt: FieldValue.serverTimestamp()
      }, { merge: true });

      const after = (await ref.get()).data();
      return res.json({
        ok: true,
        status: after.status || "pending",
        validUntilUtc: toIso(after.validUntil),
        message: after.status === "active"
          ? "Registrierung ist bereits freigeschaltet."
          : after.status === "suspended"
            ? "Diese Installation wurde gesperrt. Bitte wenden Sie sich an den Administrator: irajet.ramadani@gmail.com"
            : "Registrierungsanfrage wurde aktualisiert und wartet auf Freischaltung."
      });
    }

    await ref.set({
      installationId,
      secretHash,
      companyName,
      contactEmail,
      status: "pending",
      validUntil: null,
      lastAppVersion: appVersion,
      createdAt: FieldValue.serverTimestamp(),
      requestedAt: FieldValue.serverTimestamp(),
      updatedAt: FieldValue.serverTimestamp()
    });

    res.json({
      ok: true,
      status: "pending",
      validUntilUtc: null,
      message: "Registrierungsanfrage wurde gesendet und wartet auf Freischaltung."
    });
  } catch (error) {
    console.error(error);
    fail(res, 500, "Registrierungsanfrage konnte nicht verarbeitet werden.");
  }
});

exports.licenseRecoverySync = onRequest({ region: "europe-west1" }, async (req, res) => {
  cors(res);
  if (req.method === "OPTIONS") return res.status(204).send("");
  if (req.method !== "POST") return fail(res, 405, "Method not allowed.");

  try {
    const installationId = String(req.body?.installationId || "").trim();
    const secret = String(req.body?.secret || "").trim();
    const recoveryCode = String(req.body?.recoveryCode || "").trim().toUpperCase();
    const companyName = String(req.body?.companyName || "").trim();
    const appVersion = String(req.body?.appVersion || "").trim();

    if (!validInstallationId(installationId) || !validSecret(secret))
      return fail(res, 400, "Ungültige Installationskennung.");
    if (!/^[A-F0-9]{5}(?:-[A-F0-9]{5}){5}$/.test(recoveryCode))
      return fail(res, 400, "Ungültiger Recovery-Code.");

    const ref = db.collection("licenses").doc(installationId);
    const snap = await ref.get();
    const secretHash = sha256(secret);

    if (snap.exists) {
      const current = snap.data();
      if (current.secretHash && current.secretHash !== secretHash)
        return fail(res, 401, "Diese Installation konnte nicht bestätigt werden.");

      await ref.set({
        secretHash,
        companyName: companyName || current.companyName || "",
        supportRecoveryCode: recoveryCode,
        supportRecoveryUpdatedAt: FieldValue.serverTimestamp(),
        lastAppVersion: appVersion,
        updatedAt: FieldValue.serverTimestamp()
      }, { merge: true });
    } else {
      await ref.set({
        installationId,
        secretHash,
        companyName,
        contactEmail: "",
        status: "trial",
        validUntil: null,
        supportRecoveryCode: recoveryCode,
        supportRecoveryUpdatedAt: FieldValue.serverTimestamp(),
        lastAppVersion: appVersion,
        createdAt: FieldValue.serverTimestamp(),
        updatedAt: FieldValue.serverTimestamp()
      });
    }

    res.json({ ok: true });
  } catch (error) {
    console.error(error);
    fail(res, 500, "Recovery-Code konnte nicht für den Support hinterlegt werden.");
  }
});

exports.licenseStatus = onRequest({ region: "europe-west1" }, async (req, res) => {
  cors(res);
  if (req.method === "OPTIONS") return res.status(204).send("");
  if (req.method !== "POST") return fail(res, 405, "Method not allowed.");

  try {
    const installationId = String(req.body?.installationId || "").trim();
    const secret = String(req.body?.secret || "").trim();
    const appVersion = String(req.body?.appVersion || "").trim();
    if (!validInstallationId(installationId) || !validSecret(secret))
      return fail(res, 400, "Ungültige Installationskennung.");

    const ref = db.collection("licenses").doc(installationId);
    const snap = await ref.get();
    if (!snap.exists) {
      return res.json({
        ok: true,
        status: "unregistered",
        validUntilUtc: null,
        message: "Diese Installation ist noch nicht registriert."
      });
    }

    const data = snap.data();
    if (data.secretHash !== sha256(secret))
      return fail(res, 401, "Diese Installation konnte nicht bestätigt werden.");

    await ref.set({
      lastCheckedAt: FieldValue.serverTimestamp(),
      lastAppVersion: appVersion,
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });

    const validUntil = data.validUntil?.toDate?.() || null;
    const expired = data.status === "active" && (!validUntil || validUntil <= new Date());
    const status = expired ? "expired" : (data.status || "pending");

    res.json({
      ok: true,
      status,
      validUntilUtc: validUntil ? validUntil.toISOString() : null,
      message: status === "active"
        ? `Lizenz freigeschaltet bis ${validUntil.toLocaleDateString("de-CH")}.`
        : status === "pending"
          ? "Registrierungsanfrage wartet auf Freischaltung."
          : status === "suspended"
            ? "Diese Installation wurde gesperrt. Bitte wenden Sie sich an den Administrator: irajet.ramadani@gmail.com"
            : status === "expired"
              ? "Die Freischaltung ist abgelaufen."
              : "Diese Installation ist noch nicht freigeschaltet."
    });
  } catch (error) {
    console.error(error);
    fail(res, 500, "Lizenzstatus konnte nicht geprüft werden.");
  }
});

exports.adminLicenses = onRequest({ region: "europe-west1" }, async (req, res) => {
  cors(res);
  if (req.method === "OPTIONS") return res.status(204).send("");
  if (req.method !== "POST") return fail(res, 405, "Method not allowed.");
  const claims = await requireOwner(req, res);
  if (!claims) return;

  try {
    const snap = await db.collection("licenses").get();
    const licenses = snap.docs.map(doc => {
      const data = doc.data();
      return {
        installationId: doc.id,
        companyName: data.companyName || "",
        contactEmail: data.contactEmail || "",
        status: data.status || "pending",
        validUntilUtc: toIso(data.validUntil),
        createdAtUtc: toIso(data.createdAt),
        requestedAtUtc: toIso(data.requestedAt),
        lastCheckedAtUtc: toIso(data.lastCheckedAt),
        lastAppVersion: data.lastAppVersion || "",
        hasRecoveryCode: Boolean(data.supportRecoveryCode),
        recoveryCodeUpdatedAtUtc: toIso(data.supportRecoveryUpdatedAt)
      };
    });
    res.json({ ok: true, licenses });
  } catch (error) {
    console.error(error);
    fail(res, 500, "Registrierungen konnten nicht geladen werden.");
  }
});

exports.adminGetRecoveryCode = onRequest({ region: "europe-west1" }, async (req, res) => {
  cors(res);
  if (req.method === "OPTIONS") return res.status(204).send("");
  if (req.method !== "POST") return fail(res, 405, "Method not allowed.");
  const claims = await requireOwner(req, res);
  if (!claims) return;

  try {
    const installationId = String(req.body?.installationId || "").trim();
    if (!validInstallationId(installationId))
      return fail(res, 400, "Ungültige Installationskennung.");

    const ref = db.collection("licenses").doc(installationId);
    const snap = await ref.get();
    if (!snap.exists) return fail(res, 404, "Registrierung nicht gefunden.");

    const data = snap.data();
    const recoveryCode = String(data.supportRecoveryCode || "").trim();
    if (!recoveryCode) return fail(res, 404, "Für diese Installation ist noch kein Recovery-Code hinterlegt.");

    await ref.set({
      recoveryCodeViewedAt: FieldValue.serverTimestamp(),
      recoveryCodeViewedBy: String(claims.email || OWNER_EMAIL),
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });

    res.json({
      ok: true,
      recoveryCode,
      updatedAtUtc: toIso(data.supportRecoveryUpdatedAt)
    });
  } catch (error) {
    console.error(error);
    fail(res, 500, "Recovery-Code konnte nicht geladen werden.");
  }
});

exports.adminExtendLicense = onRequest({ region: "europe-west1" }, async (req, res) => {
  cors(res);
  if (req.method === "OPTIONS") return res.status(204).send("");
  if (req.method !== "POST") return fail(res, 405, "Method not allowed.");
  const claims = await requireOwner(req, res);
  if (!claims) return;

  try {
    const installationId = String(req.body?.installationId || "").trim();
    const days = Number(req.body?.days);
    if (!validInstallationId(installationId) || !Number.isInteger(days) || days < 1 || days > 3650)
      return fail(res, 400, "Installation und 1 bis 3650 Tage sind erforderlich.");

    const ref = db.collection("licenses").doc(installationId);
    const snap = await ref.get();
    if (!snap.exists) return fail(res, 404, "Registrierung nicht gefunden.");

    const current = snap.data();
    const now = new Date();
    const existing = current.validUntil?.toDate?.();
    const base = existing && existing > now ? existing : now;
    const validUntil = new Date(base.getTime() + days * 24 * 60 * 60 * 1000);

    await ref.set({
      status: "active",
      validUntil: Timestamp.fromDate(validUntil),
      approvedAt: FieldValue.serverTimestamp(),
      approvedBy: String(claims.email || OWNER_EMAIL),
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });

    res.json({
      ok: true,
      status: "active",
      validUntilUtc: validUntil.toISOString(),
      message: `Freischaltung um ${days} Tag(e) verlängert.`
    });
  } catch (error) {
    console.error(error);
    fail(res, 500, "Freischaltung konnte nicht gespeichert werden.");
  }
});

exports.adminSetLicenseStatus = onRequest({ region: "europe-west1" }, async (req, res) => {
  cors(res);
  if (req.method === "OPTIONS") return res.status(204).send("");
  if (req.method !== "POST") return fail(res, 405, "Method not allowed.");
  const claims = await requireOwner(req, res);
  if (!claims) return;

  try {
    const installationId = String(req.body?.installationId || "").trim();
    const status = String(req.body?.status || "").trim().toLowerCase();
    if (!validInstallationId(installationId) || !["pending", "suspended"].includes(status))
      return fail(res, 400, "Ungültiger Lizenzstatus.");

    const ref = db.collection("licenses").doc(installationId);
    if (!(await ref.get()).exists) return fail(res, 404, "Registrierung nicht gefunden.");

    await ref.set({
      status,
      statusChangedAt: FieldValue.serverTimestamp(),
      statusChangedBy: String(claims.email || OWNER_EMAIL),
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });

    res.json({ ok: true, status });
  } catch (error) {
    console.error(error);
    fail(res, 500, "Lizenzstatus konnte nicht gespeichert werden.");
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
