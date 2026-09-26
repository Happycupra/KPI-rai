const { test, before, after, beforeEach } = require('node:test');
const fs = require('node:fs');
const { initializeTestEnvironment, assertSucceeds, assertFails } = require('@firebase/rules-unit-testing');
const { doc, getDoc, setDoc, Timestamp } = require('firebase/firestore');
let env;
const company = 'company_0123456789012345';
const installation = 'installation_0123456789012345';
const weekPath = `companies/${company}/weekPlans/2026-W39`;
const claims = { companyId: company, sourceUserId: 1, role: 'Administrator', credentialVersion: 'v1' };
before(async () => {
  env = await initializeTestEnvironment({ projectId: 'demo-solutioncompakt',
    firestore: { rules: fs.readFileSync('../../online-weekplan/firestore.rules', 'utf8') } });
});
after(async () => { await env?.cleanup(); });
async function adminWrite(path, data) {
  await env.withSecurityRulesDisabled(async ctx => setDoc(doc(ctx.firestore(), path), data, { merge: true }));
}
beforeEach(async () => {
  await env.clearFirestore();
  await adminWrite(`licenses/${installation}`, { status: 'active', validUntil: Timestamp.fromMillis(Date.now() + 3600000) });
  await adminWrite(`companies/${company}`, { isActive: true, licenseInstallationId: installation });
  await adminWrite(`companies/${company}/authUsers/1`, { isActive: true, role: 'Administrator', credentialVersion: 'v1' });
  await adminWrite(weekPath, { activeVersion: 'published' });
  await adminWrite(`${weekPath}/versions/published/entries/1`, { employeeName: 'Test' });
  await adminWrite(`${weekPath}/versions/staged/entries/1`, { employeeName: 'Unpublished' });
});
test('active tenant may read published data but never hashes or licenses', async () => {
  const db = env.authenticatedContext('user', claims).firestore();
  await assertSucceeds(getDoc(doc(db, weekPath)));
  await assertSucceeds(getDoc(doc(db, `${weekPath}/versions/published/entries/1`)));
  await assertFails(getDoc(doc(db, `${weekPath}/versions/staged/entries/1`)));
  await assertFails(getDoc(doc(db, `companies/${company}/authUsers/1`)));
  await assertFails(getDoc(doc(db, `licenses/${installation}`)));
});
test('existing token loses read and write access immediately on license suspension', async () => {
  const db = env.authenticatedContext('user', claims).firestore();
  await assertSucceeds(getDoc(doc(db, weekPath)));
  await adminWrite(`licenses/${installation}`, { status: 'suspended' });
  await assertFails(getDoc(doc(db, weekPath)));
  await assertFails(setDoc(doc(db, `${weekPath}/overrides/1`), { employeeName: 'Forbidden' }));
});
test('expired license, disabled user, changed password and changed role deny stale tokens', async () => {
  const db = env.authenticatedContext('user', claims).firestore();
  await adminWrite(`licenses/${installation}`, { validUntil: Timestamp.fromMillis(Date.now() - 1000) });
  await assertFails(getDoc(doc(db, weekPath)));
  await adminWrite(`licenses/${installation}`, { validUntil: Timestamp.fromMillis(Date.now() + 3600000) });
  await adminWrite(`companies/${company}/authUsers/1`, { isActive: false });
  await assertFails(getDoc(doc(db, weekPath)));
  await adminWrite(`companies/${company}/authUsers/1`, { isActive: true, credentialVersion: 'v2' });
  await assertFails(getDoc(doc(db, weekPath)));
  await adminWrite(`companies/${company}/authUsers/1`, { credentialVersion: 'v1', role: 'Beobachter' });
  await assertFails(setDoc(doc(db, `${weekPath}/overrides/1`), { employeeName: 'Forbidden' }));
});
test('anonymous and different-company tokens cannot read the plan', async () => {
  await assertFails(getDoc(doc(env.unauthenticatedContext().firestore(), weekPath)));
  await assertFails(getDoc(doc(env.authenticatedContext('other', { ...claims, companyId: 'other' }).firestore(), weekPath)));
});
test('current administrator can correct a plan; observer can only read', async () => {
  const db = env.authenticatedContext('admin', claims).firestore();
  await assertSucceeds(setDoc(doc(db, `${weekPath}/overrides/1`), { employeeName: 'Allowed' }));
  await adminWrite(`companies/${company}/authUsers/1`, { role: 'Beobachter' });
  const observer = env.authenticatedContext('observer', { ...claims, role: 'Beobachter' }).firestore();
  await assertSucceeds(getDoc(doc(observer, weekPath)));
  await assertFails(setDoc(doc(observer, `${weekPath}/overrides/1`), { employeeName: 'Forbidden' }));
});
