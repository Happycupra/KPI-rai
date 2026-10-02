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
    const ref = { path, id: path.split('/').at(-1), doc: id => reference(path + '/' + id), collection: id => reference(path + '/' + id),
      get: async () => {
        const pieces = path.split('/');
        if (pieces.length % 2 === 0) return snapshot(path);
        const docs = [...data.keys()].filter(key => key.startsWith(path + '/') && !key.slice(path.length + 1).includes('/')).map(snapshot);
        return { docs, size: docs.length, empty: docs.length === 0 };
      },
      set: async (value, options) => { state.writes++; data.set(path, options?.merge ? { ...data.get(path), ...value } : value); return { writeTime: { toDate: () => new Date("2030-01-14T09:15:30.000Z") } }; },
      delete: async () => data.delete(path),
      listCollections: async () => [...new Set([...data.keys()].filter(key => key.startsWith(path + '/') && key.slice(path.length + 1).includes('/')).map(key => path + '/' + key.slice(path.length + 1).split('/')[0]))].map(reference),
      listDocuments: async () => [...new Set([...data.keys()].filter(key => key.startsWith(path + '/')).map(key => path + '/' + key.slice(path.length + 1).split('/')[0]))].map(reference) };
    ref.where = (field, _, value) => ({ limit: count => ({ get: async () => {
      const result = await ref.get();
      const docs = result.docs.filter(x => x.data()[field] === value).slice(0, count);
      return { docs, size: docs.length, empty: docs.length === 0 };
    } }) });
    return ref;
  }
  let transactionQueue = Promise.resolve();
  const db = { collection: name => reference(name),
    async recursiveDelete(ref) {
      if (state.beforeDelete) await state.beforeDelete(ref);
      for (const path of [...data.keys()]) if (path === ref.path || path.startsWith(ref.path + '/')) data.delete(path);
    },
    runTransaction(fn) {
      const operation = transactionQueue.then(async () => {
        const writes = [];
        const result = await fn({ get: async ref => snapshot(ref.path), set(ref, value, options) {
          writes.push(() => data.set(ref.path, options?.merge ? { ...data.get(ref.path), ...value } : value));
        } });
        if (state.failTransaction) throw new Error('Injected transaction failure');
        writes.forEach(write => write());
        state.writes += writes.length;
        return result;
      });
      transactionQueue = operation.catch(() => {});
      return operation;
    }, batch() {
    const ops = [];
    return { set: (ref, value) => ops.push(() => data.set(ref.path, value)), delete: ref => ops.push(() => data.delete(ref.path)),
      async commit() {
        state.commits++;
        if (state.commits === state.failBatch) throw new Error('Injected write failure');
        ops.forEach(op => op()); state.writes += ops.length;
        if (state.revokeOnBatch) data.get(`licenses/${installation}`).status = 'suspended';
        if (state.afterBatch) await state.afterBatch();
      } };
  } };
  const claims = { companyId, companyCode: 'SC-TEST', sourceUserId: 1, role: 'Administrator', username: 'admin', credentialVersion: 'v1' };
  const exported = {};
  const context = vm.createContext({ exports: exported, Buffer, console: { error() {} }, require(name) {
    if (name === 'firebase-functions/v2/https') return { onRequest: (_, fn) => fn };
    if (name === 'firebase-admin/app') return { initializeApp() {} };
    if (name === 'firebase-admin/auth') return { getAuth: () => ({ verifyIdToken: async (_, revoked) => { state.checkedRevocation = revoked; return claims; }, createCustomToken: async (_, tokenClaims) => { state.loginClaims = tokenClaims; return 'token'; } }) };
    if (name === 'firebase-admin/firestore') return { getFirestore: () => db, FieldValue: { serverTimestamp: () => ({ toDate: () => new Date('2030-01-14T09:15:30.000Z') }) }, Timestamp: { now: () => ({ toDate: () => new Date('2030-01-14T09:15:30.000Z') }), fromDate: date => date } };
    if (name === 'crypto') return crypto;
    if (name === './snapshot-store') return require('../online-weekplan/functions/snapshot-store');
    throw new Error(name);
  } });
  vm.runInContext(fs.readFileSync('online-weekplan/functions/index.js', 'utf8'), context);
  const body = { schemaVersion: '1.1', companyId, companyCode: 'SC-TEST', weekId: '2026-W39',
    isoYear: 2026, isoWeek: 39, weekStart: '2026-09-21', weekEnd: '2026-09-27',
    entries: [{ id: 'employee-1' }], productionSlots: [{ id: 'slot-1' }] };
  return { data, state, db, reference, companyId, installation, week, body,
    async invoke(name, value) {
      const response = { code: 200, set() {}, status(code) { this.code = code; return this; }, json(value) { this.body = value; }, send() {} };
      await exported[name]({ method: 'POST', headers: { authorization: 'Bearer test' }, body: value }, response);
      return response;
    }, async publish(value = body) {
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

function syncBody(h, count = 2) {
  const secret = 's'.repeat(40);
  Object.assign(h.data.get(`licenses/${h.installation}`), { secretHash: crypto.createHash('sha256').update(secret).digest('hex') });
  return { installationId: h.installation, secret, companyId: h.companyId, companyCode: 'SC-TEST', companyName: 'Test Company',
    users: Array.from({ length: count }, (_, index) => ({ sourceUserId: index + 1, username: `user${index + 1}`,
      role: 'Administrator', isActive: true, passwordHash: Buffer.alloc(32).toString('base64'), passwordSalt: Buffer.alloc(16).toString('base64') })) };
}

test('failed user upload or pointer transaction preserves the entire previous user list and company', async () => {
  for (const mode of ['batch', 'transaction']) {
    const h = harness();
    const body = syncBody(h, 500);
    const company = { ...h.data.get(`companies/${h.companyId}`) };
    if (mode === 'batch') h.state.failBatch = 2;
    else h.state.failTransaction = true;
    assert.equal((await h.invoke('syncOnlineAccess', body)).code, 500);
    assert.deepEqual(h.data.get(`companies/${h.companyId}`), company);
    assert.equal(h.data.get(`companies/${h.companyId}/authUsers/1`).credentialVersion, 'v1');
    assert.equal(h.data.get(`licenses/${h.installation}`).onlineAccessLastSyncedAt, undefined);
  }
});

test('all 500 users are uploaded before atomic activation; legacy user records are untouched', async () => {
  const h = harness();
  const body = syncBody(h, 500);
  h.state.afterBatch = async () => assert.equal(h.data.get(`companies/${h.companyId}`).activeUserVersion, undefined);
  assert.equal((await h.invoke('syncOnlineAccess', body)).code, 200);
  const version = h.data.get(`companies/${h.companyId}`).activeUserVersion;
  const path = `companies/${h.companyId}/authUserSets/${version}`;
  assert.equal(h.data.get(path).status, 'active');
  assert.equal(h.data.get(path).userCount, 500);
  assert.ok(h.data.has(`${path}/users/500`));
  assert.equal(h.data.get(`companies/${h.companyId}/authUsers/1`).credentialVersion, 'v1');
});

test('duplicate source IDs and license suspension during sync cannot activate a set', async () => {
  const h = harness();
  const body = syncBody(h);
  body.users[1].sourceUserId = 1;
  assert.equal((await h.invoke('syncOnlineAccess', body)).code, 400);
  assert.equal(h.state.writes, 0);
  const revoked = harness();
  revoked.state.revokeOnBatch = true;
  assert.equal((await revoked.invoke('syncOnlineAccess', syncBody(revoked))).code, 500);
  assert.equal(revoked.data.get(`companies/${revoked.companyId}`).activeUserVersion, undefined);
});

test('concurrent user syncs activate one complete immutable list without mixed users', async () => {
  const h = harness();
  const a = syncBody(h), b = syncBody(h);
  b.users.forEach(x => { x.username = 'changed' + x.sourceUserId; });
  const responses = await Promise.all([h.invoke('syncOnlineAccess', a), h.invoke('syncOnlineAccess', b)]);
  assert.ok(responses.every(x => x.code === 200));
  const version = h.data.get(`companies/${h.companyId}`).activeUserVersion;
  const path = `companies/${h.companyId}/authUserSets/${version}`;
  const isChanged = h.data.get(`${path}/users/1`).username.startsWith('changed');
  assert.equal(h.data.get(`${path}/users/2`).username.startsWith('changed'), isChanged);
  const statuses = [...h.data.entries()].filter(([key]) => key.startsWith(`companies/${h.companyId}/authUserSets/`) && key.split('/').length === 4).map(([, value]) => value.status);
  assert.deepEqual(statuses.sort(), ['active', 'superseded']);
});

test('login and publishing use the active user version rather than stale legacy permissions', async () => {
  const h = harness();
  const body = syncBody(h);
  const password = 'test-password';
  body.users[0].passwordHash = crypto.pbkdf2Sync(password, Buffer.alloc(16), 150000, 32, 'sha256').toString('base64');
  assert.equal((await h.invoke('syncOnlineAccess', body)).code, 200);
  assert.equal((await h.invoke('login', { companyCode: 'SC-TEST', username: 'user1', password })).code, 200);
  assert.equal(h.state.loginClaims.sourceUserId, 1);
  // The old token matched the legacy user; the new credential version must reject it.
  assert.equal((await h.publish()).code, 403);
});

const { retentionCandidates, cleanVersions } = require('../online-weekplan/functions/snapshot-store');
const at = value => ({ toDate: () => new Date(value) });

test('retention protects active/latest three; expires abandoned uploads and old superseded snapshots', () => {
  const now = Date.UTC(2030, 0, 15);
  const old = days => at(now - days * 86400000);
  const versions = [
    ...[1, 2, 3, 4, 5].map(id => ({ id: String(id), data: { status: 'superseded', completedAt: old(40 + id) } })),
    { id: 'active-old', data: { status: 'active', completedAt: old(100) } },
    { id: 'fresh', data: { status: 'uploading', createdAt: old(0.5) } },
    { id: 'abandoned', data: { status: 'uploading', createdAt: old(2) } },
    { id: 'legacy', data: {} },
    { id: 'resume', data: { status: 'deleting' } }
  ];
  assert.deepEqual(retentionCandidates(versions, 'active-old', now).map(x => x.id).sort(), ['4', '5', 'abandoned', 'resume']);
});

test('cleanup removes child documents and resumes partial deletion while preserving active data', async () => {
  const h = harness();
  const now = Date.now();
  const parent = h.reference(h.week);
  for (const id of ['old', 'previous', 'resume']) {
    h.data.set(`${h.week}/versions/${id}`, { status: id === 'resume' ? 'deleting' : 'uploading', createdAt: at(now - 2 * 86400000) });
    h.data.set(`${h.week}/versions/${id}/entries/1`, { value: id });
  }
  assert.equal(await cleanVersions(h.db, parent, 'versions', 'activeVersion', now), 2);
  assert.ok(h.data.has(`${h.week}/versions/previous/entries/1`));
  assert.ok(!h.data.has(`${h.week}/versions/old/entries/1`));
  assert.ok(!h.data.has(`${h.week}/versions/resume`));
});

test('cleanup rechecks the active pointer after scanning versions', async () => {
  const h = harness();
  h.data.set(`${h.week}/versions/old`, { status: 'uploading', createdAt: at(Date.now() - 2 * 86400000) });
  const run = h.db.runTransaction;
  h.db.runTransaction = fn => { h.data.get(h.week).activeVersion = 'old'; return run(fn); };
  assert.equal(await cleanVersions(h.db, h.reference(h.week), 'versions', 'activeVersion'), 0);
  assert.ok(h.data.has(`${h.week}/versions/old`));
});

test('failed child deletion keeps a tombstone so the next cleanup can resume', async () => {
  const h = harness();
  const path = `${h.week}/versions/old`;
  h.data.set(path, { status: 'uploading', createdAt: at(Date.now() - 2 * 86400000) });
  h.data.set(`${path}/entries/1`, {});
  h.state.beforeDelete = async () => { throw new Error('Interrupted cleanup'); };
  await assert.rejects(cleanVersions(h.db, h.reference(h.week), 'versions', 'activeVersion'));
  assert.equal(h.data.get(path).status, 'deleting');
  h.state.beforeDelete = null;
  assert.equal(await cleanVersions(h.db, h.reference(h.week), 'versions', 'activeVersion'), 1);
  assert.ok(!h.data.has(`${path}/entries/1`));
  assert.ok(!h.data.has(path));
});
