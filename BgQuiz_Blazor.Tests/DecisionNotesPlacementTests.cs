using AngleSharp.Dom;
using BgQuiz_Blazor.Client.Components;
using BgQuiz_Blazor.Client.Quiz;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="DecisionNotes"/>' half of the notes overlay's placement
/// (<c>SPEC-quiz-view.md</c> §4, "The notes overlay's placement is a
/// remembered preference", ruled 2026-10-05; issue
/// <c>halheinrich/backgammon#344</c>): where the overlay is drawn once its
/// stage is measured, and the state transitions of moving it — a drag that
/// begins only on the title bar's handle, shows as it goes, commits on release
/// and is cancelled by a lost capture, a cancelled pointer, Esc, closing or
/// unmounting; and the Move control's steps and Reset.
///
/// <para>
/// <b>The browser is played by hand here.</b> The module's stage report is
/// <see cref="DecisionNotes.OnStageMeasured"/> called directly, and pointer
/// capture is a planned call. What a real pointer, real keys and real storage
/// do with all of it is the e2e suite's (<c>NotesPlacement*Tests</c>); the
/// arithmetic behind every number below is <c>NotesStageTests</c>'.
/// </para>
/// </summary>
public class DecisionNotesPlacementTests : BunitContext
{
    private const string Note = "Hit loose here; the gammons are worth it.";

    /// <summary>
    /// The stage reported in these tests: a 1280 × 800 area, the 16 px
    /// clearance, a 576 × 200 overlay whose title bar ends 56 px down it. Its
    /// travel is 672 across and 568 down, so the overlay is drawn centred at
    /// (352, 300), and an eighth of the travel is 84 across and 71 down.
    /// </summary>
    private static readonly double[] Desktop = [1280, 800, 16, 576, 200, 56];

    /// <summary>A phone: the overlay fills the width less the clearance, so there is no horizontal travel.</summary>
    private static readonly double[] Phone = [375, 812, 16, 343, 220, 56];

    private const string Centred = "--notes-left: 352px; --notes-top: 300px";

    private const long Mouse = 1;

    private readonly BunitJSModuleInterop _module;

    private readonly BunitJSModuleInterop _handle;

    public DecisionNotesPlacementTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _module = JSInterop.SetupModule(DecisionNotes.ModulePath);
        _module.Mode = JSRuntimeMode.Loose;
        _handle = _module.SetupModule("watch", _ => true);
        _handle.Mode = JSRuntimeMode.Loose;
        _handle.Setup<bool>("capture", _ => true).SetResult(true);
        Services.AddScoped<NotesPlacementStore>();
        Services.AddScoped<BrowserStorageCondition>();
    }

    private void StageStored(string json) =>
        JSInterop.Setup<string?>("localStorage.getItem", NotesPlacementStore.StorageKey).SetResult(json);

    private IRenderedComponent<DecisionNotes> Notes() => Render<DecisionNotes>(p => p.Add(c => c.Comment, Note));

    private static async Task OpenAsync(IRenderedComponent<DecisionNotes> cut) =>
        await cut.Find("button.decision-notes-toggle").ClickAsync(new());

    private static Task ReportAsync(IRenderedComponent<DecisionNotes> cut, double[] stage) =>
        cut.InvokeAsync(() => cut.Instance.OnStageMeasured(stage[0], stage[1], stage[2], stage[3], stage[4], stage[5]));

    /// <summary>Open the notes and report the desktop stage: the overlay is placed.</summary>
    private async Task<IRenderedComponent<DecisionNotes>> PlacedAsync(double[]? stage = null)
    {
        var cut = Notes();
        await OpenAsync(cut);
        await ReportAsync(cut, stage ?? Desktop);
        return cut;
    }

    private static IElement Dialog(IRenderedComponent<DecisionNotes> cut) => cut.Find("dialog");

    private static string? Drawn(IRenderedComponent<DecisionNotes> cut) => Dialog(cut).GetAttribute("style");

    private static IElement Heading(IRenderedComponent<DecisionNotes> cut) => cut.Find(".decision-notes-header h2");

    private static PointerEventArgs Pointer(double x, double y, long id = Mouse, long button = 0, bool primary = true) =>
        new() { PointerId = id, ClientX = x, ClientY = y, Button = button, IsPrimary = primary, PointerType = "mouse" };

    /// <summary>Press on the title bar's heading — a spot on the handle that is no button — at (400, 330).</summary>
    private static Task GrabAsync(IRenderedComponent<DecisionNotes> cut, long id = Mouse) =>
        Heading(cut).TriggerEventAsync("onpointerdown", Pointer(400, 330, id));

    private static Task MoveToAsync(IRenderedComponent<DecisionNotes> cut, double x, double y, long id = Mouse) =>
        cut.Find(".decision-notes-header").TriggerEventAsync("onpointermove", Pointer(x, y, id));

    private static Task ReleaseAtAsync(IRenderedComponent<DecisionNotes> cut, double x, double y, long id = Mouse) =>
        cut.Find(".decision-notes-header").TriggerEventAsync("onpointerup", Pointer(x, y, id));

    private int Captures => _handle.Invocations["capture"].Count;

    /// <summary>Every write to the placement's key — a set or a removal.</summary>
    private IReadOnlyList<JSRuntimeInvocation> Writes() =>
        [.. JSInterop.Invocations.Where(i =>
            i.Identifier is "localStorage.setItem" or "localStorage.removeItem"
            && (string?)i.Arguments[0] == NotesPlacementStore.StorageKey)];

    private static IElement StepButton(IRenderedComponent<DecisionNotes> cut, string name) =>
        cut.FindAll(".decision-notes-steps button").Single(b =>
            (b.GetAttribute("aria-label") ?? b.TextContent.Trim()) == name);

    private static async Task OpenMoveAsync(IRenderedComponent<DecisionNotes> cut) =>
        await cut.Find(".decision-notes-tools button:not(.btn-close)").ClickAsync(new());

    // -----------------------------------------------------------------------
    //  Where the overlay is drawn
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UntilTheStageIsMeasured_TheOverlayIsLeftToTheStylesheet()
    {
        var cut = Notes();
        await OpenAsync(cut);

        // The stylesheet centres it — where an unset placement is shown anyway.
        Assert.False(Dialog(cut).HasAttribute("data-placed"));
        Assert.Null(Drawn(cut));
    }

    [Fact]
    public async Task AnUnsetPreference_IsDrawnCentred_OnTheMeasuredStage()
    {
        var cut = await PlacedAsync();

        Assert.True(Dialog(cut).HasAttribute("data-placed"));
        Assert.Equal(Centred, Drawn(cut));
    }

    [Fact]
    public async Task AStoredPreference_IsDrawnWhereItLands()
    {
        StageStored("""{"horizontal":0.25,"vertical":0.75}""");

        var cut = await PlacedAsync();

        // 16 + ¼ × 672 across, 16 + ¾ × 568 down.
        Assert.Equal("--notes-left: 184px; --notes-top: 442px", Drawn(cut));
    }

    [Fact]
    public async Task AReport_OfNoStage_OrWhileClosed_ChangesNothing()
    {
        var cut = Notes();
        await ReportAsync(cut, Desktop);          // closed: there is no overlay to place
        await OpenAsync(cut);
        Assert.Null(Drawn(cut));

        await ReportAsync(cut, [1280, 800, 16, 0, 0, 0]);   // an overlay not laid out
        Assert.Null(Drawn(cut));
    }

    [Fact]
    public async Task ANewReport_RedrawsTheSamePreferenceOnTheNewStage()
    {
        StageStored("""{"horizontal":1,"vertical":0}""");
        var cut = await PlacedAsync();
        Assert.Equal("--notes-left: 688px; --notes-top: 16px", Drawn(cut));

        await ReportAsync(cut, Phone);

        // No horizontal travel at a phone's width: centred across it.
        Assert.Equal("--notes-left: 16px; --notes-top: 16px", Drawn(cut));
        Assert.Empty(Writes());
    }

    // -----------------------------------------------------------------------
    //  The drag
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ADragByTheTitleBar_ShowsAsItMoves_AndWritesOnlyOnRelease()
    {
        var cut = await PlacedAsync();

        await GrabAsync(cut);
        Assert.Equal(Mouse, (long)_handle.Invocations["capture"].Single().Arguments[0]!);

        await MoveToAsync(cut, 400 + 84, 330 + 71);
        Assert.Equal("--notes-left: 436px; --notes-top: 371px", Drawn(cut));
        Assert.Empty(Writes());

        await ReleaseAtAsync(cut, 400 + 84, 330 + 71);
        var write = Assert.Single(Writes());
        Assert.Equal("""{"horizontal":0.625,"vertical":0.625}""", write.Arguments[1]);
        Assert.Equal("--notes-left: 436px; --notes-top: 371px", Drawn(cut));
    }

    [Fact]
    public async Task ThePressCommitsWhereItIsReleased_NotWhereItWasLastSeen()
    {
        var cut = await PlacedAsync();

        await GrabAsync(cut);
        await MoveToAsync(cut, 450, 330);
        await ReleaseAtAsync(cut, 400 + 168, 330);

        Assert.Equal("""{"horizontal":0.75,"vertical":null}""", Assert.Single(Writes()).Arguments[1]);
    }

    [Fact]
    public async Task ADragThatMovesNothing_WritesNothing()
    {
        var cut = await PlacedAsync();

        await GrabAsync(cut);
        await ReleaseAtAsync(cut, 400, 330);

        Assert.Empty(Writes());
        Assert.Equal(Centred, Drawn(cut));
    }

    [Fact]
    public async Task APressOnTheTitleBarsOwnButtons_NeverReachesTheHandle()
    {
        // bUnit dispatches an event the way Blazor does — up the ancestors to
        // the first handler, stopping where propagation is stopped — and
        // refuses one that reaches no handler at all. So "a press on this
        // button starts no drag" is stated as what it is: from every button in
        // the title bar, a press reaches no handler, though the title bar
        // around them has one. Were a button's group to stop stopping it, the
        // press would reach the handle and start a drag here.
        var cut = await PlacedAsync();
        await OpenMoveAsync(cut);

        var buttons = cut.FindAll(".decision-notes-header button");
        Assert.Equal(7, buttons.Count);    // Move, close, four steps and Reset
        foreach (var button in buttons)
            await Assert.ThrowsAsync<MissingEventHandlerException>(
                () => button.TriggerEventAsync("onpointerdown", Pointer(400, 330)));
        Assert.Equal(0, Captures);

        // The handle around them is live: a press on its heading, which has no
        // handler of its own, reaches it and starts a drag.
        await GrabAsync(cut);
        Assert.Equal(1, Captures);
    }

    [Theory]
    [InlineData(2, true)]     // the secondary button
    [InlineData(0, false)]    // a second finger
    public async Task OnlyThePrimaryButtonOfThePrimaryPointer_StartsADrag(long button, bool primary)
    {
        var cut = await PlacedAsync();

        await Heading(cut).TriggerEventAsync("onpointerdown", Pointer(400, 330, button: button, primary: primary));
        await MoveToAsync(cut, 500, 400);

        Assert.Equal(0, Captures);
        Assert.Equal(Centred, Drawn(cut));
    }

    [Fact]
    public async Task BeforeTheStageIsMeasured_APressStartsNoDrag()
    {
        var cut = Notes();
        await OpenAsync(cut);

        await GrabAsync(cut);

        Assert.Equal(0, Captures);
    }

    [Fact]
    public async Task ACaptureTheBrowserRefuses_EndsTheDragAtOnce()
    {
        _handle.Setup<bool>("capture", _ => true).SetResult(false);
        var cut = await PlacedAsync();

        await GrabAsync(cut);
        await MoveToAsync(cut, 500, 400);
        await ReleaseAtAsync(cut, 500, 400);

        Assert.Equal(Centred, Drawn(cut));
        Assert.Empty(Writes());
    }

    [Fact]
    public async Task AnotherPointersEvents_AreNotTheDrags()
    {
        var cut = await PlacedAsync();
        await GrabAsync(cut);

        await MoveToAsync(cut, 500, 400, id: 7);
        Assert.Equal(Centred, Drawn(cut));
        await ReleaseAtAsync(cut, 500, 400, id: 7);
        Assert.Empty(Writes());

        // The dragging pointer still drags.
        await MoveToAsync(cut, 400 + 84, 330);
        Assert.Equal("--notes-left: 436px; --notes-top: 300px", Drawn(cut));
    }

    [Fact]
    public async Task EscapeDuringADrag_CancelsItAndReleasesTheCapture_ThenEscapeCloses()
    {
        var cut = await PlacedAsync();
        await GrabAsync(cut);
        await MoveToAsync(cut, 500, 400);

        await Dialog(cut).KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(Centred, Drawn(cut));
        Assert.Single(cut.FindAll("dialog"));
        Assert.Equal(Mouse, (long)_handle.Invocations["release"].Single().Arguments[0]!);

        // The release that follows finds no drag; nothing is written.
        await ReleaseAtAsync(cut, 500, 400);
        Assert.Empty(Writes());

        await Dialog(cut).KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll("dialog"));
    }

    [Theory]
    [InlineData("onlostpointercapture")]
    [InlineData("onpointercancel")]
    public async Task ALostCapture_OrACancelledPointer_CancelsTheDrag(string cancellation)
    {
        var cut = await PlacedAsync();
        await GrabAsync(cut);
        await MoveToAsync(cut, 500, 400);

        await cut.Find(".decision-notes-header").TriggerEventAsync(cancellation, Pointer(500, 400));

        Assert.Equal(Centred, Drawn(cut));
        Assert.Single(cut.FindAll("dialog"));
        await ReleaseAtAsync(cut, 500, 400);
        Assert.Empty(Writes());
    }

    [Fact]
    public async Task TheReleasesOwnLossOfCapture_FindsNothingToCancel()
    {
        var cut = await PlacedAsync();
        await GrabAsync(cut);
        await MoveToAsync(cut, 400 + 84, 330);
        await ReleaseAtAsync(cut, 400 + 84, 330);

        await cut.Find(".decision-notes-header").TriggerEventAsync("onlostpointercapture", Pointer(400 + 84, 330));

        Assert.Equal("--notes-left: 436px; --notes-top: 300px", Drawn(cut));
        Assert.Single(Writes());
    }

    [Fact]
    public async Task ClosingMidDrag_WritesNothing_AndTheNotesReopenWhereTheyStood()
    {
        var cut = await PlacedAsync();
        await GrabAsync(cut);
        await MoveToAsync(cut, 500, 400);

        await cut.Find("button.btn-close").ClickAsync(new());
        Assert.Empty(cut.FindAll("dialog"));
        Assert.Empty(Writes());

        await OpenAsync(cut);
        await ReportAsync(cut, Desktop);
        Assert.Equal(Centred, Drawn(cut));
    }

    [Fact]
    public async Task ANewCommentMidDrag_ClosesTheNotes_AndWritesNothing()
    {
        var cut = await PlacedAsync();
        await GrabAsync(cut);
        await MoveToAsync(cut, 500, 400);

        cut.Render(p => p.Add(c => c.Comment, "Another decision's note."));

        Assert.Empty(cut.FindAll("dialog"));
        Assert.Empty(Writes());
    }

    [Fact]
    public async Task UnmountingMidDrag_WritesNothing_AndStopsTheReports()
    {
        var cut = await PlacedAsync();
        await GrabAsync(cut);
        await MoveToAsync(cut, 500, 400);

        await DisposeComponentsAsync();

        Assert.Empty(Writes());
        _handle.VerifyInvoke("stop");
    }

    [Fact]
    public async Task ClosingTheNotes_StopsTheReports_AndReopeningStartsAFreshOne()
    {
        var cut = await PlacedAsync();

        await cut.Find("button.btn-close").ClickAsync(new());
        _handle.VerifyInvoke("stop");

        await OpenAsync(cut);
        Assert.Null(Drawn(cut));    // a fresh opening waits for its own measurement
        Assert.Equal(2, _module.Invocations["watch"].Count);
    }

    // -----------------------------------------------------------------------
    //  The Move control
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TheMoveControl_ShowsFourStepsAndReset_AndWritesNothingByItself()
    {
        var cut = await PlacedAsync();
        var move = cut.Find(".decision-notes-tools button:not(.btn-close)");
        Assert.Equal("Move", move.TextContent.Trim());
        Assert.Equal("false", move.GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll(".decision-notes-steps"));

        await move.ClickAsync(new());

        move = cut.Find(".decision-notes-tools button:not(.btn-close)");
        Assert.Equal("true", move.GetAttribute("aria-expanded"));
        var steps = cut.Find(".decision-notes-steps");
        Assert.Equal(steps.Id, move.GetAttribute("aria-controls"));
        Assert.Equal("group", steps.GetAttribute("role"));
        Assert.Equal(
            ["Move up", "Move down", "Move left", "Move right", "Reset"],
            steps.QuerySelectorAll("button").Select(b => b.GetAttribute("aria-label") ?? b.TextContent.Trim()));
        Assert.All(steps.QuerySelectorAll("button"), b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.Empty(Writes());
        Assert.Equal(Centred, Drawn(cut));
    }

    [Theory]
    [InlineData("Move up", """{"horizontal":null,"vertical":0.375}""", "--notes-left: 352px; --notes-top: 229px")]
    [InlineData("Move down", """{"horizontal":null,"vertical":0.625}""", "--notes-left: 352px; --notes-top: 371px")]
    [InlineData("Move left", """{"horizontal":0.375,"vertical":null}""", "--notes-left: 268px; --notes-top: 300px")]
    [InlineData("Move right", """{"horizontal":0.625,"vertical":null}""", "--notes-left: 436px; --notes-top: 300px")]
    public async Task EachStep_MovesOneStepOfItsAxis_AndWritesIt(string step, string written, string drawn)
    {
        var cut = await PlacedAsync();
        await OpenMoveAsync(cut);

        await StepButton(cut, step).ClickAsync(new());

        Assert.Equal(written, Assert.Single(Writes()).Arguments[1]);
        Assert.Equal(drawn, Drawn(cut));
    }

    [Fact]
    public async Task AStepThatCannotMove_WritesNothing()
    {
        // At the edge it would move towards.
        StageStored("""{"horizontal":1,"vertical":null}""");
        var cut = await PlacedAsync();
        await OpenMoveAsync(cut);
        await StepButton(cut, "Move right").ClickAsync(new());
        Assert.Empty(Writes());

        // Across an axis with no travel: unset stays unset.
        await ReportAsync(cut, Phone);
        await StepButton(cut, "Move left").ClickAsync(new());
        await StepButton(cut, "Move right").ClickAsync(new());
        Assert.Empty(Writes());
    }

    [Fact]
    public async Task Reset_RemovesTheEntry_AndDrawsTheOverlayCentred()
    {
        StageStored("""{"horizontal":0.25,"vertical":0.75}""");
        var cut = await PlacedAsync();
        await OpenMoveAsync(cut);

        await StepButton(cut, "Reset").ClickAsync(new());

        Assert.Equal("localStorage.removeItem", Assert.Single(Writes()).Identifier);
        Assert.Equal(Centred, Drawn(cut));
    }

    [Fact]
    public async Task StepsAndReset_WhileADragRuns_AreIgnored()
    {
        var cut = await PlacedAsync();
        await OpenMoveAsync(cut);
        await GrabAsync(cut);
        await MoveToAsync(cut, 500, 400);

        await StepButton(cut, "Move up").ClickAsync(new());
        await StepButton(cut, "Reset").ClickAsync(new());

        Assert.Empty(Writes());
    }

    [Fact]
    public async Task ReopeningTheNotes_ShutsTheMoveControl()
    {
        var cut = await PlacedAsync();
        await OpenMoveAsync(cut);

        await cut.Find("button.btn-close").ClickAsync(new());
        await OpenAsync(cut);

        Assert.Equal("false", cut.Find(".decision-notes-tools button:not(.btn-close)").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll(".decision-notes-steps"));
    }
}
