using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// What the notes overlay's placement and size survive (<c>SPEC-quiz-view.md</c>
/// §4, "The notes overlay's placement is a remembered preference" and "The
/// notes overlay's size is a remembered preference", "What it survives", and
/// §6's table; issues <c>halheinrich/backgammon#344</c> and
/// <c>halheinrich/backgammon#414</c>): closing the notes, the next problem,
/// navigating away and back, End quiz and the next Start, and a full reload —
/// and Reset, which is what clears both.
/// </summary>
public sealed class NotesPlacementSurvivalTests : NotesPlacementTestBase
{
    public NotesPlacementSurvivalTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright, output)
    {
    }

    /// <summary>
    /// What the scenario chose — a size two steps wider and one taller, then a
    /// placement two steps right and one down — as stored, which every later
    /// opening must show by the rules.
    /// </summary>
    private StoredPreferences? _chosen;

    private ILocator ShowStatsButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ShowStatsButton, Exact = true });

    private ILocator EndQuizButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.EndQuizButton, Exact = true });

    [Fact]
    public async Task AChosenPlacementAndSize_SurviveClosing_TheNextProblem_StatsAndBack_EndQuizAndANewStart_AndAReload()
    {
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        await OpenAdjustControlAsync();
        await WiderButton.ClickAsync();
        await WiderButton.ClickAsync();
        await TallerButton.ClickAsync();
        await StepButton(ExpectedText.MoveRightButton).ClickAsync();
        await StepButton(ExpectedText.MoveRightButton).ClickAsync();
        await StepButton(ExpectedText.MoveDownButton).ClickAsync();
        await ExpectToPassAsync(async () =>
        {
            _chosen = await StoredPreferencesAsync();
            Assert.NotNull(_chosen?.Width);
            Assert.NotNull(_chosen?.Height);
            Assert.True(_chosen!.Horizontal > 0.5 && _chosen.Vertical > 0.5, $"moved right and down: {_chosen}");
        });
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
        await ExpectUrlAsync(AppRoute.Stats);
        await Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.BackToQuizButton }).ClickAsync();
        await ExpectUrlAsync(AppRoute.Quiz);
        await Expect(NotesButton).ToBeVisibleAsync();
        await OpenNotesAsync();
        await ExpectChosenAsync("after Show stats and back");

        // End quiz, and the next Start.
        await CloseNotesButton.ClickAsync();
        await EndQuizButton.ClickAsync();
        await ExpectUrlAsync(AppRoute.Done);
        await Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.BackToSetupButton }).ClickAsync();
        await ExpectUrlAsync(AppRoute.Home);
        await StartQuizAsync();
        await AnswerCubeAsync(ExpectedText.DoubleTakePill);
        await OpenNotesAsync();
        await ExpectChosenAsync("in the next quiz");

        // A full reload: the quiz is gone, the preferences are not.
        await Page.ReloadAsync();
        await ExpectUrlAsync(AppRoute.Home);
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        await ExpectChosenAsync("after a reload");

        // None of it wrote anything: what is stored is what the gestures wrote.
        Assert.Equal(stored, await StoredPlacementAsync());
    }

    [Fact]
    public async Task Reset_ThenAReload_OpensTheNotesCentred_AtTheirDefaultSize()
    {
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        await OpenAdjustControlAsync();
        await StepButton(ExpectedText.MoveLeftButton).ClickAsync();
        await StepButton(ExpectedText.MoveUpButton).ClickAsync();
        await ExpectAtPositionAsync(0.375, 0.375, "after the steps");
        await NarrowerButton.ClickAsync();
        await TallerButton.ClickAsync();
        await ExpectToPassAsync(async () => Assert.NotNull((await StoredPreferencesAsync())?.Height));

        await ResetButton.ClickAsync();
        await ExpectOverlayCentredAsync("after Reset");
        Assert.Null(await StoredPlacementAsync());

        await Page.ReloadAsync();
        await ExpectUrlAsync(AppRoute.Home);
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        await ExpectOverlayCentredAsync("after Reset and a reload");
        AssertNear(DefaultWidth, (await OverlayBoxAsync()).Width, "the default width after Reset and a reload");
        await Expect(NotesDialog).Not.ToHaveAttributeAsync("data-set-width", string.Empty);
        await Expect(NotesDialog).Not.ToHaveAttributeAsync("data-set-height", string.Empty);
        Assert.Null(await StoredPlacementAsync());
    }

    /// <summary>The chosen size shows, and stands where the rule puts the chosen placement with it.</summary>
    private async Task ExpectChosenAsync(string when)
    {
        var chosen = _chosen!;
        await ExpectAtPositionAsync(chosen.Horizontal!.Value, chosen.Vertical!.Value, when);
        await ExpectToPassAsync(async () =>
        {
            var box = await OverlayBoxAsync();
            AssertNear(chosen.Width!.Value, box.Width, $"{when}: the chosen width");
            AssertNear(chosen.Height!.Value, box.Height, $"{when}: the chosen height");
        });
    }
}
