'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const REQUIRED_CASES = Object.freeze([
  '1366x768-100', '1920x1080-100', '1920x1080-125',
  '2560x1440-125', '2560x1440-150', '3840x2160-150',
  '3840x2160-200', 'mixed-dpi-monitors',
  'large-synthetic-data', 'two-pc-concurrency', 'two-pc-messages',
  'tenant-isolation', 'server-restart', 'offline-recovery'
]);
const SOURCE_PATHS = Object.freeze(['src', 'scripts', 'installer', '.github', 'tests', 'online-weekplan', 'Produktionsplanung.sln']);
function validateAcceptance(report, version, now = new Date()) {
  if (!report || report.version !== version) throw new Error('Acceptance version does not match release tag');
  if (!/^[a-f0-9]{40}$/.test(report.sourceCommit || '')) throw new Error('A full reviewed source commit is required');
  if (!Array.isArray(report.cases)) throw new Error('Acceptance cases are required');
  for (const id of REQUIRED_CASES) {
    const matching = report.cases.filter(item => item && item.id === id);
    if (matching.length !== 1) throw new Error(`Exactly one acceptance result required: ${id}`);
    const item = matching[0];
    if (item.status !== 'passed' || typeof item.reviewer !== 'string' || !item.reviewer.trim() ||
        typeof item.evidence !== 'string' || !item.evidence.trim()) throw new Error(`Unverified acceptance case: ${id}`);
    const date = new Date(item.testedAtUtc);
    if (!item.testedAtUtc || !Number.isFinite(date.getTime()) || date > now || now - date > 30 * 86400000)
      throw new Error(`Acceptance must be dated within the last 30 days: ${id}`);
  }
}
function verifyReviewedSource(sourceCommit, git = (...args) => execFileSync('git', args, { encoding: 'utf8' })) {
  git('merge-base', '--is-ancestor', sourceCommit, 'HEAD');
  const changes = git('diff', '--name-only', sourceCommit, 'HEAD', '--', ...SOURCE_PATHS);
  if (changes.trim()) throw new Error(`Source changed since acceptance; repeat affected acceptance: ${changes.trim()}`);
}
if (require.main === module) {
  try {
    const tag = process.argv[process.argv.indexOf('--tag') + 1];
    if (!/^v\d+\.\d+\.\d+$/.test(tag || '')) throw new Error('Expected --tag vMAJOR.MINOR.PATCH');
    const version = tag.slice(1);
    const report = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'docs', 'release-acceptance', `${version}.json`), 'utf8'));
    validateAcceptance(report, version);
    verifyReviewedSource(report.sourceCommit);
    console.log(`Release acceptance verified for ${tag}`);
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
module.exports = { REQUIRED_CASES, SOURCE_PATHS, validateAcceptance, verifyReviewedSource };
