const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs');
test('desktop and backend cloud protocols are identical and credentials are excluded',()=>{
 const a=JSON.parse(fs.readFileSync('shared/cloud-schema.json','utf8')),b=require('../online-weekplan/functions/cloud-schema.json');assert.deepEqual(a,b);
 assert.ok(a.Employee && a.ProductionOrder && a.ShiftHandover && a.UserMessage && a.AuditLog);
 assert.equal(a.UserAccount,undefined);
 for(const entity of Object.values(a))for(const field of Object.keys(entity.fields))assert.ok(!/Password|Salt|Secret|Token|Pin/i.test(field),field);
});
