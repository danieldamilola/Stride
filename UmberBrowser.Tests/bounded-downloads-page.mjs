import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';

const template = readFileSync(new URL('../Resources/Pages/Downloads.html', import.meta.url), 'utf8');
const script = [...template.matchAll(/<script>([\s\S]*?)<\/script>/g)][0][1];

class Element {
    constructor() { this.children = []; this.queries = new Map(); this.dataset = {}; this.attributes = {}; this.style = {}; this.textContent = ''; this.value = ''; this.classList = { toggle() {} }; }
    querySelector(selector) { if (!this.queries.has(selector)) this.queries.set(selector,new Element()); return this.queries.get(selector); }
    addEventListener() {}
    setAttribute(name,value) { this.attributes[name] = value; }
    getAttribute(name) { return this.attributes[name]; }
    removeAttribute(name) { delete this.attributes[name]; }
    toggleAttribute() {}
    insertBefore(el,before) { el.remove(); const index = before ? this.children.indexOf(before) : this.children.length; this.children.splice(index,0,el); el.parent = this; }
    replaceChildren(...children) { this.children.forEach(child=>child.parent=null); this.children=[]; children.forEach(child=>this.insertBefore(child,null)); }
    remove() { if (this.parent) { this.parent.children.splice(this.parent.children.indexOf(this),1); this.parent = null; } }
}

function page() {
    const nodes = new Map(), messages = [];
    const node = id=>{ if (!nodes.has(id)) nodes.set(id,new Element()); return nodes.get(id); };
    const document = { hidden:false, getElementById:node, createElement:()=>new Element(), addEventListener() {} };
    const context = vm.createContext({ document, crypto:{ randomUUID:()=> 'document' },
        window:{ chrome:{ webview:{ postMessage:message=>messages.push(message), addEventListener() {} } }, addEventListener() {} },
        setTimeout:()=>1, clearTimeout() {}, Date, Intl, Map, Set });
    vm.runInContext(script,context);
    return { context, document, node, messages, run:code=>vm.runInContext(code,context),
        sync:update=>{ context.update = update; vm.runInContext('sync(update)',context); } };
}

const rows = Array.from({length:50},(_,index)=>({ id:'row-' + index, fileName:'file-' + index,
    startedAt:'2026-01-01T00:00:00Z', state:0, receivedBytes:10, totalBytes:100, canPause:true, canCancel:true }));

test('progress updates preserve existing buttons and only touch changed rows', ()=>{
    const view = page();
    view.sync({ request:'document:0', page:0, total:10000, ids:rows.map(row=>row.id), changed:rows, active:true });
    const first = view.run("itemMap.get('row-0')"), untouched = view.run("itemMap.get('row-1')");
    const button = first.querySelector('.actions').children[0];
    const original = view.run('updateItem');
    const updated = [];
    view.context.updateItem = (el,row)=>{ updated.push(row.id); original(el,row); };
    view.sync({ request:'document:0', page:0, total:10000, ids:rows.map(row=>row.id), changed:[{...rows[0],receivedBytes:30}], active:true });
    assert.deepEqual(updated,['row-0']);
    assert.equal(first.querySelector('.actions').children[0],button);
    assert.equal(view.run("itemMap.get('row-1')"),untouched);
    assert.equal(view.run('itemMap.size'),50);
    assert.equal(view.node('pageStatus').textContent,'1 to 50 of 10000');
});

test('page changes discard old rows and delayed search replies are ignored', ()=>{
    const view = page();
    view.sync({ request:'document:0', page:0, total:10000, ids:rows.map(row=>row.id), changed:rows, active:false });
    view.run('changePage(1)');
    view.sync({ request:'document:0', page:0, total:10000, ids:[], changed:[], active:false });
    assert.equal(view.run('itemMap.size'),50);
    const next = [{...rows[0],id:'last-row'}];
    view.sync({ request:'document:1', page:1, total:51, ids:['last-row'], changed:next, active:false });
    assert.equal(view.run('itemMap.size'),1);
    assert.equal(view.run('rowData.size'),1);
    assert.equal(view.node('nextPage').disabled,true);
});

test('hidden documents do not request downloads', ()=>{
    const view = page(), count = view.messages.length;
    view.document.hidden = true;
    view.run('requestSync()');
    assert.equal(view.messages.length,count);
});
