using Microsoft.Playwright;
using Xunit.Abstractions;
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
    private readonly ITestOutputHelper _output;

    public QuizFlowTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright)
    {
        _output = output;
    }

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
    public Task TooGoodToDoubleTakePath_TooGoodIsCharged_ThenNoDoubleOnReturnIsPractice() =>
        TooGoodToDoubleTakePathAsync(TooGoodConditions.AsTheFolderComes);

    /// <summary>
    /// <see cref="TooGoodToDoubleTakePath_TooGoodIsCharged_ThenNoDoubleOnReturnIsPractice"/>'s
    /// scenario, unchanged, under the condition that made it fail on umbrella
    /// CI (halheinrich/backgammon#333), in both orders: the action row arrives
    /// only after the scenario has begun waiting for its first problem.
    ///
    /// <para>
    /// There, the scenario read the Too good pill's count straight after
    /// Start, before the quiz page had any row; it read 0, took the branch
    /// for the other problem coming first, skipped Too good, and failed
    /// further on. Reproduced with the row-fit module held across that read
    /// and Too good served first, it failed that way every time. Now the
    /// scenario waits for the first problem to land before it branches, and
    /// every navigation waits for the problem it goes to. This holds the
    /// module (<see cref="RowFitModuleHold"/>) until that first wait has been
    /// issued, so the row cannot exist before the scenario is waiting for it
    /// on any runner, and serves the folder in each order
    /// (<see cref="E2eTestBase.PickFixturesInOrderAsync"/>), so each branch
    /// is taken, on every operating system. The scenario checks that its
    /// first problem is the one ordered first; the hold, that it held the
    /// module and let it through only after that checkpoint.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(TooGoodPosition.First)]
    [InlineData(TooGoodPosition.Second)]
    public Task TooGoodToDoubleTakePath_WithTheRowArrivingOnlyOnceTheScenarioWaitsForIt(TooGoodPosition position) =>
        TooGoodToDoubleTakePathAsync(TooGoodConditions.HeldAndOrdered(position));

    /// <summary>Where the Too good problem stands in the folder the scenario picks.</summary>
    public enum TooGoodPosition
    {
        /// <summary>Served first: the scenario keeps it.</summary>
        First,

        /// <summary>Served second: the scenario skips the other problem to reach it.</summary>
        Second,
    }

    /// <summary>
    /// How a run of the Too good scenario is set up: the order its folder
    /// reaches the app in, null for the browser's own enumeration, and
    /// whether the row-fit module is held until the scenario is waiting for
    /// its first problem.
    /// </summary>
    private sealed record TooGoodConditions(TooGoodPosition? Order, bool RowHeldUntilWaitedFor)
    {
        /// <summary>The smoke: the folder as the browser enumerates it, nothing held.</summary>
        internal static readonly TooGoodConditions AsTheFolderComes = new(Order: null, RowHeldUntilWaitedFor: false);

        /// <summary>The proof's condition, with Too good at <paramref name="position"/>.</summary>
        internal static TooGoodConditions HeldAndOrdered(TooGoodPosition position) =>
            new(position, RowHeldUntilWaitedFor: true);
    }

    private async Task TooGoodToDoubleTakePathAsync(TooGoodConditions conditions)
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
        // from; its fourth answer reads No double / Pass, which tells the two
        // apart. Which of the two the source serves first is not this
        // scenario's business, so it reaches Too good and returns to it either
        // way — and it acts only once the problem it is on has landed
        // (ExpectCubeProblemAsync), never on a read taken while the page may
        // still be on its way (halheinrich/backgammon#333).
        //
        // The evidence prints (QuizPageEvidence) are halheinrich/backgammon#333's
        // observability, kept for the umbrella CI run that reads this
        // correction back: they show, on a pass as on a failure, which problem
        // came first, where the first row arrived against the scenario's first
        // wait, and how each navigation landed. They observe; nothing below
        // decides or asserts on them.
        var evidence = new QuizPageEvidence(Page, _output);
        RowFitModuleHold? hold = null;
        try
        {
            await BootHomeAsync();
            await DisableMaximizeAsync();   // the score panel stays on screen while answering; it names the problem
            await BootHomeAsync();
            switch (conditions.Order)
            {
                case null:
                    await PickFixturesAsync(TooGoodTakeFixture, CubeFixture);
                    break;
                case TooGoodPosition.First:
                    await PickFixturesInOrderAsync(TooGoodTakeFixture, CubeFixture);
                    break;
                case TooGoodPosition.Second:
                    await PickFixturesInOrderAsync(CubeFixture, TooGoodTakeFixture);
                    break;
            }
            await ApplyFilterAsync();
            await evidence.RecordAsync();
            if (conditions.RowHeldUntilWaitedFor)
                hold = await RowFitModuleHold.InstallAsync(Page);
            await StartQuizAsync();

            // The branch: which problem the run served first, once it has landed.
            var first = await evidence.ReadAsync(
                "the first problem's fourth answer, once problem 1 has landed",
                () => ExpectCubeProblemAsync(1, () => hold?.Checkpoint("the scenario is waiting for problem 1 to land")));
            // The order this run was given, checked before anything relies on
            // it: the folder's order is the browser's enumeration, which the
            // proof replaces (PickFixturesInOrderAsync), and a replacement that
            // did not hold on some operating system fails here, naming what
            // was served, never later as a scenario that went the other way.
            if (conditions.Order is { } order)
            {
                var ordered = order == TooGoodPosition.First ? ExpectedText.TooGoodPill : ExpectedText.NoDoublePassPill;
                Assert.True(first == ordered,
                    $"the folder was ordered with the {ordered} problem first, but problem 1 served the {first} "
                    + "problem: the order control did not hold");
            }
            var tooGoodFirst = first == ExpectedText.TooGoodPill;
            var tooGood = tooGoodFirst ? 1 : 2;
            if (!tooGoodFirst)
            {
                await evidence.NavigateAsync("Skip", () => NavButton(ExpectedText.SkipButton).ClickAsync());
                Assert.Equal(ExpectedText.TooGoodPill, await ExpectCubeProblemAsync(tooGood));
            }

            // All four answers are offered, the fourth labelled Too good.
            await Expect(CubeAnswers).ToHaveCountAsync(4);
            await Expect(CubePill(ExpectedText.TooGoodPill)).ToBeVisibleAsync();
            await Expect(CubePill(ExpectedText.NoDoublePassPill)).ToHaveCountAsync(0);

            await AnswerCubeAsync(ExpectedText.TooGoodPill);

            await Expect(VerdictBand).ToHaveTextAsync("Not best — Too good lost 0.7992. Best: No double.");
            await Expect(VerdictBand).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("alert-danger"));
            await Expect(Page.GetByText(ExpectedText.Submitted(1))).ToBeVisibleAsync();

            // Away and back: from the first problem ▶ then ◀, from the second
            // ◀ then ▶. Each landing is awaited before the next gesture: a
            // gesture that met the page still on the problem it was leaving
            // would act on that one.
            var other = tooGoodFirst ? 2 : 1;
            var (away, back) = tooGoodFirst
                ? (ExpectedText.NextButton, ExpectedText.BackButton)
                : (ExpectedText.BackButton, ExpectedText.NextButton);
            await evidence.NavigateAsync(away, () => NavButton(away).ClickAsync());
            Assert.Equal(ExpectedText.NoDoublePassPill, await ExpectCubeProblemAsync(other));
            await evidence.NavigateAsync(back, () => NavButton(back).ClickAsync());
            Assert.Equal(ExpectedText.TooGoodPill, await ExpectCubeProblemAsync(tooGood));

            await AnswerCubeNoDoubleAsync();

            await Expect(VerdictBand).ToHaveTextAsync(
                ExpectedText.PracticePrefix + ExpectedText.CubeVerdictNoDoubleCorrect);
            await Expect(VerdictBand).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("alert-success"));
            await Expect(Page.GetByText(ExpectedText.Submitted(1))).ToBeVisibleAsync();   // the score did not move

            if (hold?.Unmet() is { } unmet)
                Assert.Fail($"the scenario passed, but not under the condition it claims: {unmet}");
        }
        finally
        {
            foreach (var (at, what) in hold?.Events() ?? [])
                evidence.Note(at, what);
            await evidence.PrintOnTheWayOutAsync();
        }
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
        // row holds it (1242 px, far more than the full form needs beside the
        // tail; the short form is CubeLabelsTests').
        await Page.SetViewportSizeAsync(1600, 900);
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();

        var pills = Page.Locator(".action-row .bg-cube-actions").GetByRole(AriaRole.Radio);
        await Expect(pills).ToHaveCountAsync(4);
        await Expect(pills.Nth(0)).ToHaveAccessibleNameAsync(ExpectedText.NoDoublePill);
        await Expect(pills.Nth(1)).ToHaveAccessibleNameAsync(ExpectedText.DoubleTakePill);
        await Expect(pills.Nth(2)).ToHaveAccessibleNameAsync(ExpectedText.DoublePassPill);
        await Expect(pills.Nth(3)).ToHaveAccessibleNameAsync(ExpectedText.NoDoublePassPill);
        await Expect(Page.Locator(".action-row .bg-cube-actions label").Nth(3)).ToHaveTextAsync(ExpectedText.NoDoublePassPill);
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
