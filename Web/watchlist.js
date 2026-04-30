(function () {
    'use strict';

    var TAG = '[Watchlist]';
    console.log(TAG, 'script loaded, version 1.0.30.0');

    function apiClient() {
        return window.ApiClient || null;
    }

    // Read an ApiClient property that may be a method or a getter/plain value.
    function apiVal(c, name) {
        var v = c[name];
        return typeof v === 'function' ? v.call(c) : v;
    }

    function getToken(c) {
        try {
            var t = apiVal(c, 'accessToken');
            if (t) return t;
            if (c._serverInfo && c._serverInfo.AccessToken) return c._serverInfo.AccessToken;
            if (c.currentUser  && c.currentUser.AccessToken)  return c.currentUser.AccessToken;
        } catch (e) { /* best effort */ }
        return null;
    }

    async function jfAjax(method, path) {
        var c = apiClient();
        if (!c) return Promise.reject(new Error('ApiClient unavailable'));

        var token = getToken(c);
        if (!token) return Promise.reject(Object.assign(new Error('No access token'), { status: 0 }));

        var url = typeof c.getUrl === 'function' ? c.getUrl(path) : path;

        var authHeader = 'MediaBrowser ' + [
            'Client="'   + (apiVal(c, 'appName')            || 'Jellyfin Web') + '"',
            'Device="'   + (apiVal(c, 'deviceName')         || 'Browser')      + '"',
            'DeviceId="' + (apiVal(c, 'deviceId')           || 'unknown')      + '"',
            'Version="'  + (apiVal(c, 'applicationVersion') || '1.0.0')        + '"',
            'Token="'    + token + '"'
        ].join(', ');

        // Log full header so we can compare against a working Jellyfin API call.
        console.log(TAG, 'jfAjax', method, path, 'header:', authHeader);

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
    var _wlIds = null; // cached Set of watchlist item IDs; null = stale

    function invalidateWlCache() { _wlIds = null; }

    async function getWatchlistIds() {
        if (_wlIds !== null) return _wlIds;
        var entries = await jfAjax('GET', 'Watchlist/Items');
        _wlIds = new Set((entries || []).map(function (e) { return e.jellyfinItemId; }));
        return _wlIds;
    }

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

    async function injectCardButtons() {
        var rows = document.querySelectorAll('.cardOverlayButton-br:not([data-wl-injected])');
        if (!rows.length) return;

        var ids;
        try { ids = await getWatchlistIds(); }
        catch (e) { console.warn(TAG, 'injectCardButtons: fetch failed', e); return; }

        rows.forEach(function (row) {
            row.setAttribute('data-wl-injected', '1');
            var card = row.closest('[data-id]');
            if (!card) return;
            var itemId = card.getAttribute('data-id');
            if (!itemId) return;

            var inWl = ids.has(itemId);

            var btn = document.createElement('button');
            btn.setAttribute('is', 'paper-icon-button-light');
            btn.type = 'button';
            btn.className = 'cardOverlayButton cardOverlayButton-hover itemAction paper-icon-button-light';
            btn.title = inWl ? 'Remove from Watchlist' : 'Add to Watchlist';
            btn.setAttribute('data-watchlist-state', inWl ? '1' : '0');

            var icon = document.createElement('span');
            icon.className = 'material-icons cardOverlayButtonIcon cardOverlayButtonIcon-hover ' +
                (inWl ? 'bookmark' : 'bookmark_border');
            icon.setAttribute('aria-hidden', 'true');
            btn.appendChild(icon);

            btn.addEventListener('click', async function (ev) {
                ev.preventDefault();
                ev.stopPropagation();
                var current = btn.getAttribute('data-watchlist-state') === '1';
                var method = current ? 'DELETE' : 'POST';
                try {
                    await jfAjax(method, 'Watchlist/Items/' + itemId);
                    var newState = !current;
                    btn.setAttribute('data-watchlist-state', newState ? '1' : '0');
                    btn.title = newState ? 'Remove from Watchlist' : 'Add to Watchlist';
                    icon.className = 'material-icons cardOverlayButtonIcon cardOverlayButtonIcon-hover ' +
                        (newState ? 'bookmark' : 'bookmark_border');
                    invalidateWlCache();
                    if (method === 'DELETE') reloadWatchlistTab();
                } catch (e) {
                    console.warn(TAG, 'card wl toggle failed for', itemId, e);
                }
            });

            row.appendChild(btn);
        });
    }

    async function ensureButton() {
        if (!looksLikeDetailsPage()) return;

        var itemId = getItemId();
        if (!itemId) {
            console.log(TAG, 'no itemId in URL', window.location.href);
            return;
        }

        // Guard: if button exists for this exact item, skip. If stale (different item), remove it.
        var existing = document.querySelector('.btnWatchlistToggle');
        if (existing) {
            if (existing.getAttribute('data-item-id') === itemId) return;
            existing.remove();
        }

        var favBtn = findFavBtn();
        if (!favBtn) {
            console.log(TAG, 'favorite button not found yet for itemId', itemId);
            return;
        }

        console.log(TAG, 'injecting bookmark button for itemId', itemId);
        var btn = makeButton(favBtn);
        btn.setAttribute('data-item-id', itemId);

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
                invalidateWlCache();
                if (method === 'DELETE') reloadWatchlistTab();
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

    // ── Watchlist tab rendering ───────────────────────────────────────────────
    // Trigger: CustomTabs HTML setting should contain only:
    //   <div id="watchlistTabPage"></div>
    // watchlist.js builds all child elements itself and loads data.

    function reloadWatchlistTab() {
        invalidateWlCache();
        var page = document.getElementById('watchlistTabPage');
        if (!page) return;
        delete page.dataset.wlLoaded;
        page.innerHTML = '';
        loadWatchlistTab();
    }

    function buildWatchlistCard(item, c, grid, empty) {
        var serverId = apiVal(c, 'serverId') || item.ServerId || '';
        var tag = item.ImageTags && item.ImageTags.Primary;
        var imgUrl = tag
            ? '/Items/' + item.Id + '/Images/Primary?fillHeight=570&fillWidth=400&quality=96&tag=' + encodeURIComponent(tag)
            : null;
        var detailHref = '#/details?id=' + item.Id +
            (serverId ? '&serverId=' + encodeURIComponent(serverId) : '');

        var card = document.createElement('div');
        card.className = 'card portrait-card card-hoverable';
        card.style.width = '170px';

        var box = document.createElement('div');
        box.className = 'cardBox cardBox-bottompadded';

        var scalable = document.createElement('div');
        scalable.className = 'cardScalable';

        // Aspect-ratio spacer; fallback icon shown when no poster.
        var padder = document.createElement('div');
        padder.className = 'cardPadder cardPadder-portrait';
        if (!tag) {
            var iconSpan = document.createElement('span');
            iconSpan.className = 'cardImageIcon material-icons movie';
            iconSpan.setAttribute('aria-hidden', 'true');
            padder.appendChild(iconSpan);
        }

        // Image rendered as background-image on the link, matching Jellyfin's pattern.
        var imgLink = document.createElement('a');
        imgLink.href = detailHref;
        imgLink.setAttribute('data-action', 'link');
        imgLink.className = 'cardImageContainer coveredImage cardContent itemAction';
        imgLink.setAttribute('aria-label', item.Name);
        imgLink.setAttribute('role', 'img');
        if (imgUrl) imgLink.style.backgroundImage = 'url("' + imgUrl + '")';

        var played     = item.UserData && item.UserData.Played     ? 'true' : 'false';
        var isFavorite = item.UserData && item.UserData.IsFavorite ? 'true' : 'false';
        var itemType   = item.Type || 'Movie';

        // Overlay: play (primary) + bottom-right row matching native Jellyfin card.
        var overlay = document.createElement('div');
        overlay.className = 'cardOverlayContainer itemAction';
        overlay.setAttribute('data-action', 'link');

        var playBtn = document.createElement('button');
        playBtn.setAttribute('is', 'paper-icon-button-light');
        playBtn.className = 'cardOverlayButton cardOverlayButton-hover itemAction paper-icon-button-light cardOverlayFab-primary';
        playBtn.setAttribute('data-action', 'resume');
        playBtn.title = 'Play';
        var playIcon = document.createElement('span');
        playIcon.className = 'material-icons cardOverlayButtonIcon cardOverlayButtonIcon-hover play_arrow';
        playIcon.setAttribute('aria-hidden', 'true');
        playBtn.appendChild(playIcon);
        overlay.appendChild(playBtn);

        var brRow = document.createElement('div');
        brRow.className = 'cardOverlayButton-br flex';

        // Mark played
        var playedBtn = document.createElement('button');
        playedBtn.setAttribute('is', 'emby-playstatebutton');
        playedBtn.type = 'button';
        playedBtn.setAttribute('data-action', 'none');
        playedBtn.className = 'cardOverlayButton cardOverlayButton-hover itemAction paper-icon-button-light emby-button';
        playedBtn.setAttribute('data-id', item.Id);
        playedBtn.setAttribute('data-serverid', serverId);
        playedBtn.setAttribute('data-itemtype', itemType);
        playedBtn.setAttribute('data-played', played);
        playedBtn.title = 'Mark played';
        var playedIcon = document.createElement('span');
        playedIcon.className = 'material-icons cardOverlayButtonIcon cardOverlayButtonIcon-hover check' +
            (played === 'true' ? '' : ' playstatebutton-icon-unplayed');
        playedIcon.setAttribute('aria-hidden', 'true');
        playedBtn.appendChild(playedIcon);
        brRow.appendChild(playedBtn);

        // Favourite
        var favBtn = document.createElement('button');
        favBtn.setAttribute('is', 'emby-ratingbutton');
        favBtn.type = 'button';
        favBtn.setAttribute('data-action', 'none');
        favBtn.className = 'cardOverlayButton cardOverlayButton-hover itemAction paper-icon-button-light emby-button';
        favBtn.setAttribute('data-id', item.Id);
        favBtn.setAttribute('data-serverid', serverId);
        favBtn.setAttribute('data-itemtype', itemType);
        favBtn.setAttribute('data-likes', '');
        favBtn.setAttribute('data-isfavorite', isFavorite);
        favBtn.title = 'Add to favourites';
        var favIcon = document.createElement('span');
        favIcon.className = 'material-icons cardOverlayButtonIcon cardOverlayButtonIcon-hover favorite';
        favIcon.setAttribute('aria-hidden', 'true');
        favBtn.appendChild(favIcon);
        brRow.appendChild(favBtn);

        // Remove from Watchlist
        var removeBtn = document.createElement('button');
        removeBtn.setAttribute('is', 'paper-icon-button-light');
        removeBtn.type = 'button';
        removeBtn.className = 'cardOverlayButton cardOverlayButton-hover itemAction paper-icon-button-light';
        removeBtn.title = 'Remove from Watchlist';
        var removeIcon = document.createElement('span');
        removeIcon.className = 'material-icons cardOverlayButtonIcon cardOverlayButtonIcon-hover bookmark_remove';
        removeIcon.setAttribute('aria-hidden', 'true');
        removeBtn.appendChild(removeIcon);
        brRow.appendChild(removeBtn);

        overlay.appendChild(brRow);

        scalable.appendChild(padder);
        scalable.appendChild(imgLink);
        scalable.appendChild(overlay);
        box.appendChild(scalable);

        // Title — direct child of cardBox, no cardFooter wrapper.
        var titleDiv = document.createElement('div');
        titleDiv.className = 'cardText cardTextCentered cardText-first';
        var titleBdi = document.createElement('bdi');
        var titleLink = document.createElement('a');
        titleLink.href = detailHref;
        titleLink.setAttribute('data-action', 'link');
        titleLink.className = 'itemAction textActionButton';
        titleLink.title = item.Name;
        titleLink.textContent = item.Name;
        titleBdi.appendChild(titleLink);
        titleDiv.appendChild(titleBdi);
        box.appendChild(titleDiv);

        if (item.ProductionYear) {
            var yearDiv = document.createElement('div');
            yearDiv.className = 'cardText cardTextCentered cardText-secondary';
            var yearBdi = document.createElement('bdi');
            yearBdi.textContent = String(item.ProductionYear);
            yearDiv.appendChild(yearBdi);
            box.appendChild(yearDiv);
        }

        card.appendChild(box);

        removeBtn.addEventListener('click', async function (ev) {
            ev.preventDefault();
            ev.stopPropagation();
            try {
                await jfAjax('DELETE', 'Watchlist/Items/' + item.Id);
                reloadWatchlistTab();
            } catch (e) {
                console.warn(TAG, 'DELETE failed', e);
            }
        });

        return card;
    }

    async function loadWatchlistTab() {
        var page = document.getElementById('watchlistTabPage');
        console.log(TAG, 'loadWatchlistTab: page=' + (page ? 'found' : 'null') +
            (page ? ' wlLoaded=' + (page.dataset.wlLoaded || '0') + ' children=' + page.children.length : ''));
        if (!page) return;
        // Retry if wlLoaded but page is still empty (prior run failed before building DOM).
        if (page.dataset.wlLoaded && page.children.length > 0) return;
        page.dataset.wlLoaded = '1';

        // Build DOM structure mirroring Jellyfin's library page layout.
        page.innerHTML = '';
        page.className = 'padded-top padded-left padded-right';

        var titleBar = document.createElement('div');
        titleBar.className = 'sectionTitleContainer sectionTitleContainer-cards';

        var h2 = document.createElement('h2');
        h2.className   = 'sectionTitle';
        h2.textContent = 'My Watchlist';
        titleBar.appendChild(h2);

        var loading = document.createElement('div');
        loading.id        = 'wlLoading';
        loading.className = 'loadingContent';
        loading.style.cssText = 'text-align:center;padding:4em 1em;opacity:.55;';
        loading.textContent = 'Loading…';

        var empty = document.createElement('div');
        empty.id          = 'wlEmpty';
        empty.className   = 'noItemsMessage';
        empty.style.cssText = 'display:none;text-align:center;padding:4em 1em;opacity:.55;';
        empty.textContent = 'Your watchlist is empty. Tap 🔖 on any movie or series to add it.';

        var grid = document.createElement('div');
        grid.id        = 'wlGrid';
        grid.className = 'itemsContainer vertical-wrap padded-left padded-right';

        page.appendChild(titleBar);
        page.appendChild(loading);
        page.appendChild(empty);
        page.appendChild(grid);

        console.log(TAG, 'loading watchlist tab');
        var c = apiClient();
        if (!c) { loading.textContent = 'ApiClient not available'; loading.style.color = '#f87171'; return; }

        try {
            var entries = await jfAjax('GET', 'Watchlist/Items');
            loading.style.display = 'none';

            if (!entries || entries.length === 0) {
                empty.style.display = '';
                return;
            }

            var ids    = entries.map(function (e) { return e.jellyfinItemId; }).join(',');
            var userId = apiVal(c, 'getCurrentUserId') || apiVal(c, 'currentUserId');
            if (!userId && c._currentUser) userId = c._currentUser.Id;
            if (!userId && c.currentUser)  userId = c.currentUser.Id;
            if (!userId) { loading.style.display = ''; loading.style.color = '#f87171'; loading.textContent = 'Could not determine user ID'; return; }

            var result = await c.getItems(userId, {
                Ids:              ids,
                Fields:           'PrimaryImageAspectRatio,Overview,UserData',
                ImageTypeLimit:   1,
                EnableImageTypes: 'Primary,Thumb,Backdrop'
            });

            if (!result || !result.Items || result.Items.length === 0) {
                empty.style.display = '';
                return;
            }

            result.Items.forEach(function (item) {
                grid.appendChild(buildWatchlistCard(item, c, grid, empty));
            });
        } catch (e) {
            console.error(TAG, 'tab load error:', e);
            loading.style.display = '';
            loading.style.color   = '#f87171';
            loading.textContent   = 'Watchlist error: ' + (e.message || String(e));
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
                injectCardButtons();
                loadWatchlistTab();
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
