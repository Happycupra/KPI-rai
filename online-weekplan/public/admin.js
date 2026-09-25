const FIREBASE_VERSION = "12.19.0";
const OWNER_EMAIL = "irajet.ramadani@gmail.com";
const urls = {
  app: `https://www.gstatic.com/firebasejs/${FIREBASE_VERSION}/firebase-app.js`,
  auth: `https://www.gstatic.com/firebasejs/${FIREBASE_VERSION}/firebase-auth.js`
};

const el = id => document.getElementById(id);
let authMod, auth, config;

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
    el("resetButton").addEventListener("click", resetPassword);
    el("logoutButton").addEventListener("click", () => authMod.signOut(auth));
    el("reloadButton").addEventListener("click", loadLicenses);

    authMod.onAuthStateChanged(auth, async user => {
      if (!user) {
        el("adminView").classList.add("hidden");
        el("loginCard").classList.remove("hidden");
        return;
      }
      if ((user.email || "").toLowerCase() !== OWNER_EMAIL) {
        await authMod.signOut(auth);
        el("loginStatus").textContent = "Dieses Konto ist nicht für die Lizenzverwaltung freigeschaltet.";
        return;
      }
      el("loginCard").classList.add("hidden");
      el("adminView").classList.remove("hidden");
      el("adminEmail").textContent = user.email || "";
      await loadLicenses();
    });
  } catch (error) {
    el("loginStatus").textContent = "Admin-Seite konnte nicht initialisiert werden: " + error.message;
  }
}

async function login() {
  setLoginBusy(true);
  el("loginStatus").textContent = "";
  try {
    const email = el("email").value.trim().toLowerCase();
    if (email !== OWNER_EMAIL) throw new Error("Nur die hinterlegte Anbieter-E-Mail ist zugelassen.");
    await authMod.signInWithEmailAndPassword(auth, email, el("password").value);
    el("password").value = "";
  } catch (error) {
    el("loginStatus").textContent = humanAuthError(error);
  } finally {
    setLoginBusy(false);
  }
}

async function resetPassword() {
  setLoginBusy(true);
  try {
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
