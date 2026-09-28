const test = require('node:test');
const assert = require('node:assert/strict');
const { selfSigningPolicy, configureLoginSigning, TOKEN_CREATOR } = require('../online-weekplan/functions/configure-login-signing');
const email = '659351015653-compute@developer.gserviceaccount.com';
const member = `serviceAccount:${email}`;

test('self signing preserves etag, policy version, other members and conditional bindings', () => {
  const original = { version: 3, etag: 'concurrency-control', bindings: [
    { role: 'roles/iam.serviceAccountUser', members: ['serviceAccount:existing@example.test'] },
    { role: TOKEN_CREATOR, members: ['serviceAccount:conditional@example.test'], condition: { title: 'expires', expression: 'false' } },
    { role: TOKEN_CREATOR, members: ['serviceAccount:other@example.test'] }
  ] };
  const before = JSON.stringify(original);
  const result = selfSigningPolicy(original, email);
  assert.equal(JSON.stringify(original), before);
  assert.equal(result.etag, original.etag);
  assert.equal(result.version, 3);
  assert.deepEqual(result.bindings.slice(0, 2), original.bindings.slice(0, 2));
  assert.deepEqual(result.bindings[2].members, ['serviceAccount:other@example.test', member]);
  assert.equal(selfSigningPolicy(result, email), null);
});

test('an existing conditional self grant cannot be silently bypassed', () => {
  assert.throws(() => selfSigningPolicy({ bindings: [{ role: TOKEN_CREATOR, members: [member], condition: { expression: 'false' } }] }, email), /administrator review/);
});

test('configuration grants only on the runtime account and verifies the stored policy', async () => {
  const calls = [];
  let stored = { etag: 'original' };
  const result = await configureLoginSigning(async (url, body) => {
    calls.push({ url, body });
    if (url.endsWith('/functions/login')) return { serviceConfig: { serviceAccountEmail: email } };
    assert.ok(url.includes(`/projects/solution-compact/serviceAccounts/${email}:`));
    if (url.endsWith(':setIamPolicy')) { stored = body.policy; return stored; }
    assert.equal(body.options.requestedPolicyVersion, 3);
    return stored;
  });
  assert.equal(result.changed, true);
  assert.equal(calls.length, 4);
  assert.deepEqual(stored.bindings, [{ role: TOKEN_CREATOR, members: [member] }]);
  assert.equal(stored.etag, 'original');
});

test('foreign runtime identities and API permission failures never trigger a policy write', async () => {
  for (const mode of ['foreign', 'denied']) {
    let writes = 0;
    await assert.rejects(configureLoginSigning(async (url) => {
      if (url.endsWith(':setIamPolicy')) writes++;
      if (url.endsWith('/functions/login')) return { serviceConfig: { serviceAccountEmail: mode === 'foreign' ? 'other@another-project.iam.gserviceaccount.com' : email } };
      throw new Error('HTTP 403');
    }));
    assert.equal(writes, 0);
  }
});
