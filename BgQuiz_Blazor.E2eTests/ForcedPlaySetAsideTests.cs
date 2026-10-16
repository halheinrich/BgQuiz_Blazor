using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// A checker play whose roll leaves one legal play asks nothing: the load sets it aside, the
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
        await Expect(body).ToContainTextAsync("1 forced play was set aside");
        await StartQuizAsync();
        await ExpectProblemNumberAsync(1);
        await Expect(Page.Locator(".problem-position")).ToContainTextAsync("of 1");
        await Expect(Page.Locator(".board-container .bg-play-entry")).ToBeVisibleAsync();
        await ClickBoardPointAsync(24);
        await ClickBoardPointAsync(18);
        await Expect(SubmitButton).ToBeEnabledAsync();
        await SubmitButton.ClickAsync();
        await Expect(VerdictBand).ToHaveTextAsync(ExpectedText.CorrectPlayVerdict);
        await ContinueToDoneAsync();
        await Expect(Page.GetByText(ExpectedText.TotalProblemsShown(1))).ToBeVisibleAsync();
    }
}
