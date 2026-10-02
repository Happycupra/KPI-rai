const { onSchedule } = require("firebase-functions/v2/scheduler");
const { getFirestore } = require("firebase-admin/firestore");
const { cleanVersions } = require("./snapshot-store");

exports.cleanupSnapshots = onSchedule({
  schedule: "every day 03:00", timeZone: "Etc/UTC", region: "europe-west1",
  timeoutSeconds: 540, memory: "512MiB"
}, async () => {
  const db = getFirestore();
  let cursor;
  let deleted = 0;
  for (;;) {
    let query = db.collection("companies").orderBy("__name__").limit(50);
    if (cursor) query = query.startAfter(cursor);
    const companies = await query.get();
    if (companies.empty) break;
    for (const company of companies.docs) {
      deleted += await cleanVersions(db, company.ref, "authUserSets", "activeUserVersion");
      const weeks = await company.ref.collection("weekPlans").listDocuments();
      for (const week of weeks)
        deleted += await cleanVersions(db, week, "versions", "activeVersion");
    }
    cursor = companies.docs.at(-1);
  }
  console.info("Snapshot retention completed", { deleted });
});
