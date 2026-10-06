// decisionNotes.js — the browser half of the decision's notes overlay's
// placement (SPEC-quiz-view.md §4, "The notes overlay's placement is a
// remembered preference", ruled 2026-10-05, halheinrich/backgammon#344).
// Imported as an ES module by DecisionNotes; nothing else imports it.
//
// Division of labour. The component owns the preference, the drag, the Move
// control and every number that places the overlay: the placement arithmetic
// is NotesStage's, in C#, and the overlay's position is what the component
// renders. This side does only what C# cannot:
//   - measure the stage — the visible area (the backdrop's box: fixed at
//     inset 0, it covers exactly the area a fixed overlay is placed in), the
//     edge clearance (the backdrop's padding, where the stylesheet states it
//     once), the overlay's size, and how far below the overlay's top edge its
//     title bar ends — and report it whenever it can have changed: when
//     watching starts, when the overlay or its title bar changes size (a long
//     note, the Move control's buttons showing), and when the window resizes
//     (size, orientation and browser zoom all arrive as a resize). The report
//     repositions the overlay and never resizes it, so the observer cannot
//     loop;
//   - hold pointer capture on the title bar for a drag, so the drag's moves
//     and its release reach the title bar wherever the pointer goes, and a
//     release over the backdrop is never the click outside that closes the
//     notes; and release it when a drag is cancelled by Esc.
// No number is computed here and no state is kept beyond the listeners: the
// report carries raw lengths, and the callback's name is handed in, so it is
// spelled once, on the C# side.

/**
 * Start reporting the stage of the overlay `dialog`, its `titleBar` and the
 * `backdrop` beneath it, through `ref`'s [JSInvokable] `method`, which takes
 * the six lengths below in CSS pixels. Reports at once (the observer's first
 * notification), then on every change. Returns a handle whose capture(id)
 * and release(id) take and give up pointer capture on the title bar, and
 * whose stop() ends the reporting.
 */
export function watch(backdrop, dialog, titleBar, ref, method) {
    function report() {
        const area = backdrop.getBoundingClientRect();
        const overlay = dialog.getBoundingClientRect();
        const clearance = parseFloat(getComputedStyle(backdrop).paddingTop);
        ref.invokeMethodAsync(method,
            area.width, area.height, clearance,
            overlay.width, overlay.height, titleBar.getBoundingClientRect().bottom - overlay.top);
    }

    const observer = new ResizeObserver(report);
    observer.observe(dialog);
    observer.observe(titleBar);
    window.addEventListener('resize', report);

    return {
        /** Capture `pointerId` on the title bar; false where the pointer is no longer active. */
        capture(pointerId) {
            try {
                titleBar.setPointerCapture(pointerId);
                return true;
            } catch {
                return false;
            }
        },
        /** Release `pointerId` from the title bar, if it holds it. */
        release(pointerId) {
            if (titleBar.hasPointerCapture(pointerId)) titleBar.releasePointerCapture(pointerId);
        },
        /** Stop reporting. */
        stop() {
            observer.disconnect();
            window.removeEventListener('resize', report);
        },
    };
}
