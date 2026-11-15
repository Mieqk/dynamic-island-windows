// Короткая обёртка над chrome.i18n: "ключ" и подстановки.
export const t = (key, ...subs) => chrome.i18n.getMessage(key, subs.map(String)) || key;

/** Текст ошибки связи с островом по коду причины из bridge.js. */
export function islandMessage(res) {
  switch (res.reason) {
    case 'offline': return t('islandOffline');
    case 'disabled': return t('islandDisabled');
    case 'unauthorized': return t('islandUnauthorized');
    case 'nokey': return t('islandNoKey');
    default: return t('islandHttp', res.status ?? '?');
  }
}
