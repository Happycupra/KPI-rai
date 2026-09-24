const FIREBASE_VERSION = "12.19.0";
const urls = {
  app: `https://www.gstatic.com/firebasejs/${FIREBASE_VERSION}/firebase-app.js`,
  auth: `https://www.gstatic.com/firebasejs/${FIREBASE_VERSION}/firebase-auth.js`,
  firestore: `https://www.gstatic.com/firebasejs/${FIREBASE_VERSION}/firebase-firestore.js`
};

const el = id => document.getElementById(id);
let config, auth, db, role = "", companyId = "", companyCode = "", weekIds = [], weekIndex = 0;
let modules = {};
let sessionVersion = 0, loadVersion = 0;

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

    authMod.onAuthStateChanged(auth, async user => {
      const session = ++sessionVersion;
      resetPlan();
      if (!user) {
        show("loginView");
        return;
      }
      try {
        const token = await user.getIdTokenResult(true);
        if (session !== sessionVersion) return;
        role = token.claims.role || "Beobachter";
        companyId = String(token.claims.companyId || "");
        companyCode = String(token.claims.companyCode || "");
        if (!companyId) {
          await authMod.signOut(auth);
          el("loginStatus").textContent = "Das Benutzerkonto ist keiner Firma zugeordnet.";
          return;
        }
        el("userName").textContent = token.claims.displayName || token.claims.username || user.uid;
        el("roleBadge").textContent = role;
        el("userBox").classList.remove("hidden");
        el("adminPublish").classList.toggle("hidden", role !== "Administrator");
        show("planView");
        await loadWeeks();
      } catch (error) {
        if (session !== sessionVersion) return;
        show("loginView");
        el("loginStatus").textContent = "Anmeldung oder Laden fehlgeschlagen. Bitte erneut anmelden.";
      }
    });

    wireEvents();
  } catch {
    show("setupView");
  }
}

function wireEvents() {
  el("loginButton").addEventListener("click", login);
  el("password").addEventListener("keydown", e => { if (e.key === "Enter") login(); });
  el("logoutButton").addEventListener("click", () => modules.authMod.signOut(auth));
  el("reloadButton").addEventListener("click", () => loadWeeks(weekIds[weekIndex]));
  el("prevWeek").addEventListener("click", () => navigateWeek(1));
  el("nextWeek").addEventListener("click", () => navigateWeek(-1));
  el("publishButton").addEventListener("click", publishPackage);
  el("saveOverrideButton").addEventListener("click", saveOverride);
}

async function login() {
  el("loginStatus").textContent = "Anmeldung läuft…";
  try {
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
    await modules.authMod.signInWithCustomToken(auth, payload.customToken);
    el("password").value = "";
    el("loginStatus").textContent = "";
  } catch (error) {
    el("loginStatus").textContent = error.message;
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
  for (const name of ["setupView","loginView","planView"]) el(name).classList.add("hidden");
  el(id).classList.remove("hidden");
}

function isoDate(date) {
  return `${date.getFullYear()}-${String(date.getMonth()+1).padStart(2,"0")}-${String(date.getDate()).padStart(2,"0")}`;
}

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, c => ({ "&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#039;" }[c]));
}
