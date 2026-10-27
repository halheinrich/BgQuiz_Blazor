using AngleSharp.Dom;
using BgQuiz_Blazor.Client.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="DecisionNotes"/>' half of the notes overlay's size
/// (<c>SPEC-quiz-view.md</c> §4, "The notes overlay's size is a remembered
/// preference", ruled 2026-10-25, amended 2026-10-27; issue
/// <c>halheinrich/backgammon#414</c>): the resize handle at the overlay's
/// bottom-right corner — a drag that never starts a move, shows as it goes,
/// commits the size and the placement in one write on release, and is
/// cancelled as a move drag is — one drag at a time; the four size steps; and
/// Reset clearing both preferences. The fixture, and what it plays by hand, is
/// <see cref="DecisionNotesGestureTestBase"/>'s.
///
/// <para>
/// The numbers are the desktop stage's (see <see cref="DecisionNotesGestureTestBase.Desktop"/>),
/// worked from the ruling: centred, the corner is at (352, 300), the room from
/// it 1280 − 16 − 352 = 912 across and 800 − 16 − 300 = 484 down. A resize to
/// a width <c>w</c> keeps the corner at 352 by the position
/// <c>336 / (1248 − w)</c>, and to a height <c>h</c> at 300 by
/// <c>284 / (768 − h)</c>. One size step is an eighth of the range from the
/// minimum to the space: (1248 − 200) / 8 = 131 across, (768 − 128) / 8 = 80
/// down.
/// </para>
/// </summary>
public class DecisionNotesResizeTests : DecisionNotesGestureTestBase
{
    /// <summary>The centred overlay's bottom-right corner, where the handle is.</summary>
    private const double CornerX = 928;

    /// <inheritdoc cref="CornerX"/>
    private const double CornerY = 500;

    private const double MinimumWidth = 200;

    private const double MinimumHeight = 56 + 3 * 24;

    private const double WidthStep = (1248 - MinimumWidth) / 8;

    private const double HeightStep = (768 - MinimumHeight) / 8;

    private static IElement ResizeHandle(IRenderedComponent<DecisionNotes> cut) => cut.Find(".decision-notes-resize");

    private static Task GrabCornerAsync(
        IRenderedComponent<DecisionNotes> cut, double x = CornerX, double y = CornerY, long id = Mouse) =>
        ResizeHandle(cut).TriggerEventAsync("onpointerdown", Pointer(x, y, id));

    private static Task MoveCornerToAsync(IRenderedComponent<DecisionNotes> cut, double x, double y, long id = Mouse) =>
        ResizeHandle(cut).TriggerEventAsync("onpointermove", Pointer(x, y, id));

    private static Task ReleaseCornerAtAsync(IRenderedComponent<DecisionNotes> cut, double x, double y, long id = Mouse) =>
        ResizeHandle(cut).TriggerEventAsync("onpointerup", Pointer(x, y, id));

    /// <summary>The centred overlay resized by (+100, +50): 676 × 250, its corner kept at (352, 300).</summary>
    private const string Grown = "--notes-left: 352px; --notes-top: 300px; --notes-width: 676px; --notes-height: 250px";

    private static readonly string GrownJson = Json(336.0 / 572, 284.0 / 518, 676, 250);

    // -----------------------------------------------------------------------
    //  The handle
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TheHandle_IsAPointerTargetOnly()
    {
        var cut = await PlacedAsync();
        var handle = ResizeHandle(cut);

        Assert.Equal("true", handle.GetAttribute("aria-hidden"));
        Assert.Null(handle.GetAttribute("tabindex"));
        Assert.Equal("DIV", handle.TagName);
        Assert.Equal("DIALOG", handle.ParentElement!.TagName);   // on the overlay, outside the title bar
    }

    [Fact]
    public async Task APressOnTheHandle_StartsAResize_CapturedOnTheHandle_AndNeverAMove()
    {
        var cut = await PlacedAsync();

        await GrabCornerAsync(cut);
        var capture = Handle.Invocations["capture"].Single();
        Assert.Equal(Mouse, (long)capture.Arguments[0]!);
        Assert.Equal(References.ResizeHandle, ((ElementReference)capture.Arguments[1]!).Id);

        // A move would put the left edge at 452; a resize keeps it at 352 and
        // grows the box.
        await MoveCornerToAsync(cut, CornerX + 100, CornerY + 50);
        Assert.Equal(Grown, Drawn(cut));
        Assert.True(Dialog(cut).HasAttribute("data-set-width"));
        Assert.True(Dialog(cut).HasAttribute("data-set-height"));
    }

    [Fact]
    public async Task AResize_ShowsAsItGoes_AndWritesOnRelease_TheSizeAndThePlacementInOneWrite()
    {
        var cut = await PlacedAsync();

        await GrabCornerAsync(cut);
        await MoveCornerToAsync(cut, CornerX + 40, CornerY + 10);
        await MoveCornerToAsync(cut, CornerX + 100, CornerY + 50);
        Assert.Equal(Grown, Drawn(cut));
        Plan.Verify(); // nothing written yet: every write is declared

        ExpectWritten(GrownJson);
        await ReleaseCornerAtAsync(cut, CornerX + 100, CornerY + 50);

        Plan.Verify();
        Assert.Equal(Grown, Drawn(cut));
    }

    [Fact]
    public async Task AResizeAlongOneAxis_WritesThatAxisOnly()
    {
        ExpectWritten(Json(336.0 / 572, null, 676));
        var cut = await PlacedAsync();

        await GrabCornerAsync(cut);
        await ReleaseCornerAtAsync(cut, CornerX + 100, CornerY);

        Plan.Verify();
        Assert.Equal("--notes-left: 352px; --notes-top: 300px; --notes-width: 676px", Drawn(cut));
        Assert.False(Dialog(cut).HasAttribute("data-set-height"));
    }

    [Fact]
    public async Task AResizeThatChangesNothing_WritesNothing()
    {
        var cut = await PlacedAsync();
        await GrabCornerAsync(cut);
        await ReleaseCornerAtAsync(cut, CornerX, CornerY);
        Plan.Verify(); // nothing written: every write is declared
        Assert.Equal(Centred, Drawn(cut));
    }

    [Fact]
    public async Task AResizeAtTheRoom_WritesNothing()
    {
        // At the bottom-right of its travel, the corner at the clearance on
        // both sides: there is no room to grow into.
        StageStored("""{"horizontal":1,"vertical":1}""");
        var cut = await PlacedAsync();
        Assert.Equal("--notes-left: 688px; --notes-top: 584px", Drawn(cut));

        await GrabCornerAsync(cut, 1264, 784);
        await MoveCornerToAsync(cut, 1364, 884);
        Assert.Equal("--notes-left: 688px; --notes-top: 584px", Drawn(cut));
        await ReleaseCornerAtAsync(cut, 1364, 884);

        Plan.Verify(); // nothing written: every write is declared
    }

    // -----------------------------------------------------------------------
    //  One drag at a time
    // -----------------------------------------------------------------------

    [Fact]
    public async Task APressOnTheTitleBar_DuringAResize_StartsNothing()
    {
        var cut = await PlacedAsync();
        await GrabCornerAsync(cut);
        await MoveCornerToAsync(cut, CornerX + 100, CornerY + 50);

        await Heading(cut).TriggerEventAsync("onpointerdown", Pointer(400, 330, id: 2));
        await MoveToAsync(cut, 600, 400, id: 2);

        Assert.Equal(1, Captures);
        Assert.Equal(Grown, Drawn(cut));
    }

    [Fact]
    public async Task APressOnTheHandle_DuringAMove_StartsNothing()
    {
        var cut = await PlacedAsync();
        await GrabAsync(cut);
        await MoveToAsync(cut, 400 + 84, 330);

        await GrabCornerAsync(cut, id: 2);
        await MoveCornerToAsync(cut, CornerX + 300, CornerY + 100, id: 2);

        Assert.Equal(1, Captures);
        Assert.Equal("--notes-left: 436px; --notes-top: 300px", Drawn(cut));
    }

    [Fact]
    public async Task StepsAndReset_DuringAResize_AreIgnored()
    {
        var cut = await PlacedAsync();
        await OpenAdjustAsync(cut);
        await GrabCornerAsync(cut);
        await MoveCornerToAsync(cut, CornerX + 100, CornerY + 50);

        await StepButton(cut, "Wider").ClickAsync(new());
        await StepButton(cut, "Move up").ClickAsync(new());
        await StepButton(cut, "Reset").ClickAsync(new());

        Plan.Verify(); // nothing written: every write is declared
        Assert.Equal(Grown, Drawn(cut));
    }

    // -----------------------------------------------------------------------
    //  Cancelling
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EscapeDuringAResize_CancelsItOnly_AndReleasesTheHandlesCapture()
    {
        var cut = await PlacedAsync();
        await GrabCornerAsync(cut);
        await MoveCornerToAsync(cut, CornerX + 100, CornerY + 50);

        await Dialog(cut).KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(Centred, Drawn(cut));
        Assert.False(Dialog(cut).HasAttribute("data-set-width"));
        Assert.Single(cut.FindAll("dialog"));
        var release = Handle.Invocations["release"].Single();
        Assert.Equal(Mouse, (long)release.Arguments[0]!);
        Assert.Equal(References.ResizeHandle, ((ElementReference)release.Arguments[1]!).Id);

        await ReleaseCornerAtAsync(cut, CornerX + 100, CornerY + 50);
        Plan.Verify(); // nothing written: every write is declared
    }

    [Theory]
    [InlineData("onlostpointercapture")]
    [InlineData("onpointercancel")]
    public async Task ALostCapture_OrACancelledPointer_CancelsTheResize(string cancellation)
    {
        StageStored(Json(0.25, 0.75, 400, 300));
        var cut = await PlacedAsync();
        const string Stored = "--notes-left: 228px; --notes-top: 367px; --notes-width: 400px; --notes-height: 300px";
        Assert.Equal(Stored, Drawn(cut));

        await GrabCornerAsync(cut, 628, 667);
        await MoveCornerToAsync(cut, 700, 700);
        Assert.NotEqual(Stored, Drawn(cut));

        await ResizeHandle(cut).TriggerEventAsync(cancellation, Pointer(700, 700));

        Assert.Equal(Stored, Drawn(cut));
        Assert.Single(cut.FindAll("dialog"));
        await ReleaseCornerAtAsync(cut, 700, 700);
        Plan.Verify(); // nothing written: every write is declared
    }

    [Fact]
    public async Task ClosingMidResize_WritesNothing_AndTheNotesReopenAsTheyWere()
    {
        var cut = await PlacedAsync();
        await GrabCornerAsync(cut);
        await MoveCornerToAsync(cut, CornerX + 100, CornerY + 50);

        await cut.Find("button.btn-close").ClickAsync(new());
        Assert.Empty(cut.FindAll("dialog"));
        Plan.Verify(); // nothing written: every write is declared

        await OpenAsync(cut);
        await ReportAsync(cut, Desktop);
        Assert.Equal(Centred, Drawn(cut));
    }

    [Fact]
    public async Task ANewCommentMidResize_ClosesTheNotes_AndWritesNothing()
    {
        var cut = await PlacedAsync();
        await GrabCornerAsync(cut);
        await MoveCornerToAsync(cut, CornerX + 100, CornerY + 50);

        cut.Render(p => p.Add(c => c.Comment, "Another decision's note."));

        Assert.Empty(cut.FindAll("dialog"));
        Plan.Verify(); // nothing written: every write is declared
    }

    [Fact]
    public async Task UnmountingMidResize_WritesNothing()
    {
        var cut = await PlacedAsync();
        await GrabCornerAsync(cut);
        await MoveCornerToAsync(cut, CornerX + 100, CornerY + 50);

        await DisposeComponentsAsync();

        Plan.Verify(); // nothing written: every write is declared
        Handle.VerifyInvoke("stop");
    }

    // -----------------------------------------------------------------------
    //  The size steps and Reset
    // -----------------------------------------------------------------------

    public static TheoryData<string, string, string> SizeSteps => new()
    {
        { "Wider", Json(336 / (1248 - (576 + WidthStep)), null, 576 + WidthStep),
            "--notes-left: 352px; --notes-top: 300px; --notes-width: 707px" },
        { "Narrower", Json(336 / (1248 - (576 - WidthStep)), null, 576 - WidthStep),
            "--notes-left: 352px; --notes-top: 300px; --notes-width: 445px" },
        { "Taller", Json(null, 284 / (768 - (200 + HeightStep)), null, 200 + HeightStep),
            "--notes-left: 352px; --notes-top: 300px; --notes-height: 280px" },
        // 200 less a step of 80 is under the minimum, which it stops at.
        { "Shorter", Json(null, 284 / (768 - MinimumHeight), null, MinimumHeight),
            "--notes-left: 352px; --notes-top: 300px; --notes-height: 128px" },
    };

    [Theory]
    [MemberData(nameof(SizeSteps))]
    public async Task EachSizeStep_ResizesOneStepFromTheTopLeftCorner_AndWritesIt(string step, string written, string drawn)
    {
        ExpectWritten(written);
        var cut = await PlacedAsync();
        await OpenAdjustAsync(cut);

        await StepButton(cut, step).ClickAsync(new());

        Plan.Verify();
        Assert.Equal(drawn, Drawn(cut));
    }

    [Fact]
    public async Task ASizeStepAtABound_WritesNothing()
    {
        // At the bottom-right of the travel there is no room to grow.
        StageStored("""{"horizontal":1,"vertical":1}""");
        var cut = await PlacedAsync();
        await OpenAdjustAsync(cut);
        await StepButton(cut, "Wider").ClickAsync(new());
        await StepButton(cut, "Taller").ClickAsync(new());
        Plan.Verify(); // nothing written: every write is declared
    }

    [Fact]
    public async Task ASizeStepAtTheMinimum_WritesNothing()
    {
        StageStored(Json(0.5, 0.5, MinimumWidth, MinimumHeight));
        var cut = await PlacedAsync();
        await OpenAdjustAsync(cut);
        await StepButton(cut, "Narrower").ClickAsync(new());
        await StepButton(cut, "Shorter").ClickAsync(new());
        Plan.Verify(); // nothing written: every write is declared
    }

    [Fact]
    public async Task Reset_RemovesTheEntry_AndDrawsTheOverlayCentredAtItsDefaultSize()
    {
        StageStored(Json(0.25, 0.75, 400, 300));
        ExpectWritten(null); // the removal: both preferences at once
        var cut = await PlacedAsync();
        await OpenAdjustAsync(cut);

        await StepButton(cut, "Reset").ClickAsync(new());

        Plan.Verify();
        Assert.Equal(Centred, Drawn(cut));
        Assert.False(Dialog(cut).HasAttribute("data-set-width"));
        Assert.False(Dialog(cut).HasAttribute("data-set-height"));
    }
}
