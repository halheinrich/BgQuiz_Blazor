using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Moving the notes overlay on a touch screen (<c>SPEC-quiz-view.md</c> §4,
/// "The notes overlay's placement is a remembered preference": "Drag by touch
/// works as by mouse, and the step buttons are there"; issue
/// <c>halheinrich/backgammon#344</c>). The context is a touch device, so taps
/// are taps; the touch drag is dispatched as real touch input through the
/// DevTools protocol, which is how a finger reaches Chromium — the browser
/// turns it into the touch pointer events the title bar handles.
/// </summary>
public sealed class NotesPlacementTouchTests : NotesPlacementTestBase
{
    public NotesPlacementTouchTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright)
    {
    }

    /// <summary>A touch screen: taps and touch input are available.</summary>
    protected override BrowserNewContextOptions ContextOptions => new() { HasTouch = true };

    /// <summary>One step: an eighth of the travel.</summary>
    private const double Step = 1.0 / 8;

    [Fact]
    public async Task EachStepButtonAndReset_ByTap_MoveOneStepAndCommit()
    {
        await StartAtTheCubeReviewAsync();
        await NotesButton.TapAsync();
        await ExpectPlacedAsync();
        await MoveButton.TapAsync();
        await Expect(MoveButton).ToHaveAttributeAsync("aria-expanded", "true");

        await TapAndExpectAsync(ExpectedText.MoveUpButton, 0.5, 0.5 - Step);
        await TapAndExpectAsync(ExpectedText.MoveLeftButton, 0.5 - Step, 0.5 - Step);
        await TapAndExpectAsync(ExpectedText.MoveDownButton, 0.5 - Step, 0.5);
        await TapAndExpectAsync(ExpectedText.MoveRightButton, 0.5, 0.5);
        await TapAndExpectAsync(ExpectedText.MoveRightButton, 0.5 + Step, 0.5);
        Assert.NotNull(await StoredPlacementAsync());

        await ResetButton.TapAsync();
        await ExpectOverlayCentredAsync("after Reset");
        Assert.Null(await StoredPlacementAsync());
        await Expect(NotesDialog).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ADragByTouch_MovesTheNotesAsTheMouseDoes_AndCommitsOnLift()
    {
        await StartAtTheCubeReviewAsync();
        await NotesButton.TapAsync();
        await ExpectPlacedAsync();
        var start = await OverlayBoxAsync();
        double scrollBefore = await Page.EvaluateAsync<double>("() => window.scrollY");

        var touch = await Page.Context.NewCDPSessionAsync(Page);
        var (x, y) = await GrabPointAsync();
        await TouchAsync(touch, "touchStart", x, y);
        for (int i = 1; i <= 8; i++)
            await TouchAsync(touch, "touchMove", x - 20 * i, y - 10 * i);

        // The finger moves the notes and nothing else: no write until it lifts.
        await ExpectOverlayAtAsync(start.X - 160, start.Y - 80, "mid-drag, the overlay follows the finger");
        Assert.Null(await StoredPlacementAsync());

        await touch.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = "touchEnd",
            ["touchPoints"] = Array.Empty<object>(),
        });
        await ExpectOverlayAtAsync(start.X - 160, start.Y - 80, "lifted, the overlay stays where it was dropped");
        Assert.NotNull(await StoredPlacementAsync());
        await Expect(NotesDialog).ToBeVisibleAsync();

        // The touch was the title bar's, not the page's to pan.
        Assert.Equal(scrollBefore, await Page.EvaluateAsync<double>("() => window.scrollY"));
    }

    private static Task TouchAsync(ICDPSession touch, string type, double x, double y) =>
        touch.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = new object[] { new Dictionary<string, object> { ["x"] = x, ["y"] = y } },
        });

    /// <summary>Tap the step button named <paramref name="name"/> and expect the overlay at the given positions.</summary>
    private async Task TapAndExpectAsync(string name, double horizontal, double vertical)
    {
        await StepButton(name).TapAsync();
        await ExpectAtPositionAsync(horizontal, vertical, $"after tapping {name}");
    }
}
