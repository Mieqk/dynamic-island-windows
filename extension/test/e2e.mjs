// Сквозной тест: настоящий Chromium с загруженным расширением против мок-LanguageTool и мок-острова.
// Запуск: node test/e2e.mjs   (нужен Playwright с Chromium; в npm-зависимости он не входит)
import { createRequire } from 'node:module';
import path from 'node:path';
import os from 'node:os';
import fs from 'node:fs';
import http from 'node:http';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { startLanguageTool, startIsland } from './helpers.mjs';

const require = createRequire(import.meta.url);
const { chromium } = require('playwright');
const here = path.dirname(fileURLToPath(import.meta.url));
const extPath = path.resolve(here, '..');
const TOKEN = 'e'.repeat(64);

const lt = await startLanguageTool();
const island = await startIsland({ port: 47811, token: TOKEN });
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'atoll-ext-'));
const context = await chromium.launchPersistentContext(profile, {
  headless: false,   // с --headless=new расширения работают; «настоящий» headless их не загружает
  args: ['--headless=new', '--no-proxy-server', '--disable-background-networking', '--no-first-run', '--disable-sync', '--disable-component-update', `--disable-extensions-except=${extPath}`, `--load-extension=${extPath}`, '--no-sandbox'],
});

let failed = 0;
const t0 = Date.now();
const step = async (name, fn) => {
  try { await fn(); console.log('ok   ', name, `(${Math.round((Date.now() - t0) / 1000)} с)`); }
  catch (e) { failed++; console.log('FAIL ', name, '\n     ', e.message.split('\n')[0]); }
};

try {
  let [sw] = context.serviceWorkers();
  sw ??= await context.waitForEvent('serviceworker', { timeout: 15000 });
  const id = new URL(sw.url()).host;
  console.log('extension id:', id);

  await step('настройки: ключ сохраняется и остров отвечает', async () => {
    const page = await context.newPage();
    await page.goto(`chrome-extension://${id}/options.html`);
    await page.fill('#key', TOKEN);
    await page.click('#save');
    await page.waitForFunction(() => document.getElementById('status').classList.contains('ok'), null, { timeout: 8000 });
    const stored = await sw.evaluate(() => chrome.storage.local.get(['key', 'server']));
    assert.equal(stored.key, TOKEN);
    // дальше проверяем против мок-LanguageTool (разрешение на чужой сервер в headless не выдать, поэтому пишем напрямую)
    await sw.evaluate((server) => chrome.storage.local.set({ server }), lt.url);
    await page.close();
  });

  await step('окно расширения: проверка находит ошибки и сообщает итог острову', async () => {
    const page = await context.newPage();
    await page.goto(`chrome-extension://${id}/popup.html`);
    await page.waitForFunction(() => document.getElementById('status').classList.contains('ok'), null, { timeout: 8000 });   // остров на связи
    await page.fill('#text', 'Ths is a tset');
    await page.click('#check');
    await page.waitForSelector('.issue');
    assert.equal(await page.locator('.issue').count(), 2);
    await page.waitForTimeout(300);
    assert.deepEqual(island.state.checks.at(-1), { issues: 2, lang: 'en-US' });
    await page.close();
  });

  await step('окно расширения: подсказка заменяет текст и проверка идёт заново', async () => {
    const page = await context.newPage();
    await page.goto(`chrome-extension://${id}/popup.html`);
    await page.fill('#text', 'Ths is a tset');
    await page.click('#check');
    await page.waitForSelector('.issue');
    await page.locator('.sug', { hasText: /^This$/ }).first().click();
    await page.waitForFunction(() => document.querySelectorAll('.issue').length === 1);
    assert.equal(await page.inputValue('#text'), 'This is a tset');
    await page.close();
  });

  await step('окно расширения: «в заметки» кладёт текст на остров', async () => {
    const page = await context.newPage();
    await page.goto(`chrome-extension://${id}/popup.html`);
    await page.fill('#text', 'Купить молоко');
    await page.click('#note');
    await page.waitForFunction(() => document.getElementById('status').classList.contains('ok') && true);
    await page.waitForTimeout(200);
    assert.equal(island.state.notes.at(-1).text, 'Купить молоко');
    await page.close();
  });

  await step('связь выключена на острове: расширение показывает причину', async () => {
    island.state.enabled = false;
    const page = await context.newPage();
    await page.goto(`chrome-extension://${id}/popup.html`);
    await page.waitForFunction(() => document.getElementById('status').classList.contains('err'), null, { timeout: 8000 });
    await page.close();
    island.state.enabled = true;
  });

  await step('чужой ключ: остров отвечает 401, расширение показывает ошибку', async () => {
    await sw.evaluate(() => chrome.storage.local.set({ key: 'f'.repeat(64) }));
    const page = await context.newPage();
    await page.goto(`chrome-extension://${id}/popup.html`);
    await page.waitForFunction(() => document.getElementById('status').classList.contains('err'), null, { timeout: 8000 });
    await page.close();
    await sw.evaluate((k) => chrome.storage.local.set({ key: k }), TOKEN);
  });

  await step('карточка на странице: показывает замечания и копирует исправленный текст', async () => {
    const web = http.createServer((_q, r) => { r.setHeader('Content-Type', 'text/html'); r.end('<!doctype html><title>t</title><body>page'); });
    await new Promise((r) => web.listen(0, 'localhost', r));
    const page = await context.newPage();
    // закрытый shadow DOM снаружи не виден: для проверки делаем его открытым
    await page.addInitScript(() => { const a = Element.prototype.attachShadow; Element.prototype.attachShadow = function (i) { return a.call(this, { ...i, mode: 'open' }); }; });
    await page.goto(`http://localhost:${web.address().port}/`);
    await page.evaluate(() => {
      window.__atollOverlay = {
        title: 'Замечаний: 2', corrected: 'This is a test', truncated: false,
        labels: { close: 'x', noIssues: 'ok', copyFixed: 'Копировать', copied: 'Готово', truncated: '' },
        issues: [{ bad: 'Ths', message: 'Опечатка', suggestions: ['This'] }, { bad: 'tset', message: 'Опечатка', suggestions: ['test'] }],
      };
    });
    await page.addScriptTag({ path: path.join(extPath, 'src', 'overlay.js') });
    const host = page.locator('#atoll-overlay-host');
    await host.waitFor();
    const root = host.locator('.issue');
    assert.equal(await root.count(), 2);
    assert.equal(await page.evaluate(() => window.__atollOverlay), undefined);   // данные убраны со страницы
    await page.close();
    await new Promise((r) => { web.closeAllConnections(); web.close(r); });
  });
} finally {
  console.log('closing browser…');
  await context.close();
  console.log('browser closed');
  await lt.close();
  await island.close();
  fs.rmSync(profile, { recursive: true, force: true });
}
console.log(failed ? `\n${failed} шаг(ов) не прошли` : '\nвсе шаги прошли');
process.exit(failed ? 1 : 0);
