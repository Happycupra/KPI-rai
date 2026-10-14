'use strict';
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const { initializeApp, deleteApp } = require('firebase-admin/app');
const { getFirestore, Timestamp } = require('firebase-admin/firestore');
const { CompanyCloudStore } = require('../../online-weekplan/functions/company-cloud-store');
const { schema } = require('../../online-weekplan/functions/cloud-validation');
let app,db,store; const guid=()=>crypto.randomUUID().replaceAll('-','');const prefixes=[];
before(()=>{app=initializeApp({projectId:'demo-solutioncompakt-cloud'},'cloud-tests');db=getFirestore(app);store=new CompanyCloudStore(db);});
after(async()=>{for(const ref of prefixes)await db.recursiveDelete(ref);await deleteApp(app);});
async function fixture() {
  const companyId=guid(),installationId='installation_'+guid(),secret='secret_'+guid(),ref=db.collection('companies').doc(companyId);
  prefixes.push(ref,db.collection('licenses').doc(installationId));
  const password='Synthetic-Password1',salt=crypto.randomBytes(16),passwordHash=crypto.pbkdf2Sync(password,salt,150000,32,'sha256').toString('base64');
  await ref.set({companyId,companyCode:'TEST-'+companyId.slice(0,6),companyName:'Synthetic company',isActive:true,licenseInstallationId:installationId});
  await db.collection('licenses').doc(installationId).set({status:'active',validUntil:Timestamp.fromDate(new Date(Date.now()+3600000))});
  for(const [id,role] of [[1,'Administrator'],[2,'Planer'],[3,'Beobachter']])await ref.collection('authUsers').doc(String(id)).set({sourceUserId:id,username:'synthetic-'+id,usernameNormalized:'synthetic-'+id,displayName:'Synthetic '+id,role,isActive:true,credentialVersion:'v1',passwordHash,passwordSalt:salt.toString('base64')});
  const claims={companyId,sourceUserId:1,role:'Administrator',credentialVersion:'v1'};
  const device={installationId,secret};await store.join(claims,device);
  return {companyId,ref,claims,device,password, user:(id,role)=>({...claims,sourceUserId:id,role})};
}
function record(kind,id=guid(),overrides={}) {
  const data={};for(const [name,f] of Object.entries(schema[kind].fields)) data[name]=f.nullable?null:f.type==='date'?'2032-04-05T00:00:00':f.type==='time'?'06:00:00':f.type==='number'?0:f.type==='boolean'?!name.includes('Deleted'):f.type==='reference'?guid():name+'-test';
  Object.assign(data,overrides);return {kind,id,baseVersion:0,deleted:false,data};
}
async function seed(f,records=[]) {
  const {dataSetId}=await store.beginImport(f.claims,f.device,{entryCount:records.length});
  for(let i=0;i<records.length;i+=100)await store.importPage(f.claims,f.device,{dataSetId,records:records.slice(i,i+100)});
  await store.completeImport(f.claims,f.device,{dataSetId});return dataSetId;
}
async function rejected(action,status){await assert.rejects(action,e=>e.status===status);}
test('homeoffice device inherits firm identity and license without receiving credentials',async()=>{
  const f=await fixture();const result=await store.join(f.user(2,'Planer'),{installationId:'homeoffice_'+guid(),secret:'secret_'+guid()});
  assert.equal(result.companyId,f.companyId);assert.equal(result.sourceUserId,2);assert.equal(result.users.length,3);
  assert.ok(!JSON.stringify(result).includes('passwordHash'));assert.ok(!JSON.stringify(result).includes('secretHash'));
  await rejected(()=>store.join(f.claims,{...f.device,secret:'wrong_'+guid()}),403);
});
test('initial publication remains unavailable until all rows and references are complete',async()=>{
  const f=await fixture(), employee=record('Employee');const {dataSetId}=await store.beginImport(f.claims,f.device,{entryCount:2});
  await store.importPage(f.claims,f.device,{dataSetId,records:[employee]});await rejected(()=>store.bootstrap(f.claims,f.device,{}),409);
  await rejected(()=>store.completeImport(f.claims,f.device,{dataSetId}),409);
  const f2=await fixture(), absence=record('Absence');await rejected(()=>seed(f2,[absence]),422);
});
test('paginated bootstrap preserves more than one page and hides private history',async()=>{
  const f=await fixture();const records=Array.from({length:205},(_,i)=>record('Employee',guid(),{PersonnelNumber:'SYN-'+i}));
  records.push(record('AuditLog'));await seed(f,records);
  const first=await store.bootstrap(f.user(3,'Beobachter'),f.device,{});assert.equal(first.records.length,199);assert.ok(first.after);
  const second=await store.bootstrap(f.user(3,'Beobachter'),f.device,{after:first.after,dataSetId:first.dataSetId});assert.equal(first.records.length+second.records.length,205);
});
test('stale writes are rejected and retries are idempotent with the same immutable operation',async()=>{
  const f=await fixture(), employee=record('Employee'),dataSetId=await seed(f,[employee]);
  employee.baseVersion=1;employee.data.LastName='First';const request={dataSetId,operationId:guid(),records:[employee]};
  const result=await store.apply(f.claims,f.device,request);assert.equal(result.records[0].version,2);
  assert.deepEqual(await store.apply(f.claims,f.device,request),result);
  await rejected(()=>store.apply(f.claims,f.device,{...request,operationId:guid()}),409);
  await rejected(()=>store.apply(f.claims,f.device,{...request,records:[{...employee,data:{...employee.data,LastName:'Changed retry'}}]}),409);
});
test('simultaneous edits cannot silently overwrite each other',async()=>{
  const f=await fixture(), employee=record('Employee'),dataSetId=await seed(f,[employee]);
  const requests=['A','B'].map(name=>store.apply(f.claims,f.device,{dataSetId,operationId:guid(),records:[{...employee,baseVersion:1,data:{...employee.data,LastName:name}}]}));
  const results=await Promise.allSettled(requests);assert.equal(results.filter(r=>r.status==='fulfilled').length,1);assert.equal(results.find(r=>r.status==='rejected').reason.status,409);
});
test('roles, foreign references and unique identifiers are enforced at the server',async()=>{
  const f=await fixture(), employee=record('Employee'),dataSetId=await seed(f,[employee]);
  await rejected(()=>store.apply(f.user(3,'Beobachter'),f.device,{dataSetId,operationId:guid(),records:[record('Employee')]}),403);
  await rejected(()=>store.apply(f.claims,f.device,{dataSetId,operationId:guid(),records:[record('Employee',guid(),{PersonnelNumber:employee.data.PersonnelNumber})]}),422);
  await rejected(()=>store.apply(f.claims,f.device,{dataSetId,operationId:guid(),records:[record('Absence')]}),422);
  await rejected(()=>store.apply(f.claims,f.device,{dataSetId,operationId:guid(),records:[record('AuditLog')]}),403);
});
test('personal delivery and read receipts are isolated, paginated and exactly once',async()=>{
  const f=await fixture(), request={operationId:guid(),recipientUserId:2,subject:'Test',body:'Synthetic body',priority:'Wichtig'};
  const message=await store.sendMessage(f.claims,request);assert.equal(message.senderUserId,1);
  assert.deepEqual(await store.sendMessage(f.claims,request),message);
  const receiver=await store.listMessages(f.user(2,'Planer'),{after:0});assert.equal(receiver.messages.length,1);
  assert.equal((await store.listMessages(f.user(3,'Beobachter'),{after:0})).messages.length,0);
  await rejected(()=>store.acknowledge(f.user(3,'Beobachter'),{messageId:message.id}),404);
  const ack=await store.acknowledge(f.user(2,'Planer'),{messageId:message.id});assert.deepEqual(await store.acknowledge(f.user(2,'Planer'),{messageId:message.id}),ack);
  const sender=await store.listMessages(f.claims,{after:1});assert.equal(sender.messages.length,1);assert.equal(sender.messages[0].acknowledgedAtUtc,ack.acknowledgedAtUtc);
});
test('company, password, role and license revocation reject previously issued credentials',async()=>{
  const f=await fixture(), foreign=await fixture();await seed(f);await seed(foreign);
  await rejected(()=>store.bootstrap(foreign.claims,f.device,{}),403);
  await f.ref.collection('authUsers').doc('1').set({isActive:false},{merge:true});await rejected(()=>store.listMessages(f.claims,{after:0}),401);
  await f.ref.collection('authUsers').doc('1').set({isActive:true,credentialVersion:'v2'},{merge:true});await rejected(()=>store.listMessages(f.claims,{after:0}),401);
  await f.ref.collection('authUsers').doc('1').set({credentialVersion:'v1',role:'Beobachter'},{merge:true});await rejected(()=>store.listMessages(f.claims,{after:0}),401);
  await f.ref.collection('authUsers').doc('1').set({role:'Administrator'},{merge:true});await db.collection('licenses').doc(f.device.installationId).set({status:'suspended'},{merge:true});await rejected(()=>store.listMessages(f.claims,{after:0}),403);
});
test('cloud user changes activate a complete new set and revoke stale passwords',async()=>{
  const f=await fixture();await seed(f);
  const result=await store.manageUser(f.claims,f.device,{sourceUserId:2,username:'synthetic-2',displayName:'Updated',role:'Planer',isActive:true,password:'New-Synthetic-Password1',expectedUserSetVersion:'legacy'});
  assert.equal(result.users.length,3);assert.ok(!JSON.stringify(result).includes('passwordHash'));
  await rejected(()=>store.listMessages(f.user(2,'Planer'),{after:0}),401);
  await rejected(()=>store.manageUser(f.claims,f.device,{sourceUserId:3,username:'synthetic-3',displayName:'Changed',role:'Planer',isActive:true,expectedUserSetVersion:'legacy'}),409);
});
test('two clients cannot create overlapping assignments with different row IDs',async()=>{
  const f=await fixture(), employee=record('Employee'), workstation=record('Workstation'),shift=record('Shift',guid(),{StartTime:'06:00:00',EndTime:'14:00:00'});
  const rule=record('WorkstationShiftRule',guid(),{WorkstationId:workstation.id,ShiftId:shift.id});
  const dataSetId=await seed(f,[employee,workstation,shift,rule]);
  const make=()=>store.apply(f.claims,f.device,{dataSetId,operationId:guid(),records:[record('PlanningAssignment',guid(),{EmployeeId:employee.id,WorkstationId:workstation.id,ShiftId:shift.id,StartTime:'06:00:00',EndTime:'14:00:00'})]});
  const results=await Promise.allSettled([make(),make()]);assert.equal(results.filter(r=>r.status==='fulfilled').length,1);assert.equal(results.find(r=>r.status==='rejected').reason.status,422);
});
test('SQL-compatible cascades remove untracked dependants in one cloud transaction',async()=>{
  const f=await fixture(), employee=record('Employee'),absence=record('Absence',guid(),{EmployeeId:employee.id});const dataSetId=await seed(f,[employee,absence]);
  const result=await store.apply(f.claims,f.device,{dataSetId,operationId:guid(),records:[{kind:'Employee',id:employee.id,baseVersion:1,deleted:true,data:{}}]});
  assert.equal(result.records.filter(r=>r.deleted).length,2);
});
test('historical private messages are visible only to their participants and can be acknowledged',async()=>{
  const f=await fixture(), historic=record('UserMessage',guid(),{SenderUserId:1,RecipientUserId:2,Priority:'Normal'});await seed(f,[historic]);
  assert.equal((await store.bootstrap(f.user(3,'Beobachter'),f.device,{})).records.length,0);
  const own=await store.bootstrap(f.user(2,'Planer'),f.device,{});assert.equal(own.records.length,1);
  await store.acknowledge(f.user(2,'Planer'),{messageId:historic.id});assert.equal((await store.listMessages(f.claims,{after:0})).messages[0].id,historic.id);
  await rejected(()=>store.getRecords(f.user(2,'Planer'),f.device,{keys:[{kind:'UserMessage',id:historic.id}]}),400);
});
test('protocol allowlists reject inherited JavaScript property names',async()=>{
  const f=await fixture(),dataSetId=await seed(f);
  await rejected(()=>store.apply(f.claims,f.device,{dataSetId,operationId:guid(),records:[{kind:'constructor',id:guid(),baseVersion:0,deleted:true,data:{}}]}),400);
  const r=record('Employee');Object.defineProperty(r.data,'__proto__',{value:'unexpected',enumerable:true});
  await rejected(()=>store.apply(f.claims,f.device,{dataSetId,operationId:guid(),records:[r]}),400);
});
