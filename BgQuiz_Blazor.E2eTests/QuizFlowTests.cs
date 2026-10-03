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
        // halheinrich/backgammon#113 — suppresses it while answering, so a primary-path smoke asserting
        // it would be asserting a composition its own users do not get. The
        // prompt's own pins live in MaximizeBoardTests (the setting-off scenario)
        // and in bUnit.
        await Expect(CubePill(ExpectedText.NoDoublePill)).ToBeVisibleAsync();
        await Expect(Page.Locator(".bg-diagram")).Not.ToContainTextAsync("Best:");

        await AnswerCubeNoDoubleAsync();

        // Review state: the Solution-mode diagram fills the analysis panel. The
        // committed fixture's truth is No double, so the panel's Best line is an
        // exact, stable pin.
        await Expect(Page.Locator(".bg-diagram")).ToContainTextAsync(ExpectedText.SolutionBestNoDouble);
        // The verdict is one line naming the answer by its label at the
        // decision, in the same label home's wording as the Best line.
        await Expect(VerdictBand).ToHaveTextAsync(ExpectedText.CubeVerdictNoDoubleCorrect);

        await ContinueToDoneAsync();
        await Expect(Page.GetByText(ExpectedText.TotalProblemsShown(1))).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TooGoodToDoubleTakePath_TooGoodIsCharged_ThenNoDoubleOnReturnIsPractice()
    {
        // The position that decided SPEC-scoring §3's 2026-09-02 amendment
        // (halheinrich/backgammon#187), end to end: XG labels it "Too good to
        // double/Take" (no double +1.1711, double/take +0.6004), and its truth
        // is No double BY RULING — Too Good requires the pass, and the
        // opponent takes. A match where gammons are possible, so the fourth
        // pill reads Too good. Answered first the way a reader of XG's label
        // would — Too good — which loses no equity at the board but is charged
        // SPEC-scoring §3's convention, 2(1 − T); then, gone away from and come
        // back to (SPEC-quiz-history.md §3: returning is how a problem is
        // practised), No double, which is correct, marked practice, and changes
        // no score. The first answer is the one of record. A second cube
        // position is in the folder so there is somewhere to go and come back
        // from; which of the two the source serves first is not this scenario's
        // business, so it reaches Too good and returns to it either way.
        await BootHomeAsync();
        await DisableMaximizeAsync();   // the score panel stays on screen while answering
        await BootHomeAsync();
        await PickFixturesAsync(TooGoodTakeFixture, CubeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();

        var tooGoodFirst = await CubePill(ExpectedText.TooGoodPill).CountAsync() == 1;
        if (!tooGoodFirst) await NavButton(ExpectedText.SkipButton).ClickAsync();

        // All four answers are offered, the fourth labelled Too good.
        await Expect(Page.Locator(".bg-cube-actions").GetByRole(AriaRole.Radio)).ToHaveCountAsync(4);
        await Expect(CubePill(ExpectedText.TooGoodPill)).ToBeVisibleAsync();
        await Expect(CubePill(ExpectedText.NoDoublePassPill)).ToHaveCountAsync(0);

        await AnswerCubeAsync(ExpectedText.TooGoodPill);

        await Expect(VerdictBand).ToHaveTextAsync("Not best — Too good lost 0.7992. Best: No double.");
        await Expect(VerdictBand).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("alert-danger"));
        await Expect(Page.GetByText(ExpectedText.Submitted(1))).ToBeVisibleAsync();

        // Away and back: from the first problem ▶ then ◀, from the second ◀ then ▶.
        if (tooGoodFirst)
        {
            await NavButton(ExpectedText.NextButton).ClickAsync();
            await NavButton(ExpectedText.BackButton).ClickAsync();
        }
        else
        {
            await NavButton(ExpectedText.BackButton).ClickAsync();
            await NavButton(ExpectedText.NextButton).ClickAsync();
        }
        await Expect(CubePill(ExpectedText.TooGoodPill)).ToBeVisibleAsync();
        await AnswerCubeNoDoubleAsync();

        await Expect(VerdictBand).ToHaveTextAsync(
            ExpectedText.PracticePrefix + ExpectedText.CubeVerdictNoDoubleCorrect);
        await Expect(VerdictBand).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("alert-success"));
        await Expect(Page.GetByText(ExpectedText.Submitted(1))).ToBeVisibleAsync();   // the score did not move
    }

    [Fact]
    public async Task MoneyJacobyCentredCube_OffersAllFour_TheFourthReadsNoDoublePass()
    {
        // SPEC-scoring §3, amended 2026-10-01 (halheinrich/backgammon#326), on a
        // real file: all four answers are always offered, and at a money
        // position under the Jacoby rule with the cube in the middle gammons
        // are not possible, so the fourth reads No double / Pass. The committed
        // cube fixture is exactly that position. Until the amendment this
        // position withheld the fourth pill; this scenario pinned that
        // absence, and now pins the four pills, in the row's order, by their
        // exact accessible names — in the full form, so at a viewport whose
        // row holds it (1242 px against the 1001.3 px it needs; the short form
        // is CubeLabelsTests').
        await Page.SetViewportSizeAsync(1600, 900);
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();

        var pills = Page.Locator(".bg-cube-actions").GetByRole(AriaRole.Radio);
        await Expect(pills).ToHaveCountAsync(4);
        await Expect(pills.Nth(0)).ToHaveAccessibleNameAsync(ExpectedText.NoDoublePill);
        await Expect(pills.Nth(1)).ToHaveAccessibleNameAsync(ExpectedText.DoubleTakePill);
        await Expect(pills.Nth(2)).ToHaveAccessibleNameAsync(ExpectedText.DoublePassPill);
        await Expect(pills.Nth(3)).ToHaveAccessibleNameAsync(ExpectedText.NoDoublePassPill);
        await Expect(Page.Locator(".bg-cube-actions label").Nth(3)).ToHaveTextAsync(ExpectedText.NoDoublePassPill);
        await Expect(CubePill(ExpectedText.TooGoodPill)).ToHaveCountAsync(0);

        // The fourth answer, pressed: the verdict names it by the same label.
        await AnswerCubeAsync(ExpectedText.NoDoublePassPill);
        await Expect(VerdictBand).ToHaveTextAsync("Not best — No double / Pass lost 1.3251. Best: No double.");
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
        await Expect(VerdictBand).ToHaveTextAsync(ExpectedText.CorrectPlayVerdict);
        await Expect(Page.Locator(".bg-diagram")).ToContainTextAsync("24/13");

        await ContinueToDoneAsync();
        await Expect(Page.GetByText(ExpectedText.TotalProblemsShown(1))).ToBeVisibleAsync();
    }
}
