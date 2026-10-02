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
        return new QuizController((_, _, _) => TestFixtures.Composed(fake), new FakeProblemStatsSink(), TimeProvider.System);
    }

    /// <summary>
    /// Constructs a controller over a recording stats sink, exposed via
    /// <paramref name="sink"/>. Lets tests assert exactly which submissions
    /// the controller folded into lifetime stats — and which flows folded
    /// nothing.
    /// </summary>
    private static QuizController MakeWithSink(
        out FakeProblemStatsSink sink, params BgDecisionData[] items)
    {
        var fake = new FakeProblemSetSource(items);
        sink = new FakeProblemStatsSink();
        return new QuizController((_, _, _) => TestFixtures.Composed(fake), sink, TimeProvider.System);
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
        return new QuizController((set, _, _) => { holder = set; return TestFixtures.Composed(fake); }, new FakeProblemStatsSink(), TimeProvider.System);
    }

    /// <summary>
    /// Constructs a controller whose source factory captures the ranking it is
    /// handed, exposed via <paramref name="handed"/> (null until the factory is
    /// first called). Lets tests assert that the run's ranking — never a
    /// default — reaches the source stack, where the filter reads it.
    /// </summary>
    private static QuizController MakeRankingCapturing(
        out Func<PlayRanking?> handed, params BgDecisionData[] items)
    {
        var fake = new FakeProblemSetSource(items);
        PlayRanking? holder = null;
        handed = () => holder;
        return new QuizController(
            (_, ranking, _) => { holder = ranking; return TestFixtures.Composed(fake); },
            new FakeProblemStatsSink(), TimeProvider.System);
    }

    private static Play BestPlay() => TestFixtures.OpeningBest();
    private static Play AltPlay() => TestFixtures.OpeningAlternative();
    private static Play UnknownPlay() => TestFixtures.OpeningUnlisted();

    /// <summary>
    /// The scored submission a play review shows, asserting the review is of a
    /// scored play — the producer's outcome read by its case, never compared
    /// (<c>PlaySubmission</c>'s equality is not the app's to use).
    /// </summary>
    private static SubmittedPlay ScoredReview(ProblemReview? review)
    {
        var play = Assert.IsType<ProblemReview.Play>(review);
        Assert.True(play.Submission.TryGetScored(out var submitted),
            $"Expected a scored review; the outcome was {play.Submission.Kind}.");
        return submitted;
    }

    /// <summary>
    /// The play review of an off-list play: the producer's off-list outcome, no
    /// candidate (so no † mark), and the play as entered, which the verdict
    /// names.
    /// </summary>
    private static ProblemReview.Play OffListReview(ProblemReview? review)
    {
        var play = Assert.IsType<ProblemReview.Play>(review);
        Assert.Equal(PlaySubmissionKind.OffList, play.Submission.Kind);
        Assert.Null(play.CandidateIndex);
        return play;
    }

    /// <summary>The scored submission a cube review shows, asserting the review is of a cube answer.</summary>
    private static SubmittedCubeAnswer CubeReview(ProblemReview? review) =>
        Assert.IsType<ProblemReview.Cube>(review).Submission;

    // -----------------------------------------------------------------------
    //  Reading the record through the controller
    //
    //  The per-problem record is the run's (QuizRun; SPEC-quiz-history.md §7),
    //  and the controller shows no list of it: what it shows is the score and
    //  the skip count the run derives from that record, the review of the
    //  submission just made, and — in the fake sink — what folded. So a pin
    //  here reads the record one of three ways:
    //
    //   * how many answers are of record: the score's own counts, which are
    //     the record's by derivation (one play decision per checker-play
    //     answer of record; one double and one take per cube answer);
    //   * what an answer of record is: the review taken straight after the
    //     live submission, which carries the very submission the run recorded
    //     (QuizRunTests pins that identity), or the submission the sink was
    //     handed when the run advanced past the problem;
    //   * that a record stands: the score it produced, unchanged — a practice
    //     answer of a different value would move it — and its instance
    //     reaching the sink.
    //
    //  The record itself — each problem's disposition, by instance — is pinned
    //  where it lives, in QuizRunTests.
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    //  Construction
    // -----------------------------------------------------------------------

    [Fact]
    public void Ctor_NullFactory_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new QuizController(null!, new FakeProblemStatsSink(), TimeProvider.System));
    }

    [Fact]
    public void Ctor_NullStatsSink_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new QuizController((_, _, _) => TestFixtures.Composed(new FakeProblemSetSource([])), null!, TimeProvider.System));
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
        Assert.Null(c.Review);
        Assert.Equal(0, c.SkippedCount);
        Assert.Equal(0, c.ProblemNumber);
        Assert.Null(c.ProblemCount);
        Assert.Null(c.LastComposition);
    }

    // -----------------------------------------------------------------------
    //  StartAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_NullFilters_Throws()
    {
        var c = Make();
        await Assert.ThrowsAsync<ArgumentNullException>(() => c.StartAsync(null!, QuizMix.Empty, PlayRanking.Equity));
    }

    [Fact]
    public async Task StartAsync_AdvancesToFirstProblem()
    {
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(d);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

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

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        var pipeline = captured();
        Assert.NotNull(pipeline);
        Assert.True(pipeline.Matches(TestFixtures.CubeDecision().ViewFor(PlayRanking.Equity)));
    }

    [Fact]
    public async Task StartAsync_UserCubeOnly_AdmitsCubeRejectsChecker()
    {
        // The user's DecisionType choice governs in both directions: a CubeOnly
        // config admits cube decisions and rejects checker plays. Confirms the
        // lift handed control to the user's filter rather than dropping the
        // policy entirely.
        var c = MakeCapturing(out var captured, TestFixtures.CubeDecision());

        await c.StartAsync(new FilterConfig { DecisionType = DecisionTypeOption.CubeOnly }, QuizMix.Empty, PlayRanking.Equity);

        var pipeline = captured();
        Assert.NotNull(pipeline);
        Assert.True(pipeline.Matches(TestFixtures.CubeDecision().ViewFor(PlayRanking.Equity)));
        Assert.False(pipeline.Matches(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()).ViewFor(PlayRanking.Equity)));
    }

    [Fact]
    public async Task StartAsync_CubeDecision_IsSurfacedNotAutoSkipped()
    {
        // A cube decision carries Dice [0, 0]; without the IsCube guard in
        // HasNoPlayChoice that hits the no-legal-play sentinel and is silently
        // auto-skipped, making the whole cube feature invisible. The guard must
        // surface the cube decision as the current problem. Unchanged by
        // halheinrich/backgammon#140: widening the rule from "no legal play" to
        // "no play choice" widens what the guard has to hold back, so the pin
        // matters more, not less.
        var cube = TestFixtures.CubeDecision();
        var c = Make(cube);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

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

        await c.StartAsync(cfg, QuizMix.Empty, PlayRanking.Equity);

        Assert.Equal(["Alice"], cfg.Players);
        Assert.Equal(DecisionTypeOption.Both, cfg.DecisionType);
    }

    [Fact]
    public async Task StartAsync_PassThenScoring_AutoSkipsPass()
    {
        var pass = TestFixtures.PassDecision();
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(pass, d);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        // Pass auto-skipped silently — counts on user-driven skips only.
        Assert.Equal(0, c.SkippedCount);
        Assert.Same(d, c.Current);
    }

    // -----------------------------------------------------------------------
    //  No play choice: the widened auto-skip rule (halheinrich/backgammon#140)
    //
    //  The rule the advance path skips on is "this record offers no play
    //  choice" — exactly one legal play under canonical-play equivalence. A
    //  pass is its degenerate case (the one play moves nothing); a forced
    //  checker play is the case beta feedback found being quizzed. Both are
    //  driven here at the layer that decides, and each fixture is paired with a
    //  scoring decision so "skipped" reads as "the next problem is showing"
    //  rather than "the quiz ended".
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_ForcedPlay_AutoSkipped()
    {
        // The finding: the dice admit one legal play and it moves something, so
        // there is no decision to make. Same treatment as a pass — silent, not
        // counted as a user skip, and the user lands on the next real problem.
        var forced = TestFixtures.ForcedPlayDecision();
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(forced, d);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        Assert.Same(d, c.Current);
        Assert.Equal(0, c.SkippedCount);
        // The slot was consumed, per ProblemNumber's stream-slot convention.
        Assert.Equal(2, c.ProblemNumber);
    }

    [Fact]
    public async Task StartAsync_ForcedDouble_AutoSkipped()
    {
        // Doubles reach BgMoveGen through GenerateDoubles rather than
        // GenerateNonDoubles, so a forced double is a separate path to the same
        // one-play answer — and the only one where a play can be forced with
        // dice left unplayed.
        var forced = TestFixtures.ForcedDoubleDecision();
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(forced, d);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        Assert.Same(d, c.Current);
        Assert.Equal(0, c.SkippedCount);
    }

    [Fact]
    public async Task StartAsync_ForcedBearOff_AutoSkipped()
    {
        // A bear-off admitting one legal play is as forced as any other
        // position and must never reach the user. The board is chosen, not
        // arbitrary: GeneratePlays returned its single play twice until
        // halheinrich/backgammon#141, so driving the controller over it pins
        // the skip end-to-end on the shape whose candidate count has actually
        // moved. See the fixture.
        var forced = TestFixtures.ForcedBearOffDecision();
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(forced, d);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        Assert.Same(d, c.Current);
        Assert.Equal(0, c.SkippedCount);
    }

    [Fact]
    public async Task StartAsync_TwoLegalPlays_IsShownNotSkipped()
    {
        // The near miss, and the half of the rule that keeps it honest: this
        // board is the forced fixture's with one blocker removed, so it offers
        // two legal plays — 12/6 and 9/3 — and a two-way choice is a decision
        // however short. A rule that over-skipped (say, keying on the record's
        // one-entry candidate list) would swallow it.
        var choice = TestFixtures.OneClickPlayDecision();
        var c = Make(choice);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        Assert.Same(choice, c.Current);
        Assert.False(c.IsFinished);
    }

    [Fact]
    public async Task AutoSkippedForcedPlay_FoldsNothing()
    {
        // The pass precedent's stats half, extended: a position the user never
        // saw must not touch the lifetime record, whatever made it skippable.
        var c = MakeWithSink(out var sink,
            TestFixtures.ForcedPlayDecision(),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        Assert.Equal(0, sink.TotalFolds);
    }

    [Fact]
    public async Task StartAsync_FiresStateChanged()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        var fired = 0;
        c.StateChanged += () => fired++;

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        Assert.True(fired >= 1);
    }

    [Fact]
    public async Task StartAsync_EmptySource_FlipsIsFinishedImmediately()
    {
        var c = Make();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(BestPlay());

        var first = ScoredReview(c.Review);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(AltPlay());

        var first = ScoredReview(c.Review);
        Assert.False(first.IsCorrect);
        Assert.Equal(0.05, first.EquityLoss, 6);
        Assert.Equal(1, first.MatchedCandidateIndex);
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(0, c.Score.Total.Correct);
        Assert.Equal(0.05, c.Score.Total.TotalEquityLoss, 6);
    }

    [Fact]
    public async Task SubmitPlay_SetsPlayReview_DoesNotAdvance()
    {
        // Submit scores and enters the review state without advancing: Current
        // still points at the answered problem and Review carries the matched
        // candidate, which drives the solution diagram's † marker.
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05);
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(d1, d2);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(AltPlay());

        Assert.Same(d1, c.Current); // unchanged — no advance
        Assert.False(c.IsFinished);
        var review = Assert.IsType<ProblemReview.Play>(c.Review);
        Assert.Equal(1, review.CandidateIndex);
        var scored = ScoredReview(review);
        Assert.False(scored.IsCorrect);
        Assert.Equal(0.05, scored.EquityLoss, 6);
    }

    [Fact]
    public async Task SubmitPlay_OffList_CountsAsSkip_SetsOffListReview()
    {
        // Off-list: counted as a skip (no answer of record, score unchanged),
        // but a Review is still produced — the off-list outcome, no candidate
        // (no marker drawn) — so the user sees the best play on the solution
        // diagram, and it carries the play as entered, which the verdict names
        // (halheinrich/backgammon#274).
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(UnknownPlay());

        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Equal(1, c.SkippedCount);
        var review = OffListReview(c.Review);
        Assert.True(UnknownPlay().IsSameEncoding(review.UserPlay));
    }

    [Fact]
    public async Task ContinueAsync_AdvancesAndClearsReview()
    {
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(d1, d2);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitPlay(BestPlay());
        var reviewBefore = c.Review;

        c.SubmitPlay(AltPlay());

        Assert.Same(reviewBefore, c.Review); // unchanged
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(1, c.Score.Total.Correct); // and still the first answer's
    }

    [Fact]
    public async Task SubmitThenContinue_LastProblem_FlipsIsFinished()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

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

        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Equal(0, c.SkippedCount);
        Assert.Null(c.Review);
    }

    [Fact]
    public async Task SubmitPlay_AfterFinish_NoOp()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync(); // exhausts → IsFinished
        Assert.True(c.IsFinished);

        var scoreBefore = c.Score;
        c.SubmitPlay(BestPlay());

        Assert.Equal(scoreBefore, c.Score);
        Assert.Null(c.Review);
    }

    [Fact]
    public async Task SubmitPlay_AccumulatesEquityLossAcrossMultiple()
    {
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.10),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.30));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

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
    public async Task SubmitPlay_CarriesProblemKeyIntoTheFold()
    {
        // Wire: the submitted play must carry the answered problem's CONTENT
        // identity into the answer of record, so the lifetime fold keys on the
        // problem rather than on where the record came from. A distinctive
        // per-problem key pins the actual carry — a placeholder key would fail
        // this equality. (The producer derives it from the scored record
        // itself; this is the consumer's end of that wire, read where the wire
        // ends: on the submission the sink is handed.)
        var problem = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 7);
        var c = MakeWithSink(out var sink, problem);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();

        Assert.Equal(ProblemKey.From(problem), Assert.Single(sink.Plays).ProblemKey);
    }

    // -----------------------------------------------------------------------
    //  SubmitPlay — matching by the producer's play identity
    //  A play is the candidate that reaches the same position from the
    //  problem's start (BoardState.IsSamePlay states the rule; the producer's
    //  PlaySubmission.Score applies it). These pin that the controller scores
    //  through it: an encoding the entry can produce finds its candidate, and a
    //  genuinely different play does not.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitPlay_DecomposedHops_MatchCombinedCandidate()
    {
        // The play-equivalence arc's acceptance pin — the exact user repro, on a
        // legal play. The candidate list carries the combined play 13/9; the
        // user enters it as two clicks, 13/10 then 10/9. Both reach the one
        // position, so the decomposed submission matches the combined candidate
        // and scores as it rather than falling off-list (the bug that arc fixed:
        // a two-click play was scored off-list though it was on the list).
        var combined = Play.Create(new(13, 9));                     // candidate: 13/9
        var c = Make(TestFixtures.TwoChoiceDecision(combined, BestPlay(), play2Loss: 0.05));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(Play.Create(new(13, 10), new(10, 9)));        // 13/10, 10/9

        var scored = ScoredReview(c.Review);
        Assert.Equal(0, scored.MatchedCandidateIndex);             // matched the combined candidate
        Assert.True(scored.IsCorrect);
        Assert.Equal(1, c.Score.PlayDecisions.Correct);            // and it is what the score counts
        Assert.Equal(0, c.SkippedCount);                           // scored, not skipped off-list
        Assert.Equal(0, Assert.IsType<ProblemReview.Play>(c.Review).CandidateIndex);
    }

    [Fact]
    public async Task SubmitPlay_DecomposedHopWithIntermediateHit_StaysOffListAgainstNonHittingCandidate()
    {
        // The guard rail on the match above: an intermediate hit reaches a
        // different position — it puts a checker on the bar — so 13/10*/8 is a
        // different play from the non-hitting candidate 13/11/8, and must stay
        // off-list. One checker on the 13-point, an opposing blot on the 10,
        // roll 3-2: both routes are legal, and they are two plays.
        var c = Make(TestFixtures.CheckerPlayOn(
            SoleCheckerOn13FacingABlotOn10(), [3, 2],
            Play.Create(new(13, 11), new(11, 8)),                   // candidate 13/8, no hit
            Play.Create(new(13, -10), new(10, 8))));                // 13/10*/8, analyzed apart
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(Play.Create(new(13, -10), new(10, 8)));        // matches the hitting one only

        Assert.Equal(1, ScoredReview(c.Review).MatchedCandidateIndex);
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
    }

    [Fact]
    public async Task SubmitPlay_TheHitMarkedOnTheOtherChecker_IsTheCandidate()
    {
        // halheinrich/backgammon#273, as a unit pin: two checkers make a point
        // on an opposing blot, and the entry marks the hit on one of them
        // (8/3* 7/3) while XG recorded it on the other (8/3 7/3*). Both reach
        // the same position, so they are the same play: the submission scores
        // as that candidate instead of falling off the list as a skip. The
        // real-file case is PlayIdentityRealFileTests; this one runs everywhere.
        var xgEncoding = Play.Create(new(8, 3), new(7, -3));        // 8/3 7/3*, as XG stores it
        var c = Make(TestFixtures.CheckerPlayOn(
            CheckersOn8And7FacingABlotOn3(), [5, 4],
            xgEncoding,
            Play.Create(new(8, 4), new(7, 2))));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(Play.Create(new(8, -3), new(7, 3)));           // 8/3* 7/3, as entered

        Assert.Equal(0, c.SkippedCount);
        var submitted = ScoredReview(c.Review);
        Assert.Equal(0, submitted.MatchedCandidateIndex);
        Assert.True(submitted.IsCorrect);
        Assert.Equal(1, c.Score.PlayDecisions.Correct);
    }

    /// <summary>One on-roll checker on the 13-point, an opposing blot on the 10 and two opposing checkers on the 20.</summary>
    private static BoardPosition SoleCheckerOn13FacingABlotOn10()
    {
        var m = new int[26];
        m[13] = 1;
        m[10] = -1;
        m[20] = -2;
        return new BoardPosition(m);
    }

    /// <summary>On-roll checkers on the 8- and 7-points, an opposing blot on the 3 and two opposing checkers on the 20.</summary>
    private static BoardPosition CheckersOn8And7FacingABlotOn3()
    {
        var m = new int[26];
        m[8] = 1;
        m[7] = 1;
        m[3] = -1;
        m[20] = -2;
        return new BoardPosition(m);
    }

    // -----------------------------------------------------------------------
    //  One quiz, one ranking (SPEC-scoring.md §2a)
    //  Every pin here runs under DepthFirst on the depth-split fixture, where the
    //  two rankings disagree: Equity is the producers' default AND the setting's,
    //  so a pin under Equity alone could not tell "passed the run's ranking" from
    //  "fell back to a default". Each would fail if its path did.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(PlayRanking.Equity, false, 0.05)]
    [InlineData(PlayRanking.DepthFirst, true, 0.0)]
    public async Task SubmitPlay_TheRolledOutPlay_IsScoredAgainstTheRankingsBest(
        PlayRanking ranking, bool correct, double loss)
    {
        // The rollout 13/10 6/5 loses 0.05 to the 3-ply 8/5 6/5 by equity, and is
        // the best play when the deepest analysis ranks first.
        var c = Make(TestFixtures.DepthSplitDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, ranking);

        c.SubmitPlay(TestFixtures.OpeningAlternative());

        var submitted = ScoredReview(c.Review);
        Assert.Equal(ranking, submitted.Ranking);
        Assert.Equal(correct, submitted.IsCorrect);
        Assert.Equal(loss, submitted.EquityLoss, 6);
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
        Assert.Equal(correct ? 1 : 0, c.Score.PlayDecisions.Correct);
    }

    [Theory]
    [InlineData(PlayRanking.Equity, 0.15)]
    [InlineData(PlayRanking.DepthFirst, 0.10)]
    public async Task SubmitPlay_AThirdPlay_LosesTheRankingsError(PlayRanking ranking, double loss)
    {
        // Scored under both rankings, but measured against each ranking's own
        // best: 0.15 below the 3-ply best, 0.10 below the rollout.
        var c = Make(TestFixtures.DepthSplitDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, ranking);

        c.SubmitPlay(TestFixtures.DepthSplitThirdPlay());

        Assert.Equal(loss, ScoredReview(c.Review).EquityLoss, 6);
        Assert.Equal(loss, c.Score.PlayDecisions.TotalEquityLoss, 6);
    }

    [Fact]
    public async Task SubmitPlay_APlayTheRankingDoesNotScore_IsASkipOfRecordThatFoldsNothing()
    {
        // Under depth first the 3-ply 8/5 6/5 was analyzed less deeply than the
        // best and rated higher there, so it is not scored: "a skip of record
        // that folds nothing" (SPEC-scoring.md §2a). The review still shows it,
        // as the candidate it is — the solution's † row — with the not-scored
        // outcome the page words as ruled.
        var c = MakeWithSink(out var sink, TestFixtures.DepthSplitDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.DepthFirst);

        c.SubmitPlay(TestFixtures.OpeningBest());

        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Equal(1, c.SkippedCount);
        var review = Assert.IsType<ProblemReview.Play>(c.Review);
        Assert.Equal(PlaySubmissionKind.NotScored, review.Submission.Kind);
        Assert.Equal(1, review.CandidateIndex);

        await c.ContinueAsync();

        Assert.Equal(0, sink.TotalFolds);
    }

    [Fact]
    public async Task SubmitPlay_APlayTheRankingDoesNotScore_StaysASkipThroughARedo()
    {
        // A skip is of record (§2): a redo after the not-scored play, then a
        // scored practice answer, leaves the skip standing and scores nothing.
        var c = Make(TestFixtures.DepthSplitDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.DepthFirst);

        c.SubmitPlay(TestFixtures.OpeningBest());          // of record: not scored
        await c.RedoAsync();
        c.SubmitPlay(TestFixtures.OpeningAlternative());   // practice: the best, discarded

        Assert.Equal(1, c.SkippedCount);
        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.True(ScoredReview(c.Review).IsCorrect);     // what is displayed
    }

    [Theory]
    [InlineData(PlayRanking.Equity)]
    [InlineData(PlayRanking.DepthFirst)]
    public async Task StartAsync_HandsTheFactoryTheRanking_AndKeepsItForTheRun(PlayRanking ranking)
    {
        var c = MakeRankingCapturing(out var handed, TestFixtures.DepthSplitDecision());

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, ranking);

        Assert.Equal(ranking, handed());
        Assert.Equal(ranking, c.Ranking);
    }

    [Fact]
    public async Task SummarizeMatchesAsync_HandsTheFactoryTheRanking_AndLeavesTheRunsAlone()
    {
        // The count is taken under the ranking a Start would take — the
        // caller's — and, touching no run, changes the running quiz's ranking
        // not at all.
        var c = MakeRankingCapturing(out var handed, TestFixtures.DepthSplitDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SummarizeMatchesAsync(new FilterConfig(), PlayRanking.DepthFirst);

        Assert.Equal(PlayRanking.DepthFirst, handed());
        Assert.Equal(PlayRanking.Equity, c.Ranking);
    }

    [Fact]
    public async Task RestartAsync_RunsUnderTheRankingItIsHanded()
    {
        // A restart is a new run, and a run takes the ranking the setting names
        // when it begins: a ranking changed since the last run applies here,
        // to the pool and to scoring alike.
        var c = MakeRankingCapturing(out var handed, TestFixtures.DepthSplitDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.RestartAsync(PlayRanking.DepthFirst);
        c.SubmitPlay(TestFixtures.OpeningAlternative());

        Assert.Equal(PlayRanking.DepthFirst, handed());
        Assert.Equal(PlayRanking.DepthFirst, c.Ranking);
        Assert.True(ScoredReview(c.Review).IsCorrect); // the rollout is best under depth first
        Assert.Equal(1, c.Score.PlayDecisions.Correct);
    }

    [Fact]
    public async Task RefusedStart_KeepsTheRunsRanking()
    {
        // A refused weighted start replaces no active-run state, and the run's
        // ranking is active-run state: the quiz under way keeps being scored
        // under the ranking it began with.
        var sink = new FakeProblemStatsSink { CanWeightMix = false };
        var fake = new FakeProblemSetSource([TestFixtures.DepthSplitDecision()]);
        var c = new QuizController((_, _, _) => TestFixtures.Composed(fake), sink, TimeProvider.System);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.DepthFirst);

        var outcome = await c.StartAsync(
            new FilterConfig(), new QuizMix([new QuizMixEntry(QuizCategory.EverythingElse, 100)]),
            PlayRanking.Equity);

        Assert.Equal(QuizStartOutcome.MixRequiresStats, outcome);
        Assert.Equal(PlayRanking.DepthFirst, c.Ranking);
    }

    [Fact]
    public void Ranking_BeforeAnyStart_Throws()
    {
        // No run, no ranking — and no default standing in for one.
        Assert.Throws<InvalidOperationException>(() => Make().Ranking);
    }

    [Fact]
    public async Task StartAsync_UndefinedRanking_IsRefusedBeforeAnythingBegins()
    {
        var c = MakeRankingCapturing(out var handed, TestFixtures.DepthSplitDecision());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => c.StartAsync(new FilterConfig(), QuizMix.Empty, (PlayRanking)99));

        Assert.False(c.HasStarted);
        Assert.Null(handed()); // the factory was never reached
    }

    [Fact]
    public async Task RestartAsync_UndefinedRanking_IsRefused_AndTheRunStands()
    {
        var c = Make(TestFixtures.DepthSplitDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.DepthFirst);
        var current = c.Current;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => c.RestartAsync((PlayRanking)99));

        Assert.False(c.IsBusy);
        Assert.Same(current, c.Current);
        Assert.Equal(PlayRanking.DepthFirst, c.Ranking);
    }

    // -----------------------------------------------------------------------
    //  Each answer instrument answers its own kind
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitPlay_OnACubeDecision_Throws_AndScoresNothing()
    {
        // The page routes each kind to its own instrument, so this is a caller
        // bug, and it fails loud rather than filing a cube as an off-list play.
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        Assert.Throws<InvalidOperationException>(() => c.SubmitPlay(BestPlay()));

        Assert.Null(c.Review);
        Assert.Equal(0, c.SkippedCount);
    }

    [Fact]
    public async Task SubmitCubeAnswer_OnACheckerPlay_Throws_AndScoresNothing()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        Assert.Throws<InvalidOperationException>(() => c.SubmitCubeAnswer(CubeAnswer.DoubleTake));

        Assert.Null(c.Review);
        Assert.Equal(QuizScore.Empty, c.Score);
    }

    // -----------------------------------------------------------------------
    //  SubmitCubeAnswer — scoring (enters review; ContinueAsync advances)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitCubeAnswer_BestAnswer_IsCorrect_AndAddsOneToEachRowItCommitsTo()
    {
        // The default fixture's truth is Double / Take. Answered so, the whole
        // answer costs nothing: one decision in Double and one in Take (it
        // commits to a response), each correct, and ONE answer in the Total —
        // the session score counts a cube answer once (SPEC-scoring.md §3,
        // 2026-10-01).
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitCubeAnswer(CubeAnswer.DoubleTake);

        var sub = CubeReview(c.Review);
        Assert.True(sub.IsCorrect);
        Assert.Equal(0.0, sub.Cost.Total, 6);

        Assert.Equal(1, c.Score.DoubleDecisions.Submitted);
        Assert.Equal(1, c.Score.DoubleDecisions.Correct);
        Assert.Equal(1, c.Score.TakeDecisions.Submitted);
        Assert.Equal(1, c.Score.TakeDecisions.Correct);
        Assert.Equal(0, c.Score.PlayDecisions.Submitted);
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(1, c.Score.Total.Correct);
    }

    [Fact]
    public async Task SubmitCubeAnswer_NoDouble_AddsToDoubleAndTotal_NotToTake()
    {
        // No double commits to no response — its implied take is never
        // charged — so it is left out of the Take row while still adding to
        // Double and to the Total (SPEC-scoring.md §3, 2026-10-01). At the
        // default Double / Take position it costs T − N = 0.20, all of it in
        // the doubling part.
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitCubeAnswer(CubeAnswer.NoDouble);

        Assert.False(CubeReview(c.Review).IsCorrect);
        Assert.Equal(1, c.Score.DoubleDecisions.Submitted);
        Assert.Equal(0, c.Score.DoubleDecisions.Correct);
        Assert.Equal(0.20, c.Score.DoubleDecisions.TotalEquityLoss, 6);
        Assert.Equal(ScoreSegment.Empty, c.Score.TakeDecisions);
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(0, c.Score.Total.Correct);
        Assert.Equal(0.20, c.Score.Total.TotalEquityLoss, 6);
    }

    [Fact]
    public async Task SubmitCubeAnswer_WrongAnswer_IsOneAnswerInTheTotal_ItsLossTheWholeCost()
    {
        // The fourth answer (Too good here: money, cube turned, so gammons are
        // possible) at a Double / Take position commits to its pass, so it adds
        // to Double AND Take; the Total still counts it once, with the whole
        // cost as its loss — the rows' losses summed — and incorrect.
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitCubeAnswer(CubeAnswer.NoDoublePass);

        var sub = CubeReview(c.Review);
        Assert.False(sub.IsCorrect);
        Assert.Equal(1, c.Score.DoubleDecisions.Submitted);
        Assert.Equal(1, c.Score.TakeDecisions.Submitted);
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(0, c.Score.Total.Correct);
        Assert.Equal(sub.Cost.Total, c.Score.Total.TotalEquityLoss, 6);
        Assert.Equal(sub.Cost.DoublingPart, c.Score.DoubleDecisions.TotalEquityLoss, 6);
        Assert.Equal(sub.Cost.TakePart, c.Score.TakeDecisions.TotalEquityLoss, 6);
    }

    [Fact]
    public async Task SubmitCubeAnswer_TwoPartsThatCountAsZero_DoNotMakeTheWholeCorrect()
    {
        // The Total's correct count follows the WHOLE answer (SPEC-scoring.md
        // §3, "When a cost counts as zero"). N = 0.99994, T = 0.99997, gammons
        // not possible (money, Jacoby, cube centred): No double / Pass costs
        // 0.00003 to double plus 0.00003 to pass — each shows 0.0000, so the
        // Double and Take rows count it correct — but its whole cost, 0.00006,
        // shows 0.0001, so the Total does not.
        var c = Make(TestFixtures.CubeDecision(
            noDoubleEquity: 0.99994, doubleTakeEquity: 0.99997, cubeOwner: CubeOwner.Centered));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitCubeAnswer(CubeAnswer.NoDoublePass);

        var sub = CubeReview(c.Review);
        Assert.True(sub.IsDoublingPartCorrect);
        Assert.True(sub.IsTakePartCorrect);
        Assert.False(sub.IsCorrect);
        Assert.Equal(1, c.Score.DoubleDecisions.Correct);
        Assert.Equal(1, c.Score.TakeDecisions.Correct);
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(0, c.Score.Total.Correct);
    }

    [Fact]
    public async Task SubmitCubeAnswer_CarriesProblemKeyIntoTheFold()
    {
        // Wire: the cube submission must carry the answered problem's content
        // identity into the answer of record — the cube analog of
        // SubmitPlay_CarriesProblemKeyIntoTheFold. Distinctive key pins the carry.
        var problem = TestFixtures.CubeDecision(away: 7);
        var c = MakeWithSink(out var sink, problem);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitCubeAnswer(CubeAnswer.DoubleTake);
        await c.ContinueAsync();

        Assert.Equal(ProblemKey.From(problem), Assert.Single(sink.Cubes).ProblemKey);
    }

    [Fact]
    public async Task SubmitCubeAnswer_SetsCubeReview_OverTheDecisionOnScreen_DoesNotAdvance()
    {
        // Submit scores and enters review without advancing. The review keeps
        // the scored answer and the very decision it was scored at — the
        // record on screen — which the verdict reads its labels and its Best
        // list from; and its scored answer is the one the score was folded
        // from.
        var d1 = TestFixtures.CubeDecision();
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(d1, d2);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitCubeAnswer(CubeAnswer.DoublePass);

        Assert.Same(d1, c.Current); // unchanged — no advance
        Assert.False(c.IsFinished);
        var review = Assert.IsType<ProblemReview.Cube>(c.Review);
        Assert.Same(d1, review.Decision);
        Assert.Equal(CubeAnswer.DoublePass, review.Submission.Answer);
        Assert.Equal(review.Submission.Cost.Total, c.Score.Total.TotalEquityLoss, 6);
    }

    [Fact]
    public async Task SubmitCubeAnswer_TheFourthAnswer_IsCorrectWhereItIsTheTruth()
    {
        // Too good / Pass (N = 1.2 above the cash, T = 1.5 a pass, gammons
        // possible): the fourth answer costs nothing, so the whole answer and
        // both rows it adds to are correct.
        var c = Make(TestFixtures.CubeDecision(noDoubleEquity: 1.2, doubleTakeEquity: 1.5));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitCubeAnswer(CubeAnswer.NoDoublePass);

        var sub = CubeReview(c.Review);
        Assert.Equal(CubeAnswer.NoDoublePass, sub.BestAnswer);
        Assert.True(sub.IsCorrect);
        Assert.Equal(1, c.Score.DoubleDecisions.Correct);
        Assert.Equal(1, c.Score.TakeDecisions.Correct);
        Assert.Equal(1, c.Score.Total.Correct);
    }

    [Fact]
    public async Task SubmitCubeAnswer_IsScoredByTheProducer_AtTheDecisionOnScreen()
    {
        // The submission is SubmittedCubeAnswer.Score(answer, decision) at the
        // record on screen — never assembled by hand. Its equality is the
        // problem and the answer only, so the cost, the truth and the verdict
        // are compared too: a scoring that read another record, or restated a
        // cost rule, would differ in them. The position (No double / Take at
        // 0.8 / 0.7, a match) makes Double / Pass cost a distinctive 0.4.
        var problem = TestFixtures.CubeDecision(noDoubleEquity: 0.8, doubleTakeEquity: 0.7, away: 5);
        var c = Make(problem);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitCubeAnswer(CubeAnswer.DoublePass);

        var expected = SubmittedCubeAnswer.Score(CubeAnswer.DoublePass, problem);
        var actual = CubeReview(c.Review);
        Assert.Equal(expected, actual);
        Assert.Equal(expected.Cost, actual.Cost);
        Assert.Equal(expected.BestAnswer, actual.BestAnswer);
        Assert.Equal(expected.IsCorrect, actual.IsCorrect);
        Assert.Equal(0.4, actual.Cost.Total, 6);
    }

    [Fact]
    public async Task ContinueAsync_AfterCubeSubmit_Advances()
    {
        var d1 = TestFixtures.CubeDecision();
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(d1, d2);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitCubeAnswer(CubeAnswer.DoubleTake);
        Assert.Same(d1, c.Current);

        await c.ContinueAsync();

        Assert.Null(c.Review);
        Assert.Same(d2, c.Current);
        Assert.False(c.IsFinished);
    }

    [Fact]
    public async Task SubmitCubeAnswer_WhileReviewSet_NoOp()
    {
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitCubeAnswer(CubeAnswer.DoubleTake);
        var reviewBefore = c.Review;

        c.SubmitCubeAnswer(CubeAnswer.NoDoublePass);

        Assert.Same(reviewBefore, c.Review);
        Assert.Equal(1, c.Score.DoubleDecisions.Submitted);
        Assert.Equal(1, c.Score.Total.Correct); // and still the first answer's
    }

    [Fact]
    public async Task SubmitCubeThenContinue_LastProblem_FlipsIsFinished()
    {
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitCubeAnswer(CubeAnswer.DoubleTake);
        Assert.False(c.IsFinished); // review first

        await c.ContinueAsync();

        Assert.True(c.IsFinished);
        Assert.Null(c.Current);
    }

    [Fact]
    public async Task SubmitCubeAnswer_BeforeStart_NoOp()
    {
        var c = Make();

        c.SubmitCubeAnswer(CubeAnswer.DoubleTake);

        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Null(c.Review);
    }

    [Fact]
    public async Task SubmitCubeAnswer_AfterFinish_NoOp()
    {
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitCubeAnswer(CubeAnswer.DoubleTake);
        await c.ContinueAsync(); // exhausts
        Assert.True(c.IsFinished);

        var scoreBefore = c.Score;
        c.SubmitCubeAnswer(CubeAnswer.DoubleTake);

        Assert.Equal(scoreBefore, c.Score);
        Assert.Null(c.Review);
    }

    [Fact]
    public async Task RestartAsync_ClearsTheCubeScoreAndReview()
    {
        var c = Make(
            TestFixtures.CubeDecision(),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitCubeAnswer(CubeAnswer.DoubleTake);
        Assert.Equal(1, c.Score.DoubleDecisions.Submitted);
        Assert.NotNull(c.Review);

        await c.RestartAsync(PlayRanking.Equity);

        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Null(c.Review);
    }

    // -----------------------------------------------------------------------
    //  RedoAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RedoAsync_OutsideReview_NoOp()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        Assert.Null(c.Review);
        var current = c.Current;

        await c.RedoAsync();

        Assert.Same(current, c.Current);
        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Equal(0, c.SkippedCount);
        Assert.False(c.IsFinished);
    }

    [Fact]
    public async Task RedoAsync_AfterCorrectPlay_LeavesTheAnswerOfRecordStanding()
    {
        // SPEC-scoring.md §2: redo re-opens the problem, never the record. Only
        // Review clears — Score and SkippedCount are exactly as the first
        // submission left them, and it is that submission which folds.
        var c = MakeWithSink(out var sink, TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        var current = c.Current;

        c.SubmitPlay(BestPlay());
        Assert.NotNull(c.Review);
        var recorded = ScoredReview(c.Review);
        var scored = c.Score;
        Assert.Equal(1, scored.Total.Submitted);

        await c.RedoAsync();

        Assert.Equal(scored, c.Score);
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(1, c.Score.Total.Correct);
        Assert.Equal(0, c.SkippedCount);
        Assert.Null(c.Review);
        Assert.Same(current, c.Current); // unchanged — same problem, answering state
        Assert.False(c.IsFinished);

        // The record is still the first submission itself: moving on folds it.
        await c.SkipCurrentAsync();
        Assert.Same(recorded, Assert.Single(sink.Plays));
    }

    [Fact]
    public async Task RedoAsync_AfterIncorrectPlay_LeavesTheEquityLossStanding()
    {
        // The equity a wrong first answer lost is not refundable by redoing.
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        var current = c.Current;

        c.SubmitPlay(AltPlay());
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(0.05, c.Score.Total.TotalEquityLoss, 6);

        await c.RedoAsync();

        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(0.05, c.Score.Total.TotalEquityLoss, 6);
        Assert.Null(c.Review);
        Assert.Same(current, c.Current);
    }

    [Fact]
    public async Task RedoAsync_AfterOffListPlay_LeavesTheSkipStanding()
    {
        // §2: a skip is of record too — an off-list submission counts as one
        // and redoing does not un-count it. (The prior model decremented here.)
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        var current = c.Current;

        c.SubmitPlay(UnknownPlay());
        Assert.Equal(1, c.SkippedCount);
        Assert.Equal(QuizScore.Empty, c.Score);
        OffListReview(c.Review);

        await c.RedoAsync();

        Assert.Equal(1, c.SkippedCount);
        Assert.Equal(QuizScore.Empty, c.Score); // an off-list play never scored
        Assert.Null(c.Review);
        Assert.Same(current, c.Current);
    }

    [Fact]
    public async Task RedoAsync_AfterOffListPlay_ThenAnOnListPractice_ScoresNothing()
    {
        // The skip stands AND the retry is recordless — the two halves of
        // redo-after-skip together. An on-list retry is the strongest form:
        // under the prior model it replaced the skip with a scored answer.
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(UnknownPlay()); // of record: a skip
        await c.RedoAsync();
        c.SubmitPlay(BestPlay());    // practice: on-list, correct, and discarded

        Assert.Equal(1, c.SkippedCount);
        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.True(ScoredReview(c.Review).IsCorrect); // still reviewed
    }

    [Fact]
    public async Task RedoAsync_AfterCubeSubmission_LeavesTheAnswerOfRecordStanding()
    {
        var c = MakeWithSink(out var sink, TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        var current = c.Current;

        c.SubmitCubeAnswer(CubeAnswer.DoubleTake);
        Assert.Equal(1, c.Score.Total.Submitted); // one answer, counted once
        var recorded = CubeReview(c.Review);
        var scored = c.Score;

        await c.RedoAsync();

        Assert.Equal(scored, c.Score);
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Null(c.Review);
        Assert.Same(current, c.Current);

        // The record is still the first submission itself: moving on folds it.
        await c.SkipCurrentAsync();
        Assert.Same(recorded, Assert.Single(sink.Cubes));
    }

    [Fact]
    public async Task RedoAsync_AfterCubeSubmission_LeavesEarlierPlaySegmentIntact()
    {
        // Interleaved answers across a redo: neither segment moves. The play
        // answered and continued past stays folded into PlayDecisions, and the
        // cube problem's own answer of record stays in DoubleDecisions /
        // TakeDecisions — redo touches no score segment at all.
        var play = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05);
        var cube = TestFixtures.CubeDecision();
        var c = Make(play, cube);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(AltPlay()); // incorrect, 0.05 loss
        await c.ContinueAsync();
        Assert.Same(cube, c.Current);

        c.SubmitCubeAnswer(CubeAnswer.NoDoublePass); // wrong
        Assert.Equal(1, c.Score.DoubleDecisions.Submitted);

        await c.RedoAsync();

        Assert.Equal(1, c.Score.PlayDecisions.Submitted); // play segment untouched
        Assert.Equal(0.05, c.Score.PlayDecisions.TotalEquityLoss, 6);
        Assert.Equal(1, c.Score.DoubleDecisions.Submitted);
        Assert.Equal(1, c.Score.TakeDecisions.Submitted);
        Assert.Same(cube, c.Current); // still on the cube problem, answering state
        Assert.Null(c.Review);
    }

    [Fact]
    public async Task RedoAsync_ThenResubmitDifferentAnswer_ScoresOnlyTheFirstAnswer()
    {
        // The headline reversal (§2): the first submission is the answer of
        // record and no later gesture amends it. Under the prior model this
        // scored the SECOND answer — one correct, zero loss.
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(AltPlay());  // of record: incorrect, 0.05 lost
        await c.RedoAsync();
        c.SubmitPlay(BestPlay()); // practice: correct, and discarded

        // One answer of record, and it is the first: not correct, 0.05 lost.
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(0, c.Score.Total.Correct);
        Assert.Equal(0.05, c.Score.Total.TotalEquityLoss, 6);
    }

    [Fact]
    public async Task RedoAsync_ManyPracticeCycles_AreEquallyRecordless()
    {
        // "Practice cycles are unbounded; each is equally recordless" (§2) —
        // the record is written once and never again, however many times the
        // user goes round.
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(AltPlay()); // of record
        var scored = c.Score;

        for (var cycle = 0; cycle < 5; cycle++)
        {
            await c.RedoAsync();
            c.SubmitPlay(cycle % 2 == 0 ? BestPlay() : AltPlay());
            Assert.Equal(scored, c.Score);
            Assert.Equal(1, c.Score.Total.Submitted);
            Assert.Equal(0, c.Score.Total.Correct);
            Assert.Equal(0.05, c.Score.Total.TotalEquityLoss, 6);
            Assert.Equal(0, c.SkippedCount);
        }
    }

    [Fact]
    public async Task RedoAsync_ThenPracticeOffList_AddsNoSecondSkip()
    {
        // The off-list branch is recordless in the practice direction too: a
        // practice submission that misses the candidate list must not mint a
        // skip on a problem that is already answered.
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(BestPlay()); // of record: on-list, correct
        await c.RedoAsync();
        c.SubmitPlay(UnknownPlay()); // practice: off-list

        Assert.Equal(0, c.SkippedCount);
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(1, c.Score.Total.Correct);
        OffListReview(c.Review); // still reviewed
    }

    [Fact]
    public async Task RedoAsync_ThenPracticeCube_LeavesTheCubeScoreAtTheOriginal()
    {
        // The cube kind's own practice pin: the answer of record stands in
        // every row it added to, and the practice answer — correct, where the
        // record is not — adds to none.
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitCubeAnswer(CubeAnswer.NoDoublePass); // of record
        var recordedDoubleCorrect = c.Score.DoubleDecisions.Correct;
        var recordedTakeCorrect = c.Score.TakeDecisions.Correct;

        await c.RedoAsync();
        c.SubmitCubeAnswer(CubeAnswer.DoubleTake); // practice

        Assert.Equal(1, c.Score.DoubleDecisions.Submitted);
        Assert.Equal(1, c.Score.TakeDecisions.Submitted);
        Assert.Equal(recordedDoubleCorrect, c.Score.DoubleDecisions.Correct);
        Assert.Equal(recordedTakeCorrect, c.Score.TakeDecisions.Correct);
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(0, c.Score.Total.Correct);
    }

    [Fact]
    public async Task PracticeSubmission_IsReviewedAndMarkedPractice()
    {
        // §2's "practice still reviews", plus this arc's design call on how it
        // shows: the practice submission gets the normal scored review, flagged
        // IsPractice; the answer of record's review is not flagged.
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(AltPlay());
        var ofRecord = Assert.IsType<ProblemReview.Play>(c.Review);
        Assert.False(ofRecord.IsPractice);
        Assert.False(ScoredReview(ofRecord).IsCorrect);

        await c.RedoAsync();
        c.SubmitPlay(BestPlay());

        var practice = Assert.IsType<ProblemReview.Play>(c.Review);
        Assert.True(practice.IsPractice);
        Assert.True(ScoredReview(practice).IsCorrect); // scored on its own merits, not the record's
        Assert.Equal(0.0, ScoredReview(practice).EquityLoss, 6);
    }

    [Fact]
    public async Task PracticeCubeSubmission_IsReviewedAndMarkedPractice()
    {
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitCubeAnswer(CubeAnswer.NoDoublePass);
        Assert.False(Assert.IsType<ProblemReview.Cube>(c.Review).IsPractice);

        await c.RedoAsync();
        var practiceAnswer = CubeAnswer.DoubleTake;
        c.SubmitCubeAnswer(practiceAnswer);

        var practice = Assert.IsType<ProblemReview.Cube>(c.Review);
        Assert.True(practice.IsPractice);
        Assert.Equal(practiceAnswer, practice.Submission.Answer); // the practice answer, not the record's
    }

    [Fact]
    public async Task RedoAsync_FiresStateChanged()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitPlay(BestPlay());
        Assert.NotNull(c.Review);

        await c.SkipCurrentAsync();

        Assert.Equal(0, c.SkippedCount);
        Assert.Same(d1, c.Current); // not advanced
        Assert.NotNull(c.Review);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SkipCurrentAsync_OnTheLastAvailableProblem_FinishesTheRunWithItCountedAsSkipped(bool aPassFollows)
    {
        // Natural exhaustion with a deferred final problem (SPEC-quiz-history.md
        // §4 and §5, amended 2026-09-30). Skip completes nothing, and the source
        // has no problem to present beyond this one, so the run finishes — and
        // finishing, not End quiz, is what converts a problem still unresolved.
        // Unconverted, it would be the unresolved frontier of a finished run,
        // which the count leaves out; converted, it is a skip of record and
        // counted, beside the answered work before it, and it folds nothing.
        // A trailing position passed over silently makes the problem the last
        // available one without being the source's last item: there the run
        // learns it has nothing left only by drawing past it.
        var items = new List<BgDecisionData>
        {
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
        };
        if (aPassFollows) items.Add(TestFixtures.PassDecision());
        var c = MakeWithSink(out var sink, [.. items]);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();
        var folded = Assert.Single(sink.Plays);

        await c.SkipCurrentAsync();

        Assert.True(c.IsFinished);
        Assert.Null(c.Current);
        Assert.Equal(1, c.SkippedCount);
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
        Assert.Same(folded, Assert.Single(sink.Plays));
        Assert.Empty(sink.Cubes);
    }

    // -----------------------------------------------------------------------
    //  EndQuizAsync — the user's own exit from a run (issue halheinrich/backgammon#57)
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
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
        // is abandoned: it records no answer, and the run finishing converts it
        // to a skip of record, counted as every problem the Skip button moved on
        // from is counted — so Done's "problems shown" still counts a problem
        // the user actually saw.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();          // problem 1 answered and finalized

        await c.EndQuizAsync();           // quitting on problem 2, unanswered

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitPlay(BestPlay());
        Assert.NotNull(c.Review);

        await c.EndQuizAsync();

        Assert.True(c.IsFinished);
        Assert.Null(c.Review);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(AltPlay());
        Assert.Equal(0, sink.TotalFolds); // submit alone still folds nothing
        var submitted = ScoredReview(c.Review);

        await c.EndQuizAsync();

        Assert.Same(submitted, Assert.Single(sink.Plays));
        Assert.Empty(sink.Cubes);
    }

    [Fact]
    public async Task EndQuizAsync_FromReview_OfAnOffListPlay_FoldsNothing()
    {
        // The off-list carve-out rides along unchanged: that submission is a
        // skip of record and left no answer, so there is nothing to fold — and
        // the skip it already recorded must not be counted again by the end.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync(); // exhaust
        Assert.True(c.IsFinished);

        await c.EndQuizAsync();

        Assert.Equal(0, c.SkippedCount);
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
    }

    [Fact]
    public async Task EndQuizAsync_TwiceInARow_CountsOneSkip()
    {
        // The same guard from the other side: the first call finished the run,
        // so the second finds nothing showing and does nothing.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.EndQuizAsync();
        await c.EndQuizAsync();

        Assert.Equal(1, c.SkippedCount);
    }

    [Fact]
    public async Task EndQuizAsync_AfterSkips_CountsEveryDeferredProblemAndTheAbandonedOne()
    {
        // Several problems unresolved at once, as the Skip button now leaves
        // them: the two it moved on from, deferred behind the frontier and
        // counted from the moment the next problem landed, and the frontier the
        // user quits on. Ending converts all three to skips of record
        // (QuizRun.End, where the conversion is pinned problem by problem); the
        // count the user sees rises by the abandoned problem alone — the same
        // numbers the controller showed before a Skip deferred anything.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SkipCurrentAsync();
        await c.SkipCurrentAsync();
        Assert.Equal(2, c.SkippedCount);
        Assert.Equal(3, c.ProblemNumber);

        await c.EndQuizAsync();

        Assert.True(c.IsFinished);
        Assert.Equal(3, c.SkippedCount);
        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Equal(0, sink.TotalFolds);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.EndQuizAsync();

        await c.RestartAsync(PlayRanking.Equity);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.RestartAsync(PlayRanking.Equity));
        Assert.False(c.HasStarted);
    }

    [Fact]
    public async Task RestartAsync_ResetsScoreAndSkippedCount()
    {
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();
        await c.SkipCurrentAsync();
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
        Assert.Equal(1, c.SkippedCount);

        await c.RestartAsync(PlayRanking.Equity);

        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Equal(0, c.SkippedCount);
        Assert.False(c.IsFinished);
        Assert.NotNull(c.Current);
    }

    [Fact]
    public async Task RestartAsync_RespawnsSourceFromFactory()
    {
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var fake = new FakeProblemSetSource([d]);
        var c = new QuizController((_, _, _) => TestFixtures.Composed(fake), new FakeProblemStatsSink(), TimeProvider.System);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        Assert.Equal(1, fake.EnumerateCallCount);

        await c.RestartAsync(PlayRanking.Equity);
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
        await Assert.ThrowsAsync<ArgumentNullException>(() => c.SummarizeMatchesAsync(null!, PlayRanking.Equity));
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

        var summary = await c.SummarizeMatchesAsync(new FilterConfig(), PlayRanking.Equity);

        Assert.Equal(3, summary.AnswerTypes.Total);
        Assert.Equal(3, summary.AnswerTypes.CheckerPlays);
    }

    [Fact]
    public async Task SummarizeMatchesAsync_BucketsEachDecisionByItsAnswerType()
    {
        // Classification is the producer's: each cube decision is bucketed by
        // its truth, the best of the four answers, which the producer derives
        // from the equities (SPEC-scoring §3). One of each kind in, one in
        // each bucket out. The last record is XG's "too good to double/Take" —
        // no double above the cash, opponent takes — whose truth is No double,
        // so it counts there: the fourth bucket holds only positions where the
        // opponent would pass.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),                     // checker
            TestFixtures.CubeDecision(noDoubleEquity: 0.8, doubleTakeEquity: 0.7),     // no double / take
            TestFixtures.CubeDecision(noDoubleEquity: 0.5, doubleTakeEquity: 0.7),     // double / take
            TestFixtures.CubeDecision(noDoubleEquity: 0.5, doubleTakeEquity: 1.5),     // double / pass
            TestFixtures.CubeDecision(noDoubleEquity: 1.2, doubleTakeEquity: 1.5),     // too good / pass
            TestFixtures.CubeDecision(noDoubleEquity: 1.2, doubleTakeEquity: 0.9));    // no double / take, by ruling

        var summary = await c.SummarizeMatchesAsync(new FilterConfig(), PlayRanking.Equity);

        Assert.Equal(new AnswerTypeDistribution(
            CheckerPlays: 1, NoDouble: 2, DoubleTake: 1, DoublePass: 1, NoDoublePass: 1),
            summary.AnswerTypes);
        Assert.Equal(6, summary.AnswerTypes.Total);
    }

    [Fact]
    public async Task SummarizeMatchesAsync_EmptySource_ReturnsEmpty()
    {
        var c = Make();
        Assert.Equal(
            new MatchSummary(AnswerTypeDistribution.Empty, 0),
            await c.SummarizeMatchesAsync(new FilterConfig(), PlayRanking.Equity));
    }

    [Fact]
    public async Task SummarizeMatchesAsync_CarriesTheStacksCollapseMagnitude()
    {
        // Issue halheinrich/backgammon#104. The pool the summary counts is
        // already deduplicated, so the count on its own cannot be reconciled
        // with what the user picked. The composed stack reports how many
        // matching records it dropped as duplicates, and the summary carries
        // that number alongside the distribution measured in the same pass —
        // one enumeration, so the two halves describe the same pool.
        //
        // What the dedupe layer itself collapses is pinned against a real parse
        // in PositionDedupeTests; what is owed here is that the controller
        // reports the stack's number rather than inventing or dropping one.
        var fake = new FakeProblemSetSource([
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay())]);
        var c = new QuizController(
            (_, _, _) => TestFixtures.Composed(fake, duplicatesCollapsed: 7),
            new FakeProblemStatsSink(), TimeProvider.System);

        var summary = await c.SummarizeMatchesAsync(new FilterConfig(), PlayRanking.Equity);

        Assert.Equal(7, summary.DuplicatesCollapsed);
        // The collapsed copies are never folded back into the count: the pool
        // is what the stack actually yielded.
        Assert.Equal(2, summary.AnswerTypes.Total);
    }

    [Fact]
    public async Task SummarizeMatchesAsync_BuildsControllerOwnedPipelineFromConfig()
    {
        // The summary reflects exactly what a Start with this config would admit:
        // it builds the same controller-owned pipeline (FilterConfig.Build) and
        // hands it to the factory. Capture that pipeline and assert the user's
        // PlayerFilter survived the materialization.
        var c = MakeCapturing(out var captured, TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));

        await c.SummarizeMatchesAsync(new FilterConfig { Players = ["Alice"] }, PlayRanking.Equity);

        var pipeline = captured();
        Assert.NotNull(pipeline);
        Assert.True(pipeline!.Matches(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), onRoll: "Alice").ViewFor(PlayRanking.Equity)));
        Assert.False(pipeline.Matches(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), onRoll: "Bob").ViewFor(PlayRanking.Equity)));
    }

    [Fact]
    public async Task SummarizeMatchesAsync_DoesNotDisturbLiveQuiz()
    {
        // The throwaway-enumerator guarantee: summarizing mid-quiz touches no
        // live state — Current, Score, the skip count and IsFinished all survive.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync(); // now on the second problem, one scored
        var currentIdBefore = c.Current!.Id;
        var scoreBefore = c.Score;
        var skippedBefore = c.SkippedCount;

        var summary = await c.SummarizeMatchesAsync(new FilterConfig(), PlayRanking.Equity);

        Assert.Equal(2, summary.AnswerTypes.Total);
        Assert.Equal(currentIdBefore, c.Current!.Id);
        Assert.Equal(scoreBefore, c.Score);
        Assert.Equal(skippedBefore, c.SkippedCount);
        Assert.False(c.IsFinished);
    }

    // -----------------------------------------------------------------------
    //  Lifetime-stats sink — bind at Start/Restart, fold on leaving review
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_BindsStatsContextOnce()
    {
        var c = MakeWithSink(out var sink, TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        Assert.Equal(1, sink.BeginQuizCallCount);
        Assert.Equal(0, sink.TotalFolds); // binding never folds
    }

    [Fact]
    public async Task RestartAsync_RebindsStatsContext()
    {
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.RestartAsync(PlayRanking.Equity);

        Assert.Equal(2, sink.BeginQuizCallCount);
    }

    [Fact]
    public async Task SubmitThenContinue_Play_FoldsExactlyTheSubmittedPlayOnce()
    {
        // Fold happens on Continue (leaving review), not at Submit — and folds
        // the very submission the live review showed, which is the record's.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(AltPlay());
        Assert.Equal(0, sink.TotalFolds); // submit alone must not fold
        var submitted = ScoredReview(c.Review);

        await c.ContinueAsync();

        Assert.Same(submitted, Assert.Single(sink.Plays));
        Assert.Empty(sink.Cubes);
    }

    [Fact]
    public async Task ContinueAsync_FoldsWhileTheReviewIsStillOnScreen()
    {
        // The order inside an advance: the fold is awaited before the run moves
        // on from the problem, so a slow stats write leaves the review up, with
        // its buttons showing busy, rather than a fresh decision on a problem
        // the run is leaving. Read from inside the fold itself.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitPlay(BestPlay());
        var shown = c.Review;
        Assert.NotNull(shown);

        ProblemReview? duringFold = null;
        var busyDuringFold = false;
        sink.OnRecording = () => (duringFold, busyDuringFold) = (c.Review, c.IsBusy);

        await c.ContinueAsync();

        Assert.Same(shown, duringFold);
        Assert.True(busyDuringFold);
        Assert.Null(c.Review); // and gone once the advance has landed
    }

    [Fact]
    public async Task EndQuizAsync_FoldsBeforeTheRunEnds()
    {
        // The same order on the terminal exit: the answer folds while the run
        // is still the live one, its review on screen, and only then does the
        // quiz read as finished.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitPlay(BestPlay());
        var shown = c.Review;
        Assert.NotNull(shown);

        ProblemReview? duringFold = null;
        bool? finishedDuringFold = null;
        sink.OnRecording = () => (duringFold, finishedDuringFold) = (c.Review, c.IsFinished);

        await c.EndQuizAsync();

        Assert.Same(shown, duringFold);
        Assert.False(finishedDuringFold);
        Assert.True(c.IsFinished);
    }

    [Fact]
    public async Task SubmitThenContinue_Cube_FoldsExactlyTheSubmittedCubeOnce()
    {
        var c = MakeWithSink(out var sink, TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitCubeAnswer(CubeAnswer.DoubleTake);
        Assert.Equal(0, sink.TotalFolds);
        var submitted = CubeReview(c.Review);

        await c.ContinueAsync();

        Assert.Same(submitted, Assert.Single(sink.Cubes));
        Assert.Empty(sink.Plays);
    }

    [Fact]
    public async Task SubmitRedoResubmitContinue_FoldsTheAnswerOfRecord()
    {
        // What folds is the answer of record, not the displayed review — the
        // split SPEC-scoring.md §2 names. Here they differ: the review on screen
        // at Continue is the correct practice answer; what reaches the lifetime
        // record is the incorrect first one. Under the prior model this folded
        // the second submission.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(AltPlay());  // of record: incorrect
        var recorded = ScoredReview(c.Review);
        await c.RedoAsync();
        c.SubmitPlay(BestPlay()); // practice: correct
        Assert.True(ScoredReview(c.Review).IsCorrect); // what is displayed

        await c.ContinueAsync();

        var folded = Assert.Single(sink.Plays);
        Assert.Same(recorded, folded);
        Assert.False(folded.IsCorrect);
    }

    [Fact]
    public async Task ManyPracticeCyclesThenContinue_FoldTheAnswerOfRecordExactlyOnce()
    {
        // Practice cycles are invisible to the fold however many there are —
        // not merely "the last one wins", but "none of them reach the sink".
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(AltPlay());
        var recorded = ScoredReview(c.Review);
        for (var cycle = 0; cycle < 4; cycle++)
        {
            await c.RedoAsync();
            c.SubmitPlay(BestPlay());
            Assert.Equal(0, sink.TotalFolds); // no practice submission ever folds
        }

        await c.ContinueAsync();

        Assert.Equal(1, sink.TotalFolds);
        Assert.Same(recorded, Assert.Single(sink.Plays));
    }

    [Fact]
    public async Task PracticeCubeCycleThenContinue_FoldsTheAnswerOfRecord()
    {
        // The cube half of the same split.
        var c = MakeWithSink(out var sink, TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitCubeAnswer(CubeAnswer.NoDoublePass);
        var recorded = CubeReview(c.Review);
        await c.RedoAsync();
        c.SubmitCubeAnswer(CubeAnswer.DoubleTake);

        await c.ContinueAsync();

        Assert.Same(recorded, Assert.Single(sink.Cubes));
        Assert.Empty(sink.Plays);
    }

    [Fact]
    public async Task PracticeCycleThenEndQuizFromReview_FoldsTheAnswerOfRecord()
    {
        // End quiz is the other advance-past-the-problem exit, and it folds the
        // same thing Continue would — the record, not the practice submission
        // whose review is on screen.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(AltPlay());  // of record: incorrect
        var recorded = ScoredReview(c.Review);
        await c.RedoAsync();
        c.SubmitPlay(BestPlay()); // practice: correct

        await c.EndQuizAsync();

        Assert.Same(recorded, Assert.Single(sink.Plays));
        Assert.False(sink.Plays[0].IsCorrect);
        Assert.Equal(0, c.SkippedCount);
    }

    [Fact]
    public async Task EndQuizMidPracticeCycle_FoldsTheAnswerOfRecordAndCountsNoSkip()
    {
        // Ending while a redo has re-opened the problem and nothing has been
        // re-answered: no review is showing, but the problem IS answered. The
        // branch keys on the record, so the answer folds and the problem is not
        // also counted as abandoned — which is what keeps "every answer visible
        // on Done has reached the lifetime record" true through a redo.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(BestPlay());
        var recorded = ScoredReview(c.Review);
        await c.RedoAsync();
        Assert.Null(c.Review); // answering again, nothing submitted this cycle

        await c.EndQuizAsync();

        Assert.Same(recorded, Assert.Single(sink.Plays));
        Assert.Equal(0, c.SkippedCount);
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
    }

    [Fact]
    public async Task SkipMidPracticeCycle_FoldsTheAnswerOfRecordAndCountsNoSkip()
    {
        // Skip is reachable from the practice-answering state, and it advances
        // the run past an answered problem — so it folds the record and counts
        // no skip. Counting one would double-count an answered problem; not
        // folding would strand an answer the score still shows.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        var first = c.Current;

        c.SubmitPlay(BestPlay());
        var recorded = ScoredReview(c.Review);
        await c.RedoAsync();

        await c.SkipCurrentAsync();

        Assert.Same(recorded, Assert.Single(sink.Plays));
        Assert.Equal(0, c.SkippedCount);
        Assert.NotSame(first, c.Current); // and the run did advance
    }

    [Fact]
    public async Task OffListThenOnListPracticeThenContinue_FoldsNothing()
    {
        // The fold must select by the answer of RECORD, not by the shape of the
        // displayed review. Here the two disagree hardest: the record is a skip
        // (no answer of record at all) while the review on screen is an on-list
        // scored play. A fold keyed on the review would fold a submission this
        // problem never recorded.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(UnknownPlay()); // of record: a skip
        await c.RedoAsync();
        c.SubmitPlay(BestPlay());    // practice: on-list
        ScoredReview(c.Review);

        await c.ContinueAsync();

        Assert.Equal(0, sink.TotalFolds);
        Assert.Equal(1, c.SkippedCount);
    }

    [Fact]
    public async Task PracticeSubmissionAlone_FoldsNothing()
    {
        // The fold trigger is the advance, not the submission — stated against
        // the practice cycle specifically.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(BestPlay());
        await c.RedoAsync();
        c.SubmitPlay(AltPlay());

        Assert.Equal(0, sink.TotalFolds);
    }

    [Fact]
    public async Task AnswerAbandonedInReview_ByRestart_NeverFolds()
    {
        // §2's flip side, restated against the new mechanism: the fold happens
        // when the run advances PAST the problem, and a Restart does not — it
        // resets. The model before halheinrich/backgammon#152 reached the same
        // verdict by a different route (an answer was final only once continued
        // past), so this pin has to be re-argued rather than inherited.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(BestPlay());
        Assert.NotNull(c.Review);

        await c.RestartAsync(PlayRanking.Equity);

        Assert.Equal(0, sink.TotalFolds);
        Assert.Equal(QuizScore.Empty, c.Score); // and the run genuinely restarted
    }

    [Fact]
    public async Task AnswerAbandonedMidPracticeCycle_ByRestart_NeverFolds()
    {
        // The same rule where the new model makes it newly reachable: a redo
        // leaves the record standing with no review showing, and a Restart from
        // there still advances past nothing.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(BestPlay());
        await c.RedoAsync();

        await c.RestartAsync(PlayRanking.Equity);

        Assert.Equal(0, sink.TotalFolds);
        Assert.Equal(QuizScore.Empty, c.Score);
    }

    [Fact]
    public async Task OffListSubmitThenContinue_FoldsNothing()
    {
        // Producer contract: off-list plays are skips, never lifetime
        // submissions — there is no answer of record to fold.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SkipCurrentAsync();

        Assert.Equal(0, sink.TotalFolds);
    }

    [Fact]
    public async Task AutoSkippedPassPosition_FoldsNothing()
    {
        // Auto-skipped pass positions were never shown to the user — they
        // must not touch lifetime stats. The forced-play half of the same rule
        // is pinned by AutoSkippedForcedPlay_FoldsNothing.
        var c = MakeWithSink(out var sink,
            TestFixtures.PassDecision(),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        Assert.Equal(0, sink.TotalFolds);
    }

    [Fact]
    public async Task LastProblemContinue_FoldsBeforeFinishing()
    {
        // The final decision's answer folds even though Continue exhausts the
        // source — the fold sits before the advance.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        c.SubmitPlay(AltPlay());
        await c.ContinueAsync();
        c.SubmitCubeAnswer(CubeAnswer.DoubleTake);
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
        // (delivering the end state; PresentNextAsync itself fires nothing).
        // The synchronous Submit fires once as before.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        var fires = 0;
        c.StateChanged += () => fires++;

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity); // +2 — gate on/off around advance to first
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
        out FakeProblemStatsSink sink, out List<QuizMix> mixes, params BgDecisionData[] items)
    {
        var fake = new FakeProblemSetSource(items);
        sink = new FakeProblemStatsSink();
        var captured = new List<QuizMix>();
        mixes = captured;
        return new QuizController(
            (_, _, mix) => { captured.Add(mix); return TestFixtures.Composed(fake); }, sink, TimeProvider.System);
    }

    /// <summary>
    /// A lifetime-stats document holding one correct sighting of
    /// <paramref name="problem"/> — keyed by the problem's content, which is
    /// what the mix classifier now looks up, so the fixtures it discriminates
    /// between must differ in content and not merely in provenance.
    /// </summary>
    private static ProblemStatsDocument DocWithSeen(CheckerPlayDecision problem) =>
        ProblemStatsDocument.Empty.Plus(
            TestFixtures.Scored(problem, BestPlay(), PlayRanking.Equity),
            TimeProvider.System);

    [Fact]
    public void Ctor_NullClock_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new QuizController((_, _, _) => TestFixtures.Composed(new FakeProblemSetSource([])), new FakeProblemStatsSink(), null!));
    }

    [Fact]
    public async Task StartAsync_NullMix_Throws()
    {
        var c = Make();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => c.StartAsync(new FilterConfig(), null!, PlayRanking.Equity));
    }

    [Fact]
    public async Task StartAsync_BlankMix_NoCompositionLayer()
    {
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = MakeWeighable(out _, out var mixes, d);

        var outcome = await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

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

        var outcome = await c.StartAsync(new FilterConfig(), NeverSeenMix(), PlayRanking.Equity);

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

        var outcome = await c.StartAsync(new FilterConfig(), NeverSeenMix(), PlayRanking.Equity);

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
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"), away: 1);
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"), away: 2);
        var c = MakeWeighable(out var sink, out var mixes, d1, d2);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitPlay(BestPlay()); // in review, one scored answer

        var outcome = await c.StartAsync(new FilterConfig(), NeverSeenMix(), PlayRanking.Equity);

        // The refused weighted start leaves the running quiz exactly as it was…
        Assert.Equal(QuizStartOutcome.MixRequiresStats, outcome);
        Assert.Same(d1, c.Current);
        Assert.NotNull(c.Review);
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.False(c.IsFinished);

        // …and never committed the refused config: Restart re-runs the stored
        // blank mix (factory invoked twice, blank both times) instead of the
        // weighted one that was refused.
        var restart = await c.RestartAsync(PlayRanking.Equity);
        Assert.Equal(QuizStartOutcome.Started, restart);
        Assert.Equal(2, mixes.Count);
        Assert.All(mixes, m => Assert.Same(QuizMix.Empty, m));
    }

    [Fact]
    public async Task StartAsync_MixWithDocument_ComposesFromLifetimeStats()
    {
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"), away: 1);
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"), away: 2);
        var c = MakeWeighable(out var sink, out var mixes, d1, d2);
        sink.CanWeightMix = true;
        sink.CurrentDocument = DocWithSeen(d1); // d1 seen before; d2 never seen

        var mix = NeverSeenMix();
        var outcome = await c.StartAsync(new FilterConfig(), mix, PlayRanking.Equity);

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
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"), away: 1);
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"), away: 2);
        var c = MakeWeighable(out var sink, out var mixes, d1, d2);
        sink.CanWeightMix = false; // stats unavailable — the refusal scenario

        var outcome = await c.StartAsync(new FilterConfig(), NeverSeenMix(), PlayRanking.Equity, ignoreMix: true);

        // The per-run override runs this quiz as passthrough…
        Assert.Equal(QuizStartOutcome.Started, outcome);
        Assert.Same(d1, c.Current);
        Assert.Null(c.LastComposition);
        Assert.Same(QuizMix.Empty, Assert.Single(mixes));

        // …while the weighted mix stayed stored: a plain Restart re-attempts
        // it and is refused again while stats remain unavailable.
        var restart = await c.RestartAsync(PlayRanking.Equity);
        Assert.Equal(QuizStartOutcome.MixRequiresStats, restart);
        Assert.Same(d1, c.Current); // refused restart also left the quiz alone
    }

    [Fact]
    public async Task RestartAsync_ReattemptsStoredMix_RecomposingAgainstCurrentDocument()
    {
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"), away: 1);
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"), away: 2);
        var c = MakeWeighable(out var sink, out _, d1, d2);
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty; // nothing seen yet

        await c.StartAsync(new FilterConfig(), NeverSeenMix(), PlayRanking.Equity);
        Assert.Same(d1, c.Current);
        Assert.Equal(2, c.LastComposition!.DrawnCount); // both never seen

        // The lifetime record advances (as folds would advance it mid-quiz);
        // Restart resolves the provider fresh and composes against the record
        // as it stands now — the deliberate Restart-recomposes semantics.
        sink.CurrentDocument = DocWithSeen(d1);
        var outcome = await c.RestartAsync(PlayRanking.Equity);

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
        sink.CurrentDocument = ProblemStatsDocument.Empty;

        await c.StartAsync(new FilterConfig(), NeverSeenMix(), PlayRanking.Equity);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();
        Assert.True(c.IsFinished);

        // Stats fall away between quizzes (e.g. the folder pick was cleared);
        // the Done page's Restart is refused and its summary must survive.
        sink.CanWeightMix = false;
        var outcome = await c.RestartAsync(PlayRanking.Equity);

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
        // The number's settled convention: N is the consumed stream slot,
        // commensurable with ProblemCount (which also counts slots). The pass
        // position in slot 2 is consumed-but-never-presented, so the second
        // presented problem reads slot 3 — and N lands exactly on M on the
        // stream's last slot rather than the quiz finishing below its stated
        // total.
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"), away: 1);
        var pass = TestFixtures.PassDecision();
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"), away: 2);
        var c = Make(d1, pass, d2);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        Assert.Same(d1, c.Current);
        Assert.Equal(1, c.ProblemNumber);
        Assert.Equal(3, c.ProblemCount);

        await c.SkipCurrentAsync();
        Assert.Same(d2, c.Current);
        Assert.Equal(3, c.ProblemNumber); // slot 2 consumed silently
        Assert.Equal(c.ProblemCount, c.ProblemNumber); // N == M on the stream's last slot

        // N is the number of the problem on screen (SPEC-quiz-history.md §5),
        // and a finished run has none: the total stands, the number goes.
        await c.SkipCurrentAsync();
        Assert.True(c.IsFinished);
        Assert.Equal(0, c.ProblemNumber);
        Assert.Equal(3, c.ProblemCount);
    }

    [Fact]
    public async Task ProblemNumber_RedoLeavesItUntouched_RestartResets()
    {
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"), away: 1);
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"), away: 2);
        var c = Make(d1, d2);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();
        Assert.Equal(2, c.ProblemNumber);

        c.SubmitPlay(BestPlay());
        await c.RedoAsync();
        Assert.Equal(2, c.ProblemNumber); // Redo re-answers the same slot

        await c.RestartAsync(PlayRanking.Equity);
        Assert.Same(d1, c.Current);
        Assert.Equal(1, c.ProblemNumber);
    }

    [Fact]
    public async Task ProblemCount_PassthroughStreamingSource_IsNull()
    {
        var fake = new FakeProblemSetSource(
            [TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay())], countKnown: false);
        var c = new QuizController((_, _, _) => TestFixtures.Composed(fake), new FakeProblemStatsSink(), TimeProvider.System);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        Assert.Null(c.ProblemCount);      // no fabricated total…
        Assert.Equal(1, c.ProblemNumber); // …but the position still tracks
    }

    [Fact]
    public async Task ProblemCount_WeightedQuiz_IsCompositionDrawnCount()
    {
        // Weighted, the total is the composition's drawn count — not the
        // inner source's Count (2 here), which the mix composed down to 1.
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"), away: 1);
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"), away: 2);
        var c = MakeWeighable(out var sink, out _, d1, d2);
        sink.CanWeightMix = true;
        sink.CurrentDocument = DocWithSeen(d1);

        await c.StartAsync(new FilterConfig(), NeverSeenMix(), PlayRanking.Equity);

        Assert.Equal(1, c.ProblemCount);
        Assert.Equal(1, c.ProblemNumber);
    }

    // -----------------------------------------------------------------------
    //  LastComposition.HasRequestedLength — the length-bound fact the Quiz
    //  page frames by, recorded by the producer (halheinrich/backgammon#12)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LastComposition_HasRequestedLength_TracksTheEffectiveMix()
    {
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = MakeWeighable(out var sink, out _, d);
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty;

        // Passthrough wires no composition layer at all, so the framing fact
        // has no composition to live on — and the page's notice block, gated
        // on the composition's existence, renders nothing to frame.
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        Assert.Null(c.LastComposition);

        await c.StartAsync(new FilterConfig(), NeverSeenMix(), PlayRanking.Equity);
        var capless = Assert.IsType<MixComposition>(c.LastComposition);
        Assert.False(capless.HasRequestedLength);           // capless — no length to bind to

        await c.StartAsync(new FilterConfig(), NeverSeenMix(quizLength: 1), PlayRanking.Equity);
        var capped = Assert.IsType<MixComposition>(c.LastComposition);
        Assert.True(capped.HasRequestedLength);             // length-bound

        await c.StartAsync(new FilterConfig(), NeverSeenMix(quizLength: 1), PlayRanking.Equity, ignoreMix: true);
        Assert.Null(c.LastComposition);                     // override runs passthrough
    }

    [Fact]
    public async Task LastComposition_RefusedStart_LeavesPriorValue()
    {
        // Refusals touch no active-run state — the composition included: a
        // running length-bound quiz keeps its telemetry, and with it the
        // notice framing, behind a refused start.
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = MakeWeighable(out var sink, out _, d);
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty;
        await c.StartAsync(new FilterConfig(), NeverSeenMix(quizLength: 1), PlayRanking.Equity);
        var running = Assert.IsType<MixComposition>(c.LastComposition);
        Assert.True(running.HasRequestedLength);

        sink.CanWeightMix = false;
        var outcome = await c.StartAsync(new FilterConfig(), NeverSeenMix(), PlayRanking.Equity);

        Assert.Equal(QuizStartOutcome.MixRequiresStats, outcome);
        Assert.Same(running, c.LastComposition);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        // The pass was skipped silently and the shown problem is the second one…
        Assert.Equal(2, c.ProblemNumber);
        var shown = c.RandomHomeBoardOnRight;

        // …and nothing but an advance can move the roll it took.
        c.SubmitPlay(BestPlay());
        Assert.Equal(shown, c.RandomHomeBoardOnRight);
    }
}
