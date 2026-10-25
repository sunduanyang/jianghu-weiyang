const { chromium } = require('C:/Users/User/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
(async () => {
  const browser = await chromium.launch({ headless: true, channel: 'msedge' });
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, deviceScaleFactor: 1 });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto('http://127.0.0.1:8789/', { waitUntil: 'networkidle' });
    await page.screenshot({ path: path.join(__dirname, 'validation', 'desktop.png'), fullPage: true });
    assert.equal(await page.locator('#stage [data-slot]').count(), 16);
    await page.locator('[data-preset="daoist"]').click();
    assert.equal(await page.locator('#head').inputValue(), 'daoist');
    await page.locator('[data-preset="escort"]').click();
    assert.equal(await page.locator('#lower').inputValue(), 'escort');
    await page.selectOption('#head', 'daoist');
    await page.selectOption('#upper', 'escort');
    await page.selectOption('#lower', 'traveller');
    await page.selectOption('#waist', 'daoist');
    await page.selectOption('#pose', 'lunge');
    await page.locator('[data-expression="angry"]').click();
    await page.locator('[data-facing="left"]').click();
    assert.equal(await page.locator('#stage [data-asset="escort-torso-left"]').count(), 1);
    assert.equal(await page.locator('#stage [data-head="daoist-angry"]').count(), 1);
    await page.locator('#wear').fill('80');
    await page.locator('#wear').dispatchEvent('input');
    assert(await page.locator('#stage [data-layer="wear"]').count() > 0);
    await page.locator('#joints').check();
    assert.equal(await page.locator('#stage [data-joint]').count(), 14);
    await page.screenshot({ path: path.join(__dirname, 'validation', 'mixed-left-lunge.png'), fullPage: true });
    // Native keyboard operation and saved state survive reload.
    await page.locator('#head').focus();
    await page.keyboard.press('Home');
    await page.keyboard.press('ArrowDown');
    const beforeReload = await page.evaluate(() => window.puppetState);
    await page.reload({ waitUntil: 'networkidle' });
    assert.deepEqual(await page.evaluate(() => window.puppetState), beforeReload);
    const downloadPromise = page.waitForEvent('download');
    await page.locator('#export').click();
    const download = await downloadPromise;
    const exported = path.join(__dirname, 'validation', 'exported.svg');
    await download.saveAs(exported);
    const data = fs.readFileSync(exported, 'utf8');
    assert(data.startsWith('<svg')); assert(!data.includes('joint-guide'));
    // Try every core swap and facing through the browser renderer.
    const combinations = await page.evaluate(() => {
      let count = 0;
      for (const head of PuppetLab.TYPES) for (const upper of PuppetLab.TYPES) for (const lower of PuppetLab.TYPES) for (const facing of ['left', 'right']) {
        setPuppetState({ head, upper, lower, facing, wear: 0, joints: false, pose: 'lunge' });
        if (document.querySelectorAll('#stage [data-slot]').length !== 16) throw Error('A slot disappeared');
        if (!document.querySelector(`#stage [data-asset="${upper}-torso-${facing}"]`)) throw Error('Wrong torso asset');
        count++;
      }
      return count;
    });
    await page.evaluate(() => setPuppetState({ head: 'traveller', upper: 'traveller', lower: 'traveller', waist: 'traveller', expression: 'calm', facing: 'right', pose: 'idle', step: 100, wear: 0, joints: false }));
    await page.goto('http://127.0.0.1:8789/previews/three-characters.svg', { waitUntil: 'networkidle' });
    await page.setViewportSize({ width: 1500, height: 820 });
    await page.screenshot({ path: path.join(__dirname, 'previews', 'three-characters.png') });
    await page.goto('http://127.0.0.1:8789/previews/cross-swap.svg', { waitUntil: 'networkidle' });
    await page.screenshot({ path: path.join(__dirname, 'previews', 'cross-swap.png') });
    await page.goto('http://127.0.0.1:8789/', { waitUntil: 'networkidle' });
    await page.setViewportSize({ width: 390, height: 844 });
    await page.screenshot({ path: path.join(__dirname, 'validation', 'mobile-390.png'), fullPage: true });
    const at390 = await page.evaluate(() => ({ width: innerWidth, scroll: document.documentElement.scrollWidth }));
    assert(at390.scroll <= at390.width, '390px horizontal overflow');
    await page.setViewportSize({ width: 320, height: 800 });
    const at320 = await page.evaluate(() => ({ width: innerWidth, scroll: document.documentElement.scrollWidth }));
    assert(at320.scroll <= at320.width, '320px horizontal overflow');
    assert.deepEqual(errors, [], 'No browser runtime exceptions');
    const result = { desktop: '1440x1050', responsive: [at390, at320], browserSwapDirections: combinations, checked: ['presets', 'independent slots', 'expressions', 'left garment asset', 'wear overlay', 'joint guides', 'keyboard', 'state restore', 'SVG download'], pageErrors: errors };
    fs.writeFileSync(path.join(__dirname, 'validation', 'browser.json'), JSON.stringify(result, null, 2));
    console.log(JSON.stringify(result, null, 2));
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
