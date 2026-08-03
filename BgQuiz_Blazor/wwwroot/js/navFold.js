// navFold.js — BgQuiz's navigation-fold applier: the app's SECOND authored JS,
// and the only one in the host project. (The first is the .Client's
// wwwroot/js/folderAccess.js, an ES module imported by JsFolderAccess.)
//
// Why this exists in JS at all. The navigation panel's collapse control is an
// uncontrolled checkbox in MainLayout, which renders STATICALLY and cannot be
// made interactive (a RenderFragment @Body cannot cross a rendermode boundary —
// see MainLayoutTests). Blazor's enhanced navigation re-renders that layout on
// every route change and its DOM synchronization clears the checkbox. So no C#
// in the WASM assembly can restore the user's "keep the navigation panel
// folded" setting; only a script sitting outside the app can, on every page
// including the ones the runtime has not booted on yet.
//
// It is loaded as a classic script from App.razor, right after blazor.web.js
// (the documented placement for Blazor.addEventListener), through the same
// @Assets[...] helper as its sibling so it fingerprints and cache-busts with
// every deploy.
//
// TWO-LANGUAGE CONTRACT — this file is coupled to the C# side twice, with no
// compiler checking either end:
//   * STORAGE_KEY / FOLDED_FIELD must match what QuizSettings writes. The C#
//     side owns both; the serialized shape is pinned byte-for-byte by a unit
//     test (QuizSettingsTests), which is the single source of truth for the
//     name read below.
//   * CHECKBOX_SELECTOR must match MainLayout's control. MainLayoutTests pins
//     the DOM contract that same selector depends on.
// Change either end without the other and the setting silently stops working.
(function () {
    'use strict';

    const STORAGE_KEY = 'xg_quizSettings';
    const FOLDED_FIELD = 'keepNavigationPanelFolded';
    const CHECKBOX_SELECTOR = '.sidebar-toggle-checkbox';

    // The one place the DOM is touched. Absent control (a layout without the
    // rail) is a no-op, never an error.
    function setFolded(folded) {
        const checkbox = document.querySelector(CHECKBOX_SELECTOR);
        if (checkbox) {
            checkbox.checked = folded === true;
        }
    }

    // Storage is user-writable and shared with future settings legs, so every
    // way of being unreadable — missing key, malformed JSON, a non-object, a
    // field that is not the literal true — means "not folded". Never throws:
    // this runs on the navigation path, where an exception would take out the
    // handler for good.
    function storedFold() {
        try {
            const raw = localStorage.getItem(STORAGE_KEY);
            if (!raw) return false;
            const parsed = JSON.parse(raw);
            return typeof parsed === 'object' && parsed !== null && parsed[FOLDED_FIELD] === true;
        } catch (e) {
            return false;
        }
    }

    function applyStored() {
        setFolded(storedFold());
    }

    // The seam QuizSettings invokes to move the fold without a navigation. In
    // practice it is called with `false` only: turning the setting ON is
    // deliberately left to the enhancedload handler below, so the panel the user
    // is standing in is never folded under them (finding #50) — turning it OFF
    // cannot wait, because a folded panel offers no navigation to wait for.
    // Takes the value explicitly all the same: the C# side then has no ordering
    // dependency on its own localStorage write having landed first, and this
    // module keeps no opinion about which direction its caller is in.
    window.bgquizNavFold = { apply: setFolded };

    // Initial load: enhancedload does not fire for it.
    applyStored();

    // Every enhanced navigation — which makes this, and not the seam above, the
    // path a newly turned-on setting first takes hold on. It applies INCLUDING
    // after a DOM synchronization that lands late (seen at ~500ms on the
    // deployed host, which is what made a fold taken just after arriving pop
    // back open — umbrella issue #46). Re-applying after each sync is what makes
    // the stored value the last word rather than the first.
    function registerEnhancedLoad() {
        if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
            window.Blazor.addEventListener('enhancedload', applyStored);
            return true;
        }
        return false;
    }

    // Registers immediately in the normal case (blazor.web.js has executed by
    // the time this script runs); the load-event retry covers a boot script that
    // published the global later than expected.
    if (!registerEnhancedLoad()) {
        window.addEventListener('load', registerEnhancedLoad);
    }
})();
