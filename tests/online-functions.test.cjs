const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const crypto = require('node:crypto');

function harness() {
  const companyId = 'company_0123456789012345';
  const installation = 'installation_0123456789012345';
  const week = `companies/${companyId}/weekPlans/2026-W39`;
  const data = new Map([
    [`companies/${companyId}`, { isActive: true, licenseInstallationId: installation }],
    [`licenses/${installation}`, { status: 'active', validUntil: { toDate: () => new Date(Date.now() + 3600000) } }],
    [`companies/${companyId}/authUsers/1`, { isActive: true, role: 'Administrator', credentialVersion: 'v1' }],
    [week, { activeVersion: 'previous', assignmentCount: 1 }]
  ]);
  const state = { commits: 0, failBatch: 0, revokeOnBatch: false, writes: 0, checkedRevocation: false };
  function snapshot(path) {
    return { id: path.split('/').at(-1), ref: reference(path), exists: data.has(path), data: () => data.get(path) };
  }
  function reference(path) {
    return { path, doc: id => reference(path + '/' + id), collection: id => reference(path + '/' + id),
      get: async () => snapshot(path),
      set: async (value, options) => { state.writes++; data.set(path, options?.merge ? { ...data.get(path), ...value } : value); return { writeTime: { toDate: () => new Date("2030-01-14T09:15:30.000Z") } }; },
      listDocuments: async () => [...data.keys()].filter(key => key.startsWith(path + '/') && !key.slice(path.length + 1).includes('/')).map(reference) };
  }
  const db = { collection: name => reference(name), batch() {
    const ops = [];
    return { set: (ref, value) => ops.push(() => data.set(ref.path, value)), delete: ref => ops.push(() => data.delete(ref.path)),
      async commit() {
        state.commits++;
        if (state.commits === state.failBatch) throw new Error('Injected write failure');
        ops.forEach(op => op()); state.writes += ops.length;
        if (state.revokeOnBatch) data.get(`licenses/${installation}`).status = 'suspended';
      } };
  } };
  const claims = { companyId, companyCode: 'SC-TEST', sourceUserId: 1, role: 'Administrator', username: 'admin', credentialVersion: 'v1' };
  const exported = {};
  const context = vm.createContext({ exports: exported, Buffer, console: { error() {} }, require(name) {
    if (name === 'firebase-functions/v2/https') return { onRequest: (_, fn) => fn };
    if (name === 'firebase-admin/app') return { initializeApp() {} };
    if (name === 'firebase-admin/auth') return { getAuth: () => ({ verifyIdToken: async (_, revoked) => { state.checkedRevocation = revoked; return claims; } }) };
    if (name === 'firebase-admin/firestore') return { getFirestore: () => db, FieldValue: { serverTimestamp: () => 'now' }, Timestamp: { fromDate: date => date } };
    if (name === 'crypto') return crypto;
    throw new Error(name);
  } });
  vm.runInContext(fs.readFileSync('online-weekplan/functions/index.js', 'utf8'), context);
  const body = { schemaVersion: '1.1', companyId, companyCode: 'SC-TEST', weekId: '2026-W39',
    isoYear: 2026, isoWeek: 39, weekStart: '2026-09-21', weekEnd: '2026-09-27',
    entries: [{ id: 'employee-1' }], productionSlots: [{ id: 'slot-1' }] };
  return { data, state, companyId, installation, week, body, async publish(value = body) {
    const response = { code: 200, set() {}, status(code) { this.code = code; return this; }, json(value) { this.body = value; }, send() {} };
    await exported.publishWeekPlan({ method: 'POST', headers: { authorization: 'Bearer test' }, body: value }, response);
    return response;
  } };
}

test('suspended/expired license denies publishing even with an existing admin token', async () => {
  for (const mode of ['suspended', 'expired']) {
    const h = harness();
    const license = h.data.get(`licenses/${h.installation}`);
    if (mode === 'suspended') license.status = mode;
    else license.validUntil = { toDate: () => new Date(0) };
    assert.equal((await h.publish()).code, 403);
    assert.equal(h.state.writes, 0);
    assert.equal(h.state.checkedRevocation, true);
  }
});
test('disabled/demoted user and stale password token cannot publish', async () => {
  for (const change of [{ isActive: false }, { role: 'Beobachter' }, { credentialVersion: 'v2' }]) {
    const h = harness();
    Object.assign(h.data.get(`companies/${h.companyId}/authUsers/1`), change);
    assert.equal((await h.publish()).code, 403);
    assert.equal(h.state.writes, 0);
  }
});
test('duplicate and invalid item IDs fail before changing published data', async () => {
  for (const entries of [[{ id: 'same' }, { id: 'same' }], [{ id: '../bad' }]]) {
    const h = harness();
    assert.equal((await h.publish({ ...h.body, entries })).code, 400);
    assert.equal(h.state.writes, 0);
    assert.equal(h.data.get(h.week).activeVersion, 'previous');
  }
});
test('failed second upload batch leaves the previous complete version active', async () => {
  const h = harness();
  h.state.failBatch = 2;
  assert.equal((await h.publish()).code, 500);
  assert.equal(h.data.get(h.week).activeVersion, 'previous');
  assert.equal(h.data.get(h.week).assignmentCount, 1);
});
test('suspension during upload prevents publication', async () => {
  const h = harness();
  h.state.revokeOnBatch = true;
  assert.equal((await h.publish()).code, 403);
  assert.equal(h.data.get(h.week).activeVersion, 'previous');
});
test('complete upload publishes a version containing both collections', async () => {
  const h = harness();
  assert.equal((await h.publish()).code, 200);
  const version = h.data.get(h.week).activeVersion;
  assert.notEqual(version, 'previous');
  assert.ok(h.data.has(`${h.week}/versions/${version}/entries/employee-1`));
  assert.ok(h.data.has(`${h.week}/versions/${version}/productionSlots/slot-1`));
});
test('concurrent publishers never mix their entries and production slots', async () => {
  const h = harness();
  await Promise.all([h.publish({ ...h.body, entries: [{ id: 'a' }], productionSlots: [{ id: 'a' }] }),
    h.publish({ ...h.body, entries: [{ id: 'b' }], productionSlots: [{ id: 'b' }] })]);
  const path = `${h.week}/versions/${h.data.get(h.week).activeVersion}`;
  const a = h.data.has(`${path}/entries/a`);
  assert.equal(h.data.has(`${path}/productionSlots/a`), a);
  assert.equal(h.data.has(`${path}/productionSlots/b`), !a);
});

test('publish returns the server write time only after the new version is committed', async () => {
  const h = harness();
  const response = await h.publish();
  assert.equal(response.code, 200);
  assert.equal(response.body.publishedAtUtc, '2030-01-14T09:15:30.000Z');
  assert.equal(response.body.weekId, h.body.weekId);
  assert.notEqual(h.data.get(h.week).activeVersion, 'previous');
  const failed = harness();
  failed.state.failBatch = 1;
  const error = await failed.publish();
  assert.equal(error.code, 500);
  assert.equal(error.body.publishedAtUtc, undefined);
});

test('messaging functions parse and enforce company-scoped storage', () => {
  const source = fs.readFileSync('online-weekplan/functions/messages.js', 'utf8');
  assert.doesNotThrow(() => new vm.Script(source));
  assert.match(source, /companyRef\.collection\("messages"\)/);
  assert.match(source, /requestedCompanyId !== companyId/);
  assert.match(source, /recipientUserId.*access\.sourceUserId/s);
  assert.match(source, /String\(data\.companyId \|\| ""\) !== access\.companyId/);

  const entry = fs.readFileSync('online-weekplan/functions/entry.js', 'utf8');
  assert.match(entry, /require\("\.\/index"\)/);
  assert.match(entry, /require\("\.\/messages"\)/);
});
