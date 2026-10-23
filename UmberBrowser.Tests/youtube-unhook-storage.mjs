// Runs the exact text YouTubeUnhook generates against a stubbed YouTube document and
// reports what the page ended up with.
//
// The question the generated text cannot answer by reading it is whether the settings
// reach the page when the page refuses them. The generator writes the config to
// localStorage, and the unhook script reads window.__UMBER_UNHOOK first and
// localStorage second. A page that denies storage, blocked third party storage or a
// private window, took the empty-object path: no classes on <html>, every setting
// reading as off, and nothing anywhere reporting why. Whether that happens is decided
// by control flow in two files, so it is decided by running them.
//
//   node youtube-unhook-storage.mjs <initialScriptPath> <liveScriptPath> <mode>
//
// mode "allowed" lets the page store the config. mode "denied" makes every
// localStorage call throw, which is what a page that refuses storage does.
//
// The initial script is run first, then the live one, then a yt-navigate-finish event,
// which is the SPA navigation the unhook script listens for. The state after the first
// injection and after the live change are reported separately, so a setting that applies
// on load but not after a live change is visible, and so a document that is pointed at
// the config on the happy path is distinguishable from one that is only pointed at it
// when the write was refused.
//
// Exits 0 on success, 1 when the run threw, 2 on a bad call.

import { readFileSync } from 'node:fs';
import { createContext, runInContext } from 'node:vm';

const [initialPath, livePath, mode = 'denied'] = process.argv.slice(2);
if (!initialPath || !livePath) {
    console.error('usage: node youtube-unhook-storage.mjs <initialScriptPath> <liveScriptPath> <mode>');
    process.exit(2);
}
if (mode !== 'allowed' && mode !== 'denied') {
    console.error(`unknown mode: ${mode}`);
    process.exit(2);
}

/** The DOM the unhook script touches. Nothing here fakes its decisions, only its shape. */
function makeDocument() {
    const html = {
        nodeType: 1,
        tagName: 'HTML',
        classes: new Set(),
        classList: {
            add(name) { html.classes.add(name); },
            remove(name) { html.classes.delete(name); },
            contains(name) { return html.classes.has(name); }
        }
    };
    const head = {
        nodeType: 1,
        tagName: 'HEAD',
        children: [],
        appendChild(node) { head.children.push(node); return node; }
    };
    return {
        readyState: 'complete',
        // The unhook script boots now rather than on DOMContentLoaded, so no event
        // has to be delivered to reach the code under test.
        head,
        body: null,
        documentElement: html,
        createElement: () => ({ nodeType: 1, tagName: 'STYLE', textContent: '' }),
        getElementById: () => null,
        querySelector: () => null,
        querySelectorAll: () => []
    };
}

/** The storage the mode asks for. A denied page throws from both calls, as browsers do. */
function makeStorage(mode) {
    const log = { reads: [], writes: [], denied: [] };
    if (mode === 'denied') {
        return {
            log,
            getItem(key) { log.reads.push(key); throw new DOMException('storage is denied', 'SecurityError'); },
            setItem(key) {
                log.denied.push(key);
                throw new DOMException('storage is denied', 'SecurityError');
            }
        };
    }
    const values = new Map();
    return {
        log,
        getItem(key) { log.reads.push(key); return values.has(key) ? values.get(key) : null; },
        setItem(key, value) { log.writes.push(key); values.set(key, value); }
    };
}

const storage = makeStorage(mode);
const document = makeDocument();
const listeners = new Map();

const sandbox = {
    console,
    JSON,
    location: { hostname: 'www.youtube.com', pathname: '/watch' },
    document,
    localStorage: storage,
    MutationObserver: class { observe() {} disconnect() {} },
    addEventListener: (name, handler) => {
        if (!listeners.has(name)) listeners.set(name, []);
        listeners.get(name).push(handler);
    }
};
sandbox.window = sandbox;
sandbox.self = sandbox;

const context = createContext(sandbox);

const classesOn = () => Array.from(document.documentElement.classes).sort();

function run(scriptPath) {
    runInContext(readFileSync(scriptPath, 'utf8'), context, { filename: scriptPath });
}

try {
    run(initialPath);
    const liveConfigAfterLoad = sandbox.__UMBER_UNHOOK !== undefined;
    const afterLoad = classesOn();

    // A settings change made while the page is open arrives as a second injection,
    // and the page only acts on it once YouTube reports a navigation.
    run(livePath);
    const liveConfigAfterLiveChange = sandbox.__UMBER_UNHOOK !== undefined;
    for (const handler of listeners.get('yt-navigate-finish') ?? []) handler({});

    console.log(JSON.stringify({
        mode,
        storageReads: storage.log.reads,
        storageWrites: storage.log.writes,
        storageWritesRefused: storage.log.denied,
        liveConfigAfterLoad,
        liveConfigAfterLiveChange,
        resolvedConfig: sandbox.__UMBER_UNHOOK_CFG ?? null,
        afterLoad,
        afterLiveChange: classesOn()
    }));
    process.exit(0);
} catch (error) {
    console.error(error && error.stack ? error.stack : String(error));
    process.exit(1);
}