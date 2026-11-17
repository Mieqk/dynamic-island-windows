// Фоновый сервис-воркер: контекстное меню и горячие клавиши. Всё запускается только действием пользователя.
import { sendNote, sendCheck } from './bridge.js';
import { check, applyFirstSuggestions } from './languagetool.js';
import { load } from './settings.js';
import { t, islandMessage } from './i18n.js';

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({ id: 'atoll-check', title: t('menuCheck'), contexts: ['selection'] });
    chrome.contextMenus.create({ id: 'atoll-note', title: t('menuNote'), contexts: ['selection'] });
  });
});

chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId === 'atoll-check') void runCheck(tab, info.selectionText);
  if (info.menuItemId === 'atoll-note') void runNote(tab, info.selectionText);
});

chrome.commands.onCommand.addListener(async (command, tab) => {
  // some browsers do not pass the tab to a keyboard command: take the active one
  tab ??= (await chrome.tabs.query({ active: true, currentWindow: true }))[0];
  if (!tab) return;
  const text = await selectionOf(tab);
  if (command === 'check-selection') await runCheck(tab, text);
  if (command === 'save-note') await runNote(tab, text);
});

async function selectionOf(tab) {
  try {
    const [res] = await chrome.scripting.executeScript({ target: { tabId: tab.id }, func: () => String(getSelection() ?? '') });
    return res?.result ?? '';
  } catch { return ''; }   // страница, куда нельзя вставлять скрипты (chrome://, магазин расширений)
}

const labels = () => ({
  close: t('btnClose'), noIssues: t('noIssues'), copyFixed: t('btnCopyFixed'), copied: t('copied'), truncated: t('truncated'),
});

async function showOverlay(tab, payload) {
  try {
    await chrome.scripting.executeScript({ target: { tabId: tab.id }, func: (p) => { window.__atollOverlay = p; }, args: [{ ...payload, labels: labels() }] });
    await chrome.scripting.executeScript({ target: { tabId: tab.id }, files: ['src/overlay.js'] });
  } catch { flash('!', '#ff453a'); }
}

function flash(text, color) {
  chrome.action.setBadgeBackgroundColor({ color });
  chrome.action.setBadgeText({ text });
  setTimeout(() => chrome.action.setBadgeText({ text: '' }), 2500);
}

async function runNote(tab, text) {
  text = (text ?? '').trim();
  if (!text) return showOverlay(tab, { title: 'Atoll', error: t('noText'), issues: [] });
  const { key } = await load();
  const res = await sendNote({ text, title: tab?.title ?? '', url: tab?.url ?? '' }, key);
  if (res.ok) flash('✓', '#30a46c');
  else showOverlay(tab, { title: 'Atoll', error: islandMessage(res), issues: [] });
}

async function runCheck(tab, text) {
  text = (text ?? '').trim();
  if (!text) return showOverlay(tab, { title: 'LanguageTool', error: t('noText'), issues: [] });
  const cfg = await load();
  let result;
  try {
    result = await check(text, { server: cfg.server, lang: cfg.lang });
  } catch (e) {
    return showOverlay(tab, { title: 'LanguageTool', error: t('ltError', e.message), issues: [] });
  }
  void sendCheck(result.matches.length, result.lang, cfg.key);   // остров покажет итог; если он выключен, не страшно
  await showOverlay(tab, {
    title: t('issuesCount', result.matches.length),
    issues: result.matches.map((m) => ({
      bad: result.text.slice(m.offset, m.offset + m.length), message: m.message, suggestions: m.replacements,
    })),
    corrected: applyFirstSuggestions(result.text, result.matches),
    truncated: result.truncated,
  });
}
