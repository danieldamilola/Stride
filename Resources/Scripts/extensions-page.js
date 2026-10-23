(() => {
  'use strict';

  const byId = id => document.getElementById(id);
  const search = byId('extensionSearch');
  const developer = byId('developerMode');
  const grid = byId('extensionGrid');
  const dialog = byId('extensionDetails');
  let state = { developerMode:false, extensions:[] };
  let busy = false;
  let busyTimer = null;
  let detailId = null;

  const icons = {
    unhook:['M3 5h18v14H3z','m10 9 5 3-5 3z'],
    youtube:['M3 5h18v14H3z','m10 9 5 3-5 3z'],
    tint:['M3 4h18v16H3z','M3 9h18','M7 6h1m3 0h1'],
    focus:['M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18','m6 6 12 12'],
    dark:['M20 15A9 9 0 0 1 9 4a9 9 0 1 0 11 11'],
    puzzle:['M8 3h4a2 2 0 1 1 4 0h4v5a2 2 0 1 0 0 4v5h-5a2 2 0 1 1-4 0H6v-5a2 2 0 1 1 0-4V3z'],
    pin:['m16 3 5 5-4 1-4 4v4l-2 2-2-6-6-2 2-2h4l4-4z','m9 15-6 6'],
    reload:['M20 7v5h-5','M4 17v-5h5','M6.1 7a7 7 0 0 1 11.5-1L20 9','M4 15l2.4 3a7 7 0 0 0 11.5-1']
  };

  function icon(name) {
    const svg = document.createElementNS('http://www.w3.org/2000/svg','svg');
    svg.setAttribute('viewBox','0 0 24 24');
    svg.setAttribute('aria-hidden','true');
    svg.setAttribute('fill','none');
    svg.setAttribute('stroke','currentColor');
    svg.setAttribute('stroke-width','1.5');
    svg.setAttribute('stroke-linecap','round');
    svg.setAttribute('stroke-linejoin','round');
    for (const d of icons[name] || icons.puzzle) {
      const path = document.createElementNS(svg.namespaceURI,'path');
      path.setAttribute('d',d);
      svg.append(path);
    }
    return svg;
  }

  function setIcon(container, extension) {
    container.replaceChildren(icon(extension.id?.startsWith('umber:') ? extension.id.slice(6) : 'puzzle'));
    const source = extension.iconDataUri;
    if (typeof source !== 'string' || !/^data:image\/(png|jpeg|jpg|gif|webp|x-icon|vnd\.microsoft\.icon);base64,[a-z\d+/=\s]+$/i.test(source)) return;
    const image = document.createElement('img');
    image.alt = '';
    image.addEventListener('error',() => container.replaceChildren(icon('puzzle')), { once:true });
    image.src = source;
    container.replaceChildren(image);
  }

  function text(tag, className, content) {
    const element = document.createElement(tag);
    if (className) element.className = className;
    element.textContent = content || '';
    return element;
  }

  function status(message, error = false) {
    const element = byId('extensionStatus');
    element.textContent = message || '';
    element.dataset.error = String(error);
  }

  function setBusy(value) {
    busy = value;
    clearTimeout(busyTimer);
    document.querySelectorAll('[data-mutation],#developerMode,#loadExtension,#reloadExtensions').forEach(control => control.disabled = busy || control.dataset.locked === 'true');
    grid.setAttribute('aria-busy',String(busy));
    if (busy) busyTimer = setTimeout(() => {
      setBusy(false);
      developer.checked = state.developerMode;
      status('The browser did not confirm this change. Try again.',true);
    },30000);
  }

  function post(route, mutation = true) {
    if (mutation && busy) return;
    if (mutation) { status(''); setBusy(true); }
    try {
      if (!window.chrome?.webview) throw new Error('No browser connection');
      window.chrome.webview.postMessage(__T + route);
    } catch {
      setBusy(false);
      developer.checked = state.developerMode;
      status('Extensions cannot connect to the browser. Reopen this page to try again.',true);
    }
  }

  function action(label, callback, extension, control, options = {}) {
    const button = text('button',options.icon ? 'icon-button' : 'extension-button',options.icon ? '' : label);
    button.type = 'button';
    button.dataset.extensionId = extension.id;
    button.dataset.control = control;
    if (options.mutation !== false) { button.dataset.mutation = ''; button.disabled = busy; }
    if (options.icon) { button.append(icon(options.icon)); button.setAttribute('aria-label',label); button.title = label; }
    if (options.danger) button.classList.add('danger');
    button.addEventListener('click',callback);
    return button;
  }

  function remove(extension) {
    if (extension.isBuiltIn || busy) return;
    if (window.confirm('Remove "' + extension.name + '" from Umber?')) post('extensions-remove:' + extension.id);
  }

  function restoreFocus(previous) {
    if (!previous) return;
    const match = [...document.querySelectorAll('[data-extension-id][data-control]')].find(element => element.dataset.extensionId === previous.id && element.dataset.control === previous.control);
    if (match && !match.disabled) match.focus({ preventScroll:true });
  }

  function render() {
    const active = document.activeElement;
    const previous = active?.dataset.extensionId ? { id:active.dataset.extensionId, control:active.dataset.control } : null;
    developer.checked = state.developerMode;
    byId('developerTools').hidden = !state.developerMode;
    const query = search.value.trim().toLocaleLowerCase();
    const extensions = state.extensions.filter(extension => [extension.name,extension.description,extension.id].some(value => String(value || '').toLocaleLowerCase().includes(query)));
    const fragment = document.createDocumentFragment();
    for (const extension of extensions) fragment.append(card(extension));
    grid.replaceChildren(fragment);
    byId('extensionCount').textContent = query ? extensions.length + (extensions.length === 1 ? ' extension found' : ' extensions found') : 'All extensions';
    byId('extensionEmpty').hidden = extensions.length > 0;
    byId('emptyHeading').textContent = query ? 'No matching extensions' : 'No extensions installed';
    byId('emptyDescription').textContent = query ? 'Try another name or search term.' : 'Turn on Developer mode to load an unpacked extension from a folder.';
    if (dialog.open && detailId) {
      const extension = state.extensions.find(item => item.id === detailId);
      if (extension) renderDetails(extension); else dialog.close();
    }
    restoreFocus(previous);
  }

  function card(extension) {
    const element = document.createElement('article');
    element.className = 'extension-card';
    const summary = document.createElement('div');
    summary.className = 'card-summary';
    const image = document.createElement('div');
    image.className = 'extension-icon';
    image.setAttribute('aria-hidden','true');
    setIcon(image,extension);
    const info = document.createElement('div');
    info.className = 'card-info';
    info.append(text('h3','extension-name',extension.name));
    info.append(text('p','extension-description',extension.description || 'No description provided.'));
    const meta = document.createElement('div');
    meta.className = 'extension-meta';
    if (extension.isBuiltIn) meta.append(text('span','','Built into Umber'));
    if (state.developerMode && extension.version) meta.append(text('span','','Version ' + extension.version));
    info.append(meta);
    summary.append(image,info);
    element.append(summary);
    if (extension.error) element.append(text('p','extension-error',extension.error));
    const actions = document.createElement('div');
    actions.className = 'card-actions';
    actions.append(action('Details',() => openDetails(extension),extension,'details',{ mutation:false }));
    if (!extension.isBuiltIn) actions.append(action('Remove',() => remove(extension),extension,'remove',{ danger:true }));
    if (state.developerMode && !extension.isBuiltIn) actions.append(action('Reload ' + extension.name,() => post('extensions-reload:' + extension.id),extension,'reload',{ icon:'reload' }));
    if (extension.hasPopup) {
      const pin = action((extension.isPinned ? 'Unpin ' : 'Pin ') + extension.name,() => post('extensions-pin:' + JSON.stringify({ id:extension.id,pinned:!extension.isPinned })),extension,'pin',{ icon:'pin' });
      pin.setAttribute('aria-pressed',String(!!extension.isPinned));
      actions.append(pin);
    }
    const label = document.createElement('label');
    label.className = 'enable-control';
    label.append(text('span','',extension.isEnabled ? 'On' : 'Off'));
    const toggle = document.createElement('input');
    toggle.type = 'checkbox';
    toggle.className = 'extension-toggle';
    toggle.checked = !!extension.isEnabled;
    toggle.disabled = busy || extension.canToggle === false;
    toggle.dataset.mutation = '';
    toggle.dataset.locked = String(extension.canToggle === false);
    toggle.dataset.extensionId = extension.id;
    toggle.dataset.control = 'enabled';
    toggle.setAttribute('aria-label','Enable ' + extension.name);
    toggle.addEventListener('change',() => {
      const enabled = toggle.checked;
      toggle.checked = !!extension.isEnabled;
      post('extensions-toggle:' + JSON.stringify({ id:extension.id,enabled }));
    });
    label.append(toggle);
    actions.append(label);
    element.append(actions);
    return element;
  }

  function renderDetails(extension) {
    byId('detailName').textContent = extension.name || 'Extension';
    setIcon(byId('detailIcon'),extension);
    byId('detailDescription').textContent = extension.description || 'No description provided.';
    byId('detailVersion').textContent = extension.version || 'Not provided';
    byId('detailSource').textContent = extension.isBuiltIn ? 'Built into Umber' : extension.sourceLabel || 'Loaded from a folder';
    byId('detailId').textContent = extension.id;
    byId('detailPath').textContent = extension.path || '';
    byId('detailPath').hidden = byId('detailPathLabel').hidden = !extension.path;
    const permissions = Array.isArray(extension.permissions) ? extension.permissions : [];
    byId('detailPermissions').replaceChildren(...(permissions.length ? permissions : ['No permissions declared.']).map(permission => text('li','',permission)));
    byId('detailError').textContent = extension.error || '';
    byId('detailError').hidden = !extension.error;
    const actions = byId('detailActions');
    actions.replaceChildren();
    if (extension.hasOptions && (extension.isEnabled || extension.id.startsWith('umber:'))) actions.append(action('Extension options',() => post('extensions-open-options:' + extension.id),extension,'options'));
    if (extension.hasPopup && extension.isEnabled) actions.append(action('Open extension',() => post('extensions-open-popup:' + extension.id),extension,'open'));
    if (state.developerMode && !extension.isBuiltIn) actions.append(action('Reload',() => post('extensions-reload:' + extension.id),extension,'detail-reload'));
    if (!extension.isBuiltIn) actions.append(action('Remove',() => remove(extension),extension,'detail-remove',{ danger:true }));
  }

  function openDetails(extension) {
    detailId = extension.id;
    renderDetails(extension);
    dialog.showModal();
  }

  function applySnapshot(snapshot) {
    if (!snapshot || !Array.isArray(snapshot.extensions)) return;
    state = { developerMode:!!snapshot.developerMode,extensions:snapshot.extensions.filter(extension => extension && typeof extension.id === 'string') };
    setBusy(false);
    render();
    if (snapshot.error) status(snapshot.error,true);
  }

  search.addEventListener('input',render);
  developer.addEventListener('change',() => post('extensions-developer:' + String(developer.checked)));
  byId('loadExtension').addEventListener('click',() => post('extensions-load'));
  byId('reloadExtensions').addEventListener('click',() => post('extensions-reload-all'));
  byId('closeDetails').addEventListener('click',() => dialog.close());
  dialog.addEventListener('close',() => detailId = null);
  window.chrome?.webview?.addEventListener('message',event => {
    if (typeof event.data !== 'string') return;
    const message = event.data.startsWith(__T) ? event.data.slice(__T.length) : event.data;
    if (message.startsWith('extensions-state:')) {
      try { applySnapshot(JSON.parse(message.slice('extensions-state:'.length))); } catch { status('The browser sent an invalid extension list. Reload this page to try again.',true); }
    } else if (message.startsWith('extensions-result:')) {
      try {
        const result = JSON.parse(message.slice('extensions-result:'.length));
        setBusy(false);
        developer.checked = state.developerMode;
        status(result.message || '',!result.success);
      } catch { setBusy(false); status('The browser did not confirm this change. Try again.',true); }
    }
  });
  try { applySnapshot(JSON.parse(byId('extensionsData').textContent)); }
  catch { render(); status('The extension list could not be loaded. Reload this page to try again.',true); }
  post('extensions-sync',false);
})();
