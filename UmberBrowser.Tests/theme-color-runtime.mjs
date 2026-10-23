import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

const source = readFileSync(new URL('../Resources/Scripts/theme-color.js',import.meta.url),'utf8');

function page(enabled = true) {
    let now = 1000, canvases = 0, styleReads = 0, observers = 0, disconnected = 0;
    let color = '#1e2832', dark = false;
    const listeners = new Map(), mediaListeners = new Set(), timers = new Map(), messages = [];
    let timerId = 0;
    const media = { get matches() { return dark; }, addEventListener:(_,fn)=>mediaListeners.add(fn), removeEventListener:(_,fn)=>mediaListeners.delete(fn) };
    const document = { visibilityState:'visible', head:{}, body:{}, documentElement:{},
        hasFocus:()=>true, elementsFromPoint:()=>[], querySelectorAll:()=>[],
        createElement:tag=>{
            assert.equal(tag,'canvas'); canvases++;
            const ctx = { fillStyle:'#000000', clearRect() {}, fillRect() { this.pixel = this.fillStyle; },
                getImageData() { const hex = this.pixel.slice(1); return {data:[0,2,4].map(index=>parseInt(hex.slice(index,index+2),16))}; } };
            return {getContext:()=>ctx};
        } };
    const window = { chrome:{webview:{postMessage:value=>messages.push(value)}}, innerWidth:1000, matchMedia:()=>media,
        getComputedStyle:()=>{ styleReads++; return {backgroundColor:color}; },
        addEventListener:(event,fn)=>{ if (!listeners.has(event)) listeners.set(event,new Set()); listeners.get(event).add(fn); },
        removeEventListener:(event,fn)=>listeners.get(event)?.delete(fn) };
    const context = vm.createContext({window,document,Date:{now:()=>now},
        setTimeout:fn=>{ timers.set(++timerId,fn); return timerId; }, clearTimeout:id=>timers.delete(id),
        MutationObserver:class { constructor() { observers++; } observe() {} disconnect() { disconnected++; } } });
    const run = value=>vm.runInContext(source.replaceAll('__STRIDE_THEME_ENABLED__',String(value)),context);
    const fire = event=>{ now += 300; for (const fn of listeners.get(event) || []) fn(); };
    run(enabled);
    return { messages, document, window, run, fire, timers,
        color:value=>{color=value;}, scheme:()=>{dark=!dark; now+=300; mediaListeners.forEach(fn=>fn());},
        stats:()=>({canvases,styleReads,observers,disconnected,listeners:[...listeners.values()].reduce((count,set)=>count+set.size,0),mediaListeners:mediaListeners.size}) };
}

test('identical scroll samples post once and reuse the canvas',()=>{
    const view = page();
    for (let index=0; index<8; index++) view.fire('scroll');
    assert.deepEqual(view.messages,['THEME_COLOR:#1e2832']);
    assert.equal(view.stats().canvases,1);
    view.color('#123456'); view.fire('scroll');
    view.color('transparent'); view.fire('scroll'); view.fire('scroll');
    assert.deepEqual(view.messages,['THEME_COLOR:#1e2832','THEME_COLOR:#123456','THEME_COLOR:']);
});

test('disabled bootstrap does no extraction and live toggles stop and restart work',()=>{
    const view = page(false);
    assert.equal(view.stats().styleReads,0);
    assert.equal(view.stats().observers,0);
    assert.equal(view.stats().listeners,0);
    view.window.__strideThemeColor.setEnabled(true);
    assert.deepEqual(view.messages,['THEME_COLOR:#1e2832']);
    view.window.__strideThemeColor.setEnabled(false);
    const reads = view.stats().styleReads;
    view.fire('scroll'); view.scheme();
    assert.equal(view.stats().styleReads,reads);
    assert.equal(view.stats().listeners,0);
    assert.equal(view.stats().mediaListeners,0);
    assert.equal(view.stats().disconnected,1);
    assert.equal(view.timers.size,0);
    view.window.__strideThemeColor.setEnabled(true);
    assert.deepEqual(view.messages,['THEME_COLOR:#1e2832','THEME_COLOR:#1e2832']);
});

test('hidden pages defer extraction and color scheme changes trigger a fresh sample',()=>{
    const view = page();
    view.document.visibilityState='hidden'; view.color('#ffffff'); view.fire('scroll');
    assert.equal(view.messages.length,1);
    view.document.visibilityState='visible'; view.fire('visibilitychange');
    view.color('#121212'); view.scheme();
    assert.deepEqual(view.messages,['THEME_COLOR:#1e2832','THEME_COLOR:#ffffff','THEME_COLOR:#121212']);
});

test('duplicate bootstrap does not add observers and a new document sends its own first value',()=>{
    const view = page();
    view.run(true);
    assert.equal(view.stats().observers,1);
    assert.equal(view.messages.length,1);
    const next = page();
    assert.deepEqual(next.messages,['THEME_COLOR:#1e2832']);
});
