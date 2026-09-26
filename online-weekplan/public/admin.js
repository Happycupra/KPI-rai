const FIREBASE_VERSION = "12.19.0";
const OWNER_EMAIL = "irajet.ramadani@gmail.com";
const urls = {
  app: `https://www.gstatic.com/firebasejs/${FIREBASE_VERSION}/firebase-app.js`,
  auth: `https://www.gstatic.com/firebasejs/${FIREBASE_VERSION}/firebase-auth.js`
};

const el = id => document.getElementById(id);
const PIN_STORAGE_KEY = "solutioncompakt.admin.pin.v1";
let authMod, auth, config;
let pinUnlocked = false;

const PIN_ITERATIONS = 150000;
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
    config = await (await fetch("config.json", { cache: "no-store" })).json();
    const [appMod, loadedAuth] = await Promise.all([import(urls.app), import(urls.auth)]);
    authMod = loadedAuth;
    auth = authMod.getAuth(appMod.initializeApp(config.firebase));
    auth.languageCode = "de";

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
    el("resetButton").addEventListener("click", resetPassword);
    el("logoutButton").addEventListener("click", logout);
    el("reloadButton").addEventListener("click", loadLicenses);

    authMod.onAuthStateChanged(auth, async user => {
      el("adminView").classList.add("hidden");
      el("loginCard").classList.add("hidden");
      el("pinCard").classList.add("hidden");
      if (!user) {
        pinUnlocked = false;
        el("loginCard").classList.remove("hidden");
        return;
      }
      if ((user.email || "").toLowerCase() !== OWNER_EMAIL) {
        clearPinRecord();
        await authMod.signOut(auth);
        el("loginStatus").textContent = "Dieses Konto ist nicht für die Lizenzverwaltung freigeschaltet.";
        return;
      }
      const record = getPinRecord();
      if (record?.userId === user.uid && !pinUnlocked) {
        el("pinUserLabel").textContent = user.email || "Gespeicherte Anmeldung";
        el("pinCard").classList.remove("hidden");
        el("unlockPin").focus();
        return;
      }
      await showAdmin(user);
    });
  } catch (error) {
    el("loginStatus").textContent = "Admin-Seite konnte nicht initialisiert werden: " + error.message;
  }
}

async function login() {
  setLoginBusy(true);
  el("loginStatus").textContent = "";
  const remember = el("rememberLogin").checked;
  if (remember) {
    const pinError = validatePinSetup(el("accessPin").value, el("confirmAccessPin").value);
    if (pinError) {
      el("loginStatus").textContent = pinError;
      setLoginBusy(false);
      return;
    }
  }
  try {
    const email = el("email").value.trim().toLowerCase();
    if (email !== OWNER_EMAIL) throw new Error("Nur die hinterlegte Anbieter-E-Mail ist zugelassen.");
    await authMod.setPersistence(
      auth,
      remember ? authMod.browserLocalPersistence : authMod.browserSessionPersistence
    );
    if (!remember) clearPinRecord();
    pinUnlocked = true;
    const credential = await authMod.signInWithEmailAndPassword(auth, email, el("password").value);
    if (remember) await savePinRecord(credential.user.uid, el("accessPin").value);
    el("password").value = "";
    el("accessPin").value = "";
    el("confirmAccessPin").value = "";
  } catch (error) {
    pinUnlocked = false;
    el("loginStatus").textContent = humanAuthError(error);
  } finally {
    setLoginBusy(false);
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
  el("pinCard").classList.add("hidden");
  await showAdmin(user);
}

async function switchAccount() {
  clearPinRecord();
  pinUnlocked = false;
  await authMod.signOut(auth);
}

async function logout() {
  clearPinRecord();
  pinUnlocked = false;
  await authMod.signOut(auth);
}

async function showAdmin(user) {
  el("loginCard").classList.add("hidden");
  el("pinCard").classList.add("hidden");
  el("adminView").classList.remove("hidden");
  el("adminEmail").textContent = user.email || "";
  await loadLicenses();
}

async function resetPassword() {
  setLoginBusy(true);
  try {
    clearPinRecord();
    pinUnlocked = false;
    el("email").value = OWNER_EMAIL;
    await authMod.sendPasswordResetEmail(auth, OWNER_EMAIL);
    el("loginStatus").style.color = "#166534";
    el("loginStatus").textContent = "E-Mail zum Festlegen/Zurücksetzen des Passworts wurde gesendet.";
  } catch (error) {
    el("loginStatus").style.color = "#b42318";
    el("loginStatus").textContent = humanAuthError(error);
  } finally {
    setLoginBusy(false);
  }
}

async function loadLicenses() {
  el("adminStatus").textContent = "Registrierungen werden geladen…";
  try {
    const data = await adminCall(config.adminLicensesEndpoint, {});
    const licenses = Array.isArray(data.licenses) ? data.licenses : [];
    licenses.sort((a,b) => String(b.requestedAtUtc || "").localeCompare(String(a.requestedAtUtc || "")));
    el("countLabel").textContent = `${licenses.length} Registrierung(en)`;
    el("licenseRows").innerHTML = "";
    for (const item of licenses) el("licenseRows").appendChild(renderRow(item));
    el("adminStatus").textContent = licenses.length ? "" : "Noch keine Registrierungsanfragen vorhanden.";
  } catch (error) {
    el("adminStatus").textContent = error.message;
  }
}

function renderRow(item) {
  const tr = document.createElement("tr");

  const company = document.createElement("td");
  company.innerHTML = `<strong>${escapeHtml(item.companyName || "—")}</strong><br><span class="muted">${escapeHtml(item.contactEmail || "—")}</span><br><span class="muted">Anfrage: ${formatDateTime(item.requestedAtUtc)}</span>`;
  tr.appendChild(company);

  const status = document.createElement("td");
  const effectiveStatus = item.status === "active" && item.validUntilUtc && new Date(item.validUntilUtc) <= new Date() ? "expired" : (item.status || "pending");
  status.innerHTML = `<span class="pill ${escapeHtml(effectiveStatus)}">${escapeHtml(effectiveStatus)}</span>`;
  tr.appendChild(status);

  const valid = document.createElement("td");
  valid.textContent = item.validUntilUtc ? new Date(item.validUntilUtc).toLocaleString("de-CH") : "—";
  tr.appendChild(valid);

  const install = document.createElement("td");
  install.innerHTML = `<span class="muted">${escapeHtml(item.installationId || "")}</span><br><span class="muted">Version ${escapeHtml(item.lastAppVersion || "—")}</span>`;
  tr.appendChild(install);

  const support = document.createElement("td");
  support.className = "support-note";
  if (item.hasRecoveryCode) {
    const info = document.createElement("div");
    info.className = "muted";
    info.textContent = item.recoveryCodeUpdatedAtUtc
      ? "Hinterlegt: " + formatDateTime(item.recoveryCodeUpdatedAtUtc)
      : "Recovery-Code hinterlegt";
    support.appendChild(info);

    const show = document.createElement("button");
    show.textContent = "Recovery-Code anzeigen";
    show.style.marginTop = "6px";
    const codeBox = document.createElement("div");
    codeBox.className = "recovery-code hidden";
    const copy = document.createElement("button");
    copy.textContent = "Kopieren";
    copy.className = "hidden";
    copy.style.marginTop = "6px";

    show.addEventListener("click", async () => {
      if (!codeBox.classList.contains("hidden")) {
        codeBox.classList.add("hidden");
        copy.classList.add("hidden");
        show.textContent = "Recovery-Code anzeigen";
        return;
      }

      show.disabled = true;
      try {
        if (!codeBox.textContent) {
          const data = await adminCall(config.adminGetRecoveryCodeEndpoint, { installationId: item.installationId });
          codeBox.textContent = data.recoveryCode || "";
        }
        codeBox.classList.remove("hidden");
        copy.classList.remove("hidden");
        show.textContent = "Recovery-Code ausblenden";
      } catch (error) {
        el("adminStatus").textContent = error.message;
      } finally {
        show.disabled = false;
      }
    });

    copy.addEventListener("click", async () => {
      try {
        await navigator.clipboard.writeText(codeBox.textContent || "");
        el("adminStatus").style.color = "#166534";
        el("adminStatus").textContent = "Recovery-Code wurde in die Zwischenablage kopiert.";
      } catch {
        el("adminStatus").style.color = "#b42318";
        el("adminStatus").textContent = "Recovery-Code konnte nicht automatisch kopiert werden.";
      }
    });

    support.appendChild(show);
    support.appendChild(document.createElement("br"));
    support.appendChild(codeBox);
    support.appendChild(document.createElement("br"));
    support.appendChild(copy);
  } else {
    support.innerHTML = '<span class="muted">Noch nicht synchronisiert.</span>';
  }
  tr.appendChild(support);

  const actions = document.createElement("td");
  actions.className = "actions";
  for (const days of [7,30,90,365]) {
    const button = document.createElement("button");
    button.textContent = `+${days} T`;
    button.addEventListener("click", () => extend(item.installationId, days));
    actions.appendChild(button);
  }
  const custom = document.createElement("input");
  custom.type = "number"; custom.min = "1"; custom.max = "3650"; custom.placeholder = "Tage"; custom.className = "custom";
  actions.appendChild(custom);
  const customButton = document.createElement("button");
  customButton.textContent = "+ Tage";
  customButton.addEventListener("click", () => {
    const days = Number(custom.value);
    if (!Number.isInteger(days) || days < 1 || days > 3650) return alert("Bitte 1 bis 3650 Tage eingeben.");
    extend(item.installationId, days);
  });
  actions.appendChild(customButton);
  const suspend = document.createElement("button");
  suspend.textContent = "Sperren"; suspend.className = "danger";
  suspend.addEventListener("click", () => setStatus(item.installationId, "suspended"));
  actions.appendChild(suspend);
  tr.appendChild(actions);
  return tr;
}

async function extend(installationId, days) {
  el("adminStatus").textContent = `Freischaltung +${days} Tage wird gespeichert…`;
  try {
    await adminCall(config.adminExtendLicenseEndpoint, { installationId, days });
    await loadLicenses();
  } catch (error) { el("adminStatus").textContent = error.message; }
}

async function setStatus(installationId, status) {
  if (status === "suspended" && !confirm("Diese Installation wirklich sperren?")) return;
  el("adminStatus").textContent = "Status wird gespeichert…";
  try {
    await adminCall(config.adminSetLicenseStatusEndpoint, { installationId, status });
    await loadLicenses();
  } catch (error) { el("adminStatus").textContent = error.message; }
}

async function adminCall(endpoint, body) {
  const user = auth.currentUser;
  if (!user) throw new Error("Nicht angemeldet.");
  const token = await user.getIdToken(true);
  const response = await fetch(endpoint, {
    method: "POST",
    headers: { "Content-Type": "application/json", "Authorization": "Bearer " + token },
    body: JSON.stringify(body || {})
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(payload.error || "Admin-Anfrage fehlgeschlagen.");
  return payload;
}

function setLoginBusy(busy) {
  el("loginButton").disabled = busy;
  el("resetButton").disabled = busy;
}
function humanAuthError(error) {
  const code = String(error?.code || "");
  if (code.includes("invalid-credential") || code.includes("wrong-password")) return "E-Mail oder Passwort ist nicht korrekt.";
  if (code.includes("too-many-requests")) return "Zu viele Versuche. Bitte später erneut versuchen.";
  if (code.includes("operation-not-allowed")) return "E-Mail/Passwort-Anmeldung ist im Firebase-Projekt noch nicht aktiviert.";
  return error?.message || "Anmeldung fehlgeschlagen.";
}
function formatDateTime(value) { return value ? new Date(value).toLocaleString("de-CH") : "—"; }
function escapeHtml(value) { return String(value ?? "").replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); }
