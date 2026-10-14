'use strict';
function stale(value,now) {
  if(value?.status==='deleting')return true;
  const touched=Date.parse(value?.lastTouchedAtUtc||value?.createdAtUtc||'');
  return ['uploading','validating'].includes(value?.status)&&Number.isFinite(touched)&&now-touched>86400000;
}
async function cleanCloudImports(db,companyRef,now=Date.now()) {
  let deleted=0;
  for(const ref of await companyRef.collection('dataSets').listDocuments()) {
    const candidate=(await ref.get()).data();if(!stale(candidate,now))continue;
    const claimed=await db.runTransaction(async tx=>{
      const company=await tx.get(companyRef),current=await tx.get(ref);
      if(company.data()?.activeDataSet===ref.id||!stale(current.data(),now))return false;
      tx.set(ref,{status:'deleting'},{merge:true});return true;
    });
    if(claimed){await db.recursiveDelete(ref);deleted++;}
  }
  return deleted;
}
module.exports={cleanCloudImports,stale};
