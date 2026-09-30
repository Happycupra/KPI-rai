const FIREBASE_VERSION = "12.19.0";
const urls = {
  app: `https://www.gstatic.com/firebasejs/${FIREBASE_VERSION}/firebase-app.js`,
  auth: `https://www.gstatic.com/firebasejs/${FIREBASE_VERSION}/firebase-auth.js`,
  firestore: `https://www.gstatic.com/firebasejs/${FIREBASE_VERSION}/firebase-firestore.js`
};

const el = id => document.getElementById(id);
const PIN_STORAGE_KEY = "solutioncompakt.weekplan.pin.v1";
const COMPANY_CODE_STORAGE_KEY = "solutioncompakt.weekplan.company-code.v1";
let config, auth, db, role = "", companyId = "", companyCode = "", sourceUserId = 0, weekIds = [], weekIndex = 0;
let modules = {};
let sessionVersion = 0, loadVersion = 0;
let pinUnlocked = false;
let loginInProgress = false;
let currentMessages = [];

const PIN_ITERATIONS = 150000;

function qrLoginParameters() {
  try {
    const params = new URLSearchParams(globalThis.location?.search || "");
    if (params.get("login") !== "qr") return null;
    const company = String(params.get("companyCode") || "").trim().toUpperCase();
    const username = String(params.get("username") || "").trim();
    return company || username ? { company, username } : null;
  } catch {
    return null;
  }
}

function applyQrLoginPrefill() {
  const qr = qrLoginParameters();
  if (!qr) return false;
  if (qr.company) el("companyCode").value = qr.company;
  if (qr.username) el("username").value = qr.username;
  el("rememberLogin").checked = false;
  el("pinSetup").classList.add("hidden");
  el("accessPin").value = "";
  el("confirmAccessPin").value = "";
  setTimeout(() => el("password").focus(), 0);
  return true;
}

function companyCodeStorage() {
  try { return globalThis.localStorage || null; } catch { return null; }
}

function rememberedCompanyCode() {
  try {
    return String(companyCodeStorage()?.getItem(COMPANY_CODE_STORAGE_KEY) || "").trim().toUpperCase();
  } catch {
    return "";
  }
}

function rememberCompanyCode(value) {
  const code = String(value || "").trim().toUpperCase();
  if (!/^[A-Z0-9-]{3,24}$/.test(code)) return;
  try { companyCodeStorage()?.setItem(COMPANY_CODE_STORAGE_KEY, code); } catch {}
}

function applyRememberedCompanyCode() {
  const code = rememberedCompanyCode();
  if (!code || el("companyCode").value.trim()) return false;
  el("companyCode").value = code;
  return true;
}

function validatePinSetup(pin, confirmation) {
  if (!/^\d{4}$/.test(String(pin || ""))) return "Der Zugangs-PIN muss genau 4 Ziffern enthalten.";
  if (pin !== confirmation) return "Die beiden PIN-Eingaben stimmen nicht überein.";
  return "";
}
function pinStorage() {
  try { return globalThis.localStorage || null; } catch { return null; }
}
function getPinRecord() {
  try {
    const raw = pinStorage()?.getItem(PIN_STORAGE_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch { return null; }
}
function clearPinRecord() {
  try { pinStorage()?.removeItem(PIN_STORAGE_KEY); } catch {}
}
function toBase64(bytes) {
  let binary = "";
  for (const b of bytes) binary += String.fromCharCode(b);
  return btoa(binary);
}
function fromBase64(value) {
  const binary = atob(value);
  return Uint8Array.from(binary, c => c.charCodeAt(0));
}
async function derivePinHash(pin, salt) {
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(pin), "PBKDF2", false, ["deriveBits"]);
  const bits = await crypto.subtle.deriveBits(
    { name: "PBKDF2", salt, iterations: PIN_ITERATIONS, hash: "SHA-256" },
    key, 256);
  return new Uint8Array(bits);
}
async function savePinRecord(userId, pin) {
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const hash = await derivePinHash(pin, salt);
  pinStorage()?.setItem(PIN_STORAGE_KEY, JSON.stringify({
    userId, salt: toBase64(salt), hash: toBase64(hash), createdAt: new Date().toISOString()
  }));
}
async function verifyPinRecord(userId, pin) {
  if (!/^\d{4}$/.test(String(pin || ""))) return false;
  const record = getPinRecord();
  if (!record || record.userId !== userId || !record.salt || !record.hash) return false;
  try {
    const actual = await derivePinHash(pin, fromBase64(record.salt));
    const expected = fromBase64(record.hash);
    if (actual.length !== expected.length) return false;
    let diff = 0;
    for (let i = 0; i < actual.length; i++) diff |= actual[i] ^ expected[i];
    return diff === 0;
  } catch { return false; }
}

bootstrap();

async function bootstrap() {
  try {
    const response = await fetch("config.json", { cache: "no-store" });
    if (!response.ok) throw new Error("config missing");
    config = await response.json();

    const [appMod, authMod, fsMod] = await Promise.all([
      import(urls.app), import(urls.auth), import(urls.firestore)
    ]);
    modules = { appMod, authMod, fsMod };
    const app = appMod.initializeApp(config.firebase);
    auth = authMod.getAuth(app);
    db = fsMod.getFirestore(app);

    if (qrLoginParameters()) {
      clearPinRecord();
      await authMod.signOut(auth);
      pinUnlocked = false;
    }

    authMod.onAuthStateChanged(auth, async user => {
      const session = ++sessionVersion;
      resetPlan();
      if (!user) {
        pinUnlocked = false;
        show("loginView");
        if (!applyQrLoginPrefill()) applyRememberedCompanyCode();
        return;
      }
      const pinRecord = getPinRecord();
      if (pinRecord?.userId === user.uid && !pinUnlocked) {
        el("pinUserLabel").textContent = "Gespeicherte Anmeldung · PIN erforderlich";
        show("pinView");
        el("unlockPin").focus();
        return;
      }
      await activateAuthenticatedUser(user, session);
    });

    wireEvents();
  } catch {
    show("setupView");
  }
}

function wireEvents() {
  el("loginButton").addEventListener("click", login);
  el("password").addEventListener("keydown", e => { if (e.key === "Enter") login(); });
  el("rememberLogin").addEventListener("change", () => {
    el("pinSetup").classList.toggle("hidden", !el("rememberLogin").checked);
    if (!el("rememberLogin").checked) {
      el("accessPin").value = "";
      el("confirmAccessPin").value = "";
    }
  });
  el("pinUnlockButton").addEventListener("click", unlockWithPin);
  el("unlockPin").addEventListener("keydown", e => { if (e.key === "Enter") unlockWithPin(); });
  el("pinSwitchAccountButton").addEventListener("click", switchAccount);
  el("logoutButton").addEventListener("click", logout);
  el("reloadButton").addEventListener("click", async () => {
    await Promise.all([loadWeeks(weekIds[weekIndex]), refreshMessageBadge()]);
  });
  el("prevWeek").addEventListener("click", () => navigateWeek(1));
  el("nextWeek").addEventListener("click", () => navigateWeek(-1));
  el("publishButton").addEventListener("click", publishPackage);
  el("saveOverrideButton").addEventListener("click", saveOverride);
}

async function login() {
  if (loginInProgress) return;
  const credentials = {
    companyCode: el("companyCode").value.trim(),
    username: el("username").value.trim(),
    password: el("password").value
  };
  for (const [field, label] of [["companyCode", "Firmen-Code"], ["username", "Benutzername"], ["password", "Passwort"]]) {
    if (!credentials[field]) {
      el("loginStatus").textContent = `Bitte ${label} eingeben.`;
      el(field).focus();
      return;
    }
  }
  const remember = el("rememberLogin").checked;
  const pin = el("accessPin").value;
  if (remember) {
    const pinError = validatePinSetup(pin, el("confirmAccessPin").value);
    if (pinError) {
      el("loginStatus").textContent = pinError;
      return;
    }
  }
  loginInProgress = true;
  el("loginButton").disabled = true;
  el("loginStatus").textContent = "Anmeldung läuft…";
  try {
    await modules.authMod.setPersistence(
      auth,
      remember ? modules.authMod.browserLocalPersistence : modules.authMod.browserSessionPersistence
    );
    if (!remember) clearPinRecord();
    pinUnlocked = true;
    const response = await fetch(config.authEndpoint, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(credentials)
    });
    const payload = await response.json();
    if (!response.ok || !payload.customToken) throw new Error(payload.error || "Anmeldung nicht möglich.");
    rememberCompanyCode(credentials.companyCode);
    const credential = await modules.authMod.signInWithCustomToken(auth, payload.customToken);
    if (remember) await savePinRecord(credential.user.uid, pin);
    el("password").value = "";
    el("accessPin").value = "";
    el("confirmAccessPin").value = "";
    el("loginStatus").textContent = "";
  } catch (error) {
    pinUnlocked = false;
    el("loginStatus").textContent = error.message;
  } finally {
    loginInProgress = false;
    el("loginButton").disabled = false;
  }
}

async function unlockWithPin() {
  const user = auth?.currentUser;
  if (!user) return switchAccount();
  el("pinStatus").textContent = "";
  if (!await verifyPinRecord(user.uid, el("unlockPin").value)) {
    el("pinStatus").textContent = "Der Zugangs-PIN ist nicht korrekt.";
    el("unlockPin").value = "";
    el("unlockPin").focus();
    return;
  }
  pinUnlocked = true;
  el("unlockPin").value = "";
  const session = ++sessionVersion;
  resetPlan();
  await activateAuthenticatedUser(user, session);
}

async function switchAccount() {
  clearPinRecord();
  pinUnlocked = false;
  await modules.authMod.signOut(auth);
}

async function logout() {
  clearPinRecord();
  pinUnlocked = false;
  await modules.authMod.signOut(auth);
}

async function activateAuthenticatedUser(user, session) {
  try {
    const token = await user.getIdTokenResult(true);
    if (session !== sessionVersion) return;
    role = token.claims.role || "Beobachter";
    companyId = String(token.claims.companyId || "");
    companyCode = String(token.claims.companyCode || "");
    sourceUserId = Number(token.claims.sourceUserId || 0);
    if (!companyId || !Number.isInteger(sourceUserId) || sourceUserId < 1) {
      clearPinRecord();
      await modules.authMod.signOut(auth);
      el("loginStatus").textContent = "Das Benutzerkonto ist keiner Firma zugeordnet.";
      return;
    }
    rememberCompanyCode(companyCode);
    el("userName").textContent = token.claims.displayName || token.claims.username || user.uid;
    el("roleBadge").textContent = role;
    el("userBox").classList.remove("hidden");
    el("adminPublish").classList.toggle("hidden", role !== "Administrator");
    ensureMessageUi();
    show("planView");
    await Promise.all([loadWeeks(), refreshMessageBadge()]);
  } catch {
    if (session !== sessionVersion) return;
    show("loginView");
    el("loginStatus").textContent = "Anmeldung oder Laden fehlgeschlagen. Bitte erneut anmelden.";
  }
}

function resetPlan() {
  ++loadVersion;
  role = companyId = companyCode = "";
  sourceUserId = 0;
  currentMessages = [];
  weekIds = [];
  weekIndex = 0;
  for (const id of ["planGrid", "publishedMeta", "planStatus", "publishStatus", "userName", "roleBadge", "loginStatus"])
    el(id).textContent = "";
  el("weekTitle").textContent = "Wochenplan";
  el("packageFile").value = "";
  el("password").value = "";
  el("editForm").reset();
  el("editDialog").close();
  el("userBox").classList.add("hidden");
  el("adminPublish").classList.add("hidden");
  if (el("messageBadge")) el("messageBadge").textContent = "";
  if (el("messageDialog")?.open) el("messageDialog").close();
  updateWeekButtons();
}

function updateWeekButtons() {
  el("prevWeek").disabled = weekIndex >= weekIds.length - 1;
  el("nextWeek").disabled = weekIndex <= 0;
}

async function loadWeeks(preferredWeekId) {
  const session = sessionVersion;
  const request = ++loadVersion;
  el("planStatus").textContent = "Wochenpläne werden geladen…";
  el("planGrid").innerHTML = "";
  el("publishedMeta").textContent = "";
  try {
    const { collection, getDocs, orderBy, query, limit } = modules.fsMod;
    const snap = await getDocs(query(collection(db, "companies", companyId, "weekPlans"), orderBy("weekStart", "desc"), limit(20)));
    if (session !== sessionVersion || request !== loadVersion) return;
    weekIds = snap.docs.map(x => x.id);
    weekIndex = Math.max(0, weekIds.indexOf(preferredWeekId));
    updateWeekButtons();
    if (!weekIds.length) {
      el("weekTitle").textContent = "Noch kein Wochenplan veröffentlicht";
      el("planStatus").textContent = "";
      return;
    }
    await loadWeek(weekIds[weekIndex]);
  } catch {
    if (session !== sessionVersion || request !== loadVersion) return;
    weekIds = [];
    weekIndex = 0;
    updateWeekButtons();
    el("weekTitle").textContent = "Wochenplan nicht verfügbar";
    el("planStatus").textContent = "Wochenpläne konnten nicht geladen werden. Bitte Verbindung und Zugriffsrechte prüfen und erneut aktualisieren.";
  }
}

async function navigateWeek(delta) {
  const next = Math.max(0, Math.min(weekIds.length - 1, weekIndex + delta));
  if (next === weekIndex) return;
  weekIndex = next;
  updateWeekButtons();
  await loadWeek(weekIds[weekIndex]);
}

async function loadWeek(weekId) {
  if (!weekId) return;
  const request = ++loadVersion;
  const tenant = companyId;
  el("planGrid").innerHTML = "";
  el("publishedMeta").textContent = "";
  el("weekTitle").textContent = weekId;
  el("planStatus").textContent = "Wochenplan wird geladen…";
  try {
    const { doc, getDoc, collection, getDocs } = modules.fsMod;
    const metaSnap = await getDoc(doc(db, "companies", tenant, "weekPlans", weekId));
    if (!metaSnap.exists()) throw new Error("Wochenplan nicht vorhanden.");
    const meta = metaSnap.data();
    const snapshotPath = ["companies", tenant, "weekPlans", weekId];
    if (meta.activeVersion) snapshotPath.push("versions", meta.activeVersion);
    const [entriesSnap, overridesSnap, slotsSnap] = await Promise.all([
      getDocs(collection(db, ...snapshotPath, "entries")),
      getDocs(collection(db, "companies", tenant, "weekPlans", weekId, "overrides")),
      getDocs(collection(db, ...snapshotPath, "productionSlots"))
    ]);
    if (request !== loadVersion || tenant !== companyId) return;
    const overrides = new Map(overridesSnap.docs.map(x => [x.id, x.data()]));
    const entries = entriesSnap.docs.map(x => ({ ...x.data(), id: x.id, override: overrides.get(x.id) || null }));
    const slots = slotsSnap.docs.map(x => x.data());
    el("weekTitle").textContent = `KW ${String(meta.isoWeek).padStart(2,"0")} · ${meta.weekStart} – ${meta.weekEnd}`;
    el("publishedMeta").textContent = `${meta.companyName || "SolutionCompakt"}${meta.siteName ? " · " + meta.siteName : ""} · ${companyCode} · veröffentlicht: ${publicationTimeText(meta.publishedAt)} · von ${meta.publishedBy || "Admin"}`;
    render(entries, meta.weekStart, slots);
    el("planStatus").textContent = entries.length || slots.length ? "" : "Für diese Woche sind keine Einsätze oder Produktionsschichten geplant.";
  } catch {
    if (request !== loadVersion || tenant !== companyId) return;
    el("planGrid").innerHTML = "";
    el("planStatus").textContent = "Wochenplan konnte nicht geladen werden. Bitte erneut aktualisieren.";
  }
}

function publicationTimeText(value) {
  if (!value) return "Zeitpunkt unbekannt";
  const date = typeof value.toDate === "function" ? value.toDate() : new Date(value);
  if (Number.isNaN(date.getTime())) return "Zeitpunkt unbekannt";
  return date.toLocaleString("de-CH", {
    day: "2-digit", month: "2-digit", year: "numeric",
    hour: "2-digit", minute: "2-digit", second: "2-digit"
  }) + " Uhr";
}

function render(entries, weekStart, productionSlots = []) {
  const days = ["Montag","Dienstag","Mittwoch","Donnerstag","Freitag","Samstag","Sonntag"];
  const byDate = new Map();
  for (const entry of entries) {
    if (!byDate.has(entry.date)) byDate.set(entry.date, []);
    byDate.get(entry.date).push(entry);
  }
  const monday = new Date(weekStart + "T12:00:00");
  if (!/^\d{4}-\d{2}-\d{2}$/.test(weekStart) || Number.isNaN(monday.getTime()))
    throw new Error("Ungültiger Wochenbeginn.");

  el("planGrid").innerHTML = "";
  for (let i=0;i<7;i++) {
    const d = new Date(monday); d.setDate(monday.getDate()+i);
    const key = isoDate(d);
    const col = document.createElement("section");
    col.className = "day";
    col.innerHTML = `<div class="day-title">${days[i]} · ${key.slice(8,10)}.${key.slice(5,7)}.</div>`;
    for (const base of (byDate.get(key) || []).sort((a,b) => String(a.start).localeCompare(String(b.start)))) {
      const v = { ...base, ...(base.override || {}) };
      const card = document.createElement("article");
      card.className = "entry" + (base.override ? " overridden" : "");
      card.innerHTML = `
        <div class="employee">${escapeHtml(v.employeeName || "")}</div>
        <div class="meta">${escapeHtml(v.workstationName || "")} · ${escapeHtml(v.shiftName || "")}</div>
        <div class="time">${escapeHtml(v.start || "")}–${escapeHtml(v.end || "")}</div>
        ${v.note ? `<div class="note">${escapeHtml(v.note)}</div>` : ""}
      `;
      if (role === "Administrator") {
        const btn = document.createElement("button");
        btn.className = "admin-edit";
        btn.textContent = "Online korrigieren";
        btn.addEventListener("click", () => openEdit(base));
        card.appendChild(btn);
      }
      col.appendChild(card);
    }
    for (const slot of productionSlots.filter(x => x.date === key).sort((a, b) => String(a.start).localeCompare(String(b.start)))) {
      const card = document.createElement("article");
      card.className = "entry production-slot";
      card.innerHTML = `<div class="employee">Produktion · ${escapeHtml(slot.orderNumber || "")}</div>
        <div class="meta">${escapeHtml(slot.product || "")}</div>
        <div class="meta">${escapeHtml(slot.workstationName || "")} · ${escapeHtml(slot.shiftName || "")}</div>
        <div class="time">${escapeHtml(slot.start || "")}–${escapeHtml(slot.end || "")}</div>
        <div class="note">Personalbedarf: ${escapeHtml(slot.requiredStaff ?? "—")}</div>`;
      col.appendChild(card);
    }
    el("planGrid").appendChild(col);
  }
}

function openEdit(base) {
  const v = { ...base, ...(base.override || {}) };
  el("editStatus").textContent = "";
  el("editEntryId").value = base.id;
  el("editEmployee").value = v.employeeName || "";
  el("editWorkstation").value = v.workstationName || "";
  el("editShift").value = v.shiftName || "";
  el("editStart").value = v.start || "";
  el("editEnd").value = v.end || "";
  el("editNote").value = v.note || "";
  el("editDialog").showModal();
}

async function saveOverride(event) {
  event.preventDefault();
  if (role !== "Administrator") return;
  const weekId = weekIds[weekIndex];
  const entryId = el("editEntryId").value;
  const { doc, setDoc, serverTimestamp } = modules.fsMod;
  el("saveOverrideButton").disabled = true;
  el("editStatus").textContent = "Wird gespeichert…";
  try {
    await setDoc(doc(db, "companies", companyId, "weekPlans", weekId, "overrides", entryId), {
      employeeName: el("editEmployee").value.trim(),
      workstationName: el("editWorkstation").value.trim(),
      shiftName: el("editShift").value.trim(),
      start: el("editStart").value,
      end: el("editEnd").value,
      note: el("editNote").value.trim(),
      updatedBy: auth.currentUser.uid,
      updatedAt: serverTimestamp()
    });
    el("editDialog").close();
    await loadWeek(weekId);
  } catch {
    el("editStatus").textContent = "Speichern fehlgeschlagen. Deine Eingaben bleiben erhalten. Bitte erneut versuchen.";
  } finally {
    el("saveOverrideButton").disabled = false;
  }
}

async function publishPackage() {
  if (role !== "Administrator") return;
  const file = el("packageFile").files[0];
  if (!file) return el("publishStatus").textContent = "Bitte zuerst eine JSON-Datei auswählen.";
  try {
    const snapshot = JSON.parse(await file.text());
    const token = await auth.currentUser.getIdToken();
    const response = await fetch(config.publishEndpoint, {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": "Bearer " + token },
      body: JSON.stringify(snapshot)
    });
    const payload = await response.json();
    if (!response.ok) throw new Error(payload.error || "Veröffentlichung fehlgeschlagen.");
    el("publishStatus").textContent = `${payload.weekId} veröffentlicht.`;
    await loadWeeks();
  } catch (error) {
    el("publishStatus").textContent = error.message;
  }
}

function ensureMessageUi() {
  if (el("messageDialog")) return;

  const style = document.createElement("style");
  style.textContent = `
    #messagesButton{position:relative}.message-badge{display:inline-flex;min-width:20px;height:20px;padding:0 6px;margin-left:6px;border-radius:10px;align-items:center;justify-content:center;font-size:12px;font-weight:800;background:#c62828;color:#fff}.message-badge:empty{display:none}
    .message-dialog{width:min(960px,94vw);max-height:90vh;border:0;border-radius:18px;padding:0;box-shadow:0 24px 80px rgba(0,0,0,.28)}.message-dialog::backdrop{background:rgba(15,23,42,.52)}
    .message-shell{padding:22px}.message-header{display:flex;align-items:flex-start;justify-content:space-between;gap:16px}.message-header h2{margin:0}.message-compose{display:grid;grid-template-columns:1.3fr 1.7fr .8fr;gap:10px;align-items:end;margin:18px 0}.message-compose label{margin:0}.message-compose textarea{grid-column:1/-1;min-height:90px}.message-send-row{grid-column:1/-1;display:flex;align-items:center;gap:12px;justify-content:flex-end}.message-columns{display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-top:18px}.message-list{display:grid;gap:10px;max-height:360px;overflow:auto}.message-card{border:1px solid rgba(100,116,139,.25);border-radius:12px;padding:12px;background:rgba(248,250,252,.72)}.message-card.unread{border-left:4px solid #2563eb}.message-card-top{display:flex;justify-content:space-between;gap:12px}.message-card-title{font-weight:800}.message-card-meta{font-size:12px;opacity:.72;margin-top:2px}.message-card-body{white-space:pre-wrap;margin-top:9px}.message-card-actions{display:flex;justify-content:flex-end;margin-top:10px}.message-empty{opacity:.65;padding:12px 0}.message-close{flex:0 0 auto}
    @media(max-width:760px){.message-compose,.message-columns{grid-template-columns:1fr}.message-compose textarea,.message-send-row{grid-column:1}.message-list{max-height:none}}
  `;
  document.head.appendChild(style);

  const button = document.createElement("button");
  button.id = "messagesButton";
  button.className = "ghost";
  button.type = "button";
  button.innerHTML = `Nachrichten <span id="messageBadge" class="message-badge"></span>`;
  button.addEventListener("click", openMessageCenter);
  const toolbar = document.querySelector("#planView .toolbar-actions");
  toolbar?.insertBefore(button, el("reloadButton"));

  const dialog = document.createElement("dialog");
  dialog.id = "messageDialog";
  dialog.className = "message-dialog";
  dialog.innerHTML = `
    <div class="message-shell">
      <div class="message-header">
        <div><span class="eyebrow">FIRMENINTERN</span><h2>Nachrichten</h2><p>Nur Benutzer deiner Firma können hier miteinander schreiben.</p></div>
        <button id="messageCloseButton" class="ghost message-close" type="button">Schliessen</button>
      </div>
      <div class="message-compose">
        <label>Empfänger<select id="messageRecipient"></select></label>
        <label>Betreff<input id="messageSubject" maxlength="120" placeholder="Hinweis"></label>
        <label>Priorität<select id="messagePriority"><option>Normal</option><option>Wichtig</option></select></label>
        <label style="grid-column:1/-1">Nachricht<textarea id="messageBody" maxlength="4000" placeholder="Nachricht schreiben…"></textarea></label>
        <div class="message-send-row"><span id="messageStatus" class="status"></span><button id="messageSendButton" class="primary" type="button">Senden</button></div>
      </div>
      <div class="message-columns">
        <section><h3>Posteingang</h3><div id="messageInbox" class="message-list"></div></section>
        <section><h3>Gesendet</h3><div id="messageSent" class="message-list"></div></section>
      </div>
    </div>`;
  document.body.appendChild(dialog);
  el("messageCloseButton").addEventListener("click", () => dialog.close());
  el("messageSendButton").addEventListener("click", sendOnlineMessage);
}

async function apiRequest(endpoint, method = "GET", body = null) {
  if (!endpoint || !auth?.currentUser) throw new Error("Online-Nachrichten sind nicht verfügbar.");
  const token = await auth.currentUser.getIdToken();
  const options = { method, headers: { "Authorization": "Bearer " + token } };
  if (body !== null) {
    options.headers["Content-Type"] = "application/json";
    options.body = JSON.stringify(body);
  }
  const response = await fetch(endpoint, options);
  let payload = {};
  try { payload = await response.json(); } catch {}
  if (!response.ok) throw new Error(payload.error || payload.message || "Online-Anfrage fehlgeschlagen.");
  return payload;
}

async function refreshMessageBadge() {
  if (!sourceUserId || !config?.messageListEndpoint) return;
  try {
    const payload = await apiRequest(config.messageListEndpoint);
    currentMessages = Array.isArray(payload.messages) ? payload.messages : [];
    updateMessageBadge();
  } catch {
    if (el("messageBadge")) el("messageBadge").textContent = "";
  }
}

function updateMessageBadge() {
  const unread = currentMessages.filter(x => Number(x.recipientUserId) === sourceUserId && !x.acknowledgedAtUtc).length;
  if (el("messageBadge")) el("messageBadge").textContent = unread ? (unread > 99 ? "99+" : String(unread)) : "";
}

async function openMessageCenter() {
  ensureMessageUi();
  el("messageDialog").showModal();
  await loadMessageCenter();
}

async function loadMessageCenter() {
  el("messageStatus").textContent = "Nachrichten werden geladen…";
  try {
    const [messagesPayload, recipientsPayload] = await Promise.all([
      apiRequest(config.messageListEndpoint),
      apiRequest(config.messageRecipientsEndpoint)
    ]);
    currentMessages = Array.isArray(messagesPayload.messages) ? messagesPayload.messages : [];
    const recipients = Array.isArray(recipientsPayload.recipients) ? recipientsPayload.recipients : [];
    const select = el("messageRecipient");
    const selected = select.value;
    select.innerHTML = "";
    for (const recipient of recipients) {
      const option = document.createElement("option");
      option.value = String(recipient.id);
      option.textContent = recipient.displayName && recipient.displayName !== recipient.username
        ? `${recipient.displayName} (${recipient.username})`
        : recipient.username;
      select.appendChild(option);
    }
    if (recipients.some(x => String(x.id) === selected)) select.value = selected;
    renderOnlineMessages();
    updateMessageBadge();
    el("messageStatus").textContent = recipients.length ? "" : "Keine weiteren aktiven Benutzer in dieser Firma vorhanden.";
  } catch (error) {
    el("messageStatus").textContent = error.message;
  }
}

function renderOnlineMessages() {
  const inbox = currentMessages
    .filter(x => Number(x.recipientUserId) === sourceUserId)
    .sort((a,b) => new Date(b.createdAtUtc || 0) - new Date(a.createdAtUtc || 0));
  const sent = currentMessages
    .filter(x => Number(x.senderUserId) === sourceUserId)
    .sort((a,b) => new Date(b.createdAtUtc || 0) - new Date(a.createdAtUtc || 0));
  renderMessageList(el("messageInbox"), inbox, false);
  renderMessageList(el("messageSent"), sent, true);
}

function renderMessageList(container, messages, sent) {
  container.innerHTML = "";
  if (!messages.length) {
    const empty = document.createElement("div");
    empty.className = "message-empty";
    empty.textContent = sent ? "Noch keine Nachrichten gesendet." : "Keine Nachrichten im Posteingang.";
    container.appendChild(empty);
    return;
  }
  for (const message of messages) {
    const card = document.createElement("article");
    card.className = "message-card" + (!sent && !message.acknowledgedAtUtc ? " unread" : "");
    const top = document.createElement("div");
    top.className = "message-card-top";
    const left = document.createElement("div");
    const title = document.createElement("div");
    title.className = "message-card-title";
    title.textContent = message.subject || "Hinweis";
    const meta = document.createElement("div");
    meta.className = "message-card-meta";
    const partner = sent ? message.recipientDisplayNameSnapshot : message.senderDisplayNameSnapshot;
    const state = sent
      ? (message.acknowledgedAtUtc ? " · gelesen " + messageTime(message.acknowledgedAtUtc) : " · noch nicht bestätigt")
      : (message.acknowledgedAtUtc ? " · gelesen bestätigt" : " · ungelesen");
    meta.textContent = `${sent ? "An" : "Von"} ${partner || "Benutzer"} · ${messageTime(message.createdAtUtc)} · ${message.priority || "Normal"}${state}`;
    left.append(title, meta);
    top.appendChild(left);
    const body = document.createElement("div");
    body.className = "message-card-body";
    body.textContent = message.body || "";
    card.append(top, body);
    if (!sent && !message.acknowledgedAtUtc) {
      const actions = document.createElement("div");
      actions.className = "message-card-actions";
      const ack = document.createElement("button");
      ack.type = "button";
      ack.className = "ghost";
      ack.textContent = "Gelesen bestätigen";
      ack.addEventListener("click", () => acknowledgeOnlineMessage(message.onlineMessageId));
      actions.appendChild(ack);
      card.appendChild(actions);
    }
    container.appendChild(card);
  }
}

async function sendOnlineMessage() {
  const recipientUserId = Number(el("messageRecipient").value);
  const body = el("messageBody").value.trim();
  if (!Number.isInteger(recipientUserId) || recipientUserId < 1) {
    el("messageStatus").textContent = "Bitte einen Empfänger auswählen.";
    return;
  }
  if (!body) {
    el("messageStatus").textContent = "Bitte eine Nachricht eingeben.";
    return;
  }
  el("messageSendButton").disabled = true;
  el("messageStatus").textContent = "Wird gesendet…";
  try {
    await apiRequest(config.messageSendEndpoint, "POST", {
      recipientUserId,
      subject: el("messageSubject").value.trim() || "Hinweis",
      body,
      priority: el("messagePriority").value
    });
    el("messageSubject").value = "";
    el("messageBody").value = "";
    el("messagePriority").value = "Normal";
    await loadMessageCenter();
    el("messageStatus").textContent = "Nachricht gesendet.";
  } catch (error) {
    el("messageStatus").textContent = error.message;
  } finally {
    el("messageSendButton").disabled = false;
  }
}

async function acknowledgeOnlineMessage(messageId) {
  el("messageStatus").textContent = "Lesebestätigung wird gespeichert…";
  try {
    await apiRequest(config.messageAcknowledgeEndpoint, "POST", { messageId });
    await loadMessageCenter();
    el("messageStatus").textContent = "Als gelesen bestätigt.";
  } catch (error) {
    el("messageStatus").textContent = error.message;
  }
}

function messageTime(value) {
  if (!value) return "Zeit unbekannt";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "Zeit unbekannt";
  return date.toLocaleString("de-CH", { day:"2-digit", month:"2-digit", year:"numeric", hour:"2-digit", minute:"2-digit" });
}

function show(id) {
  for (const name of ["setupView","loginView","pinView","planView"]) el(name).classList.add("hidden");
  el(id).classList.remove("hidden");
}

function isoDate(date) {
  return `${date.getFullYear()}-${String(date.getMonth()+1).padStart(2,"0")}-${String(date.getDate()).padStart(2,"0")}`;
}

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, c => ({ "&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#039;" }[c]));
}
