// Until the first successful versioned sync, existing companies keep using legacy users.
function userCollection(companyRef, company) {
  return company.activeUserVersion
    ? companyRef.collection("authUserSets").doc(company.activeUserVersion).collection("users")
    : companyRef.collection("authUsers");
}

function millis(value) {
  const date = value?.toDate?.();
  return date instanceof Date ? date.getTime() : 0;
}

// Retain the active version, the three most recent completed versions, and recent uploads.
function retentionCandidates(versions, activeVersion, now = Date.now()) {
  const completed = versions.filter(x => ["active", "complete", "superseded"].includes(x.data.status))
    .sort((a, b) => millis(b.data.completedAt) - millis(a.data.completedAt));
  const kept = new Set(completed.slice(0, 3).map(x => x.id));
  return versions.filter(x => {
    if (x.id === activeVersion) return false;
    if (x.data.status === "deleting") return true; // Resume interrupted recursive deletion.
    if (x.data.status === "uploading")
      return millis(x.data.createdAt) > 0 && now - millis(x.data.createdAt) > 24 * 3600000;
    const ageFrom = millis(x.data.completedAt) || millis(x.data.supersededAt);
    return !kept.has(x.id) && ageFrom > 0 && now - ageFrom > 30 * 86400000;
  });
}

async function cleanVersions(db, parentRef, collectionName, pointer, now = Date.now()) {
  // listDocuments also includes old versions with children but no metadata document.
  const refs = await parentRef.collection(collectionName).listDocuments();
  const versions = [];
  for (const ref of refs) {
    const snapshot = await ref.get();
    versions.push({ id: ref.id, ref, data: snapshot.data() || {} });
  }
  const parent = await parentRef.get();
  let deleted = 0;
  for (const version of retentionCandidates(versions, parent.data()?.[pointer], now)) {
    const claimed = await db.runTransaction(async transaction => {
      const current = await transaction.get(parentRef);
      const snapshot = await transaction.get(version.ref);
      if (current.data()?.[pointer] === version.id) return false;
      // Re-evaluate age/status, protecting uploads completed since the initial scan.
      const candidate = { ...version, data: snapshot.data() || {} };
      const refreshed = versions.map(x => x.id === candidate.id ? candidate : x);
      if (!retentionCandidates(refreshed, current.data()?.[pointer], now).some(x => x.id === candidate.id)) return false;
      transaction.set(version.ref, { status: "deleting" }, { merge: true });
      return true;
    });
    if (claimed) {
      // Keep the tombstone document until every child collection has been removed.
      // If a child deletion fails, tomorrow's run still sees status: deleting.
      for (const collection of await version.ref.listCollections())
        await db.recursiveDelete(collection);
      await version.ref.delete();
      deleted++;
    }
  }
  return deleted;
}

module.exports = { userCollection, retentionCandidates, cleanVersions };
