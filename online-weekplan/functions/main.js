const existingFunctions = require("./index");
const { onRequest } = require("firebase-functions/v2/https");
const { getAuth } = require("firebase-admin/auth");
const { getFirestore, FieldValue, Timestamp } = require("firebase-admin/firestore");

Object.assign(exports, existingFunctions, require("./retention"));

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

function validInstallationId(value) {
  return /^[A-Za-z0-9_-]{20,100}$/.test(String(value || ""));
}

async function requireOwner(req, res) {
  const authHeader = String(req.headers.authorization || "");
  if (!authHeader.startsWith("Bearer ")) {
    fail(res, 401, "Authentifizierung erforderlich.");
    return null;
  }

  try {
    const claims = await getAuth().verifyIdToken(authHeader.slice("Bearer ".length), true);
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

exports.adminSetLicenseExpiry = onRequest({ region: "europe-west1" }, async (req, res) => {
  cors(res);
  if (req.method === "OPTIONS") return res.status(204).send("");
  if (req.method !== "POST") return fail(res, 405, "Method not allowed.");

  const claims = await requireOwner(req, res);
  if (!claims) return;

  try {
    const installationId = String(req.body?.installationId || "").trim();
    const validUntilRaw = String(req.body?.validUntilUtc || "").trim();
    const validUntil = new Date(validUntilRaw);
    const year = validUntil.getUTCFullYear();

    if (!validInstallationId(installationId) || !validUntilRaw || Number.isNaN(validUntil.getTime()) || year < 2000 || year > 2100)
      return fail(res, 400, "Installation und ein gültiges Ablaufdatum sind erforderlich.");

    const ref = db.collection("licenses").doc(installationId);
    if (!(await ref.get()).exists)
      return fail(res, 404, "Registrierung nicht gefunden.");

    await ref.set({
      status: "active",
      validUntil: Timestamp.fromDate(validUntil),
      approvedAt: FieldValue.serverTimestamp(),
      approvedBy: String(claims.email || OWNER_EMAIL),
      expiryChangedAt: FieldValue.serverTimestamp(),
      expiryChangedBy: String(claims.email || OWNER_EMAIL),
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });

    res.json({
      ok: true,
      status: validUntil <= new Date() ? "expired" : "active",
      validUntilUtc: validUntil.toISOString(),
      message: "Ablaufdatum wurde gespeichert."
    });
  } catch (error) {
    console.error(error);
    fail(res, 500, "Ablaufdatum konnte nicht gespeichert werden.");
  }
});
