import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';

const template = readFileSync(new URL('../Resources/Pages/History.html', import.meta.url), 'utf8');
const script = [...template.matchAll(/<script>([\s\S]*?)<\/script>/g)][0][1];

function page(entries) {
    const nodes = new Map();
    const messages = [];
    const node = id => {
        if (!nodes.has(id)) nodes.set(id, { textContent:'', innerHTML:'', disabled:false,
            addEventListener() {}, classList:{ add() {}, remove() {}, toggle() {} } });
        return nodes.get(id);
    };
    node('historyData').textContent = JSON.stringify(entries);
    const context = vm.createContext({ document:{ getElementById:node, querySelectorAll:()=>[], addEventListener() {} },
        window:{ chrome:{ webview:{ postMessage:message=>messages.push(message) } } }, URL, Intl, Date });
    vm.runInContext(script, context);
    return { context, node, messages, run:code=>vm.runInContext(code, context) };
}

const records = Array.from({ length:10000 }, (_, index)=>({ Title:'record ' + index,
    Url:'https://example.com/' + index, VisitedAt:new Date(Date.UTC(2026,0,1,0,index)).toISOString() }));

test('all matching history stays bounded and every record can be reached', ()=>{
    const view = page(records), seen = new Set();
    for (let index = 0; index < 200; index++) {
        view.run(`render('record', ${index})`);
        const urls = [...view.node('historyContent').innerHTML.matchAll(/data-url="([^"]+)"/g)].map(match=>match[1]);
        assert.equal(urls.length,50);
        urls.forEach(url=>seen.add(url));
    }
    assert.equal(seen.size,10000);
    assert.equal(view.node('nextPage').disabled,true);
    assert.equal(view.node('pageStatus').textContent,'9951 to 10000 of 10000');
});

test('search covers old records and resets page with cached metadata', ()=>{
    const view = page(records);
    view.run("getDomain = () => { throw new Error('Reparsed domain'); }; formatDate = () => { throw new Error('Reformatted date'); }; render('record 0')");
    assert.match(view.node('historyContent').innerHTML,/data-url="https:\/\/example.com\/0"/);
    assert.equal(view.node('pageStatus').textContent,'1 to 1 of 1');
    view.run("render('no results')");
    assert.match(view.node('historyContent').innerHTML,/No results found/);
    assert.equal(view.node('nextPage').disabled,true);
});

test('history opening preserves the original URL and hostile text stays escaped', ()=>{
    const url = 'https://example.com/#\'\"<script>';
    const view = page([{ Title:'<img src=x onerror=alert(1)>', Url:url, VisitedAt:records[0].VisitedAt }]);
    const html = view.node('historyContent').innerHTML;
    assert.ok(!html.includes('<img src=x onerror=alert(1)>'));
    assert.match(html,/&lt;img/);
    assert.match(html,/&#39;&quot;&lt;script&gt;/);
    view.context.original = url;
    view.run('openUrl(original)');
    assert.equal(view.messages[0], '{{IPC_TOKEN}}:history-open:' + url);
});
