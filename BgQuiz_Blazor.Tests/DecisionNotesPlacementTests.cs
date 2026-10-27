using BgQuiz_Blazor.Client.Components;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="DecisionNotes"/>' half of the notes overlay's placement
/// (<c>SPEC-quiz-view.md</c> §4, "The notes overlay's placement is a
/// remembered preference", ruled 2026-10-05; issue
/// <c>halheinrich/backgammon#344</c>): where the overlay is drawn once its
/// stage is measured, and the state transitions of moving it — a drag that
/// begins only on the title bar's handle, shows as it goes, commits on release
/// and is cancelled by a lost capture, a cancelled pointer, Esc, closing or
/// unmounting; and the Adjust control's move steps and Reset. Resizing is
/// <c>DecisionNotesResizeTests</c>'; the shared fixture, and what it plays by
/// hand, is <see cref="DecisionNotesGestureTestBase"/>'s.
/// </summary>
public class DecisionNotesPlacementTests : DecisionNotesGestureTestBase
{
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

        await ReportAsync(cut, [1280, 800, 16, 0, 0, 0, 24]);   // an overlay not laid out
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
        Plan.Verify(); // nothing written: every write is declared
    }

    // -----------------------------------------------------------------------
    //  The drag
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ADragByTheTitleBar_ShowsAsItMoves_AndWritesOnlyOnRelease()
    {
        var cut = await PlacedAsync();

        await GrabAsync(cut);
        var capture = Handle.Invocations["capture"].Single();
        Assert.Equal(Mouse, (long)capture.Arguments[0]!);
        Assert.Equal(References.TitleBar, ((Microsoft.AspNetCore.Components.ElementReference)capture.Arguments[1]!).Id);

        await MoveToAsync(cut, 400 + 84, 330 + 71);
        Assert.Equal("--notes-left: 436px; --notes-top: 371px", Drawn(cut));
        Plan.Verify(); // nothing written: every write is declared

        ExpectWritten("""{"horizontal":0.625,"vertical":0.625,"width":null,"height":null}""");
        await ReleaseAtAsync(cut, 400 + 84, 330 + 71);
        Plan.Verify();
        Assert.Equal("--notes-left: 436px; --notes-top: 371px", Drawn(cut));
    }

    [Fact]
    public async Task ThePressCommitsWhereItIsReleased_NotWhereItWasLastSeen()
    {
        ExpectWritten("""{"horizontal":0.75,"vertical":null,"width":null,"height":null}""");
        var cut = await PlacedAsync();

        await GrabAsync(cut);
        await MoveToAsync(cut, 450, 330);
        await ReleaseAtAsync(cut, 400 + 168, 330);

        Plan.Verify();
    }

    [Fact]
    public async Task ADragThatMovesNothing_WritesNothing()
    {
        var cut = await PlacedAsync();

        await GrabAsync(cut);
        await ReleaseAtAsync(cut, 400, 330);

        Plan.Verify(); // nothing written: every write is declared
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
        await OpenAdjustAsync(cut);

        var buttons = cut.FindAll(".decision-notes-header button");
        Assert.Equal(11, buttons.Count);    // Adjust, close, eight steps and Reset
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
        Handle.Setup<bool>("capture", _ => true).SetResult(false);
        var cut = await PlacedAsync();

        await GrabAsync(cut);
        await MoveToAsync(cut, 500, 400);
        await ReleaseAtAsync(cut, 500, 400);

        Assert.Equal(Centred, Drawn(cut));
        Plan.Verify(); // nothing written: every write is declared
    }

    [Fact]
    public async Task AnotherPointersEvents_AreNotTheDrags()
    {
        var cut = await PlacedAsync();
        await GrabAsync(cut);

        await MoveToAsync(cut, 500, 400, id: 7);
        Assert.Equal(Centred, Drawn(cut));
        await ReleaseAtAsync(cut, 500, 400, id: 7);
        Plan.Verify(); // nothing written: every write is declared

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
        var release = Handle.Invocations["release"].Single();
        Assert.Equal(Mouse, (long)release.Arguments[0]!);
        Assert.Equal(References.TitleBar, ((Microsoft.AspNetCore.Components.ElementReference)release.Arguments[1]!).Id);

        // The release that follows finds no drag; nothing is written.
        await ReleaseAtAsync(cut, 500, 400);
        Plan.Verify(); // nothing written: every write is declared

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
        Plan.Verify(); // nothing written: every write is declared
    }

    [Fact]
    public async Task TheReleasesOwnLossOfCapture_FindsNothingToCancel()
    {
        ExpectWritten("""{"horizontal":0.625,"vertical":null,"width":null,"height":null}"""); // the release's, and only that
        var cut = await PlacedAsync();
        await GrabAsync(cut);
        await MoveToAsync(cut, 400 + 84, 330);
        await ReleaseAtAsync(cut, 400 + 84, 330);

        await cut.Find(".decision-notes-header").TriggerEventAsync("onlostpointercapture", Pointer(400 + 84, 330));

        Assert.Equal("--notes-left: 436px; --notes-top: 300px", Drawn(cut));
        Plan.Verify();
    }

    [Fact]
    public async Task ClosingMidDrag_WritesNothing_AndTheNotesReopenWhereTheyStood()
    {
        var cut = await PlacedAsync();
        await GrabAsync(cut);
        await MoveToAsync(cut, 500, 400);

        await cut.Find("button.btn-close").ClickAsync(new());
        Assert.Empty(cut.FindAll("dialog"));
        Plan.Verify(); // nothing written: every write is declared

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
        Plan.Verify(); // nothing written: every write is declared
    }

    [Fact]
    public async Task UnmountingMidDrag_WritesNothing_AndStopsTheReports()
    {
        var cut = await PlacedAsync();
        await GrabAsync(cut);
        await MoveToAsync(cut, 500, 400);

        await DisposeComponentsAsync();

        Plan.Verify(); // nothing written: every write is declared
        Handle.VerifyInvoke("stop");
    }

    [Fact]
    public async Task ClosingTheNotes_StopsTheReports_AndReopeningStartsAFreshOne()
    {
        var cut = await PlacedAsync();

        await cut.Find("button.btn-close").ClickAsync(new());
        Handle.VerifyInvoke("stop");

        await OpenAsync(cut);
        Assert.Null(Drawn(cut));    // a fresh opening waits for its own measurement
        Assert.Equal(2, Module.Invocations["watch"].Count);
    }

    // -----------------------------------------------------------------------
    //  The Adjust control
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TheAdjustControl_ShowsEightStepsAndReset_InOrder_AndWritesNothingByItself()
    {
        var cut = await PlacedAsync();
        var adjust = AdjustButton(cut);
        Assert.Equal("Adjust", adjust.TextContent.Trim());
        Assert.Equal("Move or resize the notes a step at a time", adjust.GetAttribute("title"));
        Assert.Equal("false", adjust.GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll(".decision-notes-steps"));

        await adjust.ClickAsync(new());

        adjust = AdjustButton(cut);
        Assert.Equal("true", adjust.GetAttribute("aria-expanded"));
        var steps = cut.Find(".decision-notes-steps");
        Assert.Equal(steps.Id, adjust.GetAttribute("aria-controls"));
        Assert.Equal("group", steps.GetAttribute("role"));
        Assert.Equal("Adjust the notes", steps.GetAttribute("aria-label"));
        var buttons = steps.QuerySelectorAll("button");
        Assert.Equal(
            ["Move up", "Move down", "Move left", "Move right", "Wider", "Narrower", "Taller", "Shorter", "Reset"],
            buttons.Select(b => b.GetAttribute("aria-label") ?? b.TextContent.Trim()));
        Assert.All(buttons, b => Assert.Equal("button", b.GetAttribute("type")));

        // The arrows show a glyph, named by their label and tooltip; the size
        // steps and Reset show their names as text.
        Assert.Equal(["↑", "↓", "←", "→"], buttons.Take(4).Select(b => b.TextContent.Trim()));
        Assert.All(buttons.Take(4), b => Assert.Equal(b.GetAttribute("aria-label"), b.GetAttribute("title")));
        Assert.All(buttons.Skip(4).Take(4), b => Assert.Null(b.GetAttribute("aria-label")));
        Assert.Equal("Put the notes back in the middle at their first size", buttons.Last().GetAttribute("title"));

        Plan.Verify(); // nothing written: every write is declared
        Assert.Equal(Centred, Drawn(cut));
    }

    [Theory]
    [InlineData("Move up", """{"horizontal":null,"vertical":0.375,"width":null,"height":null}""", "--notes-left: 352px; --notes-top: 229px")]
    [InlineData("Move down", """{"horizontal":null,"vertical":0.625,"width":null,"height":null}""", "--notes-left: 352px; --notes-top: 371px")]
    [InlineData("Move left", """{"horizontal":0.375,"vertical":null,"width":null,"height":null}""", "--notes-left: 268px; --notes-top: 300px")]
    [InlineData("Move right", """{"horizontal":0.625,"vertical":null,"width":null,"height":null}""", "--notes-left: 436px; --notes-top: 300px")]
    public async Task EachStep_MovesOneStepOfItsAxis_AndWritesIt(string step, string written, string drawn)
    {
        ExpectWritten(written);
        var cut = await PlacedAsync();
        await OpenAdjustAsync(cut);

        await StepButton(cut, step).ClickAsync(new());

        Plan.Verify();
        Assert.Equal(drawn, Drawn(cut));
    }

    [Fact]
    public async Task AStepThatCannotMove_WritesNothing()
    {
        // At the edge it would move towards.
        StageStored("""{"horizontal":1,"vertical":null}""");
        var cut = await PlacedAsync();
        await OpenAdjustAsync(cut);
        await StepButton(cut, "Move right").ClickAsync(new());
        Plan.Verify(); // nothing written: every write is declared

        // Across an axis with no travel: unset stays unset.
        await ReportAsync(cut, Phone);
        await StepButton(cut, "Move left").ClickAsync(new());
        await StepButton(cut, "Move right").ClickAsync(new());
        Plan.Verify(); // nothing written: every write is declared
    }

    [Fact]
    public async Task Reset_RemovesTheEntry_AndDrawsTheOverlayCentred()
    {
        StageStored("""{"horizontal":0.25,"vertical":0.75}""");
        ExpectWritten(null); // the removal
        var cut = await PlacedAsync();
        await OpenAdjustAsync(cut);

        await StepButton(cut, "Reset").ClickAsync(new());

        Plan.Verify();
        Assert.Equal(Centred, Drawn(cut));
    }

    [Fact]
    public async Task StepsAndReset_WhileADragRuns_AreIgnored()
    {
        var cut = await PlacedAsync();
        await OpenAdjustAsync(cut);
        await GrabAsync(cut);
        await MoveToAsync(cut, 500, 400);

        await StepButton(cut, "Move up").ClickAsync(new());
        await StepButton(cut, "Reset").ClickAsync(new());

        Plan.Verify(); // nothing written: every write is declared
    }

    [Fact]
    public async Task ReopeningTheNotes_ShutsTheAdjustControl()
    {
        var cut = await PlacedAsync();
        await OpenAdjustAsync(cut);

        await cut.Find("button.btn-close").ClickAsync(new());
        await OpenAsync(cut);

        Assert.Equal("false", AdjustButton(cut).GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll(".decision-notes-steps"));
    }
}
