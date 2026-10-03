// actionRowFit.js — fits the quiz page's action row to the width it is given
// (SPEC-quiz-view.md §4: halheinrich/backgammon#264's ruling of 2026-10-03,
// "the row narrows; the board keeps its height", the cube labels' switch, and
// "One budget from the outset", Hal, 2026-10-03). Loaded as an ES module by
// the Quiz page, as quizKeys.js is; nothing else imports it.
//
// It measures, under the fonts actually rendering, and decides three things,
// all from ONE BUDGET that holds for every state and every problem from the
// page's first render. The page renders an invisible, inert ruler beside the
// row (Quiz.razor) whose lines do not depend on the problem on screen: the
// answer rows in their narrowest form (data-ruler="lead" — the checker row,
// and the cube pills' short form drawn by the producer's inert copy, which
// needs no cube decision), the full-form pills (data-ruler="full-cube"), and
// the tail at its floor (data-ruler="tail"). The widest lead, the row's gap and
// the tail's floor are the BUDGET.
//
//   1. Whether the navigation panel folds by itself. It folds while the row
//      the panel would leave, if it were showing in flow, is narrower than the
//      budget. That width never depends on the fold itself — it is the row's
//      width less whatever the showing panel would take that it does not take
//      now — so folding cannot make the row wide enough to unfold it again,
//      and a resize at the boundary cannot oscillate. The fold is layout state,
//      held and applied by navFold.js (window.bgquizNavFold.setAutoFold),
//      which owns the panel and keeps the user's own fold untouched.
//
//   2. Whether the tail folds behind its "⋯" (halheinrich/backgammon#264's
//      widened fourth). It folds while the row as it stands after the panel's
//      fold is narrower than the same budget. The budget holds the tail at
//      full size, whatever the row is showing in its place, so showing the "⋯"
//      cannot make the row fit and switch it back. Where there is no side
//      panel (the phone layout) the tail takes a line of its own
//      (halheinrich/backgammon#236) and never folds.
//
//   3. Whether the cube pills' full form fits: the full-cube line, the gap and
//      the tail's floor against the same row. It is the window's, for every
//      decision: the line holds both readings of the fourth answer and every
//      selection, so the form never depends on which decision is on screen.
//
// "The row as it stands after the panel's fold" is worked out from the
// panel's declared state, not read off its box (navFold.js panelWidths()):
// until the first fit the page holds the row in its pending presentation,
// in which the layout hides the panel by style, and the first fit must decide
// for the row the panel will leave once that ends.
//
// The answers to 2 and 3 go to the page together (the [JSInvokable] callback
// handed in), which renders the tail's and the pills' form; this side renders
// nothing. The first report ends the page's pending presentation.
//
// WHEN IT MEASURES: once per frame at most, always in the frame's animation
// callbacks (requestAnimationFrame) — after the frame's resize event, before
// its style, layout and paint — so what it decides is in place before the
// frame paints. It is asked for on observe (the first fit, the one that ends
// the pending presentation), on every window resize, after an enhanced
// navigation's DOM synchronization, and whenever the row or a ruler line
// changes size (the panel toggled, a scrollbar, a font finishing loading).
// A ResizeObserver callback runs after layout, and changing the fold there
// would resize the row inside the observer's own delivery, which the browser
// reports as an error ("ResizeObserver loop completed with undelivered
// notifications"); from there the fit is asked for in the next frame. The
// page's report is sent whenever it changes, and unconditionally after
// observe, so the first decision is always reported.
//
// The ruler's data-ruler names are a contract with Quiz.razor, spelled once
// on each side.

let state = null;

/**
 * Start fitting `row`, measuring with `ruler`, reporting the tail's and the
 * pills' fit through `ref`, the page's DotNetObjectReference, by invoking its
 * [JSInvokable] `method` — a name handed in so it has one spelling, on the C#
 * side. Replaces any earlier observation. The first fit runs in the coming
 * frame's animation callbacks, before that frame paints.
 */
export function observe(row, ruler, ref, method) {
    unobserve();
    state = {
        row,
        ruler,
        ref,
        method,
        reported: null,   // the last report, as "tailFits/fullCubeFits"
        reportNext: false,
        frame: 0,
        observer: new ResizeObserver(() => requestFit(false)),
        onResize: () => requestFit(false),
        onEnhancedLoad: () => requestFit(false),
    };
    state.observer.observe(row);
    for (const line of ruler.children) state.observer.observe(line);
    window.addEventListener('resize', state.onResize);
    if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
        // An enhanced navigation's DOM synchronization resets the panel (it
        // re-renders the layout), and navFold.js re-applies the user's fold on
        // it; measure again after, so a fold still needed is re-applied.
        window.Blazor.addEventListener('enhancedload', state.onEnhancedLoad);
    }
    requestFit(true);
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

// Ask for one fit in the coming frame's animation callbacks, however many
// requests arrive before it; `report` asks for the report even if unchanged.
function requestFit(report) {
    if (state === null) return;
    state.reportNext = state.reportNext || report;
    if (state.frame !== 0) return;
    state.frame = requestAnimationFrame(() => {
        if (state === null) return;
        state.frame = 0;
        const reportNow = state.reportNext;
        state.reportNext = false;
        fit(reportNow);
    });
}

function fit(report) {
    const { row, ruler } = state;
    if (!row.isConnected || !ruler.isConnected) return;

    const leads = [...ruler.querySelectorAll('[data-ruler="lead"]')].map(width);
    const tail = ruler.querySelector('[data-ruler="tail"]');
    const fullCube = ruler.querySelector('[data-ruler="full-cube"]');
    if (leads.length === 0 || tail === null || fullCube === null) return;

    const gap = parseFloat(getComputedStyle(row).columnGap) || 0;
    const floor = width(tail);
    const budget = Math.max(...leads) + gap + floor;

    // 1. The panel. panelWidths() is null where there is no side panel (the
    // phone layout), and then nothing folds.
    const fold = window.bgquizNavFold;
    const panel = fold ? fold.panelWidths() : null;
    let rowWidth = width(row);
    if (panel !== null) {
        const rowWithPanelShowing = rowWidth + panel.inFlow - panel.showing;
        fold.setAutoFold(rowWithPanelShowing < budget);
        // The row as it stands after the fold, from the panel's declared state.
        rowWidth = rowWithPanelShowing + panel.showing - fold.panelWidths().taken;
    }

    // 2. The tail, against that row.
    const tailFits = panel === null || rowWidth >= budget;

    // 3. The cube pills' full form, against the same row.
    const fullCubeFits = width(fullCube) + gap + floor <= rowWidth;

    const reported = `${tailFits}/${fullCubeFits}`;
    if (report || reported !== state.reported) {
        state.reported = reported;
        state.ref.invokeMethodAsync(state.method, tailFits, fullCubeFits);
    }
}

function width(element) {
    return element.getBoundingClientRect().width;
}
