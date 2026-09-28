const { initializeApp, cert } = require('firebase-admin/app');
const { getAuth } = require('firebase-admin/auth');
const { getFirestore, Timestamp } = require('firebase-admin/firestore');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');
initializeApp({credential:cert(JSON.parse(process.env.FIREBASE_SERVICE_ACCOUNT)),projectId:'solution-compact'});
const db=getFirestore(), auth=getAuth();
const suffix=crypto.randomBytes(16).toString('hex');
const companyId='diagnostic_'+suffix, installationId='diagnostic_'+suffix;
const companyCode='CHECK-'+suffix.slice(0,16).toUpperCase(), username='diagnostic';
const uid='solutioncompakt-'+companyId+'-1';
const password=crypto.randomBytes(32).toString('base64url');
const salt=crypto.randomBytes(16);
const passwordHash=crypto.pbkdf2Sync(password,salt,150000,32,'sha256').toString('base64');
const company=db.collection('companies').doc(companyId), license=db.collection('licenses').doc(installationId);
const user=company.collection('authUsers').doc('1');
let seeded=false;
async function post(url, body, token){
 const r=await fetch(url,{method:'POST',headers:{'Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{})},body:JSON.stringify(body),signal:AbortSignal.timeout(150000)});
 assert.equal(r.status,200,'Expected HTTP 200 at '+new URL(url).pathname);
 return r.json();
}
(async()=>{
 try{
  const batch=db.batch();
  batch.create(license,{status:'active',validUntil:Timestamp.fromMillis(Date.now()+3600000)});
  batch.create(company,{companyCode,isActive:true,licenseInstallationId:installationId});
  batch.create(user,{sourceUserId:1,username,usernameNormalized:username,displayName:'Temporary publish diagnostic',role:'Administrator',isActive:true,passwordHash,passwordSalt:salt.toString('base64'),credentialVersion:'diagnostic'});
  await batch.commit();seeded=true;
  const config=require('../public/config.json');
  const login=await post(config.authEndpoint,{companyCode,username,password});
  const session=await post('https://identitytoolkit.googleapis.com/v1/accounts:signInWithCustomToken?key='+encodeURIComponent(config.firebase.apiKey),{token:login.customToken,returnSecureToken:true});
  const snapshot={schemaVersion:'1.1',companyId,companyCode,weekId:'2030-W03',isoYear:2030,isoWeek:3,weekStart:'2030-01-14',weekEnd:'2030-01-20',entries:[{id:'diagnostic',date:'2030-01-14',employeeName:'Synthetic test',start:'08:00',end:'09:00'}],productionSlots:[]};
  const receipt=await post(config.publishEndpoint,snapshot,session.idToken);
  assert.equal(receipt.ok,true);assert.equal(receipt.weekId,snapshot.weekId);
  assert.ok(Number.isFinite(Date.parse(receipt.publishedAtUtc)),'Server timestamp is missing');
  const week=company.collection('weekPlans').doc(snapshot.weekId);
  const stored=(await week.get()).data();
  // Firestore serverTimestamp is the request time; WriteResult reports the later commit time.
  const serverTimeDelta=Date.parse(receipt.publishedAtUtc)-stored.publishedAt.toMillis();
  assert.ok(serverTimeDelta>=0 && serverTimeDelta<2000, "Receipt must correspond to the same server publication");
  assert.ok((await week.collection('versions').doc(stored.activeVersion).collection('entries').doc('diagnostic').get()).exists);
  console.log('PASS: live login, token exchange, authorized publish, complete snapshot and server-confirmed publication time');
 }finally{
  if(seeded){
   const results=await Promise.allSettled([db.recursiveDelete(company),license.delete(),auth.deleteUser(uid).catch(e=>{if(e.code!=='auth/user-not-found')throw e})]);
   if(results.some(r=>r.status==='rejected'))throw new Error('Diagnostic cleanup incomplete: '+companyId);
   console.log('PASS: isolated company, week versions, license and Auth user removed');
  }
 }
})().catch(e=>{console.error(e.message);process.exitCode=1});

