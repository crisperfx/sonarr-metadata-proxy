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

  function baseCss() {
    return (
      'position:fixed;top:64px;right:16px;z-index:99999;background:#222c3d;color:#fff;' +
      'border:1px solid #334155;border-radius:8px;padding:10px 12px;' +
      'font:13px/1.4 "Open Sans",sans-serif;box-shadow:0 4px 12px rgba(0,0,0,.4);min-width:210px;'
    );
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
    var root = document.createElement('div');
    root.id = PANEL_ID;
    root.style.cssText = baseCss();

    var txt = document.createElement('div');
    txt.style.cssText = 'font-weight:600;margin-bottom:8px;';
    txt.textContent = 'Metadata source';
    root.appendChild(txt);

    var hint = document.createElement('div');
    hint.style.cssText = 'font-size:11px;color:#94a3b8;margin-bottom:8px;';
    hint.textContent = message || 'Enter your Sonarr API key first (Settings → General → API Key).';
    root.appendChild(hint);

    var btn = document.createElement('button');
    btn.style.cssText =
      'padding:4px 10px;background:#3b82f6;color:#fff;border:none;border-radius:4px;cursor:pointer;';
    btn.textContent = 'Enter API key';
    btn.addEventListener('click', function () {
      if (setApiKey()) {
        root.remove();
        tick(true);
      }
    });
    root.appendChild(btn);

    document.body.appendChild(root);
  }

  function buildPickerPanel() {
    var root = document.createElement('div');
    root.id = PANEL_ID;
    root.style.cssText = baseCss();

    var title = document.createElement('div');
    title.style.cssText = 'font-weight:600;margin-bottom:6px;';
    title.textContent = 'Metadata source: ' + (series.title || series.tvdbId);
    root.appendChild(title);

    var select = document.createElement('select');
    select.style.cssText = 'width:100%;padding:4px;margin-bottom:6px;';
    [
      { value: '', label: 'Automatic (active source)' },
      { value: 'tmdb', label: 'TMDB (TMDB order)' },
      { value: 'tvdb', label: 'TVDB (TVDB order)' }
    ].forEach(function (opt) {
      var option = document.createElement('option');
      option.value = opt.value;
      option.textContent = opt.label;
      select.appendChild(option);
    });
    select.value = '';
    root.appendChild(select);

    var status = document.createElement('div');
    status.className = 'mpo-status';
    status.style.cssText = 'font-size:11px;color:#94a3b8;';
    status.textContent = 'TVDB id: ' + series.tvdbId;
    root.appendChild(status);

    select.addEventListener('change', function () {
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

    document.body.appendChild(root);
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

  setInterval(tick, POLL_MS);
  tick();
})();