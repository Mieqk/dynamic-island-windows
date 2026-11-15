// Клиент LanguageTool (https://languagetool.org). Текст уходит на выбранный сервер только по нажатию пользователя.
export const DEFAULT_SERVER = 'https://api.languagetool.org';
export const MAX_CHARS = 20000;   // лимит бесплатного сервера

/** Проверка текста. Возвращает { text, truncated, lang, matches }, где matches: { offset, length, message, replacements[] }. */
export async function check(text, { server = DEFAULT_SERVER, lang = 'auto' } = {}, fetchImpl = fetch) {
  const clipped = text.slice(0, MAX_CHARS);
  const res = await fetchImpl(server.replace(/\/+$/, '') + '/v2/check', {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded', Accept: 'application/json' },
    body: new URLSearchParams({ text: clipped, language: lang || 'auto' }),
    signal: AbortSignal.timeout(15000),
  });
  if (!res.ok) throw new Error('HTTP ' + res.status);
  const data = await res.json();
  return {
    text: clipped,
    truncated: text.length > MAX_CHARS,
    lang: data.language?.detectedLanguage?.code ?? data.language?.code ?? '',
    matches: (data.matches ?? []).map((m) => ({
      offset: m.offset,
      length: m.length,
      message: m.message ?? m.shortMessage ?? '',
      replacements: (m.replacements ?? []).slice(0, 5).map((r) => r.value),
    })),
  };
}

/** Заменяет один фрагмент. */
export function applyOne(text, match, value) {
  return text.slice(0, match.offset) + value + text.slice(match.offset + match.length);
}

/** Применяет первую подсказку к каждому замечанию (с конца, чтобы смещения не сбивались; пересекающиеся пропускает). */
export function applyFirstSuggestions(text, matches) {
  let out = text;
  let limit = Infinity;   // начало уже изменённого куска
  for (const m of [...matches].sort((a, b) => b.offset - a.offset)) {
    if (!m.replacements.length || m.offset + m.length > limit) continue;
    out = applyOne(out, m, m.replacements[0]);
    limit = m.offset;
  }
  return out;
}
