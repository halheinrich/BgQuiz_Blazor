using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BgQuiz_Blazor.Client.Components;

/// <summary>
/// The decision's notes — the comment its author saved on the position in
/// eXtreme Gammon — as a <b>Notes</b> control that opens the text in an
/// overlay above the page (<c>SPEC-quiz-view.md</c> §4, amended 2026-09-15;
/// issue <c>halheinrich/backgammon#31</c>). The one display seam for
/// <c>DescriptiveData.Comment</c>: nothing else in the app reads it.
///
/// <para>
/// <b>One owner for the control, the overlay and whether it is open.</b> The
/// host passes <see cref="Comment"/> and nothing else, and places the
/// component where the ruling puts it — the review action row's leading
/// cluster, after Redo. The open bit is a field here: never app-scoped, never
/// persisted, never a view state of the page, and set only by the user's own
/// gesture. The ruled consequence "Continue and Redo close it" is the host's
/// render tree doing it — both leave the review branch, which unmounts this
/// component — so nothing here listens for them.
/// </para>
///
/// <para>
/// <b>The text is shown as the converter stamped it.</b> It is rendered as one
/// text node and the stylesheet keeps its whitespace (<c>pre-wrap</c>), so
/// runs of spaces and embedded CRLFs read as their author laid them out. This
/// component never inspects the text's format, and has no need to: XG stores
/// a comment as an RTF document, and since <c>halheinrich/backgammon#233</c>
/// the converter reduces it to the plain text XG's own comment pane shows
/// before stamping it, so <see cref="Comment"/> arrives as plain text. Knowing
/// XG's comment format is the converter's job, not this component's.
/// </para>
///
/// <para>
/// <b>The overlay is a native <c>dialog</c></b>, rendered only while open, so
/// its <c>open</c> attribute accompanies it whenever it exists; it is exposed
/// to assistive technology as a dialog named by its heading. Nothing reflows
/// when it opens — it and its backdrop are fixed-position — so the board never
/// moves (<c>SPEC-quiz-view.md</c> §2's invariance holds by construction). A
/// click anywhere outside lands on the full-viewport backdrop and closes it;
/// Esc closes it while focus is inside, which is where opening puts focus and
/// where a click on the text keeps it; the visible close button is the third
/// way and the one a reader can see. Closing, by any route, returns focus to
/// the control. No authored script is involved — the Space shortcut's filter
/// in <c>quizKeys.js</c> is what keeps Space inside the open notes from
/// reaching the page behind them.
/// </para>
///
/// <para>
/// An empty <see cref="Comment"/> renders nothing at all — no control, and so
/// nothing in the row — which is what "a problem without notes has no
/// control" means.
/// </para>
/// </summary>
public partial class DecisionNotes : ComponentBase
{
    /// <summary>
    /// The control's caption, and the overlay's heading: one word naming one
    /// thing to both audiences, so the dialog is announced by the name of the
    /// control that opened it.
    /// </summary>
    private const string Label = "Notes";

    /// <summary>The close button's accessible name.</summary>
    private const string CloseLabel = "Close notes";

    /// <summary>
    /// The decision's comment, verbatim — <c>DescriptiveData.Comment</c> as the
    /// converter stamped it. Null or empty hides the control entirely, so a
    /// host may bind it unconditionally.
    /// </summary>
    [Parameter, EditorRequired]
    public string? Comment { get; set; }

    /// <summary>The dialog's id, unique per instance — the control's <c>aria-controls</c> target.</summary>
    private readonly string _dialogId = $"decision-notes-{Guid.NewGuid():N}";

    /// <summary>The dialog heading's id, which names the dialog.</summary>
    private string HeadingId => _dialogId + "-heading";

    private ElementReference _toggle;
    private ElementReference _dialog;

    /// <summary>Whether the notes are open.</summary>
    private bool _open;

    /// <summary>
    /// The text the notes were opened on. A host that hands this instance a
    /// different comment while it is open is showing a different decision,
    /// whose notes no gesture opened, so the overlay closes — see
    /// <see cref="OnParametersSet"/>.
    /// </summary>
    private string? _openedOn;

    /// <summary>Where focus goes after the next render, if anywhere.</summary>
    private PendingFocus _pendingFocus;

    /// <summary>
    /// Close without a focus move when the comment the notes were opened on is
    /// no longer the one being shown — including when it has gone. The overlay
    /// only ever shows the notes the user asked for.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (_open && !string.Equals(Comment, _openedOn, StringComparison.Ordinal))
        {
            _open = false;
            _openedOn = null;
        }
    }

    /// <summary>
    /// Move focus where the last transition sent it: into the dialog on open,
    /// back to the control on close. After the render, because the element
    /// that receives it only exists — and only has its reference — once that
    /// render has landed.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var target = _pendingFocus;
        _pendingFocus = PendingFocus.None;
        switch (target)
        {
            case PendingFocus.Dialog:
                await _dialog.FocusAsync();
                break;
            case PendingFocus.Control:
                await _toggle.FocusAsync();
                break;
        }
    }

    /// <summary>The control toggles: it opens the notes, and closes them if a keyboard user returns to it.</summary>
    private void Toggle()
    {
        if (_open)
            Close();
        else
            Open();
    }

    private void Open()
    {
        _open = true;
        _openedOn = Comment;
        _pendingFocus = PendingFocus.Dialog;
    }

    private void Close()
    {
        if (!_open) return;
        _open = false;
        _openedOn = null;
        _pendingFocus = PendingFocus.Control;
    }

    /// <summary>Esc closes the notes; every other key is left alone.</summary>
    private void HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape") Close();
    }

    private enum PendingFocus
    {
        None,
        Dialog,
        Control,
    }
}
