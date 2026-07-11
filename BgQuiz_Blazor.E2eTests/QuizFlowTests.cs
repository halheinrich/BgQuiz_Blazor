using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The two primary-path smokes: a full quiz for each decision kind, from a real
/// file pick through answering, review, and the Done summary. Both fixtures are
/// single-decision <c>.xgp</c> files, so each quiz is exactly one problem long —
/// deterministic with shuffle left off.
/// </summary>
public sealed class QuizFlowTests : E2eTestBase
{
    public QuizFlowTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    [Fact]
    public async Task CubePath_PickApplyStartAnswerReviewDone()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();

        // Answering state: cube problems prompt for the radio row, and the
        // Problem-mode board must not leak the answer.
        await Expect(VerdictBand).ToContainTextAsync("Pick the cube action, then Submit.");
        await Expect(Page.Locator(".bg-diagram")).Not.ToContainTextAsync("Best:");

        await AnswerCubeNoDoubleAsync();

        // Review state: the Solution-mode diagram fills the analysis panel. The
        // committed fixture's best action is No Double, so the panel's Best
        // banner is an exact, stable pin (the taker half is suppressed when the
        // best doubler action is No Double).
        await Expect(Page.Locator(".bg-diagram")).ToContainTextAsync("Best: No Double");
        // "No double" answers both halves correctly against this fixture.
        await Expect(VerdictBand).ToContainTextAsync("Double: correct · Take: correct");

        await ContinueToDoneAsync();
        await Expect(Page.GetByText("Total problems shown: 1")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task CheckerPath_EnterBestPlayByBoardClicks()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CheckerFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();

        // Answering state: checker problems prompt for board clicks, and Submit
        // stays gated until a complete play has been assembled.
        await Expect(VerdictBand).ToContainTextAsync("Click the board to build your play, then Submit.");
        await Expect(SubmitButton).ToBeDisabledAsync();

        // The fixture's decision is a 6-5 roll whose best play is 24/13. The
        // entry model is one-click source-advance consuming the leftmost
        // rendered die first, so clicking point 24 moves 24/18 (the 6) and
        // clicking point 18 moves 18/13 (the 5), completing the play.
        await ClickBoardPointAsync(24);
        await ClickBoardPointAsync(18);

        await Expect(SubmitButton).ToBeEnabledAsync();
        await SubmitButton.ClickAsync();

        // Review: the entered play matches the zero-loss candidate, and the
        // Solution-mode analysis panel lists it in its collapsed notation.
        await Expect(VerdictBand).ToContainTextAsync("Correct — you found the best play.");
        await Expect(Page.Locator(".bg-diagram")).ToContainTextAsync("24/13");

        await ContinueToDoneAsync();
        await Expect(Page.GetByText("Total problems shown: 1")).ToBeVisibleAsync();
    }
}
