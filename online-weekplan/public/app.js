const FIREBASE_VERSION = "12.19.0";
const urls = {
  app: `https://www.gstatic.com/firebasejs/${FIREBASE_VERSION}/firebase-app.js`,
  auth: `https://www.gstatic.com/firebasejs/${FIREBASE_VERSION}/firebase-auth.js`,
  firestore: `https://www.gstatic.com/firebasejs/${FIREBASE_VERSION}/firebase-firestore.js`
};

const el = id => document.getElementById(id);
const PIN_STORAGE_KEY = "solutioncompakt.weekplan.pin.v1";
let config, auth, db, role = "", companyId = "", companyCode = "", weekIds = [], weekIndex = 0;
let modules = {};
let sessionVersion = 0, loadVersion = 0;
let pinUnlocked = false;

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

    // A scanned user QR code must always lead to an explicit password prompt,
    // never silently reuse a previously stored browser session/PIN.
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
        applyQrLoginPrefill();
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
  el("reloadButton").addEventListener("click", () => loadWeeks(weekIds[weekIndex]));
  el("prevWeek").addEventListener("click", () => navigateWeek(1));
  el("nextWeek").addEventListener("click", () => navigateWeek(-1));
  el("publishButton").addEventListener("click", publishPackage);
  el("saveOverrideButton").addEventListener("click", saveOverride);
}

async function login() {
  el("loginStatus").textContent = "Anmeldung läuft…";
  const remember = el("rememberLogin").checked;
  if (remember) {
    const pinError = validatePinSetup(el("accessPin").value, el("confirmAccessPin").value);
    if (pinError) {
      el("loginStatus").textContent = pinError;
      return;
    }
  }
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
      body: JSON.stringify({
        companyCode: el("companyCode").value,
        username: el("username").value,
        password: el("password").value
      })
    });
    const payload = await response.json();
    if (!response.ok || !payload.customToken) throw new Error(payload.error || "Anmeldung nicht möglich.");
    const credential = await modules.authMod.signInWithCustomToken(auth, payload.customToken);
    if (remember) await savePinRecord(credential.user.uid, el("accessPin").value);
    el("password").value = "";
    el("accessPin").value = "";
    el("confirmAccessPin").value = "";
    el("loginStatus").textContent = "";
  } catch (error) {
    pinUnlocked = false;
    el("loginStatus").textContent = error.message;
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
    if (!companyId) {
      clearPinRecord();
      await modules.authMod.signOut(auth);
      el("loginStatus").textContent = "Das Benutzerkonto ist keiner Firma zugeordnet.";
      return;
    }
    el("userName").textContent = token.claims.displayName || token.claims.username || user.uid;
    el("roleBadge").textContent = role;
    el("userBox").classList.remove("hidden");
    el("adminPublish").classList.toggle("hidden", role !== "Administrator");
    show("planView");
    await loadWeeks();
  } catch {
    if (session !== sessionVersion) return;
    show("loginView");
    el("loginStatus").textContent = "Anmeldung oder Laden fehlgeschlagen. Bitte erneut anmelden.";
  }
}

function resetPlan() {
  ++loadVersion;
  role = companyId = companyCode = "";
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
    const [entriesSnap, overridesSnap, slotsSnap] = await Promise.all([
      getDocs(collection(db, "companies", tenant, "weekPlans", weekId, "entries")),
      getDocs(collection(db, "companies", tenant, "weekPlans", weekId, "overrides")),
      getDocs(collection(db, "companies", tenant, "weekPlans", weekId, "productionSlots"))
    ]);
    if (request !== loadVersion || tenant !== companyId) return;
    const overrides = new Map(overridesSnap.docs.map(x => [x.id, x.data()]));
    const entries = entriesSnap.docs.map(x => ({ ...x.data(), id: x.id, override: overrides.get(x.id) || null }));
    const slots = slotsSnap.docs.map(x => x.data());
    el("weekTitle").textContent = `KW ${String(meta.isoWeek).padStart(2,"0")} · ${meta.weekStart} – ${meta.weekEnd}`;
    el("publishedMeta").textContent = `${meta.companyName || "SolutionCompakt"}${meta.siteName ? " · " + meta.siteName : ""} · ${companyCode} · veröffentlicht von ${meta.publishedBy || "Admin"}`;
    render(entries, meta.weekStart, slots);
    el("planStatus").textContent = entries.length || slots.length ? "" : "Für diese Woche sind keine Einsätze oder Produktionsschichten geplant.";
  } catch {
    if (request !== loadVersion || tenant !== companyId) return;
    el("planGrid").innerHTML = "";
    el("planStatus").textContent = "Wochenplan konnte nicht geladen werden. Bitte erneut aktualisieren.";
  }
}

function render(entries, weekStart, productionSlots = []) {
  const days = ["Montag","Dienstag","Mittwoch","Donnerstag","Freitag","Samstag","Sonntag"];
  const byDate = new Map();
  for (const entry of entries) {
    if (!byDate.has(entry.date)) byDate.set(entry.date, []);
    byDate.get(entry.date).push(entry);
  }
  // The selected snapshot defines the week, including empty historical weeks.
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
