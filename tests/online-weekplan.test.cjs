const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

function harness() {
  const nodes = new Map();
  function element() {
    return { children: [], value: '', disabled: false, textContent: '',
      classList: { add() {}, remove() {}, toggle() {} },
      appendChild(child) { this.children.push(child); },
      addEventListener() {}, close() { this.closed = true; }, reset() {},
      set innerHTML(value) { this.html = value; this.children = []; },
      get innerHTML() { return this.html || ''; }
    };
  }
  const document = {
    getElementById(id) { if (!nodes.has(id)) nodes.set(id, element()); return nodes.get(id); },
    createElement: element
  };
  const context = vm.createContext({ document, console });
  const source = fs.readFileSync('online-weekplan/public/app.js', 'utf8').replace('bootstrap();', '');
  vm.runInContext(source, context);
  return { nodes, run: code => vm.runInContext(code, context), el: document.getElementById };
}

test('empty historical week uses snapshot dates, not the current date', () => {
  const h = harness();
  h.run('render([], "2030-01-14")');
  const columns = h.el('planGrid').children;
  assert.equal(columns.length, 7);
  assert.match(columns[0].innerHTML, /Montag · 14\.01\./);
  assert.match(columns[6].innerHTML, /Sonntag · 20\.01\./);
});

test('production-only week displays its production order and staffing', () => {
  const h = harness();
  h.run('render([], "2030-01-14", [{date:"2030-01-15", orderNumber:"P-1", product:"Test", requiredStaff:3, start:"06:00", end:"14:00"}])');
  const cards = h.el('planGrid').children[1].children;
  assert.equal(cards.length, 1);
  assert.match(cards[0].innerHTML, /Produktion · P-1/);
  assert.match(cards[0].innerHTML, /Personalbedarf: 3/);
});

test('employee and production text is escaped', () => {
  const h = harness();
  h.run('render([{date:"2030-01-14",employeeName:"<img onerror=x>",start:"06:00"}], "2030-01-14", [{date:"2030-01-14", product:"<script>x</script>"}])');
  const cards = h.el('planGrid').children[0].children;
  assert.match(cards[0].innerHTML, /&lt;img/);
  assert.match(cards[1].innerHTML, /&lt;script&gt;/);
  assert.ok(cards.every(card => !card.innerHTML.includes('<script>')));
});

test('logout clears tenant state, imported file and old plan', () => {
  const h = harness();
  h.run('role="Administrator"; companyId="old"; weekIds=["2030-W03"]; render([],"2030-01-14"); resetPlan()');
  assert.equal(h.run('companyId'), '');
  assert.equal(h.run('role'), '');
  assert.equal(h.run('weekIds.length'), 0);
  assert.equal(h.el('publishedMeta').textContent, '');
  assert.equal(h.el('prevWeek').disabled, true);
  assert.equal(h.el('nextWeek').disabled, true);
});

test('failed week load gives a visible message and clears the old plan', async () => {
  const h = harness();
  h.run('companyId="tenant"; modules.fsMod={doc(){},getDoc(){throw new Error("offline")}}');
  await h.run('loadWeek("2030-W03")');
  assert.match(h.el('planStatus').textContent, /konnte nicht geladen/);
  assert.equal(h.el('planGrid').children.length, 0);
});

test('failed correction preserves values and re-enables save', async () => {
  const h = harness();
  h.run('role="Administrator";companyId="tenant";weekIds=["2030-W03"];auth={currentUser:{uid:"admin"}};modules.fsMod={doc(){},serverTimestamp(){},setDoc(){throw new Error("offline")}}');
  h.el('editEmployee').value = 'Ada';
  await h.run('saveOverride({preventDefault(){}})');
  assert.equal(h.el('editEmployee').value, 'Ada');
  assert.equal(h.el('saveOverrideButton').disabled, false);
  assert.match(h.el('editStatus').textContent, /Speichern fehlgeschlagen/);
  assert.ok(!h.el('editDialog').closed);
});

test('late responses after logout cannot repaint the previous tenant', async () => {
  const h = harness();
  h.run('companyId="old"; var resolveMeta; modules.fsMod={doc(){},collection(){},getDoc(){return new Promise(resolve=>resolveMeta=resolve)},getDocs(){return Promise.resolve({docs:[]})}}');
  const pending = h.run('loadWeek("2030-W03")');
  h.run('resetPlan();resolveMeta({exists:()=>true,data:()=>({weekStart:"2030-01-14",weekEnd:"2030-01-20",isoWeek:3})})');
  await pending;
  assert.equal(h.el('weekTitle').textContent, 'Wochenplan');
  assert.equal(h.el('publishedMeta').textContent, '');
});

test('newly published first week can be discovered with refresh', async () => {
  const h = harness();
  h.run(`companyId="tenant"; modules.fsMod={
    collection(...args){return args.at(-1)},query(x){return x},orderBy(){},limit(){},doc(){},
    getDocs(x){return Promise.resolve({docs:x==="weekPlans"?[{id:"2030-W03"}]:[]})},
    getDoc(){return Promise.resolve({exists:()=>true,data:()=>({weekStart:"2030-01-14",weekEnd:"2030-01-20",isoWeek:3})})}
  }`);
  await h.run('loadWeeks()');
  assert.equal(h.run('weekIds[0]'), '2030-W03');
  assert.equal(h.el('planGrid').children.length, 7);
});
