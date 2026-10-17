using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Resample, and a new sample ending the quiz in progress, over the published
/// artifact in a real browser (<c>SPEC-filtering.md</c> §4, "The sample, the
/// limits and Resample"; <c>SPEC-quiz-history.md</c> §9;
/// halheinrich/backgammon#416, halheinrich/backgammon#417). The folder
/// module's redraw of a held context is the producer's, and only a browser
/// runs it; the pages' half — the offer, the landing, the line, Done's one
/// button — is pinned in <c>PageTests</c>, and these scenarios check that the
/// two meet.
/// </summary>
public sealed class ResampleTests : E2eTestBase
{
    public ResampleTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright, output) { }

    private ILocator HomeNavLink => Page.GetByRole(AriaRole.Link, new() { Name = ExpectedText.HomeNavLink });

    private ILocator BackToQuizButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.BackToQuizButton });

    private ILocator RestartButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.RestartWithSameFiltersButton });

    /// <summary>Done's resample-and-restart: present exactly while Home's Resample would be.</summary>
    private ILocator RestartWithNewSampleButton => Page.Locator("#restartWithNewSample");

    /// <summary>
    /// Offered for a folder the limits truncated, and not for one they held
    /// whole — the producer's per-kind report, read through the real pick.
    /// The within-limits pick comes first, so the negative half is read on a
    /// page whose summary has rendered: the offer renders with it, or not at
    /// all.
    /// </summary>
    [Fact]
    public async Task Resample_IsOfferedForAnOverLimitFolder_AndNotForOneWithinItsLimits()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await Expect(ResampleButton).ToHaveCountAsync(0);

        await PickOverLimitFolderAsync();
        await Expect(TruncationNotice).ToContainTextAsync(ExpectedText.OneFileNotRead);
        await Expect(ResampleButton).ToBeVisibleAsync();
    }

    /// <summary>
    /// A resample made from Home mid-quiz ends the quiz: Home says so, the way
    /// back to a live quiz is gone, the line's link reaches the finished
    /// quiz's summary on Done, and Done's Restart starts a quiz again — over
    /// the sample the resample left held, the only one there is.
    /// </summary>
    [Fact]
    public async Task AResampleMidQuiz_EndsTheQuiz_HomeSaysSo_TheLinkReachesItsSummary_AndRestartRuns()
    {
        await BootHomeAsync();
        await PickOverLimitFolderAsync();
        await ExpectFilterInEffectAsync();
        await StartQuizAsync();
        await Expect(CubePill(ExpectedText.NoDoublePill)).ToBeVisibleAsync();

        await HomeNavLink.ClickAsync();
        await ExpectUrlAsync(AppRoute.Home);
        await Expect(BackToQuizButton).ToBeVisibleAsync();
        await ResampleButton.ClickAsync();

        await Expect(EndedByNewSampleLine).ToContainTextAsync(ExpectedText.QuizFinishedByNewFiles);
        await Expect(BackToQuizButton).ToHaveCountAsync(0);

        await EndedByNewSampleLine.GetByRole(AriaRole.Link).ClickAsync();
        await ExpectPageRenderedAsync(AppRoute.Done);
        await Expect(Page.Locator("main")).ToContainTextAsync(ExpectedText.TotalProblemsShown(1));

        await RestartButton.ClickAsync();
        await ExpectPageRenderedAsync(AppRoute.Quiz);
        await Expect(CubePill(ExpectedText.NoDoublePill)).ToBeVisibleAsync();
    }

    /// <summary>
    /// Done's one button (halheinrich/backgammon#417): from the summary, a new
    /// draw of the folder and a quiz over it, in one click.
    /// </summary>
    [Fact]
    public async Task RestartWithANewSample_FromDone_DrawsAgainAndRunsTheQuiz()
    {
        await BootHomeAsync();
        await PickOverLimitFolderAsync();
        await ExpectFilterInEffectAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();
        await Expect(RestartWithNewSampleButton).ToBeVisibleAsync();

        await RestartWithNewSampleButton.ClickAsync();

        await ExpectPageRenderedAsync(AppRoute.Quiz);
        await Expect(CubePill(ExpectedText.NoDoublePill)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task RestartWithANewSample_IsNotOffered_ForAFolderHeldWhole()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ExpectFilterInEffectAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();

        await Expect(RestartButton).ToBeVisibleAsync();
        await Expect(RestartWithNewSampleButton).ToHaveCountAsync(0);
    }
}
