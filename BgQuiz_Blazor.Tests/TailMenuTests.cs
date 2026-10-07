using AngleSharp.Dom;
using BgQuiz_Blazor.Client.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="TailMenu"/>'s own contract, away from the page that hosts it
/// (<c>SPEC-quiz-view.md</c> §4, halheinrich/backgammon#264's widened fourth,
/// Hal, 2026-10-03): the menu-button markup, what the list offers and under
/// which names, where focus goes by what closed the list, and the module's
/// wiring.
///
/// <para>
/// <b>What bUnit cannot see, and who does.</b> The keys and the outside press
/// are <c>menuButton.js</c>'s, and the list's place over the page is CSS;
/// the browser suite drives both (<c>TailMenuTests</c> in the e2e project).
/// Here the module is planned, and its callback, <see cref="TailMenu.CloseList"/>,
/// is called as the module calls it.
/// </para>
/// </summary>
public class TailMenuTests : BunitContext
{
    private const string Xgid = "XGID=-b----E-C---eE---c-e----B-:0:0:1:52:0:0:0:0:10";

    private const string SourceFile = "synthetic-match-2026-04-12.xg";

    /// <summary>The planned module and the handle its attach returns.</summary>
    private readonly BunitJSModuleInterop _module;
    private readonly BunitJSModuleInterop _handle;

    /// <summary>What the host's actions did, in order.</summary>
    private readonly List<string> _chosen = [];

    /// <summary>The toggle's element-reference id, read off its first render (see DecisionNotesTests for why then).</summary>
    private string? _toggleReferenceId;

    /// <summary>The clock Copy XGID's confirmation runs on; its own contract is <see cref="XgidCopyTests"/>'.</summary>
    private readonly ManualTimeProvider _clock = new();

    public TailMenuTests()
    {
        Services.AddSingleton<TimeProvider>(_clock);
        JSInterop.Mode = JSRuntimeMode.Loose;
        _module = JSInterop.SetupModule(TailMenu.ModulePath);
        _module.Mode = JSRuntimeMode.Loose;
        _handle = _module.SetupModule("attach", _ => true);
        _handle.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<TailMenu> Menu(bool endQuizDisabled = false, string xgid = Xgid)
    {
        var cut = Render<TailMenu>(p => p
            .Add(c => c.Xgid, xgid)
            .Add(c => c.SourceFile, SourceFile)
            .Add(c => c.Game, 2)
            .Add(c => c.MoveNumber, 4)
            .Add(c => c.Actions, new TailMenuAction[]
            {
                new("Show stats", EventCallback.Factory.Create(this, () => _chosen.Add("Show stats")), Disabled: false),
                new("End quiz", EventCallback.Factory.Create(this, () => _chosen.Add("End quiz")), Disabled: endQuizDisabled),
            }));
        _toggleReferenceId = Toggle(cut).GetAttribute("blazor:elementreference");
        return cut;
    }

    private static IElement Toggle(IRenderedComponent<TailMenu> cut) =>
        cut.Find(".tail-menu > button");

    private static IReadOnlyList<IElement> Items(IRenderedComponent<TailMenu> cut) =>
        cut.FindAll("[role=menu] [role=menuitem]");

    private static async Task OpenAsync(IRenderedComponent<TailMenu> cut)
    {
        await Toggle(cut).ClickAsync(new());
        Assert.Single(cut.FindAll("[role=menu]"));
    }

    /// <summary>
    /// How many times focus has been moved by <c>ElementReference.FocusAsync</c>
    /// — counted from the invocations, since bUnit's own verifier cannot
    /// assert zero.
    /// </summary>
    private int FocusMoves() =>
        JSInterop.Invocations.Count(i => i.Identifier == "Blazor._internal.domWrapper.focus");

    /// <summary>Focus has moved exactly <paramref name="moves"/> times, the last back to the toggle.</summary>
    private void AssertFocusOnTheToggle(int moves)
    {
        Assert.False(string.IsNullOrEmpty(_toggleReferenceId));
        var calls = JSInterop.VerifyFocusAsyncInvoke(calledTimes: moves);
        Assert.Equal(_toggleReferenceId, Assert.IsType<ElementReference>(calls[^1].Arguments[0]).Id);
    }

    [Fact]
    public void Closed_TheToggleIsAMenuButton_NamedMore_AndNoListRenders()
    {
        var cut = Menu();
        var toggle = Toggle(cut);

        Assert.Equal("button", toggle.GetAttribute("type"));
        Assert.Equal("menu", toggle.GetAttribute("aria-haspopup"));
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Null(toggle.GetAttribute("aria-controls"));
        Assert.Equal(TailMenu.ToggleName, toggle.GetAttribute("aria-label"));
        Assert.Equal(TailMenu.ToggleName, toggle.GetAttribute("title"));
        Assert.Equal("More", TailMenu.ToggleName);
        Assert.Empty(cut.FindAll("[role=menu]"));
    }

    [Fact]
    public async Task Open_TheListOffersEachMemberUnderItsOwnName_InTheTailsOrder_EndQuizLast()
    {
        var cut = Menu();
        await OpenAsync(cut);
        var toggle = Toggle(cut);
        var menu = cut.Find("[role=menu]");

        Assert.Equal("true", toggle.GetAttribute("aria-expanded"));
        Assert.Equal(menu.Id, toggle.GetAttribute("aria-controls"));
        Assert.Equal(TailMenu.ToggleName, menu.GetAttribute("aria-label"));

        // The items, exact: their visible text is their name, which is the
        // name of the control each stands for.
        Assert.Equal(
            ["Copy XGID to clipboard", "Show stats", "End quiz"],
            Items(cut).Select(i => i.TextContent.Trim()));
        Assert.All(Items(cut), i =>
        {
            Assert.Equal("button", i.LocalName);
            Assert.Equal("-1", i.GetAttribute("tabindex"));
            Assert.Null(i.GetAttribute("aria-label"));
        });

        // The locator is a line to read, not an item: the full wording, which
        // labels the group the copy item sits in.
        var line = cut.Find(".tail-menu-line");
        Assert.Null(line.GetAttribute("role"));
        Assert.Equal($"{SourceFile} · Game 2 · Move 4", line.TextContent.Trim());
        var group = cut.Find("[role=menu] > [role=group]");
        Assert.Equal(line.Id, group.GetAttribute("aria-labelledby"));
        Assert.Same(Items(cut)[0], group.QuerySelector("[role=menuitem]"));

        // The order on screen: copy, locator, Show stats, End quiz.
        var order = menu.QuerySelectorAll("[role=menuitem], .tail-menu-line")
            .Select(e => e.TextContent.Trim()).ToList();
        Assert.Equal(["Copy XGID to clipboard", $"{SourceFile} · Game 2 · Move 4", "Show stats", "End quiz"], order);
    }

    [Fact]
    public async Task TheCopyItemsName_IsTheBadgesOwn()
    {
        // One name for one control: the badge's copy button and the item read
        // the same constant, and the badge's button is named by it.
        var badge = Render<XgidLabel>(p => p.Add(c => c.Xgid, Xgid));
        Assert.Equal(XgidCopy.CopyLabel, badge.Find(".xgid-label-copy").GetAttribute("title"));

        var cut = Menu();
        await OpenAsync(cut);
        Assert.Equal(XgidCopy.CopyLabel, Items(cut)[0].TextContent.Trim());
    }

    [Fact]
    public async Task AnUnavailableAction_IsADisabledItem()
    {
        var cut = Menu(endQuizDisabled: true);
        await OpenAsync(cut);

        Assert.False(Items(cut)[1].HasAttribute("disabled"));
        Assert.True(Items(cut)[2].HasAttribute("disabled"));
    }

    [Fact]
    public async Task NoXgid_OffersNoCopyItem()
    {
        var cut = Menu(xgid: "");
        await OpenAsync(cut);

        Assert.Equal(["Show stats", "End quiz"], Items(cut).Select(i => i.TextContent.Trim()));
    }

    [Fact]
    public async Task Opening_FocusesTheFirstItem_ThroughTheModule_AndMovesNoOtherFocus()
    {
        var cut = Menu();
        _handle.VerifyNotInvoke("focusFirst");

        await OpenAsync(cut);

        _handle.VerifyInvoke("focusFirst", calledTimes: 1);
        Assert.Equal(0, FocusMoves());
    }

    [Fact]
    public async Task TheToggle_ClosesAnOpenList_FocusStayingOnIt()
    {
        var cut = Menu();
        await OpenAsync(cut);

        await Toggle(cut).ClickAsync(new());

        Assert.Empty(cut.FindAll("[role=menu]"));
        Assert.Equal("false", Toggle(cut).GetAttribute("aria-expanded"));
        Assert.Equal(0, FocusMoves());   // a click already left it there
    }

    [Fact]
    public async Task Escape_ClosesTheList_AndFocusGoesBackToTheToggle()
    {
        // The module's Escape: CloseList(true).
        var cut = Menu();
        await OpenAsync(cut);

        await cut.InvokeAsync(() => cut.Instance.CloseList(restoreFocus: true));

        Assert.Empty(cut.FindAll("[role=menu]"));
        AssertFocusOnTheToggle(moves: 1);
    }

    [Fact]
    public async Task AClickElsewhere_ClosesTheList_AndMovesNoFocus()
    {
        // The module's outside press (and Tab): CloseList(false). Focus stays
        // where the press put it.
        var cut = Menu();
        await OpenAsync(cut);

        await cut.InvokeAsync(() => cut.Instance.CloseList(restoreFocus: false));

        Assert.Empty(cut.FindAll("[role=menu]"));
        Assert.Equal(0, FocusMoves());
    }

    [Fact]
    public async Task CloseList_OnAClosedList_DoesNothing()
    {
        var cut = Menu();
        var renders = cut.RenderCount;

        await cut.InvokeAsync(() => cut.Instance.CloseList(restoreFocus: true));

        Assert.Equal(renders, cut.RenderCount);
        Assert.Equal(0, FocusMoves());
    }

    [Fact]
    public async Task CopyXgid_CopiesTheWholeXgid_ClosesTheList_AndFocusGoesBackToTheToggle()
    {
        var cut = Menu();
        await OpenAsync(cut);

        // The choice's handler runs on through the copy's confirmation, which
        // is XgidCopyTests' to pin; it ends once the confirmation's moment does.
        var choice = Items(cut)[0].ClickAsync(new());

        var copy = JSInterop.VerifyInvoke("navigator.clipboard.writeText");
        Assert.Equal(Xgid, copy.Arguments[0]);
        Assert.Empty(cut.FindAll("[role=menu]"));
        AssertFocusOnTheToggle(moves: 1);
        _clock.Advance(XgidCopy.ConfirmationTime);
        await choice;
    }

    [Theory]
    [InlineData(1, "Show stats")]
    [InlineData(2, "End quiz")]
    public async Task AnAction_DoesWhatItsButtonDoes_AfterHandingFocusToTheToggle(int item, string chosen)
    {
        // Focus goes back to the toggle first, then the action runs — so an
        // action that leaves the page leaves focus wherever its navigation
        // puts it. Order is read at the moment the action runs.
        var focusCallsWhenChosen = -1;
        var cut = Render<TailMenu>(p => p
            .Add(c => c.Xgid, Xgid)
            .Add(c => c.SourceFile, SourceFile)
            .Add(c => c.Game, 2)
            .Add(c => c.MoveNumber, 4)
            .Add(c => c.Actions, new TailMenuAction[]
            {
                new("Show stats", EventCallback.Factory.Create(this, () => Record("Show stats")), Disabled: false),
                new("End quiz", EventCallback.Factory.Create(this, () => Record("End quiz")), Disabled: false),
            }));
        _toggleReferenceId = Toggle(cut).GetAttribute("blazor:elementreference");
        await OpenAsync(cut);

        await Items(cut)[item].ClickAsync(new());

        Assert.Equal([chosen], _chosen);
        Assert.Equal(1, focusCallsWhenChosen);
        Assert.Empty(cut.FindAll("[role=menu]"));
        AssertFocusOnTheToggle(moves: 1);
        JSInterop.VerifyNotInvoke("navigator.clipboard.writeText");

        void Record(string name)
        {
            focusCallsWhenChosen = FocusMoves();
            _chosen.Add(name);
        }
    }

    [Fact]
    public async Task TheModule_IsAttachedToTheComponent_ClosingThroughCloseList_AndDetachedOnDispose()
    {
        var cut = Menu();
        var root = cut.Find(".tail-menu").GetAttribute("blazor:elementreference");

        var attach = _module.VerifyInvoke("attach");
        Assert.Equal(3, attach.Arguments.Count);
        Assert.Equal(root, ((ElementReference)attach.Arguments[0]!).Id);
        Assert.IsType<DotNetObjectReference<TailMenu>>(attach.Arguments[1]);
        Assert.Equal(nameof(TailMenu.CloseList), attach.Arguments[2]);
        _handle.VerifyNotInvoke("detach");

        await DisposeComponentsAsync();

        _handle.VerifyInvoke("detach");
    }
}
