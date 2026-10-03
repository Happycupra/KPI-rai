'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { validateAcceptance, verifyReviewedSource, REQUIRED_CASES } = require('../scripts/validate-release-acceptance.cjs');
const now = new Date('2026-10-03T12:00:00Z');
function report() {
  return { version: '1.2.3', sourceCommit: 'a'.repeat(40), cases: REQUIRED_CASES.map(id => ({
    id, status: 'passed', reviewer: 'Windows QA', testedAtUtc: '2026-10-02T12:00:00Z', evidence: 'QA report with screenshots and two device logs'
  })) };
}
test('complete, dated acceptance for the release is accepted', () => validateAcceptance(report(), '1.2.3', now));
test('pending, missing, duplicate and undocumented checks prevent release', () => {
  for (const mutate of [r => r.cases.pop(), r => r.cases.push(r.cases[0]),
    r => r.cases[0].status = 'pending', r => r.cases[0].reviewer = '', r => r.cases[0].evidence = '']) {
    const r = report(); mutate(r); assert.throws(() => validateAcceptance(r, '1.2.3', now));
  }
});
test('wrong versions, abbreviated commits and stale or future results are rejected', () => {
  for (const mutate of [r => r.version = '1.2.4', r => r.sourceCommit = 'abcdef',
    r => r.cases[0].testedAtUtc = '2026-08-01T00:00:00Z',
    r => r.cases[0].testedAtUtc = '2027-01-01T00:00:00Z', r => r.cases[0].testedAtUtc = 'invalid']) {
    const r = report(); mutate(r); assert.throws(() => validateAcceptance(r, '1.2.3', now));
  }
});
test('changed or unrelated source cannot reuse acceptance from another build', () => {
  assert.throws(() => verifyReviewedSource('a'.repeat(40), () => { throw new Error('Not an ancestor'); }));
  assert.throws(() => verifyReviewedSource('a'.repeat(40), (...args) => args[0] === 'diff' ? 'src/changed.cs\n' : ''), /Source changed/);
  const calls = [];
  verifyReviewedSource('a'.repeat(40), (...args) => { calls.push(args); return ''; });
  assert.equal(calls[0][0], 'merge-base');
  assert.ok(calls[1].includes('scripts') && calls[1].includes('.github') && calls[1].includes('src'));
});
