(function () {
    'use strict';

    var TAG = '[Watchlist]';
    console.log(TAG, 'script loaded, version 1.0.15.0');

    function apiClient() {
        return window.ApiClient || null;
    }

    // Extract access token from ApiClient regardless of which internal field
    // jellyfin-apiclient uses in the running web version.
    function getToken(c) {
        try {
            if (typeof c.accessToken === 'function') {
                var t = c.accessToken();
                if (t) return t;
            }
            if (c._serverInfo && c._serverInfo.AccessToken) return c._serverInfo.AccessToken;
            if (c.currentUser  && c.currentUser.AccessToken)  return c.currentUser.AccessToken;
        } catch (e) { /* best effort */ }
        return null;
    }

    // Explicit native fetch with full X-Emby-Authorization header.
    // ApiClient.ajax exists but its internal _serverInfo.AccessToken has been
    // observed as null in some 10.11 environments, producing 401 silently.
    async function jfAjax(method, path) {
        var c = apiClient();
        if (!c) return Promise.reject(new Error('ApiClient unavailable'));

        var token = getToken(c);
        console.log(TAG, 'jfAjax', method, path,
            'token:', token ? token.slice(0, 8) + '...' : 'NULL — will 401');

        if (!token) return Promise.reject(Object.assign(new Error('No access token'), { status: 0 }));

        var url = typeof c.getUrl === 'function' ? c.getUrl(path) : path;

        var authHeader = [
            'MediaBrowser',
            'Client="'  + (typeof c.appName            === 'function' ? c.appName()            : 'Jellyfin Web') + '"',
            'Device="'  + (typeof c.deviceName         === 'function' ? c.deviceName()         : 'Browser')      + '"',
            'DeviceId="'+ (typeof c.deviceId           === 'function' ? c.deviceId()           : 'unknown')      + '"',
            'Version="' + (typeof c.applicationVersion === 'function' ? c.applicationVersion() : '1.0.0')        + '"',
            'Token="'   + token + '"'
        ].join(', ');

        var resp = await window.fetch(url, {
            method:  method,
            headers: { 'X-Emby-Authorization': authHeader }
        });

        if (!resp.ok) {
            var err = new Error('HTTP ' + resp.status);
            err.status = resp.status;
            throw err;
        }

        if (method === 'GET') return resp.json();
        return null;
    }

    // Try every known way to extract the current item id from the URL.
    function getItemId() {
        var url = window.location.href;
        var hash = window.location.hash || '';
        var pathname = window.location.pathname || '';

        // Strategy 1: query string in hash, ?id=xxx or ?itemId=xxx
        var qIdx = hash.indexOf('?');
        if (qIdx >= 0) {
            var p = new URLSearchParams(hash.slice(qIdx + 1));
            var v = p.get('id') || p.get('itemId');
            if (v) return v;
        }

        // Strategy 2: query string in real URL
        if (window.location.search) {
            var p2 = new URLSearchParams(window.location.search);
            var v2 = p2.get('id') || p2.get('itemId');
            if (v2) return v2;
        }

        // Strategy 3: path segment that looks like a GUID (hex with optional dashes, 32+ chars)
        var guidRe = /([0-9a-f]{8}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{12})/i;
        var m = url.match(guidRe);
        if (m) return m[1].replace(/-/g, '');

        return null;
    }

    function looksLikeDetailsPage() {
        var url = (window.location.href || '').toLowerCase();
        return url.includes('details')
            || url.includes('/item')
            || url.includes('itemid=')
            || url.includes('?id=');
    }

    // Selectors for the favorite/heart button across Jellyfin web versions.
    // 10.11 detail pages render <button is="emby-ratingbutton" class="...
    // btnUserRating detailButton emby-button" data-isfavorite="false">
    // wrapping a <div class="detailButton-content"><span class="material-icons
    // detailButton-icon favorite">.
    var FAV_SELECTORS = [
        'button.btnUserRating',                            // 10.11 detail page
        'button[is="emby-ratingbutton"]',                  // 10.11 fallback
        'button[data-isfavorite]',                         // 10.11 attribute
        'button.btnUserData[data-method="markFavorite"]',  // userdatabuttons component
        'button[data-method="markFavorite"]',
        '.btnUserDataFavorite',
        '.btnFavorite',                                    // legacy
        'button[data-action="favorite"]',
        'button[title*="avorite" i]'
    ];

    function findFavBtn() {
        for (var i = 0; i < FAV_SELECTORS.length; i++) {
            var els = document.querySelectorAll(FAV_SELECTORS[i]);
            if (els.length > 0) {
                console.log(TAG, 'found favorite button via selector:', FAV_SELECTORS[i], '(' + els.length + ' matches)');
                return els[0];
            }
        }
        return null;
    }

    var _state = {};

    function buttonStateFor(itemId, inWatchlist) {
        _state[itemId] = inWatchlist;
    }

    function applyButtonState(btn, inWl) {
        btn.title = inWl ? 'Remove from Watchlist' : 'Add to Watchlist';
        btn.style.color = inWl ? 'var(--theme-button-focus-color, #00a4dc)' : '';
        btn.setAttribute('data-watchlist-state', inWl ? '1' : '0');
    }

    function makeButton(refBtn) {
        var btn = document.createElement('button');
        btn.type = 'button';

        // Inherit class list from the favorite button so sizing/spacing matches.
        // Strip plugin-specific state classes.
        var refClass = (refBtn && refBtn.className) ? refBtn.className : 'paper-icon-button-light autoSize';
        refClass = refClass.replace(/\b(btnUserDataOn|btnUserRating|btnUserData)\b/g, '').replace(/\s+/g, ' ').trim();
        btn.className = refClass + ' btnWatchlistToggle';

        // 10.11 detail buttons wrap their icon in <div class="detailButton-content">
        // <span class="material-icons detailButton-icon favorite">. Mirror the
        // structure so layout matches; otherwise drop a plain icon span.
        var refContent = refBtn ? refBtn.querySelector('.detailButton-content') : null;
        var refIcon    = refBtn ? refBtn.querySelector('span.material-icons')    : null;

        if (refContent && refIcon) {
            var content = document.createElement('div');
            content.className = refContent.className;
            var icon = document.createElement('span');
            // Keep all of the icon's modifier classes EXCEPT the foreground glyph
            // ones (favorite/check/heart) — replace with bookmark.
            icon.className = refIcon.className.replace(/\b(favorite|check|heart)\b/g, '').replace(/\s+/g, ' ').trim() + ' bookmark';
            icon.setAttribute('aria-hidden', 'true');
            content.appendChild(icon);
            btn.appendChild(content);
        } else {
            var iconCls = refIcon
                ? refIcon.className.replace(/\b(favorite|check|heart)\b/g, '').replace(/\s+/g, ' ').trim()
                : 'material-icons md-18';
            btn.innerHTML = '<span class="' + iconCls + ' bookmark" aria-hidden="true"></span>';
        }

        btn.style.cssText = 'cursor:pointer;';
        return btn;
    }

    async function ensureButton() {
        if (!looksLikeDetailsPage()) return;

        var itemId = getItemId();
        if (!itemId) {
            console.log(TAG, 'no itemId in URL', window.location.href);
            return;
        }

        if (document.querySelector('.btnWatchlistToggle')) return; // already injected

        var favBtn = findFavBtn();
        if (!favBtn) {
            console.log(TAG, 'favorite button not found yet for itemId', itemId);
            return;
        }

        console.log(TAG, 'injecting bookmark button for itemId', itemId);
        var btn = makeButton(favBtn);

        try {
            var data = await jfAjax('GET', 'Watchlist/Status/' + itemId);
            buttonStateFor(itemId, !!data.inWatchlist);
            applyButtonState(btn, !!data.inWatchlist);
        } catch (e) {
            console.warn(TAG, 'Status fetch failed', e && e.status, e);
            return;
        }

        btn.addEventListener('click', async function (ev) {
            ev.preventDefault();
            ev.stopPropagation();

            var current = btn.getAttribute('data-watchlist-state') === '1';
            var method = current ? 'DELETE' : 'POST';
            try {
                await jfAjax(method, 'Watchlist/Items/' + itemId);
                applyButtonState(btn, !current);
                console.log(TAG, method, 'OK for', itemId);
            } catch (e) {
                console.warn(TAG, method, 'failed', e && e.status, e);
            }
        });

        // Insert directly after the favorite button.
        if (favBtn.parentNode) {
            favBtn.parentNode.insertBefore(btn, favBtn.nextSibling);
            console.log(TAG, 'bookmark button inserted');
        }
    }

    // Aggressive observation: SPA navigations + DOM mutations.
    function startObserver() {
        var lastUrl = '';
        var debounce = null;

        function tick(reason) {
            if (debounce) clearTimeout(debounce);
            debounce = setTimeout(function () {
                if (window.location.href !== lastUrl) {
                    lastUrl = window.location.href;
                    console.log(TAG, 'navigation detected (' + reason + '):', lastUrl);
                }
                ensureButton();
            }, 250);
        }

        var mo = new MutationObserver(function () { tick('mutation'); });
        mo.observe(document.body, { childList: true, subtree: true });

        window.addEventListener('hashchange', function () { tick('hashchange'); });
        window.addEventListener('popstate',   function () { tick('popstate'); });

        // Patch pushState/replaceState so we catch React Router navigations.
        ['pushState', 'replaceState'].forEach(function (m) {
            var orig = history[m];
            history[m] = function () {
                var r = orig.apply(this, arguments);
                tick(m);
                return r;
            };
        });

        // Initial attempt + retries during boot.
        tick('initial');
        setTimeout(function () { tick('boot-1'); }, 1000);
        setTimeout(function () { tick('boot-2'); }, 3000);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', startObserver);
    } else {
        startObserver();
    }
})();
