(() => {
  let serial = 0;
  let activeDialog = null;
  let previousFocus = null;
  const inertState = new Map();
  function enhance() {
    document.querySelectorAll('.setting-row').forEach(row => {
      const label = row.querySelector('.setting-label');
      const description = row.querySelector('.setting-desc');
      if (!label) return;
      if (!label.id) label.id = 'setting-label-' + ++serial;
      if (description && !description.id) description.id = 'setting-help-' + ++serial;
      row.querySelectorAll('input,select,textarea').forEach(control => {
        control.setAttribute('aria-labelledby', label.id);
        if (description) control.setAttribute('aria-describedby', description.id);
      });
    });
    document.querySelectorAll('.date-header,.domain-header,.group-name,.tab-star,a.entry-title:not([href])').forEach(el => {
      if (el.dataset.keyboardReady) return;
      el.dataset.keyboardReady = 'true';
      el.tabIndex = 0;
      el.setAttribute('role', el.matches('a') ? 'link' : 'button');
      if (el.matches('.group-name')) el.setAttribute('aria-label', 'Rename saved tab group');
      if (el.matches('.tab-star')) el.setAttribute('aria-label', 'Toggle saved tab star');
      el.addEventListener('keydown', e => {
        if (el.isContentEditable) return;
        if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); el.click(); }
      });
    });
    document.querySelectorAll('.date-header,.domain-header').forEach(el => {
      el.setAttribute('aria-expanded', String(!el.parentElement.classList.contains('collapsed')));
      const body = el.nextElementSibling;
      if (body) { if (!body.id) body.id = 'expanded-content-' + ++serial; el.setAttribute('aria-controls', body.id); }
    });
    document.querySelectorAll('input[placeholder]').forEach(el => {
      if (!el.hasAttribute('aria-label') && !el.hasAttribute('aria-labelledby') && !document.querySelector('label[for="' + el.id + '"]')) el.setAttribute('aria-label', el.placeholder);
    });
    const overlay = document.querySelector('.dialog-overlay.open');
    if (overlay === activeDialog) return;
    if (activeDialog) {
      inertState.forEach((value, el) => el.inert = value);
      inertState.clear();
      if (previousFocus?.isConnected) previousFocus.focus();
    }
    activeDialog = overlay;
    if (!overlay) return;
    previousFocus = document.activeElement;
    const dialog = overlay.querySelector('.dialog') || overlay;
    dialog.setAttribute('role', 'dialog'); dialog.setAttribute('aria-modal', 'true');
    const heading = dialog.querySelector('h2,h3');
    if (heading) { if (!heading.id) heading.id = 'dialog-heading-' + ++serial; dialog.setAttribute('aria-labelledby', heading.id); }
    for (const sibling of document.body.children) {
      if (sibling === overlay || sibling.contains(overlay) || sibling.tagName === 'SCRIPT') continue;
      inertState.set(sibling, sibling.inert); sibling.inert = true;
    }
    const first = dialog.querySelector('[autofocus],input,button,select,textarea');
    if (first) first.focus();
  }
  document.addEventListener('keydown', e => {
    if (!activeDialog) return;
    if (e.key === 'Escape') {
      const cancel = [...activeDialog.querySelectorAll('button')].find(b => b.textContent.trim() === 'Cancel' || b.textContent.trim() === 'Close');
      if (cancel) { e.preventDefault(); cancel.click(); }
    }
    if (e.key !== 'Tab') return;
    const items = [...activeDialog.querySelectorAll('button,input,select,textarea,a[href],[tabindex="0"]')].filter(el => !el.disabled && el.getClientRects().length);
    const first = items[0], last = items[items.length - 1];
    if (e.shiftKey && (document.activeElement === first || !activeDialog.contains(document.activeElement))) { e.preventDefault(); last?.focus(); }
    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first?.focus(); }
  }, true);
  document.addEventListener('DOMContentLoaded', () => {
    enhance();
    new MutationObserver(enhance).observe(document.body, { childList:true, subtree:true, attributes:true, attributeFilter:['class'] });
  });
})();
