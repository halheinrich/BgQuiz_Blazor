// quizKeys.js — the quiz page's one keyboard shortcut: Space performs the
// primary action (halheinrich/backgammon#149, ruled 2026-09-02: always on, no
// setting). Loaded as an ES module by the Quiz page (import "./js/quizKeys.js"
// — this project's static web assets serve at the app root, the way the folder
// module did before it moved to BgFolderAccess_Razor); nothing else imports it.
//
// Division of labour, ruled: THIS side decides eligibility, synchronously,
// from the event alone — which key, which modifiers, where focus is — and
// calls preventDefault() only when the shortcut fires. The C# side decides
// what the primary action IS right now (Continue at review, Submit once an
// answer has enabled it, nothing while the controller is busy): the state
// rule lives in one place, the page, beside the buttons that show it. No copy
// of the quiz's state lives here, and none may — a JS mirror of "is Submit
// enabled" is exactly the second source the page's CanSubmit exists to
// prevent. That is also why a press that is eligible here but idle there is
// swallowed rather than let scroll: whether to prevent the default has to be
// decided before the async hop to C#, and the desktop quiz page has nothing
// to scroll in any case.
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
//     must still happen; a checked one ignores space natively. The user's
//     last click before Space is a cube pill, which keeps focus, so the
//     one-pill case reads "select it" and the two-pill case "submit".
// Everything else — the body, the board's SVG, a plain div — fires.

let dotNetRef = null;
let methodName = null;
let listener = null;

/**
 * Start listening. `ref` is the page's DotNetObjectReference; `method` is the
 * name of its [JSInvokable] callback — handed in rather than spelled here, so
 * the method has exactly one spelling, C#'s nameof. Idempotent: a second
 * attach replaces the first.
 */
export function attach(ref, method) {
    detach();
    dotNetRef = ref;
    methodName = method;
    listener = onKeyDown;
    document.addEventListener('keydown', listener);
}

/** Stop listening and drop the reference. Safe to call when not attached. */
export function detach() {
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
