const { onRequest } = require("firebase-functions/v2/https");
const { getAuth } = require("firebase-admin/auth");
const { getFirestore, FieldValue, Timestamp } = require("firebase-admin/firestore");
const crypto = require("crypto");

const db = getFirestore();

function cors(res) {
  res.set("Access-Control-Allow-Origin", "*");
  res.set("Access-Control-Allow-Headers", "Content-Type, Authorization");
  res.set("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
}

function fail(res, status, message) {
  res.status(status).json({ error: message, message });
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

function validCompanyId(value) {
  return /^[A-Za-z0-9_-]{16,100}$/.test(String(value || ""));
}

function toIso(value) {
  if (!value) return null;
  if (typeof value.toDate === "function") return value.toDate().toISOString();
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

function parseDate(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return null;
  if (date.getTime() > Date.now() + 24 * 60 * 60 * 1000) return null;
  if (date.getUTCFullYear() < 2020) return null;
  return date;
}

async function activeCompany(companyRef) {
  const company = await companyRef.get();
  if (!company.exists || company.data().isActive === false) return null;
  const data = company.data();
  const installationId = String(data.licenseInstallationId || "");
  if (!validInstallationId(installationId)) return null;
  const license = await db.collection("licenses").doc(installationId).get();
  const value = license.exists ? license.data() : {};
  const until = value.validUntil?.toDate?.();
  return value.status === "active" && until && until > new Date() ? data : null;
}

async function requireCompanyUser(req, res) {
  const header = String(req.headers.authorization || "");
  if (!header.startsWith("Bearer ")) {
    fail(res, 401, "Authentifizierung erforderlich.");
    return null;
  }

  let claims;
  try {
    claims = await getAuth().verifyIdToken(header.slice(7), true);
  } catch {
    fail(res, 401, "Bitte erneut anmelden.");
    return null;
  }

  const companyId = String(claims.companyId || "");
  const sourceUserId = Number(claims.sourceUserId);
  if (!validCompanyId(companyId) || !Number.isInteger(sourceUserId) || sourceUserId < 1) {
    fail(res, 403, "Das Benutzerkonto ist keiner gültigen Firma zugeordnet.");
    return null;
  }

  const companyRef = db.collection("companies").doc(companyId);
  if (!await activeCompany(companyRef)) {
    fail(res, 403, "Online-Zugang ist nicht freigeschaltet.");
    return null;
  }

  const userSnap = await companyRef.collection("authUsers").doc(String(sourceUserId)).get();
  const user = userSnap.exists ? userSnap.data() : {};
  if (user.isActive !== true ||
      (user.credentialVersion || "legacy") !== (claims.credentialVersion || "legacy")) {
    fail(res, 403, "Berechtigung geändert. Bitte erneut anmelden.");
    return null;
  }

  return { claims, companyId, companyRef, sourceUserId, user };
}

function messageJson(doc) {
  const data = doc.data();
  return {
    onlineMessageId: doc.id,
    companyId: String(data.companyId || ""),
    sourceMessageId: Number.isInteger(data.sourceMessageId) ? data.sourceMessageId : null,
    sourceInstallationId: String(data.sourceInstallationId || ""),
    senderUserId: Number(data.senderUserId),
    senderUsernameSnapshot: String(data.senderUsernameSnapshot || ""),
    senderDisplayNameSnapshot: String(data.senderDisplayNameSnapshot || ""),
    recipientUserId: Number(data.recipientUserId),
    recipientUsernameSnapshot: String(data.recipientUsernameSnapshot || ""),
    recipientDisplayNameSnapshot: String(data.recipientDisplayNameSnapshot || ""),
    subject: String(data.subject || "Hinweis"),
    body: String(data.body || ""),
    priority: data.priority === "Wichtig" ? "Wichtig" : "Normal",
    createdAtUtc: toIso(data.createdAt),
    acknowledgedAtUtc: toIso(data.acknowledgedAt),
    acknowledgedByUsername: data.acknowledgedByUsername ? String(data.acknowledgedByUsername) : null
  };
}

async function recentMessages(companyRef) {
  const snap = await companyRef.collection("messages")
    .orderBy("createdAt", "desc")
    .limit(500)
    .get();
  return snap.docs.map(messageJson);
}

exports.desktopMessagesSync = onRequest({ region: "europe-west1", timeoutSeconds: 60 }, async (req, res) => {
  cors(res);
  if (req.method === "OPTIONS") return res.status(204).send("");
  if (req.method !== "POST") return fail(res, 405, "Method not allowed.");

  try {
    const installationId = String(req.body?.installationId || "").trim();
    const secret = String(req.body?.secret || "").trim();
    const companyId = String(req.body?.companyId || "").trim();
    const messages = Array.isArray(req.body?.messages) ? req.body.messages : [];

    if (!validInstallationId(installationId) || !validSecret(secret) || !validCompanyId(companyId))
      return fail(res, 400, "Ungültige Installations- oder Firmendaten.");
    if (messages.length > 500)
      return fail(res, 400, "Es können höchstens 500 Nachrichten pro Abgleich verarbeitet werden.");

    const licenseRef = db.collection("licenses").doc(installationId);
    const licenseSnap = await licenseRef.get();
    if (!licenseSnap.exists || licenseSnap.data().secretHash !== sha256(secret))
      return fail(res, 401, "Diese Installation konnte nicht bestätigt werden.");

    const license = licenseSnap.data();
    const validUntil = license.validUntil?.toDate?.() || null;
    if (String(license.status || "").toLowerCase() !== "active" || !validUntil || validUntil <= new Date())
      return fail(res, 403, "Die Lizenz ist für den Online-Zugang nicht aktiv.");
    if (license.companyId && String(license.companyId) !== companyId)
      return fail(res, 403, "Die Installation gehört zu einer anderen Firma.");

    const companyRef = db.collection("companies").doc(companyId);
    const companySnap = await companyRef.get();
    if (!companySnap.exists || String(companySnap.data().licenseInstallationId || "") !== installationId)
      return fail(res, 403, "Die Firma ist dieser Installation nicht zugeordnet.");
    if (!await activeCompany(companyRef))
      return fail(res, 403, "Online-Zugang ist nicht freigeschaltet.");

    const usersSnap = await companyRef.collection("authUsers").get();
    const users = new Map(usersSnap.docs.map(doc => [Number(doc.id), doc.data()]));

    let batch = db.batch();
    let operations = 0;
    for (const raw of messages) {
      const localMessageId = Number(raw?.localMessageId);
      const senderUserId = Number(raw?.senderUserId);
      const recipientUserId = Number(raw?.recipientUserId);
      const requestedCompanyId = String(raw?.companyId || "");
      const subject = String(raw?.subject || "Hinweis").trim() || "Hinweis";
      const body = String(raw?.body || "").trim();
      const priority = raw?.priority === "Wichtig" ? "Wichtig" : "Normal";
      const createdAt = parseDate(raw?.createdAtUtc);
      const sender = users.get(senderUserId);
      const recipient = users.get(recipientUserId);

      if (!Number.isInteger(localMessageId) || localMessageId < 1 ||
          requestedCompanyId !== companyId ||
          !Number.isInteger(senderUserId) || senderUserId < 1 ||
          !Number.isInteger(recipientUserId) || recipientUserId < 1 ||
          senderUserId === recipientUserId || !sender || !recipient ||
          subject.length > 120 || body.length < 1 || body.length > 4000 || !createdAt)
        return fail(res, 400, "Ungültige Nachrichtendaten.");

      const suppliedOnlineId = String(raw?.onlineMessageId || "").trim();
      if (suppliedOnlineId && !/^[A-Za-z0-9_-]{1,200}$/.test(suppliedOnlineId))
        return fail(res, 400, "Ungültige Online-Nachrichtenkennung.");

      const onlineMessageId = suppliedOnlineId || `desktop-${sha256(installationId).slice(0, 16)}-${localMessageId}`;
      const ref = companyRef.collection("messages").doc(onlineMessageId);
      const data = {
        companyId,
        senderUserId,
        senderUsernameSnapshot: String(sender.username || raw.senderUsernameSnapshot || ""),
        senderDisplayNameSnapshot: String(sender.displayName || sender.username || raw.senderDisplayNameSnapshot || ""),
        recipientUserId,
        recipientUsernameSnapshot: String(recipient.username || raw.recipientUsernameSnapshot || ""),
        recipientDisplayNameSnapshot: String(recipient.displayName || recipient.username || raw.recipientDisplayNameSnapshot || ""),
        participantUserIds: [senderUserId, recipientUserId],
        subject,
        body,
        priority,
        createdAt: Timestamp.fromDate(createdAt),
        updatedAt: FieldValue.serverTimestamp()
      };

      if (!suppliedOnlineId) {
        data.sourceInstallationId = installationId;
        data.sourceMessageId = localMessageId;
      }

      const acknowledgedAt = raw?.acknowledgedAtUtc ? parseDate(raw.acknowledgedAtUtc) : null;
      if (acknowledgedAt) {
        data.acknowledgedAt = Timestamp.fromDate(acknowledgedAt);
        data.acknowledgedByUsername = String(recipient.username || raw.acknowledgedByUsername || "");
      }

      batch.set(ref, data, { merge: true });
      operations++;
      if (operations >= 400) {
        await batch.commit();
        batch = db.batch();
        operations = 0;
      }
    }
    if (operations > 0) await batch.commit();

    const cloudMessages = await recentMessages(companyRef);
    res.json({ ok: true, message: `${cloudMessages.length} Nachrichten abgeglichen.`, messages: cloudMessages });
  } catch (error) {
    console.error(error);
    fail(res, 500, "Nachrichten konnten nicht synchronisiert werden.");
  }
});

exports.messageRecipients = onRequest({ region: "europe-west1" }, async (req, res) => {
  cors(res);
  if (req.method === "OPTIONS") return res.status(204).send("");
  if (req.method !== "GET") return fail(res, 405, "Method not allowed.");

  try {
    const access = await requireCompanyUser(req, res);
    if (!access) return;
    const snap = await access.companyRef.collection("authUsers").get();
    const recipients = snap.docs
      .map(doc => ({ id: Number(doc.id), ...doc.data() }))
      .filter(x => Number.isInteger(x.id) && x.id !== access.sourceUserId && x.isActive === true)
      .map(x => ({
        id: x.id,
        username: String(x.username || ""),
        displayName: String(x.displayName || x.username || "")
      }))
      .sort((a, b) => a.displayName.localeCompare(b.displayName, "de"));
    res.json({ ok: true, recipients });
  } catch (error) {
    console.error(error);
    fail(res, 500, "Empfänger konnten nicht geladen werden.");
  }
});

exports.messageList = onRequest({ region: "europe-west1" }, async (req, res) => {
  cors(res);
  if (req.method === "OPTIONS") return res.status(204).send("");
  if (req.method !== "GET") return fail(res, 405, "Method not allowed.");

  try {
    const access = await requireCompanyUser(req, res);
    if (!access) return;
    const messages = (await recentMessages(access.companyRef))
      .filter(x => x.senderUserId === access.sourceUserId || x.recipientUserId === access.sourceUserId);
    res.json({ ok: true, messages });
  } catch (error) {
    console.error(error);
    fail(res, 500, "Nachrichten konnten nicht geladen werden.");
  }
});

exports.messageSend = onRequest({ region: "europe-west1" }, async (req, res) => {
  cors(res);
  if (req.method === "OPTIONS") return res.status(204).send("");
  if (req.method !== "POST") return fail(res, 405, "Method not allowed.");

  try {
    const access = await requireCompanyUser(req, res);
    if (!access) return;

    const recipientUserId = Number(req.body?.recipientUserId);
    const subject = String(req.body?.subject || "Hinweis").trim() || "Hinweis";
    const body = String(req.body?.body || "").trim();
    const priority = req.body?.priority === "Wichtig" ? "Wichtig" : "Normal";
    if (!Number.isInteger(recipientUserId) || recipientUserId < 1 || recipientUserId === access.sourceUserId ||
        subject.length > 120 || body.length < 1 || body.length > 4000)
      return fail(res, 400, "Ungültige Nachrichtendaten.");

    const recipientSnap = await access.companyRef.collection("authUsers").doc(String(recipientUserId)).get();
    const recipient = recipientSnap.exists ? recipientSnap.data() : {};
    if (recipient.isActive !== true)
      return fail(res, 404, "Der ausgewählte Empfänger ist nicht mehr aktiv.");

    const ref = access.companyRef.collection("messages").doc();
    const now = Timestamp.now();
    await ref.set({
      companyId: access.companyId,
      senderUserId: access.sourceUserId,
      senderUsernameSnapshot: String(access.user.username || access.claims.username || ""),
      senderDisplayNameSnapshot: String(access.user.displayName || access.user.username || access.claims.displayName || ""),
      recipientUserId,
      recipientUsernameSnapshot: String(recipient.username || ""),
      recipientDisplayNameSnapshot: String(recipient.displayName || recipient.username || ""),
      participantUserIds: [access.sourceUserId, recipientUserId],
      subject,
      body,
      priority,
      createdAt: now,
      acknowledgedAt: null,
      acknowledgedByUsername: null,
      sourceInstallationId: "",
      sourceMessageId: null,
      updatedAt: FieldValue.serverTimestamp()
    });

    res.json({ ok: true, message: messageJson(await ref.get()) });
  } catch (error) {
    console.error(error);
    fail(res, 500, "Nachricht konnte nicht gesendet werden.");
  }
});

exports.messageAcknowledge = onRequest({ region: "europe-west1" }, async (req, res) => {
  cors(res);
  if (req.method === "OPTIONS") return res.status(204).send("");
  if (req.method !== "POST") return fail(res, 405, "Method not allowed.");

  try {
    const access = await requireCompanyUser(req, res);
    if (!access) return;
    const messageId = String(req.body?.messageId || "").trim();
    if (!/^[A-Za-z0-9_-]{1,200}$/.test(messageId))
      return fail(res, 400, "Ungültige Nachrichtenkennung.");

    const ref = access.companyRef.collection("messages").doc(messageId);
    await db.runTransaction(async tx => {
      const snap = await tx.get(ref);
      if (!snap.exists) throw new Error("NOT_FOUND");
      const data = snap.data();
      if (String(data.companyId || "") !== access.companyId || Number(data.recipientUserId) !== access.sourceUserId)
        throw new Error("FORBIDDEN");
      if (!data.acknowledgedAt) {
        tx.update(ref, {
          acknowledgedAt: FieldValue.serverTimestamp(),
          acknowledgedByUsername: String(access.user.username || access.claims.username || ""),
          updatedAt: FieldValue.serverTimestamp()
        });
      }
    });

    res.json({ ok: true });
  } catch (error) {
    if (error.message === "NOT_FOUND") return fail(res, 404, "Nachricht nicht gefunden.");
    if (error.message === "FORBIDDEN") return fail(res, 403, "Diese Nachricht gehört nicht zu deinem Posteingang.");
    console.error(error);
    fail(res, 500, "Lesebestätigung konnte nicht gespeichert werden.");
  }
});
