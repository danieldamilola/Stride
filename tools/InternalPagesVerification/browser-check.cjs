const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const assert = require('node:assert/strict');
const { chromium } = require(process.env.UMBER_PLAYWRIGHT || 'playwright');

(async () => {
  const directory = process.argv[2];
  const browser = await chromium.launch({ headless:true });
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
      assert.equal(await page.locator('#section-general').isVisible(),true);
      await page.locator('[data-target=section-performance]').click();
      assert.equal(await page.locator('#section-performance').isVisible(),true);
      assert.equal(await page.locator('#section-general').isVisible(),false);
      await page.locator('#searchInput').fill('lock');
      assert.equal(await page.locator('#section-focus').isVisible(),true);
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
})().catch(error => { console.error(error); process.exitCode=1; });
