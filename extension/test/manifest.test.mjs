// Статические проверки манифеста: один и тот же пакет должен грузиться и в Chrome, и в Firefox.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (p) => JSON.parse(fs.readFileSync(path.join(root, p), 'utf8'));
const manifest = read('manifest.json');
const exists = (p) => fs.existsSync(path.join(root, p));

test('фон: сервис-воркер для Chrome и тот же файл как скрипт для Firefox', () => {
  assert.equal(manifest.manifest_version, 3);
  assert.equal(manifest.background.service_worker, 'src/background.js');
  assert.deepEqual(manifest.background.scripts, ['src/background.js']);
  assert.equal(manifest.background.type, 'module');
});

test('Firefox: есть идентификатор, минимальная версия и заявление о данных', () => {
  const gecko = manifest.browser_specific_settings.gecko;
  assert.match(gecko.id, /^[\w.-]+@[\w.-]+$/);
  assert.ok(Number.parseInt(gecko.strict_min_version, 10) >= 128);   // с 127-й разрешения на сайты выдаются при установке
  assert.deepEqual(gecko.data_collection_permissions.required, ['websiteContent']);
});

test('все файлы, на которые ссылается манифест, на месте', () => {
  const files = [
    manifest.background.service_worker, manifest.action.default_popup, manifest.options_ui.page,
    ...Object.values(manifest.icons),
  ];
  for (const f of files) assert.ok(exists(f), f);
  for (const page of ['popup.html', 'options.html']) {
    const html = fs.readFileSync(path.join(root, page), 'utf8');
    for (const [, src] of html.matchAll(/(?:src|href)="([^"]+)"/g)) assert.ok(exists(src), `${page} → ${src}`);
  }
});

test('остров и LanguageTool доступны без дополнительных запросов разрешений', () => {
  assert.ok(manifest.host_permissions.includes('http://localhost:47811/*'));
  assert.ok(manifest.host_permissions.includes('https://api.languagetool.org/*'));
  for (const p of ['storage', 'contextMenus', 'activeTab', 'scripting']) assert.ok(manifest.permissions.includes(p), p);
});

test('переводы: в ru и en одни и те же ключи, и все __MSG_ из манифеста существуют', () => {
  const ru = read('_locales/ru/messages.json');
  const en = read('_locales/en/messages.json');
  assert.deepEqual(Object.keys(en).sort(), Object.keys(ru).sort());
  assert.ok(exists(`_locales/${manifest.default_locale}/messages.json`));
  for (const [, key] of JSON.stringify(manifest).matchAll(/__MSG_(\w+)__/g)) assert.ok(ru[key], key);
});

test('каждый ключ перевода, который просит код, существует', () => {
  const ru = read('_locales/ru/messages.json');
  for (const f of fs.readdirSync(path.join(root, 'src'))) {
    const code = fs.readFileSync(path.join(root, 'src', f), 'utf8');
    for (const [, key] of code.matchAll(/\bt\('(\w+)'/g)) assert.ok(ru[key], `${f}: ${key}`);
  }
});
