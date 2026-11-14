/* Coclico AdBlock - conversion des listes communautaires (DNR + cosmétiques).
   Partagé entre le service worker (importScripts) et la page d'options. */

const COCLICO_LISTS = [
  {
    id: 'peterlowe',
    name: 'Peter Lowe’s Ad & tracker list',
    url: 'https://pgl.yoyo.org/adservers/serverlist.php?hostformat=one-line&showintro=0&mimetype=plaintext',
    format: 'hosts'
  },
  {
    id: 'easyprivacy',
    name: 'EasyPrivacy',
    url: 'https://easylist.to/easylist/easyprivacy.txt',
    format: 'abp'
  },
  {
    id: 'easylist',
    name: 'EasyList',
    url: 'https://easylist.to/easylist/easylist.txt',
    format: 'abp'
  },
  {
    id: 'easylistfr',
    name: 'EasyList FR',
    url: 'https://easylist.fr/easylist/easylistfr.txt',
    format: 'abp'
  },
  {
    id: 'ubo-annoyances',
    name: 'uBlock Annoyances (popups, overlays)',
    url: 'https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/annoyances.txt',
    format: 'abp'
  },
  {
    id: 'ubo-cookies',
    name: 'uBlock Cookie consent banners',
    url: 'https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/cookies.txt',
    format: 'abp'
  },
  {
    id: 'hagezi-pro',
    name: 'HaGeZi Multi PRO+ (DNS blocklist, mise à jour régulière)',
    url: 'https://raw.githubusercontent.com/hagezi/dns-blocklists/main/adblock/pro.txt',
    format: 'abp'
  },
  {
    id: 'brave-specific',
    name: 'Brave Specific (listes officielles Brave, adblock-lists)',
    url: 'https://raw.githubusercontent.com/brave/adblock-lists/master/brave-lists/brave-specific.txt',
    format: 'abp'
  },
  {
    id: 'brave-unbreak',
    name: 'Brave Unbreak (anti-casse des sites)',
    url: 'https://raw.githubusercontent.com/brave/adblock-lists/master/brave-unbreak.txt',
    format: 'abp'
  },
  {
    id: 'ubo-filters',
    name: 'uBlock Origin filters (complet)',
    url: 'https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/filters.txt',
    format: 'abp'
  }
];

/* DNR (Chrome ET Firefox) utilise "sub_frame" pour les iframes ; "subdocument" n'existe pas en DNR.
   Firefox n'accepte pas "csp_report" (on le mappe sur "ping"). */
const IS_FIREFOX = typeof navigator !== 'undefined' && /firefox/i.test(navigator.userAgent);

function dnrType(t) {
  if (t === 'subdocument' || t === 'sub_frame') return 'sub_frame';
  if (t === 'csp_report') return IS_FIREFOX ? 'ping' : 'csp_report';
  return t;
}

const DNR_ALL_TYPES = [
  'xmlhttprequest', 'script', 'image', 'stylesheet', 'sub_frame',
  'media', 'font', 'websocket', 'ping', 'object', 'other', 'main_frame'
].map(dnrType);

const ABP_TYPE_MAP = {
  script: ['script'],
  image: ['image'],
  stylesheet: ['stylesheet'],
  subdocument: [dnrType('subdocument')],
  xmlhttprequest: ['xmlhttprequest'],
  document: ['main_frame'],
  websocket: ['websocket'],
  media: ['media'],
  font: ['font'],
  ping: ['ping'],
  object: ['object'],
  other: ['other']
};

/* Domaines traqueurs : sur Firefox on bloque aussi les iframes (sub_frame) via règles de session. */
const COCLICO_IFRAME_BLOCK_DOMAINS = [
  'google-analytics.com', 'analytics.google.com', 'googletagmanager.com', 'doubleclick.net',
  'connect.facebook.net', 'pixel.facebook.com', 'graph.facebook.com', 'hotjar.com', 'mixpanel.com',
  'segment.io', 'cdn.segment.com', 'amplitude.com', 'clarity.ms', 'fullstory.com', 'chartbeat.com',
  'scorecardresearch.com', 'quantserve.com', 'criteo.com', 'criteo.net', 'taboola.com', 'outbrain.com',
  'adnxs.com', 'advertising.com', 'adform.net', 'newrelic.com', 'sentry.io', 'matomo.cloud'
];

function abpOptionsToResourceTypes(options) {
  const types = new Set();
  for (const opt of options.split(',')) {
    const mapped = ABP_TYPE_MAP[opt.trim().replace(/^-/, '')];
    if (mapped) mapped.forEach(t => types.add(t));
  }
  return types.size ? [...types] : DNR_ALL_TYPES;
}

function makeBlockRule(id, urlFilter, resourceTypes) {
  return { id, priority: 1, action: { type: 'block' }, condition: { urlFilter, resourceTypes } };
}

function makeAllowRule(id, urlFilter, resourceTypes) {
  return { id, priority: 100, action: { type: 'allow' }, condition: { urlFilter, resourceTypes } };
}

/* Convertit une liste texte en règles DNR. */
function convertListToDnr(text, format, idBase) {
  const rules = [];
  let seq = 0;
  const push = (rule) => {
    if (seq >= 29000) return;
    rule.id = idBase + seq++;
    rules.push(rule);
  };

  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith('!') || line.startsWith('[')) continue;
    if (format === 'hosts') {
      if (/^[a-z0-9*.-]+\.[a-z]{2,}$/i.test(line)) {
        push(makeBlockRule(0, '||' + line.replace(/^\*\./, '') + '^', DNR_ALL_TYPES));
      }
      continue;
    }
    if (line.includes('##') || line.includes('#@#') || line.includes('#?#') || line.includes('#$#')) continue;
    let filter = line;
    let isException = false;
    if (filter.startsWith('@@')) { isException = true; filter = filter.slice(2); }
    const dollar = filter.indexOf('$');
    let options = '';
    if (dollar >= 0) { options = filter.slice(dollar + 1); filter = filter.slice(0, dollar); }
    if (filter.startsWith('||')) {
      if (/^\|\|[a-z0-9*.-]+\^/i.test(filter) || /^\|\|[a-z0-9*.-]+$/i.test(filter)) {
        const urlFilter = filter.endsWith('^') ? filter : filter + '^';
        const types = options ? abpOptionsToResourceTypes(options) : DNR_ALL_TYPES;
        push(isException ? makeAllowRule(0, urlFilter, types) : makeBlockRule(0, urlFilter, types));
      }
      continue;
    }
    if (filter.startsWith('/') || filter.startsWith('|')) {
      const types = options ? abpOptionsToResourceTypes(options) : DNR_ALL_TYPES;
      push(isException ? makeAllowRule(0, filter, types) : makeBlockRule(0, filter, types));
    }
  }
  return { rules };
}

/* Convertit les filtres cosmétiques ABP/uBO d'une liste en CSS injectable. */
function convertCosmeticToCss(text) {
  const global = [];
  const byDomain = {};
  let skipped = 0;
  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith('!') || line.startsWith('[')) continue;
    if (line.includes('#$#') || line.includes('#$?#')) { skipped++; continue; }
    const iExt = line.indexOf('#?#');
    const iStd = line.indexOf('##');
    const iExc = line.indexOf('#@#');
    if (iExc >= 0) { skipped++; continue; }
    let sep, markerLen;
    if (iExt >= 0) { sep = iExt; markerLen = 3; }
    else if (iStd >= 0) { sep = iStd; markerLen = 2; }
    else continue;
    const domainsPart = line.slice(0, sep).trim();
    const selector = line.slice(sep + markerLen).trim();
    if (!selector) continue;
    if (!domainsPart) global.push(selector);
    else for (const d of domainsPart.split(',')) {
      const dom = d.trim().toLowerCase();
      if (dom) { (byDomain[dom] ??= []).push(selector); }
    }
  }
  return { global, byDomain, skipped };
}

/* CSS à injecter pour un hôte : globaux + domaine exact + domaines parents. */
function cssForHost(cosmetic, host) {
  if (!cosmetic) return '';
  const parts = [];
  if (cosmetic.global && cosmetic.global.length) {
    parts.push(cosmetic.global.join(',\n') + ' { display: none !important; }');
  }
  let h = (host || '').toLowerCase();
  while (h) {
    const sels = cosmetic.byDomain && cosmetic.byDomain[h];
    if (sels && sels.length) parts.push(sels.join(',\n') + ' { display: none !important; }');
    const idx = h.indexOf('.');
    if (idx < 0) break;
    h = h.slice(idx + 1);
  }
  return parts.join('\n');
}

/* Domaines traqueurs pour le nettoyage des cookies. */
const COCLICO_TRACKER_DOMAINS = [
  'doubleclick.net', 'google-analytics.com', 'googlesyndication.com', 'googletagmanager.com',
  'connect.facebook.net', 'graph.facebook.com', 'pixel.facebook.com', 'adnxs.com', 'advertising.com',
  'adform.net', 'adroll.com', 'criteo.com', 'criteo.net', 'scorecardresearch.com', 'quantserve.com',
  'hotjar.com', 'mixpanel.com', 'chartbeat.com', 'fullstory.com', 'clarity.ms', 'segment.io',
  'amplitude.com', 'snowplowanalytics.com', 'newrelic.com', 'sentry.io', 'taboola.com', 'outbrain.com',
  'zedo.com', 'amazon-adsystem.com', 'smartadserver.com', 'pubmatic.com', 'rubiconproject.com',
  'openx.net', 'casalemedia.com', 'appsflyer.com', 'adjust.com', 'kochava.com', 'branch.io',
  'urbanairship.com', 'onesignal.com', 'braze.com', 'clevertap.com', 'popads.net', 'popcash.net',
  'adsterra.com', 'propellerads.com', 'coinhive.com', 'fingerprintjs.com', 'maxmind.com'
];

/* Catégories pour l'affichage détaillé des blocages (popup). */
const COCLICO_CATEGORIES = [
  ['Minage', ['coinhive.com', 'minero.cc', 'crypto-loot', 'webminepool.com', 'cryptonight']],
  ['Consentement', ['cookiebot', 'trustarc.com', 'usercentrics.eu', 'privacy-center.org', 'onetrust.com']],
  ['Affiliation', ['linkynergy.com', 'skimresources.com', 'viglink.com', 'impactradius', 'awin1.com', 'zenaps.com', 'androezrs.net', 'dpbolvw.net', 'tkqlhce.com']],
  ['Vidéo', ['jwpsrv.com', 'connatix.com', 'fwmrm.net', 'innovid.com', 'tremorhub.com', 'imasdk.googleapis.com', 'dai.google.com', 'r.youtube.com']],
  ['Télémétrie appareil', ['apple.com', 'icloud.com', 'mzstatic.com', 'xiaomi.com', 'miui.com', 'oppomobile.com', 'realme.com', 'realmemobile.com', 'oneplus', 'hicloud.com', 'samsung', 'lgappstv', 'amazon.com', 'roku.com', 'data.microsoft.com', 'bat.bing.com', 'c.bing.com']],
  ['Réseau social', ['facebook', 'twitter.com', 'x.com', 'linkedin.com', 'pinterest.com', 'reddit', 'tiktok.com', 'byteoversea.com', 'snapchat.com', 'quora.com', 'tumblr.com', 'mail.ru']],
  ['Analytique', ['google-analytics', 'analytics.google', 'googletagmanager', 'mixpanel', 'amplitude', 'segment', 'clarity.ms', 'hotjar', 'mouseflow', 'luckyorange', 'crazyegg', 'heapanalytics', 'plausible', 'matomo', 'fullstory', 'chartbeat', 'quantserve', 'scorecardresearch', 'newrelic', 'nr-data.net', 'bugsnag', 'sentry', 'crashlytics', 'app-measurement', 'ipify', 'posthog', 'freshmarketer', 'optimizely', 'dynamicyield', 'hubspot', 'marketo', 'drift.com', 'intercom']],
  ['Publicité', ['doubleclick', 'googlesyndication', 'googleadservices', 'adservice.google', 'amazon-adsystem', 'adnxs.com', 'advertising.com', 'adform', 'criteo', 'taboola', 'outbrain', 'mgid', 'applovin', 'pubmatic', 'rubiconproject', 'openx', 'casalemedia', 'bidswitch', 'sharethrough', 'sovrn', 'lijit', 'indexww', 'gumgum', 'juicyads', 'adcolony', 'vungle', 'supersonicads', 'fyber', 'ironsrc', 'unityads', 'adsrvr', 'adroll', 'contextweb', 'moatads', 'smartadserver', 'zedo', '3lift', 'adkernel', 'adsafeprotected', 'doubleverify', 'gum.criteo', 'yahoo', 'bing.com', 'ads.microsoft', 'media.net']]
];

function categoryOf(host) {
  const h = (host || '').toLowerCase();
  if (!h) return 'Autre';
  for (const [label, doms] of COCLICO_CATEGORIES) {
    for (const d of doms) {
      if (h === d || h.endsWith('.' + d) || h.includes(d)) return label;
    }
  }
  return 'Autre';
}

const COCLICO_TRACKING_COOKIE_PREFIXES = [
  '_ga', '_gid', '_gat', '_dc_gtm', 'ide', 'test_cookie', '_fbp', 'fr', 'datr', 'sb',
  'c_user', 'xs', '_gcl', '_ttp', 'li_sugr', 'bcookie', 'lidc', '__hstc', 'hubspotutk',
  '_hj', 'intercom-id', 'personalization_id', '__gads', '__gpi', 'amplitude_id',
  'ajs_anonymous_id', 'ajs_user_id', '_pk_id', 'sync-his'
];
