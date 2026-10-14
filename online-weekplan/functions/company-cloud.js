'use strict';
const { onRequest } = require('firebase-functions/v2/https');
const { getFirestore } = require('firebase-admin/firestore');
const { getAuth } = require('firebase-admin/auth');
const { CompanyCloudStore } = require('./company-cloud-store');
const { CloudError } = require('./cloud-validation');
const store = new CompanyCloudStore(getFirestore());
const routes = {
  cloudCompanyJoin: ['join', false], cloudImportBegin:['beginImport',true], cloudImportPage:['importPage',true], cloudImportComplete:['completeImport',true],
  cloudGetRecords:['getRecords',true], cloudManageUser:['manageUser',true], cloudCompanyUpdate:['companyUpdate',true],
  cloudBootstrap:['bootstrap',true], cloudPullChanges:['pull',true], cloudApplyChanges:['apply',true],
  cloudMessageRecipients:['recipients',false], cloudMessageList:['listMessages',false], cloudMessageSend:['sendMessage',false], cloudMessageAcknowledge:['acknowledge',false]
};
for (const [name, [method, needsDevice]] of Object.entries(routes)) exports[name] = onRequest({ region:'europe-west1', timeoutSeconds:540, memory:'1GiB', maxInstances:20 }, async (req,res) => {
  res.set('Access-Control-Allow-Origin','*'); res.set('Access-Control-Allow-Headers','Content-Type, Authorization, X-SolutionCompakt-Device, X-SolutionCompakt-Secret'); res.set('Access-Control-Allow-Methods','POST, OPTIONS');
  if (req.method === 'OPTIONS') return res.status(204).send('');
  if (req.method !== 'POST') return res.status(405).json({ error:'POST erforderlich.' });
  try {
    const header = String(req.headers.authorization || '');
    if (!header.startsWith('Bearer ')) throw new CloudError(401,'Anmeldung erforderlich.');
    const claims = await getAuth().verifyIdToken(header.slice(7),true);
    const device = needsDevice ? { installationId: String(req.headers['x-solutioncompakt-device'] || ''), secret:String(req.headers['x-solutioncompakt-secret'] || '') } : null;
    if (device && (!/^[A-Za-z0-9_-]{20,100}$/.test(device.installationId) || !/^[A-Za-z0-9_-]{30,120}$/.test(device.secret))) throw new CloudError(403,'Gerätezuordnung erforderlich.');
    const result = needsDevice ? await store[method](claims,device,req.body || {}) : await store[method](claims,req.body || {});
    res.json(result);
  } catch(error) {
    const status = error.status || (String(error.code || '').startsWith('auth/') ? 401 : 500);
    if (status === 500) console.error('Cloud operation failed:', name, error.code || error.name);
    res.status(status).json({ error:status === 500 ? 'Cloud-Vorgang fehlgeschlagen. Bitte erneut versuchen.' : error.message, details:error.details || null });
  }
});
