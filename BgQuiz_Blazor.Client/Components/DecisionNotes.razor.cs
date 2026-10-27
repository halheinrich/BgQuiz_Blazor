using System.Globalization;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

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
/// cluster, after the navigation buttons (SPEC-quiz-view.md §4, which read
/// "after Redo" until the Redo button was retired). The open bit is a field
/// here: never app-scoped, never persisted, never a view state of the page,
/// and set only by the user's own gesture. The ruled consequence that leaving
/// the review closes it is the host's render tree doing it — Continue and
/// every navigation button leave the review branch, which unmounts this
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
/// when it opens or moves — it and its backdrop are fixed-position — so the
/// board never moves (<c>SPEC-quiz-view.md</c> §2's invariance holds by
/// construction). A click anywhere outside lands on the full-viewport backdrop
/// and closes it; Esc closes it while focus is inside, which is where opening
/// puts focus and where a click on the text keeps it; the visible close
/// button is the third way and the one a reader can see. Closing, by any
/// route, returns focus to the control. The Space shortcut's filter in
/// <c>quizKeys.js</c> is what keeps Space inside the open notes from reaching
/// the page behind them.
/// </para>
///
/// <para>
/// <b>Where it opens, how large, and how it moves and resizes</b> (§4, "The
/// notes overlay's placement is a remembered preference", ruled 2026-10-05,
/// issue <c>halheinrich/backgammon#344</c>; "The notes overlay's size is a
/// remembered preference", ruled 2026-10-25, issue
/// <c>halheinrich/backgammon#414</c>). It opens at the reader's
/// <see cref="NotesPreferences"/>, which <see cref="NotesPreferencesStore"/>
/// holds and nothing here copies: the overlay is drawn as
/// <see cref="NotesStage.Show"/> of them on the stage <c>decisionNotes.js</c>
/// measures, and centred at the stylesheet's size until the first measurement
/// lands (where and how unset preferences show it anyway). Three ways change
/// it, and each completed change is written through the store in one write:
/// </para>
/// <list type="bullet">
/// <item><description><b>A drag by the title bar</b>, which moves the overlay
/// (<see cref="NotesStage.Drag"/>), or <b>by the resize handle</b> at its
/// bottom-right corner, which resizes it with its top-left corner fixed
/// (<see cref="NotesStage.Resize(NotesPreferences, double, double)"/>). A
/// primary press on either — not on the title bar's buttons, whose groups stop
/// the press — starts it, while no other drag runs, and takes pointer capture
/// on the element pressed, so every move and the release reach it wherever the
/// pointer is. Each move shows the gesture applied to the starting preferences
/// on the stage measured at the start; the release commits it, wherever it
/// happens, and only if it changed something. A release over the backdrop is
/// therefore never a click on it. A lost capture or a cancelled pointer
/// cancels the drag, and so does Esc, which then does only that; a cancelled
/// drag writes nothing, and the overlay is shown as the preferences it started
/// from. Closing or unmounting mid-drag drops the drag with the overlay and
/// writes nothing.</description></item>
/// <item><description><b>The Adjust control</b>, the single-pointer
/// alternative WCAG 2.2 SC 2.5.7 asks for: a title-bar button that shows four
/// move steps, four size steps and Reset. A step writes
/// <see cref="NotesStage.Step"/> or
/// <see cref="NotesStage.Resize(NotesPreferences, NotesSizeStep)"/> of the
/// current preferences, or nothing where it cannot change them; Reset writes
/// <see cref="NotesPreferences.Unset"/>, both preferences at
/// once.</description></item>
/// </list>
///
/// <para>
/// An empty <see cref="Comment"/> renders nothing at all — no control, and so
/// nothing in the row — which is what "a problem without notes has no
/// control" means.
/// </para>
/// </summary>
public partial class DecisionNotes : ComponentBase, IAsyncDisposable
{
    /// <summary>
    /// The placement and size module, imported from this project's static web assets
    /// (served at the app root), as the quiz page's own modules are. Internal
    /// so the bUnit fixtures plan the very import the component makes.
    /// </summary>
    internal const string ModulePath = "./js/decisionNotes.js";

    /// <summary>
    /// The control's caption, and the overlay's heading: one word naming one
    /// thing to both audiences, so the dialog is announced by the name of the
    /// control that opened it.
    /// </summary>
    private const string Label = "Notes";

    /// <summary>
    /// The control's classes — Bootstrap's outline button, and the hook the
    /// tests find the control by — shared with its inert copy
    /// (<see cref="RulerCopy"/>), so the two are one button's look.
    /// </summary>
    private const string ToggleClass = "btn btn-outline-secondary decision-notes-toggle";

    /// <summary>The close button's accessible name.</summary>
    private const string CloseLabel = "Close notes";

    /// <summary>The Adjust control's caption, which is also its accessible name.</summary>
    private const string AdjustLabel = "Adjust";

    /// <summary>The Adjust control's tooltip: what it offers, open or shut.</summary>
    private const string AdjustTitle = "Move or resize the notes a step at a time";

    /// <summary>The step buttons' group's accessible name.</summary>
    private const string StepsName = "Adjust the notes";

    /// <summary>The Reset button's caption, which is also its accessible name.</summary>
    private const string ResetLabel = "Reset";

    /// <summary>The Reset button's tooltip: where and how large it puts the notes.</summary>
    private const string ResetTitle = "Put the notes back in the middle at their first size";

    /// <summary>
    /// The eight step buttons, in the ruling's order — the move steps up, down,
    /// left, right, then the size steps wider, narrower, taller, shorter —
    /// which is their tab order, with Reset after them.
    /// </summary>
    private static readonly StepButton[] Steps =
    [
        new("↑", "Move up", (stage, from) => stage.Step(from, NotesStep.Up)),
        new("↓", "Move down", (stage, from) => stage.Step(from, NotesStep.Down)),
        new("←", "Move left", (stage, from) => stage.Step(from, NotesStep.Left)),
        new("→", "Move right", (stage, from) => stage.Step(from, NotesStep.Right)),
        new(null, "Wider", (stage, from) => stage.Resize(from, NotesSizeStep.Wider)),
        new(null, "Narrower", (stage, from) => stage.Resize(from, NotesSizeStep.Narrower)),
        new(null, "Taller", (stage, from) => stage.Resize(from, NotesSizeStep.Taller)),
        new(null, "Shorter", (stage, from) => stage.Resize(from, NotesSizeStep.Shorter)),
    ];

    /// <summary>
    /// The decision's comment, verbatim — <c>DescriptiveData.Comment</c> as the
    /// converter stamped it. Null or empty hides the control entirely, so a
    /// host may bind it unconditionally.
    /// </summary>
    [Parameter, EditorRequired]
    public string? Comment { get; set; }

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    /// <summary>The placement and size preferences — read for every box drawn, written by every completed gesture.</summary>
    [Inject]
    private NotesPreferencesStore Preferences { get; set; } = default!;

    [Inject]
    private ILogger<DecisionNotes> Logger { get; set; } = default!;

    /// <summary>The dialog's id, unique per instance — the control's <c>aria-controls</c> target.</summary>
    private readonly string _dialogId = $"decision-notes-{Guid.NewGuid():N}";

    /// <summary>The dialog heading's id, which names the dialog.</summary>
    private string HeadingId => _dialogId + "-heading";

    /// <summary>The step buttons' group's id — the Adjust control's <c>aria-controls</c> target.</summary>
    private string StepsId => _dialogId + "-steps";

    private ElementReference _toggle;
    private ElementReference _backdrop;
    private ElementReference _dialog;
    private ElementReference _titleBar;
    private ElementReference _text;
    private ElementReference _resizeHandle;

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

    /// <summary>Whether the Adjust control's step buttons are showing. Shut whenever the notes open.</summary>
    private bool _adjustOpen;

    /// <summary>The stage as last measured for this opening; null until its first report.</summary>
    private NotesStage? _stage;

    /// <summary>The drag in progress, a move or a resize, if any — the one copy of that fact.</summary>
    private PreferencesDrag? _drag;

    /// <summary>Counts openings, so a watch started for one is never kept for the next.</summary>
    private int _opening;

    /// <summary>The opening the last watch was started for.</summary>
    private int _watchedOpening;

    /// <summary>
    /// The preference's load and the module's import, started when the control
    /// first renders, so that both are usually ready before the reader opens
    /// the notes and the overlay is usually placed before it is first painted.
    /// Nothing guarantees that: the stage's measurement is asynchronous, and
    /// an opening early enough can also outrun the import and the load.
    /// </summary>
    private Task? _preparation;

    /// <summary>The imported module; null until the import lands, and for good where it failed.</summary>
    private IJSObjectReference? _module;

    /// <summary>The reference the module reports the stage through; created with the module, disposed with the component.</summary>
    private DotNetObjectReference<DecisionNotes>? _self;

    /// <summary>The module's handle on the open overlay: its reports, and pointer capture on either drag's element. Null while closed.</summary>
    private IJSObjectReference? _watch;

    /// <summary>Set by <see cref="DisposeAsync"/>, so work still in flight releases what it gets rather than keeping it.</summary>
    private bool _disposed;

    /// <summary>How the overlay is drawn: the preferences — mid-drag, the drag's — on the measured stage; null until measured.</summary>
    private NotesBox? Box =>
        _stage is { } stage ? stage.Show(_drag?.Current ?? Preferences.Current) : null;

    /// <summary>
    /// <see cref="Box"/> as the custom properties the stylesheet places and
    /// sizes a <c>data-placed</c> overlay by: the position always, a length only
    /// on an axis whose size is set. Invariant culture: a comma-decimal browser
    /// locale must never reach a CSS length.
    /// </summary>
    private string? BoxStyle
    {
        get
        {
            if (Box is not { } box) return null;
            var style = string.Create(CultureInfo.InvariantCulture,
                $"--notes-left: {box.Left:0.##}px; --notes-top: {box.Top:0.##}px");
            if (box.Width is double width)
                style += string.Create(CultureInfo.InvariantCulture, $"; --notes-width: {width:0.##}px");
            if (box.Height is double height)
                style += string.Create(CultureInfo.InvariantCulture, $"; --notes-height: {height:0.##}px");
            return style;
        }
    }

    /// <summary>
    /// Close without a focus move when the comment the notes were opened on is
    /// no longer the one being shown — including when it has gone. The overlay
    /// only ever shows the notes the user asked for.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (_open && !string.Equals(Comment, _openedOn, StringComparison.Ordinal))
            Shut();
    }

    /// <summary>
    /// Start preparing on the first render that draws the control; move focus
    /// where the last transition sent it — into the dialog on open, back to
    /// the control on close — after the render, because the element that
    /// receives it only exists, and only has its reference, once that render
    /// has landed; and keep the stage's reports running exactly while the
    /// notes are open.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!string.IsNullOrEmpty(Comment)) _preparation ??= PrepareAsync();

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

        if (_open && _watchedOpening != _opening)
        {
            _watchedOpening = _opening;
            await StopWatchingAsync();
            await WatchAsync(_opening);
        }
        else if (!_open)
        {
            await StopWatchingAsync();
        }
    }

    /// <summary>
    /// The stage, as <c>decisionNotes.js</c> measured it (CSS pixels): at the
    /// start of each opening, and again whenever the overlay, its title bar or
    /// the window changes size. Public and <see cref="JSInvokableAttribute"/>
    /// for the module's sake; a JS-invoked call gets no automatic render, so it
    /// asks for one when the stage changed. A report while closed, or of an
    /// element no longer laid out, is not a stage and changes nothing.
    /// </summary>
    /// <param name="areaWidth">The layout viewport's width.</param>
    /// <param name="areaHeight">The layout viewport's height.</param>
    /// <param name="clearance">The edge clearance on each side.</param>
    /// <param name="overlayWidth">The overlay's width.</param>
    /// <param name="overlayHeight">The overlay's height.</param>
    /// <param name="titleBarBottom">The title bar's bottom edge, measured from the overlay's top edge.</param>
    /// <param name="lineHeight">The text's computed line height.</param>
    [JSInvokable]
    public void OnStageMeasured(
        double areaWidth, double areaHeight, double clearance,
        double overlayWidth, double overlayHeight, double titleBarBottom, double lineHeight)
    {
        if (_disposed || !_open) return;
        if (!NotesStage.TryCreate(areaWidth, areaHeight, clearance, overlayWidth, overlayHeight, titleBarBottom,
                lineHeight, out var stage)) return;
        if (_stage == stage) return;
        _stage = stage;
        StateHasChanged();
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
        _opening++;
    }

    private void Close()
    {
        if (!_open) return;
        Shut();
        _pendingFocus = PendingFocus.Control;
    }

    /// <summary>
    /// Everything closing discards: the open bit, the Adjust control's state,
    /// the stage measured for this opening and any drag in progress, which
    /// writes nothing. The preferences are not touched.
    /// </summary>
    private void Shut()
    {
        _open = false;
        _openedOn = null;
        _adjustOpen = false;
        _stage = null;
        _drag = null;
    }

    /// <summary>
    /// Esc cancels a drag in progress, a move or a resize — releasing its
    /// capture, and nothing else — and otherwise closes the notes; every other
    /// key is left alone.
    /// </summary>
    private async Task HandleKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key != "Escape") return;
        if (_drag is { } drag)
        {
            _drag = null;
            if (_watch is not null) await _watch.InvokeVoidAsync("release", drag.PointerId, drag.Captor);
            return;
        }
        Close();
    }

    /// <summary>A press on the title bar starts a move (see <see cref="BeginDragAsync"/>).</summary>
    private Task BeginMoveAsync(PointerEventArgs e) =>
        BeginDragAsync(e, _titleBar, static (stage, from, dx, dy) => stage.Drag(from, dx, dy));

    /// <summary>A press on the resize handle starts a resize (see <see cref="BeginDragAsync"/>).</summary>
    private Task BeginResizeAsync(PointerEventArgs e) =>
        BeginDragAsync(e, _resizeHandle, static (stage, from, dx, dy) => stage.Resize(from, dx, dy));

    /// <summary>
    /// A press on a drag's element starts that drag — the primary button of
    /// the primary pointer, once the stage is known and while no drag of
    /// either kind is already running — and captures the pointer on
    /// <paramref name="captor"/>, the element pressed. A capture the browser
    /// refuses (the pointer was already released) ends the drag at once.
    /// </summary>
    private async Task BeginDragAsync(PointerEventArgs e, ElementReference captor, Func<NotesStage, NotesPreferences, double, double, NotesPreferences> gesture)
    {
        if (e.Button != 0 || !e.IsPrimary || _drag is not null) return;
        if (_stage is not { } stage || _watch is not { } watch) return;

        var drag = new PreferencesDrag(e.PointerId, captor, e.ClientX, e.ClientY, Preferences.Current, stage, gesture);
        _drag = drag;
        if (!await watch.InvokeAsync<bool>("capture", e.PointerId, captor) && _drag == drag)
            _drag = null;
    }

    /// <summary>A move of the dragging pointer shows the drag's preferences.</summary>
    private void ContinueDrag(PointerEventArgs e)
    {
        if (_drag is { } drag && drag.PointerId == e.PointerId)
            drag.MoveTo(e.ClientX, e.ClientY);
    }

    /// <summary>
    /// The release commits the drag, wherever it happens: the preferences it
    /// ends on — for a resize, the size and the placement together — are
    /// written in one write if they differ from the ones it started from.
    /// </summary>
    private async Task EndDragAsync(PointerEventArgs e)
    {
        if (_drag is not { } drag || drag.PointerId != e.PointerId) return;
        _drag = null;
        var ended = drag.MoveTo(e.ClientX, e.ClientY);
        if (ended != drag.From) await Preferences.SetAsync(ended);
    }

    /// <summary>
    /// A cancelled pointer or a lost capture cancels the drag: nothing is
    /// written, and the overlay is drawn as the preferences it started from. A
    /// release's own loss of capture comes after the release has ended the
    /// drag, and finds nothing to cancel.
    /// </summary>
    private void CancelDrag(PointerEventArgs e)
    {
        if (_drag is { } drag && drag.PointerId == e.PointerId) _drag = null;
    }

    /// <summary>The Adjust control shows or hides its step buttons. Not a preference: nothing is written.</summary>
    private void ToggleAdjust() => _adjustOpen = !_adjustOpen;

    /// <summary>
    /// One step, committed: the preferences <paramref name="step"/> makes of
    /// the current ones are written, unless they are the same — a step that
    /// cannot move or resize writes nothing, so an unset preference stays
    /// unset. Not while a drag runs, which owns the preferences until it ends.
    /// </summary>
    private async Task StepAsync(Func<NotesStage, NotesPreferences, NotesPreferences> step)
    {
        if (_drag is not null || _stage is not { } stage) return;
        var from = Preferences.Current;
        var to = step(stage, from);
        if (to != from) await Preferences.SetAsync(to);
    }

    /// <summary>
    /// Reset: both preferences back to unset — the overlay back in the middle
    /// at its default size, the entry removed. Not while a drag runs.
    /// </summary>
    private Task ResetAsync() =>
        _drag is null ? Preferences.SetAsync(NotesPreferences.Unset) : Task.CompletedTask;

    /// <summary>
    /// The control's first render's preparation: the stored preference, then
    /// the module. A module that cannot be imported is logged and leaves the
    /// overlay where and as the stylesheet puts it — centred, at its default
    /// size — unmovable and unresizable; it still opens and closes. A disposal
    /// landing during the import releases it.
    /// </summary>
    private async Task PrepareAsync()
    {
        await Preferences.EnsureLoadedAsync();
        IJSObjectReference module;
        try
        {
            module = await JS.InvokeAsync<IJSObjectReference>("import", ModulePath);
        }
        catch (JSException e)
        {
            Logger.LogWarning(e,
                "The notes' placement and size module ({Module}) could not be imported; the notes open centred at their first size and cannot be moved or resized.",
                ModulePath);
            return;
        }
        if (_disposed)
        {
            await module.DisposeAsync();
            return;
        }
        _module = module;
        _self = DotNetObjectReference.Create(this);
    }

    /// <summary>
    /// Start the stage's reports for <paramref name="opening"/>, once the
    /// preparation is done — unless that opening has closed meanwhile, taking
    /// the elements it would watch with it. A watch that lands after its
    /// opening has closed, or after the component has gone, is stopped at once.
    /// </summary>
    private async Task WatchAsync(int opening)
    {
        await (_preparation ??= PrepareAsync());
        if (_module is null || _self is null || !IsCurrent(opening)) return;

        var watch = await _module.InvokeAsync<IJSObjectReference>(
            "watch", _backdrop, _dialog, _titleBar, _text, _self, nameof(OnStageMeasured));
        if (!IsCurrent(opening))
        {
            await StopAsync(watch);
            return;
        }
        _watch = watch;
    }

    /// <summary>Whether <paramref name="opening"/> is still the notes' opening: open, the latest, and the component alive.</summary>
    private bool IsCurrent(int opening) => !_disposed && _open && opening == _opening;

    private async Task StopWatchingAsync()
    {
        if (_watch is not { } watch) return;
        _watch = null;
        await StopAsync(watch);
    }

    private static async Task StopAsync(IJSObjectReference watch)
    {
        await watch.InvokeVoidAsync("stop");
        await watch.DisposeAsync();
    }

    /// <summary>
    /// Stop the reports and release the module, then the reference it called
    /// back through, which must outlive the last thing that could invoke it.
    /// A drag in progress goes with the component, unwritten.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _drag = null;
        await StopWatchingAsync();
        if (_module is not null)
        {
            await _module.DisposeAsync();
            _module = null;
        }
        _self?.Dispose();
        _self = null;
    }

    /// <summary>
    /// A drag of the title bar or of the resize handle: the pointer that
    /// started it and the element holding its capture, where it started, the
    /// preferences it started from, the stage it is measured on and the
    /// gesture it applies — <see cref="NotesStage.Drag"/> or
    /// <see cref="NotesStage.Resize(NotesPreferences, double, double)"/> — and
    /// the preferences it shows now. (A <c>Func</c>, not a nested delegate
    /// type: the render tree keeps every member of a component's nested types
    /// for the trimmer, and a delegate type's include reflection-only ones.)
    /// </summary>
    private sealed class PreferencesDrag(
        long pointerId, ElementReference captor, double startX, double startY,
        NotesPreferences from, NotesStage stage, Func<NotesStage, NotesPreferences, double, double, NotesPreferences> gesture)
    {
        public long PointerId { get; } = pointerId;

        public ElementReference Captor { get; } = captor;

        public NotesPreferences From { get; } = from;

        public NotesPreferences Current { get; private set; } = from;

        /// <summary>The preferences with the pointer at (<paramref name="x"/>, <paramref name="y"/>): shown now, and returned.</summary>
        public NotesPreferences MoveTo(double x, double y) => Current = gesture(stage, From, x - startX, y - startY);
    }

    /// <summary>
    /// One of the Adjust control's step buttons: an arrow, or <c>null</c> for
    /// a button that shows its name as its text; its name; and what a press
    /// makes of the preferences on the measured stage.
    /// </summary>
    private sealed record StepButton(string? Glyph, string Name, Func<NotesStage, NotesPreferences, NotesPreferences> Apply);

    private enum PendingFocus
    {
        None,
        Dialog,
        Control,
    }
}
