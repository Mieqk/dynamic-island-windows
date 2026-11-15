// Карточка с результатом на странице. Подставляется фоном (chrome.scripting) после проверки;
// данные лежат в window.__atollOverlay. Всё внутри закрытого shadow DOM: стили страницы не мешают.
(() => {
  const data = window.__atollOverlay;
  delete window.__atollOverlay;
  if (!data) return;

  document.getElementById('atoll-overlay-host')?.remove();
  const host = document.createElement('div');
  host.id = 'atoll-overlay-host';
  host.style.cssText = 'all:initial;position:fixed;top:16px;right:16px;z-index:2147483647;';
  const root = host.attachShadow({ mode: 'closed' });

  const style = document.createElement('style');
  style.textContent = `
    .card{font:13px/1.45 system-ui,sans-serif;color:#f2f2f7;background:#1c1c1e;border:1px solid #3a3a3c;border-radius:14px;
      width:340px;max-height:70vh;overflow:auto;padding:12px 14px;box-shadow:0 12px 40px rgba(0,0,0,.45)}
    .head{display:flex;align-items:center;gap:8px;margin-bottom:8px}.head b{flex:1;font-size:13px}
    button{font:inherit;color:inherit;background:#2c2c2e;border:1px solid #48484a;border-radius:999px;padding:4px 10px;cursor:pointer}
    button:hover{border-color:#6fc3a1}
    .issue{border-top:1px solid #2c2c2e;padding:8px 0}.bad{color:#ff9f0a;font-weight:600}.msg{color:#aeaeb2;margin:2px 0 4px}
    .sug{display:inline-block;margin:2px 4px 0 0;color:#6fc3a1;background:#1f2d27;border-radius:999px;padding:1px 8px}
    .err{color:#ff453a}.ok{color:#6fc3a1}.note{color:#aeaeb2;font-size:12px;margin-top:6px}`;
  root.append(style);

  const card = document.createElement('div');
  card.className = 'card';
  const head = document.createElement('div');
  head.className = 'head';
  const title = document.createElement('b');
  title.textContent = data.title;
  const close = document.createElement('button');
  close.textContent = '×';
  close.title = data.labels.close;
  close.onclick = () => host.remove();
  head.append(title, close);
  card.append(head);

  const addLine = (cls, text) => { const p = document.createElement('div'); p.className = cls; p.textContent = text; card.append(p); return p; };

  if (data.error) {
    addLine('err', data.error);
  } else if (!data.issues.length) {
    addLine('ok', data.labels.noIssues);
  } else {
    for (const it of data.issues) {
      const box = document.createElement('div');
      box.className = 'issue';
      const bad = document.createElement('div');
      bad.className = 'bad';
      bad.textContent = it.bad;
      const msg = document.createElement('div');
      msg.className = 'msg';
      msg.textContent = it.message;
      box.append(bad, msg);
      for (const s of it.suggestions) {
        const chip = document.createElement('span');
        chip.className = 'sug';
        chip.textContent = s;
        box.append(chip);
      }
      card.append(box);
    }
    const copy = document.createElement('button');
    copy.textContent = data.labels.copyFixed;
    copy.onclick = async () => {
      try { await navigator.clipboard.writeText(data.corrected); copy.textContent = data.labels.copied; }
      catch { copy.textContent = '!'; }
    };
    card.append(copy);
  }
  if (data.truncated) addLine('note', data.labels.truncated);

  root.append(card);
  document.documentElement.append(host);
  setTimeout(() => host.remove(), 60000);
})();
