using BgDataTypes_Lib;
using BgGame_Lib;
using BgQuiz_Blazor.Quiz;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

public class QuizControllerTests
{
    private static QuizController Make(params BgDecisionData[] items)
    {
        var fake = new FakeProblemSetSource(items);
        return new QuizController(_ => fake);
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

        await c.StartAsync(new DecisionFilterSet());

        Assert.True(c.HasStarted);
        Assert.False(c.IsFinished);
        Assert.NotNull(c.Current);
        Assert.Same(d, c.Current);
    }

    [Fact]
    public async Task StartAsync_AppendsCheckerPlaysOnlyFilter()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        var filters = new DecisionFilterSet();
        // Pre-condition: a fresh empty set passes any data, including a cube
        // decision. Behavioral assertion below verifies StartAsync mutates the
        // caller's set such that cube data is now rejected.
        var cubeData = new BgDecisionData
        {
            Position = new PositionData { Mop = TestFixtures.StandardMop() },
            Decision = new DecisionData { IsCube = true },
            Descriptive = new DescriptiveData { OnRollName = "Alice", OpponentName = "Bob" },
        };
        Assert.True(filters.Matches(cubeData));

        await c.StartAsync(filters);

        Assert.False(filters.Matches(cubeData));
    }

    [Fact]
    public async Task StartAsync_PassThenScoring_AutoSkipsPass()
    {
        var pass = TestFixtures.PassDecision();
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(pass, d);

        await c.StartAsync(new DecisionFilterSet());

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

        await c.StartAsync(new DecisionFilterSet());

        Assert.True(fired >= 1);
    }

    [Fact]
    public async Task StartAsync_EmptySource_FlipsIsFinishedImmediately()
    {
        var c = Make();
        await c.StartAsync(new DecisionFilterSet());

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
        await c.StartAsync(new DecisionFilterSet());

        await c.SubmitPlayAsync(BestPlay());

        Assert.Single(c.History);
        var first = c.History[0];
        Assert.True(first.IsCorrect);
        Assert.Equal(0.0, first.EquityLoss);
        Assert.Equal(0, first.MatchedCandidateIndex);
        Assert.Equal(1, c.Score.Submitted);
        Assert.Equal(1, c.Score.Correct);
        Assert.Equal(0.0, c.Score.TotalEquityLoss);
    }

    [Fact]
    public async Task SubmitPlayAsync_NonBestPlay_Scores_NotCorrect()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05));
        await c.StartAsync(new DecisionFilterSet());

        await c.SubmitPlayAsync(AltPlay());

        Assert.Single(c.History);
        Assert.False(c.History[0].IsCorrect);
        Assert.Equal(0.05, c.History[0].EquityLoss, 6);
        Assert.Equal(1, c.History[0].MatchedCandidateIndex);
        Assert.Equal(1, c.Score.Submitted);
        Assert.Equal(0, c.Score.Correct);
        Assert.Equal(0.05, c.Score.TotalEquityLoss, 6);
    }

    [Fact]
    public async Task SubmitPlayAsync_OffList_CountsAsSkip_NoHistoryEntry()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new DecisionFilterSet());

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
        await c.StartAsync(new DecisionFilterSet());
        Assert.Same(d1, c.Current);

        await c.SubmitPlayAsync(BestPlay());

        Assert.Same(d2, c.Current);
        Assert.False(c.IsFinished);
    }

    [Fact]
    public async Task SubmitPlayAsync_LastProblem_FlipsIsFinished()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new DecisionFilterSet());

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
        await c.StartAsync(new DecisionFilterSet());
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
        await c.StartAsync(new DecisionFilterSet());

        await c.SubmitPlayAsync(AltPlay());
        await c.SubmitPlayAsync(AltPlay());

        Assert.Equal(2, c.Score.Submitted);
        Assert.Equal(0, c.Score.Correct);
        Assert.Equal(0.40, c.Score.TotalEquityLoss, 6);
        Assert.Equal(0.20, c.Score.AverageEquityLoss, 6);
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
        await c.StartAsync(new DecisionFilterSet());

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
        await c.StartAsync(new DecisionFilterSet());
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
        await c.StartAsync(new DecisionFilterSet());
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

        await c.StartAsync(new DecisionFilterSet());
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

        await c.StartAsync(new DecisionFilterSet()); // 1
        await c.SubmitPlayAsync(BestPlay());         // 2
        await c.SkipCurrentAsync();                  // 3

        Assert.Equal(3, fires);
    }
}
