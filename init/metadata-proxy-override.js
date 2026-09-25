(function () {
  'use strict';

  var PROXY_PORT = 9697;
  var OVERRIDES_API_URL = '__OVERRIDES_API_URL__';
  var PANEL_ID = 'metadata-override-ui';
  var POLL_MS = 2000;

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

  function positionCss() {
    return 'position:fixed;top:0;left:0;bottom:0;z-index:99999;';
  }

  function baseCss() {
    return (
      positionCss() + 'background:#2a2a2a;color:#e1e2e3;' +
      'border-right:1px solid #393f45;padding:16px 14px;' +
      'font:13px/1.5 "Open Sans","Segoe UI",sans-serif;box-shadow:4px 0 28px rgba(0,0,0,.55);' +
      'width:280px;max-width:88vw;overflow:auto;' +
      'transform:translateX(0);transition:transform .2s ease;' +
      'display:flex;flex-direction:column;'
    );
  }

  function pillCss() {
    return (
      'position:fixed;left:0;top:38%;z-index:99999;' +
      'background:#2a2a2a;color:#e1e2e3;border:1px solid #393f45;border-left:none;' +
      'border-radius:0 10px 10px 0;padding:14px 8px;' +
      'font:13px/1.4 "Open Sans",sans-serif;box-shadow:4px 0 18px rgba(0,0,0,.5);' +
      'cursor:pointer;writing-mode:vertical-rl;text-orientation:mixed;' +
      'letter-spacing:.14em;text-transform:uppercase;'
    );
  }

  function ensureStylesheet() {
    if (document.getElementById('mpo-styles')) {
      return;
    }
    var style = document.createElement('style');
    style.id = 'mpo-styles';
    style.textContent = [
      '.mpo-pill:hover{background:#333;color:#fff;}',
      '.mpo-toggle:hover{background:rgba(255,255,255,.12);color:#fff;border-color:#5d9cec;}',
      '.mpo-toggle:active{transform:scale(.95);}',
      '.mpo-select{font:13px/1.5 "Open Sans",sans-serif;color:#ccc;background:#333;border:1px solid #393f45;border-radius:4px;padding:6px 8px;}',
      '.mpo-select:hover{border-color:#5a6265;}',
      '.mpo-select:focus{outline:none;border-color:#5d9cec;box-shadow:0 0 0 2px rgba(93,156,236,.25);}',
      '.mpo-status{font-size:11px;line-height:1.5;color:#909293;}',
      '.mpo-warn{font-size:11px;line-height:1.5;color:#ffa500;}',
      '.mpo-btn-primary{font:600 12px/1.5 "Open Sans",sans-serif;color:#fff;background:#5d9cec;border:1px solid #5899eb;border-radius:4px;padding:6px 12px;cursor:pointer;}',
      '.mpo-btn-primary:hover{background:#4b91ea;}',
      '.mpo-title{font:600 15px/1.3 "Open Sans","Segoe UI",sans-serif;color:#fff;}',
      '.mpo-subtitle{font-size:11px;line-height:1.5;color:#909293;}',
      '.mpo-poster{width:100%;border-radius:8px;border:1px solid #393f45;margin:8px 0 4px;display:block;}',
      '.mpo-badge{margin-top: 5px;margin-bottom: 5px;display:inline-block;font:600 11px/1 "Open Sans",sans-serif;padding:4px 8px;border-radius:10px;background:#333;border:1px solid #393f45;color:#e1e2e3;letter-spacing:.04em;white-space:nowrap;}',
      '.mpo-badge-active{background:#173a24;border-color:#2f7a44;color:#7ddf9b;}',
      '.mpo-dot{display:inline-block;width:8px;height:8px;border-radius:50%;margin:0 6px 0 2px;vertical-align:middle;}',
      '.mpo-dot-on{background:#4ade80;}',
      '.mpo-dot-off{background:#f87171;}',
      '.mpo-bullets{margin:0;padding:0 0 0 16px;font-size:11px;line-height:1.7;color:#909293;}',
      '.mpo-divider{border:none;border-top:1px solid #393f45;margin:10px 0 4px;}'
    ].join('\n');
    (document.head || document.documentElement).appendChild(style);
  }

  function buildShell(titleText, pillText, id) {
    var root = document.createElement('div');
    root.id = id || PANEL_ID;
    root.style.cssText = baseCss();

    var pill = document.createElement('div');
    pill.className = 'mpo-pill';
    pill.style.cssText = 'display:none;font:600 12px/1.4 "Open Sans",sans-serif;';
    pill.textContent = pillText || 'Metadata \u25B8';

    var header = document.createElement('div');
    header.style.cssText =
      'display:flex;align-items:center;justify-content:space-between;gap:8px;margin-bottom:14px;';
    var title = document.createElement('span');
    title.className = 'mpo-panel-title';
    title.style.cssText =
      'font:600 12px/1.4 "Open Sans",sans-serif;text-transform:uppercase;letter-spacing:.12em;color:#e1e2e3;';
    title.textContent = titleText;
    var toggle = document.createElement('button');
    toggle.className = 'mpo-toggle';
    toggle.textContent = '\u2013';
    toggle.style.cssText =
      'width:22px;height:22px;padding:0;background:transparent;color:#909293;' +
      'border:1px solid #393f45;border-radius:50%;cursor:pointer;font-size:13px;line-height:1;';
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
      status.style.color = color || '#909293';
    }
  }

  function buildNoticePanel(message) {
    var shell = buildShell('Metadata source');

    var hint = document.createElement('div');
    hint.className = 'mpo-status';
    hint.textContent = message || 'Could not load the series. Make sure you are signed in to Sonarr in this browser.';
    shell._mpoBody.appendChild(hint);

    var btn = document.createElement('button');
    btn.className = 'mpo-btn-primary';
    btn.textContent = 'Retry';
    btn.addEventListener('click', function () {
      shell.remove();
      tick(true);
    });
    shell._mpoBody.appendChild(btn);

    document.body.appendChild(shell);
    shell.mpoCollapse(false);
    return shell;
  }

  function sourceLabel(v) {
    v = normalizeSearchSource(v);
    return v === 'tmdb' ? 'TMDB' : v === 'tvdb' ? 'TVDB' : v === 'anilist' ? 'AniList' : v === 'mal' ? 'MAL' : v === 'tvmaze' ? 'TVMaze' : '';
  }

  function seriesPoster() {
    if (!series || !series.images) {
      return null;
    }
    for (var i = 0; i < series.images.length; i++) {
      var im = series.images[i];
      if (im && im.coverType === 'poster') {
        return im.remoteUrl || im.url || null;
      }
    }
    return null;
  }

  function buildPickerPanel() {
    var shell = buildShell('Metadata source settings');

    var title = document.createElement('div');
    title.className = 'mpo-title';
    title.textContent = (series.title || '') + (series.year ? ' (' + series.year + ')' : '');
    shell._mpoBody.appendChild(title);

    var poster = seriesPoster();
    if (poster) {
      var img = document.createElement('img');
      img.className = 'mpo-poster';
      img.src = poster;
      img.alt = series.title || '';
      shell._mpoBody.appendChild(img);
    }

    var select = document.createElement('select');
    select.className = 'mpo-select';
    select.style.cssText = 'width:100%;';
    [
      { value: '', label: 'Default' },
      { value: 'tmdb', label: 'TMDB' },
      { value: 'tvdb', label: 'TVDB' },
      { value: 'anilist', label: 'AniList' },
      { value: 'mal', label: 'MAL' },
      { value: 'tvmaze', label: 'TVMaze' }
    ].forEach(function (opt) {
      var option = document.createElement('option');
      option.value = opt.value;
      option.textContent = opt.label;
      select.appendChild(option);
    });
    select.value = '';
    shell._mpoBody.appendChild(select);

    var badge = document.createElement('span');
    badge.className = 'mpo-badge';
    badge.id = 'mpo-series-source-badge';
    badge.textContent = 'Bron: Default';
    shell._mpoBody.appendChild(badge);

    var status = document.createElement('div');
    status.id = 'mpo-series-id-badges';
    shell._mpoBody.appendChild(status);
    updateSeriesIdBadges(null);

    var isSynthetic = series.tvdbId >= 1000000000;
    var bullets = [];
    if (isSynthetic) {
      bullets.push('Geen echte TVDB-ID — "TVDB" als bron werkt niet (fallback naar standaard bron).');
    }
    bullets.push('After changing the source: Refresh & Scan on the serie.');

    if (bullets.length) {
      var list = document.createElement('ul');
      list.className = 'mpo-bullets';
      bullets.forEach(function (text) {
        var li = document.createElement('li');
        li.textContent = text;
        list.appendChild(li);
      });
      shell._mpoBody.appendChild(list);
    }

    select.addEventListener('change', function () {
      var selectedSource = select.value;
      if (selectedSource === 'tvdb' && isSynthetic) {
        setStatus('Waarschuwing: TVDB passthrough werkt niet voor deze serie (geen echte TVDB-ID). Fallback naar standaard bron.', '#fbbf24');
      }
      updateSeriesBadge(selectedSource);
      saveOverride(series.tvdbId, select.value)
        .then(function (dto) {
          if (dto) {
            updateSeriesIdBadges(dto);
          }
          if (dto && dto.source === 'tmdb') {
            if (dto.tmdbId) {
              setStatus('Saved: TMDB (id ' + dto.tmdbId + '). Now run Refresh & Scan.', '#4ade80');
            } else {
              setStatus('Saved, but no TMDB id found — falling back to TVDB.', '#fbbf24');
            }
          } else if (dto && dto.source === 'tvmaze') {
            if (dto.tvmazeId) {
              setStatus('Saved: TVMaze (id ' + dto.tvmazeId + '). Now run Refresh & Scan.', '#4ade80');
            } else {
              setStatus('Saved, but no TVMaze id found — falling back to TVDB.', '#fbbf24');
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

  function updateSeriesBadge(source) {
    var badge = document.getElementById('mpo-series-source-badge');
    if (!badge) {
      return;
    }
    badge.textContent = 'Bron: ' + (source ? sourceLabel(source) : 'Default');
    badge.className = 'mpo-badge' + (source ? ' mpo-badge-active' : '');
  }

  function updateSeriesIdBadges(entry) {
    var holder = document.getElementById('mpo-series-id-badges');
    if (!holder) {
      return;
    }
    holder.textContent = '';
    var pairs = [['TVDB', series.tvdbId]];
    if (entry) {
      if (entry.tmdbId) {
        pairs.push(['TMDB', entry.tmdbId]);
      }
      if (entry.aniListId) {
        pairs.push(['AniList', entry.aniListId]);
      }
      if (entry.malId) {
        pairs.push(['MAL', entry.malId]);
      }
      if (entry.tvmazeId) {
        pairs.push(['TVMaze', entry.tvmazeId]);
      }
    }
    for (var i = 0; i < pairs.length; i++) {
      var badge = document.createElement('span');
      badge.className = 'mpo-badge';
      badge.textContent = pairs[i][0] + ' id: ' + pairs[i][1];
      holder.appendChild(badge);
    }
  }

  function currentOverrideEntry(list, tvdbId) {
    for (var i = 0; i < list.length; i++) {
      if (list[i].tvdbId === tvdbId) {
        return list[i];
      }
    }
    return null;
  }

  function currentOverride(list, tvdbId) {
    var entry = currentOverrideEntry(list, tvdbId);
    return entry ? entry.source : '';
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
    var entry = currentOverrideEntry(list, tvdbId);
    return entry ? entry.source : '';
  }

  function sonarrSeriesUrl() {
    if (window.Sonarr && window.Sonarr.apiRoot) {
      return window.Sonarr.apiRoot.replace(/\/+$/, '') + '/series';
    }
    return '/api/v3/series';
  }

  function waitForSonarrKey(maxMs) {
    var start = Date.now();
    var done = false;
    return new Promise(function (resolve) {
      var finish = function (key) {
        if (done) {
          return;
        }
        done = true;
        resolve(key);
      };
      (function poll() {
        if (window.Sonarr && window.Sonarr.apiKey) {
          return finish(window.Sonarr.apiKey);
        }
        if (Date.now() - start >= maxMs) {
          return finish('');
        }
        setTimeout(poll, 250);
      })();
    });
  }

  function getSeriesList() {
    var now = Date.now();
    if (seriesCache && now - seriesCacheAt < SERIES_CACHE_TTL_MS) {
      return Promise.resolve(seriesCache);
    }
    return waitForSonarrKey(6000).then(function (apiKey) {
      var options = { headers: {} };
      if (apiKey) {
        options.headers['X-Api-Key'] = apiKey;
      }
      return fetch(sonarrSeriesUrl(), options);
    }).then(function (response) {
      if (!response.ok) {
        throw new Error(
          'Sonarr API: HTTP ' + response.status + (window.Sonarr && window.Sonarr.apiKey
            ? ''
            : ' — Sonarr API key not available (window.Sonarr.apiKey missing).')
        );
      }
      return response.json();
    }).then(function (list) {
      seriesCache = list;
      seriesCacheAt = Date.now();
      return list;
    });
  }

  function findSeries(ident) {
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
      throw new Error('Series not found via "' + slug + '" (signed in to Sonarr in this browser?).');
    }

    return getSeriesList().then(function (list) {
      var found = pick(list);
      if (found) {
        return found;
      }
      if (seriesCache && Date.now() - seriesCacheAt < SERIES_CACHE_TTL_MS) {
        seriesCache = null;
        seriesCacheAt = 0;
        return getSeriesList().then(function (fresh) {
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

    findSeries(ident)
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
            console.debug('[metadata-proxy-override] overrides list fetch:', url, proxyResponse.status);
            if (!proxyResponse.ok) {
              throw new Error('HTTP ' + proxyResponse.status);
            }
            return proxyResponse.json();
          })
          .then(function (list) {
            console.debug('[metadata-proxy-override] overrides list:', list);
            var entry = currentOverrideEntry(list, series.tvdbId);
            var overrideSource = entry ? entry.source : '';
            console.debug('[metadata-proxy-override] current override for', series.tvdbId, ':', overrideSource);
            select.value = overrideSource;
            updateSeriesBadge(select.value);
            updateSeriesIdBadges(entry);
          })
          .catch(function (err) {
            console.error('[metadata-proxy-override] overrides fetch failed:', err);
            setStatus(
              'overrides API unreachable: ' + proxyUrl() + ' (' + err.message + ')',
              '#f87171'
            );
          });
      })
      .catch(function (err) {
        console.debug('[metadata-proxy-override]', err);
        el = null;
        buildNoticePanel('Error: ' + err.message);
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
    if (v !== 'tmdb' && v !== 'tvdb' && v !== 'anilist' && v !== 'mal' && v !== 'tvmaze') {
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
    var badge = document.getElementById('mpo-current-source');
    if (badge) {
      badge.textContent = SEARCH_PROVIDER ? sourceLabel(SEARCH_PROVIDER) : 'Automatic';
      badge.className = 'mpo-badge' + (SEARCH_PROVIDER ? ' mpo-badge-active' : '');
    }
  }

  function loadSearchProvider() {
    // 1. Direct localStorage als UI-truth (instant, geen flash)
    try {
      var ls = normalizeSearchSource(localStorage.getItem(LS_PROVIDER_KEY));
      if (ls) {
        SEARCH_PROVIDER = ls;
        applySearchProvider(ls);
      }
    } catch (e) { /* ignore */ }

    // 2. Achtergrond: haal serverwaarde op en sync
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
        var server = data && data.source ? normalizeSearchSource(data.source) : '';
        // Alleen overschrijven als server een geldige waarde heeft
        if (server) {
          SEARCH_PROVIDER = server;
          try { localStorage.setItem(LS_PROVIDER_KEY, server); } catch (e) {}
          applySearchProvider(server);
          refreshSearchPickers();
        }
      })
      .catch(function () {
        /* fallback naar localStorage blijft gelden */
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

  function buildSearchPickerPanel() {
    var existing = document.getElementById(SEARCH_UI_ID);
    if (existing) {
      applySearchProvider(SEARCH_PROVIDER);
      return existing;
    }

    var ui = buildShell('Search via', 'Metasources \u25B8', SEARCH_UI_ID);
    ui._mpoBody.style.cssText = 'display:flex;flex-direction:column;gap:10px;';

    var select = document.createElement('select');
    select.className = 'mpo-select';
    select.style.cssText = 'width:100%;';
    select.setAttribute('data-mpo-provider', '1');
    [
      { value: '', label: 'Automatic' },
      { value: 'tmdb', label: 'TMDB' },
      { value: 'tvdb', label: 'TVDB' },
      { value: 'anilist', label: 'AniList' },
      { value: 'mal', label: 'MAL' },
      { value: 'tvmaze', label: 'TVMaze' }
    ].forEach(function (opt) {
      var option = document.createElement('option');
      option.value = opt.value;
      option.textContent = opt.label;
      select.appendChild(option);
    });
    select.addEventListener('change', function () {
      setSearchProvider(this.value);
    });
    ui._mpoBody.appendChild(select);

    var row = document.createElement('div');
    row.style.cssText = 'display:flex;align-items:center;gap:8px;flex-wrap:wrap;';

    var badge = document.createElement('span');
    badge.className = 'mpo-badge';
    badge.id = 'mpo-current-source';
    badge.textContent = 'Automatic';
    row.appendChild(badge);

    var dot = document.createElement('span');
    dot.id = 'mpo-proxy-dot';
    dot.className = 'mpo-dot mpo-dot-off';
    row.appendChild(dot);

    var dotLabel = document.createElement('span');
    dotLabel.className = 'mpo-subtitle';
    dotLabel.id = 'mpo-proxy-label';
    dotLabel.textContent = 'proxy status\u2026';
    row.appendChild(dotLabel);

    ui._mpoBody.appendChild(row);

    var list = document.createElement('ul');
    list.className = 'mpo-bullets';
    [
      'Automatic = METADATA_SOURCE (.env), TVDB fallback on empty/error',
      'Prefixes: tmdb: / tvdb: / anilist: / mal: / tvmaze:'
    ].forEach(function (text) {
      var li = document.createElement('li');
      li.textContent = text;
      list.appendChild(li);
    });
    ui._mpoBody.appendChild(list);

    document.body.appendChild(ui);
    ui.mpoCollapse(true);
    applySearchProvider(SEARCH_PROVIDER);
    refreshProxyStatus();
    return ui;
  }

  function refreshProxyStatus() {
    var dot = document.getElementById('mpo-proxy-dot');
    var label = document.getElementById('mpo-proxy-label');
    var base = overridesApiBase();
    if (!base) {
      if (dot) {
        dot.className = 'mpo-dot mpo-dot-off';
      }
      if (label) {
        label.textContent = 'proxy onbereikbaar (stel OVERRIDES_API_URL in)';
      }
      return;
    }
    fetch(base + '/api/overrides/searchsource')
      .then(function (res) {
        if (!res.ok) {
          throw new Error('HTTP ' + res.status);
        }
        return res.json();
      })
      .then(function () {
        if (dot) {
          dot.className = 'mpo-dot mpo-dot-on';
        }
        if (label) {
          label.textContent = 'proxy online';
        }
      })
      .catch(function () {
        if (dot) {
          dot.className = 'mpo-dot mpo-dot-off';
        }
        if (label) {
          label.textContent = 'proxy offline';
        }
      });
  }

  function attachSearchPicker(input) {
    if (input.getAttribute('data-mpo-search') !== '1') {
      input.setAttribute('data-mpo-search', '1');
      input.addEventListener('focus', function () {
        lastSearchInput = input;
      });
      input.addEventListener('input', function () {
        lastSearchInput = input;
      });
    }
    buildSearchPickerPanel();
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
      removeSearchUi();
      return;
    }
    var candidates = searchInputCandidates();
    if (!candidates.length) {
      removeSearchUi();
      return;
    }
    attachSearchPicker(candidates[0]);
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
    ensureStylesheet();
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
      ui.mpoCollapse(true);
    }
  });

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initSearchPickers);
  } else {
    initSearchPickers();
  }

  setInterval(tick, POLL_MS);
  setInterval(refreshSearchPickers, POLL_MS);
  // refreshProxyStatus only when panel is open/visible
  tick();
  refreshSearchPickers();
})();