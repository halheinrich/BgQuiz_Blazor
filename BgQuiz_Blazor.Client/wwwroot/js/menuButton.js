// menuButton.js — the keyboard and pointer half of a menu button (the WAI-ARIA
// Authoring Practices' menu-button pattern) whose markup and open state are a
// Blazor component's: today the action row's "⋯" control, TailMenu
// (SPEC-quiz-view.md §4, halheinrich/backgammon#264's widened fourth, Hal,
// 2026-10-03). Imported as an ES module by that component; nothing else
// imports it.
//
// Division of labour. The component renders the toggle, the list while it is
// open (role="menu", its actions role="menuitem"), and aria-expanded; it owns
// whether the list is open, what each item does, and where focus goes when it
// closes. This side does only what Blazor cannot: a keydown whose default
// action is prevented for some keys and not others, and a pointer press
// anywhere on the page. So:
//   - Arrow Down and Up move focus to the next and previous enabled item,
//     wrapping; Home and End to the first and last. Their default (scrolling)
//     is prevented and they go no further.
//   - Escape closes the list and asks for focus back on the toggle. It stops
//     here: Escape closes the active surface only (the navigation panel's
//     drawer, navFold.js, listens on the document and never sees it).
//   - Tab closes the list without moving focus; the browser moves it on.
//   - Enter and Space are left alone. They press the focused item or toggle,
//     natively, which is a click, which is the component's handler — so the
//     keyboard can do nothing a click would not. The quiz page's Space
//     shortcut (quizKeys.js) already yields to a focused button.
//   - A pointer pressed outside the component closes the list and leaves
//     focus where that press puts it.
// None of it acts while the list is closed: the list's presence in the DOM is
// the open state, read afresh on every event, so there is no second copy of it
// here.
//
// The close callback's name is handed in, so it is spelled once, on the C#
// side, and the reference is the component's DotNetObjectReference.

/**
 * Start handling `root` — the element holding the toggle and, while open, the
 * list — closing through `ref`'s [JSInvokable] `closeMethod`, which takes one
 * argument: whether focus goes back to the toggle. Returns a handle whose
 * focusFirst() focuses the list's first enabled item (the component calls it
 * once the list it opened has rendered) and whose detach() stops everything.
 */
export function attach(root, ref, closeMethod) {
    const isOpen = () => root.querySelector('[role="menu"]') !== null;

    const items = () =>
        [...root.querySelectorAll('[role="menu"] [role="menuitem"]')].filter(item => !item.disabled);

    const close = restoreFocus => ref.invokeMethodAsync(closeMethod, restoreFocus);

    function moveFocus(event) {
        const enabled = items();
        if (enabled.length === 0) return false;
        const at = enabled.indexOf(document.activeElement);
        const last = enabled.length - 1;
        const to = {
            ArrowDown: at < 0 || at === last ? 0 : at + 1,
            ArrowUp: at <= 0 ? last : at - 1,
            Home: 0,
            End: last,
        }[event.key];
        if (to === undefined) return false;
        enabled[to].focus();
        return true;
    }

    function onKeyDown(event) {
        if (!isOpen()) return;
        if (event.key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            close(true);
            return;
        }
        if (event.key === 'Tab') {
            close(false);
            return;
        }
        if (moveFocus(event)) {
            event.preventDefault();
            event.stopPropagation();
        }
    }

    function onPointerDown(event) {
        if (isOpen() && !root.contains(event.target)) close(false);
    }

    root.addEventListener('keydown', onKeyDown);
    // Capture, so a press on a control that stops its own events still
    // closes the list.
    document.addEventListener('pointerdown', onPointerDown, true);

    return {
        focusFirst() {
            const first = items()[0];
            if (first) first.focus();
        },
        detach() {
            root.removeEventListener('keydown', onKeyDown);
            document.removeEventListener('pointerdown', onPointerDown, true);
        },
    };
}
