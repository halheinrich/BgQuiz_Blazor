using BgDataTypes_Lib;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

public class QuizControllerTests
{
    private static QuizController Make(params BgDecisionData[] items)
    {
        var fake = new FakeProblemSetSource(items);
        return new QuizController((_, _) => fake, new FakeDecisionStatsSink(), TimeProvider.System);
    }

    /// <summary>
    /// Constructs a controller over a recording stats sink, exposed via
    /// <paramref name="sink"/>. Lets tests assert exactly which submissions
    /// the controller folded into lifetime stats — and which flows folded
    /// nothing.
    /// </summary>
    private static QuizController MakeWithSink(
        out FakeDecisionStatsSink sink, params BgDecisionData[] items)
    {
        var fake = new FakeProblemSetSource(items);
        sink = new FakeDecisionStatsSink();
        return new QuizController((_, _) => fake, sink, TimeProvider.System);
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
        return new QuizController((set, _) => { holder = set; return fake; }, new FakeDecisionStatsSink(), TimeProvider.System);
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
        Assert.Throws<ArgumentNullException>(
            () => new QuizController(null!, new FakeDecisionStatsSink(), TimeProvider.System));
    }

    [Fact]
    public void Ctor_NullStatsSink_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new QuizController((_, _) => new FakeProblemSetSource([]), null!, TimeProvider.System));
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
        Assert.Equal(0, c.ProblemNumber);
        Assert.Null(c.ProblemCount);
        Assert.False(c.ActiveMixHasLength);
    }

    // -----------------------------------------------------------------------
    //  StartAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_NullFilters_Throws()
    {
        var c = Make();
        await Assert.ThrowsAsync<ArgumentNullException>(() => c.StartAsync(null!, QuizMix.Empty));
    }

    [Fact]
    public async Task StartAsync_AdvancesToFirstProblem()
    {
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(d);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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

        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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

        await c.StartAsync(new FilterConfig { DecisionType = DecisionTypeOption.CubeOnly }, QuizMix.Empty);

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

        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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

        await c.StartAsync(cfg, QuizMix.Empty);

        Assert.Equal(["Alice"], cfg.Players);
        Assert.Equal(DecisionTypeOption.Both, cfg.DecisionType);
    }

    [Fact]
    public async Task StartAsync_PassThenScoring_AutoSkipsPass()
    {
        var pass = TestFixtures.PassDecision();
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(pass, d);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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

        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        Assert.True(fired >= 1);
    }

    [Fact]
    public async Task StartAsync_EmptySource_FlipsIsFinishedImmediately()
    {
        var c = Make();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        c.SubmitPlay(AltPlay());
        await c.ContinueAsync();
        c.SubmitPlay(AltPlay());
        await c.ContinueAsync();

        Assert.Equal(2, c.Score.Total.Submitted);
        Assert.Equal(0, c.Score.Total.Correct);
        Assert.Equal(0.40, c.Score.Total.TotalEquityLoss, 6);
        Assert.Equal(0.20, c.Score.Total.AverageEquityLoss, 6);
    }

    [Fact]
    public async Task SubmitPlay_CarriesDecisionIdIntoHistory()
    {
        // Wire: the submitted play must carry the answered decision's stable
        // identity (BgDecisionData.Id) into History so a submission can be keyed
        // back to its problem. A distinctive per-problem id pins the actual
        // carry — a fix that merely compiled by passing a placeholder id would
        // fail this equality.
        var id = new XgpDecisionId("wire-play.xgp");
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: id));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        c.SubmitPlay(BestPlay());

        Assert.Equal(id, c.History[^1].DecisionId);
    }

    // -----------------------------------------------------------------------
    //  SubmitPlay — canonical play-equality matching (the play-equivalence arc)
    //  Order- and decomposition-insensitive, but fully hit-sensitive.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitPlay_DecomposedHops_MatchCombinedCandidate()
    {
        // The arc's acceptance pin — the exact user repro. The candidate list
        // carries the combined play 13/8; the user enters it as two clicks,
        // 13/10 then 10/8. Canonical Play equality is decomposition-insensitive,
        // so the decomposed submission matches the combined candidate and scores
        // as it rather than falling off-list (the bug this arc fixed: two-click
        // 13/8 was scored off-list even though 13/8 is on the candidate list).
        var combined = TestFixtures.MakePlay((13, 8));                // candidate: 13/8
        var other = TestFixtures.MakePlay((24, 22), (24, 23));
        var c = Make(TestFixtures.TwoChoiceDecision(combined, other, play2Loss: 0.05));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        c.SubmitPlay(TestFixtures.MakePlay((13, 10), (10, 8)));       // 13/10, 10/8

        Assert.Single(c.History);
        Assert.Equal(0, c.History[0].MatchedCandidateIndex);         // matched the combined candidate
        Assert.True(c.History[0].IsCorrect);
        Assert.Equal(0, c.SkippedCount);                             // scored, not skipped off-list
        var review = Assert.IsType<ProblemReview.Play>(c.Review);
        Assert.False(review.OffList);
        Assert.Equal(0, review.UserPlayIndex);
    }

    [Fact]
    public async Task SubmitPlay_DecomposedHopWithIntermediateHit_StaysOffListAgainstNonHittingCandidate()
    {
        // Hit-sensitivity negative — the guard rail on the match above. Canonical
        // equality preserves hits, so 13/10*/8 (a hit on the intermediate 10-pt)
        // is a genuinely different play from the non-hitting candidate 13/8 and
        // must stay off-list. Decomposition-insensitivity must not start
        // collapsing hitting and non-hitting plays together.
        var nonHitting = TestFixtures.MakePlay((13, 8));             // candidate 13/8, no hit
        var other = TestFixtures.MakePlay((24, 22), (24, 23));
        var c = Make(TestFixtures.TwoChoiceDecision(nonHitting, other));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        // 13/10*/8 — the intermediate 10-pt hit is the sign-encoded negative ToPt.
        c.SubmitPlay(TestFixtures.MakePlay((13, -10), (10, 8)));

        Assert.Empty(c.History);
        Assert.Equal(1, c.SkippedCount);
        Assert.Equal(QuizScore.Empty, c.Score);
        var review = Assert.IsType<ProblemReview.Play>(c.Review);
        Assert.True(review.OffList);
        Assert.Equal(-1, review.UserPlayIndex);
    }

    // -----------------------------------------------------------------------
    //  SubmitCubeAction — scoring (enters review; ContinueAsync advances)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitCubeAction_BestAnswer_ScoresBothHalvesCorrect()
    {
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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
    public async Task SubmitCubeAction_CarriesDecisionIdIntoCubeHistory()
    {
        // Wire: the cube submission must carry the answered decision's stable
        // identity (BgDecisionData.Id) into CubeHistory — the cube analog of
        // SubmitPlay_CarriesDecisionIdIntoHistory. Distinctive id pins the carry.
        var id = new XgpDecisionId("wire-cube.xgp");
        var c = Make(TestFixtures.CubeDecision(id: id));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        c.SubmitCubeAction(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));

        Assert.Equal(id, c.CubeHistory[^1].DecisionId);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitCubeAction(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));
        Assert.Single(c.CubeHistory);
        Assert.NotNull(c.Review);

        await c.RestartAsync();

        Assert.Empty(c.CubeHistory);
        Assert.Null(c.Review);
    }

    // -----------------------------------------------------------------------
    //  RedoAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RedoAsync_OutsideReview_NoOp()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        Assert.Null(c.Review);
        var current = c.Current;

        await c.RedoAsync();

        Assert.Same(current, c.Current);
        Assert.Empty(c.History);
        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Equal(0, c.SkippedCount);
        Assert.False(c.IsFinished);
    }

    [Fact]
    public async Task RedoAsync_AfterCorrectPlay_RevertsHistoryScoreAndReview()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var current = c.Current;

        c.SubmitPlay(BestPlay());
        Assert.Single(c.History);
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.NotNull(c.Review);

        await c.RedoAsync();

        Assert.Empty(c.History);
        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Equal(0, c.SkippedCount);
        Assert.Null(c.Review);
        Assert.Same(current, c.Current); // unchanged — same problem, answering state
        Assert.False(c.IsFinished);
    }

    [Fact]
    public async Task RedoAsync_AfterIncorrectPlay_RevertsHistoryScoreAndReview()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var current = c.Current;

        c.SubmitPlay(AltPlay());
        Assert.Single(c.History);
        Assert.Equal(0.05, c.Score.Total.TotalEquityLoss, 6);

        await c.RedoAsync();

        Assert.Empty(c.History);
        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Null(c.Review);
        Assert.Same(current, c.Current);
    }

    [Fact]
    public async Task RedoAsync_AfterOffListPlay_RevertsSkippedCountAndReview()
    {
        // Off-list submissions never add a History entry — Redo's inverse is
        // decrementing SkippedCount instead of popping History.
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var current = c.Current;

        c.SubmitPlay(UnknownPlay());
        Assert.Equal(1, c.SkippedCount);
        Assert.Empty(c.History);
        var reviewBefore = Assert.IsType<ProblemReview.Play>(c.Review);
        Assert.True(reviewBefore.OffList);

        await c.RedoAsync();

        Assert.Equal(0, c.SkippedCount);
        Assert.Empty(c.History);
        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Null(c.Review);
        Assert.Same(current, c.Current);
    }

    [Fact]
    public async Task RedoAsync_AfterCubeSubmission_RevertsCubeHistoryScoreAndReview()
    {
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var current = c.Current;

        c.SubmitCubeAction(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));
        Assert.Single(c.CubeHistory);
        Assert.Equal(2, c.Score.Total.Submitted); // one Double + one Take

        await c.RedoAsync();

        Assert.Empty(c.CubeHistory);
        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Null(c.Review);
        Assert.Same(current, c.Current);
    }

    [Fact]
    public async Task RedoAsync_AfterCubeSubmission_LeavesEarlierPlaySegmentIntact()
    {
        // Interleaved history: a play submitted first, then a cube position
        // reversed via Redo. Refolding must not disturb the play segment folded
        // in earlier — the edge case the refold-from-Empty approach depends on.
        var play = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05);
        var cube = TestFixtures.CubeDecision();
        var c = Make(play, cube);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        c.SubmitPlay(AltPlay()); // incorrect, 0.05 loss
        await c.ContinueAsync();
        Assert.Same(cube, c.Current);

        c.SubmitCubeAction(new CubeDecisionPair(CubeAction.NoDouble, CubeAction.Pass)); // wrong
        Assert.Single(c.CubeHistory);

        await c.RedoAsync();

        Assert.Empty(c.CubeHistory);
        Assert.Single(c.History); // play segment untouched
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
        Assert.Equal(0.05, c.Score.PlayDecisions.TotalEquityLoss, 6);
        Assert.Equal(0, c.Score.DoubleDecisions.Submitted);
        Assert.Equal(0, c.Score.TakeDecisions.Submitted);
        Assert.Same(cube, c.Current); // still on the cube problem, answering state
        Assert.Null(c.Review);
    }

    [Fact]
    public async Task RedoAsync_ThenResubmitDifferentAnswer_ScoresOnlyTheNewAnswer()
    {
        // The controller-level half of the "no contamination" guarantee: after
        // Redo, submitting a different answer to the same problem must not
        // leave any trace of the reversed attempt.
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        c.SubmitPlay(AltPlay()); // first attempt: incorrect
        await c.RedoAsync();
        c.SubmitPlay(BestPlay()); // second attempt: correct

        Assert.Single(c.History);
        Assert.True(c.History[0].IsCorrect);
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(1, c.Score.Total.Correct);
        Assert.Equal(0.0, c.Score.Total.TotalEquityLoss, 6);
    }

    [Fact]
    public async Task RedoAsync_FiresStateChanged()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());
        var fired = 0;
        c.StateChanged += () => fired++;

        await c.RedoAsync();

        Assert.True(fired >= 1);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());
        Assert.NotNull(c.Review);

        await c.SkipCurrentAsync();

        Assert.Equal(0, c.SkippedCount);
        Assert.Same(d1, c.Current); // not advanced
        Assert.NotNull(c.Review);
    }

    // -----------------------------------------------------------------------
    //  EndQuizAsync — the user's own exit from a run (issue #57)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EndQuizAsync_WhileAnswering_FinishesWithNothingShowing()
    {
        // The transition itself: the run ends where it stands, with problems
        // still unread in the source. IsFinished is what the page redirects on,
        // so this is the whole user-visible mechanism.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        Assert.NotNull(c.Current);

        await c.EndQuizAsync();

        Assert.True(c.IsFinished);
        Assert.Null(c.Current);
        Assert.Null(c.Review);
        Assert.True(c.HasStarted); // the finished run is still THE run — Done reads its score
    }

    [Fact]
    public async Task EndQuizAsync_WhileAnswering_KeepsAnsweredWorkAndCountsTheAbandonedProblemAsSkipped()
    {
        // The scoring half of the ruling. What was answered stands — that is the
        // partial score Done shows — and the problem showing when the user quit
        // is abandoned: it records no answer and takes the same non-scoring
        // outcome an explicit Skip records, so Done's "problems shown" still
        // counts a problem the user actually saw.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();          // problem 1 answered and finalized

        await c.EndQuizAsync();           // quitting on problem 2, unanswered

        Assert.Single(c.History);
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
        Assert.Equal(1, c.Score.PlayDecisions.Correct);
        Assert.Equal(1, c.SkippedCount);
    }

    [Fact]
    public async Task EndQuizAsync_FromReview_KeepsTheAnswerAndCountsNoSkip()
    {
        // Ending while reading a solution is a forward exit, not an
        // abandonment: the answer was submitted and scored, so it stays in the
        // score and nothing is counted as skipped on top of it.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());
        Assert.NotNull(c.Review);

        await c.EndQuizAsync();

        Assert.True(c.IsFinished);
        Assert.Null(c.Review);
        Assert.Single(c.History);
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
        Assert.Equal(0, c.SkippedCount);
    }

    [Fact]
    public async Task EndQuizAsync_FromReview_FoldsTheReviewedAnswerExactlyOnce()
    {
        // The invariant this method must not break: every answer visible on Done
        // has reached the lifetime record. Until End quiz existed that held only
        // because Continue was the sole route to Done — and Done tells the user
        // in as many words that nothing needs saving. So the reviewed answer
        // folds here exactly as Continue would fold it, through the one shared
        // fold path, and exactly once.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        c.SubmitPlay(AltPlay());
        Assert.Equal(0, sink.TotalFolds); // submit alone still folds nothing
        var submitted = c.History[^1];

        await c.EndQuizAsync();

        Assert.Same(submitted, Assert.Single(sink.Plays));
        Assert.Empty(sink.Cubes);
    }

    [Fact]
    public async Task EndQuizAsync_FromReview_OfAnOffListPlay_FoldsNothing()
    {
        // The off-list carve-out rides along unchanged: that submission added no
        // history entry, so there is nothing to fold — and the skip it already
        // recorded must not be double-counted by the abandon branch.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(UnknownPlay());
        Assert.Equal(1, c.SkippedCount);

        await c.EndQuizAsync();

        Assert.Equal(0, sink.TotalFolds);
        Assert.Equal(1, c.SkippedCount);
    }

    [Fact]
    public async Task EndQuizAsync_WhileAnswering_FoldsNothing()
    {
        // The abandoned problem was never answered, so it cannot reach the
        // lifetime record — the same rule skips and pass positions follow.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        await c.EndQuizAsync();

        Assert.Equal(0, sink.TotalFolds);
    }

    [Fact]
    public async Task EndQuizAsync_BeforeStart_NoOp()
    {
        var c = Make();

        await c.EndQuizAsync();

        Assert.False(c.HasStarted);
        Assert.False(c.IsFinished);
        Assert.Equal(0, c.SkippedCount);
    }

    [Fact]
    public async Task EndQuizAsync_AfterFinish_NoOp()
    {
        // A run that ended on its own has no current problem to abandon; a
        // second ending must not invent a skip for one.
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync(); // exhaust
        Assert.True(c.IsFinished);

        await c.EndQuizAsync();

        Assert.Equal(0, c.SkippedCount);
        Assert.Single(c.History);
    }

    [Fact]
    public async Task EndQuizAsync_TwiceInARow_CountsOneSkip()
    {
        // The same guard from the other side: the first call finished the run,
        // so the second finds nothing showing and does nothing.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        await c.EndQuizAsync();
        await c.EndQuizAsync();

        Assert.Equal(1, c.SkippedCount);
    }

    [Fact]
    public async Task EndQuizAsync_ThenRestart_ReplaysTheWholeSource()
    {
        // Ending releases the live enumerator early (the run is over and the
        // gate guarantees no MoveNextAsync is in flight). Restart must be
        // unaffected: it builds a fresh enumeration from the stored config, so
        // the ended-early run leaves no mark on the one that follows.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        await c.EndQuizAsync();

        await c.RestartAsync();

        Assert.False(c.IsFinished);
        Assert.NotNull(c.Current);
        Assert.Equal(0, c.SkippedCount);
        Assert.Equal(QuizScore.Empty, c.Score);
    }

    [Fact]
    public async Task EndQuizAsync_FiresStateChangedTwice_BusyThenDone()
    {
        // Gated like every other async transition, so pages observing IsBusy
        // see exactly [busy, not-busy] — and the second fire carries the
        // finished state the Quiz page redirects on.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var snapshots = new List<(bool Busy, bool Finished)>();
        c.StateChanged += () => snapshots.Add((c.IsBusy, c.IsFinished));

        await c.EndQuizAsync();

        Assert.Equal([(true, false), (false, true)], snapshots);
    }

    // -----------------------------------------------------------------------
    //  RestartAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RestartAsync_BeforeStart_Throws()
    {
        // A restart with no prior run is a caller bug, not an outcome — the
        // controller fails fast rather than fabricating a Started (the same
        // contract class as the producer's null-provider throw).
        var c = Make();
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.RestartAsync());
        Assert.False(c.HasStarted);
    }

    [Fact]
    public async Task RestartAsync_ResetsScoreAndHistory()
    {
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        var c = new QuizController((_, _) => fake, new FakeDecisionStatsSink(), TimeProvider.System);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        Assert.Equal(1, fake.EnumerateCallCount);

        await c.RestartAsync();
        Assert.Equal(2, fake.EnumerateCallCount);
    }

    // -----------------------------------------------------------------------
    //  SummarizeMatchesAsync — the pre-Start "what did my filters select"
    //  affordance: the match count (Total) and the answer-type breakdown, from
    //  one fold of one enumeration
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SummarizeMatchesAsync_NullConfig_Throws()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => c.SummarizeMatchesAsync(null!));
    }

    [Fact]
    public async Task SummarizeMatchesAsync_TotalCountsWhatTheSourceYields()
    {
        // The count is a byproduct of enumerating the factory source (which
        // applies the real filters in production; the fake yields its whole
        // list). Three items in → Total 3, and — the producer's fold contract —
        // Total is exactly the sum of the buckets, so the number on screen and
        // its breakdown come from one pass.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));

        var summary = await c.SummarizeMatchesAsync(new FilterConfig());

        Assert.Equal(3, summary.Total);
        Assert.Equal(3, summary.CheckerPlays);
    }

    [Fact]
    public async Task SummarizeMatchesAsync_BucketsEachDecisionByItsAnswerType()
    {
        // Classification is the producer's and is keyed off the inner
        // DecisionData (BgDecisionData forwards IsCube but not the best-pair
        // halves — folding the composite would misbucket every cube decision).
        // One of each kind in, one in each bucket out. Cube best pairs come from
        // the equities: Double iff min(DoubleTake, 1) > NoDouble; Take iff
        // DoubleTake < 1.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),                     // checker
            TestFixtures.CubeDecision(noDoubleEquity: 0.8, doubleTakeEquity: 0.7),     // no double / take
            TestFixtures.CubeDecision(noDoubleEquity: 1.2, doubleTakeEquity: 1.5),     // too good
            TestFixtures.CubeDecision(noDoubleEquity: 0.5, doubleTakeEquity: 0.7),     // double / take
            TestFixtures.CubeDecision(noDoubleEquity: 0.5, doubleTakeEquity: 1.5));    // double / pass

        var summary = await c.SummarizeMatchesAsync(new FilterConfig());

        Assert.Equal(new AnswerTypeDistribution(
            CheckerPlays: 1, NoDoubleTake: 1, TooGood: 1, DoubleTake: 1, DoublePass: 1),
            summary);
        Assert.Equal(5, summary.Total);
    }

    [Fact]
    public async Task SummarizeMatchesAsync_EmptySource_ReturnsEmpty()
    {
        var c = Make();
        Assert.Equal(AnswerTypeDistribution.Empty, await c.SummarizeMatchesAsync(new FilterConfig()));
    }

    [Fact]
    public async Task SummarizeMatchesAsync_BuildsControllerOwnedPipelineFromConfig()
    {
        // The summary reflects exactly what a Start with this config would admit:
        // it builds the same controller-owned pipeline (FilterConfig.Build) and
        // hands it to the factory. Capture that pipeline and assert the user's
        // PlayerFilter survived the materialization.
        var c = MakeCapturing(out var captured, TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));

        await c.SummarizeMatchesAsync(new FilterConfig { Players = ["Alice"] });

        var pipeline = captured();
        Assert.NotNull(pipeline);
        Assert.True(pipeline!.Matches(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), onRoll: "Alice")));
        Assert.False(pipeline.Matches(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), onRoll: "Bob")));
    }

    [Fact]
    public async Task SummarizeMatchesAsync_DoesNotDisturbLiveQuiz()
    {
        // The throwaway-enumerator guarantee: summarizing mid-quiz touches no
        // live state — Current, Score, the histories, and IsFinished all survive.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync(); // now on the second problem, one scored
        var currentIdBefore = c.Current!.Id;
        var scoreBefore = c.Score;
        var historyBefore = c.History.Count;

        var summary = await c.SummarizeMatchesAsync(new FilterConfig());

        Assert.Equal(2, summary.Total);
        Assert.Equal(currentIdBefore, c.Current!.Id);
        Assert.Equal(scoreBefore, c.Score);
        Assert.Equal(historyBefore, c.History.Count);
        Assert.False(c.IsFinished);
    }

    // -----------------------------------------------------------------------
    //  Lifetime-stats sink — bind at Start/Restart, fold on leaving review
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_BindsStatsContextOnce()
    {
        var c = MakeWithSink(out var sink, TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));

        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        Assert.Equal(1, sink.BeginQuizCallCount);
        Assert.Equal(0, sink.TotalFolds); // binding never folds
    }

    [Fact]
    public async Task RestartAsync_RebindsStatsContext()
    {
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        await c.RestartAsync();

        Assert.Equal(2, sink.BeginQuizCallCount);
    }

    [Fact]
    public async Task SubmitThenContinue_Play_FoldsExactlyTheSubmittedPlayOnce()
    {
        // Fold happens on Continue (leaving review), not at Submit — and folds
        // the same submission object History carries.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        c.SubmitPlay(AltPlay());
        Assert.Equal(0, sink.TotalFolds); // submit alone must not fold
        var submitted = c.History[^1];

        await c.ContinueAsync();

        Assert.Same(submitted, Assert.Single(sink.Plays));
        Assert.Empty(sink.Cubes);
    }

    [Fact]
    public async Task SubmitThenContinue_Cube_FoldsExactlyTheSubmittedCubeOnce()
    {
        var c = MakeWithSink(out var sink, TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        c.SubmitCubeAction(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));
        Assert.Equal(0, sink.TotalFolds);
        var submitted = c.CubeHistory[^1];

        await c.ContinueAsync();

        Assert.Same(submitted, Assert.Single(sink.Cubes));
        Assert.Empty(sink.Plays);
    }

    [Fact]
    public async Task SubmitRedoResubmitContinue_FoldsOnlyTheSecondSubmission()
    {
        // The reason folding lives on Continue: Redo pops the last submission
        // while Review is set, and the stats document has no Minus. A redone
        // answer must leave no trace — only the final answer folds.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        c.SubmitPlay(AltPlay());  // first attempt: incorrect
        await c.RedoAsync();
        c.SubmitPlay(BestPlay()); // second attempt: correct
        await c.ContinueAsync();

        var folded = Assert.Single(sink.Plays);
        Assert.True(folded.IsCorrect);
    }

    [Fact]
    public async Task OffListSubmitThenContinue_FoldsNothing()
    {
        // Producer contract: off-list plays are skips, never lifetime
        // submissions — there is no history entry to fold.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        c.SubmitPlay(UnknownPlay());
        await c.ContinueAsync();

        Assert.Equal(0, sink.TotalFolds);
    }

    [Fact]
    public async Task SkipCurrent_FoldsNothing()
    {
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        await c.SkipCurrentAsync();

        Assert.Equal(0, sink.TotalFolds);
    }

    [Fact]
    public async Task AutoSkippedPassPosition_FoldsNothing()
    {
        // Auto-skipped pass positions were never shown to the user — they
        // must not touch lifetime stats.
        var c = MakeWithSink(out var sink,
            TestFixtures.PassDecision(),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));

        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        Assert.Equal(0, sink.TotalFolds);
    }

    [Fact]
    public async Task LastProblemContinue_FoldsBeforeFinishing()
    {
        // The final decision's answer folds even though Continue exhausts the
        // source — the fold sits before the advance.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();

        Assert.True(c.IsFinished);
        Assert.Single(sink.Plays);
    }

    [Fact]
    public async Task InterleavedQuiz_FoldsEachContinuedAnswerInOrder()
    {
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05),
            TestFixtures.CubeDecision(),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        c.SubmitPlay(AltPlay());
        await c.ContinueAsync();
        c.SubmitCubeAction(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));
        await c.ContinueAsync();
        await c.SkipCurrentAsync(); // third problem skipped — no fold

        Assert.Single(sink.Plays);
        Assert.Single(sink.Cubes);
        Assert.Equal(2, sink.TotalFolds);
    }

    // -----------------------------------------------------------------------
    //  StateChanged firing
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StateChanged_FiresTwicePerGatedTransitionOncePerSubmit()
    {
        // The gated async transitions each fire exactly twice — busy-on (so
        // pages render the busy affordances before the churn) and busy-off
        // (delivering the end state; AdvanceAsync itself no longer fires).
        // The synchronous Submit fires once as before.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        var fires = 0;
        c.StateChanged += () => fires++;

        await c.StartAsync(new FilterConfig(), QuizMix.Empty); // +2 — gate on/off around advance to first
        c.SubmitPlay(BestPlay());               // +1 — score + enter review
        await c.ContinueAsync();                // +2 — gate on/off around advance to second
        await c.SkipCurrentAsync();             // +2 — gate on/off around skip + advance (exhausts)

        Assert.Equal(7, fires);
    }

    // -----------------------------------------------------------------------
    //  Stats-weighted mix: wrap, refusal, override, Restart-recomposes
    // -----------------------------------------------------------------------

    /// <summary>A minimal weighted mix: 100% never-seen, deterministic order.</summary>
    private static QuizMix NeverSeenMix(int? quizLength = null) =>
        new([new QuizMixEntry(QuizCategory.NeverSeen, 100)], quizLength, randomOrder: false);

    /// <summary>
    /// Constructs a controller over a scriptable sink and a mix-capturing
    /// factory: <paramref name="sink"/> scripts stats availability
    /// (<c>CanWeightMix</c> / <c>CurrentDocument</c>); <paramref name="mixes"/>
    /// records the <i>effective</i> mix each factory invocation received, so
    /// tests can pin what the controller actually composes with.
    /// </summary>
    private static QuizController MakeWeighable(
        out FakeDecisionStatsSink sink, out List<QuizMix> mixes, params BgDecisionData[] items)
    {
        var fake = new FakeProblemSetSource(items);
        sink = new FakeDecisionStatsSink();
        var captured = new List<QuizMix>();
        mixes = captured;
        return new QuizController(
            (_, mix) => { captured.Add(mix); return fake; }, sink, TimeProvider.System);
    }

    /// <summary>A lifetime-stats document holding one correct sighting of <paramref name="id"/>.</summary>
    private static DecisionStatsDocument DocWithSeen(DecisionId id) =>
        DecisionStatsDocument.Empty.Plus(
            new SubmittedPlay(id, BestPlay(), 0, 0.0, IsCorrect: true), TimeProvider.System);

    [Fact]
    public void Ctor_NullClock_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new QuizController((_, _) => new FakeProblemSetSource([]), new FakeDecisionStatsSink(), null!));
    }

    [Fact]
    public async Task StartAsync_NullMix_Throws()
    {
        var c = Make();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => c.StartAsync(new FilterConfig(), null!));
    }

    [Fact]
    public async Task StartAsync_BlankMix_NoCompositionLayer()
    {
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = MakeWeighable(out _, out var mixes, d);

        var outcome = await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        // Passthrough: started with no stats at all, no telemetry, and the
        // factory saw the blank mix (its shuffle-arbitration input).
        Assert.Equal(QuizStartOutcome.Started, outcome);
        Assert.Same(d, c.Current);
        Assert.Null(c.LastComposition);
        Assert.Same(QuizMix.Empty, Assert.Single(mixes));
    }

    [Fact]
    public async Task StartAsync_MixWithoutCapability_RefusedBeforeBind()
    {
        var c = MakeWeighable(out var sink, out var mixes,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        sink.CanWeightMix = false; // fallback pick / denied / nothing picked
        var fires = 0;
        c.StateChanged += () => fires++;

        var outcome = await c.StartAsync(new FilterConfig(), NeverSeenMix());

        // Stage-1 refusal: zero quiz-state side effects — no bind, no source
        // build, nothing started. The only StateChanged firings are the
        // transition gate's two busy flips, which deliver unchanged state.
        Assert.Equal(QuizStartOutcome.MixRequiresStats, outcome);
        Assert.Equal(0, sink.BeginQuizCallCount);
        Assert.Empty(mixes);
        Assert.False(c.HasStarted);
        Assert.Equal(2, fires);
    }

    [Fact]
    public async Task StartAsync_MixBindsWithoutDocument_RefusedAfterBind()
    {
        var c = MakeWeighable(out var sink, out var mixes,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        sink.CanWeightMix = true;
        sink.CurrentDocument = null; // the bind ran but yielded no document (unreadable file)

        var outcome = await c.StartAsync(new FilterConfig(), NeverSeenMix());

        // Stage-2 refusal: the bind is the one side effect; quiz state stays
        // untouched and no source is ever built.
        Assert.Equal(QuizStartOutcome.MixRequiresStats, outcome);
        Assert.Equal(1, sink.BeginQuizCallCount);
        Assert.Empty(mixes);
        Assert.False(c.HasStarted);
    }

    [Fact]
    public async Task StartAsync_RefusedMidQuiz_PriorQuizAndStoredConfigUntouched()
    {
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"));
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"));
        var c = MakeWeighable(out var sink, out var mixes, d1, d2);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay()); // in review, one scored answer

        var outcome = await c.StartAsync(new FilterConfig(), NeverSeenMix());

        // The refused weighted start leaves the running quiz exactly as it was…
        Assert.Equal(QuizStartOutcome.MixRequiresStats, outcome);
        Assert.Same(d1, c.Current);
        Assert.NotNull(c.Review);
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.False(c.IsFinished);

        // …and never committed the refused config: Restart re-runs the stored
        // blank mix (factory invoked twice, blank both times) instead of the
        // weighted one that was refused.
        var restart = await c.RestartAsync();
        Assert.Equal(QuizStartOutcome.Started, restart);
        Assert.Equal(2, mixes.Count);
        Assert.All(mixes, m => Assert.Same(QuizMix.Empty, m));
    }

    [Fact]
    public async Task StartAsync_MixWithDocument_ComposesFromLifetimeStats()
    {
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"));
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"));
        var c = MakeWeighable(out var sink, out var mixes, d1, d2);
        sink.CanWeightMix = true;
        sink.CurrentDocument = DocWithSeen(d1.Id); // d1 seen before; d2 never seen

        var mix = NeverSeenMix();
        var outcome = await c.StartAsync(new FilterConfig(), mix);

        // The real MixedProblemSetSource composes over the fake inner source:
        // a 100% never-seen mix admits only d2, and the telemetry reports the
        // one-decision composition before the first problem shows.
        Assert.Equal(QuizStartOutcome.Started, outcome);
        Assert.Same(d2, c.Current);
        Assert.NotNull(c.LastComposition);
        Assert.Equal(1, c.LastComposition!.DrawnCount);
        Assert.Same(mix, Assert.Single(mixes)); // the factory saw the effective (real) mix

        await c.SkipCurrentAsync();
        Assert.True(c.IsFinished); // d1 never reached the quiz
    }

    [Fact]
    public async Task StartAsync_IgnoreMix_RunsPassthroughButStoresTheMix()
    {
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"));
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"));
        var c = MakeWeighable(out var sink, out var mixes, d1, d2);
        sink.CanWeightMix = false; // stats unavailable — the refusal scenario

        var outcome = await c.StartAsync(new FilterConfig(), NeverSeenMix(), ignoreMix: true);

        // The per-run override runs this quiz as passthrough…
        Assert.Equal(QuizStartOutcome.Started, outcome);
        Assert.Same(d1, c.Current);
        Assert.Null(c.LastComposition);
        Assert.Same(QuizMix.Empty, Assert.Single(mixes));

        // …while the weighted mix stayed stored: a plain Restart re-attempts
        // it and is refused again while stats remain unavailable.
        var restart = await c.RestartAsync();
        Assert.Equal(QuizStartOutcome.MixRequiresStats, restart);
        Assert.Same(d1, c.Current); // refused restart also left the quiz alone
    }

    [Fact]
    public async Task RestartAsync_ReattemptsStoredMix_RecomposingAgainstCurrentDocument()
    {
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"));
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"));
        var c = MakeWeighable(out var sink, out _, d1, d2);
        sink.CanWeightMix = true;
        sink.CurrentDocument = DecisionStatsDocument.Empty; // nothing seen yet

        await c.StartAsync(new FilterConfig(), NeverSeenMix());
        Assert.Same(d1, c.Current);
        Assert.Equal(2, c.LastComposition!.DrawnCount); // both never seen

        // The lifetime record advances (as folds would advance it mid-quiz);
        // Restart resolves the provider fresh and composes against the record
        // as it stands now — the deliberate Restart-recomposes semantics.
        sink.CurrentDocument = DocWithSeen(d1.Id);
        var outcome = await c.RestartAsync();

        Assert.Equal(QuizStartOutcome.Started, outcome);
        Assert.Same(d2, c.Current);
        Assert.Equal(1, c.LastComposition!.DrawnCount);
    }

    [Fact]
    public async Task RestartAsync_Refused_LeavesFinishedQuizIntact()
    {
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = MakeWeighable(out var sink, out _, d);
        sink.CanWeightMix = true;
        sink.CurrentDocument = DecisionStatsDocument.Empty;

        await c.StartAsync(new FilterConfig(), NeverSeenMix());
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();
        Assert.True(c.IsFinished);

        // Stats fall away between quizzes (e.g. the folder pick was cleared);
        // the Done page's Restart is refused and its summary must survive.
        sink.CanWeightMix = false;
        var outcome = await c.RestartAsync();

        Assert.Equal(QuizStartOutcome.MixRequiresStats, outcome);
        Assert.True(c.IsFinished);
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(1, c.Score.Total.Correct);
    }

    // -----------------------------------------------------------------------
    //  Problem position & total — the Quiz page's "Problem N of M"
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProblemNumber_CountsConsumedStreamSlots_PassPositionsIncluded()
    {
        // The counter's settled convention: N is the consumed stream slot,
        // commensurable with ProblemCount (which also counts slots). The pass
        // position in slot 2 is consumed-but-never-presented, so the second
        // presented problem reads slot 3 — and N lands exactly on M at the
        // stream's end rather than the quiz finishing below its stated total.
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"));
        var pass = TestFixtures.PassDecision();
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"));
        var c = Make(d1, pass, d2);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        Assert.Same(d1, c.Current);
        Assert.Equal(1, c.ProblemNumber);
        Assert.Equal(3, c.ProblemCount);

        await c.SkipCurrentAsync();
        Assert.Same(d2, c.Current);
        Assert.Equal(3, c.ProblemNumber); // slot 2 consumed silently

        await c.SkipCurrentAsync();
        Assert.True(c.IsFinished);
        Assert.Equal(3, c.ProblemNumber); // N == M at stream end
    }

    [Fact]
    public async Task ProblemNumber_RedoLeavesItUntouched_RestartResets()
    {
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"));
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"));
        var c = Make(d1, d2);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();
        Assert.Equal(2, c.ProblemNumber);

        c.SubmitPlay(BestPlay());
        await c.RedoAsync();
        Assert.Equal(2, c.ProblemNumber); // Redo re-answers the same slot

        await c.RestartAsync();
        Assert.Same(d1, c.Current);
        Assert.Equal(1, c.ProblemNumber);
    }

    [Fact]
    public async Task ProblemCount_PassthroughStreamingSource_IsNull()
    {
        var fake = new FakeProblemSetSource(
            [TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay())], countKnown: false);
        var c = new QuizController((_, _) => fake, new FakeDecisionStatsSink(), TimeProvider.System);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        Assert.Null(c.ProblemCount);      // no fabricated total…
        Assert.Equal(1, c.ProblemNumber); // …but the position still tracks
    }

    [Fact]
    public async Task ProblemCount_WeightedQuiz_IsCompositionDrawnCount()
    {
        // Weighted, the total is the composition's drawn count — not the
        // inner source's Count (2 here), which the mix composed down to 1.
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"));
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"));
        var c = MakeWeighable(out var sink, out _, d1, d2);
        sink.CanWeightMix = true;
        sink.CurrentDocument = DocWithSeen(d1.Id);

        await c.StartAsync(new FilterConfig(), NeverSeenMix());

        Assert.Equal(1, c.ProblemCount);
        Assert.Equal(1, c.ProblemNumber);
    }

    // -----------------------------------------------------------------------
    //  ActiveMixHasLength — the length-bound fact the Quiz page frames by
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ActiveMixHasLength_TracksTheEffectiveMix()
    {
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = MakeWeighable(out var sink, out _, d);
        sink.CanWeightMix = true;
        sink.CurrentDocument = DecisionStatsDocument.Empty;

        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        Assert.False(c.ActiveMixHasLength);                 // passthrough

        await c.StartAsync(new FilterConfig(), NeverSeenMix());
        Assert.False(c.ActiveMixHasLength);                 // capless — no length to bind to

        await c.StartAsync(new FilterConfig(), NeverSeenMix(quizLength: 1));
        Assert.True(c.ActiveMixHasLength);                  // length-bound

        await c.StartAsync(new FilterConfig(), NeverSeenMix(quizLength: 1), ignoreMix: true);
        Assert.False(c.ActiveMixHasLength);                 // override runs passthrough
    }

    [Fact]
    public async Task ActiveMixHasLength_RefusedStart_LeavesPriorValue()
    {
        // Refusals touch no active-run state — this flag included: a running
        // length-bound quiz keeps its notice framing behind a refused start.
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = MakeWeighable(out var sink, out _, d);
        sink.CanWeightMix = true;
        sink.CurrentDocument = DecisionStatsDocument.Empty;
        await c.StartAsync(new FilterConfig(), NeverSeenMix(quizLength: 1));
        Assert.True(c.ActiveMixHasLength);

        sink.CanWeightMix = false;
        var outcome = await c.StartAsync(new FilterConfig(), NeverSeenMix());

        Assert.Equal(QuizStartOutcome.MixRequiresStats, outcome);
        Assert.True(c.ActiveMixHasLength);
    }

    // -----------------------------------------------------------------------
    //  RandomHomeBoardOnRight — the per-problem side roll
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RandomHomeBoardOnRight_TakesBothSides_AcrossAProblemStream()
    {
        // The anti-memorization contract: a fresh roll per problem, so the same
        // position can come back mirrored. Deliberately not seeded (see the
        // property, and the Program.cs shuffle rationale) — over 40 advances a
        // roll stuck on one side would have to survive odds of 2^-39, which is
        // comfortably beyond a flaky test and squarely a broken one.
        const int problems = 40;
        var stream = Enumerable.Range(0, problems)
            .Select(_ => TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()))
            .ToArray();
        var c = Make(stream);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        var seen = new HashSet<bool> { c.RandomHomeBoardOnRight };
        for (var i = 1; i < problems; i++)
        {
            await c.SkipCurrentAsync();
            seen.Add(c.RandomHomeBoardOnRight);
        }

        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public async Task RandomHomeBoardOnRight_HoldsStillForOneProblem_AcrossSubmitAndRedo()
    {
        // One problem, one side — the rule that keeps the board from moving
        // under the user between answering it and reading its solution, and
        // again when Redo returns them to the same problem.
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var rolled = c.RandomHomeBoardOnRight;

        c.SubmitPlay(BestPlay());
        Assert.Equal(rolled, c.RandomHomeBoardOnRight);

        await c.RedoAsync();
        Assert.Equal(rolled, c.RandomHomeBoardOnRight);
    }

    [Fact]
    public async Task RandomHomeBoardOnRight_PassPositions_TakeNoRoll()
    {
        // Rolled beside the assignment of Current, after the auto-skip: a
        // position the user never sees must not consume a side, or the "one
        // roll per problem shown" reading of the property stops being true.
        var c = Make(
            TestFixtures.PassDecision(),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        // The pass was skipped silently and the shown problem is the second one…
        Assert.Equal(2, c.ProblemNumber);
        var shown = c.RandomHomeBoardOnRight;

        // …and nothing but an advance can move the roll it took.
        c.SubmitPlay(BestPlay());
        Assert.Equal(shown, c.RandomHomeBoardOnRight);
    }
}
