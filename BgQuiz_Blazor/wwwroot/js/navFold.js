// navFold.js — BgQuiz's navigation-fold applier: the one authored script in the
// host project. (The client's own ES modules are in BgQuiz_Blazor.Client's
// wwwroot/js, the one list of them. The folder module that came first moved to
// BgFolderAccess_Razor, which ships it as its own static web asset.)
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
//     The layout also folds the panel, by style alone, while a page carries
//     data-nav-fold-pending (the quiz row's pending presentation, before its
//     first measurement); this file never sets that attribute, and
//     panelWidths() tells its box from its state so the hiding is not
//     mistaken for a fold.
// Change either end without the other and the setting silently stops working.
//
// THE AUTO-FOLD (SPEC-quiz-view.md §4, halheinrich/backgammon#264's ruling of
// 2026-10-03: "The navigation panel folds by itself below the width where the
// row fits beside it. Opened again there, it is a drawer over the page" — Hal,
// 2026-10-03: "yes, make it a drawer". That replaced "Opening it again there
// leaves no control covered", which no panel taking no width from the row can
// satisfy: the consultant's finding on halheinrich/backgammon#8, comment
// 5966011660).
// The quiz page measures its action row (actionRowFit.js) and asks this file,
// the panel's one owner, to fold it: setAutoFold(true / false). It is LAYOUT
// state, never the user's preference:
//   * Entering it saves the user's fold (the checkbox) and folds the panel
//     (checks the box, so the control still tells the truth: checked = hidden);
//     <html> carries AUTO_FOLD_ATTRIBUTE while it holds.
//   * While it holds, the rail still works, and it is the only thing that
//     opens the panel — never this file by itself: unchecking it opens THE
//     DRAWER, the panel lying over the page beside the rail
//     (MainLayout.razor.css). It takes no width from the page, so the row
//     keeps the width it was fitted to; while open it covers part of the
//     page. Escape, a pointer pressed outside it, or the rail closes it (the
//     drawer's handlers below), and once closed nothing is covered. Opening
//     and closing it are not preferences: the saved fold is untouched, and
//     both are forgotten when the auto-fold ends.
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

    // THE SESSION'S PREFERENCE (halheinrich/backgammon#360): the "Keep the
    // navigation panel folded" choice as the user last made it on this page's
    // life, told by QuizSettings through prefer() whenever it changes; null
    // until then. The navigation path applies it in place of the stored value,
    // so a choice holds on every enhanced navigation whether or not the browser
    // let its storage write land. It lives exactly as long as this script's
    // run: a full reload starts afresh, and storage, or the default, speaks
    // again.
    let preferredFold = null;

    function autoFolded() {
        return document.documentElement.hasAttribute(AUTO_FOLD_ATTRIBUTE);
    }

    // The drawer is open: the auto-fold holds and the rail's box is unchecked
    // (checked = hidden, as everywhere). Read afresh each time; the DOM is the
    // one copy of it.
    function drawerOpen() {
        const checkbox = document.querySelector(CHECKBOX_SELECTOR);
        return checkbox !== null && !checkbox.checked && autoFolded();
    }

    function closeDrawer() {
        const checkbox = document.querySelector(CHECKBOX_SELECTOR);
        if (checkbox) checkbox.checked = true;
    }

    // Whether `target` is in the drawer or on its rail.
    function inDrawerOrRail(target) {
        return target instanceof Element && target.closest(`${SIDEBAR_SELECTOR}, ${CHECKBOX_SELECTOR}`) !== null;
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

    // The panel's widths, for the quiz page's row-fit module; null where there
    // is no side panel to fold (the phone layout, where the rail is not
    // displayed):
    //   * showing — what the panel takes from the page in flow when it shows
    //     (its --sidebar-width for the layout band);
    //   * inFlow — what its box takes from the page as laid out now: nothing
    //     while folded, while open as the drawer (fixed, over the page), or
    //     while the quiz row's pending presentation hides it (the
    //     data-nav-fold-pending rule in MainLayout.razor.css);
    //   * taken — what its fold state says it takes: `showing` when it shows,
    //     nothing when the user or the auto-fold has folded it or it is open as
    //     the drawer. Read from the state, not the box, so a style that hides
    //     it for a moment does not count.
    // The module learns the row the panel would leave if it showed (the row,
    // plus inFlow, less showing — which does not depend on the fold) and the
    // row it will stand at once the fold is applied (that, plus showing, less
    // taken).
    function panelWidths() {
        const checkbox = document.querySelector(CHECKBOX_SELECTOR);
        const sidebar = document.querySelector(SIDEBAR_SELECTOR);
        if (!checkbox || !sidebar || getComputedStyle(checkbox).display === 'none') return null;
        const style = getComputedStyle(sidebar);
        const showing = parseFloat(style.getPropertyValue(SIDEBAR_WIDTH_PROPERTY)) || 0;
        const inFlow = style.position === 'fixed' ? 0 : sidebar.getBoundingClientRect().width;
        const taken = checkbox.checked || autoFolded() ? 0 : showing;
        return { showing, inFlow, taken };
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

    // The session's preference where the user has made one this page's life,
    // the stored choice otherwise.
    function chosenFold() {
        return preferredFold !== null ? preferredFold : storedFold();
    }

    function applyChosen() {
        // After a DOM synchronization the attribute is gone with the old
        // layout, and so is any fold saved for it.
        if (!autoFolded()) savedFold = null;
        setFolded(chosenFold());
    }

    // The session's preference (see its declaration). Records the choice and
    // moves nothing: when it takes hold is the caller's decision, made by
    // calling apply as well or not.
    function prefer(folded) {
        preferredFold = folded === true;
    }

    // The seam QuizSettings invokes to move the fold without a navigation. In
    // practice it is called with `false` only: turning the setting ON is
    // deliberately left to the enhancedload handler below, so the panel the user
    // is standing in is never folded under them (finding halheinrich/backgammon#50) — turning it OFF
    // cannot wait, because a folded panel offers no navigation to wait for.
    // Takes the value explicitly all the same: the C# side then has no ordering
    // dependency on its own localStorage write having landed, and this module
    // keeps no opinion about which direction its caller is in. prefer is the
    // other half of every change of the setting: it is what the enhancedload
    // handler applies from then on. setAutoFold and panelWidths are the quiz
    // page's row-fit module's (actionRowFit.js; the auto-fold in the header).
    // A page where this line never runs has no owner for the panel: the quiz
    // row then stays pending, and QuizSettings logs the choice it cannot hand
    // over (INSTRUCTIONS.md, "The panel's owner, and a page without it").
    window.bgquizNavFold = { apply: setFolded, prefer, setAutoFold, panelWidths };

    // Initial load: enhancedload does not fire for it. No preference exists
    // yet on a fresh script, so this is the stored choice.
    applyChosen();

    // THE DRAWER'S CLOSES (see the header). Registered once, on the document,
    // which outlives every enhanced navigation; each reads the drawer's state
    // afresh, so nothing acts while it is closed or where the auto-fold does
    // not hold.
    //
    // A pointer pressed outside the drawer and its rail closes it, and the
    // press goes on to whatever it landed on: focus goes where that press puts
    // it. Capture, so a control that stops its own events still closes it.
    // The rail is left to its own click, which closes the drawer by checking
    // the box.
    document.addEventListener('pointerdown', event => {
        if (drawerOpen() && !inDrawerOrRail(event.target)) closeDrawer();
    }, true);

    // Escape closes it when it is the active surface: not when something
    // nearer took the key — the quiz row's "⋯" list stops its own Escape
    // (menuButton.js), and a prevented press is someone else's — nor while
    // focus is in an open dialog (the review's notes), which closes on its own
    // Escape. Focus inside the drawer goes back to the rail that opened it, as
    // it would be lost with the panel; anywhere else it stays.
    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape' || event.defaultPrevented || !drawerOpen()) return;
        if (event.target instanceof Element && event.target.closest('dialog[open]')) return;
        const sidebar = document.querySelector(SIDEBAR_SELECTOR);
        const focusWasInside = sidebar !== null && sidebar.contains(document.activeElement);
        event.preventDefault();
        closeDrawer();
        if (focusWasInside) document.querySelector(CHECKBOX_SELECTOR)?.focus();
    });

    // Space and Enter in the drawer are the drawer's, never the page's: a
    // press with focus on nothing in particular while the drawer is open —
    // which is how a click on its plain background leaves focus, since a
    // click anywhere else closes it — or on the drawer's own plain content is
    // prevented here, before the quiz page's Space shortcut (quizKeys.js,
    // which yields to a prevented press) can submit or move the quiz on. Its
    // links and the rail keep their own handling; the shortcut already yields
    // to those. Capture on the window, so this runs first.
    window.addEventListener('keydown', event => {
        if ((event.key !== ' ' && event.key !== 'Enter') || !drawerOpen()) return;
        const target = event.target;
        const unfocused = target === document.body || target === document.documentElement;
        const plainInside = inDrawerOrRail(target)
            && target.closest('a[href], button, input, select, textarea, summary') === null;
        if (unfocused || plainInside) event.preventDefault();
    }, true);

    // Every enhanced navigation — which makes this, and not the seam above, the
    // path a newly turned-on setting first takes hold on. It applies INCLUDING
    // after a DOM synchronization that lands late (seen at ~500ms on the
    // deployed host, which is what made a fold taken just after arriving pop
    // back open — umbrella issue halheinrich/backgammon#46). Re-applying after each sync is what makes
    // the chosen value — the session's preference, else the stored one — the
    // last word rather than the first.
    function registerEnhancedLoad() {
        if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
            window.Blazor.addEventListener('enhancedload', applyChosen);
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
