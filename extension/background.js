/* Coclico AdBlock - service worker MV3 (Chrome) / event page (Firefox/Zen via background.scripts). */
if (typeof importScripts === 'function') importScripts('filters.js');

const DYNAMIC_ID_BASE = 1000;
const SESSION_SWITCH_ID = 1;
const SESSION_SITE_ID_BASE = 100;
const SESSION_PARAM_ID_A = 500;
const SESSION_PARAM_ID_B = 501;
const MAX_DYNAMIC_RULES = 30000;
const NETLOG_MAX = 50;

const TRACKING_PARAMS = 'utm_[a-z0-9_]+|gclid|fbclid|msclkid|mc_cid|mc_eid|dclid|igshid|yclid|twclid|li_fat_id|_ga|wickedid';

const netLog = [];
let netLogFlushTimer = null;

async function getSettings() {
  const s = await chrome.storage.local.get({
    enabled: true,
    networkShield: true,
    aggressiveMode: false,
    scriptlets: true,
    lists: Object.fromEntries(COCLICO_LISTS.map(l => [l.id, true])),
    siteOverrides: {},
    customFilters: '',
    stats: { blockedToday: 0, cookiesRemovedToday: 0, totalBlocked: 0, byCategory: {}, date: new Date().toDateString() }
  });
  if (s.stats.date !== new Date().toDateString()) {
    s.stats.blockedToday = 0;
    s.stats.cookiesRemovedToday = 0;
    s.stats.byCategory = {};
    s.stats.date = new Date().toDateString();
  }
  s.stats.totalBlocked = s.stats.totalBlocked || 0;
  s.stats.byCategory = s.stats.byCategory || {};
  return s;
}

async function refreshDynamicRules() {
  const settings = await getSettings();
  const previous = (await chrome.storage.local.get({ dynamicRuleIds: [] })).dynamicRuleIds;

  const addRules = [];
  let idBase = DYNAMIC_ID_BASE;
  const perList = {};
  const cosmetic = { global: [], byDomain: {} };
  const mergeCosmetic = (cos) => {
    for (const sel of cos.global) cosmetic.global.push(sel);
    for (const d of Object.keys(cos.byDomain)) {
      (cosmetic.byDomain[d] ??= []).push(...cos.byDomain[d]);
    }
  };

  for (const list of COCLICO_LISTS) {
    if (!settings.lists[list.id]) continue;
    try {
      const resp = await fetch(list.url, { cache: 'no-store' });
      if (!resp.ok) throw new Error('HTTP ' + resp.status);
      const text = await resp.text();
      const { rules } = convertListToDnr(text, list.format, idBase);
      const cos = convertCosmeticToCss(text);
      mergeCosmetic(cos);
      perList[list.id] = rules.length + ' réseau + ' + (cos.global.length + Object.keys(cos.byDomain).length) + ' cosm.';
      if (addRules.length + rules.length > MAX_DYNAMIC_RULES) {
        rules.length = Math.max(0, MAX_DYNAMIC_RULES - addRules.length);
      }
      addRules.push(...rules);
      idBase += 30000;
    } catch (e) {
      perList[list.id] = 'erreur: ' + e.message;
    }
  }

  if (settings.customFilters.trim()) {
    const custom = convertListToDnr(settings.customFilters, 'abp', idBase);
    const customCos = convertCosmeticToCss(settings.customFilters);
    mergeCosmetic(customCos);
    addRules.push(...custom.rules.slice(0, Math.max(0, MAX_DYNAMIC_RULES - addRules.length)));
    perList['custom'] = custom.rules.length + ' réseau + ' + (customCos.global.length + Object.keys(customCos.byDomain).length) + ' cosm.';
  }

  try { await chrome.declarativeNetRequest.updateDynamicRules({ removeRuleIds: previous, addRules: [] }); } catch (_) { }
  const CHUNK = 4000;
  let added = 0;
  for (let i = 0; i < addRules.length; i += CHUNK) {
    try {
      await chrome.declarativeNetRequest.updateDynamicRules({ removeRuleIds: [], addRules: addRules.slice(i, i + CHUNK) });
      added += Math.min(CHUNK, addRules.length - i);
    } catch (e) {
      perList['quota@' + i] = 'arrêt: ' + e.message;
      break;
    }
  }
  await chrome.storage.local.set({
    dynamicRuleIds: addRules.slice(0, added).map(r => r.id),
    cosmetic,
    ruleMeta: { updated: new Date().toISOString(), perList, total: added }
  });
}

async function applySessionRules() {
  const settings = await getSettings();
  const removeIds = [SESSION_SWITCH_ID];
  const addRules = [];
  if (!settings.enabled) {
    addRules.push({ id: SESSION_SWITCH_ID, priority: 1000000, action: { type: 'allowAllRequests' }, condition: {} });
  }
  let siteId = SESSION_SITE_ID_BASE;
  for (const domain of Object.keys(settings.siteOverrides)) {
    if (!settings.siteOverrides[domain]) continue;
    removeIds.push(siteId);
    addRules.push({ id: siteId++, priority: 1000000, action: { type: 'allowAllRequests' }, condition: { initiatorDomains: [domain] } });
  }
  await chrome.declarativeNetRequest.updateSessionRules({ removeRuleIds: removeIds, addRules });
}

async function applyNetworkShield() {
  const settings = await getSettings();
  const on = settings.enabled !== false && settings.networkShield !== false;
  await chrome.declarativeNetRequest.updateEnabledRulesets({
    enableRulesetIds: on ? ['trackers'] : [],
    disableRulesetIds: on ? [] : ['trackers']
  });
  const allTypes = DNR_ALL_TYPES.filter(t => t !== 'main_frame' && t !== 'other');
  const param = '([?&])(' + TRACKING_PARAMS + ')=[^&]*';
  const addRules = on ? [
    { id: SESSION_PARAM_ID_A, priority: 1, action: { type: 'redirect', redirect: { regexSubstitution: '$1' } }, condition: { regexFilter: param + '&', resourceTypes: allTypes } },
    { id: SESSION_PARAM_ID_B, priority: 1, action: { type: 'redirect', redirect: { regexSubstitution: '' } }, condition: { regexFilter: param + '($|#)', resourceTypes: allTypes } }
  ] : [];
  const SESSION_IFRAME_ID_BASE = 600;
  const iframeRemove = [];
  if (on && IS_FIREFOX) {
    let iid = SESSION_IFRAME_ID_BASE;
    for (const d of COCLICO_IFRAME_BLOCK_DOMAINS) {
      iframeRemove.push(iid);
      addRules.push({
        id: iid++,
        priority: 2,
        action: { type: 'block' },
        condition: { urlFilter: '||' + d + '^', resourceTypes: ['sub_frame'] }
      });
    }
  }
  await chrome.declarativeNetRequest.updateSessionRules({
    removeRuleIds: [SESSION_PARAM_ID_A, SESSION_PARAM_ID_B, ...iframeRemove],
    addRules
  });
}

async function applyAggressiveMode() {
  const settings = await getSettings();
  const on = settings.enabled !== false && settings.aggressiveMode === true;
  await chrome.declarativeNetRequest.updateEnabledRulesets({
    enableRulesetIds: on ? ['aggressive'] : [],
    disableRulesetIds: on ? [] : ['aggressive']
  });
}

/* Scriptlets (monde MAIN) pour les contenus first-party : YouTube, Twitch… */
async function applyScriptlets() {
  const settings = await getSettings();
  const on = settings.enabled !== false && settings.scriptlets !== false;
  const id = 'coclico-scriptlets';
  try {
    const existing = await chrome.scripting.getRegisteredContentScripts({ ids: [id] });
    if (existing.length) await chrome.scripting.unregisterContentScripts({ ids: [id] });
    if (on) {
      await chrome.scripting.registerContentScripts([{
        id,
        matches: ['http://*/*', 'https://*/*'], // tous les sites : partie générique toujours active
        js: ['scriptlets.js'],
        runAt: 'document_start',
        world: 'MAIN',
        allFrames: false
      }]);
    }
  } catch (_) { /* world MAIN indisponible : scriptlets désactivés */ }
}

async function sweepTrackingCookies() {
  if (!chrome.cookies || !chrome.cookies.getAll) return 0;
  let removed = 0;
  try {
    const all = await chrome.cookies.getAll({});
    for (const c of all) {
      const name = (c.name || '').toLowerCase();
      const host = (c.domain || '').replace(/^\./, '').toLowerCase();
      const byName = COCLICO_TRACKING_COOKIE_PREFIXES.some(p => name === p || name.startsWith(p));
      const byDomain = COCLICO_TRACKER_DOMAINS.some(d => host === d || host.endsWith('.' + d));
      if (!byName && !byDomain) continue;
      const url = (c.secure ? 'https://' : 'http://') + host + (c.path || '/');
      try { await chrome.cookies.remove({ url, name: c.name }); removed++; } catch (_) { }
    }
    if (removed > 0) {
      const s = await getSettings();
      s.stats.cookiesRemovedToday = (s.stats.cookiesRemovedToday || 0) + removed;
      await chrome.storage.local.set({ stats: s.stats });
    }
  } catch (_) { }
  return removed;
}

function pushNetLog(entry) {
  netLog.unshift(entry);
  if (netLog.length > NETLOG_MAX) netLog.length = NETLOG_MAX;
  if (!netLogFlushTimer) {
    netLogFlushTimer = setTimeout(async () => {
      netLogFlushTimer = null;
      try { await chrome.storage.local.set({ netLog: netLog.slice(0, NETLOG_MAX) }); } catch (_) { }
    }, 1500);
  }
}

async function bumpBlockedCounter(tabId, host) {
  try {
    const s = await getSettings();
    s.stats.blockedToday += 1;
    s.stats.totalBlocked += 1;
    const cat = categoryOf(host);
    s.stats.byCategory[cat] = (s.stats.byCategory[cat] || 0) + 1;
    await chrome.storage.local.set({ stats: s.stats });
    if (typeof tabId === 'number' && tabId >= 0) {
      await chrome.action.setBadgeBackgroundColor({ tabId, color: '#c0392b' });
      await chrome.action.setBadgeText({ tabId, text: String(s.stats.totalBlocked) });
    }
  } catch (_) { }
}

chrome.runtime.onInstalled.addListener(async () => {
  await refreshDynamicRules();
  await applySessionRules();
  await applyNetworkShield();
  await applyAggressiveMode();
  await applyScriptlets();
  await chrome.alarms.create('coclico-cookie-sweep', { periodInMinutes: 5 });
  await sweepTrackingCookies();
});

chrome.runtime.onStartup.addListener(async () => {
  await applySessionRules();
  await applyNetworkShield();
  await applyAggressiveMode();
  await applyScriptlets();
  await chrome.alarms.create('coclico-cookie-sweep', { periodInMinutes: 5 });
  await sweepTrackingCookies();
  const meta = await chrome.storage.local.get({ ruleMeta: null });
  if (!meta.ruleMeta) await refreshDynamicRules();
});

chrome.storage.onChanged.addListener((changes, area) => {
  if (area !== 'local') return;
  if (changes.enabled || changes.siteOverrides) applySessionRules();
  if (changes.enabled || changes.networkShield) applyNetworkShield();
  if (changes.enabled || changes.aggressiveMode) applyAggressiveMode();
  if (changes.enabled || changes.scriptlets) applyScriptlets();
  if (changes.lists || changes.customFilters) refreshDynamicRules();
});

try {
  chrome.alarms.onAlarm.addListener((alarm) => {
    if (alarm && alarm.name === 'coclico-cookie-sweep') {
      getSettings().then(s => { if (s.enabled !== false) sweepTrackingCookies(); });
    }
  });
} catch (_) { }

try {
  chrome.declarativeNetRequest.onRuleMatchedDebug.addListener((info) => {
    let host = '';
    try { host = new URL((info.request || {}).url).hostname; } catch (_) { }
    bumpBlockedCounter(info.tabId, host);
    try {
      const req = info.request || {};
      try { host = new URL(req.url).hostname; } catch (_) { host = req.url || ''; }
      pushNetLog({ t: Date.now(), host, type: req.resourceType || 'other', cat: categoryOf(host), ruleId: info.ruleId, tabId: typeof info.tabId === 'number' ? info.tabId : -1 });
    } catch (_) { }
  });
} catch (_) { }

/* Injection du CSS cosmétique des listes dans chaque page chargée.
   Perf : le CSS par hôte est mis en cache (évite de re-concaténer des dizaines de milliers
   de sélecteurs à chaque navigation — c'était le frein de Gmail). */
const cssHostCache = new Map();
function cachedCssForHost(cosmetic, host) {
  if (!cosmetic) return '';
  if (cssHostCache.has(host)) return cssHostCache.get(host);
  const css = cssForHost(cosmetic, host);
  if (cssHostCache.size > 500) cssHostCache.clear();
  cssHostCache.set(host, css);
  return css;
}

try {
  chrome.webNavigation.onCommitted.addListener(async (details) => {
    if (details.frameId !== 0) return;
    let host;
    try { host = new URL(details.url).hostname; } catch (_) { return; }
    if (!host || !/^https?:/.test(details.url)) return;
    const s = await chrome.storage.local.get({ cosmetic: null, enabled: true, siteOverrides: {} });
    if (s.enabled === false || s.siteOverrides[host] === true) return;
    const css = cachedCssForHost(s.cosmetic, host);
    if (!css) return;
    try {
      await chrome.scripting.insertCSS({ target: { tabId: details.tabId, allFrames: true }, css });
    } catch (_) { }
  });
} catch (_) { }

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg && msg.type === 'refreshRules') {
    refreshDynamicRules().then(() => sendResponse({ ok: true }));
    return true;
  }
  if (msg && msg.type === 'getState') {
    (async () => {
      const settings = await getSettings();
      const meta = await chrome.storage.local.get({ ruleMeta: null });
      sendResponse({ settings, ruleMeta: meta.ruleMeta });
    })();
    return true;
  }
  if (msg && msg.type === 'getNetLog') {
    (async () => {
      if (netLog.length) sendResponse({ log: netLog.slice(0, NETLOG_MAX) });
      else {
        const s = await chrome.storage.local.get({ netLog: [] });
        sendResponse({ log: s.netLog });
      }
    })();
    return true;
  }
  if (msg && msg.type === 'toggleSite' && msg.domain) {
    (async () => {
      const s = await getSettings();
      s.siteOverrides[msg.domain] = !(s.siteOverrides[msg.domain] === true);
      await chrome.storage.local.set({ siteOverrides: s.siteOverrides });
      await applySessionRules();
      sendResponse({ disabled: s.siteOverrides[msg.domain] === true });
    })();
    return true;
  }
});
