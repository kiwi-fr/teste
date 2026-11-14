/* Coclico AdBlock - filtrage cosmétique personnalisé + sélecteur d'éléments (zap). */
(function () {
  'use strict';

  const styleId = 'coclico-adblock-custom';

  function injectCustomCss(css) {
    let el = document.getElementById(styleId);
    if (!el) {
      el = document.createElement('style');
      el.id = styleId;
      (document.head || document.documentElement).appendChild(el);
    }
    el.textContent = css;
  }

  function selectorForElement(el) {
    if (el.id) return '#' + CSS.escape(el.id);
    const parts = [];
    let node = el;
    while (node && node.nodeType === 1 && parts.length < 4) {
      let part = node.tagName.toLowerCase();
      if (node.classList.length) {
        part += '.' + [...node.classList].slice(0, 3).map(c => CSS.escape(c)).join('.');
      }
      parts.unshift(part);
      node = node.parentElement;
    }
    return parts.join(' > ');
  }

  function cssFromCustom(customCosmetic) {
    const host = location.hostname;
    const globalRules = customCosmetic['*'] || [];
    const siteRules = customCosmetic[host] || [];
    return [...globalRules, ...siteRules].map(sel => sel + ' { display: none !important; }').join('\n');
  }

  let pickerMode = false;

  chrome.storage.onChanged.addListener((changes, area) => {
    if (area !== 'local') return;
    if (changes.pickerMode) pickerMode = changes.pickerMode.newValue === true;
    if (changes.customCosmetic) injectCustomCss(cssFromCustom(changes.customCosmetic.newValue || {}));
  });

  chrome.storage.local.get({ pickerMode: false, customCosmetic: {} }).then(s => {
    pickerMode = s.pickerMode === true;
    const css = cssFromCustom(s.customCosmetic);
    if (css) injectCustomCss(css);
  });

  document.addEventListener('click', async (e) => {
    if (!pickerMode) return;
    e.preventDefault();
    e.stopPropagation();
    const sel = selectorForElement(e.target);
    try {
      const s = await chrome.storage.local.get({ customCosmetic: {}, pickerMode: true });
      const host = location.hostname;
      if (!s.customCosmetic[host]) s.customCosmetic[host] = [];
      if (!s.customCosmetic[host].includes(sel)) s.customCosmetic[host].push(sel);
      await chrome.storage.local.set({ customCosmetic: s.customCosmetic, pickerMode: false });
      injectCustomCss(cssFromCustom(s.customCosmetic));
    } catch (_) { }
  }, true);
})();
