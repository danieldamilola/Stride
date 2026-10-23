// Loads the shipped Settings script with a minimal DOM stub and exercises the
// shortcut recorder. The recorder builds the combo string that the C# side
// parses, so a combo it produces for a supported key has to parse back to the
// same modifiers and key.
import fs from 'node:fs';
import vm from 'node:vm';

const scriptPath = process.argv[2];
const defaultCombos = process.argv[3] ? JSON.parse(process.argv[3]) : [];

const sandbox = {
  __T: 'token:',
  document: {
    addEventListener() {},
    getElementById() { return null; },
    querySelectorAll() { return []; },
  },
  window: { addEventListener() {} },
  localStorage: { getItem() { return null; }, setItem() {} },
  setTimeout,
  clearTimeout,
};
vm.createContext(sandbox);
vm.runInContext(fs.readFileSync(scriptPath, 'utf8') + '\n;globalThis.__comboFromEvent = comboFromEvent;', sandbox, { filename: scriptPath });
const comboFromEvent = sandbox.__comboFromEvent;

const codeFor = {
  '1': 'Digit1', '2': 'Digit2', '3': 'Digit3', '4': 'Digit4', '5': 'Digit5',
  '6': 'Digit6', '7': 'Digit7', '8': 'Digit8', '9': 'Digit9', '0': 'Digit0',
  '=': 'Equal', '-': 'Minus', ',': 'Comma', '.': 'Period', '/': 'Slash',
  '\\': 'Backslash', '[': 'BracketLeft', ']': 'BracketRight', ';': 'Semicolon',
  '"': 'Quote', '`': 'Backquote',
};

// Turns a combo the way C# writes it into the keydown that a user pressing it
// would generate. Shifted symbols need the unshifted e.key because e.code is
// what the recorder reads now, and that is the whole point of the test.
function eventFor(combo) {
  const parts = combo.split('+');
  const keyPart = parts[parts.length - 1];
  const shift = parts.includes('Shift');
  const event = {
    ctrlKey: parts.includes('Ctrl'),
    shiftKey: shift,
    altKey: parts.includes('Alt'),
    metaKey: false,
    key: keyPart,
    code: '',
  };
  if (/^[0-9]$/.test(keyPart)) {
    event.code = codeFor[keyPart];
  } else if (/^[A-Z]$/.test(keyPart)) {
    event.code = 'Key' + keyPart;
    event.key = shift ? keyPart : keyPart.toLowerCase();
  } else if (codeFor[keyPart]) {
    event.code = codeFor[keyPart];
  }
  return event;
}

const cases = [
  ['Ctrl+Shift+1', { key: '!', code: 'Digit1', ctrlKey: true, shiftKey: true }],
  ['Ctrl+1', { key: '1', code: 'Digit1', ctrlKey: true }],
  ['Shift+1', { key: '!', code: 'Digit1', shiftKey: true }],
  ['Ctrl+Shift+=', { key: '+', code: 'Equal', ctrlKey: true, shiftKey: true }],
  ['Ctrl+-', { key: '-', code: 'Minus', ctrlKey: true }],
  ['Ctrl+,', { key: ',', code: 'Comma', ctrlKey: true }],
  ['Ctrl+/', { key: '/', code: 'Slash', ctrlKey: true }],
  ['Ctrl+Shift+[', { key: '{', code: 'BracketLeft', ctrlKey: true, shiftKey: true }],
  ['Ctrl+Shift+`', { key: '~', code: 'Backquote', ctrlKey: true, shiftKey: true }],
  ['Ctrl+Shift+1', { key: '!', code: 'Numpad1', ctrlKey: true, shiftKey: true }],
  ['Ctrl+Shift+T', { key: 'T', code: 'KeyT', ctrlKey: true, shiftKey: true }],
  ['Space', { key: ' ', code: 'Space' }],
  ['Alt+Left', { key: 'ArrowLeft', code: 'ArrowLeft', altKey: true }],
];

const report = [
  ...cases.map(([expected, event]) => ({ source: 'physical', expected, actual: comboFromEvent(event) })),
  ...defaultCombos.map(expected => ({ source: 'default', expected, actual: comboFromEvent(eventFor(expected)) })),
];
const mismatches = report.filter(entry => entry.expected !== entry.actual);

process.stdout.write(JSON.stringify({ report, mismatches }));
process.exitCode = mismatches.length === 0 ? 0 : 1;
