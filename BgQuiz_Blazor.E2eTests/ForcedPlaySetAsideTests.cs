using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// A checker play whose roll leaves no choice of play asks nothing: the walk sets it aside, the
/// count says so, and the quiz holds only the real decision.
/// </summary>
public sealed class ForcedPlaySetAsideTests : E2eTestBase
{
    public ForcedPlaySetAsideTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright, output) { }

    [Fact]
    public async Task AForcedPlayIsSetAsideAtTheLoad_AndTheQuizHoldsTheRealDecision()
    {
        await BootHomeAsync();
        await PickFixturesAsync(ForcedFixture, CheckerFixture);
        await ExpectFilterInEffectAsync();
        var body = Page.Locator("body");
        await Expect(body).ToContainTextAsync("2 problem files");
        await Expect(body).ToContainTextAsync(ExpectedText.DecisionsMatchYourFilters(1));
        await Expect(body).ToContainTextAsync(ExpectedText.ForcedPlaysSetAside(1));
        await StartQuizAsync();
        await Expect(Page.Locator(".board-container .bg-play-entry")).ToBeVisibleAsync();
        await ClickBoardPointAsync(24);
        await ClickBoardPointAsync(18);
        await Expect(SubmitButton).ToBeEnabledAsync();
        await SubmitButton.ClickAsync();
        await Expect(VerdictBand).ToHaveTextAsync(ExpectedText.CorrectPlayVerdict);
        await ExpectProblemNumberAsync(1);   // the score panel shows at review: one problem, not two
        await Expect(Page.Locator(".problem-position")).ToContainTextAsync("of 1");
        await ContinueToDoneAsync();
        await Expect(Page.GetByText(ExpectedText.TotalProblemsShown(1))).ToBeVisibleAsync();
    }
}
