const pendingSettings = new Map();
let activeRecorder = null;
let recorderTimer = null;
function showSettingStatus(message, error = false) {
  const status = document.getElementById('settingsStatus');
  if (status) { status.textContent = message; status.dataset.error = String(error); status.hidden = !message; }
}
function postSetting(key, val, el) {
  val = String(val);
  const previous = pendingSettings.get(key);
  if (previous) clearTimeout(previous.timer);
  const pending = { value:val, el, timer:null };
  pending.timer = setTimeout(() => {
    if (pendingSettings.get(key) !== pending) return;
    pendingSettings.delete(key);
    if (el?.dataset.savedValue !== undefined) { if (el.type === 'checkbox') el.checked = el.dataset.savedValue === 'true'; else el.value = el.dataset.savedValue; }
    showSettingStatus('The browser did not confirm this change. Reload Settings and try again.', true);
  }, 5000);
  pendingSettings.set(key, pending);
  showSettingStatus('Saving...');
  try { window.chrome.webview.postMessage(__T + "setting:" + key + ":" + val); }
  catch { clearTimeout(pending.timer); pendingSettings.delete(key); showSettingStatus('Settings cannot connect to the browser. Reopen Settings to try again.', true); }
}
function s(key, el) {
  const val = el.type === "checkbox" ? el.checked : el.value;
  postSetting(key, val, el);
}
function updateOpacity(key, el, labelId) {
  document.getElementById(labelId).textContent = Math.round(parseFloat(el.value) * 100) + '%';
  postSetting(key, el.value, el);
}
function toggleDim(key, el, sliderId) {
  document.getElementById(sliderId).disabled = !el.checked;
  s(key, el);
}
function startRecording(badge) {
  if (activeRecorder) stopRecording(activeRecorder, false);
  activeRecorder = badge;
  badge.classList.add('recording'); badge.textContent = 'Press keys. Escape cancels.';
  window.chrome?.webview?.postMessage(__T + 'shortcut-recording:true');
  recorderTimer = setTimeout(() => stopRecording(badge, false), 15000);
}
function stopRecording(badge, save) {
  clearTimeout(recorderTimer);
  badge.classList.remove('recording');
  if (!save) badge.textContent = formatCombo(badge.dataset.combo);
  activeRecorder = null;
  window.chrome?.webview?.postMessage(__T + 'shortcut-recording:false');
}
function formatCombo(combo) { return combo.split('+').join(' + '); }
function resetShortcut(action, badge) { postSetting('shortcutReset', action, badge); }
const specialKeys = { ' ':'Space', ArrowLeft:'Left', ArrowRight:'Right', ArrowUp:'Up', ArrowDown:'Down' };
const codeKeys = { Backquote:'`', Minus:'-', Equal:'=', BracketLeft:'[', BracketRight:']', Backslash:'\\', Semicolon:';', Quote:"'", Comma:',', Period:'.', Slash:'/' };
// e.key carries the character the layout produces, so Shift+1 arrives as "!",
// which the shortcut parser cannot bind. Read physical digit and punctuation
// keys from e.code so the recorded combo matches the key the user pressed.
function recordedKey(e) {
  if (/^(Digit|Numpad)\d$/.test(e.code || '')) return e.code.slice(-1);
  if (codeKeys[e.code]) return codeKeys[e.code];
  if (specialKeys[e.key]) return specialKeys[e.key];
  return e.key.length === 1 ? e.key.toUpperCase() : e.key;
}
function comboFromEvent(e) {
  return [e.ctrlKey && 'Ctrl', e.shiftKey && 'Shift', e.altKey && 'Alt', recordedKey(e)].filter(Boolean).join('+');
}
document.addEventListener('keydown', e => {
  if (!activeRecorder) return;
  e.preventDefault(); e.stopPropagation();
  if (['Control','Shift','Alt','Meta'].includes(e.key)) return;
  if (e.key === 'Escape') { stopRecording(activeRecorder, false); return; }
  const combo = comboFromEvent(e);
  const badge = activeRecorder;
  stopRecording(badge, false);
  postSetting('shortcut', badge.dataset.action + ':' + combo, badge);
}, true);
window.addEventListener('blur', () => { if (activeRecorder) stopRecording(activeRecorder, false); });
function saveFocusDomains() { postSetting('focusDomains', encodeURIComponent(document.getElementById('focusDomains').value)); }
function lockFocus() {
  if (confirm('Lock these domains and the adult-content and Telegram blocklists? This lock also survives resetting browser settings.')) {
    postSetting('focusLock', encodeURIComponent(document.getElementById('focusDomains').value));
  }
}
document.addEventListener('DOMContentLoaded', () => {
  const navItems = [...document.querySelectorAll('.sidebar-nav-item')];
  const sections = [...document.querySelectorAll('.section')];
  const search = document.getElementById('searchInput');
  const summary = document.getElementById('searchSummary');
  const clear = document.getElementById('clearSearch');
  let selected = navItems[0].dataset.target;
  try { const saved = localStorage.getItem('umber_active_settings_tab'); if (sections.some(s => s.id === saved)) selected = saved; } catch {}
  const openBeforeSearch = new Map();
  function render() {
    const query = search.value.trim().toLowerCase();
    let count = 0;
    navItems.forEach(item => {
      const active = !query && item.dataset.target === selected;
      item.classList.toggle('active', active);
      if (active) item.setAttribute('aria-current','page'); else item.removeAttribute('aria-current');
    });
    sections.forEach(section => {
      const categoryMatch = query && section.dataset.searchTitle?.toLowerCase().includes(query);
      const rows = [...section.querySelectorAll('.setting-row')];
      let found = 0;
      rows.forEach(row => {
        const match = !query || categoryMatch || row.textContent.toLowerCase().includes(query);
        row.hidden = !match; if (match) found++;
      });
      section.querySelectorAll('.card').forEach(card => { card.hidden = !!query && ![...card.querySelectorAll('.setting-row')].some(r => !r.hidden); });
      section.querySelectorAll('.subsection-label').forEach(label => { label.hidden = !!query; });
      if (!rows.length && query && section.textContent.toLowerCase().includes(query)) found = 1;
      const visible = query ? found > 0 : section.id === selected;
      section.hidden = !visible; section.classList.toggle('active', visible);
      section.style.removeProperty('display');
      if (query) count += found;
      section.querySelectorAll('.collapsible').forEach(group => {
        group.hidden = !!query && ![...group.querySelectorAll('.setting-row')].some(r => !r.hidden);
        if (query) { if (!openBeforeSearch.has(group)) openBeforeSearch.set(group, group.open); group.open = !group.hidden; }
        else if (openBeforeSearch.has(group)) { group.open = openBeforeSearch.get(group); openBeforeSearch.delete(group); }
      });
    });
    clear.hidden = !query;
    summary.hidden = !query;
    summary.textContent = query ? (count ? count + ' matching settings' : 'No settings match. Try another search.') : '';
    document.getElementById('settingsContent').scrollTop = 0;
  }
  navItems.forEach(item => item.addEventListener('click', () => {
    selected = item.dataset.target; search.value = '';
    try { localStorage.setItem('umber_active_settings_tab', selected); } catch {}
    render();
  }));
  search.addEventListener('input', render);
  clear.addEventListener('click', () => { search.value = ''; render(); search.focus(); });
  document.querySelectorAll('input[onchange],select[onchange]').forEach(el => {
    const match = el.getAttribute('onchange').match(/(?:s|toggleDim)\('([^']+)'/);
    if (match) el.dataset.settingKey = match[1];
    el.dataset.savedValue = String(el.type === 'checkbox' ? el.checked : el.value);
  });
  for (const [key,id] of [['tabSleepDim','sleepSlider'],['tabHibernationDim','hibSlider']]) {
    const toggle = document.querySelector('[data-setting-key="'+key+'"]');
    document.getElementById(id).disabled = !toggle.checked;
  }
  function refreshCounts() {
    document.querySelectorAll('.collapsible').forEach(group => {
      const checks = [...group.querySelectorAll('input[type="checkbox"]')];
      const master = checks.shift();
      const count = group.querySelector('.collapsible-count');
      if (count) count.textContent = !master?.checked ? 'off' : checks.filter(c => c.checked).length + '/' + checks.length;
    });
  }
  document.addEventListener('change', refreshCounts);
  window.chrome?.webview?.addEventListener('message', event => {
    if (typeof event.data !== 'string' || !event.data.startsWith('setting-result:')) return;
    let result; try { result = JSON.parse(event.data.slice('setting-result:'.length)); } catch { return; }
    const pending = pendingSettings.get(result.key);
    if (pending && pending.value !== result.value) return;
    if (pending) { clearTimeout(pending.timer); pendingSettings.delete(result.key); }
    const message = result.success ? (result.restartRequired ? 'Restart Umber to apply this change.' : '') : result.message;
    showSettingStatus(message, !result.success);
    if (!result.success) { const el = pending?.el; if (el?.dataset.savedValue !== undefined) { if (el.type === 'checkbox') el.checked = el.dataset.savedValue === 'true'; else el.value = el.dataset.savedValue; } return; }
    document.querySelectorAll('[data-setting-key="'+result.key+'"]').forEach(el => {
      if (el.type === 'checkbox') el.checked = result.value === 'true'; else el.value = result.value;
      el.dataset.savedValue = result.value;
    });
    if (result.key === 'shortcut' || result.key === 'shortcutReset') {
      const parts = result.value.split(':');
      const badge = [...document.querySelectorAll('.shortcut-badge')].find(b => b.dataset.action === parts[0]);
      if (badge) { badge.dataset.combo = result.key === 'shortcutReset' ? badge.dataset.defaultCombo : parts[1]; badge.textContent = formatCombo(badge.dataset.combo); badge.previousElementSibling.hidden = badge.dataset.combo === badge.dataset.defaultCombo; }
    }
    if (result.key === 'focusLock') {
      document.getElementById('focusInputSection').hidden = true;
      document.getElementById('focusLockedSection').hidden = false;
      document.getElementById('focusInputSection').style.display = 'none';
      document.getElementById('focusLockedSection').style.display = 'block';
      document.getElementById('lockedDomains').textContent = decodeURIComponent(result.value) || 'No additional domains.';
    }
    refreshCounts();
  });
  render(); refreshCounts();
});
