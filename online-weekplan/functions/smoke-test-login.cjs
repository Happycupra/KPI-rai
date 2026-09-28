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
const company=db.collection('companies').doc(companyId);
const license=db.collection('licenses').doc(installationId);
const user=company.collection('authUsers').doc('1');
const endpoint='https://europe-west1-solution-compact.cloudfunctions.net/login';
let seeded=false;
async function login(pw){
 const r=await fetch(endpoint,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({companyCode,username,password:pw}),signal:AbortSignal.timeout(30000)});
 return {status:r.status,body:await r.json()};
}
(async()=>{
 try{
  const batch=db.batch();
  batch.create(license,{status:'active',validUntil:Timestamp.fromMillis(Date.now()+3600000)});
  batch.create(company,{companyCode,isActive:true,licenseInstallationId:installationId});
  batch.create(user,{sourceUserId:1,username,usernameNormalized:username,displayName:'Temporary login diagnostic',role:'Beobachter',isActive:true,passwordHash,passwordSalt:salt.toString('base64'),credentialVersion:'diagnostic'});
  await batch.commit();seeded=true;
  assert.equal((await login('intentionally-wrong-password')).status,401,'Wrong password must be denied');
  console.log('PASS: incorrect password denied');
  let result;
  for(let attempt=0;attempt<12;attempt++){
   result=await login(password);
   if(result.status!==500)break;
   if(attempt<11)await new Promise(resolve=>setTimeout(resolve,10000));
  }
  assert.equal(result.status,200,'Valid credentials must produce HTTP 200');
  assert.ok(result.body.customToken,'Custom token required');
  const config=require('./public-config-diagnostic.json');
  const response=await fetch('https://identitytoolkit.googleapis.com/v1/accounts:signInWithCustomToken?key='+encodeURIComponent(config.firebase.apiKey),{
   method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({token:result.body.customToken,returnSecureToken:true}),signal:AbortSignal.timeout(30000)
  });
  assert.equal(response.status,200,'Firebase must accept the signed custom token');
  const session=await response.json();
  const claims=await auth.verifyIdToken(session.idToken);
  assert.equal(claims.uid,uid);assert.equal(claims.companyId,companyId);assert.equal(claims.role,'Beobachter');
  console.log('PASS: live login, Firebase token exchange and verified tenant claims');
  await license.update({status:'suspended'});
  assert.equal((await login(password)).status,401,'Suspended license must be denied');
  console.log('PASS: suspended license denied');
 }finally{
  if(seeded){
   const cleanup=db.batch();cleanup.delete(user);cleanup.delete(company);cleanup.delete(license);
   const results=await Promise.allSettled([cleanup.commit(),auth.deleteUser(uid).catch(e=>{if(e.code!=='auth/user-not-found')throw e})]);
   if(results.some(r=>r.status==='rejected'))throw new Error('Diagnostic cleanup incomplete; inspect isolated fixture '+companyId);
   console.log('PASS: temporary Firestore records and Firebase Auth user removed');
  }
 }
})().catch(e=>{console.error(e.message);process.exitCode=1});
