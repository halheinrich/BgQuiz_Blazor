using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// How one remembered size shows as the window and the note change
/// (<c>SPEC-quiz-view.md</c> §4, "The notes overlay's size is a remembered
/// preference", "Bounds and the clamp"; issue <c>halheinrich/backgammon#414</c>):
/// a chosen height is the overlay's height exactly, for a short note and a
/// long one; a window smaller than the chosen size shows it clamped, with the
/// title bar and the close control in view, down to a window smaller than the
/// minimum; the clamp never writes, and a window that grows again shows the
/// held size; and a phone's width resizes within the same bounds as a
/// desktop's.
/// </summary>
public sealed class NotesSizeGeometryTests : NotesPlacementTestBase
{
    public NotesSizeGeometryTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright, output)
    {
    }

    private const int PhoneWidth = 375;

    private const int PhoneHeight = 812;

    [Fact]
    public async Task AShortNote_AtALargerChosenHeight_ShowsThatHeight_WithSpaceBelowItsText()
    {
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        await OpenAdjustControlAsync();
        await ExpectOverlayCentredAsync("the Adjust control open");
        var start = await OverlayBoxAsync();
        var area = await AreaAsync();
        double down = SizeStep(area.Height, await MinimumHeightAsync());

        for (int i = 0; i < 3; i++) await TallerButton.ClickAsync();
        await ExpectBoxAsync(start.X, start.Y, start.Width, start.Height + 3 * down, "three steps taller");

        // The text ends well above the overlay's bottom: the chosen height is
        // the height, not a cap the note's own length decides under.
        double textBottom = await Page.EvaluateAsync<double>("""
            () => {
              const range = document.createRange();
              range.selectNodeContents(document.querySelector('.decision-notes-text'));
              return Math.max(...[...range.getClientRects()].map(r => r.bottom));
            }
            """);
        var box = await OverlayBoxAsync();
        Assert.True(
            box.Y + box.Height - textBottom > 2 * down,
            $"the short note's text should end well above the overlay's bottom ({box.Y + box.Height:0.#}); it ends at {textBottom:0.#}.");
        Assert.False(await NotesText.EvaluateAsync<bool>("e => e.scrollHeight > e.clientHeight"), "a short note does not scroll");
    }

    [Fact]
    public async Task ALongNote_AtASmallerChosenHeight_ScrollsInside()
    {
        await StartAtTheCubeReviewAsync(
            stagedFileName: SyntheticXgMatch.LongNoteStagedFileName, bytes: SyntheticXgMatch.LongNoteBytes());

        // On to the play, whose note is long.
        await ContinueButton.ClickAsync();
        await Expect(Page.Locator(".board-container .bg-play-entry")).ToBeVisibleAsync();
        await ClickBoardPointAsync(8);
        await ClickBoardPointAsync(6);
        await Expect(SubmitButton).ToBeEnabledAsync();
        await SubmitButton.ClickAsync();
        await Expect(NotesButton).ToBeVisibleAsync();
        await OpenNotesAsync();
        await OpenAdjustControlAsync();
        await ExpectOverlayCentredAsync("the long note, the Adjust control open");
        var start = await OverlayBoxAsync();
        var area = await AreaAsync();
        double down = SizeStep(area.Height, await MinimumHeightAsync());

        // Its default height is the cap, 32rem.
        AssertNear(512, start.Height, "the long note's default height");

        await ShorterButton.ClickAsync();
        await ShorterButton.ClickAsync();
        await ExpectBoxAsync(start.X, start.Y, start.Width, start.Height - 2 * down, "two steps shorter");

        Assert.True(await NotesText.EvaluateAsync<bool>("e => e.scrollHeight > e.clientHeight"), "the long note overflows its text area");
        var text = await LaidOutBoxAsync(NotesText, "the notes' text");
        var box = await OverlayBoxAsync();
        Assert.True(text.Y + text.Height <= box.Y + box.Height + Tolerance, "the text area ends inside the overlay");
        await Page.Mouse.MoveAsync(text.X + text.Width / 2, text.Y + text.Height / 2);
        await Page.Mouse.WheelAsync(0, 200);
        await ExpectToPassAsync(async () =>
            Assert.True(await NotesText.EvaluateAsync<double>("e => e.scrollTop") > 0, "the long note scrolls"));
    }

    [Fact]
    public async Task NarrowerOnANoteThatRewraps_KeepsTheLeftAndTopEdges_AndHoldsTheHeight()
    {
        // §4, amended 2026-10-27: the first resize chooses the box, so a width
        // step holds a height that is still fit to the note.
        await CountPlacementWritesAsync();
        await StartAtTheCubeReviewAsync(
            stagedFileName: SyntheticXgMatch.MediumNoteStagedFileName, bytes: SyntheticXgMatch.MediumNoteBytes());
        await ContinueButton.ClickAsync();
        await Expect(Page.Locator(".board-container .bg-play-entry")).ToBeVisibleAsync();
        await ClickBoardPointAsync(8);
        await ClickBoardPointAsync(6);
        await Expect(SubmitButton).ToBeEnabledAsync();
        await SubmitButton.ClickAsync();
        await Expect(NotesButton).ToBeVisibleAsync();
        await OpenNotesAsync();
        await OpenAdjustControlAsync();
        await ExpectOverlayCentredAsync("the medium note, the Adjust control open");
        var start = await OverlayBoxAsync();
        var area = await AreaAsync();
        double across = SizeStep(area.Width, MinimumWidth);

        // The fixture first: its height is fit to the note under the cap, and
        // a step narrower the same text is taller — measured on a hidden copy
        // of the text, so the overlay itself is untouched.
        Assert.True(start.Height < 512 - Tolerance, $"the medium note fits under the cap: {start.Height:0.#}");
        Assert.False(await NotesText.EvaluateAsync<bool>("e => e.scrollHeight > e.clientHeight"), "the medium note does not scroll at its default size");
        var heights = await Page.EvaluateAsync<double[]>("""
            widths => widths.map(w => {
              const copy = document.querySelector('.decision-notes-text').cloneNode(true);
              copy.style.cssText = `position: fixed; visibility: hidden; left: 0; top: 0; width: ${w}px; height: auto; flex: none`;
              document.body.appendChild(copy);
              const h = copy.getBoundingClientRect().height;
              copy.remove();
              return h;
            })
            """, new[] { (double)start.Width - 2, (double)start.Width - across - 2 });
        Assert.True(heights[1] > heights[0] + 1, $"a step narrower the note rewraps taller: {heights[0]:0.#} → {heights[1]:0.#}");
        Assert.Equal(0, await PlacementWritesAsync());

        await NarrowerButton.ClickAsync();

        // The left and top edges stay, the width is a step less, the height
        // stays what it was; the rewrapped text scrolls inside.
        await ExpectBoxAsync(start.X, start.Y, start.Width - across, start.Height, "a step narrower");
        Assert.Equal(1, await PlacementWritesAsync());
        var stored = await StoredPreferencesAsync();
        Assert.NotNull(stored);
        AssertNear(start.Width - across, stored!.Width!.Value, "the stored width");
        AssertNear(start.Height, stored.Height!.Value, "the stored height");
        AssertNear(start.Y, ExpectedOffset(stored.Vertical!.Value, area.Height, stored.Height.Value), "where the stored placement puts the top");
        Assert.True(await NotesText.EvaluateAsync<bool>("e => e.scrollHeight > e.clientHeight"), "the rewrapped note overflows its held height");
        var text = await LaidOutBoxAsync(NotesText, "the notes' text");
        await Page.Mouse.MoveAsync(text.X + text.Width / 2, text.Y + text.Height / 2);
        await Page.Mouse.WheelAsync(0, 200);
        await ExpectToPassAsync(async () =>
            Assert.True(await NotesText.EvaluateAsync<double>("e => e.scrollTop") > 0, "the rewrapped note scrolls"));
    }

    [Fact]
    public async Task AWindowSmallerThanTheChosenSize_ClampsWhatIsShown_KeepsTheTitleBarInView_AndNeverWrites()
    {
        await CountPlacementWritesAsync();
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        await OpenAdjustControlAsync();

        // A chosen size: dragged out to 900 x 560 from the top-left of the travel.
        for (int i = 0; i < 4; i++)
        {
            await StepButton(ExpectedText.MoveLeftButton).ClickAsync();
            await StepButton(ExpectedText.MoveUpButton).ClickAsync();
        }
        await ExpectAtPositionAsync(0, 0, "at the top-left of the travel");
        var corner = await OverlayBoxAsync();
        await DragCornerAsync(900 - corner.Width, 560 - corner.Height);
        await ExpectBoxAsync(Clearance, Clearance, 900, 560, "the chosen size");
        var stored = await StoredPlacementAsync();
        int writes = await PlacementWritesAsync();

        // The Adjust group shut, as a reader leaves it: its nine buttons
        // wrap to many lines in a window this small, and the title bar with them.
        await AdjustButton.ClickAsync();
        await Expect(AdjustButton).ToHaveAttributeAsync("aria-expanded", "false");
        await ExpectBoxAsync(Clearance, Clearance, 900, 560, "the Adjust control shut");

        // Ever smaller windows, the last below the minimum on both axes: the
        // shown size is clamped to the window less the clearance (the space
        // wins over the minimum), and the title bar and its close control stay
        // inside the window.
        foreach (var (width, height) in new[] { (700, 400), (320, 240), (180, 120) })
        {
            await Page.SetViewportSizeAsync(width, height);
            await ExpectToPassAsync(async () =>
            {
                var box = await OverlayBoxAsync();
                AssertNear(Math.Min(900, width - 2 * Clearance), box.Width, $"the shown width at {width} x {height}");
                Assert.True(box.Height <= height - 2 * Clearance + Tolerance, $"the shown height fits the {height} px window: {box.Height:0.#}");
                await AssertInsideTheWindowAsync(TitleBar, "the title bar", width, height);
                await AssertInsideTheWindowAsync(CloseNotesButton, "the close control", width, height);
            });
        }
        await Expect(Page.Locator("#blazor-error-ui")).ToBeHiddenAsync();

        // Grown again, the held size shows where it was; nothing was written.
        await Page.SetViewportSizeAsync(DesktopWidth, DesktopHeight);
        await ExpectBoxAsync(Clearance, Clearance, 900, 560, "grown again");
        Assert.Equal(writes, await PlacementWritesAsync());
        Assert.Equal(stored, await StoredPlacementAsync());

        // A gesture is bounded the same way at every width: the overlay closes
        // and reopens at the held size.
        await CloseNotesButton.ClickAsync();
        await Expect(NotesDialog).ToHaveCountAsync(0);
        await OpenNotesAsync();
        await ExpectBoxAsync(Clearance, Clearance, 900, 560, "reopened");
    }

    [Fact]
    public async Task AtAPhonesWidth_NarrowerThenWider_WorksWithinTheSameBounds()
    {
        await CountPlacementWritesAsync();
        await StartAtTheCubeReviewAsync(PhoneWidth, PhoneHeight);
        await OpenNotesAsync();
        await OpenAdjustControlAsync();
        var area = await AreaAsync();
        double space = area.Width - 2 * Clearance;
        double across = SizeStep(area.Width, MinimumWidth);

        // The default width fills the space: the corner at the clearance.
        await ExpectToPassAsync(async () => AssertNear(space, (await OverlayBoxAsync()).Width, "the default width at a phone's"));
        var start = await OverlayBoxAsync();
        AssertNear(Clearance, start.X, "the default left edge at a phone's width");

        // The height is unset and follows the note, which rewraps as the width
        // changes; the width and the left edge are what a width step fixes.
        await NarrowerButton.ClickAsync();
        await ExpectWidthAsync(Clearance, space - across, "one step narrower");
        await WiderButton.ClickAsync();
        await ExpectWidthAsync(Clearance, space, "and back");
        Assert.Equal(2, await PlacementWritesAsync());

        // At the room, wider writes nothing; eight steps narrower reach the
        // minimum, the same minimum as a desktop's, and a ninth writes nothing.
        await WiderButton.ClickAsync();
        Assert.Equal(2, await PlacementWritesAsync());
        for (int i = 0; i < 8; i++) await NarrowerButton.ClickAsync();
        await ExpectWidthAsync(Clearance, MinimumWidth, "eight steps narrower");
        Assert.Equal(10, await PlacementWritesAsync());
        await NarrowerButton.ClickAsync();
        Assert.Equal(10, await PlacementWritesAsync());
    }

    private async Task ExpectWidthAsync(double left, double width, string because) =>
        await ExpectToPassAsync(async () =>
        {
            var box = await OverlayBoxAsync();
            AssertNear(left, box.X, $"{because}: the left edge");
            AssertNear(width, box.Width, $"{because}: the width");
        });

    /// <summary>
    /// How far outside the window an edge may stand and still count as in
    /// view: sub-pixel rounding, and less than the overlay's one-pixel border.
    /// </summary>
    private const double InView = 0.5;

    private static async Task AssertInsideTheWindowAsync(Microsoft.Playwright.ILocator element, string what, int width, int height)
    {
        var box = await LaidOutBoxAsync(element, what);
        Assert.True(
            box.X >= -InView && box.Y >= -InView
            && box.X + box.Width <= width + InView && box.Y + box.Height <= height + InView,
            $"{what} should be inside the {width} x {height} window; it is at ({box.X:0.#}, {box.Y:0.#}), {box.Width:0.#} x {box.Height:0.#}.");
    }
}
