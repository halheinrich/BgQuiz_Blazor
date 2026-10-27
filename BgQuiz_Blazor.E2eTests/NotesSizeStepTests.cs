using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Resizing the notes overlay without dragging: the Adjust control's four size
/// steps and Reset, by mouse and by keyboard (<c>SPEC-quiz-view.md</c> §4, "The
/// notes overlay's size is a remembered preference", "Without dragging"; issue
/// <c>halheinrich/backgammon#414</c>). Each press changes the size one step —
/// an eighth of the axis's range from the minimum to the area less the
/// clearance on each side, the build's step — from the same top-left corner,
/// and commits it; a step at a bound writes nothing; Reset puts the notes back
/// in the middle at their first size and clears both preferences. Taps are
/// <c>NotesPlacementTouchTests</c>'.
/// </summary>
public sealed class NotesSizeStepTests : NotesPlacementTestBase
{
    public NotesSizeStepTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright, output)
    {
    }

    [Fact]
    public async Task EachSizeStepAndReset_ByMouse_ResizeOneStepFromTheCorner_AndCommit()
    {
        await CountPlacementWritesAsync();
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        await OpenAdjustControlAsync();
        await ExpectOverlayCentredAsync("the Adjust control open, the preferences unset");
        var start = await OverlayBoxAsync();
        var area = await AreaAsync();
        double across = SizeStep(area.Width, MinimumWidth);
        double down = SizeStep(area.Height, await MinimumHeightAsync());

        await ClickAndExpectAsync(WiderButton, start.X, start.Y, start.Width + across, start.Height, writes: 1);
        await ClickAndExpectAsync(TallerButton, start.X, start.Y, start.Width + across, start.Height + down, writes: 2);
        await ClickAndExpectAsync(NarrowerButton, start.X, start.Y, start.Width, start.Height + down, writes: 3);
        await ClickAndExpectAsync(ShorterButton, start.X, start.Y, start.Width, start.Height, writes: 4);

        // Wider until the room from the corner runs out: the right edge at the
        // clearance. A further step cannot change the size, and writes nothing.
        double room = area.Width - Clearance - start.X;
        int writes = 4;
        double width = start.Width;
        while (width < room)
        {
            width = Math.Min(width + across, room);
            await ClickAndExpectAsync(WiderButton, start.X, start.Y, width, start.Height, ++writes);
        }
        var atTheRoom = await StoredPlacementAsync();
        await ClickAndExpectAsync(WiderButton, start.X, start.Y, room, start.Height, writes);
        Assert.Equal(atTheRoom, await StoredPlacementAsync());

        // Reset: back in the middle at the first size, the entry gone.
        await ResetButton.ClickAsync();
        await ExpectOverlayCentredAsync("after Reset");
        await ExpectToPassAsync(async () => AssertNear(DefaultWidth, (await OverlayBoxAsync()).Width, "the width after Reset"));
        Assert.Null(await StoredPlacementAsync());
        Assert.Equal(writes + 1, await PlacementWritesAsync());
        await Expect(NotesDialog).ToBeVisibleAsync();
    }

    [Fact]
    public async Task EachSizeStepAndReset_ByKeyboard_TabToIt_ThenEnterAndSpace()
    {
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        await Expect(NotesDialog).ToBeFocusedAsync();
        await ExpectKeyboardShortcutReadyAsync();

        await Page.Keyboard.PressAsync("Tab");
        await Expect(AdjustButton).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(AdjustButton).ToHaveAttributeAsync("aria-expanded", "true");
        await ExpectOverlayCentredAsync("the Adjust control open");
        var start = await OverlayBoxAsync();
        var area = await AreaAsync();
        double across = SizeStep(area.Width, MinimumWidth);
        double down = SizeStep(area.Height, await MinimumHeightAsync());

        // Past the close button and the four move steps, in their tab order.
        foreach (var name in new[]
                 {
                     ExpectedText.CloseNotesButton, ExpectedText.MoveUpButton, ExpectedText.MoveDownButton,
                     ExpectedText.MoveLeftButton, ExpectedText.MoveRightButton,
                 })
        {
            await Page.Keyboard.PressAsync("Tab");
            await Expect(NotesDialog.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = name, Exact = true }))
                .ToBeFocusedAsync();
        }

        await TabToAsync(WiderButton);
        await PressAndExpectAsync("Enter", start.X, start.Y, start.Width + across, start.Height);
        await PressAndExpectAsync(" ", start.X, start.Y, start.Width + 2 * across, start.Height);

        await TabToAsync(NarrowerButton);
        await PressAndExpectAsync("Enter", start.X, start.Y, start.Width + across, start.Height);
        await PressAndExpectAsync(" ", start.X, start.Y, start.Width, start.Height);

        await TabToAsync(TallerButton);
        await PressAndExpectAsync("Enter", start.X, start.Y, start.Width, start.Height + down);
        await PressAndExpectAsync(" ", start.X, start.Y, start.Width, start.Height + 2 * down);

        await TabToAsync(ShorterButton);
        await PressAndExpectAsync("Enter", start.X, start.Y, start.Width, start.Height + down);
        Assert.NotNull(await StoredPlacementAsync());

        // Reset by Enter, after which the next Tab stop is the end of the group.
        await TabToAsync(ResetButton);
        await Page.Keyboard.PressAsync("Enter");
        await ExpectOverlayCentredAsync("after Reset by Enter");
        Assert.Null(await StoredPlacementAsync());

        // ...and by Space, after a step back to Shorter's neighbour.
        await Page.Keyboard.PressAsync("Shift+Tab");
        await Expect(ShorterButton).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Shift+Tab");
        await Expect(TallerButton).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Enter");
        Assert.NotNull(await StoredPlacementAsync());
        await Page.Keyboard.PressAsync("Tab");
        await TabToAsync(ResetButton);
        await Page.Keyboard.PressAsync(" ");
        await ExpectOverlayCentredAsync("after Reset by Space");
        Assert.Null(await StoredPlacementAsync());

        // Space on the notes' buttons was the buttons', never the page's shortcut.
        await Expect(ContinueButton).ToBeVisibleAsync();
        await Expect(NotesDialog).ToBeVisibleAsync();
    }

    private async Task TabToAsync(Microsoft.Playwright.ILocator button)
    {
        await Page.Keyboard.PressAsync("Tab");
        await Expect(button).ToBeFocusedAsync();
    }

    private async Task PressAndExpectAsync(string key, double left, double top, double width, double height)
    {
        await Page.Keyboard.PressAsync(key);
        await ExpectBoxAsync(left, top, width, height, $"after {(key == " " ? "Space" : key)}");
    }

    private async Task ClickAndExpectAsync(
        Microsoft.Playwright.ILocator button, double left, double top, double width, double height, int writes)
    {
        await button.ClickAsync();
        await ExpectBoxAsync(left, top, width, height, $"after {await button.TextContentAsync()}");
        Assert.Equal(writes, await PlacementWritesAsync());
    }
}
