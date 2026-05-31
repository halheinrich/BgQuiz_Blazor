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
    //  SubmitPlayAsync — scoring
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitPlayAsync_BestPlay_Scores_IsCorrect()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05));
        await c.StartAsync(new FilterConfig());

        await c.SubmitPlayAsync(BestPlay());

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
    public async Task SubmitPlayAsync_NonBestPlay_Scores_NotCorrect()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05));
        await c.StartAsync(new FilterConfig());

        await c.SubmitPlayAsync(AltPlay());

        Assert.Single(c.History);
        Assert.False(c.History[0].IsCorrect);
        Assert.Equal(0.05, c.History[0].EquityLoss, 6);
        Assert.Equal(1, c.History[0].MatchedCandidateIndex);
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(0, c.Score.Total.Correct);
        Assert.Equal(0.05, c.Score.Total.TotalEquityLoss, 6);
    }

    [Fact]
    public async Task SubmitPlayAsync_OffList_CountsAsSkip_NoHistoryEntry()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());

        await c.SubmitPlayAsync(UnknownPlay());

        Assert.Empty(c.History);
        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Equal(1, c.SkippedCount);
    }

    [Fact]
    public async Task SubmitPlayAsync_AdvancesToNext()
    {
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(d1, d2);
        await c.StartAsync(new FilterConfig());
        Assert.Same(d1, c.Current);

        await c.SubmitPlayAsync(BestPlay());

        Assert.Same(d2, c.Current);
        Assert.False(c.IsFinished);
    }

    [Fact]
    public async Task SubmitPlayAsync_LastProblem_FlipsIsFinished()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());

        await c.SubmitPlayAsync(BestPlay());

        Assert.True(c.IsFinished);
        Assert.Null(c.Current);
    }

    [Fact]
    public async Task SubmitPlayAsync_BeforeStart_NoOp()
    {
        var c = Make();

        await c.SubmitPlayAsync(BestPlay());

        Assert.Empty(c.History);
        Assert.Equal(0, c.SkippedCount);
    }

    [Fact]
    public async Task SubmitPlayAsync_AfterFinish_NoOp()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        await c.SubmitPlayAsync(BestPlay()); // exhausts

        var historyBefore = c.History.Count;
        await c.SubmitPlayAsync(BestPlay());

        Assert.Equal(historyBefore, c.History.Count);
    }

    [Fact]
    public async Task SubmitPlayAsync_AccumulatesEquityLossAcrossMultiple()
    {
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.10),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.30));
        await c.StartAsync(new FilterConfig());

        await c.SubmitPlayAsync(AltPlay());
        await c.SubmitPlayAsync(AltPlay());

        Assert.Equal(2, c.Score.Total.Submitted);
        Assert.Equal(0, c.Score.Total.Correct);
        Assert.Equal(0.40, c.Score.Total.TotalEquityLoss, 6);
        Assert.Equal(0.20, c.Score.Total.AverageEquityLoss, 6);
    }

    // -----------------------------------------------------------------------
    //  SubmitCubeActionAsync — scoring
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitCubeActionAsync_BestAnswer_ScoresBothHalvesCorrect()
    {
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig());

        await c.SubmitCubeActionAsync(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));

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
    public async Task SubmitCubeActionAsync_WrongAnswer_ScoresPerHalfLoss()
    {
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig());

        await c.SubmitCubeActionAsync(new CubeDecisionPair(CubeAction.NoDouble, CubeAction.Pass));

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
    public async Task SubmitCubeActionAsync_AdvancesToNext()
    {
        var d1 = TestFixtures.CubeDecision();
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(d1, d2);
        await c.StartAsync(new FilterConfig());
        Assert.Same(d1, c.Current);

        await c.SubmitCubeActionAsync(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));

        Assert.Same(d2, c.Current);
        Assert.False(c.IsFinished);
    }

    [Fact]
    public async Task SubmitCubeActionAsync_LastProblem_FlipsIsFinished()
    {
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig());

        await c.SubmitCubeActionAsync(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));

        Assert.True(c.IsFinished);
        Assert.Null(c.Current);
    }

    [Fact]
    public async Task SubmitCubeActionAsync_BeforeStart_NoOp()
    {
        var c = Make();

        await c.SubmitCubeActionAsync(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));

        Assert.Empty(c.CubeHistory);
        Assert.Equal(QuizScore.Empty, c.Score);
    }

    [Fact]
    public async Task SubmitCubeActionAsync_AfterFinish_NoOp()
    {
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig());
        await c.SubmitCubeActionAsync(new CubeDecisionPair(CubeAction.Double, CubeAction.Take)); // exhausts
        Assert.True(c.IsFinished);

        var countBefore = c.CubeHistory.Count;
        await c.SubmitCubeActionAsync(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));

        Assert.Equal(countBefore, c.CubeHistory.Count);
    }

    [Fact]
    public async Task RestartAsync_ClearsCubeHistory()
    {
        var c = Make(
            TestFixtures.CubeDecision(),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        await c.SubmitCubeActionAsync(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));
        Assert.Single(c.CubeHistory);

        await c.RestartAsync();

        Assert.Empty(c.CubeHistory);
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
        await c.SubmitPlayAsync(BestPlay()); // exhaust
        Assert.True(c.IsFinished);

        await c.SkipCurrentAsync();

        Assert.Equal(0, c.SkippedCount);
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
        await c.SubmitPlayAsync(BestPlay());
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
    public async Task StateChanged_FiresOnEachAdvance()
    {
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        var fires = 0;
        c.StateChanged += () => fires++;

        await c.StartAsync(new FilterConfig()); // 1
        await c.SubmitPlayAsync(BestPlay());         // 2
        await c.SkipCurrentAsync();                  // 3

        Assert.Equal(3, fires);
    }
}
