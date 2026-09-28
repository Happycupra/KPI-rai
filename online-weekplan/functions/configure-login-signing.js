// Custom-token signing uses the login function's runtime identity, not the CI identity.
const PROJECT_ID = 'solution-compact';
const PROJECT_NUMBER = '659351015653';
const TOKEN_CREATOR = 'roles/iam.serviceAccountTokenCreator';

function selfSigningPolicy(policy, email) {
  const member = `serviceAccount:${email}`;
  const bindings = policy.bindings || [];
  if (bindings.some(b => b.role === TOKEN_CREATOR && !b.condition && b.members?.includes(member))) return null;
  if (bindings.some(b => b.role === TOKEN_CREATOR && b.condition && b.members?.includes(member)))
    throw new Error('Existing conditional signing grant requires administrator review.');
  const result = structuredClone(policy);
  result.bindings ||= [];
  const binding = result.bindings.find(b => b.role === TOKEN_CREATOR && !b.condition);
  if (binding) binding.members = [...(binding.members || []), member];
  else result.bindings.push({ role: TOKEN_CREATOR, members: [member] });
  return result;
}

async function configureLoginSigning(request) {
  const fn = await request(`https://cloudfunctions.googleapis.com/v2/projects/${PROJECT_ID}/locations/europe-west1/functions/login`);
  const email = fn.serviceConfig?.serviceAccountEmail;
  if (email !== `${PROJECT_NUMBER}-compute@developer.gserviceaccount.com` &&
      !/^[a-z][a-z0-9-]*@solution-compact\.iam\.gserviceaccount\.com$/.test(email || ''))
    throw new Error('Unexpected login runtime account; refusing to change permissions.');
  // Resource-level self grant only: never grant Token Creator across the project.
  const resource = `https://iam.googleapis.com/v1/projects/${PROJECT_ID}/serviceAccounts/${email}`;
  const current = await request(`${resource}:getIamPolicy`, { options: { requestedPolicyVersion: 3 } });
  const policy = selfSigningPolicy(current, email);
  if (policy) {
    await request(`${resource}:setIamPolicy`, { policy });
    const verified = await request(`${resource}:getIamPolicy`, { options: { requestedPolicyVersion: 3 } });
    if (selfSigningPolicy(verified, email)) throw new Error('Login signing permission was not saved.');
  }
  return { email, changed: Boolean(policy) };
}

async function main() {
  const { applicationDefault } = require('firebase-admin/app');
  const credential = applicationDefault();
  const request = async (url, body) => {
    const { access_token } = await credential.getAccessToken();
    const response = await fetch(url, {
      method: body ? 'POST' : 'GET',
      headers: { Authorization: `Bearer ${access_token}`, 'Content-Type': 'application/json' },
      ...(body ? { body: JSON.stringify(body) } : {}),
      signal: AbortSignal.timeout(30000)
    });
    if (!response.ok) throw new Error(`Login signing configuration failed: HTTP ${response.status} at ${new URL(url).pathname}.`);
    return response.json();
  };
  const result = await configureLoginSigning(request);
  console.log(`Login signing ${result.changed ? 'configured' : 'already configured'} for runtime account ${result.email}.`);
}

module.exports = { selfSigningPolicy, configureLoginSigning, TOKEN_CREATOR };
if (require.main === module) main().catch(error => { console.error(error.message); process.exitCode = 1; });
