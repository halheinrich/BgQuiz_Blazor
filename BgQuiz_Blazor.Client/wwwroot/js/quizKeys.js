// quizKeys.js — the quiz page's one keyboard shortcut: Space presses Continue
// on the solution view and, while answering, Submit when Submit is lit and
// Skip otherwise (halheinrich/backgammon#149, ruled 2026-09-02: always on, no
// setting; amended by halheinrich/backgammon#200, ruled 2026-09-23 and
// amended 2026-09-24). Loaded as an ES module by the Quiz
// page (import "./js/quizKeys.js" — this project's static web assets serve at
// the app root, the way the folder module did before it moved to
// BgFolderAccess_Razor); nothing else imports it.
//
// Division of labour, ruled: THIS side decides eligibility, synchronously,
// from the event alone — which key, which modifiers, where focus is — and
// calls preventDefault() only when the shortcut fires. The C# side decides
// what Space DOES right now (Continue at review, Submit or Skip while
// answering, nothing while the controller is busy), by reading the very gates
// the three buttons render from and calling the very methods they call: the
// state rule lives in one place, the page, beside the buttons that show it.
// No copy of the quiz's state lives here, and none may — a JS mirror of "is
// Submit enabled" is exactly the second source the page's CanSubmit exists
// to prevent. That is also why a press that is eligible here but idle there is
// swallowed rather than let scroll: whether to prevent the default has to be
// decided before the async hop to C#, and the desktop quiz page has nothing
// to scroll in any case.
//
// Attached, the module marks the document element with an attribute whose
// name the page hands in (QuizKeysMark.AttachedAttribute on the C# side —
// never spelled here), and detaching removes it (halheinrich/backgammon#198).
// It is a readiness signal for the browser tests, which cannot otherwise
// know the listener exists before they press; nothing in the app reads it.
//
// The focus filter names where Space already means something, and there the
// shortcut never fires — so nothing double-fires and nothing is stolen:
//   - typing surfaces: text-like <input>s, <textarea>, <select>, editable
//     regions (isContentEditable), role=textbox — space types;
//   - activation surfaces: <button> (button-like <input>s included), <a href>,
//     <summary>, role=button / link — space activates them natively;
//   - checkboxes, native and role=checkbox — space toggles them, checked or
//     not, so they always consume it;
//   - radios, native and role=radio — fire only when the focused radio is
//     ALREADY checked. Space on an unchecked focused radio selects it, which
//     must still happen; a checked one ignores space natively. A cube pill
//     keeps focus after it is clicked, so Space right after choosing an
//     answer comes from a checked pill and reaches the page;
//   - anything inside an open <dialog> — the review's notes overlay
//     (halheinrich/backgammon#31), which takes focus when it opens. Space
//     there scrolls a long note, and the page's buttons are behind the
//     overlay, where a click cannot reach them either.
// Everything else — the body, the board's SVG, a plain div — fires.

let dotNetRef = null;
let methodName = null;
let listener = null;
let markName = null;

/**
 * Start listening. `ref` is the page's DotNetObjectReference; `method` is the
 * name of its [JSInvokable] callback, and `mark` the name of the readiness
 * attribute — both handed in rather than spelled here, so each has exactly
 * one spelling on the C# side. The mark goes on only once the listener is
 * in place, so a reader that sees it can press. Idempotent: a second attach
 * replaces the first.
 */
export function attach(ref, method, mark) {
    detach();
    dotNetRef = ref;
    methodName = method;
    listener = onKeyDown;
    document.addEventListener('keydown', listener);
    markName = mark;
    document.documentElement.setAttribute(markName, '');
}

/**
 * Stop listening, remove the readiness mark, and drop the reference. Safe to
 * call when not attached. The mark comes off first, so no reader can see it
 * while the listener is going. In the app today detach runs only as the page
 * leaves, and that enhanced navigation re-renders <html> without the mark
 * anyway; the removal is here so the module keeps its own promise — marked
 * exactly while listening — without leaning on how the host navigates.
 */
export function detach() {
    if (markName !== null) document.documentElement.removeAttribute(markName);
    markName = null;
    if (listener !== null) document.removeEventListener('keydown', listener);
    listener = null;
    dotNetRef = null;
    methodName = null;
}

function onKeyDown(event) {
    if (!isEligible(event)) return;
    event.preventDefault();
    dotNetRef.invokeMethodAsync(methodName);
}

function isEligible(event) {
    if (event.key !== ' ') return false;
    if (event.ctrlKey || event.altKey || event.metaKey || event.shiftKey) return false;
    if (event.repeat || event.defaultPrevented) return false;
    return !consumesSpace(event.target);
}

// Whether Space already does something at `target` — the filter above, as code.
function consumesSpace(target) {
    if (!(target instanceof Element)) return false;
    if (target.closest('dialog[open]')) return true;
    if (target.isContentEditable) return true;
    if (target instanceof HTMLInputElement) {
        // Every input type but radio consumes: text-like types type a space,
        // checkbox toggles, and the button-like types activate.
        return target.type === 'radio' ? !target.checked : true;
    }
    if (target.matches('textarea, select, button, a[href], summary')) return true;
    const role = target.getAttribute('role');
    if (role === 'radio') return target.getAttribute('aria-checked') !== 'true';
    return role === 'button' || role === 'link' || role === 'textbox' || role === 'checkbox';
}
