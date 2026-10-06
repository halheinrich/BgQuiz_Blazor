using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Where one placement shows the notes overlay as the window and the note
/// change (<c>SPEC-quiz-view.md</c> §4, "The notes overlay's placement is a
/// remembered preference", "How it is held and shown"; issue
/// <c>halheinrich/backgammon#344</c>): proportionally within the travel, for
/// any window and any note; centred on an axis with no travel, which keeps
/// what it holds; with the title bar and its close control always in view;
/// and never rewritten by a resize. Below 641 px the rule is the same rule.
/// </summary>
public sealed class NotesPlacementGeometryTests : NotesPlacementTestBase
{
    public NotesPlacementGeometryTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright)
    {
    }

    /// <summary>A phone's width, below the 641 px band edge: the overlay fills it, so there is no horizontal travel.</summary>
    private const int PhoneWidth = 375;

    /// <inheritdoc cref="PhoneWidth"/>
    private const int PhoneHeight = 812;

    [Fact]
    public async Task AtAPhoneWidth_TheNotesMoveUpAndDownOnly_AndTheHorizontalChoiceComesBackWhenWidened()
    {
        await CountPlacementWritesAsync();
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        await OpenMoveControlAsync();

        // A desktop choice off the centre: three steps right.
        for (int i = 0; i < 3; i++) await StepButton(ExpectedText.MoveRightButton).ClickAsync();
        await ExpectAtPositionAsync(0.875, 0.5, "the desktop choice");
        Assert.Equal(3, await PlacementWritesAsync());

        // Narrowed to a phone: the overlay fills the width less the clearance,
        // so it shows centred across it, whatever is held.
        await Page.SetViewportSizeAsync(PhoneWidth, PhoneHeight);
        await ExpectFillsTheWidthAsync();
        await ExpectAtPositionAsync(0.5, 0.5, "at a phone's width");

        // Left and right cannot move it, and write nothing.
        await StepButton(ExpectedText.MoveLeftButton).ClickAsync();
        await StepButton(ExpectedText.MoveRightButton).ClickAsync();
        await ExpectAtPositionAsync(0.5, 0.5, "after left and right at a phone's width");
        Assert.Equal(3, await PlacementWritesAsync());

        // Up and down move it, and are written.
        await StepButton(ExpectedText.MoveUpButton).ClickAsync();
        await ExpectAtPositionAsync(0.5, 0.375, "after a step up at a phone's width");
        Assert.Equal(4, await PlacementWritesAsync());

        // A diagonal drag moves it down only.
        var before = await OverlayBoxAsync();
        await DragTitleBarAsync(120, 60);
        await ExpectOverlayAtAsync(before.X, before.Y + 60, "after a diagonal drag at a phone's width");
        Assert.Equal(5, await PlacementWritesAsync());
        var area = await AreaAsync();
        var dropped = await OverlayBoxAsync();
        double vertical = PositionOf(dropped.Y, area.Height, dropped.Height);

        // Widened again: the horizontal choice is back, the vertical move kept.
        await Page.SetViewportSizeAsync(DesktopWidth, DesktopHeight);
        await ExpectAtPositionAsync(0.875, vertical, "widened again");
        Assert.Equal(5, await PlacementWritesAsync());
    }

    [Fact]
    public async Task AtAPhoneWidth_AStepThatCannotMove_LeavesAnUnsetPreferenceUnset()
    {
        await CountPlacementWritesAsync();
        await StartAtTheCubeReviewAsync(PhoneWidth, PhoneHeight);
        await OpenNotesAsync();
        await ExpectFillsTheWidthAsync();
        await OpenMoveControlAsync();

        await StepButton(ExpectedText.MoveLeftButton).ClickAsync();
        await StepButton(ExpectedText.MoveRightButton).ClickAsync();

        await ExpectOverlayCentredAsync("after steps that cannot move");
        Assert.Equal(0, await PlacementWritesAsync());
        Assert.Null(await StoredPlacementAsync());
    }

    [Fact]
    public async Task AVerySmallWindow_KeepsTheTitleBarAndTheCloseControlInView()
    {
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        await OpenMoveControlAsync();

        // The far corner: four steps right and four down.
        for (int i = 0; i < 4; i++)
        {
            await StepButton(ExpectedText.MoveRightButton).ClickAsync();
            await StepButton(ExpectedText.MoveDownButton).ClickAsync();
        }
        await ExpectAtPositionAsync(1, 1, "in the far corner");
        var stored = await StoredPlacementAsync();

        // Ever smaller windows: the title bar, Move's buttons and the close
        // control stay inside the window. The last is shorter than the overlay's
        // own height cap leaves room for, so only the clamp keeps it there.
        foreach (var (width, height) in new[] { (480, 240), (320, 180), (260, 100) })
        {
            await Page.SetViewportSizeAsync(width, height);
            await ExpectToPassAsync(async () =>
            {
                await AssertInsideTheWindowAsync(TitleBar, "the title bar", width, height);
                await AssertInsideTheWindowAsync(CloseNotesButton, "the close control", width, height);
            });
        }

        // The clamp changed what was shown, never what is held: grown again,
        // the window shows the overlay in the far corner.
        await Page.SetViewportSizeAsync(DesktopWidth, DesktopHeight);
        await ExpectAtPositionAsync(1, 1, "grown again");
        Assert.Equal(stored, await StoredPlacementAsync());
        await CloseNotesButton.ClickAsync();
        await Expect(NotesDialog).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ALongNoteAndAShortNote_AtOnePlacement_BothLandProportionally()
    {
        await StartAtTheCubeReviewAsync(
            stagedFileName: SyntheticXgMatch.LongNoteStagedFileName, bytes: SyntheticXgMatch.LongNoteBytes());

        // The cube's short note, moved one step right and three down.
        await OpenNotesAsync();
        await OpenMoveControlAsync();
        await StepButton(ExpectedText.MoveRightButton).ClickAsync();
        for (int i = 0; i < 3; i++) await StepButton(ExpectedText.MoveDownButton).ClickAsync();
        await ExpectAtPositionAsync(0.625, 0.875, "the short note");
        var shortNote = await OverlayBoxAsync();

        // The play's long note, at the same placement: the same position within
        // a shorter vertical travel.
        await CloseNotesButton.ClickAsync();
        await ContinueButton.ClickAsync();
        await Expect(Page.Locator(".board-container .bg-play-entry")).ToBeVisibleAsync();
        await ClickBoardPointAsync(8);
        await ClickBoardPointAsync(6);
        await Expect(SubmitButton).ToBeEnabledAsync();
        await SubmitButton.ClickAsync();
        await Expect(NotesButton).ToBeVisibleAsync();
        await OpenNotesAsync();
        await ExpectAtPositionAsync(0.625, 0.875, "the long note");

        var longNote = await OverlayBoxAsync();
        Assert.True(
            longNote.Height > shortNote.Height + 200,
            $"the long note's overlay is genuinely taller ({longNote.Height:0.#} against {shortNote.Height:0.#})");

        // Its text scrolls inside the overlay, the handle notwithstanding.
        Assert.True(await NotesText.EvaluateAsync<bool>("e => e.scrollHeight > e.clientHeight"), "the long note overflows its text area");
        var text = await LaidOutBoxAsync(NotesText, "the notes' text");
        await Page.Mouse.MoveAsync(text.X + text.Width / 2, text.Y + text.Height / 2);
        await Page.Mouse.WheelAsync(0, 200);
        await ExpectToPassAsync(async () =>
            Assert.True(await NotesText.EvaluateAsync<double>("e => e.scrollTop") > 0, "the long note scrolls"));
    }

    [Fact]
    public async Task AWindowResize_NeverRewritesThePlacement()
    {
        await CountPlacementWritesAsync();
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        await OpenMoveControlAsync();
        await StepButton(ExpectedText.MoveLeftButton).ClickAsync();
        await StepButton(ExpectedText.MoveLeftButton).ClickAsync();
        await StepButton(ExpectedText.MoveDownButton).ClickAsync();
        await ExpectAtPositionAsync(0.25, 0.625, "as chosen");
        var stored = await StoredPlacementAsync();
        Assert.Equal(3, await PlacementWritesAsync());

        // Resized while open — wider, narrower, shorter, to a phone and back —
        // the overlay is shown afresh each time, and nothing is written.
        foreach (var (width, height) in new[] { (900, 600), (1600, 1000), (700, 500) })
        {
            await Page.SetViewportSizeAsync(width, height);
            await ExpectAtPositionAsync(0.25, 0.625, $"at {width} x {height}");
        }
        await Page.SetViewportSizeAsync(PhoneWidth, PhoneHeight);
        await ExpectFillsTheWidthAsync();
        await Page.SetViewportSizeAsync(DesktopWidth, DesktopHeight);
        await ExpectAtPositionAsync(0.25, 0.625, "back at the start");

        Assert.Equal(3, await PlacementWritesAsync());
        Assert.Equal(stored, await StoredPlacementAsync());
    }

    /// <summary>
    /// The overlay fills the visible area's width less the clearance on each
    /// side — no horizontal travel — and stands centred across it.
    /// </summary>
    private async Task ExpectFillsTheWidthAsync()
    {
        await ExpectToPassAsync(async () =>
        {
            var area = await AreaAsync();
            var box = await OverlayBoxAsync();
            Assert.True(
                Math.Abs(box.Width - (area.Width - 2 * Clearance)) <= Tolerance,
                $"the overlay fills the width less the clearance: {box.Width:0.#} in {area.Width:0.#}");
            Assert.True(
                Math.Abs(box.X - Clearance) <= Tolerance,
                $"so it stands centred across it, at {Clearance}; it stands at {box.X:0.#}");
        });
    }

    /// <summary>
    /// How far outside the window an edge may stand and still count as in
    /// view: sub-pixel rounding, and less than the overlay's one-pixel border,
    /// so a title bar pushed out by its border fails.
    /// </summary>
    private const double InView = 0.5;

    private static async Task AssertInsideTheWindowAsync(ILocator element, string what, int width, int height)
    {
        var box = await LaidOutBoxAsync(element, what);
        Assert.True(
            box.X >= -InView && box.Y >= -InView
            && box.X + box.Width <= width + InView && box.Y + box.Height <= height + InView,
            $"{what} should be inside the {width} x {height} window; it is at ({box.X:0.#}, {box.Y:0.#}), {box.Width:0.#} x {box.Height:0.#}.");
    }
}
