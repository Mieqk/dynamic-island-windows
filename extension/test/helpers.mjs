// Мок-серверы для тестов: LanguageTool и «остров» с теми же правилами доступа, что в Services/Agents/ExtensionBridge.cs.
import http from 'node:http';

const listen = (server, port) => new Promise((resolve) => server.listen(port, 'localhost', () => resolve(server)));
const readBody = (req) => new Promise((resolve) => { let s = ''; req.on('data', (c) => (s += c)); req.on('end', () => resolve(s)); });

/** LanguageTool: на «Ths is a tset» находит две ошибки. */
export async function startLanguageTool() {
  const requests = [];
  const server = http.createServer(async (req, res) => {
    const body = new URLSearchParams(await readBody(req));
    requests.push({ url: req.url, text: body.get('text'), language: body.get('language') });
    res.setHeader('Access-Control-Allow-Origin', '*');   // как у настоящего сервера
    res.setHeader('Content-Type', 'application/json');
    if (req.method === 'OPTIONS') { res.statusCode = 204; return res.end(); }
    const text = body.get('text') ?? '';
    const matches = [];
    for (const [bad, good] of [['Ths', 'This'], ['tset', 'test']]) {
      const i = text.indexOf(bad);
      if (i >= 0) matches.push({ message: `Опечатка: «${bad}»`, offset: i, length: bad.length, replacements: [{ value: good }, { value: good + 's' }] });
    }
    res.end(JSON.stringify({ language: { code: 'en-US', detectedLanguage: { code: 'en-US' } }, matches }));
  });
  await listen(server, 0);
  return { server, requests, url: `http://localhost:${server.address().port}`, close: () => new Promise((r) => { server.closeAllConnections?.(); server.close(r); }) };
}

const EXT_ORIGIN = /^(chrome|moz)-extension:\/\/[a-z0-9-]{8,64}$/;

/** «Остров»: включается флагом, проверяет Origin расширения и ключ, ведёт журнал заметок и проверок. */
export async function startIsland({ port = 0, token = 'a'.repeat(64), enabled = true } = {}) {
  const state = { token, enabled, notes: [], checks: [], rejected: [] };
  const server = http.createServer(async (req, res) => {
    const origin = req.headers.origin;
    const reply = (status, body) => {
      res.statusCode = status;
      if (origin && EXT_ORIGIN.test(origin)) {
        res.setHeader('Access-Control-Allow-Origin', origin);
        res.setHeader('Vary', 'Origin');
        res.setHeader('Access-Control-Allow-Methods', 'POST, OPTIONS');
        res.setHeader('Access-Control-Allow-Headers', 'Content-Type, X-Island-Ext-Token');
        res.setHeader('Access-Control-Allow-Private-Network', 'true');
      }
      if (body) res.setHeader('Content-Type', 'application/json');
      res.end(body ?? '');
    };
    if (!origin || !EXT_ORIGIN.test(origin)) { state.rejected.push('origin'); return reply(403); }
    if (req.method === 'OPTIONS') return reply(204);
    if (!state.enabled) return reply(403, '{"error":"disabled"}');
    const body = await readBody(req);
    const json = (() => { try { return JSON.parse(body); } catch { return null; } })();
    if (req.method !== 'POST' || !(req.headers['content-type'] ?? '').startsWith('application/json') ||
        req.headers['x-island-ext-token'] !== state.token) { state.rejected.push('token'); return reply(401, '{"error":"unauthorized"}'); }
    if (!json) return reply(400, '{"error":"bad json"}');
    if (req.url === '/ext/ping') return reply(200, '{"ok":true,"app":"Atoll"}');
    if (req.url === '/ext/note') { if (!json.text) return reply(400, '{"error":"empty"}'); state.notes.push(json); return reply(200, '{"ok":true}'); }
    if (req.url === '/ext/check') { state.checks.push(json); return reply(200, '{"ok":true}'); }
    return reply(404, '{"error":"not found"}');
  });
  await listen(server, port);
  return { server, state, port: server.address().port, close: () => new Promise((r) => { server.closeAllConnections?.(); server.close(r); }) };
}

/** fetch, который подписывается Origin расширения (как браузер в сервис-воркере) и ходит на порт мок-острова. */
export const asExtension = (port, origin = 'chrome-extension://abcdefghijklmnopabcdefghijklmnop') => (url, init = {}) =>
  fetch(String(url).replace(':47811', ':' + port), { ...init, headers: { ...init.headers, Origin: origin } });
