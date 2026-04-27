(function () {
    'use strict';

    function getToken() {
        try {
            return window.ApiClient && window.ApiClient.accessToken
                ? window.ApiClient.accessToken()
                : null;
        } catch (_) { return null; }
    }

    function getItemId() {
        var hash = window.location.hash || '';
        var qs = hash.indexOf('?') >= 0 ? hash.slice(hash.indexOf('?') + 1) : '';
        var params = new URLSearchParams(qs);
        return params.get('id') || params.get('itemId');
    }

    function authHeader() {
        var token = getToken();
        return token ? { 'Authorization': 'MediaBrowser Token=' + token } : {};
    }

    var _inWatchlist = false;

    function updateBtn(btn, inWl) {
        _inWatchlist = inWl;
        btn.title = inWl ? 'Remove from Watchlist' : 'Add to Watchlist';
        btn.style.color = inWl ? 'var(--theme-button-focus-color, #00a4dc)' : '';
    }

    function createBtn() {
        var btn = document.createElement('button');
        btn.className = 'btnWatchlistToggle paper-icon-button-light';
        btn.type = 'button';
        btn.innerHTML = '<span class="material-icons md-18">bookmark</span>';
        btn.style.cssText = 'cursor:pointer;';
        return btn;
    }

    async function injectButton() {
        var itemId = getItemId();
        if (!itemId) return;

        var hash = window.location.hash || '';
        if (!hash.includes('details') && !hash.includes('item')) return;

        var favBtn = document.querySelector('.btnFavorite');
        if (!favBtn || document.querySelector('.btnWatchlistToggle')) return;

        try {
            var resp = await fetch('/Watchlist/Status/' + itemId, { headers: authHeader() });
            if (!resp.ok) return;
            var data = await resp.json();
            var btn = createBtn();
            updateBtn(btn, data.inWatchlist);

            btn.addEventListener('click', async function () {
                var method = _inWatchlist ? 'DELETE' : 'POST';
                var url = '/Watchlist/Items/' + itemId;
                try {
                    var r = await fetch(url, { method: method, headers: authHeader() });
                    if (r.ok) updateBtn(btn, !_inWatchlist);
                } catch (e) { console.warn('Watchlist toggle error', e); }
            });

            if (favBtn.parentNode) favBtn.parentNode.insertBefore(btn, favBtn.nextSibling);
        } catch (e) {
            console.warn('Watchlist inject error', e);
        }
    }

    var _lastHash = '';
    var observer = new MutationObserver(function () {
        var h = window.location.hash;
        if (h !== _lastHash) { _lastHash = h; setTimeout(injectButton, 400); }
    });
    document.addEventListener('DOMContentLoaded', function () {
        observer.observe(document.body, { childList: true, subtree: true });
        setTimeout(injectButton, 400);
    });

    if (document.readyState !== 'loading') {
        observer.observe(document.body, { childList: true, subtree: true });
        setTimeout(injectButton, 400);
    }
})();
