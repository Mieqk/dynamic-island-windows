// Настройки расширения (chrome.storage.local: ключ острова лежит только в этом браузере и не синхронизируется).
import { DEFAULT_SERVER } from './languagetool.js';

export const DEFAULTS = { key: '', server: DEFAULT_SERVER, lang: 'auto' };

export async function load(storage = chrome.storage.local) {
  return { ...DEFAULTS, ...(await storage.get(Object.keys(DEFAULTS))) };
}

export async function save(values, storage = chrome.storage.local) {
  await storage.set(values);
}
