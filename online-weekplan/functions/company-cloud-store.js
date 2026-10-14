'use strict';
const crypto = require('node:crypto');
const { userCollection } = require('./snapshot-store');
const { CloudError, schema, id, validateRecord, uniqueKeys, messageInput } = require('./cloud-validation');
const hash = s => crypto.createHash('sha256').update(String(s)).digest('hex');
const uuid = () => crypto.randomUUID().replaceAll('-', '');
const iso = x => x?.toDate?.().toISOString() || x || null;
class CompanyCloudStore {
  constructor(db) { this.db = db; }
  async authorize(claims, device, tx) {
    const read = ref => tx ? tx.get(ref) : ref.get();
    if (!claims || !/^[A-Za-z0-9_-]{16,100}$/.test(claims.companyId || '') || !Number.isSafeInteger(claims.sourceUserId) || claims.sourceUserId < 1) throw new CloudError(401, 'Bitte erneut anmelden.');
    const ref = this.db.collection('companies').doc(claims.companyId);
    const snap = await read(ref); const company = snap.data();
    if (!snap.exists || company.isActive === false || !company.licenseInstallationId) throw new CloudError(403, 'Firma ist nicht freigeschaltet.');
    const license = (await read(this.db.collection('licenses').doc(company.licenseInstallationId))).data();
    const until = license?.validUntil?.toDate?.();
    if (license?.status !== 'active' || !until || until <= new Date()) throw new CloudError(403, 'Firmenlizenz ist gesperrt oder abgelaufen.');
    const user = (await read(userCollection(ref, company).doc(String(claims.sourceUserId)))).data();
    if (!user || user.isActive !== true || user.role !== claims.role || (user.credentialVersion || 'legacy') !== (claims.credentialVersion || 'legacy')) throw new CloudError(401, 'Benutzerzugang wurde geändert. Bitte erneut anmelden.');
    if (device) {
      const d = (await read(ref.collection('devices').doc(device.installationId))).data();
      if (!d || d.isActive === false || d.secretHash !== hash(device.secret)) throw new CloudError(403, 'Gerät ist nicht mit dieser Firma verbunden.');
    }
    return { ref, company, user, claims, licenseUntil: until.toISOString() };
  }
  admin(access) { if (access.user.role !== 'Administrator') throw new CloudError(403, 'Administrator erforderlich.'); }
  writable(access) { if (!['Administrator', 'Planer'].includes(access.user.role)) throw new CloudError(403, 'Keine Schreibberechtigung.'); }
  dataset(access) {
    if (!id(access.company.activeDataSet || '')) throw new CloudError(409, 'Die Firma muss ihre Daten zunächst in der App mit Firebase verbinden.');
    return access.ref.collection('dataSets').doc(access.company.activeDataSet);
  }
  async profiles(access) {
    return (await userCollection(access.ref, access.company).get()).docs.map(s => {
      const u = s.data(); return { id: u.sourceUserId, username: u.username, displayName: u.displayName || u.username, role: u.role, isActive: u.isActive === true };
    });
  }
  async join(claims, input) {
    if (!/^[A-Za-z0-9_-]{20,100}$/.test(input.installationId || '') || !/^[A-Za-z0-9_-]{30,120}$/.test(input.secret || '')) throw new CloudError(400, 'Ungültige Gerätekennung.');
    const access = await this.db.runTransaction(async tx => {
      const a = await this.authorize(claims, null, tx);
      const ref = a.ref.collection('devices').doc(input.installationId); const old = (await tx.get(ref)).data();
      if (old && (old.secretHash !== hash(input.secret) || old.isActive === false)) throw new CloudError(403, 'Gerät konnte nicht bestätigt werden.');
      tx.set(ref, { secretHash: hash(input.secret), isActive: true, lastUserId: claims.sourceUserId, lastSeenAtUtc: new Date().toISOString() }, { merge: true });
      return a;
    });
    return { companyId: claims.companyId, companyCode: access.company.companyCode, companyName: access.company.companyName, siteName: access.company.siteName || "", userSetVersion: access.company.activeUserVersion || "legacy", sourceUserId: claims.sourceUserId, username: access.user.username, displayName: access.user.displayName || access.user.username, role: access.user.role, licenseValidUntilUtc: access.licenseUntil, credentialVersion: access.user.credentialVersion || "legacy", dataSetId: access.company.activeDataSet || null, users: await this.profiles(access) };
  }
  async beginImport(claims, device, input) {
    const access = await this.authorize(claims, device); this.admin(access);
    if (access.company.activeDataSet) throw new CloudError(409, 'Diese Firma hat bereits gemeinsame Daten.');
    if (!Number.isSafeInteger(input.entryCount) || input.entryCount < 0 || input.entryCount > 1000000) throw new CloudError(400, 'Ungültige Datenmenge.');
    const dataSetId = uuid();
    await access.ref.collection('dataSets').doc(dataSetId).set({ status: 'uploading', expectedCount: input.entryCount, ownerUserId: claims.sourceUserId, createdAtUtc: new Date().toISOString() });
    return { dataSetId };
  }
  async importPage(claims, device, input) {
    const access = await this.authorize(claims, device); this.admin(access);
    if (!id(input.dataSetId) || !Array.isArray(input.records) || input.records.length > 100) throw new CloudError(400, 'Ungültiger Upload.');
    input.records.forEach(validateRecord);
    const ds = access.ref.collection('dataSets').doc(input.dataSetId);
    await this.db.runTransaction(async tx => {
      const a = await this.authorize(claims, device, tx); this.admin(a);
      const state = (await tx.get(ds)).data();
      if (!state || state.status !== 'uploading' || state.ownerUserId !== claims.sourceUserId || a.company.activeDataSet) throw new CloudError(409, 'Import ist nicht mehr verfügbar.');
      for (const r of input.records) tx.set(ds.collection('records').doc(r.kind + '_' + r.id), { ...r, version: 1, updatedAtUtc: new Date().toISOString(), references: this.references(r),dayKeys:this.dayKeys(r) });
    });
    return { ok: true };
  }
  references(r) { return r.deleted ? [] : Object.entries(schema[r.kind].fields).filter(([k, f]) => f.reference && k !== 'WorkstationIdSnapshot' && r.data[k]).map(([k, f]) => f.reference + '_' + r.data[k]); }
  dayKeys(r) {
    if(r.deleted || !['PlanningAssignment','Absence'].includes(r.kind))return [];
    const first=String(r.data.Date||r.data.StartDate).slice(0,10), last=String(r.data.EndDate||r.data.Date).slice(0,10);
    const start=Date.parse(first+'T00:00:00Z'),end=Date.parse(last+'T00:00:00Z');
    if(end<start||end-start>3650*86400000)throw new CloudError(422,'Ungültiger Planungszeitraum.');
    const keys=[];for(let t=start;t<=end+(r.kind==='PlanningAssignment'?86400000:0);t+=86400000)keys.push(r.data.EmployeeId+'_'+new Date(t).toISOString().slice(0,10));return keys;
  }
  interval(r) {
    const date=Date.parse(String(r.data.Date).slice(0,10)+'T00:00:00Z');
    const time=value=>{const match=String(value).match(/^(?:(\d+)\.)?(\d+):(\d+):(\d+)/);return match?(Number(match[1]||0)*86400+Number(match[2])*3600+Number(match[3])*60+Number(match[4]))*1000:NaN;};
    const start=date+time(r.data.StartTime);let end=date+time(r.data.EndTime);if(end<=start)end+=86400000;return [start,end];
  }
  async validateScheduling(tx,ds,changes) {
    const locked=new Map();
    for(const r of changes.values())for(const day of this.dayKeys(r)){const key=day.slice(0,40);if(!locked.has(key))locked.set(key,(await tx.get(ds.collection('dayLocks').doc(key))).data()?.version||0);}
    if(locked.size>200)throw new CloudError(422,'Bitte den Planungsauftrag in kleinere Abschnitte aufteilen.');
    for(const r of changes.values()) {
      if(r.deleted||r.kind!=='PlanningAssignment')continue;
      const [start,end]=this.interval(r),keys=this.dayKeys(r);
      if(!Number.isFinite(start)||!Number.isFinite(end))throw new CloudError(422,'Ungültige Planungszeit.');
      const found=await tx.get(ds.collection('records').where('dayKeys','array-contains-any',keys));
      const candidates=new Map(found.docs.map(d=>[d.id,d.data()]));for(const [key,c] of changes)candidates.set(key,c);
      for(const [key,c] of candidates) {
        if(c.deleted||c.data.EmployeeId!==r.data.EmployeeId||key===r.kind+'_'+r.id)continue;
        if(c.kind==='PlanningAssignment'){const [a,b]=this.interval(c);if(start<b&&a<end)throw new CloudError(422,'Mitarbeiter ist bereits in einer überschneidenden Planung eingetragen.');}
        if(c.kind==='Absence'){const a=Date.parse(c.data.StartDate.slice(0,10)+'T00:00:00Z'),b=Date.parse(c.data.EndDate.slice(0,10)+'T00:00:00Z')+86400000;if(start<b&&a<end)throw new CloudError(422,'Mitarbeiter ist in diesem Zeitraum abwesend.');}
      }
      const related=async(kind,id)=>changes.get(kind+'_'+id)||(await tx.get(ds.collection('records').doc(kind+'_'+id))).data();
      const employee=await related('Employee',r.data.EmployeeId),workstation=await related('Workstation',r.data.WorkstationId);
      if(!employee?.data.IsActive||employee.data.IsDeleted||!workstation?.data.IsActive)throw new CloudError(422,'Mitarbeiter oder Arbeitsplatz ist nicht aktiv.');
      if(workstation.data.RequiredQualificationId&&workstation.data.RequiredQualificationLevel>0){
        const dummy={kind:'EmployeeQualification',deleted:false,data:{EmployeeId:r.data.EmployeeId,QualificationId:workstation.data.RequiredQualificationId}};
        const pending=[...changes.values()].find(c=>!c.deleted&&c.kind==='EmployeeQualification'&&c.data.EmployeeId===r.data.EmployeeId&&c.data.QualificationId===workstation.data.RequiredQualificationId);
        const owner=(await tx.get(ds.collection('unique').doc(uniqueKeys(dummy)[0]))).data()?.owner;
        const level=pending?.data.Level|| (owner?(await tx.get(ds.collection('records').doc(owner))).data()?.data.Level:0);
        if(level<workstation.data.RequiredQualificationLevel)throw new CloudError(422,'Die erforderliche Qualifikation fehlt.');
      }
      if(r.data.ShiftId){
        const rules=await tx.get(ds.collection('records').where('references','array-contains','Workstation_'+r.data.WorkstationId).where('kind','==','WorkstationShiftRule'));
        const ruleMap=new Map(rules.docs.map(d=>[d.id,d.data()]));for(const [key,c]of changes)if(c.kind==='WorkstationShiftRule')ruleMap.set(key,c);
        const day=['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday'][new Date(start).getUTCDay()];
        if(![...ruleMap.values()].some(c=>!c.deleted&&c.data.WorkstationId===r.data.WorkstationId&&c.data.ShiftId===r.data.ShiftId&&c.data[day]))throw new CloudError(422,'Schicht ist für diesen Arbeitsplatz und Tag nicht freigegeben.');
        const closed=await tx.get(ds.collection('records').where('kind','==','OperatingCalendarDay').where('data.Date','==',r.data.Date));if(closed.docs.some(d=>!d.data().deleted&&!d.data().data.IsWorkingDay))throw new CloudError(422,'Der Betriebstag ist geschlossen.');
      }
    }
    return locked;
  }
  async completeImport(claims, device, input) {
    const a = await this.authorize(claims, device); this.admin(a);
    if (!id(input.dataSetId)) throw new CloudError(400, 'Ungültiger Import.');
    const ds = a.ref.collection('dataSets').doc(input.dataSetId);
    const state = await this.db.runTransaction(async tx => {
      const current = await this.authorize(claims, device, tx); this.admin(current);
      const value = (await tx.get(ds)).data();
      if (!value || !['uploading','validating'].includes(value.status) || value.ownerUserId !== claims.sourceUserId || current.company.activeDataSet) throw new CloudError(409,'Import ist nicht verfügbar.');
      tx.set(ds,{ status:'validating' },{ merge:true });return value;
    });
    const keys = new Set(); let recordCount=0;
    for await(const snapshot of ds.collection('records').stream()) { keys.add(snapshot.id);recordCount++; }
    if(recordCount!==state.expectedCount)throw new CloudError(409,'Import ist noch unvollständig.');
    const unique=new Set();let batch=this.db.batch(),count=0;
    for await(const snapshot of ds.collection('records').stream()) {
      const r=snapshot.data();validateRecord(r);
      if(this.references(r).some(k=>!keys.has(k)))throw new CloudError(422,'Import enthält fehlende Datenbeziehungen.');
      for(const key of uniqueKeys(r)) {
        if(unique.has(key))throw new CloudError(422,'Import enthält doppelte Kennungen.');unique.add(key);
        batch.set(ds.collection('unique').doc(key),{owner:snapshot.id});
        if(++count%300===0){await batch.commit();batch=this.db.batch();}
      }
    }
    if(count%300)await batch.commit();
    await this.db.runTransaction(async tx => {
      const current = await this.authorize(claims, device, tx); this.admin(current);
      const currentStage = (await tx.get(ds)).data();
      if (current.company.activeDataSet || currentStage?.status !== 'validating') throw new CloudError(409, 'Import wurde bereits veröffentlicht.');
      tx.set(ds, { status: 'active', completedAtUtc: new Date().toISOString(), sequence: 0 }, { merge: true });
      tx.set(current.ref, { activeDataSet: input.dataSetId, cloudDataEnabled: true }, { merge: true });
    });
    return { dataSetId: input.dataSetId };
  }
  async bootstrap(claims, device, input) {
    const a = await this.authorize(claims, device), ds = this.dataset(a);
    if (input.dataSetId && input.dataSetId !== ds.id) throw new CloudError(409, 'Der gemeinsame Datenstand wurde gewechselt.');
    const sequence = (await ds.get()).data().sequence || 0;
    let query = ds.collection('records').orderBy('__name__').limit(200);
    if (input.after) { if (!/^[A-Za-z]+_[a-f0-9]{32}$/.test(input.after)) throw new CloudError(400, 'Ungültiger Cursor.'); query = query.startAfter(input.after); }
    const page = await query.get();
    return { dataSetId: ds.id, sequence, records: page.docs.map(x => x.data()).filter(r => (r.kind !== 'AuditLog' || a.user.role === 'Administrator') && (r.kind !== 'UserMessage' || r.data.SenderUserId === claims.sourceUserId || r.data.RecipientUserId === claims.sourceUserId)), userSetVersion: a.company.activeUserVersion || 'legacy', after: page.size === 200 ? page.docs.at(-1).id : null, users: input.after ? [] : await this.profiles(a) };
  }
  async pull(claims, device, input) {
    const a = await this.authorize(claims, device), ds = this.dataset(a);
    if (input.dataSetId !== ds.id) throw new CloudError(409, 'Der gemeinsame Datenstand wurde gewechselt.');
    if (!Number.isSafeInteger(input.after) || input.after < 0) throw new CloudError(400, 'Ungültiger Cursor.');
    const page = await ds.collection('changes').where('sequence', '>', input.after).orderBy('sequence').limit(200).get();
    const entries = page.docs.map(x => x.data());
    if (page.size === 200) {
      const tail = await ds.collection('changes').where('sequence','>',entries.at(-1).sequence).orderBy('sequence').limit(100).get();
      for (const doc of tail.docs) { if (doc.data().operationId !== entries.at(-1).operationId) break; entries.push(doc.data()); }
    }
    return { records: entries.filter(x => x.kind !== 'AuditLog' || a.user.role === 'Administrator'), sequence: entries.at(-1)?.sequence || input.after, hasMore: page.size === 200, users: input.userSetVersion === (a.company.activeUserVersion || 'legacy') ? [] : await this.profiles(a), userSetVersion: a.company.activeUserVersion || 'legacy', licenseValidUntilUtc: a.licenseUntil };
  }
  async apply(claims, device, input) {
    if (!id(input.operationId) || !Array.isArray(input.records) || !input.records.length || input.records.length > 50) throw new CloudError(400, 'Ungültiger Änderungsauftrag.');
    input.records.forEach(validateRecord);
    if (input.records.some(r => ['AuditLog','UserMessage'].includes(r.kind))) throw new CloudError(403,'Diese Daten dürfen nicht direkt geändert werden.');
    if (new Set(input.records.map(r => r.kind + '_' + r.id)).size !== input.records.length) throw new CloudError(400, 'Doppelte Datensätze im Auftrag.');
    return this.db.runTransaction(async tx => {
      const a = await this.authorize(claims, device, tx); this.writable(a); const ds = this.dataset(a);
      if (input.dataSetId !== ds.id) throw new CloudError(409, 'Der gemeinsame Datenstand wurde gewechselt.');
      const receipt = ds.collection('operations').doc(input.operationId), existingReceipt = (await tx.get(receipt)).data();
      const fingerprint = hash(JSON.stringify(input.records));
      if (existingReceipt) { if (existingReceipt.userId !== claims.sourceUserId || existingReceipt.fingerprint !== fingerprint) throw new CloudError(409, 'Auftragskennung wurde bereits verwendet.'); return existingReceipt.result; }
      const state = (await tx.get(ds)).data(); let sequence = state.sequence || 0;
      const changes = new Map(input.records.map(r => [r.kind + '_' + r.id, r]));
      const cascade=new Set(['EmployeeQualification.EmployeeId','EmployeeQualification.QualificationId','WorkstationShiftRule.WorkstationId','Absence.EmployeeId','PlanningAssignment.EmployeeId','PlanningAssignment.WorkstationId','ProductionRunSlot.ProductionOrderId','ProductionActual.ProductionOrderId','DowntimeEntry.ProductionActualId','RoutingStep.ManufacturingRoutingId','JobCard.ProductionOrderId']);
      for(const [key,r] of changes)if(r.deleted) {
        const children=await tx.get(ds.collection('records').where('references','array-contains',key).limit(51));
        for(const child of children.docs){if(child.data().deleted||changes.has(child.id))continue;const c=child.data();const fields=Object.entries(schema[c.kind].fields).filter(([name,f])=>f.reference===r.kind&&c.data[name]===r.id);
          if(fields.every(([name])=>cascade.has(c.kind+'.'+name)))changes.set(child.id,{...c,baseVersion:c.version,deleted:true,data:{}});
          else if(fields.every(([,f])=>f.nullable)) {const data={...c.data};for(const [name]of fields)data[name]=null;changes.set(child.id,{...c,baseVersion:c.version,data});}
          else throw new CloudError(422,'Verknüpfte Datensätze verhindern das Entfernen.');
        }
        if(changes.size>50)throw new CloudError(422,'Zu viele verknüpfte Datensätze. Bitte diese zuerst entfernen.');
      }
      const current = new Map();
      for (const [key, r] of changes) {
        const old = (await tx.get(ds.collection('records').doc(key))).data(); current.set(key, old);
        if ((old?.version || 0) !== r.baseVersion) throw new CloudError(409, 'Dieser Datensatz wurde auf einem anderen PC geändert.', { kind: r.kind, id: r.id, current: old });
      }
      const locks = new Map();
      for (const [key, r] of changes) {
        for (const target of this.references(r)) {
          const related = changes.get(target) || (await tx.get(ds.collection('records').doc(target))).data();
          if (!related || related.deleted) throw new CloudError(422, 'Eine verknüpfte Zeile ist nicht mehr vorhanden.');
        }
        if (r.deleted) {
          const dependents = await tx.get(ds.collection('records').where('references', 'array-contains', key).limit(51));
          if (dependents.docs.some(d => !d.data().deleted && !changes.get(d.id)?.deleted)) throw new CloudError(422, 'Verknüpfte Datensätze müssen gemeinsam entfernt werden.');
        }
        for (const u of new Set([...uniqueKeys(current.get(key) || { deleted: true }), ...uniqueKeys(r)]))
          if (!locks.has(u)) locks.set(u, (await tx.get(ds.collection('unique').doc(u))).data());
        for (const u of uniqueKeys(r)) {
          const owner = locks.get(u)?.owner;
          if (owner && owner !== key && (!changes.has(owner) || uniqueKeys(changes.get(owner)).includes(u))) throw new CloudError(422, 'Diese Kennung ist bereits vergeben.');
        }
      }
      const dayLocks=await this.validateScheduling(tx,ds,changes);
      const now = new Date().toISOString(), result = { records: [] };
      for (const [key, r] of changes) {
        const value = { ...r, data:r.deleted ? current.get(key)?.data || r.data : r.data, version: r.baseVersion + 1, updatedAtUtc: now, references: this.references(r), dayKeys:this.dayKeys(r), operationId: input.operationId, sequence: ++sequence };
        if (!r.deleted && 'UpdatedAtUtc' in value.data) value.data = { ...value.data, UpdatedAtUtc: now };
        if (r.kind === 'ShiftHandover' && !r.deleted) {
          value.data = { ...value.data };
          if (!current.get(key)) { value.data.CreatedBy = a.user.username; value.data.CreatedAtUtc = now; }
          if (value.data.Status === 'Bestätigt' && current.get(key)?.data.Status !== 'Bestätigt') { value.data.AcknowledgedBy = a.user.username; value.data.AcknowledgedAtUtc = now; }
          if (value.data.Status === 'Erledigt' && current.get(key)?.data.Status !== 'Erledigt') { value.data.ResolvedAtUtc = now; }
        }
        tx.set(ds.collection('records').doc(key), value);
        tx.set(ds.collection('changes').doc(String(sequence).padStart(16,'0')), value);
        for (const oldKey of uniqueKeys(current.get(key) || { deleted: true })) if (!uniqueKeys(r).includes(oldKey)) tx.delete(ds.collection('unique').doc(oldKey));
        for (const u of uniqueKeys(r)) tx.set(ds.collection('unique').doc(u), { owner: key });
        result.records.push({kind:value.kind,id:value.id,baseVersion:value.baseVersion,version:value.version,deleted:value.deleted,data:value.data,updatedAtUtc:value.updatedAtUtc});
        const audit = { operationId: input.operationId, kind: 'AuditLog', id: uuid(), version: 1, sequence: ++sequence, deleted: false, updatedAtUtc: now, data: { TimestampUtc: now, Username: a.user.username, Action: r.deleted ? 'Gelöscht' : r.baseVersion ? 'Geändert' : 'Erstellt', EntityType: r.kind, EntityId: r.id, Details: 'Firebase · Auftrag ' + input.operationId } };
        tx.set(ds.collection('changes').doc(String(sequence).padStart(16,'0')), audit);
        tx.set(ds.collection('records').doc('AuditLog_' + audit.id), audit);
      }
      for(const [key,version]of dayLocks)tx.set(ds.collection("dayLocks").doc(key),{version:version+1});
      tx.set(ds, { sequence }, { merge: true });
      if(Buffer.byteLength(JSON.stringify(result))>800000)throw new CloudError(400,'Bitte die verknüpften Datensätze in kleineren Gruppen bearbeiten.');
      tx.set(receipt, { userId: claims.sourceUserId, fingerprint, result });
      return result;
    });
  }
  async getRecords(claims,device,input) {
    const a=await this.authorize(claims,device), ds=this.dataset(a);this.writable(a);
    if(!Array.isArray(input.keys)||input.keys.length>50||input.keys.some(k=>!Object.hasOwn(schema,k.kind)||['AuditLog','UserMessage'].includes(k.kind)||!id(k.id)))throw new CloudError(400,'Ungültiger Datensatz.');
    const records=[];for(const key of input.keys){const r=(await ds.collection('records').doc(key.kind+'_'+key.id)).data();records.push(r||{...key,version:0,deleted:true,data:{}});}return { records };
  }
  async manageUser(claims,device,input) {
    const a=await this.authorize(claims,device);
    if(input.ownPassword) {
      if(typeof input.currentPassword!=='string' || !a.user.passwordHash || !a.user.passwordSalt) throw new CloudError(403,'Passwortprüfung erforderlich.');
      const expected=Buffer.from(a.user.passwordHash,'base64'), actual=crypto.pbkdf2Sync(input.currentPassword,Buffer.from(a.user.passwordSalt,'base64'),150000,expected.length,'sha256');
      if(!crypto.timingSafeEqual(actual,expected))throw new CloudError(403,'Aktuelles Passwort ist falsch.');
      input={ ...input,sourceUserId:claims.sourceUserId,username:a.user.username,displayName:a.user.displayName,role:a.user.role,isActive:true };
    } else this.admin(a);
    if(input.expectedUserSetVersion!==(a.company.activeUserVersion||'legacy'))throw new CloudError(409,'Die Benutzerliste wurde geändert. Bitte neu laden.');
    const username=String(input.username||'').trim(), displayName=String(input.displayName||username).trim();
    if(username.length<3||username.length>100||displayName.length<1||displayName.length>160||!['Administrator','Planer','Beobachter'].includes(input.role)||typeof input.isActive!=='boolean'||!Number.isSafeInteger(input.sourceUserId)||input.sourceUserId<0)throw new CloudError(400,'Ungültige Benutzerdaten.');
    const users=(await userCollection(a.ref,a.company).get()).docs.map(d=>d.data());
    const old=users.find(u=>u.sourceUserId===input.sourceUserId);
    if(input.sourceUserId>0&&!old)throw new CloudError(404,'Benutzer nicht gefunden.');
    if(users.some(u=>u.sourceUserId!==input.sourceUserId&&u.usernameNormalized===username.toLowerCase()))throw new CloudError(409,'Benutzername ist bereits vergeben.');
    const user={ ...old,sourceUserId:old?.sourceUserId||Math.max(0,...users.map(u=>u.sourceUserId))+1,username,usernameNormalized:username.toLowerCase(),displayName,role:input.role,isActive:input.isActive };
    if(input.password) {
      const p=input.password;if(typeof p!=='string'||p.length<10||p.length>256||!/\p{Lu}/u.test(p)||!/\p{Ll}/u.test(p)||!/\p{Nd}/u.test(p))throw new CloudError(400,'Passwort benötigt mindestens 10 Zeichen, Groß- und Kleinbuchstaben sowie eine Zahl.');
      const salt=crypto.randomBytes(16);user.passwordSalt=salt.toString('base64');user.passwordHash=crypto.pbkdf2Sync(p,salt,150000,32,'sha256').toString('base64');user.credentialVersion=hash(user.passwordHash+':'+user.passwordSalt);
    } else if(!old)throw new CloudError(400,'Ein neuer Benutzer benötigt ein Passwort.');
    const updated=[...users.filter(u=>u.sourceUserId!==user.sourceUserId),user];
    if(!updated.some(u=>u.isActive&&u.role==='Administrator'))throw new CloudError(400,'Mindestens ein aktiver Administrator muss erhalten bleiben.');
    const version=uuid(), ref=a.ref.collection('authUserSets').doc(version);
    await ref.set({ status:'uploading',versionId:version,userCount:updated.length,createdAt:new Date(),createdAtUtc:new Date().toISOString() });
    for(let i=0;i<updated.length;i+=200){const batch=this.db.batch();for(const u of updated.slice(i,i+200))batch.set(ref.collection('users').doc(String(u.sourceUserId)),u);await batch.commit();}
    await this.db.runTransaction(async tx=> {
      const current=await this.authorize(claims,device,tx);if(!input.ownPassword)this.admin(current);
      if((current.company.activeUserVersion||'legacy')!==input.expectedUserSetVersion)throw new CloudError(409,'Benutzerliste wurde gleichzeitig geändert. Bitte neu laden.');
      if(current.company.activeUserVersion)tx.set(a.ref.collection('authUserSets').doc(current.company.activeUserVersion),{status:'superseded',supersededAt:new Date()},{merge:true});
      tx.set(a.ref,{ activeUserVersion:version },{merge:true});tx.set(ref,{ status:'active',completedAt:new Date(),completedAtUtc:new Date().toISOString() },{merge:true});
    });
    return { userSetVersion:version,users:updated.map(u=>({id:u.sourceUserId,username:u.username,displayName:u.displayName,role:u.role,isActive:u.isActive})) };
  }
  async companyUpdate(claims,device,input) {
    return this.db.runTransaction(async tx=>{const a=await this.authorize(claims,device,tx);this.admin(a);const name=String(input.companyName||'').trim(),site=String(input.siteName||'').trim();if(name.length<2||name.length>160||site.length>160)throw new CloudError(400,'Ungültige Firmendaten.');tx.set(a.ref,{companyName:name,siteName:site},{merge:true});return {ok:true};});
  }
  async recipients(claims) { const a = await this.authorize(claims); return { recipients: (await this.profiles(a)).filter(u => u.isActive && u.id !== claims.sourceUserId) }; }
  async listMessages(claims, input) {
    const a = await this.authorize(claims);
    if (!Number.isSafeInteger(input.after) || input.after < 0) throw new CloudError(400,'Ungültiger Nachrichtencursor.');
    const page = await a.ref.collection('privateFeeds').doc(String(claims.sourceUserId)).collection('events').where('sequence','>',input.after).orderBy('sequence').limit(200).get();
    return { messages:page.docs.map(d => d.data().message), sequence:page.docs.at(-1)?.data().sequence || input.after, hasMore:page.size === 200 };
  }
  async messageAuditState(tx,access) {
    if(!access.company.activeDataSet)return null;const ref=this.dataset(access),state=(await tx.get(ref)).data();return {ref,sequence:(state.sequence||0)+1};
  }
  writeMessageAudit(tx,state,access,messageId,action) {
    if(!state)return;const now=new Date().toISOString(),record={kind:'AuditLog',id:uuid(),version:1,sequence:state.sequence,operationId:messageId,deleted:false,updatedAtUtc:now,data:{TimestampUtc:now,Username:access.user.username,Action:action,EntityType:'UserMessage',EntityId:messageId,Details:'Persönlicher Hinweis'}};
    tx.set(state.ref,{sequence:state.sequence},{merge:true});tx.set(state.ref.collection('changes').doc(String(state.sequence).padStart(16,'0')),record);tx.set(state.ref.collection('records').doc('AuditLog_'+record.id),record);
  }
  async feedStates(tx, access, ids) {
    const states=[];
    for (const uid of new Set(ids)) { const ref=access.ref.collection('privateFeeds').doc(String(uid));states.push({ ref, sequence:((await tx.get(ref)).data()?.sequence || 0) + 1 }); }
    return states;
  }
  writeFeeds(tx, states, message) {
    for (const state of states) { tx.set(state.ref,{ sequence:state.sequence },{ merge:true });tx.set(state.ref.collection('events').doc(String(state.sequence).padStart(16,'0')),{ sequence:state.sequence,message }); }
  }
  async sendMessage(claims, request) {
    const input = messageInput(request);
    return this.db.runTransaction(async tx => {
      const a = await this.authorize(claims, null, tx);
      if (request.recipientUserId === claims.sourceUserId) throw new CloudError(400, 'Ein Hinweis kann nicht an das eigene Konto gesendet werden.');
      const recipient = (await tx.get(userCollection(a.ref, a.company).doc(String(request.recipientUserId)))).data();
      if (recipient?.isActive !== true) throw new CloudError(400, 'Empfänger ist nicht aktiv.');
      const ref = a.ref.collection('messages').doc(request.operationId); const old = (await tx.get(ref)).data();
      if (old) {
        if (old.senderUserId !== claims.sourceUserId || old.recipientUserId !== request.recipientUserId || old.subject !== input.subject || old.body !== input.body || old.priority !== input.priority) throw new CloudError(409, 'Nachrichtenkennung wurde bereits verwendet.');
        return { id: ref.id, ...old };
      }
      const value = { ...input, senderUserId: claims.sourceUserId, senderUsernameSnapshot: a.user.username, senderDisplayNameSnapshot: a.user.displayName || a.user.username, recipientUserId: request.recipientUserId, recipientUsernameSnapshot: recipient.username, recipientDisplayNameSnapshot: recipient.displayName || recipient.username, participantUserIds: [claims.sourceUserId, request.recipientUserId], createdAtUtc: new Date().toISOString(), acknowledgedAtUtc: null, acknowledgedByUsername: null };
      const feeds=await this.feedStates(tx,a,[claims.sourceUserId,request.recipientUserId]);
      const audit=await this.messageAuditState(tx,a);
      tx.set(ref, value);this.writeMessageAudit(tx,audit,a,ref.id,'Hinweis gesendet'); const message={ id:ref.id,...value };this.writeFeeds(tx,feeds,message);return message;
    });
  }
  async acknowledge(claims, input) {
    if (!id(input.messageId)) throw new CloudError(400, 'Ungültiger Hinweis.');
    return this.db.runTransaction(async tx => {
      const a = await this.authorize(claims, null, tx), ref = a.ref.collection('messages').doc(input.messageId);let message = (await tx.get(ref)).data();
      if (!message && a.company.activeDataSet) {
        const legacy=(await tx.get(this.dataset(a).collection('records').doc('UserMessage_' + input.messageId))).data();
        if (legacy) { message={};for (const [key,value] of Object.entries(legacy.data)) message[key[0].toLowerCase()+key.slice(1)]=value;message.participantUserIds=[message.senderUserId,message.recipientUserId]; }
      }
      if (!message || message.recipientUserId !== claims.sourceUserId) throw new CloudError(404, 'Hinweis nicht gefunden.');
      const value = { acknowledgedAtUtc: message.acknowledgedAtUtc || new Date().toISOString(), acknowledgedByUsername: message.acknowledgedByUsername || a.user.username };
      if (!message.acknowledgedAtUtc) { const feeds=await this.feedStates(tx,a,[message.senderUserId,message.recipientUserId]);const audit=await this.messageAuditState(tx,a);tx.set(ref,{ ...message,...value });this.writeFeeds(tx,feeds,{ id:ref.id,...message,...value });this.writeMessageAudit(tx,audit,a,ref.id,'Gelesen bestätigt'); }
      return { found: true, ...value };
    });
  }
}
module.exports = { CompanyCloudStore, hash, uuid, iso };
