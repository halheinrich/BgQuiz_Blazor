// actionRowWidth.js — reports the width of the quiz page's action row to the
// page, which decides from it whether the cube pills take their short labels
// (SPEC-quiz-view.md §4, "The action row under quiz navigation": the labels
// abbreviate only where the row cannot fit them, at a switch-over width the
// leg measures, never guesses). Loaded as an ES module by the Quiz page, as
// quizKeys.js is; nothing else imports it.
//
// Division of labour: THIS side measures and decides nothing. It watches one
// element and hands its width to the page; the switch-over widths and the rule
// are the page's (Quiz.CubeLabelsAbbreviate), beside the row they describe, so
// no copy of either lives here. The width is the row's own rather than the
// viewport's because the navigation panel's fold changes the row's width at a
// fixed viewport, and only the row's width says what the row can hold in both
// fold states.
//
// What is reported: the row's content-box width, once when observed (a
// ResizeObserver delivers an initial observation for an element with a size)
// and whenever it changes after that. A zero width is never reported. The
// observed element reads zero once Blazor has removed it (the page renders no
// row between quizzes), and a zero would read as a row too narrow for anything,
// putting the short labels on the next row before its first observation.

let observer = null;

/**
 * Watch `row` and report its width through `ref`, the page's
 * DotNetObjectReference, by invoking its [JSInvokable] callback `method`,
 * whose name is handed in rather than spelled here so it has one spelling, on
 * the C# side. Replaces any earlier observation: the page has one row at a
 * time, and observes the new one whenever Blazor creates it afresh.
 */
export function observe(row, ref, method) {
    unobserve();
    let reported = 0;
    observer = new ResizeObserver(entries => {
        const width = entries[entries.length - 1].contentRect.width;
        if (width === 0 || width === reported) return;
        reported = width;
        ref.invokeMethodAsync(method, width);
    });
    observer.observe(row);
}

/**
 * Stop watching. Safe to call when nothing is observed. The page calls it as
 * it leaves, before it disposes the reference the observer calls back through.
 */
export function unobserve() {
    if (observer !== null) observer.disconnect();
    observer = null;
}
