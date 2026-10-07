using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Client.Components;

/// <summary>
/// The quiz page's action-row tail, folded behind one "⋯" control
/// (<c>SPEC-quiz-view.md</c> §4, halheinrich/backgammon#264's widened fourth,
/// Hal, 2026-10-03: "the whole tail folds behind one "⋯" control at the row's
/// far end"). The host renders it in the tail's place exactly where the tail
/// does not fit beside the row's other controls — a measurement that is the
/// host's, never this component's — so the XGID and the locator keep their one
/// home, the tail, one tap away.
///
/// <para>
/// <b>What the list offers</b>, in the tail's own order: Copy XGID, named,
/// acting and confirming as <see cref="XgidLabel"/>'s copy button does — the
/// one statement of both, <see cref="XgidCopy"/>;
/// the locator, in its full wording (<see cref="ProblemLocatorForm.Line"/>) —
/// display only, so a line to read and not an item, labelling the group the
/// copy item sits in; then the host's <see cref="Actions"/>, in its order, each
/// named, acting and disabled as its button is. Each item's visible text is
/// its button's accessible name, so it is named by what it shows.
/// </para>
///
/// <para>
/// <b>The menu-button pattern</b> (WAI-ARIA Authoring Practices). The toggle
/// carries <c>aria-haspopup="menu"</c> and <c>aria-expanded</c>, and opens
/// and closes on its own click, which Enter and Space produce natively.
/// Opening moves focus to the first enabled item. The list closes on Escape,
/// on a pointer pressed anywhere outside this component, on Tab, and on any
/// choice. Where focus goes then depends on what closed it: back to the
/// toggle after Escape and after a choice; after a click elsewhere, wherever
/// that click put it; after Tab, wherever Tab moves it. A choice that leaves
/// the page (Show stats, End quiz) still hands focus to the toggle first, as
/// the pattern does, and the navigation that follows puts it where it puts it,
/// exactly as after pressing the button itself. The keys and the outside
/// press are <c>wwwroot/js/menuButton.js</c>'s, which closes through
/// <see cref="CloseList"/>; the state, the markup and every action are here.
/// </para>
///
/// <para>
/// <b>Copy XGID confirms on the toggle</b> (issue
/// <c>halheinrich/backgammon#334</c>). Choosing it closes the list and hands
/// focus to the toggle, as every choice does, and the copy runs; once the
/// write resolves, the toggle shows its result for the badge's moment — named
/// "Copied", with the badge's ticked clipboard in place of the dots, or named
/// by the failure with the crossed one where the browser refused — and then
/// is "More" again. The toggle because it is what is on screen where the tail
/// is folded, the badge's button not being, and where focus is: it is the
/// control the user just used, as the badge's button is when it confirms on
/// itself. Its width does not change, so the row does not move.
/// </para>
///
/// <para>
/// <b>Opening reflows nothing.</b> The list lies over the page above the row
/// (<c>app.css</c>, <c>.tail-menu-list</c>), so the board never moves and
/// the row never grows (§2).
/// </para>
/// </summary>
public partial class TailMenu : ComponentBase, IAsyncDisposable
{
    /// <summary>
    /// The menu-button module, imported from this project's static web assets
    /// (served at the app root), as the quiz page's own modules are. Internal
    /// so the bUnit fixture plans the very import the component makes.
    /// </summary>
    internal const string ModulePath = "./js/menuButton.js";

    /// <summary>
    /// The toggle's accessible name and tooltip, and the list's name. The
    /// toggle is an icon, a "⋯", so this is what names it to a screen reader
    /// and what a pointer's hover shows.
    /// </summary>
    internal const string ToggleName = "More";

    /// <summary>The decision's XGID, which Copy XGID copies. Empty offers no copy item, as the badge renders nothing.</summary>
    [Parameter, EditorRequired]
    public string Xgid { get; set; } = string.Empty;

    /// <summary>The locator's file name, as <see cref="ProblemLocator.SourceFile"/>.</summary>
    [Parameter, EditorRequired]
    public string? SourceFile { get; set; }

    /// <summary>The locator's game number, as <see cref="ProblemLocator.Game"/>.</summary>
    [Parameter, EditorRequired]
    public int? Game { get; set; }

    /// <summary>The locator's move number, as <see cref="ProblemLocator.MoveNumber"/>.</summary>
    [Parameter, EditorRequired]
    public int? MoveNumber { get; set; }

    /// <summary>
    /// The tail's buttons after the locator, in the tail's order — the host's
    /// to state, since the buttons, their names and their gates are the
    /// host's. The last is the row's far end (End quiz).
    /// </summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<TailMenuAction> Actions { get; set; } = [];

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private TimeProvider Clock { get; set; } = default!;

    [Inject]
    private ILogger<XgidCopy> Logger { get; set; } = default!;

    /// <summary>The Copy XGID item's copies, and the result the toggle is showing.</summary>
    private XgidCopy _copy = default!;

    /// <summary>The toggle's name and tooltip now: a copy's result while it shows, else <see cref="ToggleName"/>.</summary>
    private string ToggleLabel => _copy.ShowingLabel ?? ToggleName;

    /// <summary>The list's id, for the toggle's <c>aria-controls</c>; unique per instance.</summary>
    private readonly string _menuId = $"tail-menu-{Guid.NewGuid():N}";

    /// <summary>The locator line's id, which labels the group the copy item sits in.</summary>
    private string LocatorLineId => _menuId + "-locator";

    private ElementReference _root;
    private ElementReference _toggle;

    /// <summary>The imported module; null until the first render's import lands.</summary>
    private IJSObjectReference? _module;

    /// <summary>The module's handle on this component's element; null until attached.</summary>
    private IJSObjectReference? _handle;

    /// <summary>The reference the module closes the list through; created on attach, disposed with the component.</summary>
    private DotNetObjectReference<TailMenu>? _self;

    /// <summary>Whether the list is open — the one copy of that fact; the module reads the list's presence.</summary>
    private bool _open;

    /// <summary>Where focus goes once the render in flight lands; see <see cref="PendingFocus"/>.</summary>
    private PendingFocus _pendingFocus;

    /// <summary>Set by <see cref="DisposeAsync"/>, so an import still in flight releases rather than attaches.</summary>
    private bool _disposed;

    /// <summary>Give the Copy XGID item its copies, over the injected browser, clock and log.</summary>
    protected override void OnInitialized() => _copy = new XgidCopy(JS, Clock, Logger);

    /// <summary>
    /// Import and attach the module on the first render, then move focus as
    /// the last change asked. An open waits for its list to render before
    /// focusing its first item, which is why focus moves here and not in the
    /// handler.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender) await AttachAsync();

        var target = _pendingFocus;
        _pendingFocus = PendingFocus.None;
        switch (target)
        {
            case PendingFocus.FirstItem when _handle is not null:
                await _handle.InvokeVoidAsync("focusFirst");
                break;
            case PendingFocus.Toggle:
                await _toggle.FocusAsync();
                break;
        }
    }

    /// <summary>
    /// The first render's import and attach. A disposal landing during the
    /// import is honoured by releasing the module instead of attaching it.
    /// </summary>
    private async Task AttachAsync()
    {
        var module = await JS.InvokeAsync<IJSObjectReference>("import", ModulePath);
        if (_disposed)
        {
            await module.DisposeAsync();
            return;
        }
        _module = module;
        _self = DotNetObjectReference.Create(this);
        _handle = await _module.InvokeAsync<IJSObjectReference>("attach", _root, _self, nameof(CloseList));
    }

    /// <summary>The toggle's click: open the list, focusing its first item, or close it, focus staying on the toggle the click left it on.</summary>
    private void Toggle()
    {
        _open = !_open;
        _pendingFocus = _open ? PendingFocus.FirstItem : PendingFocus.None;
    }

    /// <summary>
    /// Close the list, focus going back to the toggle when
    /// <paramref name="restoreFocus"/> (Escape) and staying where it is
    /// otherwise (a pointer pressed elsewhere, Tab). The module's callback,
    /// public and <see cref="JSInvokableAttribute"/> for its sake; a
    /// JS-invoked call gets no automatic render, so it asks for one.
    /// </summary>
    [JSInvokable]
    public void CloseList(bool restoreFocus)
    {
        if (!_open) return;
        _open = false;
        _pendingFocus = restoreFocus ? PendingFocus.Toggle : PendingFocus.None;
        StateHasChanged();
    }

    /// <summary>
    /// Copy XGID: what <see cref="XgidLabel"/>'s copy button does, confirmed
    /// on the toggle once the write resolves.
    /// </summary>
    private Task CopyXgidAsync() =>
        ChooseAsync(() => _copy.CopyAsync(Xgid, StateHasChanged));

    /// <summary>
    /// A choice: the list closes and focus goes back to the toggle, as the
    /// pattern has it, and then the item does what its control does. Focus
    /// moves before the action, so an action that leaves the page — Show
    /// stats, End quiz — leaves focus wherever its navigation puts it, never
    /// pulled back to a control that is gone.
    /// </summary>
    private async Task ChooseAsync(Func<Task> act)
    {
        _open = false;
        await _toggle.FocusAsync();
        await act();
    }

    /// <summary>
    /// Detach the module and release it, then the reference it called back
    /// through, which must outlive the last thing that could invoke it.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_handle is not null)
        {
            await _handle.InvokeVoidAsync("detach");
            await _handle.DisposeAsync();
            _handle = null;
        }
        if (_module is not null)
        {
            await _module.DisposeAsync();
            _module = null;
        }
        _self?.Dispose();
        _self = null;
    }

    /// <summary>Where focus goes after the next render.</summary>
    private enum PendingFocus
    {
        None,
        FirstItem,
        Toggle,
    }
}
