# Atomic online user sync and snapshot retention

## User versions

`syncOnlineAccess` stages each complete list under
`companies/{companyId}/authUserSets/{versionId}/users/{sourceUserId}`. Each version
has `versionId`, `createdAt`, `completedAt`, `createdBy`, `status` and `userCount`.
The server validates distinct IDs/names and the uploaded count before activating.
A Firestore transaction changes `activeUserVersion`, company/license metadata,
and the previous/new version statuses together. An upload/transaction failure
leaves the previous list active. The license and installation binding are checked
again inside that transaction. Concurrent uploads remain separate complete lists;
the last successful transaction becomes active.

Login, HTTP administrator authorization and Firestore rules resolve the same
active version. Existing companies fall back to `authUsers` until their first
successful versioned sync. After activation, legacy users never authorize access.
Password hashes, salts and user-version metadata cannot be read/written by web
clients. Password, role, deactivation and removal checks still revoke stale tokens.

## Desktop retries

User changes durably save a `PendingOnlineAccessSync` in local settings before
scheduling a request. It contains `PayloadVersion`, `Attempts`, `NextRetryAtUtc`
and `LastError`. The database remains the source of the payload: the queue stores
no extra credential copies and coalesces edits into the latest full user list.
The worker resumes pending work after startup and checks every 30 seconds.
Failures use exponential backoff from 30 seconds to one hour. A successful request
only clears its own revision, preserving edits queued while it was in flight.
License/configuration failures also remain pending until they can be retried.
Local user saves do not wait for the network request.

## Snapshot cleanup

Week-plan uploads create metadata before writing children, then activate in a
transaction with completion timestamps and the previous version's superseded
status. A failed upload stays `uploading` and cannot replace the published plan.

The `cleanupSnapshots` scheduled Cloud Function runs daily at 03:00 UTC for both
week-plan versions and auth-user sets:

- Always retain the active pointer target.
- Retain the three newest completed versions.
- Delete abandoned `uploading` versions older than 24 hours.
- Delete other completed versions older than 30 days.
- Delete all child collections recursively, resuming versions marked `deleting`.

Before deletion, a transaction rechecks the active pointer and eligibility and
claims the version with `status: deleting`. Activation refuses such a version.
Old versions without timestamps are retained conservatively because their age
cannot be established. A legacy active week version gains a superseded timestamp
when the next plan is published; untouched historical orphans need a separately
reviewed migration rather than guessing their age.

## Deployment and verification

Deploy the **functions and Firestore rules together**, and upgrade desktop clients
for persistent retries. The existing endpoint URLs and browser application do not
change. Deploying this code creates a Cloud Scheduler job and requires the Firebase
billing plan/permissions for scheduled functions. Existing legacy data remains
usable before the first successful versioned sync.

```sh
node --test tests/online-weekplan.test.cjs tests/online-functions.test.cjs tests/login-signing.test.cjs
npm ci --prefix tests/firestore --ignore-scripts
npm test --prefix tests/firestore
```

Windows CI additionally exercises persisted retry state, backoff, stale
acknowledgements, HTTP failure and successful retry alongside existing WPF/SQLite
regressions. Deployment to the live Firebase project is separate from a GitHub
merge; the scheduled cleanup runs only once the functions are deployed.
