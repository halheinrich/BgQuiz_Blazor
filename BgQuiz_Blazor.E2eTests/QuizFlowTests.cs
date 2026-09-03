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

        // Answering state: cube problems offer the radio row, and the
        // Problem-mode board must not leak the answer.
        //
        // Keyed on the radios, not on the status strip's neutral prompt. The
        // prompt is Normal-view chrome and the maximize mode — the default since
        // #113 — suppresses it while answering, so a primary-path smoke asserting
        // it would be asserting a composition its own users do not get. The
        // prompt's own pins live in MaximizeBoardTests (the setting-off scenario)
        // and in bUnit.
        await Expect(Page.GetByRole(AriaRole.Radio, new() { Name = "No double" })).ToBeVisibleAsync();
        await Expect(Page.Locator(".bg-diagram")).Not.ToContainTextAsync("Best:");

        await AnswerCubeNoDoubleAsync();

        // Review state: the Solution-mode diagram fills the analysis panel. The
        // committed fixture's best action is No Double, so the panel's Best
        // banner is an exact, stable pin (the taker half is suppressed when the
        // best doubler action is No Double).
        await Expect(Page.Locator(".bg-diagram")).ToContainTextAsync("Best: No Double");
        // No double / Take answers both halves correctly against this fixture.
        // The verdict line labels each half by what was submitted — the claim
        // and the taker action — in the diagram's banner wording.
        await Expect(VerdictBand).ToContainTextAsync("No Double: correct · Take: correct");

        await ContinueToDoneAsync();
        await Expect(Page.GetByText("Total problems shown: 1")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TooGoodToDoubleTakePath_TooGoodIsTheWrongClaim_ThenNoDoubleScoresOnRedo()
    {
        // The position that decided SPEC-scoring §3's 2026-09-02 amendment
        // (halheinrich/backgammon#187), end to end: XG labels it "Too good to
        // double/Take" (no double +1.1711, double/take +0.6004), and it is a
        // No double / Take here BY RULING — Too Good requires the pass, and
        // the opponent takes. Answered first the way a reader of XG's label
        // would — Too good — which is the wrong claim over the right action,
        // scored wrong at no equity lost; then, as a practice retry, No double,
        // which is fully correct. The first answer is the one of record.
        await BootHomeAsync();
        await PickFixtureAsync(TooGoodTakeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();

        // A match position: Too good is offered (the withheld case is money
        // under Jacoby with the cube centred — see the scenario below), so
        // the wrong claim is a pill a user can actually press.
        await Expect(Page.GetByRole(AriaRole.Radio, new() { Name = "Too good" })).ToBeVisibleAsync();

        await AnswerCubeAsync("Too good");

        // Right action, wrong claim, in this direction too: the line names the
        // truth claim rather than printing a zero loss. The taker half is the
        // Too good pill's implied Pass, wrong against a take.
        await Expect(VerdictBand).ToContainTextAsync(
            "Too Good: wrong claim — it's No Double (right action, no equity lost) · Pass: incorrect");
        await Expect(VerdictBand).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("alert-danger"));

        await Page.GetByRole(AriaRole.Button, new() { Name = "Redo" }).ClickAsync();
        await AnswerCubeNoDoubleAsync();

        await Expect(VerdictBand).ToContainTextAsync("Practice retry");
        await Expect(VerdictBand).ToContainTextAsync("No Double: correct · Take: correct");
        await Expect(VerdictBand).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("alert-success"));

        // The answer of record stands: one doubling decision, scored wrong.
        await ContinueToDoneAsync();
        await Expect(Page.GetByText("Total problems shown: 1")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task MoneyJacobyCentredCube_WithholdsTooGood_AndOffersTheOtherThree()
    {
        // SPEC-scoring §3's 2026-09-02 amendment, consequence (v), on a real
        // file (halheinrich/backgammon#187): at a money position under the
        // Jacoby rule with the cube in the middle, gammons do not count until
        // the cube turns, so the no-double equity never exceeds the cash and
        // Too Good cannot occur — the pill is withheld. The committed cube
        // fixture is exactly that position (money, Jacoby on, cube centred),
        // so the absence is pinned on the file the primary path already runs
        // on, beside the positive precondition that the other three pairs are
        // there: a row that failed to render at all would pass an absence pin
        // for free.
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();

        await Expect(Page.GetByRole(AriaRole.Radio, new() { Name = "No double" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Radio, new() { Name = "Double / Take" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Radio, new() { Name = "Double / Pass" })).ToBeVisibleAsync();
        await Expect(Page.Locator(".bg-cube-actions").GetByRole(AriaRole.Radio)).ToHaveCountAsync(3);
        await Expect(Page.GetByRole(AriaRole.Radio, new() { Name = "Too good" })).ToHaveCountAsync(0);

        // And the three that are offered still answer the problem: one click
        // is a complete pair, Submit lights, the review lands.
        await AnswerCubeNoDoubleAsync();
        await Expect(VerdictBand).ToContainTextAsync("No Double: correct · Take: correct");
    }

    [Fact]
    public async Task CheckerPath_EnterBestPlayByBoardClicks()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CheckerFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();

        // Answering state: checker problems get the click-to-build board, and
        // Submit stays gated until a complete play has been assembled. Keyed on
        // the play-entry board rather than the status strip's neutral prompt —
        // see the cube path above for why.
        await Expect(Page.Locator(".board-container .bg-play-entry")).ToBeVisibleAsync();
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
