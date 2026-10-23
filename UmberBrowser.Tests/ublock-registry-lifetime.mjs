// Runs the pinned uBlock Origin 1.73.0 registry loader across two background
// lifetimes, over one shared cache, and reports what each lifetime did.
//
// The question file contents cannot answer is whether a repaired script actually reads
// the registry it saved. Moving the storage key on the get and the set looks like a
// completed repair and is not: the callback still looks for the old property name
// inside what the get returned, finds nothing, and rebuilds from the bundled
// assets.json on every start. Only running the shipped function twice over one cache
// shows that, so the functions below are extracted by anchor from the script under
// test and executed, not paraphrased.
//
//   node ublock-registry-lifetime.mjs <assetsJsPath> <bundledAssetsJsonPath> [mode]
//
// mode "repaired" seeds the upstream key, so lifetime one is a cache miss and has to
// rebuild. mode "stale" seeds the repaired key with metadata the bundled file does not
// carry, so lifetime two has to come out of the cache. mode "fresh" seeds nothing.
//
// Exits 0 when the expected lifetimes behaved, 1 when they did not, 2 on a bad call.

import { readFileSync } from 'node:fs';

const [jsPath, assetsJsonPath, mode = 'repaired'] = process.argv.slice(2);
if (!jsPath || !assetsJsonPath) {
    console.error('usage: node ublock-registry-lifetime.mjs <assetsJsPath> <bundledAssetsJsonPath> [mode]');
    process.exit(2);
}

const source = readFileSync(jsPath, 'utf8');
const bundledAssetsText = readFileSync(assetsJsonPath, 'utf8');

/**
 * The text of one function or arrow, from its opening anchor to its own closing token.
 * The default closing anchor is a brace at column zero, so a nested block cannot end
 * it. A function whose body is wrapped in an immediately invoked arrow has to say so.
 */
function fn(from, end = '\n}') {
    const start = source.indexOf(from);
    if (start === -1) throw new Error(`anchor not found in the script under test: ${from}`);
    const stop = source.indexOf(end, start);
    if (stop === -1) throw new Error(`no closing token for: ${from}`);
    return source.slice(start, stop + end.length);
}

const reIsExternalPath = /^(?:[a-z-]+):\/\//;
const probeKey = 'ublock-filters';
const repairedKey = 'assetSourceRegistry.umber.refresh.v1';
const newerRemoteUrl = 'https://updates.example.invalid/newer-only.txt';
const newerUpdateAfter = 4102444800; // 2100-01-01, further out than the bundled file carries.

/** One cache shared by both lifetimes, the way vAPI.storage outlives a background page. */
const cache = Object.create(null);

/** What the two lifetimes did, so a rebuild can never pass as a cache hit. */
const log = { getKeys: [], setKeys: [], fetchTextCalls: [] };

const cacheStorage = {
    async get(key) {
        log.getKeys.push(key);
        return key in cache ? { [key]: cache[key] } : undefined;
    },
    async set(bin) {
        for (const [key, value] of Object.entries(bin)) {
            log.setKeys.push(key);
            cache[key] = structuredClone(value);
        }
    }
};

/**
 * One background lifetime: the module level registry is created fresh, the shipped
 * getAssetSourceRegistry is called once, and the registry it settled on is handed back.
 *
 * Everything the loader reaches for is the shipped text. Only the network and the log
 * are replaced, so a fallback to the bundled file is recorded rather than served.
 */
async function backgroundLifetime(label) {
    const calls = [];
    const assets = {
        async fetchText(url) {
            log.fetchTextCalls.push({ lifetime: label, url });
            return { content: bundledAssetsText };
        }
    };
    const µb = {
        assetsBootstrapLocation: undefined,
        assetsJsonPath: '/assets/assets.json',
        badLists: new Set()
    };
    // The shipped defer returns something that can be cancelled, and a lazy save is
    // what the shipped loader would use. Save eagerly so the write lands in the cache
    // before the lifetime ends, which is the state the next lifetime has to find.
    const vAPI = {
        defer: {
            create: (f) => ({ off() {}, offon() {}, run: f })
        }
    };

    // The registry lives in module scope in the shipped script, so it is declared here
    // rather than captured, and each lifetime gets its own.
    const factory = new Function(
        'cacheStorage', 'assets', 'µb', 'ubolog', 'fireNotification',
        'assetCacheRemove', 'reIsExternalPath', 'vAPI', 'calls',
        'let assetSourceRegistryPromise;\nlet assetSourceRegistry = Object.create(null);\n' +
        fn('function unregisterAssetSource(assetKey) {') + '\n' +
        fn('const saveAssetSourceRegistry = (( ) => {', '\n})();') + '\n' +
        fn('function registerAssetSource(assetKey, newDict) {') + '\n' +
        fn('function updateAssetSourceRegistry(json, silent = false) {') + '\n' +
        fn('function getAssetSourceRegistry() {') + '\n' +
        'return getAssetSourceRegistry().then(() => ({ registry: assetSourceRegistry }));'
    );

    const before = log.fetchTextCalls.length;
    const { registry } = await factory(
        cacheStorage, assets, µb,
        (msg) => calls.push({ ubolog: msg }),
        (topic) => calls.push({ notification: topic }),
        (key) => calls.push({ removed: key }),
        reIsExternalPath, vAPI, calls
    );

    return {
        label,
        registry,
        // A lifetime that read the bundled file is a rebuild, whatever it then saved.
        rebuiltFromBundledFile: log.fetchTextCalls.length > before,
        notifications: calls.filter(c => c.notification !== undefined).map(c => c.notification)
    };
}

/**
 * The registry entries the shipped loader builds when it reads the bundled file.
 *
 * A plain object, not Object.create(null), because that is what a cache read hands
 * back and the shipped loader guards it with `instanceof Object`, which a null
 * prototype object does not satisfy.
 */
function seedFromBundledFile() {
    const registry = {};
    const register = new Function(
        'assetSourceRegistry', 'reIsExternalPath',
        fn('function registerAssetSource(assetKey, newDict) {') + '\nreturn registerAssetSource;'
    )(registry, reIsExternalPath);
    const parsed = JSON.parse(bundledAssetsText);
    parsed['assets.json'].defaultListset = Array.from(Object.entries(parsed))
        .filter(a => a[1].content === 'filters' && a[1].off === undefined)
        .map(a => a[0]);
    for (const [key, value] of Object.entries(parsed)) register(key, value);
    return registry;
}

/** The entry as the first lifetime left it, wherever it put it. */
function savedEntryUnderAnyKey() {
    for (const value of Object.values(cache)) {
        if (value && typeof value === 'object' && value[probeKey]) return value[probeKey];
    }
    return undefined;
}

const lifetimes = [];

if (mode === 'stale') {
    // The state a profile is in after a first repaired start: the registry the previous
    // start saved, carrying metadata newer than the bundled file knows about.
    const saved = seedFromBundledFile();
    saved[probeKey].contentURL = [...(saved[probeKey].contentURL ?? []), newerRemoteUrl];
    saved[probeKey].updateAfter = newerUpdateAfter;
    cache[repairedKey] = saved;
} else if (mode === 'repaired') {
    // The state a profile already had before the repair: a cached registry under the
    // upstream key, built from an assets.json whose entries had no remote urls. The
    // repaired loader must not read it, so the first lifetime is a cache miss.
    const stale = seedFromBundledFile();
    for (const entry of Object.values(stale)) {
        if (Array.isArray(entry.contentURL)) {
            entry.contentURL = entry.contentURL.filter(u => !reIsExternalPath.test(u));
        }
    }
    cache['assetSourceRegistry'] = stale;
}

const first = await backgroundLifetime('first');
lifetimes.push(first);
const firstEntry = first.registry[probeKey] ?? {};

// Between the two lifetimes the user selected a list and the updater learned something
// the bundled file does not carry. Only a cache hit can preserve it.
const saved = savedEntryUnderAnyKey();
if (saved) {
    saved.contentURL = [...(saved.contentURL ?? []), newerRemoteUrl];
    saved.updateAfter = newerUpdateAfter;
}

const second = await backgroundLifetime('second');
lifetimes.push(second);

const secondEntry = second.registry[probeKey] ?? {};
const secondUrls = secondEntry.contentURL ?? [];

const report = {
    mode,
    getKeys: log.getKeys,
    setKeys: [...new Set(log.setKeys)],
    fetchTextCalls: log.fetchTextCalls,
    firstRebuiltFromBundledFile: first.rebuiltFromBundledFile,
    secondRebuiltFromBundledFile: second.rebuiltFromBundledFile,
    firstEntryHasRemoteUrl: firstEntry.hasRemoteURL === true,
    secondEntryHasRemoteUrl: secondEntry.hasRemoteURL === true,
    secondKeptNewerRemoteUrl: secondUrls.includes(newerRemoteUrl),
    secondKeptNewerUpdateAfter: secondEntry.updateAfter === newerUpdateAfter,
    upstreamKeyStillWritten: log.setKeys.includes('assetSourceRegistry'),
    firstNotifications: first.notifications
};

if (mode === 'repaired') {
    // Lifetime one has to rebuild, because the upstream key is not the one the repaired
    // loader reads, and it has to persist what it rebuilt under the repaired key.
    report.ok =
        first.rebuiltFromBundledFile &&
        firstEntry.hasRemoteURL === true &&
        log.setKeys.includes(repairedKey) &&
        !log.setKeys.includes('assetSourceRegistry') &&
        log.getKeys.every(k => k === repairedKey);
}

if (mode === 'stale') {
    // Lifetime two has to come out of the cache with the newer metadata intact, and must
    // not have gone back to the bundled file for any reason.
    report.ok =
        !second.rebuiltFromBundledFile &&
        log.fetchTextCalls.length === 0 &&
        secondEntry.hasRemoteURL === true &&
        secondUrls.includes(newerRemoteUrl) &&
        secondEntry.updateAfter === newerUpdateAfter &&
        log.getKeys.length > 0 &&
        log.getKeys.every(k => k === repairedKey);
}

if (mode === 'fresh') {
    report.ok =
        first.rebuiltFromBundledFile &&
        firstEntry.hasRemoteURL === true &&
        log.setKeys.includes(repairedKey);
}

console.log(JSON.stringify(report, null, 2));
process.exit(report.ok ? 0 : 1);