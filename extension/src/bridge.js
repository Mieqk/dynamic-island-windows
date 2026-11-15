// Связь с островом Atoll по localhost. Остров принимает расширение только если связь включена в его
// настройках и ключ совпадает; сайты на этот маршрут попасть не могут (нужен Origin расширения и ключ).
export const ISLAND = 'http://localhost:47811';

/** Один вызов; никогда не бросает исключение: возвращает { ok, reason }. */
async function call(path, body, key, fetchImpl = fetch) {
  if (!key) return { ok: false, reason: 'nokey' };
  let res;
  try {
    res = await fetchImpl(ISLAND + path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Island-Ext-Token': key },
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(3000),
    });
  } catch {
    return { ok: false, reason: 'offline' };
  }
  if (res.status === 200) return { ok: true };
  if (res.status === 403) return { ok: false, reason: 'disabled' };
  if (res.status === 401) return { ok: false, reason: 'unauthorized' };
  return { ok: false, reason: 'http', status: res.status };
}

export const ping = (key, f) => call('/ext/ping', {}, key, f);
export const sendNote = (note, key, f) => call('/ext/note', note, key, f);
export const sendCheck = (issues, lang, key, f) => call('/ext/check', { issues, lang }, key, f);
