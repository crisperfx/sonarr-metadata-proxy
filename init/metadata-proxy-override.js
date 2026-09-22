(function () {
  'use strict';

  var PROXY_PORT = 9697;
  var OVERRIDES_API_URL = '__OVERRIDES_API_URL__';
  var LS_KEY = 'sonarrMetadataOverride.apiKey';
  var PANEL_ID = 'metadata-override-ui';
  var POLL_MS = 2000;
  var EMBEDDED_KEY = '__SONARR_API_KEY__';

  if (window.__metadataOverrideInstalled) {
    return;
  }
  window.__metadataOverrideInstalled = true;

  var el;
  var series;
  var lastRoute = null;
  var seriesCache = null;
  var seriesCacheAt = 0;
  var SERIES_CACHE_TTL_MS = 10 * 60 * 1000;

  function getApiKey() {
    if (EMBEDDED_KEY && EMBEDDED_KEY.indexOf('__SONARR_') !== 0) {
      return EMBEDDED_KEY;
    }
    return localStorage.getItem(LS_KEY);
  }

  function setApiKey() {
    var key = window
      .prompt(
        'Metadata source: enter your Sonarr API key (Settings → General → API Key). ' +
          'It is only stored in your browser (localStorage).'
      )
      .trim();
    if (key) {
      localStorage.setItem(LS_KEY, key);
    }
    return key || null;
  }

  function seriesIdentifier() {
    var parts = window.location.pathname.split('/').filter(Boolean);
    if (parts.length < 2 || parts[0] !== 'series') {
      return null;
    }
    var last = decodeURIComponent(parts[1]);
    if (/^\d+$/.test(last)) {
      return { id: parseInt(last, 10) };
    }
    return { slug: last };
  }

  function overridesApiBase() {
    var origin = OVERRIDES_API_URL;
    if (!origin || origin.indexOf('http') !== 0) {
      if (window.location.protocol === 'https:') {
        return null;
      }
      origin = 'http://' + window.location.hostname + ':' + PROXY_PORT;
    }
    return origin.replace(/\/+$/, '');
  }

  function proxyUrl() {
    var base = overridesApiBase();
    return base ? base + '/api/overrides' : null;
  }

  function isMobile() {
    return (window.innerWidth || document.documentElement.clientWidth) < 768;
  }

  function positionCss() {
    return 'position:fixed;top:0;left:0;bottom:0;z-index:99999;';
  }

  function baseCss() {
    return (
      positionCss() + 'background:#222c3d;color:#fff;' +
      'border-right:1px solid #334155;padding:10px 12px;' +
      'font:13px/1.4 "Open Sans",sans-serif;box-shadow:0 0 20px rgba(0,0,0,.55);' +
      'width:300px;max-width:88vw;overflow:auto;' +
      'transform:translateX(0);transition:transform .22s ease;' +
      'display:flex;flex-direction:column;'
    );
  }

  function pillCss() {
    return (
      'position:fixed;left:0;top:38%;z-index:99999;' +
      'background:#222c3d;color:#fff;border:1px solid #334155;border-left:none;' +
      'border-radius:0 8px 8px 0;padding:10px 7px;' +
      'font:13px/1.4 "Open Sans",sans-serif;box-shadow:0 4px 12px rgba(0,0,0,.4);' +
      'cursor:pointer;writing-mode:vertical-rl;text-orientation:mixed;'
    );
  }

  function buildShell(titleText) {
    var root = document.createElement('div');
    root.id = PANEL_ID;
    root.style.cssText = baseCss();

    var pill = document.createElement('div');
    pill.style.cssText = 'display:none;font-weight:600;';
    pill.textContent = 'Metadata \u25B8';

    var header = document.createElement('div');
    header.style.cssText =
      'display:flex;align-items:center;justify-content:space-between;gap:8px;margin-bottom:6px;';
    var title = document.createElement('span');
    title.style.cssText = 'font-weight:600;';
    title.textContent = titleText;
    var toggle = document.createElement('button');
    toggle.textContent = '\u2013';
    toggle.style.cssText =
      'padding:2px 8px;background:#334155;color:#fff;border:none;border-radius:4px;cursor:pointer;font-size:12px;';
    toggle.setAttribute('aria-label', 'Collapse');
    header.appendChild(title);
    header.appendChild(toggle);

    var body = document.createElement('div');
    body.className = 'mpo-body';

    root.appendChild(pill);
    root.appendChild(header);
    root.appendChild(body);
    root._mpoBody = body;

    toggle.addEventListener('click', function (e) {
      e.stopPropagation();
      setCollapsed(true);
    });
    pill.addEventListener('click', function () {
      setCollapsed(false);
    });

    function setCollapsed(on) {
      root.dataset.mpoCollapsed = on ? '1' : '';
      root.style.cssText = on ? pillCss() : baseCss();
      body.style.display = on ? 'none' : '';
      header.style.display = on ? 'none' : '';
      pill.style.display = on ? '' : 'none';
    }

    root.mpoCollapse = setCollapsed;
    setCollapsed(true);
    return root;
  }

  function setStatus(text, color) {
    if (!el) {
      return;
    }
    var status = el.querySelector('.mpo-status');
    if (status) {
      status.textContent = text;
      status.style.color = color || '#94a3b8';
    }
  }

  function buildKeyPanel(message) {
    var shell = buildShell('Metadata source');

    var hint = document.createElement('div');
    hint.style.cssText = 'font-size:11px;color:#94a3b8;margin-bottom:8px;';
    hint.textContent = message || 'Enter your Sonarr API key first (Settings → General → API Key).';
    shell._mpoBody.appendChild(hint);

    var btn = document.createElement('button');
    btn.style.cssText =
      'padding:4px 10px;background:#3b82f6;color:#fff;border:none;border-radius:4px;cursor:pointer;';
    btn.textContent = 'Enter API key';
    btn.addEventListener('click', function () {
      if (setApiKey()) {
        shell.remove();
        tick(true);
      }
    });
    shell._mpoBody.appendChild(btn);

    document.body.appendChild(shell);
    shell.mpoCollapse(false);
    return shell;
  }

  function buildPickerPanel() {
    var shell = buildShell('Metadata: ' + (series.title || series.tvdbId));

    var select = document.createElement('select');
    select.style.cssText = 'width:100%;padding:4px;margin-bottom:6px;';
    [
      { value: '', label: 'Default' },
      { value: 'tmdb', label: 'TMDB' },
      { value: 'tvdb', label: 'TVDB' },
      { value: 'anilist', label: 'AniList' }
    ].forEach(function (opt) {
      var option = document.createElement('option');
      option.value = opt.value;
      option.textContent = opt.label;
      select.appendChild(option);
    });
    select.value = '';
    shell._mpoBody.appendChild(select);

    var status = document.createElement('div');
    status.className = 'mpo-status';
    status.style.cssText = 'font-size:11px;color:#94a3b8;';
    status.textContent = 'TVDB id: ' + series.tvdbId;
    shell._mpoBody.appendChild(status);

    var isSynthetic = series.tvdbId >= 1000000000;
    if (isSynthetic) {
      var warn = document.createElement('div');
      warn.style.cssText = 'font-size:11px;color:#fbbf24;margin-top:4px;';
      warn.textContent = 'Let op: deze serie heeft geen echte TVDB-ID. Bij "TVDB" als bron werkt passthrough niet (fallback naar standaard bron).';
      shell._mpoBody.appendChild(warn);
    }

    select.addEventListener('change', function () {
      var selectedSource = select.value;
      if (selectedSource === 'tvdb' && isSynthetic) {
        setStatus('Waarschuwing: TVDB passthrough werkt niet voor deze serie (geen echte TVDB-ID). Fallback naar standaard bron.', '#fbbf24');
      }
      saveOverride(series.tvdbId, select.value)
        .then(function (dto) {
          if (dto && dto.source === 'tmdb') {
            if (dto.tmdbId) {
              setStatus('Saved: TMDB (id ' + dto.tmdbId + '). Now run Refresh & Scan.', '#4ade80');
            } else {
              setStatus('Saved, but no TMDB id found — falling back to TVDB.', '#fbbf24');
            }
          } else {
            setStatus('Saved (' + (select.value || 'automatic') + '). Now run Refresh & Scan.', '#fbbf24');
          }
        })
        .catch(function (err) {
          setStatus('Error: ' + err.message, '#f87171');
        });
    });

    document.body.appendChild(shell);
    return select;
  }

  function saveOverride(tvdbId, source) {
    var url = proxyUrl();
    if (!url) {
      setStatus(
        'HTTPS page: set OVERRIDES_API_URL on the Sonarr container to reach the overrides API.',
        '#f87171'
      );
      return Promise.resolve();
    }
    var options = { method: source ? 'POST' : 'DELETE' };
    if (!source) {
      url += '/' + tvdbId;
    } else {
      options.headers = { 'Content-Type': 'application/json' };
      var year = series && series.year;
      if (!year && series && series.firstAired) {
        year = parseInt(String(series.firstAired).slice(0, 4), 10);
      }
      options.body = JSON.stringify({
        tvdbId: tvdbId,
        source: source,
        title: series ? series.title : undefined,
        year: year || undefined
      });
    }

    return fetch(url, options).then(function (response) {
      if (!response.ok && response.status !== 204) {
        throw new Error('HTTP ' + response.status);
      }
      return response.status === 204 ? null : response.json();
    });
  }

  function currentOverride(list, tvdbId) {
    for (var i = 0; i < list.length; i++) {
      if (list[i].tvdbId === tvdbId) {
        return list[i].source;
      }
    }
    return '';
  }

  function getSeriesList(key) {
    var now = Date.now();
    if (seriesCache && now - seriesCacheAt < SERIES_CACHE_TTL_MS) {
      return Promise.resolve(seriesCache);
    }
    return fetch('/api/v3/series?apikey=' + encodeURIComponent(key))
      .then(function (response) {
        if (!response.ok) {
          throw new Error('Sonarr API: HTTP ' + response.status + ' — klopt je API-key?');
        }
        return response.json();
      })
      .then(function (list) {
        seriesCache = list;
        seriesCacheAt = Date.now();
        return list;
      });
  }

  function findSeries(key, ident) {
    var slug = String(ident.slug || ident.id);
    var normalized = slug.replace(/[^a-z0-9]+/g, '-');

    function pick(list) {
      for (var i = 0; i < list.length; i++) {
        if (String(list[i].titleSlug || '') === slug) {
          return list[i];
        }
      }
      for (var j = 0; j < list.length; j++) {
        var titleNorm = String(list[j].title || '')
          .toLowerCase()
          .replace(/[^a-z0-9]+/g, '-')
          .replace(/^-|-$/g, '');
        if (titleNorm === normalized) {
          return list[j];
        }
      }
      if (ident.id) {
        for (var k = 0; k < list.length; k++) {
          if (list[k].id === ident.id) {
            return list[k];
          }
        }
      }
      return null;
    }

    function fail() {
      throw new Error('Series not found via "' + slug + '" (check your API key and that the series exists).');
    }

    return getSeriesList(key).then(function (list) {
      var found = pick(list);
      if (found) {
        return found;
      }
      if (seriesCache && Date.now() - seriesCacheAt < SERIES_CACHE_TTL_MS) {
        seriesCache = null;
        seriesCacheAt = 0;
        return getSeriesList(key).then(function (fresh) {
          var refound = pick(fresh);
          if (refound) {
            return refound;
          }
          fail();
        });
      }
      fail();
    });
  }

  function tick(force) {
    var ident = seriesIdentifier();
    if (!ident) {
      lastRoute = null;
      series = null;
      el = null;
      var stale = document.getElementById(PANEL_ID);
      if (stale) {
        stale.remove();
      }
      return;
    }
    var route = String(ident.id || ident.slug);

    if (force || lastRoute !== route) {
      lastRoute = route;
      series = null;
      el = null;
      var existing = document.getElementById(PANEL_ID);
      if (existing) {
        existing.remove();
      }
    }
    if (document.getElementById(PANEL_ID)) {
      return;
    }

    if (!getApiKey()) {
      buildKeyPanel();
      return;
    }

    findSeries(getApiKey(), ident)
      .then(function (data) {
        if (!data || !data.tvdbId) {
          throw new Error('No tvdbId received from Sonarr');
        }
        series = data;
        var select = buildPickerPanel();
        el = document.getElementById(PANEL_ID);

        var url = proxyUrl();
        if (!url) {
          setStatus(
            'HTTPS page: set OVERRIDES_API_URL on the Sonarr container to reach the overrides API.',
            '#fbbf24'
          );
          return;
        }

        fetch(url)
          .then(function (proxyResponse) {
            if (!proxyResponse.ok) {
              throw new Error('HTTP ' + proxyResponse.status);
            }
            return proxyResponse.json();
          })
          .then(function (list) {
            select.value = currentOverride(list, series.tvdbId);
          })
          .catch(function (err) {
            setStatus(
              'overrides API unreachable: ' + proxyUrl() + ' (' + err.message + ')',
              '#f87171'
            );
          });
      })
      .catch(function (err) {
        console.debug('[metadata-proxy-override]', err);
        el = null;
        buildKeyPanel('Error: ' + err.message);
      });
  }

  function searchInputCandidates() {
    var found = [];
    var path = window.location.pathname || '';
    if (path.indexOf('/add/new') !== 0) {
      return found;
    }
    var inputs = document.querySelectorAll('input');
    for (var i = 0; i < inputs.length; i++) {
      var input = inputs[i];
      var name = String(input.getAttribute('name') || '').toLowerCase();
      var ph = String(input.placeholder || '').toLowerCase();
      var aria = String(input.getAttribute('aria-label') || '').toLowerCase();
      if (
        name === 'serieslookup' ||
        ph.indexOf('tvdb') !== -1 ||
        ph.indexOf('search') !== -1 ||
        ph.indexOf('series') !== -1 ||
        aria.indexOf('search') !== -1
      ) {
        found.push(input);
      }
    }
    var unique = [];
    for (var j = 0; j < found.length; j++) {
      if (unique.indexOf(found[j]) === -1) {
        unique.push(found[j]);
      }
    }
    return unique;
  }

  function setNativeValue(element, value) {
    var proto =
      element.tagName === 'INPUT'
        ? window.HTMLInputElement.prototype
        : window.HTMLTextAreaElement.prototype;
    var setter = Object.getOwnPropertyDescriptor(proto, 'value');
    if (setter && setter.set) {
      setter.set.call(element, value);
    } else {
      element.value = value;
    }
  }

  function dispatchInput(input) {
    var evt;
    try {
      evt = new Event('input', { bubbles: true });
    } catch (e) {
      evt = document.createEvent('Event');
      evt.initEvent('input', true, false);
    }
    input.dispatchEvent(evt);
  }

  var SEARCH_UI_ID = 'metadata-search-ui';
  var LS_PROVIDER_KEY = 'sonarrMetadataOverride.searchProvider';
  var SEARCH_PROVIDER = '';
  var lastSearchInput = null;

  function normalizeSearchSource(value) {
    var v = String(value || '').trim().toLowerCase().replace(/:$/, '');
    if (v !== 'tmdb' && v !== 'tvdb' && v !== 'anilist') {
      return '';
    }
    return v;
  }

  function applySearchProvider(source) {
    SEARCH_PROVIDER = normalizeSearchSource(source);
    var selects = document.querySelectorAll('select[data-mpo-provider]');
    for (var i = 0; i < selects.length; i++) {
      selects[i].value = SEARCH_PROVIDER;
    }
  }

  function loadSearchProvider() {
    try {
      SEARCH_PROVIDER = normalizeSearchSource(localStorage.getItem(LS_PROVIDER_KEY));
    } catch (e) {
      SEARCH_PROVIDER = '';
    }
    var base = overridesApiBase();
    if (!base) {
      return;
    }
    fetch(base + '/api/overrides/searchsource')
      .then(function (res) {
        if (!res.ok) {
          throw new Error('bad status ' + res.status);
        }
        return res.json();
      })
      .then(function (data) {
        applySearchProvider(data && data.source);
      })
      .catch(function () {
        /* fall back to localStorage value */
      });
  }
  loadSearchProvider();

  function triggerSearchRestart() {
    var pick = lastSearchInput;
    if (!pick) {
      var candidates = searchInputCandidates();
      pick = candidates.length ? candidates[0] : null;
    }
    if (pick && String(pick.value).trim()) {
      dispatchInput(pick);
    }
  }

  function setSearchProvider(source) {
    applySearchProvider(source);
    try {
      localStorage.setItem(LS_PROVIDER_KEY, SEARCH_PROVIDER);
    } catch (e) {
      /* ignore */
    }
    triggerSearchRestart();
    var base = overridesApiBase();
    if (!base) {
      return;
    }
    fetch(base + '/api/overrides/searchsource', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ source: SEARCH_PROVIDER })
    }).catch(function () {
      /* proxy offline; localStorage value remains effective */
    });
  }

  function buildMobileSearchPicker() {
    var ui = document.getElementById(SEARCH_UI_ID);
    if (ui) {
      ui.remove();
    }

    ui = document.createElement('div');
    ui.id = SEARCH_UI_ID;
    ui.style.cssText = baseCss();

    var pill = document.createElement('div');
    pill.style.cssText = 'display:none;font-weight:600;';
    pill.textContent = 'Metasources \u25B8';

    var header = document.createElement('div');
    header.style.cssText =
      'display:flex;align-items:center;justify-content:space-between;gap:8px;margin-bottom:6px;';
    var title = document.createElement('span');
    title.style.cssText = 'font-weight:600;';
    title.textContent = 'Search metasource';
    var toggle = document.createElement('button');
    toggle.textContent = '\u2013';
    toggle.style.cssText =
      'padding:2px 8px;background:#334155;color:#fff;border:none;border-radius:4px;cursor:pointer;font-size:12px;';
    toggle.setAttribute('aria-label', 'Collapse');

    var select = document.createElement('select');
    select.style.cssText = 'width:100%;padding:4px;';
    select.setAttribute('data-mpo-provider', '1');
    select.value = SEARCH_PROVIDER;
    [
      { value: '', label: 'Default' },
      { value: 'tmdb', label: 'TMDB' },
      { value: 'tvdb', label: 'TVDB' },
      { value: 'anilist', label: 'AniList' }
    ].forEach(function (opt) {
      var option = document.createElement('option');
      option.value = opt.value;
      option.textContent = opt.label;
      select.appendChild(option);
    });

    header.appendChild(title);
    header.appendChild(toggle);
    ui.appendChild(pill);
    ui.appendChild(header);
    ui.appendChild(select);

    ui.mpoCollapse = function () {
      ui.dataset.mpoCollapsed = '1';
      ui.style.cssText = pillCss();
      header.style.display = 'none';
      select.style.display = 'none';
      pill.style.display = '';
    };
    ui.mpoExpand = function () {
      ui.dataset.mpoCollapsed = '';
      ui.style.cssText = baseCss();
      header.style.display = '';
      select.style.display = '';
      pill.style.display = 'none';
    };
    toggle.addEventListener('click', function (e) {
      e.stopPropagation();
      ui.mpoCollapse();
    });
    pill.addEventListener('click', function () {
      ui.mpoExpand();
    });

    select.addEventListener('change', function () {
      setSearchProvider(this.value);
    });

    document.body.appendChild(ui);
    ui.mpoCollapse();
    return ui;
  }

  function attachSearchPicker(input) {
    if (input.getAttribute('data-mpo-search') === '1') {
      return;
    }
    input.setAttribute('data-mpo-search', '1');
    input.addEventListener('focus', function () {
      lastSearchInput = input;
    });
    input.addEventListener('input', function () {
      lastSearchInput = input;
    });

    if (isMobile()) {
      buildMobileSearchPicker();
      return;
    }

    var row = document.createElement('div');
    row.style.cssText = 'display:flex;align-items:center;gap:8px;margin-bottom:6px;';

    var label = document.createElement('span');
    label.style.cssText = 'font-size:11px;color:#94a3b8;';
    label.textContent = 'Search via';

    var select = document.createElement('select');
    select.style.cssText =
      'padding:3px 6px;font-size:12px;background:#263241;color:#fff;border:1px solid #334155;border-radius:4px;';
    select.setAttribute('data-mpo-provider', '1');
    select.value = SEARCH_PROVIDER;
    [
      { value: '', label: 'Automatic' },
      { value: 'tmdb', label: 'TMDB' },
      { value: 'tvdb', label: 'TVDB' },
      { value: 'anilist', label: 'AniList' }
    ].forEach(function (opt) {
      var option = document.createElement('option');
      option.value = opt.value;
      option.textContent = opt.label;
      select.appendChild(option);
    });

    row.appendChild(label);
    row.appendChild(select);
    input.parentNode.insertBefore(row, input);
    row._mpoInput = input;
    input._mpoRow = row;

    select.addEventListener('change', function () {
      setSearchProvider(this.value);
    });
  }

  function removeSearchRow(input) {
    var row = input._mpoRow;
    if (row && row.parentNode) {
      row.parentNode.removeChild(row);
    }
    input._mpoRow = null;
    input.removeAttribute('data-mpo-search');
  }

  function removeSearchUi() {
    var ui = document.getElementById(SEARCH_UI_ID);
    if (ui && ui.parentNode) {
      ui.parentNode.removeChild(ui);
    }
  }

  function refreshSearchPickers() {
    var path = window.location.pathname || '';
    if (path.indexOf('/add/new') !== 0) {
      removeAllSearchRows();
      removeSearchUi();
      return;
    }
    var candidates = searchInputCandidates();
    if (!candidates.length) {
      return;
    }
    var primary = candidates[0];
    var existing = document.querySelectorAll('input[data-mpo-search]');
    for (var j = 0; j < existing.length; j++) {
      if (existing[j] !== primary) {
        removeSearchRow(existing[j]);
      }
    }
    attachSearchPicker(primary);
  }

  function removeAllSearchRows() {
    var inputs = document.querySelectorAll('input[data-mpo-search]');
    for (var i = 0; i < inputs.length; i++) {
      removeSearchRow(inputs[i]);
    }
  }

  function ensurePortalRoot() {
    if (document.getElementById('portal-root')) {
      return;
    }
    var node = document.createElement('div');
    node.id = 'portal-root';
    document.body.appendChild(node);
  }

  var searchObserver = null;
  var refreshSearchTimer = null;
  function initSearchPickers() {
    ensurePortalRoot();
    refreshSearchPickers();
    if (!document.body) {
      return;
    }
    if (window.MutationObserver) {
      try {
        searchObserver = new MutationObserver(function () {
          if (refreshSearchTimer) {
            clearTimeout(refreshSearchTimer);
          }
          refreshSearchTimer = setTimeout(refreshSearchPickers, 300);
        });
        searchObserver.observe(document.body, { childList: true, subtree: true });
      } catch (e) {
        /* ignore */
      }
    }
  }

  window.addEventListener('resize', function () {
    var panel = document.getElementById(PANEL_ID);
    if (panel) {
      panel.style.cssText =
        panel.dataset.mpoCollapsed === '1' ? pillCss() : baseCss();
    }
    var ui = document.getElementById(SEARCH_UI_ID);
    if (ui) {
      ui.style.cssText =
        ui.dataset.mpoCollapsed === '1' ? pillCss() : baseCss();
    }
  });

  document.addEventListener('mousedown', function (e) {
    var target = e.target;
    var panel = document.getElementById(PANEL_ID);
    if (panel && panel.dataset.mpoCollapsed !== '1' && !panel.contains(target)) {
      panel.mpoCollapse(true);
    }
    var ui = document.getElementById(SEARCH_UI_ID);
    if (ui && ui.dataset.mpoCollapsed !== '1' && !ui.contains(target)) {
      ui.mpoCollapse();
    }
  });

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initSearchPickers);
  } else {
    initSearchPickers();
  }

  setInterval(tick, POLL_MS);
  setInterval(refreshSearchPickers, POLL_MS);
  tick();
  refreshSearchPickers();
})();