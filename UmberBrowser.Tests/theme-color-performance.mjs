// Runs the exact text Umber registers on a core against a stubbed page and reports
// what the page sent to the host, step by step.
//
// The questions this answers cannot be answered by reading the script. The script
// throttles its work, drops a colour that the host already has, and stops entirely
// while the adaptive tint setting is off. Whether it sends one message or eight for
// a page that scrolls without changing colour, whether it reads the page at all
// while the setting is off, and whether the host can switch the page on without a
// reload are all decided by control flow that only a run shows.
//
//   node theme-color-performance.mjs <scriptPath>
//
// The script path holds the text as Umber registers it, so the adaptive tint
// setting is already baked into it. The harness never edits the script.
//
// Exits 0 on success, 1 when the run threw, 2 on a bad call.

import { readFileSync } from 'node:fs';
import { createContext, runInContext } from 'node:vm';

const [scriptPath] = process.argv.slice(2);
if (!scriptPath) {
    console.error('usage: node theme-color-performance.mjs <scriptPath>');
    process.exit(2);
}

/** The colours the page can show. The scheme decides what the page looks like in. */
const page = {
    metaColor: 'rgba(20, 30, 40, 0.5)',
    dark: false
};

/** Parses the colour forms a browser accepts for fillStyle. */
function parseColor(value) {
    if (typeof value !== 'string') return null;
    const text = value.trim().toLowerCase();
    if (text === 'transparent') return { r: 0, g: 0, b: 0, a: 0 };
    if (text.startsWith('#')) {
        const hex = text.slice(1);
        if (hex.length === 3) {
            return {
                r: parseInt(hex[0] + hex[0], 16),
                g: parseInt(hex[1] + hex[1], 16),
                b: parseInt(hex[2] + hex[2], 16),
                a: 1
            };
        }
        if (hex.length === 6) {
            return {
                r: parseInt(hex.slice(0, 2), 16),
                g: parseInt(hex.slice(2, 4), 16),
                b: parseInt(hex.slice(4, 6), 16),
                a: 1
            };
        }
        return null;
    }
    const match = text.match(/^rgba?\(([^)]*)\)$/);
    if (!match) return null;
    const parts = match[1].split(',').map(part => Number(part.trim()));
    if (parts.length < 3 || parts.some(part => !Number.isFinite(part))) return null;
    return { r: parts[0], g: parts[1], b: parts[2], a: parts.length > 3 ? parts[3] : 1 };
}

/** The form a canvas reports a colour in. */
function serialize(color) {
    const hex = value => ('0' + Math.round(value).toString(16)).slice(-2);
    if (color.a >= 1) return '#' + hex(color.r) + hex(color.g) + hex(color.b);
    return 'rgba(' + Math.round(color.r) + ', ' + Math.round(color.g) + ', ' +
        Math.round(color.b) + ', ' + color.a + ')';
}

/** Paints a translucent colour over what is already there. */
function blend(top, bottom) {
    const alpha = top.a + bottom.a * (1 - top.a);
    if (alpha === 0) return { r: 0, g: 0, b: 0, a: 0 };
    return {
        r: (top.r * top.a + bottom.r * bottom.a * (1 - top.a)) / alpha,
        g: (top.g * top.a + bottom.g * bottom.a * (1 - top.a)) / alpha,
        b: (top.b * top.a + bottom.b * bottom.a * (1 - top.a)) / alpha,
        a: alpha
    };
}

/**
 * A 2D context that keeps the colour a canvas would keep. A colour a canvas cannot
 * parse leaves the previous colour in place, and that is what the script uses to
 * recognise a colour it cannot use.
 */
function makeContext() {
    let state = { r: 0, g: 0, b: 0, a: 0 };
    return {
        get fillStyle() { return serialize(state); },
        set fillStyle(value) {
            const parsed = parseColor(value);
            if (!parsed) return;
            state = parsed.a >= 1 ? parsed : blend(parsed, state);
        },
        fillRect() { },
        getImageData() {
            return {
                data: [
                    Math.round(state.r), Math.round(state.g),
                    Math.round(state.b), Math.round(state.a * 255)
                ]
            };
        }
    };
}

/**
 * The page the script runs against, with the clock and the host channel the script
 * talks to. Nothing here decides anything the script decides: it only gives the
 * script the shapes it expects and counts what it does.
 */
function makeSandbox(isFrame) {
    const state = {
        posted: [],
        styleReads: 0,
        windowListeners: new Map(),
        webviewListeners: new Map(),
        mediaListeners: [],
        timers: [],
        timerId: 0,
        now: 1000
    };

    const document = {
        visibilityState: 'visible',
        hasFocus: () => true,
        body: {},
        documentElement: {},
        head: {},
        createElement: tag => {
            if (tag === 'canvas') {
                const context = makeContext();
                return { width: 0, height: 0, getContext: () => context };
            }
            return { style: {} };
        },
        querySelectorAll: selector => {
            if (selector === 'meta[name="theme-color"]') {
                if (!page.metaColor) return [];
                return [{
                    getAttribute: name => name === 'content' ? page.metaColor : null
                }];
            }
            return [];
        },
        elementsFromPoint: () => []
    };

    const sandbox = {
        console,
        JSON,
        document,
        innerWidth: 1280,
        Date: { now: () => state.now },
        setTimeout: (fn, delay) => {
            const id = ++state.timerId;
            state.timers.push({ id, fn, at: state.now + (delay || 0) });
            return id;
        },
        clearTimeout: id => {
            const index = state.timers.findIndex(timer => timer.id === id);
            if (index >= 0) state.timers.splice(index, 1);
        },
        getComputedStyle: () => {
            state.styleReads++;
            return { backgroundColor: 'rgba(0, 0, 0, 0)' };
        },
        matchMedia: query => {
            if (query !== '(prefers-color-scheme: dark)') return null;
            return {
                get matches() { return page.dark; },
                addEventListener: (name, handler) => {
                    if (name === 'change') state.mediaListeners.push(handler);
                },
                removeEventListener: (name, handler) => {
                    const index = state.mediaListeners.indexOf(handler);
                    if (index >= 0) state.mediaListeners.splice(index, 1);
                }
            };
        },
        MutationObserver: class {
            constructor(callback) { this.callback = callback; }
            observe() { }
            disconnect() { }
        },
        addEventListener: (name, handler) => {
            if (!state.windowListeners.has(name)) state.windowListeners.set(name, []);
            state.windowListeners.get(name).push(handler);
        },
        removeEventListener: (name, handler) => {
            const handlers = state.windowListeners.get(name) ?? [];
            const index = handlers.indexOf(handler);
            if (index >= 0) handlers.splice(index, 1);
        },
        chrome: {
            webview: {
                postMessage: message => state.posted.push(message),
                addEventListener: (name, handler) => {
                    if (!state.webviewListeners.has(name)) state.webviewListeners.set(name, []);
                    state.webviewListeners.get(name).push(handler);
                }
            }
        }
    };
    sandbox.window = sandbox;
    sandbox.self = sandbox;
    // A frame is not the top level document. The host drops its messages, so the
    // script must do nothing there.
    sandbox.top = isFrame ? { name: 'top' } : sandbox;

    state.fire = name => {
        for (const handler of state.windowListeners.get(name) ?? []) handler({ type: name });
    };
    state.postFromHost = data => {
        for (const handler of state.webviewListeners.get('message') ?? []) handler({ data });
    };
    state.changeScheme = () => {
        for (const handler of [...state.mediaListeners]) handler({ matches: page.dark });
    };
    state.advance = ms => {
        const target = state.now + ms;
        for (;;) {
            const due = state.timers.filter(timer => timer.at <= target)
                .sort((left, right) => left.at - right.at);
            if (!due.length) break;
            const next = due[0];
            state.timers.splice(state.timers.indexOf(next), 1);
            state.now = Math.max(state.now, next.at);
            next.fn();
        }
        state.now = target;
    };

    return { sandbox, state };
}

try {
    const script = readFileSync(scriptPath, 'utf8');
    const { sandbox, state } = makeSandbox(false);
    const context = createContext(sandbox);

    const steps = [];
    const step = (name, body) => {
        const postedBefore = state.posted.length;
        const readsBefore = state.styleReads;
        body();
        steps.push({
            name,
            messages: state.posted.slice(postedBefore),
            styleReads: state.styleReads - readsBefore
        });
    };

    step('boot', () => runInContext(script, context, { filename: scriptPath }));

    // A page that scrolls without changing its colour must not produce one message
    // per scroll event.
    step('scrolls', () => {
        for (let i = 0; i < 8; i++) {
            state.advance(300);
            state.fire('scroll');
        }
    });

    step('colourChange', () => {
        page.metaColor = 'rgba(60, 70, 80, 0.5)';
        state.advance(300);
        state.fire('scroll');
    });

    step('colourSchemeChange', () => {
        page.dark = true;
        state.changeScheme();
        state.advance(300);
    });

    step('disabled', () => {
        state.postFromHost('theme-tint-control:0');
        state.advance(300);
        state.fire('scroll');
    });

    step('enabled', () => {
        state.postFromHost('theme-tint-control:1');
        state.advance(300);
    });

    step('colourCleared', () => {
        page.metaColor = '';
        state.advance(300);
        state.fire('scroll');
    });

    step('malformedControl', () => {
        state.postFromHost('theme-tint-control:maybe');
        state.postFromHost({ data: { not: 'a string' } });
        state.advance(300);
        state.fire('scroll');
    });

    // The same script in a frame must do nothing at all.
    const frame = makeSandbox(true);
    const frameContext = createContext(frame.sandbox);
    runInContext(script, frameContext, { filename: scriptPath });
    const frameMessages = frame.state.posted.slice();

    console.log(JSON.stringify({
        steps,
        messages: state.posted,
        frameMessages
    }));
    process.exit(0);
} catch (error) {
    console.error(error && error.stack ? error.stack : String(error));
    process.exit(1);
}
