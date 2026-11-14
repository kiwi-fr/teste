(function () {
  'use strict';

  var HOST = location.hostname;

  var scriptlets = {};

  scriptlets['json-prune'] = function (keysStr) {
    var keys = keysStr.split(/\s+/).filter(Boolean);
    var origParse = JSON.parse;
    JSON.parse = function (text, reviver) {
      var out = origParse.call(JSON, text, reviver);
      (function prune(o, depth) {
        if (!o || typeof o !== 'object' || depth > 12) return;
        if (Array.isArray(o)) { o.forEach(function (x) { prune(x, depth + 1); }); return; }
        for (var i = 0; i < keys.length; i++) delete o[keys[i]];
        for (var k in o) prune(o[k], depth + 1);
      })(out, 0);
      return out;
    };
  };

  scriptlets['set-constant'] = function (name, value) {
    try {
      var parts = name.split('.');
      var obj = window;
      for (var i = 0; i < parts.length - 1; i++) { obj = obj[parts[i]]; if (!obj) return; }
      var last = parts[parts.length - 1];
      try { Object.defineProperty(obj, last, { value: value === 'false' ? false : value === 'true' ? true : value, writable: false }); }
      catch (e) { obj[last] = value; }
    } catch (e) { }
  };
  scriptlets['no-fetch-if'] = function (pattern) {
    var re;
    try { re = new RegExp(pattern); } catch (e) { return; }
    var blocked = function () {
      return Promise.reject(new DOMException('Blocked by Coclico AdBlock', 'AbortError'));
    };
    if (window.fetch) {
      var of = window.fetch.bind(window);
      window.fetch = function (input, init) {
        var url = typeof input === 'string' ? input : (input && input.url) || '';
        if (re.test(url)) return blocked();
        return of(input, init);
      };
    }
    if (window.XMLHttpRequest) {
      var oOpen = XMLHttpRequest.prototype.open;
      var oSend = XMLHttpRequest.prototype.send;
      var oAbort = XMLHttpRequest.prototype.abort;
      XMLHttpRequest.prototype.open = function (m, url) {
        this.__coclicoUrl = String(url);
        return oOpen.apply(this, arguments);
      };
      XMLHttpRequest.prototype.send = function () {
        if (this.__coclicoUrl && re.test(this.__coclicoUrl)) {
          return oAbort.apply(this);  
        }
        return oSend.apply(this, arguments);
      };
    }
  };

  scriptlets['no-xhr-if'] = scriptlets['no-fetch-if'];
  scriptlets['fake-empty-if'] = function (pattern) {
    var re;
    try { re = new RegExp(pattern); } catch (e) { return; }
    function bodyFor(url) {
      if (/vast/i.test(url)) return { body: '<?xml version="1.0" encoding="UTF-8"?><VAST/>', type: 'application/xml' };
      return { body: '{}', type: 'application/json' };
    }
    if (window.fetch) {
      var of = window.fetch.bind(window);
      window.fetch = function (input, init) {
        var url = typeof input === 'string' ? input : (input && input.url) || '';
        if (re.test(url)) {
          var b = bodyFor(url);
          return Promise.resolve(new Response(b.body, { status: 200, headers: { 'content-type': b.type } }));
        }
        return of(input, init);
      };
    }
    if (window.XMLHttpRequest) {
      var oOpen = XMLHttpRequest.prototype.open;
      var oSend = XMLHttpRequest.prototype.send;
      XMLHttpRequest.prototype.open = function (m, u) {
        this.__coclicoUrl = String(u);
        return oOpen.apply(this, arguments);
      };
      XMLHttpRequest.prototype.send = function () {
        var url = this.__coclicoUrl || '';
        if (re.test(url)) {
          var b = bodyFor(url);
          try {
            Object.defineProperty(this, 'readyState', { value: 4, configurable: true });
            Object.defineProperty(this, 'status', { value: 200, configurable: true });
            Object.defineProperty(this, 'statusText', { value: 'OK', configurable: true });
            Object.defineProperty(this, 'responseText', { value: b.body, configurable: true });
            Object.defineProperty(this, 'response', { value: b.body, configurable: true });
            Object.defineProperty(this, 'responseURL', { value: url, configurable: true });
          } catch (e) { }
          var self = this;
          setTimeout(function () {
            try { if (self.onreadystatechange) self.onreadystatechange(); } catch (e) { }
            try { if (self.onload) self.onload(); } catch (e) { }
            try {
              self.dispatchEvent(new Event('readystatechange'));
              self.dispatchEvent(new Event('load'));
              self.dispatchEvent(new Event('loadend'));
            } catch (e) { }
          }, 0);
          return;
        }
        return oSend.apply(this, arguments);
      };
    }
  };
  scriptlets['vaft-ublock-origin'] = function () {
    function skip() {
      var v = document.querySelector('video');
      var btn = document.querySelector('[data-a-target="player-skip-ad-button"], .video-player__overlay button');
      if (btn) { try { btn.click(); } catch (e) { } }
      if (v) { try { v.muted = true; v.currentTime = Math.max(v.currentTime, v.duration - 0.5); } catch (e) { } }
    }
    setInterval(skip, 700);
  };

  scriptlets['youtube-skip'] = function () {
    function skip() {
      var btn = document.querySelector('.ytp-ad-skip-button, .ytp-ad-skip-button-modern');
      if (btn) { try { btn.click(); } catch (e) { } }
      var v = document.querySelector('video');
      var adShowing = document.querySelector('.ytp-ad-showing, .ytp-ad-player-overlay, .ad-showing, #player-ads .ad-container');
      if (v && adShowing && isFinite(v.duration) && v.duration > 0) {
        try { v.currentTime = v.duration; } catch (e) { }
      }
      var ov = document.querySelector('.ytp-ad-overlay-container, .ytp-ad-player-overlay, #player-ads, .ad-container, .ytp-ad-module');
      if (ov) ov.style.setProperty('display', 'none', 'important');
    }
    setInterval(skip, 300);
    skip();
  };
  var RULES = {
    'youtube.com': [
      ['json-prune', 'playerAds adSlots adPlacements adBreakHeartbeatParams'],
      ['fake-empty-if', '/api/stats/ads|pagead|ad_view|adBreak|\\/ads\\/vast|adserver\\/'],
      ['set-constant', 'ytcfg.adBlockDetected false'],
      ['youtube-skip', '']
    ],
    'twitch.tv': [
      ['fake-empty-if', 'vast|amazon-adsystem|adservice|spade\\.twitch|\\/ads\\/'],
      ['vaft-ublock-origin', '']
    ]
  };

  function hostMatches(ruleHost) {
    return HOST === ruleHost || HOST.endsWith('.' + ruleHost);
  }

  for (var rh in RULES) {
    if (hostMatches(rh)) {
      RULES[rh].forEach(function (r) {
        var fn = scriptlets[r[0]];
        if (fn) { try { fn(r[1]); } catch (e) { } }
      });
    }
  }

  var style = document.createElement('style');
  style.textContent = [
    '[class*="adblock" i], [id*="adblock" i], [class*="ad-block" i], [id*="ad-block" i]',
    '[class*="adblock-banner" i], [id*="adblock-banner" i], [class*="disable-adblock" i]',
    '.ytp-ad-overlay-container, .ytp-ad-player-overlay, #player-ads, .ad-container, .ytp-ad-module',
    '[data-a-target="ad-banner"], .video-ads__overlay'
  ].join(',') + ' { display: none !important; }';
  (document.head || document.documentElement).appendChild(style);

  try { scriptlets['fake-empty-if']('/api/stats/ads|/pagead/|/adclick|adserver/|/ads/vast'); } catch (e) { }
})();
