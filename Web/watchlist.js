(function () {
    'use strict';

    var TAG = '[Watchlist]';
    console.log(TAG, 'script loaded, version 1.0.9.0');

    function token() {
        try {
            return window.ApiClient && window.ApiClient.accessToken
                ? window.ApiClient.accessToken()
                : null;
        } catch (_) { return null; }
    }

    function authHeaders() {
        var t = token();
        return t ? { 'Authorization': 'MediaBrowser Token=' + t } : {};
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
    // Jellyfin 10.11 uses `<button class="btnUserData ..." data-method="markFavorite">`
    // (see jellyfin-web src/components/userdatabuttons/userdatabuttons.js).
    var FAV_SELECTORS = [
        'button.btnUserData[data-method="markFavorite"]',  // 10.11
        'button[data-method="markFavorite"]',              // 10.11 fallback (no btnUserData class)
        '.btnUserDataFavorite',
        '.btnFavorite',                                    // legacy
        'button[data-action="favorite"]',
        '.detailButton[data-action="favorite"]',
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
        // Match the reference button's class so styling/sizing line up with the
        // native userdata button next to it. Strip any 'btnUserDataOn' active state.
        var refClass = (refBtn && refBtn.className ? refBtn.className : 'paper-icon-button-light autoSize');
        refClass = refClass.replace(/\bbtnUserDataOn\b/g, '').trim();
        btn.className = refClass + ' btnWatchlistToggle';
        btn.type = 'button';
        // Match the reference's icon span so size/alignment match.
        var refIcon = refBtn ? refBtn.querySelector('span.material-icons') : null;
        var iconCls = refIcon ? refIcon.className.replace(/\b(check|favorite|heart)\b/g, '').trim() : 'material-icons';
        btn.innerHTML = '<span class="' + iconCls + ' bookmark" aria-hidden="true"></span>';
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
            var resp = await fetch('/Watchlist/Status/' + itemId, { headers: authHeaders() });
            if (!resp.ok) {
                console.warn(TAG, 'Status fetch failed:', resp.status, resp.statusText);
                return;
            }
            var data = await resp.json();
            buttonStateFor(itemId, data.inWatchlist);
            applyButtonState(btn, data.inWatchlist);
        } catch (e) {
            console.warn(TAG, 'Status fetch error', e);
            return;
        }

        btn.addEventListener('click', async function (ev) {
            ev.preventDefault();
            ev.stopPropagation();

            var current = btn.getAttribute('data-watchlist-state') === '1';
            var method = current ? 'DELETE' : 'POST';
            try {
                var r = await fetch('/Watchlist/Items/' + itemId, {
                    method: method,
                    headers: authHeaders()
                });
                if (r.ok) {
                    applyButtonState(btn, !current);
                    console.log(TAG, method, 'OK for', itemId);
                } else {
                    console.warn(TAG, method, 'failed:', r.status, await r.text());
                }
            } catch (e) {
                console.warn(TAG, 'toggle error', e);
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
