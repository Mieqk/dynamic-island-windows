import { ping } from './bridge.js';
import { DEFAULT_SERVER } from './languagetool.js';
import { load, save } from './settings.js';
import { t, islandMessage } from './i18n.js';

const $ = (id) => document.getElementById(id);
const status = (text, cls = '') => { $('status').textContent = text; $('status').className = 'status ' + cls; };

$('title').textContent = t('optTitle');
$('keyLabel').textContent = t('optKey');
$('keyHelp').textContent = t('optKeyHelp');
$('serverLabel').textContent = t('optServer');
$('serverHelp').textContent = t('optServerHelp');
$('langLabel').textContent = t('optLang');
$('save').textContent = t('optSave');
$('privacy').textContent = t('optPrivacy');

const LANGS = ['ru-RU', 'en-US', 'en-GB', 'uk-UA', 'de-DE', 'es', 'fr', 'pt-PT', 'pl-PL'];
for (const code of ['auto', ...LANGS]) {
  const o = document.createElement('option');
  o.value = code;
  o.textContent = code === 'auto' ? t('optLangAuto') : code;
  $('lang').append(o);
}

(async () => {
  const cfg = await load();
  $('key').value = cfg.key;
  $('server').value = cfg.server;
  $('lang').value = cfg.lang;
})();

$('save').onclick = async () => {
  const key = $('key').value.trim();
  const server = ($('server').value.trim() || DEFAULT_SERVER).replace(/\/+$/, '');
  let origin;
  try { origin = new URL(server).origin; } catch { status(t('optNeedPermission'), 'err'); return; }

  // свой сервер: браузер должен разрешить к нему доступ (вызов из нажатия кнопки)
  if (origin !== new URL(DEFAULT_SERVER).origin) {
    const granted = await chrome.permissions.request({ origins: [origin + '/*'] });
    if (!granted) { status(t('optNeedPermission'), 'err'); return; }
  }
  await save({ key, server, lang: $('lang').value });
  const res = await ping(key);
  if (res.ok) status(t('optSaved') + ' ' + t('islandOk'), 'ok'); else status(t('optSaved') + ' ' + islandMessage(res), res.reason === 'nokey' ? '' : 'err');
};
