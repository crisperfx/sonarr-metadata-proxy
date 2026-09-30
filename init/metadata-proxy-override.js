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
      '.mpo-alert-warning{display:none;align-items:flex-start;gap:8px;padding:10px 12px;border-radius:8px;background:rgba(248,113,113,.12);border:1px solid rgba(248,113,113,.35);border-left:3px solid #f87171;box-shadow:0 1px 3px rgba(0,0,0,.15);word-wrap:break-word;}',
      '.mpo-alert-icon{flex-shrink:0;font-size:14px;line-height:1.3;color:#f87171;}',
      '.mpo-alert-body{flex:1;min-width:0;}',
      '.mpo-alert-title{font:600 12px/1.4 "Open Sans",sans-serif;color:#fca5a5;margin-bottom:2px;}',
      '.mpo-alert-text{font-size:11px;line-height:1.5;color:#fecaca;}',
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
      '.mpo-divider{border:none;border-top:1px solid #393f45;margin:10px 0 4px;}',
      '.mpo-provider{display:flex;align-items:center;gap:8px;padding:6px 8px;background:#222;border-radius:6px;border:1px solid #333;}',
      '.mpo-provider-dot{width:10px;height:10px;border-radius:50%;flex-shrink:0;}',
      '.mpo-provider-dot-on{background:#4ade80;box-shadow:0 0 8px #4ade80;}',
      '.mpo-provider-dot-off{background:#f87171;box-shadow:0 0 8px #f87171;}',
      '.mpo-provider-label{font:500 12px/1 "Open Sans",sans-serif;color:#e1e2e3;}',
      '.mpo-provider-latency{font:11px/1 "Open Sans",sans-serif;color:#909293;margin-left:auto;}',
      '.mpo-provider-error{font:11px/1 "Open Sans",sans-serif;color:#f87171;margin-left:8px;}',
      '.mpo-providers-grid{display:grid;grid-template-columns:1fr 1fr;gap:6px;margin-top:8px;}',
      '.mpo-section-title{font:600 11px/1 "Open Sans",sans-serif;color:#909293;text-transform:uppercase;letter-spacing:.08em;margin:12px 0 6px;}'
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
      'display:flex;align-items:center;justify-content:space-between;gap:10px;margin-bottom:14px;';
    var title = document.createElement('span');
    title.className = 'mpo-panel-title';
    title.style.cssText =
      'font:600 12px/1.4 "Open Sans",sans-serif;text-transform:uppercase;letter-spacing:.12em;color:#e1e2e3;' +
      'margin-right:auto;min-width:0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;';
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
      status.style.display = text ? 'block' : 'none';
    }
  }

  function setSeriesWarn(title, text) {
    var box = el && el.querySelector('#mpo-series-warn');
    if (!box) {
      return;
    }
    var t = box.querySelector('.mpo-alert-title');
    var b = box.querySelector('.mpo-alert-text');
    if (t) {
      t.textContent = title;
    }
    if (b) {
      b.textContent = text;
    }
    box.style.display = 'flex';
  }

  function clearSeriesWarn() {
    var box = el && el.querySelector('#mpo-series-warn');
    if (box) {
      box.style.display = 'none';
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
    return v === 'tmdb' ? 'TMDB' : v === 'tvdb' ? 'TVDB' : v === 'anilist' ? 'AniList' : v === 'mal' ? 'MAL' : v === 'tvmaze' ? 'TVMaze' : v === 'anidb' ? 'AniDB' : '';
  }

  function providerConfigured(id) {
    if (!PROVIDERS || !PROVIDERS.length) {
      return true;
    }
    for (var i = 0; i < PROVIDERS.length; i++) {
      if (PROVIDERS[i].id === id) {
        return PROVIDERS[i].configured !== false;
      }
    }
    return true;
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
      img.referrerPolicy = 'no-referrer';
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
      { value: 'tvmaze', label: 'TVMaze' },
      { value: 'anidb', label: 'AniDB' }
    ].forEach(function (opt) {
      if (opt.value && !providerConfigured(opt.value)) {
        return;
      }
      var option = document.createElement('option');
      option.value = opt.value;
      option.textContent = opt.label;
      select.appendChild(option);
    });
    select.value = '';
    shell._mpoBody.appendChild(select);

    var refreshBtn = document.createElement('button');
    refreshBtn.className = 'mpo-btn-primary';
    refreshBtn.style.cssText = 'margin-top:8px;width:100%;display:none;background:#2f7a44;border-color:#389a52;';
    refreshBtn.textContent = 'Refresh & Scan';
    refreshBtn.addEventListener('click', function () {
      refreshBtn.disabled = true;
      refreshBtn.textContent = 'Refreshing...';
      triggerRefreshScan()
        .then(function () {
          setStatus('Refresh & Scan started. Reloading this page\u2026', '#4ade80');
          window.setTimeout(function () {
            window.location.reload();
          }, 2000);
        })
        .catch(function (err) {
          refreshBtn.disabled = false;
          refreshBtn.textContent = 'Refresh & Scan';
          setStatus('Refresh & Scan failed: ' + err.message, '#f87171');
        });
    });
    shell._mpoBody.appendChild(refreshBtn);

    var someStatus = document.createElement('div');
    someStatus.id = 'mpo-series-status-plain';
    someStatus.className = 'mpo-status';
    shell._mpoBody.appendChild(someStatus);

    var warnBox = document.createElement('div');
    warnBox.className = 'mpo-alert-warning';
    warnBox.id = 'mpo-series-warn';
    var warnIcon = document.createElement('span');
    warnIcon.className = 'mpo-alert-icon';
    warnIcon.textContent = '\u26A0';
    warnBox.appendChild(warnIcon);
    var warnBody = document.createElement('span');
    warnBody.className = 'mpo-alert-body';
    var warnTitle = document.createElement('div');
    warnTitle.className = 'mpo-alert-title';
    warnBody.appendChild(warnTitle);
    var warnText = document.createElement('div');
    warnText.className = 'mpo-alert-text';
    warnBody.appendChild(warnText);
    warnBox.appendChild(warnBody);
    shell._mpoBody.appendChild(warnBox);

    var badge = document.createElement('span');
    badge.className = 'mpo-badge';
    badge.id = 'mpo-series-source-badge';
    badge.style.cssText = 'margin-top:10px;';
    badge.textContent = 'Source: Default';
    shell._mpoBody.appendChild(badge);

    var status = document.createElement('div');
    status.id = 'mpo-series-id-badges';
    shell._mpoBody.appendChild(status);
    updateSeriesIdBadges(null);

    var resetBtn = document.createElement('button');
    resetBtn.className = 'mpo-btn-primary';
    resetBtn.style.cssText = 'margin-top:10px;width:100%;background:#9c4d2e;border-color:#a0542f;';
    resetBtn.textContent = 'Reset series to TVDB';
    resetBtn.addEventListener('click', function () {
      resetSeries(select);
    });
    shell._mpoBody.appendChild(resetBtn);

    var isSynthetic = series.tvdbId >= 1000000000;

    select.addEventListener('change', function () {
      var selectedSource = select.value;
      if (selectedSource === 'tvdb' && isSynthetic) {
        setStatus('Warning: TVDB passthrough does not work for this series (no real TVDB ID). Falling back to the default source.', '#fbbf24');
      }
      updateSeriesBadge(selectedSource);
      refreshBtn.style.display = 'block';
      saveOverride(series.tvdbId, select.value)
        .then(function (dto) {
          if (dto) {
            updateSeriesIdBadges(dto);
            // Check if selected provider has an ID for this series
            // DTO uses camelCase due to JSON CamelCase policy: tmdbId, aniListId, malId, tvmazeId, anidbId, tvdbId
            var idFieldMap = {
              tmdb: 'tmdbId',
              anilist: 'aniListId',
              mal: 'malId',
              tvmaze: 'tvmazeId',
              anidb: 'anidbId',
              tvdb: 'tvdbId'
            };
            var idField = idFieldMap[selectedSource];
            var hasId = idField && typeof dto[idField] === 'number' && !isNaN(dto[idField]) && dto[idField] !== 0;
            if (selectedSource && !hasId) {
              setSeriesWarn(
                'No ' + sourceLabel(selectedSource) + ' ID found',
                'This series has no ' + sourceLabel(selectedSource) + ' ID yet. Switching source may result in wrong metadata until a mapping is recorded.'
              );
            } else {
              clearSeriesWarn();
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
            }
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
    badge.textContent = 'Source: ' + (source ? sourceLabel(source) : 'Default');
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
      if (entry.anidbId) {
        pairs.push(['AniDB', entry.anidbId]);
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

  function sonarrCommandUrl() {
    if (window.Sonarr && window.Sonarr.apiRoot) {
      return window.Sonarr.apiRoot.replace(/\/+$/, '') + '/command';
    }
    return '/api/v3/command';
  }

  function resetSeries(select) {
    var tvdbId = series.tvdbId;
    var title = series.title || ('TVDB ' + tvdbId);
    if (!window.confirm(
      'Reset "' + title + '" to TVDB?\n\n' +
      'All stored IDs (TMDB, TVMaze, AniDB, MAL, AniList)\n' +
      'and episode mappings will be removed and the source\n' +
      'will be set to TVDB. After that, Refresh & Scan is\n' +
      'started automatically.'
    )) {
      return;
    }
    var url = proxyUrl();
    if (!url) {
      setStatus(
        'HTTPS page: set OVERRIDES_API_URL on the Sonarr container.',
        '#f87171'
      );
      return;
    }
    var fail = function (err) {
      setStatus('Reset failed: ' + err.message, '#f87171');
    };
    fetch(url + '/reset/' + tvdbId, { method: 'POST' })
      .then(function (response) {
        if (!response.ok) {
          throw new Error('HTTP ' + response.status);
        }
        return response.json();
      })
      .then(function (dto) {
        if (select) {
          select.value = dto.source || '';
        }
        updateSeriesBadge(dto.source);
        updateSeriesIdBadges(dto);
        setStatus('Reset done: back to TVDB. Refresh & Scan is starting...', '#4ade80');
        return triggerRefreshScan();
      })
      .then(function () {
        setStatus('Reset done and Refresh & Scan started.', '#4ade80');
      })
      .catch(fail);
  }

  function triggerRefreshScan() {
    if (!series || !series.id) {
      setStatus('No Sonarr series id found; run Refresh & Scan manually.', '#fbbf24');
      return Promise.resolve();
    }
    return waitForSonarrKey(6000).then(function (apiKey) {
      var options = {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: 'RefreshSeries', seriesId: series.id, doSearch: false })
      };
      if (apiKey) {
        options.headers['X-Api-Key'] = apiKey;
      }
      return fetch(sonarrCommandUrl(), options);
    }).then(function (response) {
      if (!response.ok) {
        throw new Error('Sonarr RefreshSeries: HTTP ' + response.status);
      }
      return response;
    });
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
  var PROVIDERS = null;
  var lastSearchInput = null;

  function normalizeSearchSource(value) {
    var v = String(value || '').trim().toLowerCase().replace(/:$/, '');
    if (v !== 'tmdb' && v !== 'tvdb' && v !== 'anilist' && v !== 'mal' && v !== 'tvmaze' && v !== 'anidb') {
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
    scanFooter();
  }

  var FOOTER_PROVIDER_RE = /\b(The\s*TVDB|TMDB|TVDB|AniList|MAL|TVMaze|AniDB)\b/gi;
  function footerLabel() {
    return sourceLabel(SEARCH_PROVIDER);
  }
  function scanFooter() {
    if (!document.body) {
      return;
    }
    var label = footerLabel();
    if (!label) {
      return;
    }
    var walker;
    try {
      walker = document.createTreeWalker(document.body, NodeFilter.SHOW_ELEMENT, null);
    } catch (e) {
      return;
    }
    var containers = [];
    while (walker.nextNode()) {
      var el = walker.currentNode;
      if (el.textContent && el.textContent.indexOf('Metadata is provided by') !== -1) {
        containers.push(el);
      }
    }
    for (var i = 0; i < containers.length; i++) {
      var isSmallest = true;
      for (var j = 0; j < containers.length; j++) {
        if (i !== j && containers[i] !== containers[j] && containers[j].contains(containers[i])) {
          isSmallest = false;
          break;
        }
      }
      if (isSmallest) {
        rewriteFooterNode(containers[i], label);
      }
    }
  }

  function rewriteFooterNode(root, label) {
    var walker;
    try {
      walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, null);
    } catch (e) {
      return;
    }
    while (walker.nextNode()) {
      var node = walker.currentNode;
      var next = node.nodeValue ? node.nodeValue.replace(FOOTER_PROVIDER_RE, label) : node.nodeValue;
      if (next !== node.nodeValue) {
        node.nodeValue = next;
      }
    }
  }
  var footerObserver = null;
  var footerTimer = null;
  function watchFooter() {
    scanFooter();
    if (!window.MutationObserver || footerObserver) {
      return;
    }
    try {
      footerObserver = new MutationObserver(function () {
        if (footerTimer) {
          clearTimeout(footerTimer);
        }
        footerTimer = setTimeout(scanFooter, 250);
      });
      footerObserver.observe(document.body, { childList: true, subtree: true, characterData: true });
    } catch (e) {
      /* ignore */
    }
  }

  function loadProviders() {
    var base = overridesApiBase();
    if (!base) {
      return;
    }
    fetch(base + '/api/overrides/providers')
      .then(function (res) {
        if (!res.ok) {
          throw new Error('bad status ' + res.status);
        }
        return res.json();
      })
      .then(function (data) {
        if (data && data.providers) {
          PROVIDERS = data.providers;
          refreshSearchPickers();
        }
      })
      .catch(function () {
        /* keep static full list */
      });
  }

  function loadSearchProvider() {
    // 1. Read localStorage directly as UI truth (instant, no flash)
    try {
      var ls = normalizeSearchSource(localStorage.getItem(LS_PROVIDER_KEY));
      if (ls) {
        SEARCH_PROVIDER = ls;
        applySearchProvider(ls);
      }
    } catch (e) { /* ignore */ }

    // 2. Background: fetch the server value and sync
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
        // Only override when the server has a valid value
        if (server) {
          SEARCH_PROVIDER = server;
          try { localStorage.setItem(LS_PROVIDER_KEY, server); } catch (e) {}
          applySearchProvider(server);
          refreshSearchPickers();
        }
      })
      .catch(function () {
        /* fallback to localStorage remains in effect */
      });
  }
  loadSearchProvider();
  loadProviders();

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
      { value: 'tvmaze', label: 'TVMaze' },
      { value: 'anidb', label: 'AniDB' }
    ].forEach(function (opt) {
      if (opt.value && !providerConfigured(opt.value)) {
        return;
      }
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

    var healthContainer = document.createElement('div');
    healthContainer.id = 'mpo-provider-health';
    healthContainer.style.cssText = 'margin-top:8px;';
    ui._mpoBody.appendChild(healthContainer);

    var list = document.createElement('ul');
    list.className = 'mpo-bullets';
    [
      'Automatic = METADATA_SOURCE (.env), TVDB fallback on empty/error',
      'Prefixes: tmdb: / tvdb: / anilist: / mal: / tvmaze: / anidb:'
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
    refreshProviderHealth();
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
        label.textContent = 'proxy unreachable (set OVERRIDES_API_URL)';
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

  function refreshProviderHealth() {
    var container = document.getElementById('mpo-provider-health');
    if (!container) {
      return;
    }
    var base = overridesApiBase();
    if (!base) {
      container.textContent = 'proxy unreachable';
      return;
    }
    fetch(base + '/api/overrides/health/providers')
      .then(function (res) {
        if (!res.ok) {
          throw new Error('HTTP ' + res.status);
        }
        return res.json();
      })
      .then(function (data) {
        if (!data || !data.providers) {
          container.textContent = 'no data';
          return;
        }
        container.innerHTML = '';
        var title = document.createElement('div');
        title.className = 'mpo-section-title';
        title.textContent = 'Provider Status';
        container.appendChild(title);

        var grid = document.createElement('div');
        grid.className = 'mpo-providers-grid';
        data.providers.forEach(function (p) {
          var card = document.createElement('div');
          card.className = 'mpo-provider';

          var dot = document.createElement('span');
          dot.className = 'mpo-provider-dot ' + (p.online ? 'mpo-provider-dot-on' : 'mpo-provider-dot-off');
          card.appendChild(dot);

          var label = document.createElement('span');
          label.className = 'mpo-provider-label';
          label.textContent = p.label;
          card.appendChild(label);

          if (p.configured === false) {
            var badge = document.createElement('span');
            badge.className = 'mpo-badge';
            badge.style.fontSize = '10px';
            badge.style.padding = '2px 6px';
            badge.textContent = 'not configured';
            card.appendChild(badge);
          }

          if (p.latencyMs != null && p.latencyMs > 0) {
            var latency = document.createElement('span');
            latency.className = 'mpo-provider-latency';
            latency.textContent = p.latencyMs + ' ms';
            card.appendChild(latency);
          }

          if (p.error) {
            var err = document.createElement('span');
            err.className = 'mpo-provider-error';
            err.textContent = p.error;
            card.appendChild(err);
          }

          grid.appendChild(card);
        });
        container.appendChild(grid);
      })
      .catch(function (err) {
        container.textContent = 'failed to load: ' + err.message;
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
    watchFooter();
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