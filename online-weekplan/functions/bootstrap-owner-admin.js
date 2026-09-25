const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getAuth } = require("firebase-admin/auth");

const PROJECT_ID = "solution-compact";
const PROJECT_NUMBER = "659351015653";
const OWNER_EMAIL = "irajet.ramadani@gmail.com";

async function request(url, options, token) {
  const response = await fetch(url, {
    ...options,
    headers: {
      "Authorization": `Bearer ${token}`,
      "Content-Type": "application/json",
      ...(options?.headers || {})
    }
  });
  const text = await response.text();
  const body = text ? JSON.parse(text) : {};
  if (!response.ok) throw new Error(`${response.status} ${response.statusText}: ${text}`);
  return body;
}

async function waitOperation(operation, token) {
  if (!operation?.name || operation.done === true || String(operation.name).includes("DONE_OPERATION")) return;
  for (let i = 0; i < 30; i++) {
    const current = await request(`https://serviceusage.googleapis.com/v1/${operation.name}`, { method: "GET" }, token);
    if (current.done) {
      if (current.error) throw new Error(JSON.stringify(current.error));
      return;
    }
    await new Promise(resolve => setTimeout(resolve, 2000));
  }
  throw new Error("Timeout while enabling Identity Toolkit API.");
}

async function main() {
  const credential = applicationDefault();
  initializeApp({ credential, projectId: PROJECT_ID });
  const access = await credential.getAccessToken();
  const token = access.access_token;

  const operation = await request(
    `https://serviceusage.googleapis.com/v1/projects/${PROJECT_NUMBER}/services/identitytoolkit.googleapis.com:enable`,
    { method: "POST", body: "{}" },
    token);
  await waitOperation(operation, token);

  const configUrl =
    `https://identitytoolkit.googleapis.com/admin/v2/projects/${PROJECT_ID}/config?updateMask=signIn.email.enabled,signIn.email.passwordRequired`;
  const configBody = JSON.stringify({ signIn: { email: { enabled: true, passwordRequired: true } } });

  try {
    await request(configUrl, { method: "PATCH", body: configBody }, token);
  } catch (error) {
    if (!String(error.message || error).includes("CONFIGURATION_NOT_FOUND")) throw error;

    await request(
      `https://identitytoolkit.googleapis.com/v2/projects/${PROJECT_ID}/identityPlatform:initializeAuth`,
      { method: "POST", body: "{}" },
      token);

    await request(configUrl, { method: "PATCH", body: configBody }, token);
  }

  await request(
    `https://identitytoolkit.googleapis.com/admin/v2/projects/${PROJECT_ID}/config?updateMask=notification.sendEmail.callbackUri`,
    {
      method: "PATCH",
      body: JSON.stringify({
        notification: {
          sendEmail: {
            callbackUri: "https://solution-compact.web.app/auth-action.html"
          }
        }
      })
    },
    token);

  const auth = getAuth();
  try {
    const user = await auth.getUserByEmail(OWNER_EMAIL);
    await auth.updateUser(user.uid, {
      email: OWNER_EMAIL,
      emailVerified: true,
      disabled: false,
      displayName: "SolutionCompakt Lizenz-Admin"
    });
    console.log("Owner admin account is ready:", OWNER_EMAIL);
  } catch (error) {
    if (error?.code !== "auth/user-not-found") throw error;
    await auth.createUser({
      email: OWNER_EMAIL,
      emailVerified: true,
      disabled: false,
      displayName: "SolutionCompakt Lizenz-Admin"
    });
    console.log("Owner admin account created:", OWNER_EMAIL);
  }
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
