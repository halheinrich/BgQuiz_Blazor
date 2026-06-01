using BgDataTypes_Lib;
using BgGame_Lib;
using BgQuiz_Blazor.Quiz;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

public class QuizControllerTests
{
    private static QuizController Make(params BgDecisionData[] items)
    {
        var fake = new FakeProblemSetSource(items);
        return new QuizController(_ => fake);
    }

    /// <summary>
    /// Constructs a controller whose source factory captures the
    /// <see cref="DecisionFilterSet"/> it receives, exposed via
    /// <paramref name="captured"/>. Lets tests assert what the controller
    /// hands the source after materializing the user's <c>FilterConfig</c>.
    /// </summary>
    private static QuizController MakeCapturing(
        out Func<DecisionFilterSet?> captured, params BgDecisionData[] items)
    {
        var fake = new FakeProblemSetSource(items);
        DecisionFilterSet? holder = null;
        captured = () => holder;
        return new QuizController(set => { holder = set; return fake; });
    }

    private static Play BestPlay() => TestFixtures.MakePlay((8, 5), (8, 5));
    private static Play AltPlay() => TestFixtures.MakePlay((13, 11), (11, 8));
    private static Play UnknownPlay() => TestFixtures.MakePlay((24, 23), (24, 22));

    // -----------------------------------------------------------------------
    //  Construction
    // -----------------------------------------------------------------------

    [Fact]
    public void Ctor_NullFactory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new QuizController(null!));
    }

    [Fact]
    public void Initial_State_IsEmpty()
    {
        var c = Make();
        Assert.False(c.HasStarted);
        Assert.False(c.IsFinished);
        Assert.Null(c.Current);
        Assert.Null(c.Name);
        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Empty(c.History);
        Assert.Equal(0, c.SkippedCount);
    }

    // -----------------------------------------------------------------------
    //  StartAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_NullFilters_Throws()
    {
        var c = Make();
        await Assert.ThrowsAsync<ArgumentNullException>(() => c.StartAsync(null!));
    }

    [Fact]
    public async Task StartAsync_AdvancesToFirstProblem()
    {
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(d);

        await c.StartAsync(new FilterConfig());

        Assert.True(c.HasStarted);
        Assert.False(c.IsFinished);
        Assert.NotNull(c.Current);
        Assert.Same(d, c.Current);
    }

    [Fact]
    public async Task StartAsync_DoesNotForceCheckerPlaysOnly_CubeDataFlows()
    {
        // Regression: the controller previously appended a CheckerPlaysOnly
        // DecisionTypeFilter that silently dropped every cube decision. After
        // the lift, the user's FilterConfig.DecisionType governs — a default
        // config (DecisionType.Both) adds no decision-type filter, so cube data
        // flows. Capture the pipeline the controller hands the source factory
        // and assert it admits cube data.
        var c = MakeCapturing(out var captured,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));

        await c.StartAsync(new FilterConfig());

        var pipeline = captured();
        Assert.NotNull(pipeline);
        Assert.True(pipeline.Matches(TestFixtures.CubeDecision()));
    }

    [Fact]
    public async Task StartAsync_UserCubeOnly_AdmitsCubeRejectsChecker()
    {
        // The user's DecisionType choice governs in both directions: a CubeOnly
        // config admits cube decisions and rejects checker plays. Confirms the
        // lift handed control to the user's filter rather than dropping the
        // policy entirely.
        var c = MakeCapturing(out var captured, TestFixtures.CubeDecision());

        await c.StartAsync(new FilterConfig { DecisionType = DecisionTypeOption.CubeOnly });

        var pipeline = captured();
        Assert.NotNull(pipeline);
        Assert.True(pipeline.Matches(TestFixtures.CubeDecision()));
        Assert.False(pipeline.Matches(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay())));
    }

    [Fact]
    public async Task StartAsync_CubeDecision_IsSurfacedNotAutoSkipped()
    {
        // A cube decision carries Dice [0, 0]; without the IsCube guard in
        // IsPassPosition that hits the no-legal-play sentinel and is silently
        // auto-skipped, making the whole cube feature invisible. The guard must
        // surface the cube decision as the current problem.
        var cube = TestFixtures.CubeDecision();
        var c = Make(cube);

        await c.StartAsync(new FilterConfig());

        Assert.Same(cube, c.Current);
        Assert.False(c.IsFinished);
        Assert.Equal(0, c.SkippedCount);
    }

    [Fact]
    public async Task StartAsync_DoesNotMutateCallerConfig()
    {
        // FilterConfig is a wire DTO — the controller materializes via
        // FilterConfig.Build() and owns the resulting set. The caller's
        // config must be untouched by StartAsync.
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        var cfg = new FilterConfig
        {
            Players = ["Alice"],
            DecisionType = DecisionTypeOption.Both,
        };

        await c.StartAsync(cfg);

        Assert.Equal(["Alice"], cfg.Players);
        Assert.Equal(DecisionTypeOption.Both, cfg.DecisionType);
    }

    [Fact]
    public async Task StartAsync_PassThenScoring_AutoSkipsPass()
    {
        var pass = TestFixtures.PassDecision();
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(pass, d);

        await c.StartAsync(new FilterConfig());

        // Pass auto-skipped silently — counts on user-driven skips only.
        Assert.Equal(0, c.SkippedCount);
        Assert.Same(d, c.Current);
    }

    [Fact]
    public async Task StartAsync_FiresStateChanged()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        var fired = 0;
        c.StateChanged += () => fired++;

        await c.StartAsync(new FilterConfig());

        Assert.True(fired >= 1);
    }

    [Fact]
    public async Task StartAsync_EmptySource_FlipsIsFinishedImmediately()
    {
        var c = Make();
        await c.StartAsync(new FilterConfig());

        Assert.True(c.HasStarted);
        Assert.True(c.IsFinished);
        Assert.Null(c.Current);
    }

    // -----------------------------------------------------------------------
    //  SubmitPlay — scoring (enters review; ContinueAsync advances)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitPlay_BestPlay_Scores_IsCorrect()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05));
        await c.StartAsync(new FilterConfig());

        c.SubmitPlay(BestPlay());

        Assert.Single(c.History);
        var first = c.History[0];
        Assert.True(first.IsCorrect);
        Assert.Equal(0.0, first.EquityLoss);
        Assert.Equal(0, first.MatchedCandidateIndex);
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(1, c.Score.Total.Correct);
        Assert.Equal(0.0, c.Score.Total.TotalEquityLoss);
    }

    [Fact]
    public async Task SubmitPlay_NonBestPlay_Scores_NotCorrect()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05));
        await c.StartAsync(new FilterConfig());

        c.SubmitPlay(AltPlay());

        Assert.Single(c.History);
        Assert.False(c.History[0].IsCorrect);
        Assert.Equal(0.05, c.History[0].EquityLoss, 6);
        Assert.Equal(1, c.History[0].MatchedCandidateIndex);
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(0, c.Score.Total.Correct);
        Assert.Equal(0.05, c.Score.Total.TotalEquityLoss, 6);
    }

    [Fact]
    public async Task SubmitPlay_SetsPlayReview_DoesNotAdvance()
    {
        // Submit scores and enters the review state without advancing: Current
        // still points at the answered problem and Review carries the matched
        // candidate index that drives the solution diagram's UserPlayIndex marker.
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05);
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(d1, d2);
        await c.StartAsync(new FilterConfig());

        c.SubmitPlay(AltPlay());

        Assert.Same(d1, c.Current); // unchanged — no advance
        Assert.False(c.IsFinished);
        var review = Assert.IsType<ProblemReview.Play>(c.Review);
        Assert.Equal(1, review.UserPlayIndex);
        Assert.False(review.OffList);
        Assert.False(review.IsCorrect);
        Assert.Equal(0.05, review.EquityLoss, 6);
    }

    [Fact]
    public async Task SubmitPlay_OffList_CountsAsSkip_SetsOffListReview()
    {
        // Off-list: counted as a skip (no History entry, score unchanged), but a
        // Review is still produced — OffList true, index -1 (no marker drawn) —
        // so the user sees the best play on the solution diagram.
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());

        c.SubmitPlay(UnknownPlay());

        Assert.Empty(c.History);
        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Equal(1, c.SkippedCount);
        var review = Assert.IsType<ProblemReview.Play>(c.Review);
        Assert.True(review.OffList);
        Assert.Equal(-1, review.UserPlayIndex);
    }

    [Fact]
    public async Task ContinueAsync_AdvancesAndClearsReview()
    {
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(d1, d2);
        await c.StartAsync(new FilterConfig());
        c.SubmitPlay(BestPlay());
        Assert.NotNull(c.Review);
        Assert.Same(d1, c.Current);

        await c.ContinueAsync();

        Assert.Null(c.Review);
        Assert.Same(d2, c.Current);
        Assert.False(c.IsFinished);
    }

    [Fact]
    public async Task ContinueAsync_OutsideReview_NoOp()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        Assert.Null(c.Review);

        await c.ContinueAsync(); // no Review set — must not advance

        Assert.NotNull(c.Current);
        Assert.False(c.IsFinished);
    }

    [Fact]
    public async Task SubmitPlay_WhileReviewSet_NoOp()
    {
        // Once in review, a second Submit must be ignored — Continue is the only
        // way forward. Guards against a double-click double-scoring the problem.
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05));
        await c.StartAsync(new FilterConfig());
        c.SubmitPlay(BestPlay());
        var reviewBefore = c.Review;

        c.SubmitPlay(AltPlay());

        Assert.Same(reviewBefore, c.Review); // unchanged
        Assert.Single(c.History);
        Assert.Equal(1, c.Score.Total.Submitted);
    }

    [Fact]
    public async Task SubmitThenContinue_LastProblem_FlipsIsFinished()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());

        c.SubmitPlay(BestPlay());
        Assert.False(c.IsFinished); // review first — not yet advanced
        Assert.NotNull(c.Current);

        await c.ContinueAsync();

        Assert.True(c.IsFinished);
        Assert.Null(c.Current);
        Assert.Null(c.Review);
    }

    [Fact]
    public async Task SubmitPlay_BeforeStart_NoOp()
    {
        var c = Make();

        c.SubmitPlay(BestPlay());

        Assert.Empty(c.History);
        Assert.Equal(0, c.SkippedCount);
        Assert.Null(c.Review);
    }

    [Fact]
    public async Task SubmitPlay_AfterFinish_NoOp()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync(); // exhausts → IsFinished
        Assert.True(c.IsFinished);

        var historyBefore = c.History.Count;
        c.SubmitPlay(BestPlay());

        Assert.Equal(historyBefore, c.History.Count);
    }

    [Fact]
    public async Task SubmitPlay_AccumulatesEquityLossAcrossMultiple()
    {
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.10),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.30));
        await c.StartAsync(new FilterConfig());

        c.SubmitPlay(AltPlay());
        await c.ContinueAsync();
        c.SubmitPlay(AltPlay());
        await c.ContinueAsync();

        Assert.Equal(2, c.Score.Total.Submitted);
        Assert.Equal(0, c.Score.Total.Correct);
        Assert.Equal(0.40, c.Score.Total.TotalEquityLoss, 6);
        Assert.Equal(0.20, c.Score.Total.AverageEquityLoss, 6);
    }

    // -----------------------------------------------------------------------
    //  SubmitCubeAction — scoring (enters review; ContinueAsync advances)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitCubeAction_BestAnswer_ScoresBothHalvesCorrect()
    {
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig());

        c.SubmitCubeAction(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));

        Assert.Single(c.CubeHistory);
        var sub = c.CubeHistory[0];
        Assert.True(sub.DoublerCorrect);
        Assert.True(sub.TakerCorrect);
        Assert.Equal(0.0, sub.DoublerEquityLoss, 6);
        Assert.Equal(0.0, sub.TakerEquityLoss, 6);

        // One Double + one Take decision folded into their own segments; the
        // play segment is untouched.
        Assert.Equal(1, c.Score.DoubleDecisions.Submitted);
        Assert.Equal(1, c.Score.DoubleDecisions.Correct);
        Assert.Equal(1, c.Score.TakeDecisions.Submitted);
        Assert.Equal(1, c.Score.TakeDecisions.Correct);
        Assert.Equal(0, c.Score.PlayDecisions.Submitted);
        Assert.Equal(2, c.Score.Total.Submitted);
        Assert.Empty(c.History);
    }

    [Fact]
    public async Task SubmitCubeAction_WrongAnswer_ScoresPerHalfLoss()
    {
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig());

        c.SubmitCubeAction(new CubeDecisionPair(CubeAction.NoDouble, CubeAction.Pass));

        var sub = c.CubeHistory[0];
        Assert.False(sub.DoublerCorrect);
        Assert.False(sub.TakerCorrect);
        Assert.Equal(0.20, sub.DoublerEquityLoss, 6);
        Assert.Equal(0.30, sub.TakerEquityLoss, 6);

        Assert.Equal(0, c.Score.DoubleDecisions.Correct);
        Assert.Equal(0.20, c.Score.DoubleDecisions.TotalEquityLoss, 6);
        Assert.Equal(0, c.Score.TakeDecisions.Correct);
        Assert.Equal(0.30, c.Score.TakeDecisions.TotalEquityLoss, 6);
    }

    [Fact]
    public async Task SubmitCubeAction_SetsCubeReview_CarryingBothErrors_DoesNotAdvance()
    {
        // Submit scores and enters review without advancing; Review.Cube carries
        // the two per-half equity losses that drive the solution diagram's
        // "Actual" banner.
        var d1 = TestFixtures.CubeDecision();
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(d1, d2);
        await c.StartAsync(new FilterConfig());

        c.SubmitCubeAction(new CubeDecisionPair(CubeAction.NoDouble, CubeAction.Pass));

        Assert.Same(d1, c.Current); // unchanged — no advance
        Assert.False(c.IsFinished);
        var review = Assert.IsType<ProblemReview.Cube>(c.Review);
        Assert.Equal(0.20, review.DoublerEquityLoss, 6);
        Assert.Equal(0.30, review.TakerEquityLoss, 6);
        Assert.False(review.DoublerCorrect);
        Assert.False(review.TakerCorrect);
    }

    [Fact]
    public async Task ContinueAsync_AfterCubeSubmit_Advances()
    {
        var d1 = TestFixtures.CubeDecision();
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(d1, d2);
        await c.StartAsync(new FilterConfig());
        c.SubmitCubeAction(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));
        Assert.Same(d1, c.Current);

        await c.ContinueAsync();

        Assert.Null(c.Review);
        Assert.Same(d2, c.Current);
        Assert.False(c.IsFinished);
    }

    [Fact]
    public async Task SubmitCubeAction_WhileReviewSet_NoOp()
    {
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig());
        c.SubmitCubeAction(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));
        var reviewBefore = c.Review;

        c.SubmitCubeAction(new CubeDecisionPair(CubeAction.NoDouble, CubeAction.Pass));

        Assert.Same(reviewBefore, c.Review);
        Assert.Single(c.CubeHistory);
    }

    [Fact]
    public async Task SubmitCubeThenContinue_LastProblem_FlipsIsFinished()
    {
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig());

        c.SubmitCubeAction(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));
        Assert.False(c.IsFinished); // review first

        await c.ContinueAsync();

        Assert.True(c.IsFinished);
        Assert.Null(c.Current);
    }

    [Fact]
    public async Task SubmitCubeAction_BeforeStart_NoOp()
    {
        var c = Make();

        c.SubmitCubeAction(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));

        Assert.Empty(c.CubeHistory);
        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Null(c.Review);
    }

    [Fact]
    public async Task SubmitCubeAction_AfterFinish_NoOp()
    {
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig());
        c.SubmitCubeAction(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));
        await c.ContinueAsync(); // exhausts
        Assert.True(c.IsFinished);

        var countBefore = c.CubeHistory.Count;
        c.SubmitCubeAction(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));

        Assert.Equal(countBefore, c.CubeHistory.Count);
    }

    [Fact]
    public async Task RestartAsync_ClearsCubeHistoryAndReview()
    {
        var c = Make(
            TestFixtures.CubeDecision(),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        c.SubmitCubeAction(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));
        Assert.Single(c.CubeHistory);
        Assert.NotNull(c.Review);

        await c.RestartAsync();

        Assert.Empty(c.CubeHistory);
        Assert.Null(c.Review);
    }

    // -----------------------------------------------------------------------
    //  SkipCurrentAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SkipCurrentAsync_IncrementsAndAdvances()
    {
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(d1, d2);
        await c.StartAsync(new FilterConfig());

        await c.SkipCurrentAsync();

        Assert.Equal(1, c.SkippedCount);
        Assert.Same(d2, c.Current);
    }

    [Fact]
    public async Task SkipCurrentAsync_BeforeStart_NoOp()
    {
        var c = Make();
        await c.SkipCurrentAsync();
        Assert.Equal(0, c.SkippedCount);
    }

    [Fact]
    public async Task SkipCurrentAsync_AfterFinish_NoOp()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync(); // exhaust
        Assert.True(c.IsFinished);

        await c.SkipCurrentAsync();

        Assert.Equal(0, c.SkippedCount);
    }

    [Fact]
    public async Task SkipCurrentAsync_WhileReviewSet_NoOp()
    {
        // Skip bypasses review, but only from the answering state. While a
        // Review is showing, Continue is the only exit — Skip must not advance.
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(d1, d2);
        await c.StartAsync(new FilterConfig());
        c.SubmitPlay(BestPlay());
        Assert.NotNull(c.Review);

        await c.SkipCurrentAsync();

        Assert.Equal(0, c.SkippedCount);
        Assert.Same(d1, c.Current); // not advanced
        Assert.NotNull(c.Review);
    }

    // -----------------------------------------------------------------------
    //  RestartAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RestartAsync_BeforeStart_NoOp()
    {
        var c = Make();
        await c.RestartAsync();
        Assert.False(c.HasStarted);
    }

    [Fact]
    public async Task RestartAsync_ResetsScoreAndHistory()
    {
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();
        await c.SkipCurrentAsync();
        Assert.Single(c.History);
        Assert.Equal(1, c.SkippedCount);

        await c.RestartAsync();

        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Empty(c.History);
        Assert.Equal(0, c.SkippedCount);
        Assert.False(c.IsFinished);
        Assert.NotNull(c.Current);
    }

    [Fact]
    public async Task RestartAsync_RespawnsSourceFromFactory()
    {
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var fake = new FakeProblemSetSource([d]);
        var c = new QuizController(_ => fake);

        await c.StartAsync(new FilterConfig());
        Assert.Equal(1, fake.EnumerateCallCount);

        await c.RestartAsync();
        Assert.Equal(2, fake.EnumerateCallCount);
    }

    // -----------------------------------------------------------------------
    //  StateChanged firing
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StateChanged_FiresOnSubmitAndEachAdvance()
    {
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        var fires = 0;
        c.StateChanged += () => fires++;

        await c.StartAsync(new FilterConfig()); // 1 — advance to first
        c.SubmitPlay(BestPlay());               // 2 — score + enter review
        await c.ContinueAsync();                // 3 — advance to second
        await c.SkipCurrentAsync();             // 4 — skip + advance (exhausts)

        Assert.Equal(4, fires);
    }
}
