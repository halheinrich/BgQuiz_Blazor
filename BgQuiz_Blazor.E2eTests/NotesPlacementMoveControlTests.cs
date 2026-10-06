using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Moving the notes overlay without dragging: the Move control's four step
/// buttons and Reset, by mouse and by keyboard (<c>SPEC-quiz-view.md</c> §4,
/// "The notes overlay's placement is a remembered preference", "Without
/// dragging"; issue <c>halheinrich/backgammon#344</c>). Each press moves the
/// overlay one step — an eighth of its travel on that axis, the build's step
/// — and commits it; a step that cannot move writes nothing; Reset puts it
/// back in the middle and clears the preference. Taps are
/// <c>NotesPlacementTouchTests</c>'.
/// </summary>
public sealed class NotesPlacementMoveControlTests : NotesPlacementTestBase
{
    public NotesPlacementMoveControlTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright)
    {
    }

    /// <summary>One step: an eighth of the travel (the build's step, stated where <c>NotesStage</c> defines it).</summary>
    private const double Step = 1.0 / 8;

    [Fact]
    public async Task EachStepButtonAndReset_ByMouse_MoveOneStepAndCommit()
    {
        await CountPlacementWritesAsync();
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        await OpenMoveControlAsync();

        // From the centre — unset — each button moves one step its way, and
        // each press is written.
        await ClickAndExpectAsync(ExpectedText.MoveRightButton, 0.5 + Step, 0.5, writes: 1);
        await ClickAndExpectAsync(ExpectedText.MoveDownButton, 0.5 + Step, 0.5 + Step, writes: 2);
        await ClickAndExpectAsync(ExpectedText.MoveLeftButton, 0.5, 0.5 + Step, writes: 3);
        await ClickAndExpectAsync(ExpectedText.MoveUpButton, 0.5, 0.5, writes: 4);

        // Back at the centre by moves, the preference is set — a chosen
        // placement that happens to be the centre — until Reset clears it.
        Assert.NotNull(await StoredPlacementAsync());

        // Four steps right reach the edge; a fifth cannot move, and writes nothing.
        await ClickAndExpectAsync(ExpectedText.MoveRightButton, 0.5 + Step, 0.5, writes: 5);
        await ClickAndExpectAsync(ExpectedText.MoveRightButton, 0.5 + 2 * Step, 0.5, writes: 6);
        await ClickAndExpectAsync(ExpectedText.MoveRightButton, 0.5 + 3 * Step, 0.5, writes: 7);
        await ClickAndExpectAsync(ExpectedText.MoveRightButton, 1, 0.5, writes: 8);
        var atTheEdge = await StoredPlacementAsync();
        await ClickAndExpectAsync(ExpectedText.MoveRightButton, 1, 0.5, writes: 8);
        Assert.Equal(atTheEdge, await StoredPlacementAsync());

        // Reset: back in the middle, and the preference gone from the browser.
        await ResetButton.ClickAsync();
        await ExpectOverlayCentredAsync("after Reset");
        Assert.Null(await StoredPlacementAsync());
        Assert.Equal(9, await PlacementWritesAsync());

        // None of it moved anything else on the page, or closed the notes.
        await Expect(NotesDialog).ToBeVisibleAsync();
        await Expect(ContinueButton).ToBeVisibleAsync();
    }

    [Fact]
    public async Task EachStepButtonAndReset_ByKeyboard_TabToIt_ThenEnterAndSpace()
    {
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        await Expect(NotesDialog).ToBeFocusedAsync();
        await ExpectKeyboardShortcutReadyAsync();

        // The Move control is the first thing Tab reaches in the notes; Enter
        // opens its buttons.
        await Page.Keyboard.PressAsync("Tab");
        await Expect(MoveButton).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(MoveButton).ToHaveAttributeAsync("aria-expanded", "true");

        // Past the close button to each step in turn: Enter steps once, Space
        // steps again — the keyboard presses the same buttons a click does.
        await Page.Keyboard.PressAsync("Tab");
        await Expect(CloseNotesButton).ToBeFocusedAsync();

        await TabToAsync(ExpectedText.MoveUpButton);
        await PressAndExpectAsync("Enter", 0.5, 0.5 - Step);
        await PressAndExpectAsync(" ", 0.5, 0.5 - 2 * Step);

        await TabToAsync(ExpectedText.MoveDownButton);
        await PressAndExpectAsync("Enter", 0.5, 0.5 - Step);
        await PressAndExpectAsync(" ", 0.5, 0.5);

        await TabToAsync(ExpectedText.MoveLeftButton);
        await PressAndExpectAsync("Enter", 0.5 - Step, 0.5);
        await PressAndExpectAsync(" ", 0.5 - 2 * Step, 0.5);

        await TabToAsync(ExpectedText.MoveRightButton);
        await PressAndExpectAsync("Enter", 0.5 - Step, 0.5);
        await PressAndExpectAsync(" ", 0.5, 0.5);
        await PressAndExpectAsync(" ", 0.5 + Step, 0.5);
        Assert.NotNull(await StoredPlacementAsync());

        // Reset by Enter...
        await TabToAsync(ExpectedText.ResetNotesButton);
        await Page.Keyboard.PressAsync("Enter");
        await ExpectOverlayCentredAsync("after Reset by Enter");
        Assert.Null(await StoredPlacementAsync());

        // ...and by Space, after a step away.
        await Page.Keyboard.PressAsync("Shift+Tab");
        await Expect(StepButton(ExpectedText.MoveRightButton)).ToBeFocusedAsync();
        await PressAndExpectAsync("Enter", 0.5 + Step, 0.5);
        await TabToAsync(ExpectedText.ResetNotesButton);
        await Page.Keyboard.PressAsync(" ");
        await ExpectOverlayCentredAsync("after Reset by Space");
        Assert.Null(await StoredPlacementAsync());

        // Space on the notes' buttons was the buttons', never the page's
        // shortcut: the review is still here.
        await Expect(ContinueButton).ToBeVisibleAsync();
        await Expect(NotesDialog).ToBeVisibleAsync();
    }

    /// <summary>Press Tab once and expect the step (or Reset) button named <paramref name="name"/> to have focus.</summary>
    private async Task TabToAsync(string name)
    {
        await Page.Keyboard.PressAsync("Tab");
        await Expect(StepButton(name)).ToBeFocusedAsync();
    }

    /// <summary>Press <paramref name="key"/> on the focused button and expect the overlay at the given positions.</summary>
    private async Task PressAndExpectAsync(string key, double horizontal, double vertical)
    {
        await Page.Keyboard.PressAsync(key);
        await ExpectAtPositionAsync(horizontal, vertical, $"after {(key == " " ? "Space" : key)}");
    }

    /// <summary>Click the step button named <paramref name="name"/>, and expect the overlay there with that many writes made.</summary>
    private async Task ClickAndExpectAsync(string name, double horizontal, double vertical, int writes)
    {
        await StepButton(name).ClickAsync();
        await ExpectAtPositionAsync(horizontal, vertical, $"after {name}");
        Assert.Equal(writes, await PlacementWritesAsync());
    }
}
