/* Page d'options de Coclico AdBlock. */
(async function () {
  const listsDiv = document.getElementById('lists');
  const customEl = document.getElementById('customFilters');
  const whitelistEl = document.getElementById('whitelist');
  const metaEl = document.getElementById('meta');
  const savedEl = document.getElementById('saved');

  const state = await chrome.runtime.sendMessage({ type: 'getState' });
  const settings = state.settings;
  const ruleMeta = state.ruleMeta;

  for (const list of COCLICO_LISTS) {
    const label = document.createElement('label');
    const cb = document.createElement('input');
    cb.type = 'checkbox';
    cb.checked = settings.lists[list.id] !== false;
    cb.dataset.listId = list.id;
    label.appendChild(cb);
    label.appendChild(document.createTextNode(' ' + list.name + (ruleMeta && ruleMeta.perList ? ' (' + ruleMeta.perList[list.id] + ')' : '')));
    listsDiv.appendChild(label);
  }

  customEl.value = settings.customFilters || '';
  whitelistEl.value = Object.keys(settings.siteOverrides || {}).filter(d => settings.siteOverrides[d]).join('\n');

  if (ruleMeta) {
    metaEl.textContent = 'Règles dynamiques: ' + ruleMeta.total + ' — total bloqué (extension): ' + (settings.stats.totalBlocked || 0) + ' — mises à jour: ' + new Date(ruleMeta.updated).toLocaleString();
  } else {
    metaEl.textContent = 'Les listes seront téléchargées à la prochaine mise à jour.';
  }

  document.getElementById('save').addEventListener('click', async () => {
    const lists = {};
    listsDiv.querySelectorAll('input[type=checkbox]').forEach(cb => { lists[cb.dataset.listId] = cb.checked; });
    const siteOverrides = {};
    whitelistEl.value.split(/\n|,|;/).map(s => s.trim().toLowerCase()).filter(Boolean).forEach(d => { siteOverrides[d] = true; });
    await chrome.storage.local.set({ lists, customFilters: customEl.value, siteOverrides });
    savedEl.textContent = 'Enregistré.';
    setTimeout(() => { savedEl.textContent = ''; }, 2000);
  });

  document.getElementById('refresh').addEventListener('click', async () => {
    savedEl.textContent = 'Mise à jour des listes…';
    await chrome.runtime.sendMessage({ type: 'refreshRules' });
    location.reload();
  });
})();
