(() => {
  let serial = 0;
  let activeDialog = null;
  let previousFocus = null;
  const inertState = new Map();
  function elements(root, selector) {
    const matches = [...root.querySelectorAll(selector)];
    if (root.matches?.(selector)) matches.unshift(root);
    return matches;
  }
  function enhance(root) {
    elements(root, '.setting-row').forEach(row => {
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
    elements(root, '.date-header,.domain-header,.group-name,.tab-star,a.entry-title:not([href])').forEach(el => {
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
    elements(root, '.date-header,.domain-header').forEach(el => {
      el.setAttribute('aria-expanded', String(!el.parentElement.classList.contains('collapsed')));
      const body = el.nextElementSibling;
      if (body) { if (!body.id) body.id = 'expanded-content-' + ++serial; el.setAttribute('aria-controls', body.id); }
    });
    elements(root, 'input[placeholder]').forEach(el => {
      if (!el.hasAttribute('aria-label') && !el.hasAttribute('aria-labelledby') && !el.labels?.length) el.setAttribute('aria-label', el.placeholder);
    });
  }
  function syncDialog(overlay) {
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
    enhance(document.body);
    syncDialog(document.querySelector('.dialog-overlay.open'));
    new MutationObserver(records => {
      const added = new Set();
      for (const record of records) {
        if (record.type === 'childList') {
          for (const node of record.addedNodes) if (node.nodeType === 1) added.add(node);
        } else {
          const target = record.target;
          // Expanded state changes need only their own heading.
          if (target.matches('.date-section,.domain-group')) {
            const heading = target.querySelector(':scope > .date-header,:scope > .domain-header');
            if (heading) heading.setAttribute('aria-expanded',String(!target.classList.contains('collapsed')));
          }
          if (target.matches('.dialog-overlay')) syncDialog(target.classList.contains('open') ? target : null);
        }
      }
      for (const node of added) {
        if (!node.isConnected || [...added].some(parent => parent !== node && parent.contains(node))) continue;
        enhance(node);
        const row = node.closest('.setting-row');
        if (row && row !== node) enhance(row);
        const overlay = node.matches('.dialog-overlay.open') ? node : node.querySelector('.dialog-overlay.open');
        if (overlay) syncDialog(overlay);
      }
      if (activeDialog && !activeDialog.isConnected) syncDialog(null);
    }).observe(document.body, { childList:true, subtree:true, attributes:true, attributeFilter:['class'] });
  });
})();
