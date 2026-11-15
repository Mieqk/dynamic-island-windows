import { ping, sendNote, sendCheck } from './bridge.js';
import { check, applyOne, applyFirstSuggestions } from './languagetool.js';
import { load } from './settings.js';
import { t, islandMessage } from './i18n.js';

const $ = (id) => document.getElementById(id);
const status = (text, cls = '') => { $('status').textContent = text; $('status').className = 'status ' + cls; };

$('title').textContent = t('popupTitle');
$('check').textContent = t('btnCheck');
$('note').textContent = t('btnNote');
$('options').textContent = t('btnSettings');
$('copy').textContent = t('btnCopyFixed');
$('text').placeholder = t('popupHint');
$('options').onclick = () => chrome.runtime.openOptionsPage();

let cfg;
let last = null;   // последний результат проверки

(async () => {
  cfg = await load();
  // выделенный на странице текст подставляется сам (кнопку расширения нажал человек, так что страница доступна)
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    const [res] = await chrome.scripting.executeScript({ target: { tabId: tab.id }, func: () => String(getSelection() ?? '') });
    if (res?.result?.trim()) $('text').value = res.result.trim();
  } catch { /* служебная страница */ }
  const res = await ping(cfg.key);
  if (res.ok) status(t('islandOk'), 'ok'); else status(islandMessage(res), res.reason === 'nokey' ? '' : 'err');
})();

function render(result) {
  last = result;
  const box = $('results');
  box.textContent = '';
  $('copy').hidden = !result.matches.some((m) => m.replacements.length);
  if (!result.matches.length) { status(t('noIssues'), 'ok'); return; }
  status(t('issuesCount', result.matches.length) + (result.truncated ? ' · ' + t('truncated') : ''));
  for (const m of result.matches) {
    const div = document.createElement('div');
    div.className = 'issue';
    const bad = document.createElement('div');
    bad.className = 'bad';
    bad.textContent = result.text.slice(m.offset, m.offset + m.length) || '…';
    const msg = document.createElement('div');
    msg.className = 'msg';
    msg.textContent = m.message;
    div.append(bad, msg);
    for (const s of m.replacements) {
      const chip = document.createElement('button');
      chip.className = 'sug';
      chip.textContent = s;
      chip.onclick = () => { $('text').value = applyOne(result.text, m, s); void run(); };   // заменили и проверили заново
      div.append(chip);
    }
    box.append(div);
  }
}

async function run() {
  const text = $('text').value.trim();
  if (!text) { status(t('noText')); return; }
  status(t('checking'));
  try {
    const result = await check(text, { server: cfg.server, lang: cfg.lang });
    render(result);
    void sendCheck(result.matches.length, result.lang, cfg.key);
  } catch (e) {
    status(t('ltError', e.message), 'err');
  }
}

$('check').onclick = run;

$('note').onclick = async () => {
  const text = $('text').value.trim();
  if (!text) { status(t('noText')); return; }
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  const res = await sendNote({ text, title: tab?.title ?? '', url: tab?.url ?? '' }, cfg.key);
  if (res.ok) status(t('savedNote'), 'ok'); else status(islandMessage(res), 'err');
};

$('copy').onclick = async () => {
  if (!last) return;
  try { await navigator.clipboard.writeText(applyFirstSuggestions(last.text, last.matches)); status(t('copied'), 'ok'); }
  catch { status('!', 'err'); }
};
