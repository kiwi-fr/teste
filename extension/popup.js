/* Popup Coclico AdBlock. */
(async function () {
  const masterEl = document.getElementById('master');
  const shieldEl = document.getElementById('shield');
  const aggrEl = document.getElementById('aggr');
  const aggrWarn = document.getElementById('aggrWarn');
  const scriptletsEl = document.getElementById('scriptlets');
  const siteEl = document.getElementById('siteOff');
  const pickerEl = document.getElementById('picker');
  const countEl = document.getElementById('count');
  const countTotalEl = document.getElementById('countTotal');
  const cookieCountEl = document.getElementById('cookieCount');
  const catsEl = document.getElementById('cats');
  const siteLabel = document.getElementById('site');
  const netlogEl = document.getElementById('netlog');

  const state = await chrome.runtime.sendMessage({ type: 'getState' });
  masterEl.checked = state.settings.enabled !== false;
  shieldEl.checked = state.settings.networkShield !== false;
  aggrEl.checked = state.settings.aggressiveMode === true;
  aggrWarn.hidden = !aggrEl.checked;
  scriptletsEl.checked = state.settings.scriptlets !== false;
  countEl.textContent = String(state.settings.stats.blockedToday || 0);
  countTotalEl.textContent = String(state.settings.stats.totalBlocked || 0);
  cookieCountEl.textContent = String(state.settings.stats.cookiesRemovedToday || 0);

  const byCat = state.settings.stats.byCategory || {};
  const catParts = Object.entries(byCat).sort((a, b) => b[1] - a[1]);
  catsEl.innerHTML = catParts.length
    ? 'Aujourd’hui : ' + catParts.map(([c, n]) => '<b>' + n + '</b> ' + c).join(' · ')
    : 'Aucune catégorie comptabilisée aujourd’hui.';

  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  const domain = tab && tab.url ? new URL(tab.url).hostname : '';
  siteLabel.textContent = domain;
  siteEl.checked = state.settings.siteOverrides[domain] === true;
  siteEl.disabled = !domain || !/^https?:/.test(tab.url || '');

  masterEl.addEventListener('change', async () => {
    await chrome.storage.local.set({ enabled: masterEl.checked });
  });
  shieldEl.addEventListener('change', async () => {
    await chrome.storage.local.set({ networkShield: shieldEl.checked });
  });
  aggrEl.addEventListener('change', async () => {
    aggrWarn.hidden = !aggrEl.checked;
    await chrome.storage.local.set({ aggressiveMode: aggrEl.checked });
  });
  scriptletsEl.addEventListener('change', async () => {
    await chrome.storage.local.set({ scriptlets: scriptletsEl.checked });
  });
  siteEl.addEventListener('change', async () => {
    if (!domain) return;
    await chrome.runtime.sendMessage({ type: 'toggleSite', domain });
  });
  pickerEl.addEventListener('change', async () => {
    await chrome.storage.local.set({ pickerMode: pickerEl.checked });
  });

  async function renderNetLog() {
    try {
      const { log } = await chrome.runtime.sendMessage({ type: 'getNetLog' });
      netlogEl.textContent = '';
      if (!log || !log.length) {
        const li = document.createElement('li');
        li.className = 'empty';
        li.textContent = 'Aucune requête interceptée pour le moment.';
        netlogEl.appendChild(li);
        return;
      }
      for (const e of log.slice(0, 15)) {
        const li = document.createElement('li');
        const h = document.createElement('span');
        h.className = 'h';
        h.textContent = e.host;
        h.title = e.host + ' (' + (e.type || '') + ')';
        const cat = document.createElement('span');
        const catName = e.cat || 'Autre';
        cat.className = 'cat cat-' + catName.replace(/\s+/g, '-');
        cat.textContent = catName;
        const t = document.createElement('span');
        t.className = 't';
        t.textContent = e.type;
        li.appendChild(h);
        li.appendChild(cat);
        li.appendChild(t);
        netlogEl.appendChild(li);
      }
    } catch (_) { }
  }

  renderNetLog();
  const netlogTimer = setInterval(renderNetLog, 2000);
  window.addEventListener('unload', () => clearInterval(netlogTimer));

  document.getElementById('options').addEventListener('click', () => {
    chrome.runtime.openOptionsPage();
  });
})();
