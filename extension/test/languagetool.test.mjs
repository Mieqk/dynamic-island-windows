import { test } from 'node:test';
import assert from 'node:assert/strict';
import { check, applyOne, applyFirstSuggestions, MAX_CHARS } from '../src/languagetool.js';
import { startLanguageTool } from './helpers.mjs';

test('находит замечания и отдаёт подсказки', async () => {
  const lt = await startLanguageTool();
  try {
    const r = await check('Ths is a tset', { server: lt.url, lang: 'auto' });
    assert.equal(r.matches.length, 2);
    assert.deepEqual(r.matches[0], { offset: 0, length: 3, message: 'Опечатка: «Ths»', replacements: ['This', 'Thiss'] });
    assert.equal(r.lang, 'en-US');
    assert.equal(r.truncated, false);
    assert.equal(lt.requests[0].url, '/v2/check');
    assert.equal(lt.requests[0].language, 'auto');
  } finally { await lt.close(); }
});

test('слишком длинный текст обрезается и помечается', async () => {
  const lt = await startLanguageTool();
  try {
    const r = await check('a'.repeat(MAX_CHARS + 50), { server: lt.url + '///' });
    assert.equal(r.truncated, true);
    assert.equal(r.text.length, MAX_CHARS);
  } finally { await lt.close(); }
});

test('ошибка сервера превращается в исключение', async () => {
  await assert.rejects(check('x', {}, async () => ({ ok: false, status: 503 })), /HTTP 503/);
});

test('applyOne заменяет один фрагмент', () => {
  assert.equal(applyOne('Ths is', { offset: 0, length: 3 }, 'This'), 'This is');
});

test('applyFirstSuggestions применяет с конца и не ломает смещения', () => {
  const text = 'Ths is a tset';
  const matches = [
    { offset: 0, length: 3, replacements: ['This'] },
    { offset: 9, length: 4, replacements: ['test'] },
  ];
  assert.equal(applyFirstSuggestions(text, matches), 'This is a test');
});

test('applyFirstSuggestions пропускает пересекающиеся и без подсказок', () => {
  const text = 'abcdef';
  const matches = [
    { offset: 0, length: 4, replacements: ['X'] },
    { offset: 2, length: 3, replacements: ['Y'] },    // пересекается с последним применённым куском: пропущено
    { offset: 5, length: 1, replacements: [] },       // нечем заменить
  ];
  assert.equal(applyFirstSuggestions(text, matches), 'abcdef'.slice(0, 2) + 'Y' + 'f'.slice(0));
});
