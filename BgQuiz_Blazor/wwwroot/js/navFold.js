// navFold.js — BgQuiz's navigation-fold applier: the one authored script in the
// host project. (The .Client's wwwroot/js holds the Quiz page's two ES modules,
// quizKeys.js for its Space shortcut and actionRowFit.js for its action row. The
// folder module that came first moved to BgFolderAccess_Razor, which ships it
// as its own static web asset.)
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
//     the DOM contract that same selector depends on. SIDEBAR_SELECTOR and
//     the --sidebar-width property name the panel MainLayout.razor.css lays
//     out, and AUTO_FOLD_ATTRIBUTE is the attribute its auto-fold rule keys on.
// Change either end without the other and the setting silently stops working.
//
// THE AUTO-FOLD (SPEC-quiz-view.md §4, halheinrich/backgammon#264's ruling of
// 2026-10-03: "The navigation panel folds by itself below the width where the
// row fits beside it. Opening it again there leaves no control covered.").
// The quiz page measures its action row (actionRowFit.js) and asks this file,
// the panel's one owner, to fold it: setAutoFold(true / false). It is LAYOUT
// state, never the user's preference:
//   * Entering it saves the user's fold (the checkbox) and folds the panel
//     (checks the box, so the control still tells the truth: checked = hidden);
//     <html> carries AUTO_FOLD_ATTRIBUTE while it holds.
//   * While it holds, the user's control still works: unchecking opens the
//     panel as an OVERLAY above the page (MainLayout.razor.css), which takes
//     no width from the row, so opening it covers nothing in the row; checking
//     it again closes the overlay. Neither is a preference: both are forgotten
//     when the auto-fold ends.
//   * Leaving it restores the saved fold exactly. The stored "Keep the
//     navigation panel folded" setting is never written here (this file only
//     ever reads it); a fold applied from it while the auto-fold holds goes to
//     the saved fold, so it lands when the auto-fold ends.
//   * An enhanced navigation's DOM synchronization re-renders the layout: the
//     attribute and the checkbox are reset with it, so the saved fold is
//     discarded and the stored one applied as on any page; the quiz page, if it
//     is still the page, asks again (its module re-measures on the same
//     event, registered after this file's).
(function () {
    'use strict';

    const STORAGE_KEY = 'xg_quizSettings';
    const FOLDED_FIELD = 'keepNavigationPanelFolded';
    const CHECKBOX_SELECTOR = '.sidebar-toggle-checkbox';
    const SIDEBAR_SELECTOR = '.sidebar';
    const SIDEBAR_WIDTH_PROPERTY = '--sidebar-width';
    const AUTO_FOLD_ATTRIBUTE = 'data-nav-autofold';

    // The user's fold while the auto-fold holds (see the header); null otherwise.
    let savedFold = null;

    function autoFolded() {
        return document.documentElement.hasAttribute(AUTO_FOLD_ATTRIBUTE);
    }

    // The user's fold: the checkbox, or, while the auto-fold holds, the fold
    // saved for when it ends. Absent control (a layout without the rail) is a
    // no-op, never an error.
    function setFolded(folded) {
        if (autoFolded()) {
            savedFold = folded === true;
            return;
        }
        const checkbox = document.querySelector(CHECKBOX_SELECTOR);
        if (checkbox) {
            checkbox.checked = folded === true;
        }
    }

    // Fold the panel by itself, or stop (see the header). Idempotent: asking
    // for the state already held changes nothing.
    function setAutoFold(on) {
        const checkbox = document.querySelector(CHECKBOX_SELECTOR);
        if (!checkbox || on === autoFolded()) return;
        if (on) {
            savedFold = checkbox.checked;
            checkbox.checked = true;
            document.documentElement.setAttribute(AUTO_FOLD_ATTRIBUTE, '');
        } else {
            document.documentElement.removeAttribute(AUTO_FOLD_ATTRIBUTE);
            checkbox.checked = savedFold === true;
            savedFold = null;
        }
    }

    // How much narrower the page's content would be if the panel were showing
    // in flow, beyond what it takes now: the panel's width when it is folded or
    // open as an overlay, nothing when it already shows in flow. Null where
    // there is no side panel to fold (the phone layout, where the rail is not
    // displayed). The quiz page subtracts it from its row's width to learn the
    // row the panel would leave, which does not depend on the fold.
    function widthIfShown() {
        const checkbox = document.querySelector(CHECKBOX_SELECTOR);
        const sidebar = document.querySelector(SIDEBAR_SELECTOR);
        if (!checkbox || !sidebar || getComputedStyle(checkbox).display === 'none') return null;
        const style = getComputedStyle(sidebar);
        const showing = parseFloat(style.getPropertyValue(SIDEBAR_WIDTH_PROPERTY)) || 0;
        const inFlow = style.position === 'fixed' ? 0 : sidebar.getBoundingClientRect().width;
        return showing - inFlow;
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
        // After a DOM synchronization the attribute is gone with the old
        // layout, and so is any fold saved for it.
        if (!autoFolded()) savedFold = null;
        setFolded(storedFold());
    }

    // The seam QuizSettings invokes to move the fold without a navigation. In
    // practice it is called with `false` only: turning the setting ON is
    // deliberately left to the enhancedload handler below, so the panel the user
    // is standing in is never folded under them (finding halheinrich/backgammon#50) — turning it OFF
    // cannot wait, because a folded panel offers no navigation to wait for.
    // Takes the value explicitly all the same: the C# side then has no ordering
    // dependency on its own localStorage write having landed first, and this
    // module keeps no opinion about which direction its caller is in.
    // setAutoFold and widthIfShown are the quiz page's row-fit module's
    // (actionRowFit.js; the auto-fold in the header).
    window.bgquizNavFold = { apply: setFolded, setAutoFold, widthIfShown };

    // Initial load: enhancedload does not fire for it.
    applyStored();

    // Every enhanced navigation — which makes this, and not the seam above, the
    // path a newly turned-on setting first takes hold on. It applies INCLUDING
    // after a DOM synchronization that lands late (seen at ~500ms on the
    // deployed host, which is what made a fold taken just after arriving pop
    // back open — umbrella issue halheinrich/backgammon#46). Re-applying after each sync is what makes
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
