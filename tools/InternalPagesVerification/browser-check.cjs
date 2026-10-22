const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const assert = require('node:assert/strict');
const { chromium } = require(process.env.STRIDE_PLAYWRIGHT || 'playwright');
let browser;

(async () => {
  const directory = process.argv[2];
  browser = await chromium.launch({ headless:true });
  const context = await browser.newContext({ viewport:{width:1280,height:800} });
  await context.addInitScript(() => {
    const handlers = [];
    window.__messages = [];
    window.__sendNative = data => handlers.forEach(callback => callback({data}));
    window.chrome ||= {};
    window.chrome.webview = {
      addEventListener: (_, callback) => handlers.push(callback),
      postMessage: message => {
        window.__messages.push(message);
        const parts = message.split(':');
        if (parts[1] !== 'setting') return;
        const key = parts[2], value = parts.slice(3).join(':');
        const response = 'setting-result:' + JSON.stringify({key,value,success:true,message:'Saved.',restartRequired:false});
        queueMicrotask(() => handlers.forEach(callback => callback({data:response})));
      }
    };
  });
  const page = await context.newPage();
  await page.route('http://**/*', route => route.abort());
  await page.route('https://**/*', route => route.abort());
  await page.emulateMedia({ reducedMotion:'reduce' });
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('dialog', dialog => dialog.accept());
  const files = fs.readdirSync(directory).filter(name => name.endsWith('.html'));
  assert.equal(files.length,20);
  for (const name of files) {
    await page.goto(pathToFileURL(path.join(directory,name)).href);
    await page.waitForLoadState('domcontentloaded');
    assert.equal(await page.locator('body').count(),1);
    assert.equal(await page.evaluate(() => /\{\{[A-Z_]+\}\}/.test(document.documentElement.innerHTML)),false,name + ' has an unresolved template token');
    if (name.startsWith('settings-')) {
      const unlabeled = await page.locator('input:not([aria-label]):not([aria-labelledby]),select:not([aria-labelledby]),textarea:not([aria-labelledby]):not(#focusDomains)').count();
      assert.equal(unlabeled,0,'Settings has an unnamed control');
      await page.locator('#searchInput').fill('zoom');
      assert.equal(await page.locator('#section-appearance').isVisible(),true);
      assert.equal(await page.locator('#section-appearance .setting-row:visible').count(),1);
      assert.equal(await page.getByText('Website display',{exact:true}).isVisible(),true);
      await page.locator('[data-target=section-tabs]').click();
      assert.equal(await page.locator('#section-tabs').isVisible(),true);
      assert.equal(await page.locator('#section-general').isVisible(),false);
      await page.locator('[data-target=section-search]').click();
      assert.equal(await page.locator('#section-search [data-setting-key=search]').isVisible(),true);
      await page.reload();
      assert.equal(await page.locator('#section-search').isVisible(),true,'Selected category was not restored');
      for (const [saved, target] of [['section-performance','section-tabs'],['section-extensions','section-focus']]) {
        await page.evaluate(value => localStorage.setItem('stride_active_settings_tab',value),saved);
        await page.reload();
        assert.equal(await page.locator('#'+target).isVisible(),true,'Old section did not redirect to its new location');
      }
      assert.equal(await page.locator('[data-target=section-extensions]').count(),0);
      await page.locator('[data-target=section-search]').click();
      await page.evaluate(() => {
        window.__normalPostMessage = window.chrome.webview.postMessage;
        window.chrome.webview.postMessage = message => { window.__heldMessage = message; };
      });
      const suggestions = page.locator('[data-setting-key=searchSuggestions]');
      await suggestions.click();
      assert.equal(await page.locator('#settingsStatus').isVisible(),false,'Changing a setting showed routine saving text');
      assert.equal(await page.evaluate(() => pendingSettings.size),1,'The delayed save was not pending');
      await page.evaluate(() => {
        const parts = window.__heldMessage.split(':');
        window.__sendNative('setting-result:'+JSON.stringify({key:parts[2],value:parts.slice(3).join(':'),success:true,restartRequired:false}));
        window.chrome.webview.postMessage = window.__normalPostMessage;
      });
      assert.equal(await page.locator('#settingsStatus').isVisible(),false,'Successful save showed routine confirmation text');
      await page.evaluate(() => window.__sendNative('setting-result:'+JSON.stringify({key:'hwAccel',value:'false',success:true,restartRequired:true})));
      assert.match(await page.locator('#settingsStatus').textContent(),/Restart Stride/);
      await suggestions.click();
      assert.equal(await page.locator('#settingsStatus').isVisible(),true,'A routine save hid the restart notice');
      await page.evaluate(() => window.__sendNative('setting-result:'+JSON.stringify({key:'searchSuggestions',value:'false',success:false,message:'Could not save this preference.'})));
      assert.equal(await page.locator('#settingsStatus').isVisible(),true);
      assert.match(await page.locator('#settingsStatus').textContent(),/Could not save/);
      await suggestions.click();
      assert.equal(await page.locator('#settingsStatus').isVisible(),false,'Successful save did not clear the previous error');
      await page.locator('#searchInput').fill('toolbar buttons');
      assert.equal(await page.locator('.setting-row:visible').count(),6);
      await page.locator('#clearSearch').click();
      assert.equal(await page.locator('#section-search').isVisible(),true,'Clearing search lost the selected category');
      await page.locator('#searchInput').fill('annotations');
      assert.equal(await page.locator('.collapsible').evaluate(el=>el.open),true);
      assert.equal(await page.locator('.setting-row:visible').count(),1);
      assert.equal(await page.getByText('Website blocking',{exact:true}).isVisible(),false);
      assert.equal(await page.getByText('Player',{exact:true}).isVisible(),true);
      assert.equal(await page.getByText('Feed',{exact:true}).isVisible(),false);
      await page.locator('#clearSearch').click();
      assert.equal(await page.locator('.collapsible').evaluate(el=>el.open),false,'Search did not restore the collapsed state');
      await page.locator('[data-target=section-focus]').click();
      await page.locator('.collapsible > summary').click();
      await page.locator('#searchInput').fill('annotations');
      await page.locator('#clearSearch').click();
      assert.equal(await page.locator('.collapsible').evaluate(el=>el.open),true,'Search lost an expanded group');
      await page.locator('#searchInput').fill('architectural toolkit');
      const technical = page.locator('#section-system details');
      assert.equal(await technical.evaluate(el=>el.open),true,'Technical details stayed closed in search');
      assert.equal(await page.locator('.setting-row:visible').count(),1);
      await page.locator('#clearSearch').click();
      assert.equal(await technical.evaluate(el=>el.open),false);
      await page.locator('#searchInput').fill('lock');
      assert.equal(await page.locator('#section-focus').isVisible(),true);
      assert.equal(await page.getByText('Website blocking',{exact:true}).isVisible(),true);
      await page.locator('#searchInput').fill('not-a-setting');
      assert.match(await page.locator('#searchSummary').textContent(),/No settings match/);
      await page.locator('#clearSearch').click();
      await page.locator('[data-target=section-shortcuts]').focus();
      await page.keyboard.press('Enter');
      const badge = page.locator('[data-action=NewTab]');
      await badge.click();
      await page.keyboard.press('Control+k');
      assert.equal(await badge.textContent(),'Ctrl + K');
      assert.equal(await badge.locator('..').locator('.shortcut-reset').isVisible(),true);
      assert.equal(await page.evaluate(() => window.__messages.some(m => m.endsWith('setting:shortcut:NewTab:Ctrl+K'))),true);
      await page.locator('#searchInput').fill('Ctrl + K');
      assert.equal(await badge.isVisible(),true,'Search missed the updated shortcut');
      await page.locator('#clearSearch').click();
      await page.locator('[data-target=section-system]').click();
      await page.locator('#checkUpdateBtn').click();
      assert.equal(await page.locator('#checkUpdateBtn').textContent(),'Checking...');
      await page.evaluate(() => window.__sendNative('update-check-result:true:9.9.9:'));
      await page.locator('#checkUpdateBtn').click();
      await page.evaluate(() => window.__sendNative('update-progress:50'));
      assert.match(await page.locator('#updateStatus').textContent(),/50%/);
      await page.evaluate(() => window.__sendNative('update-failed:Download failed. Try again.'));
      assert.equal(await page.locator('#checkUpdateBtn').isEnabled(),true);
      await page.locator('[data-target=section-privacy]').click();
      const color = await page.locator('.setting-desc:visible').first().evaluate(el => ({text:getComputedStyle(el).color,background:getComputedStyle(el.closest('.card')).backgroundColor}));
      function luminance(rgb) { const c=rgb.match(/[\d.]+/g).slice(0,3).map(Number).map(x=>x/255).map(x=>x<=.04045?x/12.92:((x+.055)/1.055)**2.4); return c[0]*.2126+c[1]*.7152+c[2]*.0722; }
      const a=luminance(color.text),b=luminance(color.background);
      assert.ok((Math.max(a,b)+.05)/(Math.min(a,b)+.05)>=4.5,'Description contrast is too low');
      await page.screenshot({path:path.join(directory,name.replace('.html','.png'))});
      await page.setViewportSize({width:760,height:480});
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth),true,'Settings overflows a narrow window');
      await page.screenshot({path:path.join(directory,name.replace('.html','-narrow.png'))});
      await page.setViewportSize({width:1280,height:800});
      for (const category of ['general','appearance','tabs','search','privacy','focus','shortcuts','system']) {
        await page.locator('[data-target=section-'+category+']').click();
        assert.equal(await page.locator('.section:visible').count(),1);
        assert.equal(await page.locator('#section-'+category).isVisible(),true);
        await page.screenshot({path:path.join(directory,name.replace('.html','-'+category+'.png'))});
        await page.setViewportSize({width:440,height:720});
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth),true,category+' overflows a narrow window');
        await page.setViewportSize({width:1280,height:800});
      }
    }
    if (name.startsWith('saved-tabs-')) {
      await page.locator('#onetab-search').fill('Research');
      assert.equal(await page.locator('.tab:visible').count(),1);
      await page.locator('#onetab-search').fill('missing-name');
      assert.equal(await page.locator('.no-results').isVisible(),true);
    }
    if (name.startsWith('newtab-')) {
      await page.locator('.shortcut-add').click();
      await page.locator('#scUrl').fill('https://');
      await page.getByRole('button',{name:'Save',exact:true}).click();
      assert.match(await page.locator('#shortcutError').textContent(),/valid/);
      await page.getByRole('button',{name:'Cancel',exact:true}).click();
      assert.equal(await page.locator('#dialogOverlay').isVisible(),false);
      await page.locator('.wallpapers-btn').click();
      assert.equal(await page.locator('#wallpaperDialog [role=dialog]').count(),1);
      await page.keyboard.press('Escape');
      assert.equal(await page.locator('#wallpaperDialog').isVisible(),false);
    }
    if (name.startsWith('downloads-')) {
      await page.locator('#clearAllBtn').click();
      await page.getByRole('button',{name:'Cancel',exact:true}).focus();
      await page.keyboard.press('Shift+Tab');
      assert.equal(await page.locator('#confirmActionBtn').evaluate(el=>el===document.activeElement),true);
      await page.keyboard.press('Escape');
      assert.equal(await page.locator('#confirmDialog').isVisible(),false);
    }
    if (/^(newtab|onboarding|health|release-notes|focus)-light/.test(name)) await page.screenshot({path:path.join(directory,name.replace('.html','.png'))});
  }
  assert.deepEqual(errors,[]);
  await browser.close();
  console.log('PASS: 20 rendered pages, Settings search and keyboard editing, contrast, narrow layout, saved group search, shortcut validation, dialog focus and Escape.');
})().catch(error => { console.error(error); process.exitCode=1; }).finally(async () => { await browser?.close(); });
