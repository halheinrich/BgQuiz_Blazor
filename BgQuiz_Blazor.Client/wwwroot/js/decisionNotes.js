// decisionNotes.js — the browser half of the decision's notes overlay's
// placement and size (SPEC-quiz-view.md §4, "The notes overlay's placement is
// a remembered preference", ruled 2026-10-05, halheinrich/backgammon#344, and
// "The notes overlay's size is a remembered preference", ruled 2026-10-25,
// halheinrich/backgammon#414). Imported as an ES module by DecisionNotes;
// nothing else imports it.
//
// Division of labour. The component owns the preferences, the drags, the
// Adjust control and every number that places and sizes the overlay: the
// arithmetic is NotesStage's, in C#, and the overlay's box is what the
// component renders. This side does only what C# cannot:
//   - measure the stage — the layout viewport (the backdrop's box: fixed at
//     inset 0, it covers exactly the area a fixed overlay is placed in), the
//     edge clearance (the backdrop's padding, where the stylesheet states it
//     once), the overlay's size, how far below the overlay's top edge its
//     title bar ends, and the text's line height, which the minimum height is
//     counted in — and report it whenever it can have changed: when watching
//     starts, when the overlay or its title bar changes size (a long note, a
//     resize, the Adjust control's buttons showing), and when the window
//     resizes (size, orientation and browser zoom all arrive as a resize). The
//     report never changes the size the component renders, which is worked
//     from the held size and the area alone, so the observer cannot loop;
//   - hold pointer capture for a drag on the element it started on — the
//     title bar for a move, the resize handle for a resize — so the drag's
//     moves and its release reach that element wherever the pointer goes, and
//     a release over the backdrop is never the click outside that closes the
//     notes; and release it when a drag is cancelled by Esc.
// No number is computed here and no state is kept beyond the listeners: the
// report carries raw lengths, the callback's name is handed in, so it is
// spelled once, on the C# side, and the element to capture on is handed in
// with each capture.

/**
 * Start reporting the stage of the overlay `dialog`, its `titleBar`, its
 * `text` and the `backdrop` beneath it, through `ref`'s [JSInvokable]
 * `method`, which takes the seven lengths below in CSS pixels. Reports at once
 * (the observer's first notification), then on every change. Returns a handle
 * whose capture(id, element) and release(id, element) take and give up
 * pointer capture on `element`, and whose stop() ends the reporting.
 */
export function watch(backdrop, dialog, titleBar, text, ref, method) {
    function report() {
        const area = backdrop.getBoundingClientRect();
        const overlay = dialog.getBoundingClientRect();
        const clearance = parseFloat(getComputedStyle(backdrop).paddingTop);
        const lineHeight = parseFloat(getComputedStyle(text).lineHeight);
        ref.invokeMethodAsync(method,
            area.width, area.height, clearance,
            overlay.width, overlay.height, titleBar.getBoundingClientRect().bottom - overlay.top,
            lineHeight);
    }

    const observer = new ResizeObserver(report);
    observer.observe(dialog);
    observer.observe(titleBar);
    window.addEventListener('resize', report);

    return {
        /** Capture `pointerId` on `element`; false where the pointer is no longer active. */
        capture(pointerId, element) {
            try {
                element.setPointerCapture(pointerId);
                return true;
            } catch {
                return false;
            }
        },
        /** Release `pointerId` from `element`, if it holds it. */
        release(pointerId, element) {
            if (element.hasPointerCapture(pointerId)) element.releasePointerCapture(pointerId);
        },
        /** Stop reporting. */
        stop() {
            observer.disconnect();
            window.removeEventListener('resize', report);
        },
    };
}
