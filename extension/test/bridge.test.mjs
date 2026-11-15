import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ping, sendNote, sendCheck } from '../src/bridge.js';
import { startIsland, asExtension } from './helpers.mjs';

const TOKEN = 'b'.repeat(64);
test('связь: ping, заметка и итог проверки доходят до острова', async () => {
  const island = await startIsland({ token: TOKEN });
  const f = asExtension(island.port);
  try {
    assert.deepEqual(await ping(TOKEN, f), { ok: true });
    assert.deepEqual(await sendNote({ text: 'Привет', title: 'Страница', url: 'https://example.test/' }, TOKEN, f), { ok: true });
    assert.deepEqual(await sendCheck(3, 'ru-RU', TOKEN, f), { ok: true });
    assert.deepEqual(island.state.notes, [{ text: 'Привет', title: 'Страница', url: 'https://example.test/' }]);
    assert.deepEqual(island.state.checks, [{ issues: 3, lang: 'ru-RU' }]);
  } finally { await island.close(); }
});

test('неверный ключ даёт unauthorized, выключенная связь даёт disabled', async () => {
  const island = await startIsland({ token: TOKEN });
  const f = asExtension(island.port);
  try {
    assert.equal((await ping('c'.repeat(64), f)).reason, 'unauthorized');
    island.state.enabled = false;
    assert.equal((await ping(TOKEN, f)).reason, 'disabled');
  } finally { await island.close(); }
});

test('без ключа запрос даже не отправляется', async () => {
  let called = false;
  const res = await ping('', async () => { called = true; });
  assert.equal(res.reason, 'nokey');
  assert.equal(called, false);
});

test('остров не запущен: offline, а не исключение', async () => {
  const res = await ping(TOKEN, () => Promise.reject(new TypeError('fetch failed')));
  assert.equal(res.reason, 'offline');
});

test('запрос без Origin расширения (скрипт, страница) остров отклоняет', async () => {
  const island = await startIsland({ token: TOKEN });
  try {
    const plain = await fetch(`http://localhost:${island.port}/ext/note`, {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Island-Ext-Token': TOKEN }, body: '{"text":"x"}',
    });
    assert.equal(plain.status, 403);
    const web = await fetch(`http://localhost:${island.port}/ext/note`, {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Island-Ext-Token': TOKEN, Origin: 'https://evil.example' }, body: '{"text":"x"}',
    });
    assert.equal(web.status, 403);
    assert.deepEqual(island.state.notes, []);
  } finally { await island.close(); }
});
