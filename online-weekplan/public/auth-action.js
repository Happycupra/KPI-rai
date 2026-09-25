const FIREBASE_VERSION = "12.19.0";
const $ = id => document.getElementById(id);

let authMod, auth, code;

bootstrap();

async function bootstrap() {
  try {
    const params = new URLSearchParams(location.search);
    const mode = params.get("mode");
    code = params.get("oobCode") || "";
    if (mode !== "resetPassword" || !code) throw new Error("Dieser Link ist unvollständig oder nicht für einen Passwort-Reset bestimmt.");

    const config = await (await fetch("config.json", { cache: "no-store" })).json();
    const [appMod, loadedAuth] = await Promise.all([
      import(`https://www.gstatic.com/firebasejs/${FIREBASE_VERSION}/firebase-app.js`),
      import(`https://www.gstatic.com/firebasejs/${FIREBASE_VERSION}/firebase-auth.js`)
    ]);
    authMod = loadedAuth;
    auth = authMod.getAuth(appMod.initializeApp(config.firebase));
    auth.languageCode = "de";

    const email = await authMod.verifyPasswordResetCode(auth, code);
    $("emailText").textContent = "Konto: " + email;
    $("loading").classList.add("hidden");
    $("form").classList.remove("hidden");
    $("saveButton").addEventListener("click", save);
    $("backButton").addEventListener("click", () => location.href = "/admin.html");
  } catch (error) {
    $("loading").classList.add("hidden");
    $("status").textContent =
      "Der Link ist ungültig oder abgelaufen. Bitte auf der Admin-Seite erneut „Passwort festlegen / vergessen“ wählen.";
    console.error(error);
  }
}

async function save() {
  $("status").textContent = "";
  const password = $("password").value;
  const confirm = $("confirm").value;
  if (password.length < 10) {
    $("status").textContent = "Das Passwort muss mindestens 10 Zeichen lang sein.";
    return;
  }
  if (password !== confirm) {
    $("status").textContent = "Die Passwörter stimmen nicht überein.";
    return;
  }

  $("saveButton").disabled = true;
  try {
    await authMod.confirmPasswordReset(auth, code, password);
    $("form").classList.add("hidden");
    $("done").classList.remove("hidden");
  } catch (error) {
    $("status").textContent =
      "Das Passwort konnte nicht gespeichert werden. Bitte fordere auf der Admin-Seite einen neuen Link an.";
    console.error(error);
    $("saveButton").disabled = false;
  }
}
