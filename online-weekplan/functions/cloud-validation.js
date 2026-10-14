'use strict';
const schema = require('./cloud-schema.json');
const crypto = require('node:crypto');
class CloudError extends Error { constructor(status, message, details) { super(message); this.status = status; this.details = details; } }
const id = value => typeof value === 'string' && /^[a-f0-9]{32}$/.test(value);
function validateRecord(record) {
  if (!record || !Object.hasOwn(schema,record.kind) || !id(record.id) || !Number.isSafeInteger(record.baseVersion) || record.baseVersion < 0 || typeof record.deleted !== 'boolean') throw new CloudError(400, 'Ungültiger Datensatz.');
  if (record.deleted) return;
  if (!record.data || typeof record.data !== 'object' || Array.isArray(record.data) || Buffer.byteLength(JSON.stringify(record.data)) > 200000) throw new CloudError(400, 'Ungültige oder zu große Daten.');
  const fields = schema[record.kind].fields;
  for (const key of Object.keys(record.data)) if (!Object.hasOwn(fields,key)) throw new CloudError(400, `Nicht erlaubtes Feld: ${key}`);
  for (const [key, field] of Object.entries(fields)) {
    const v = record.data[key];
    if (v === null && field.nullable) continue;
    const valid = field.type === 'reference' ? id(v) : field.type === 'date' ? typeof v === 'string' && Number.isFinite(Date.parse(v)) : field.type === 'time' ? typeof v === 'string' && /^(-?\d+\.)?\d{2}:\d{2}:\d{2}(\.\d+)?$/.test(v) : typeof v === field.type && (field.type !== 'number' || Number.isFinite(v));
    if (field.integer && !Number.isSafeInteger(v)) throw new CloudError(400,`Ganzzahl erforderlich: ${key}`);
    if (!valid) throw new CloudError(400, `Ungültiges Feld: ${record.kind}.${key}`);
    if (typeof v === 'string' && v.length > 60000) throw new CloudError(400, 'Text ist zu lang.');
  }
  if(record.kind==='EmployeeQualification' && record.id!==crypto.createHash('sha256').update(record.kind+':'+record.data.EmployeeId+':'+record.data.QualificationId).digest('hex').slice(0,32))throw new CloudError(400,'Ungültige Qualifikationszuordnung.');
  if (record.kind === 'EmployeeQualification' && (!Number.isInteger(record.data.Level) || record.data.Level < 0 || record.data.Level > 5)) throw new CloudError(400, 'Ungültige Qualifikationsstufe.');
  if (record.kind === 'Workstation' && (record.data.MinimumStaff < 0 || record.data.OptimalStaff < record.data.MinimumStaff || record.data.MaximumStaff < record.data.OptimalStaff)) throw new CloudError(400, 'Ungültige Besetzungsgrenzen.');
  if (record.kind === 'ShiftHandover' && (!['Normal','Hoch','Kritisch'].includes(record.data.Priority) || !['Offen','Bestätigt','Erledigt'].includes(record.data.Status))) throw new CloudError(400, 'Ungültiger Übergabestatus.');
  if (record.kind === 'RecycleBinItem') {
    if (!['Employee','ProductionOrder','ProductionActual','DowntimeEntry'].includes(record.data.EntityType) || !id(record.data.EntityId)) throw new CloudError(400, 'Ungültiger Papierkorbeintrag.');
    if (record.data.SnapshotJson) { let snapshot; try { snapshot = JSON.parse(record.data.SnapshotJson); } catch { throw new CloudError(400, 'Ungültiges Archiv.'); }
      const allowed = schema[record.data.EntityType].fields;
      if (Object.keys(snapshot).some(k => k !== 'Id' && !Object.hasOwn(allowed,k))) throw new CloudError(400, 'Ungültige Archivfelder.');
    }
  }
}
function uniqueKeys(record) {
  if (record.deleted) return [];
  const d = record.data, kind = record.kind;
  const keys = { Employee: ['PersonnelNumber'], ArticleMaster: ['ArticleNumber'], ProductionOrder: ['OrderNumber'], EmployeeQualification: ['EmployeeId','QualificationId'], WorkstationShiftRule: ['WorkstationId','ShiftId'], OperatingCalendarDay: ['Date'], ProductionRunSlot: ['ProductionOrderId','SequenceNumber'], RoutingStep: ['ManufacturingRoutingId','SequenceNumber'], JobCard: ['ProductionOrderId','SequenceNumber'] }[kind];
  if (!keys || d.IsDeleted) return [];
  return [kind + '-' + crypto.createHash('sha256').update(JSON.stringify(keys.map(k => d[k]))).digest('hex')];
}
function messageInput(request) {
  if (!id(request.operationId) || !Number.isSafeInteger(request.recipientUserId) || request.recipientUserId < 1 || typeof request.body !== 'string' || !request.body.trim() || request.body.trim().length > 4000 || typeof request.subject !== 'string' || request.subject.trim().length > 120) throw new CloudError(400, 'Ungültiger Hinweis.');
  return { subject: request.subject.trim() || 'Hinweis', body: request.body.trim(), priority: request.priority === 'Wichtig' ? 'Wichtig' : 'Normal' };
}
module.exports = { CloudError, schema, id, validateRecord, uniqueKeys, messageInput };
