using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// What the notes overlay's placement survives (<c>SPEC-quiz-view.md</c> §4,
/// "The notes overlay's placement is a remembered preference", "What it
/// survives", and §6's table; issue <c>halheinrich/backgammon#344</c>):
/// closing the notes, the next problem, navigating away and back, End quiz
/// and the next Start, and a full reload — and Reset, which is what clears it.
/// </summary>
public sealed class NotesPlacementSurvivalTests : NotesPlacementTestBase
{
    public NotesPlacementSurvivalTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright)
    {
    }

    /// <summary>The placement the scenario chooses: two steps right, one down.</summary>
    private const double ChosenHorizontal = 0.75;

    /// <inheritdoc cref="ChosenHorizontal"/>
    private const double ChosenVertical = 0.625;

    private ILocator ShowStatsButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ShowStatsButton, Exact = true });

    private ILocator EndQuizButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.EndQuizButton, Exact = true });

    [Fact]
    public async Task AChosenPlacement_SurvivesClosing_TheNextProblem_StatsAndBack_EndQuizAndANewStart_AndAReload()
    {
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        await OpenMoveControlAsync();
        await StepButton(ExpectedText.MoveRightButton).ClickAsync();
        await StepButton(ExpectedText.MoveRightButton).ClickAsync();
        await StepButton(ExpectedText.MoveDownButton).ClickAsync();
        await ExpectChosenAsync("as chosen");
        var stored = await StoredPlacementAsync();
        Assert.NotNull(stored);

        // Closing the notes.
        await CloseNotesButton.ClickAsync();
        await Expect(NotesDialog).ToHaveCountAsync(0);
        await OpenNotesAsync();
        await ExpectChosenAsync("closed and opened again");

        // The next problem — the checker play, with notes of its own.
        await CloseNotesButton.ClickAsync();
        await ContinueButton.ClickAsync();
        await Expect(Page.Locator(".board-container .bg-play-entry")).ToBeVisibleAsync();
        await ClickBoardPointAsync(8);
        await ClickBoardPointAsync(6);
        await Expect(SubmitButton).ToBeEnabledAsync();
        await SubmitButton.ClickAsync();
        await Expect(NotesButton).ToBeVisibleAsync();
        await OpenNotesAsync();
        await ExpectChosenAsync("on the next problem's notes");

        // Navigating away and back.
        await CloseNotesButton.ClickAsync();
        await ShowStatsButton.ClickAsync();
        await ExpectUrlAsync("/stats");
        await Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.BackToQuizButton }).ClickAsync();
        await ExpectUrlAsync("/quiz");
        await Expect(NotesButton).ToBeVisibleAsync();
        await OpenNotesAsync();
        await ExpectChosenAsync("after Show stats and back");

        // End quiz, and the next Start.
        await CloseNotesButton.ClickAsync();
        await EndQuizButton.ClickAsync();
        await ExpectUrlAsync("/done");
        await Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.BackToSetupButton }).ClickAsync();
        await ExpectUrlAsync("/");
        await StartQuizAsync();
        await AnswerCubeAsync(ExpectedText.DoubleTakePill);
        await OpenNotesAsync();
        await ExpectChosenAsync("in the next quiz");

        // A full reload: the quiz is gone, the placement is not.
        await Page.ReloadAsync();
        await ExpectUrlAsync("/");
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        await ExpectChosenAsync("after a reload");

        // None of it wrote anything: what is stored is what the moves wrote.
        Assert.Equal(stored, await StoredPlacementAsync());
    }

    [Fact]
    public async Task Reset_ThenAReload_OpensTheNotesCentred()
    {
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        await OpenMoveControlAsync();
        await StepButton(ExpectedText.MoveLeftButton).ClickAsync();
        await StepButton(ExpectedText.MoveUpButton).ClickAsync();
        await ExpectAtPositionAsync(0.375, 0.375, "after the steps");
        Assert.NotNull(await StoredPlacementAsync());

        await ResetButton.ClickAsync();
        await ExpectOverlayCentredAsync("after Reset");
        Assert.Null(await StoredPlacementAsync());

        await Page.ReloadAsync();
        await ExpectUrlAsync("/");
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        await ExpectOverlayCentredAsync("after Reset and a reload");
        Assert.Null(await StoredPlacementAsync());
    }

    private Task ExpectChosenAsync(string when) => ExpectAtPositionAsync(ChosenHorizontal, ChosenVertical, when);
}
