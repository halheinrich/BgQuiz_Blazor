using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// A checker play whose roll leaves no choice of play asks nothing: the walk sets it aside, the
/// count says so, and the quiz never asks it. <see cref="E2eTestBase.ForcedFixture"/> also holds
/// an analysed cube pane, and a play the walk set aside does not suppress it, so the file's one
/// decision is that cube (Hal's ruling, halheinrich/backgammon#514, 2026-11-01: "a forced play
/// with an analysed cube now yields the cube").
/// </summary>
public sealed class ForcedPlaySetAsideTests : E2eTestBase
{
    public ForcedPlaySetAsideTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright, output) { }

    [Fact]
    public async Task AForcedPlayIsSetAsideAtTheLoad_AndItsFilesCubeIsAskedBesideTheRealPlay()
    {
        await BootHomeAsync();
        await PickFixturesInOrderAsync(CheckerFixture, ForcedFixture);
        await ExpectFilterInEffectAsync();
        var body = Page.Locator("body");
        await Expect(body).ToContainTextAsync(ExpectedText.ProblemFiles(2));
        await Expect(body).ToContainTextAsync(ExpectedText.DecisionsMatchYourFilters(2));
        await Expect(body).ToContainTextAsync(ExpectedText.ForcedPlaysSetAside(1));
        await Expect(body).ToContainTextAsync(ExpectedText.AnswerTypeCount(ExpectedText.CheckerPlaysType, 1));

        // The checker fixture's play first, as read.
        await StartQuizAsync();
        await Expect(Page.Locator(".board-container .bg-play-entry")).ToBeVisibleAsync();
        await ClickBoardPointAsync(24);
        await ClickBoardPointAsync(18);
        await Expect(SubmitButton).ToBeEnabledAsync();
        await SubmitButton.ClickAsync();
        await Expect(VerdictBand).ToHaveTextAsync(ExpectedText.CorrectPlayVerdict);
        await ExpectProblemNumberAsync(1);

        // Then the forced fixture's cube, never its play.
        await Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ContinueButton }).ClickAsync();
        await Expect(CubeAnswers).ToHaveCountAsync(4);
        await Expect(Page.GetByRole(AriaRole.Group, new() { Name = "Problem location" }))
            .ToContainTextAsync(ForcedFixture);
    }
}
