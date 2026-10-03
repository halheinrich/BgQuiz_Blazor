// actionRowFit.js — fits the quiz page's action row to the width it is given
// (SPEC-quiz-view.md §4: halheinrich/backgammon#264's ruling of 2026-10-03,
// "the row narrows; the board keeps its height", and the cube labels' switch).
// Loaded as an ES module by the Quiz page, as quizKeys.js is; nothing else
// imports it.
//
// It measures, under the fonts actually rendering, and decides two things:
//
//   1. Whether the navigation panel folds by itself. The page renders an
//      invisible, inert ruler beside the row (Quiz.razor): the answer rows in
//      their narrowest form (data-ruler="lead") and the tail at its floor
//      (data-ruler="tail"). The widest lead, the row's gap and the tail's
//      floor are the BUDGET — one for every state, so no state's board or
//      chrome differs from another's. The panel folds while the row the panel
//      would leave, if it were showing in flow, is narrower than the budget.
//      That width never depends on the fold itself — it is the row's width
//      less whatever the showing panel would take that it does not take now —
//      so folding cannot make the row wide enough to unfold it again, and a
//      resize at the boundary cannot oscillate. The fold is layout state, held
//      and applied by navFold.js (window.bgquizNavFold.setAutoFold), which
//      owns the panel and keeps the user's own fold untouched.
//
//   2. Whether the cube pills' full form fits. While a cube decision is
//      answered the ruler also holds the full-label row at its widest
//      selection (data-ruler="full-cube"); it fits when that line, the gap and
//      the tail's floor fit the row as it stands after the fold. That answer
//      goes to the page (the [JSInvokable] callback handed in), which renders
//      the pills' form; this side renders nothing.
//
// When it measures: once on observe; on refresh() (the page calls it when the
// problem on screen, or whether it is answered, changes); on every window
// resize; and whenever the row or any ruler line changes size — the panel
// toggled, a scrollbar coming or going, a font finishing loading. The page's
// report is sent whenever it changes, and unconditionally after observe and
// refresh, so each new decision is reported.
//
// Why the window's resize event and not only a ResizeObserver: a resize event
// fires early in the frame, before layout, so the fold it decides is in place
// before that frame paints. A ResizeObserver callback runs after layout, and
// changing the fold there resizes the row inside the observer's own delivery,
// which the browser reports as an error ("ResizeObserver loop completed with
// undelivered notifications"). So an observer callback only asks for a fit on
// the next animation frame; by then a resize has usually already fitted the
// row, and the frame's fit finds nothing to change.
//
// The ruler's data-ruler names are a contract with Quiz.razor, spelled once
// on each side.

let state = null;

/**
 * Start fitting `row`, measuring with `ruler`, reporting the full-form fit
 * through `ref`, the page's DotNetObjectReference, by invoking its
 * [JSInvokable] `method` — a name handed in so it has one spelling, on the C#
 * side. Replaces any earlier observation.
 */
export function observe(row, ruler, ref, method) {
    unobserve();
    state = {
        row,
        ruler,
        ref,
        method,
        reported: null,
        frame: 0,
        observer: new ResizeObserver(fitNextFrame),
        onResize: () => fit(false),
        onEnhancedLoad: () => fit(false),
    };
    watch();
    window.addEventListener('resize', state.onResize);
    if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
        // An enhanced navigation's DOM synchronization resets the panel (it
        // re-renders the layout), and navFold.js re-applies the user's fold on
        // it; measure again after, so a fold still needed is re-applied.
        window.Blazor.addEventListener('enhancedload', state.onEnhancedLoad);
    }
    fit(true);
}

/** Measure afresh, after the page changed what the ruler holds, and report unconditionally. */
export function refresh() {
    if (state === null) return;
    watch();
    fit(true);
}

/**
 * Stop fitting and hand the panel back: the fold this module asked for ends,
 * and navFold.js restores the user's own. Safe to call when nothing is
 * observed. The page calls it as it leaves, before it disposes the reference.
 */
export function unobserve() {
    if (state === null) return;
    state.observer.disconnect();
    cancelAnimationFrame(state.frame);
    window.removeEventListener('resize', state.onResize);
    if (window.Blazor && typeof window.Blazor.removeEventListener === 'function') {
        window.Blazor.removeEventListener('enhancedload', state.onEnhancedLoad);
    }
    state = null;
    if (window.bgquizNavFold) window.bgquizNavFold.setAutoFold(false);
}

// Observe the row and every ruler line now present.
function watch() {
    const { observer, row, ruler } = state;
    observer.disconnect();
    observer.observe(row);
    for (const line of ruler.children) observer.observe(line);
}

// A ResizeObserver callback's request: one fit on the next animation frame,
// however many observations arrived (see the header for why not at once).
function fitNextFrame() {
    if (state === null || state.frame !== 0) return;
    state.frame = requestAnimationFrame(() => {
        if (state === null) return;
        state.frame = 0;
        fit(false);
    });
}

function fit(report) {
    if (state === null) return;
    const { row, ruler } = state;
    if (!row.isConnected || !ruler.isConnected) return;

    const leads = [...ruler.querySelectorAll('[data-ruler="lead"]')].map(width);
    const tail = ruler.querySelector('[data-ruler="tail"]');
    if (leads.length === 0 || tail === null) return;

    const gap = parseFloat(getComputedStyle(row).columnGap) || 0;
    const floor = width(tail);
    const budget = Math.max(...leads) + gap + floor;

    // 1. The panel. widthIfShown() is null where there is no side panel (the
    // phone layout), and then nothing folds.
    const fold = window.bgquizNavFold;
    const panelWidth = fold ? fold.widthIfShown() : null;
    if (panelWidth !== null) {
        const rowWithPanelShowing = width(row) - panelWidth;
        fold.setAutoFold(rowWithPanelShowing < budget);
    }

    // 2. The cube pills, against the row as it now stands (read after the fold).
    const fullCube = ruler.querySelector('[data-ruler="full-cube"]');
    if (fullCube === null) return;
    const fits = width(fullCube) + gap + floor <= width(row);
    if (report || fits !== state.reported) {
        state.reported = fits;
        state.ref.invokeMethodAsync(state.method, fits);
    }
}

function width(element) {
    return element.getBoundingClientRect().width;
}
