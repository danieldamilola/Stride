import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';

const script = readFileSync(new URL('../Resources/Scripts/internal-page.js', import.meta.url), 'utf8');

test('added headings get keyboard access and expansion updates do not scan the document', ()=>{
    let ready, observer, documentQueries = 0, bodyQueries = 0;
    const body = { querySelectorAll:()=>{ bodyQueries++; return []; }, children:[] };
    const context = vm.createContext({
        document:{ body, querySelector:()=>{ documentQueries++; return null; },
            addEventListener:(event,handler)=>{ if (event === 'DOMContentLoaded') ready = handler; } },
        MutationObserver:class { constructor(callback) { observer = callback; } observe() {} }
    });
    vm.runInContext(script,context);
    ready();
    const initialDocumentQueries = documentQueries, initialBodyQueries = bodyQueries;
    let collapsed = true, clicks = 0;
    const attributes = {}, handlers = {};
    const parent = { matches:selector=>selector.includes('.date-section'),
        classList:{ contains:()=>collapsed }, querySelector:()=>heading };
    const heading = { nodeType:1, isConnected:true, dataset:{}, parentElement:parent,
        nextElementSibling:{ id:'' },
        matches:selector=>selector.split(',').some(part=>part === '.date-header'),
        querySelectorAll:()=>[], querySelector:()=>null, closest:()=>null,
        setAttribute:(name,value)=>attributes[name]=value,
        addEventListener:(event,handler)=>handlers[event]=handler,
        click:()=>clicks++ };
    observer([{ type:'childList', addedNodes:[heading] }]);
    assert.equal(heading.tabIndex,0);
    assert.equal(attributes.role,'button');
    assert.equal(attributes['aria-expanded'],'false');
    assert.ok(attributes['aria-controls']);
    handlers.keydown({ key:'Enter', preventDefault() {} });
    assert.equal(clicks,1);
    collapsed = false;
    observer([{ type:'attributes', target:parent }]);
    assert.equal(attributes['aria-expanded'],'true');
    assert.equal(documentQueries,initialDocumentQueries);
    assert.equal(bodyQueries,initialBodyQueries);
});
