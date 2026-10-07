using BgDataTypes_Lib;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using XgFilter_Lib;
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
    //  SubmitPlay — scoring (enters review; ▶ advances)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitPlay_BestPlay_Scores_IsCorrect()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitPlayAsync(BestPlay());

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

        await c.SubmitPlayAsync(AltPlay());

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

        await c.SubmitPlayAsync(AltPlay());

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

        await c.SubmitPlayAsync(UnknownPlay());

        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Equal(1, c.SkippedCount);
        var review = OffListReview(c.Review);
        Assert.True(UnknownPlay().IsSameEncoding(review.UserPlay));
    }

    [Fact]
    public async Task Next_FromAReview_AdvancesAndClearsTheReview()
    {
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(d1, d2);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitPlayAsync(BestPlay());
        Assert.NotNull(c.Review);
        Assert.Same(d1, c.Current);

        await c.NextAsync();

        Assert.Null(c.Review);
        Assert.Same(d2, c.Current);
        Assert.False(c.IsFinished);
    }

    [Fact]
    public async Task SubmitPlay_WhileReviewSet_NoOp()
    {
        // Once in review, a second Submit must be ignored — the review is left
        // by moving. Guards against a double-click double-scoring the problem.
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitPlayAsync(BestPlay());
        var reviewBefore = c.Review;

        await c.SubmitPlayAsync(AltPlay());

        Assert.Same(reviewBefore, c.Review); // unchanged
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(1, c.Score.Total.Correct); // and still the first answer's
    }

    [Fact]
    public async Task SubmitThenContinue_LastProblem_FlipsIsFinished()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitPlayAsync(BestPlay());
        Assert.False(c.IsFinished); // review first — not yet advanced
        Assert.NotNull(c.Current);

        await c.NextAsync();

        Assert.True(c.IsFinished);
        Assert.Null(c.Current);
        Assert.Null(c.Review);
    }

    [Fact]
    public async Task SubmitPlay_BeforeStart_NoOp()
    {
        var c = Make();

        await c.SubmitPlayAsync(BestPlay());

        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Equal(0, c.SkippedCount);
        Assert.Null(c.Review);
    }

    [Fact]
    public async Task SubmitPlay_AfterFinish_NoOp()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitPlayAsync(BestPlay());
        await c.NextAsync(); // exhausts → IsFinished
        Assert.True(c.IsFinished);

        var scoreBefore = c.Score;
        await c.SubmitPlayAsync(BestPlay());

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

        await c.SubmitPlayAsync(AltPlay());
        await c.NextAsync();
        await c.SubmitPlayAsync(AltPlay());
        await c.NextAsync();

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

        await c.SubmitPlayAsync(BestPlay());

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

        await c.SubmitPlayAsync(Play.Create(new(13, 10), new(10, 9)));        // 13/10, 10/9

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

        await c.SubmitPlayAsync(Play.Create(new(13, -10), new(10, 8)));        // matches the hitting one only

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

        await c.SubmitPlayAsync(Play.Create(new(8, -3), new(7, 3)));           // 8/3* 7/3, as entered

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

        await c.SubmitPlayAsync(TestFixtures.OpeningAlternative());

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

        await c.SubmitPlayAsync(TestFixtures.DepthSplitThirdPlay());

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

        await c.SubmitPlayAsync(TestFixtures.OpeningBest());

        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Equal(1, c.SkippedCount);
        var review = Assert.IsType<ProblemReview.Play>(c.Review);
        Assert.Equal(PlaySubmissionKind.NotScored, review.Submission.Kind);
        Assert.Equal(1, review.CandidateIndex);

        await c.NextAsync();

        Assert.Equal(0, sink.TotalFolds);
    }

    [Fact]
    public async Task SubmitPlay_APlayTheRankingDoesNotScore_StaysASkipThroughAReturn()
    {
        // A skip is of record (§2): a return after the not-scored play, then a
        // scored practice answer, leaves the skip standing and scores nothing.
        var c = Make(TestFixtures.DepthSplitDecision(), TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.DepthFirst);

        await c.SubmitPlayAsync(TestFixtures.OpeningBest());          // of record: not scored
        await ComeBackAsync(c);
        await c.SubmitPlayAsync(TestFixtures.OpeningAlternative());   // practice: the best, discarded

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
        await c.SubmitPlayAsync(TestFixtures.OpeningAlternative());

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

        await Assert.ThrowsAsync<InvalidOperationException>(() => c.SubmitPlayAsync(BestPlay()));

        Assert.Null(c.Review);
        Assert.Equal(0, c.SkippedCount);
        Assert.False(c.IsBusy); // the gate is released behind the throw
    }

    [Fact]
    public async Task SubmitCubeAnswer_OnACheckerPlay_Throws_AndScoresNothing()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await Assert.ThrowsAsync<InvalidOperationException>(() => c.SubmitCubeAnswerAsync(CubeAnswer.DoubleTake));

        Assert.Null(c.Review);
        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.False(c.IsBusy);
    }

    // -----------------------------------------------------------------------
    //  SubmitCubeAnswer — scoring (enters review; ▶ advances)
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

        await c.SubmitCubeAnswerAsync(CubeAnswer.DoubleTake);

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

        await c.SubmitCubeAnswerAsync(CubeAnswer.NoDouble);

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

        await c.SubmitCubeAnswerAsync(CubeAnswer.NoDoublePass);

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

        await c.SubmitCubeAnswerAsync(CubeAnswer.NoDoublePass);

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

        await c.SubmitCubeAnswerAsync(CubeAnswer.DoubleTake);

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

        await c.SubmitCubeAnswerAsync(CubeAnswer.DoublePass);

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

        await c.SubmitCubeAnswerAsync(CubeAnswer.NoDoublePass);

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

        await c.SubmitCubeAnswerAsync(CubeAnswer.DoublePass);

        var expected = SubmittedCubeAnswer.Score(CubeAnswer.DoublePass, problem);
        var actual = CubeReview(c.Review);
        Assert.Equal(expected, actual);
        Assert.Equal(expected.Cost, actual.Cost);
        Assert.Equal(expected.BestAnswer, actual.BestAnswer);
        Assert.Equal(expected.IsCorrect, actual.IsCorrect);
        Assert.Equal(0.4, actual.Cost.Total, 6);
    }

    [Fact]
    public async Task Next_AfterACubeSubmit_Advances()
    {
        var d1 = TestFixtures.CubeDecision();
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(d1, d2);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitCubeAnswerAsync(CubeAnswer.DoubleTake);
        Assert.Same(d1, c.Current);

        await c.NextAsync();

        Assert.Null(c.Review);
        Assert.Same(d2, c.Current);
        Assert.False(c.IsFinished);
    }

    [Fact]
    public async Task SubmitCubeAnswer_WhileReviewSet_NoOp()
    {
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitCubeAnswerAsync(CubeAnswer.DoubleTake);
        var reviewBefore = c.Review;

        await c.SubmitCubeAnswerAsync(CubeAnswer.NoDoublePass);

        Assert.Same(reviewBefore, c.Review);
        Assert.Equal(1, c.Score.DoubleDecisions.Submitted);
        Assert.Equal(1, c.Score.Total.Correct); // and still the first answer's
    }

    [Fact]
    public async Task SubmitCubeThenContinue_LastProblem_FlipsIsFinished()
    {
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitCubeAnswerAsync(CubeAnswer.DoubleTake);
        Assert.False(c.IsFinished); // review first

        await c.NextAsync();

        Assert.True(c.IsFinished);
        Assert.Null(c.Current);
    }

    [Fact]
    public async Task SubmitCubeAnswer_BeforeStart_NoOp()
    {
        var c = Make();

        await c.SubmitCubeAnswerAsync(CubeAnswer.DoubleTake);

        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Null(c.Review);
    }

    [Fact]
    public async Task SubmitCubeAnswer_AfterFinish_NoOp()
    {
        var c = Make(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitCubeAnswerAsync(CubeAnswer.DoubleTake);
        await c.NextAsync(); // exhausts
        Assert.True(c.IsFinished);

        var scoreBefore = c.Score;
        await c.SubmitCubeAnswerAsync(CubeAnswer.DoubleTake);

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
        await c.SubmitCubeAnswerAsync(CubeAnswer.DoubleTake);
        Assert.Equal(1, c.Score.DoubleDecisions.Submitted);
        Assert.NotNull(c.Review);

        await c.RestartAsync(PlayRanking.Equity);

        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Null(c.Review);
    }

    // -----------------------------------------------------------------------
    //  Practice — a completed problem returned to (SPEC-quiz-history.md §3)
    //
    //  Returning to a problem is how it is practised; the Redo button that
    //  used to re-open one is retired (§2). ComeBackAsync is the return: ▶ to
    //  the next problem and ◀ back, so each source here holds one problem more
    //  than the scenario answers.
    // -----------------------------------------------------------------------

    /// <summary>
    /// The problem on screen, after a submission there, again: ▶ away and ◀
    /// back — the way a completed problem is returned to (SPEC-quiz-history.md
    /// §3). Behind the frontier ▶ only moves the cursor; on it, ▶ draws the
    /// next problem, which the source must hold and which is left unresolved
    /// on the frontier, so the totals are as they were.
    /// </summary>
    private static async Task ComeBackAsync(QuizController c)
    {
        var problem = c.Current;
        var number = c.ProblemNumber;
        var (score, skipped) = (c.Score, c.SkippedCount);

        await c.NextAsync();
        Assert.False(c.IsFinished, "The source held no further problem to move on to.");
        c.GoBack();

        Assert.Same(problem, c.Current);
        Assert.Equal(number, c.ProblemNumber);
        Assert.Null(c.Review);
        Assert.Equal(score, c.Score);
        Assert.Equal(skipped, c.SkippedCount);
    }

    [Fact]
    public async Task ReturningToAnAnsweredProblem_LandsOnItsDecision_AndTheRecordStands()
    {
        // SPEC-scoring.md §2: returning re-opens the problem, never the record.
        // Only the review goes — Score and SkippedCount are exactly as the first
        // submission left them, and it is that submission which folded.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 1));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        var current = c.Current;

        await c.SubmitPlayAsync(BestPlay());
        var recorded = ScoredReview(c.Review);
        var scored = c.Score;

        await ComeBackAsync(c);

        Assert.Equal(scored, c.Score);
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(1, c.Score.Total.Correct);
        Assert.Equal(0, c.SkippedCount);
        Assert.Same(current, c.Current); // the same problem, its decision
        Assert.False(c.IsFinished);
        Assert.Same(recorded, Assert.Single(sink.Plays));
    }

    [Fact]
    public async Task ReturningAfterAnIncorrectPlay_LeavesTheEquityLossStanding()
    {
        // The equity a wrong first answer lost is not refundable by returning.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 1));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitPlayAsync(AltPlay());
        await ComeBackAsync(c);
        await c.SubmitPlayAsync(BestPlay()); // practice: correct, and discarded

        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(0, c.Score.Total.Correct);
        Assert.Equal(0.05, c.Score.Total.TotalEquityLoss, 6);
    }

    [Fact]
    public async Task ReturningAfterAnOffListPlay_LeavesTheSkipStanding_AndAnOnListPracticeScoresNothing()
    {
        // §2: a skip is of record too — an off-list submission counts as one and
        // nothing un-counts it. The on-list practice answer after it is the
        // strongest form of the rule: under the prior model it replaced the skip
        // with a scored answer.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 1));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitPlayAsync(UnknownPlay());
        Assert.Equal(1, c.SkippedCount);
        await ComeBackAsync(c);
        Assert.Equal(1, c.SkippedCount);

        await c.SubmitPlayAsync(BestPlay());

        Assert.Equal(1, c.SkippedCount);
        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.True(ScoredReview(c.Review).IsCorrect); // still reviewed
        Assert.True(c.Review!.IsPractice);
    }

    [Fact]
    public async Task ReturningToACubeAnswer_LeavesTheAnswerOfRecordStanding_AndThePlaySegmentBeforeIt()
    {
        // Interleaved answers and a return: neither segment moves. The play
        // answered first stays in PlayDecisions, and the cube problem's own
        // answer of record stays in DoubleDecisions / TakeDecisions while a
        // correct practice answer is given on it.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05),
            TestFixtures.CubeDecision(),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 1));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitPlayAsync(AltPlay()); // incorrect, 0.05 lost
        await c.NextAsync();

        await c.SubmitCubeAnswerAsync(CubeAnswer.NoDoublePass); // of record: wrong
        var recorded = CubeReview(c.Review);
        var scored = c.Score;
        await ComeBackAsync(c);
        await c.SubmitCubeAnswerAsync(CubeAnswer.DoubleTake); // practice: correct

        Assert.Equal(scored, c.Score);
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
        Assert.Equal(0.05, c.Score.PlayDecisions.TotalEquityLoss, 6);
        Assert.Equal(1, c.Score.DoubleDecisions.Submitted);
        Assert.Equal(1, c.Score.TakeDecisions.Submitted);
        Assert.Equal(0, c.Score.Total.Correct);
        Assert.Same(recorded, Assert.Single(sink.Cubes));
    }

    [Fact]
    public async Task PracticeCycles_AreUnbounded_AndEquallyRecordless()
    {
        // "Practice cycles are unbounded; each is equally recordless" (§2) — the
        // record is written once and never again, however many times the user
        // comes back.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 1));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitPlayAsync(AltPlay()); // of record
        var scored = c.Score;

        for (var cycle = 0; cycle < 5; cycle++)
        {
            await ComeBackAsync(c);
            await c.SubmitPlayAsync(cycle % 2 == 0 ? BestPlay() : UnknownPlay());
            Assert.True(c.Review!.IsPractice);
            Assert.Equal(scored, c.Score);
            Assert.Equal(0, c.SkippedCount); // an off-list practice adds no skip
            Assert.Single(sink.Plays);
        }
    }

    [Fact]
    public async Task APracticeSubmission_IsReviewedAndMarkedPractice_OnItsOwnMerits()
    {
        // §2's "practice still reviews": the practice submission gets the normal
        // scored review, flagged IsPractice; the answer of record's review is
        // not flagged.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 1));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitPlayAsync(AltPlay());
        var ofRecord = Assert.IsType<ProblemReview.Play>(c.Review);
        Assert.False(ofRecord.IsPractice);
        Assert.False(ScoredReview(ofRecord).IsCorrect);

        await ComeBackAsync(c);
        await c.SubmitPlayAsync(BestPlay());

        var practice = Assert.IsType<ProblemReview.Play>(c.Review);
        Assert.True(practice.IsPractice);
        Assert.True(ScoredReview(practice).IsCorrect); // scored on its own merits, not the record's
        Assert.Equal(0.0, ScoredReview(practice).EquityLoss, 6);
    }

    [Fact]
    public async Task APracticeCubeSubmission_IsReviewedAndMarkedPractice()
    {
        var c = Make(TestFixtures.CubeDecision(), TestFixtures.CubeDecision(away: 1));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitCubeAnswerAsync(CubeAnswer.NoDoublePass);
        Assert.False(Assert.IsType<ProblemReview.Cube>(c.Review).IsPractice);

        await ComeBackAsync(c);
        await c.SubmitCubeAnswerAsync(CubeAnswer.DoubleTake);

        var practice = Assert.IsType<ProblemReview.Cube>(c.Review);
        Assert.True(practice.IsPractice);
        Assert.Equal(CubeAnswer.DoubleTake, practice.Submission.Answer); // the practice answer, not the record's
    }

    [Fact]
    public async Task ACompletedProblem_ReturnedTo_GivesAPracticeReview_WithTheTotalsUnchanged_AndNoFold()
    {
        // The brief's controller pin for a return to a completed problem, both
        // ways it is completed: answered, and skipped of record. Its review is
        // marked practice, the score and the skip count do not move, and the
        // lifetime record gains nothing.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 1),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 2));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitPlayAsync(AltPlay());     // answered
        await c.NextAsync();
        await c.SubmitPlayAsync(UnknownPlay()); // skipped of record
        await c.NextAsync();                    // the third, unresolved on the frontier
        var (score, skipped, folds) = (c.Score, c.SkippedCount, sink.TotalFolds);

        c.GoBack();
        await c.SubmitPlayAsync(BestPlay());
        Assert.True(c.Review!.IsPractice);
        Assert.Equal((score, skipped, folds), (c.Score, c.SkippedCount, sink.TotalFolds));

        c.GoToFirst();
        await c.SubmitPlayAsync(BestPlay());
        Assert.True(c.Review!.IsPractice);
        Assert.Equal((score, skipped, folds), (c.Score, c.SkippedCount, sink.TotalFolds));
    }

    [Fact]
    public async Task ADeferredProblem_AnsweredLiveOnReturn_FoldsOnceAtSubmit_TheScoreGainsIt_AndTheSkipCountDrops()
    {
        // SPEC-quiz-history.md §3: "A deferred problem answered live becomes
        // answered. The provisional skip does not stand: the problem is counted
        // once, its answer goes to the score and the lifetime record, and the
        // session skip count drops." The answer is behind the frontier, where a
        // fold that read the frontier could never reach it.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 1));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.NextAsync();                     // deferred
        Assert.Equal(1, c.SkippedCount);
        Assert.Equal(QuizScore.Empty, c.Score);

        c.GoBack();
        await c.SubmitPlayAsync(AltPlay());

        var review = Assert.IsType<ProblemReview.Play>(c.Review);
        Assert.False(review.IsPractice);
        Assert.Same(ScoredReview(review), Assert.Single(sink.Plays));
        Assert.Equal(0, c.SkippedCount);
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
        Assert.Equal(0.05, c.Score.PlayDecisions.TotalEquityLoss, 6);

        // Once, whatever follows.
        await c.NextAsync();
        await c.EndQuizAsync();
        Assert.Single(sink.Plays);
        Assert.Equal(1, c.SkippedCount); // the second, unresolved when the run finished
    }

    [Fact]
    public async Task ADeferredCubeProblem_AnsweredLiveOnReturn_FoldsOnceAtSubmit()
    {
        var c = MakeWithSink(out var sink,
            TestFixtures.CubeDecision(),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.NextAsync();
        c.GoBack();

        await c.SubmitCubeAnswerAsync(CubeAnswer.DoubleTake);

        Assert.Same(CubeReview(c.Review), Assert.Single(sink.Cubes));
        Assert.Equal(0, c.SkippedCount);
        Assert.Equal(1, c.Score.Total.Submitted);
    }

    // -----------------------------------------------------------------------
    //  ▶ — "the next problem" (SPEC-quiz-history.md §2, §4)
    //
    //  One method, NextAsync, for ▶ in both view states: Continue from a
    //  review, Skip or Next while answering.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Next_OnTheUnresolvedFrontier_DefersIt_CountsItOnceTheNextLands_AndBringsTheNext()
    {
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(d1, d2);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        Assert.True(c.NextAddsToSkipCount);

        await c.NextAsync();

        Assert.Equal(1, c.SkippedCount);
        Assert.Same(d2, c.Current);
        Assert.Equal(QuizScore.Empty, c.Score);
    }

    [Fact]
    public async Task Next_FromALiveReview_IsContinue_ItAdvancesAndCountsNoSkip()
    {
        // §2: "On the solution view it is Continue." The problem was completed
        // by its submission, so moving on adds nothing to the record.
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = Make(d1, d2);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitPlayAsync(BestPlay());
        Assert.False(c.NextAddsToSkipCount);

        await c.NextAsync();

        Assert.Null(c.Review);
        Assert.Same(d2, c.Current);
        Assert.Equal(0, c.SkippedCount);
        Assert.Equal(1, c.Score.Total.Submitted);
    }

    [Fact]
    public async Task Next_BeforeStart_NoOp()
    {
        var c = Make();
        await c.NextAsync();
        Assert.False(c.HasStarted);
        Assert.Equal(0, c.SkippedCount);
    }

    [Fact]
    public async Task Next_AfterFinish_NoOp()
    {
        var c = Make(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitPlayAsync(BestPlay());
        await c.NextAsync(); // exhaust
        Assert.True(c.IsFinished);

        await c.NextAsync();

        Assert.True(c.IsFinished);
        Assert.Equal(0, c.SkippedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Next_OnTheLastAvailableProblem_FinishesTheRunWithItCountedAsSkipped(bool aPassFollows)
    {
        // Natural exhaustion with a deferred final problem (SPEC-quiz-history.md
        // §4 and §5, amended 2026-09-30). ▶ completes nothing, and the source
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
        await c.SubmitPlayAsync(BestPlay());
        await c.NextAsync();
        var folded = Assert.Single(sink.Plays);

        await c.NextAsync();

        Assert.True(c.IsFinished);
        Assert.Null(c.Current);
        Assert.Equal(1, c.SkippedCount);
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
        Assert.Same(folded, Assert.Single(sink.Plays));
        Assert.Empty(sink.Cubes);
    }

    [Fact]
    public async Task Next_BehindTheFrontier_MovesTheCursorAtOnce_DrawingRecordingAndFoldingNothing()
    {
        // §4: behind the frontier ▶ "goes to the next problem" — the one already
        // presented after the cursor. No source work: with the source's next
        // item held at its gate, the move still completes at once, fires
        // StateChanged once and never shows busy; no problem is drawn, and
        // nothing is recorded or folded.
        var gated = new GatedProblemSetSource(
        [
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 1),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 2),
        ]);
        var sink = new FakeProblemStatsSink();
        var c = new QuizController((_, _, _) => TestFixtures.Composed(gated), sink, TimeProvider.System);
        gated.ReleaseNext(2);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitPlayAsync(BestPlay());
        await c.NextAsync();          // the second, on the frontier
        c.GoBack();                   // back on the first, answered
        var drawn = gated.DrawsRequested;
        var (score, skipped, folds) = (c.Score, c.SkippedCount, sink.TotalFolds);
        var snapshots = new List<bool>();
        c.StateChanged += () => snapshots.Add(c.IsBusy);

        var next = c.NextAsync();

        Assert.True(next.IsCompletedSuccessfully);
        Assert.Equal([false], snapshots);
        Assert.Equal(2, c.ProblemNumber);
        Assert.Equal(drawn, gated.DrawsRequested);
        Assert.Equal((score, skipped, folds), (c.Score, c.SkippedCount, sink.TotalFolds));
    }

    // -----------------------------------------------------------------------
    //  §4 · The transition table — ▶ ⏮ ◀ ⏭ from each state on screen
    //
    //  The staged run: the first problem answered, the second deferred, the
    //  third the unresolved frontier, and two more waiting in the source.
    //  Each state of §4's table is reached from it, each move taken, and
    //  where the cursor lands, what shows there and what the totals do are
    //  read back. Submit, the table's first column, is the practice and
    //  deferred pins above.
    // -----------------------------------------------------------------------

    public enum OnScreen
    {
        LiveAnsweringOnTheFrontier,
        LiveAnsweringBehindTheFrontier,
        LiveReviewOnTheFrontier,
        LiveReviewBehindTheFrontier,
        PracticeAnswering,
        PracticeReview,
    }

    public enum Move { Next, GoToFirst, GoBack, GoToLast }

    [Theory]
    // Live answering — "Goes to the next problem; this one stays unresolved
    // (on the frontier, that defers it)"; ⏮ ◀ ⏭ leave it unresolved.
    [InlineData(OnScreen.LiveAnsweringOnTheFrontier, Move.Next, 4, 1)]
    [InlineData(OnScreen.LiveAnsweringOnTheFrontier, Move.GoToFirst, 1, 0)]
    [InlineData(OnScreen.LiveAnsweringOnTheFrontier, Move.GoBack, 2, 0)]
    [InlineData(OnScreen.LiveAnsweringOnTheFrontier, Move.GoToLast, 0, 0)]
    [InlineData(OnScreen.LiveAnsweringBehindTheFrontier, Move.Next, 3, 0)]
    [InlineData(OnScreen.LiveAnsweringBehindTheFrontier, Move.GoToFirst, 1, 0)]
    [InlineData(OnScreen.LiveAnsweringBehindTheFrontier, Move.GoBack, 1, 0)]
    [InlineData(OnScreen.LiveAnsweringBehindTheFrontier, Move.GoToLast, 3, 0)]
    // Live review — ▶ "Same as Continue"; ⏮ ◀ ⏭ "Navigates; the record stands".
    [InlineData(OnScreen.LiveReviewOnTheFrontier, Move.Next, 4, 0)]
    [InlineData(OnScreen.LiveReviewOnTheFrontier, Move.GoToFirst, 1, 0)]
    [InlineData(OnScreen.LiveReviewOnTheFrontier, Move.GoBack, 2, 0)]
    [InlineData(OnScreen.LiveReviewOnTheFrontier, Move.GoToLast, 0, 0)]
    [InlineData(OnScreen.LiveReviewBehindTheFrontier, Move.Next, 3, 0)]
    [InlineData(OnScreen.LiveReviewBehindTheFrontier, Move.GoToFirst, 1, 0)]
    [InlineData(OnScreen.LiveReviewBehindTheFrontier, Move.GoBack, 1, 0)]
    [InlineData(OnScreen.LiveReviewBehindTheFrontier, Move.GoToLast, 3, 0)]
    // Practice answering and practice review — "Goes to the next problem";
    // ⏮ ◀ ⏭ "Navigates".
    [InlineData(OnScreen.PracticeAnswering, Move.Next, 2, 0)]
    [InlineData(OnScreen.PracticeAnswering, Move.GoToFirst, 0, 0)]
    [InlineData(OnScreen.PracticeAnswering, Move.GoBack, 0, 0)]
    [InlineData(OnScreen.PracticeAnswering, Move.GoToLast, 3, 0)]
    [InlineData(OnScreen.PracticeReview, Move.Next, 2, 0)]
    [InlineData(OnScreen.PracticeReview, Move.GoToFirst, 0, 0)]
    [InlineData(OnScreen.PracticeReview, Move.GoBack, 0, 0)]
    [InlineData(OnScreen.PracticeReview, Move.GoToLast, 3, 0)]
    public async Task TheTransitionTable(OnScreen from, Move move, int landsOn, int skipsAdded)
    {
        // landsOn: the problem number the move lands on, or 0 where the move is
        // unavailable there (⏮ ◀ on the first problem, ⏭ on the frontier) and
        // changes nothing at all.
        var c = MakeWithSink(out var sink, Enumerable.Range(1, 5)
            .Select(n => (BgDecisionData)TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: n))
            .ToArray());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitPlayAsync(AltPlay());  // 1: answered
        await c.NextAsync();
        await c.NextAsync();                 // 2: deferred; 3: the frontier
        switch (from)
        {
            case OnScreen.LiveAnsweringOnTheFrontier: break;
            case OnScreen.LiveAnsweringBehindTheFrontier: c.GoBack(); break;
            case OnScreen.LiveReviewOnTheFrontier: await c.SubmitPlayAsync(BestPlay()); break;
            case OnScreen.LiveReviewBehindTheFrontier: c.GoBack(); await c.SubmitPlayAsync(BestPlay()); break;
            case OnScreen.PracticeAnswering: c.GoToFirst(); break;
            case OnScreen.PracticeReview: c.GoToFirst(); await c.SubmitPlayAsync(BestPlay()); break;
        }
        var before = (Number: c.ProblemNumber, c.Review, c.Score, c.SkippedCount, Folds: sink.TotalFolds);
        Assert.Equal(from.ToString().Contains("Review"), before.Review is not null);

        switch (move)
        {
            case Move.Next: await c.NextAsync(); break;
            case Move.GoToFirst: c.GoToFirst(); break;
            case Move.GoBack: c.GoBack(); break;
            case Move.GoToLast: c.GoToLast(); break;
        }

        if (landsOn == 0)
        {
            Assert.Equal(before, (c.ProblemNumber, c.Review, c.Score, c.SkippedCount, sink.TotalFolds));
            return;
        }

        // Every landing shows the decision (§3), and a move records and folds
        // nothing: only a deferral adds a skip, once the next problem lands.
        Assert.Equal(landsOn, c.ProblemNumber);
        Assert.Null(c.Review);
        Assert.NotNull(c.Current);
        Assert.Equal(before.Score, c.Score);
        Assert.Equal(before.SkippedCount + skipsAdded, c.SkippedCount);
        Assert.Equal(before.Folds, sink.TotalFolds);
        Assert.False(c.IsFinished);
    }

    // -----------------------------------------------------------------------
    //  ⏮ ◀ ⏭ and what the controller says of the run
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CanGoBack_AndCanGoToLast_AreTheRuns_FromStartToFinish()
    {
        // §2: ⏮ and ◀ are unavailable at the first problem, ⏭ at the frontier;
        // with nothing on screen, all three.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 1));
        Assert.False(c.CanGoBack);
        Assert.False(c.CanGoToLast);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        Assert.False(c.CanGoBack);    // the first problem
        Assert.False(c.CanGoToLast);  // and the frontier

        await c.NextAsync();
        Assert.True(c.CanGoBack);
        Assert.False(c.CanGoToLast);

        c.GoBack();
        Assert.False(c.CanGoBack);
        Assert.True(c.CanGoToLast);

        await c.EndQuizAsync();
        Assert.False(c.CanGoBack);
        Assert.False(c.CanGoToLast);
    }

    [Fact]
    public async Task NextAddsToSkipCount_IsTheRuns()
    {
        // The controller forwards the run's rule (QuizRunTests pins the rule
        // itself against what a press does): true on the unresolved frontier,
        // false behind it, on a review, and with nothing on screen.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 1));
        Assert.False(c.NextAddsToSkipCount);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        Assert.True(c.NextAddsToSkipCount);

        await c.NextAsync();
        c.GoBack();
        Assert.False(c.NextAddsToSkipCount);   // deferred, behind the frontier

        c.GoToLast();
        await c.SubmitPlayAsync(BestPlay());
        Assert.False(c.NextAddsToSkipCount);   // a review

        await c.EndQuizAsync();
        Assert.False(c.NextAddsToSkipCount);
    }

    [Fact]
    public async Task PresentedCount_IsTheRunsPresentedSequence_ThroughNavigationAndTheFinish()
    {
        // Done's "problems shown" (halheinrich/backgammon#325, item 2): the
        // presented sequence, never the score's submitted count plus the skips.
        // A position passed over silently is not in it; navigation adds nothing
        // to it; a finished run keeps it.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.PassDecision(),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 1),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 2));
        Assert.Equal(0, c.PresentedCount);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        Assert.Equal(1, c.PresentedCount);

        await c.NextAsync();
        Assert.Equal(2, c.PresentedCount);   // the pass between was never shown
        c.GoToFirst();
        await c.NextAsync();
        Assert.Equal(2, c.PresentedCount);   // ▶ behind the frontier presents nothing

        await c.EndQuizAsync();
        Assert.Equal(2, c.PresentedCount);
        Assert.Equal(c.PresentedCount, c.Score.Total.Submitted + c.SkippedCount);
    }

    [Fact]
    public async Task TheMoves_FireStateChangedOnce_AndShowNoBusy()
    {
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 1),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 2));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.NextAsync();
        await c.NextAsync();
        var snapshots = new List<bool>();
        c.StateChanged += () => snapshots.Add(c.IsBusy);

        c.GoBack();
        c.GoToFirst();
        c.GoToLast();

        Assert.Equal([false, false, false], snapshots);
    }

    [Fact]
    public async Task TheMoves_WhereUnavailable_FireNothing()
    {
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 1));
        var fired = 0;
        c.StateChanged += () => fired++;

        c.GoToFirst();
        c.GoBack();
        c.GoToLast();
        Assert.Equal(0, fired);               // nothing on screen

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        fired = 0;
        c.GoToFirst();
        c.GoBack();
        c.GoToLast();
        Assert.Equal(0, fired);               // the first problem is the frontier
        Assert.Equal(1, c.ProblemNumber);
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
        // to a skip of record, counted as every problem ▶ moved on
        // from is counted — so Done's "problems shown" still counts a problem
        // the user actually saw.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitPlayAsync(BestPlay());
        await c.NextAsync();          // problem 1 answered and finalized

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
        await c.SubmitPlayAsync(BestPlay());
        Assert.NotNull(c.Review);

        await c.EndQuizAsync();

        Assert.True(c.IsFinished);
        Assert.Null(c.Review);
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
        Assert.Equal(0, c.SkippedCount);
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
        await c.SubmitPlayAsync(UnknownPlay());
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
        await c.SubmitPlayAsync(BestPlay());
        await c.NextAsync(); // exhaust
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
        // Several problems unresolved at once, as ▶ leaves
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
        await c.NextAsync();
        await c.NextAsync();
        Assert.Equal(2, c.SkippedCount);
        Assert.Equal(3, c.ProblemNumber);

        await c.EndQuizAsync();

        Assert.True(c.IsFinished);
        Assert.Equal(3, c.SkippedCount);
        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Equal(0, sink.TotalFolds);
    }

    [Fact]
    public async Task EndQuiz_FromBehindTheFrontier_ConvertsEveryUnresolvedProblem()
    {
        // §4: End quiz "acts on the run, not the cursor: it applies wherever the
        // user is viewing". Viewing the first problem, answered, with a deferred
        // problem and the unresolved frontier ahead: ending converts both of
        // them to skips of record, leaves the answer standing, and folds nothing.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 1),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 2),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 3));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitPlayAsync(BestPlay());
        await c.NextAsync();
        await c.NextAsync();             // the second deferred, the third the frontier
        c.GoToFirst();
        Assert.Equal(1, c.SkippedCount); // the deferred one, provisionally
        var folds = sink.TotalFolds;

        await c.EndQuizAsync();

        Assert.True(c.IsFinished);
        Assert.Equal(2, c.SkippedCount);
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(3, c.PresentedCount);
        Assert.Equal(folds, sink.TotalFolds);
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
        await c.SubmitPlayAsync(BestPlay());
        await c.NextAsync();
        await c.NextAsync();
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

        var summary = await c.SummarizeMatchesAsync(new FilterConfig(), PlayRanking.Equity);

        Assert.Equal(AnswerTypeDistribution.Empty, summary.AnswerTypes);
        Assert.Equal(0, summary.DuplicatesCollapsed);
        // A substitute stack has no parse layer: a completed walk over no
        // files, which is not an all-rejected one (nothing was attempted).
        Assert.True(summary.Sources.IsComplete);
        Assert.Equal(0, summary.Sources.AttemptedCount);
        Assert.False(summary.Sources.AllRejected);
    }

    [Fact]
    public async Task SummarizeMatchesAsync_CarriesTheStacksSourceReport()
    {
        // halheinrich/backgammon#368. The count's summary says which of the
        // picked files the parse behind it could not read, by carrying the
        // stack's own report — the very object, not a copy — so what the count
        // says and what the parse found cannot come apart. The report is a
        // real one: the real iterator over a stream the producer refuses
        // (TestFixtures.WalkedReport); what is owed here is that the controller
        // hands the stack's report through rather than inventing or dropping it.
        var report = TestFixtures.WalkedReport(
            new XgFileStream("damaged.xg", new MemoryStream([1, 2, 3])));
        var fake = new FakeProblemSetSource([TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay())]);
        var c = new QuizController(
            (_, _, _) => TestFixtures.Composed(fake, sources: report),
            new FakeProblemStatsSink(), TimeProvider.System);

        var summary = await c.SummarizeMatchesAsync(new FilterConfig(), PlayRanking.Equity);

        Assert.Same(report, summary.Sources);
        Assert.Equal("damaged.xg", Assert.Single(summary.Sources.Rejected).SourceName);
        Assert.Equal(1, summary.AnswerTypes.Total); // the count itself is untouched by the report
    }

    [Fact]
    public async Task SummarizeMatchesAsync_StackReportingNoWalkAfterADrain_IsRefused()
    {
        // A fully drained stack that still reports no walk is a composition
        // that lost its reader — a wiring defect, refused loudly rather than
        // summarized as an empty, complete selection.
        var fake = new FakeProblemSetSource([]);
        var c = new QuizController(
            (_, _, _) => new ComposedProblemSource(fake, () => 0, () => null),
            new FakeProblemStatsSink(), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => c.SummarizeMatchesAsync(new FilterConfig(), PlayRanking.Equity));
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
        await c.SubmitPlayAsync(BestPlay());
        await c.NextAsync(); // now on the second problem, one scored
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
    //  Lifetime-stats sink — bind at Start/Restart, fold at Submit
    //
    //  SPEC-scoring.md §2's fold trigger (amended 2026-09-24): "The answer of
    //  record folds into lifetime stats at the first submission itself. A skip
    //  is of record when made and folds nothing. Practice never folds." So the
    //  sink is read straight after each Submit, before any further gesture, and
    //  every pin that says a gesture folds nothing first has a live answer fold
    //  at its own Submit — the positive control that makes "nothing more" mean
    //  something, and the half that fails on a build folding anywhere else.
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
    public async Task Submit_Play_FoldsTheAnswerOfRecordAtSubmit_AndContinueAddsNothing()
    {
        // The fold is the submission's own: the sink holds the very submission
        // the live review shows — the record's — the moment Submit returns,
        // before any further gesture. Moving on writes nothing more.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitPlayAsync(AltPlay());

        var submitted = ScoredReview(c.Review);
        Assert.Same(submitted, Assert.Single(sink.Plays));
        Assert.Empty(sink.Cubes);

        await c.NextAsync();

        Assert.Same(submitted, Assert.Single(sink.Plays));
        Assert.Empty(sink.Cubes);
    }

    [Fact]
    public async Task Submit_Cube_FoldsTheAnswerOfRecordAtSubmit_AndContinueAddsNothing()
    {
        var c = MakeWithSink(out var sink, TestFixtures.CubeDecision(), TestFixtures.CubeDecision(away: 1));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitCubeAnswerAsync(CubeAnswer.DoubleTake);

        var submitted = CubeReview(c.Review);
        Assert.Same(submitted, Assert.Single(sink.Cubes));
        Assert.Empty(sink.Plays);

        await c.NextAsync();

        Assert.Same(submitted, Assert.Single(sink.Cubes));
        Assert.Empty(sink.Plays);
    }

    [Fact]
    public async Task Submit_PutsItsReviewUpBeforeTheWrite_AndHoldsTheGateUntilTheWriteLands()
    {
        // The design call this leg made (constraint 3 of its brief left it
        // open): the review renders at once, and the write is awaited inside
        // the transition gate. Read from inside the write itself, so there is
        // no window to race: the review being written is on screen, and the
        // controller is busy. Once the write lands the gate is released and the
        // review stays.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        ProblemReview? duringWrite = null;
        var busyDuringWrite = false;
        sink.OnRecording = () => (duringWrite, busyDuringWrite) = (c.Review, c.IsBusy);

        await c.SubmitPlayAsync(BestPlay());

        Assert.NotNull(duringWrite);
        Assert.Same(c.Review, duringWrite);
        Assert.Same(ScoredReview(duringWrite), Assert.Single(sink.Plays));
        Assert.True(busyDuringWrite);
        Assert.False(c.IsBusy);
    }

    [Fact]
    public async Task Submit_TheReviewIsOnScreenWhenTheCallReturns_TheTaskCompletesWhenTheWriteLands()
    {
        // The contract the Quiz page reads: the review is up as soon as the call
        // returns its task, and the task — and the gate — complete with the
        // write. A held write keeps both open.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        var write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sink.RecordGate = write.Task;

        var submitting = c.SubmitPlayAsync(BestPlay());

        Assert.NotNull(c.Review);
        Assert.True(c.IsBusy);
        Assert.False(submitting.IsCompleted);

        write.SetResult();
        await submitting;

        Assert.False(c.IsBusy);
        Assert.Single(sink.Plays);
    }

    [Fact]
    public async Task Continue_AfterALiveSubmit_AddsNothingMore_ThroughToTheEndOfTheSource()
    {
        // The last problem's answer folds at its Submit, before the Continue that
        // exhausts the source and finishes the run; finishing writes nothing.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitPlayAsync(BestPlay());
        var submitted = Assert.Single(sink.Plays);

        await c.NextAsync();

        Assert.True(c.IsFinished);
        Assert.Same(submitted, Assert.Single(sink.Plays));
    }

    [Fact]
    public async Task NextFromAPracticeAnswering_AfterALiveSubmit_AddsNothingMore()
    {
        // ▶ is reachable from the practice-answering state a return opens, and
        // it moves on from an answered problem: it folds nothing — the answer
        // reached the record at its Submit — and counts no skip, which would
        // double-count a problem that was answered.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        var first = c.Current;

        await c.SubmitPlayAsync(BestPlay());
        var recorded = ScoredReview(c.Review);
        Assert.Same(recorded, Assert.Single(sink.Plays));
        await ComeBackAsync(c);

        await c.NextAsync();

        Assert.Same(recorded, Assert.Single(sink.Plays));
        Assert.Equal(0, c.SkippedCount);
        Assert.NotSame(first, c.Current); // and the run did move on
    }

    [Fact]
    public async Task EndQuiz_FromTheReview_AfterALiveSubmit_AddsNothingMore()
    {
        // Every answer visible on Done has reached the lifetime record — by
        // construction now: it did at its Submit, so ending writes nothing.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitPlayAsync(AltPlay());
        var submitted = ScoredReview(c.Review);
        Assert.Same(submitted, Assert.Single(sink.Plays));

        await c.EndQuizAsync();

        Assert.True(c.IsFinished);
        Assert.Same(submitted, Assert.Single(sink.Plays));
        Assert.Empty(sink.Cubes);
    }

    [Fact]
    public async Task EndQuiz_MidPracticeCycle_AfterALiveSubmit_AddsNothingMore_AndCountsNoSkip()
    {
        // Ending on a problem returned to, with nothing re-answered: no review
        // is showing, but the problem IS answered. The run's conversion keys on
        // the record, so it is not counted as abandoned — only the problem the
        // return left unresolved on the frontier is — and its answer, folded at
        // Submit, is not folded again.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitPlayAsync(BestPlay());
        var recorded = ScoredReview(c.Review);
        Assert.Same(recorded, Assert.Single(sink.Plays));
        await ComeBackAsync(c);
        Assert.Null(c.Review); // answering again, nothing submitted this cycle

        await c.EndQuizAsync();

        Assert.Same(recorded, Assert.Single(sink.Plays));
        Assert.Equal(1, c.SkippedCount); // the second, never answered — not the first
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
    }

    [Fact]
    public async Task EndQuiz_FromAPracticeReview_AddsNothingMore_TheRecordNotThePractice()
    {
        // The review on screen at End quiz is a correct practice answer; what
        // reached the lifetime record is the incorrect first one, at its Submit,
        // and ending adds neither.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitPlayAsync(AltPlay());  // of record: incorrect
        var recorded = ScoredReview(c.Review);
        Assert.Same(recorded, Assert.Single(sink.Plays));
        await ComeBackAsync(c);
        await c.SubmitPlayAsync(BestPlay()); // practice: correct

        await c.EndQuizAsync();

        Assert.Same(recorded, Assert.Single(sink.Plays));
        Assert.False(sink.Plays[0].IsCorrect);
        Assert.Equal(1, c.SkippedCount); // the second, never answered
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ANewRun_BegunRightAfterALiveSubmit_LeavesTheAnswerInTheRecord(bool byRestart)
    {
        // SPEC-scoring.md §2: "The abandoned-in-review posture is retired: an
        // answer submitted and then left by tab close or Restart now counts."
        // The answer reached the record at its Submit; the new run binds afresh
        // and folds nothing of its own on the way in.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitPlayAsync(BestPlay());
        var submitted = ScoredReview(c.Review);

        if (byRestart)
            await c.RestartAsync(PlayRanking.Equity);
        else
            await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        Assert.Same(submitted, Assert.Single(sink.Plays));
        Assert.Equal(QuizScore.Empty, c.Score); // and the run genuinely began again
        Assert.Equal(2, sink.BeginQuizCallCount);
    }

    [Fact]
    public async Task ANewRun_BegunMidPracticeCycle_LeavesTheAnswerInTheRecord()
    {
        // The same where a return leaves the record standing with no review
        // showing: the answer of record is in the lifetime record, and the
        // practice cycle the Restart cut short adds nothing.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitPlayAsync(BestPlay());
        var recorded = ScoredReview(c.Review);
        await ComeBackAsync(c);

        await c.RestartAsync(PlayRanking.Equity);

        Assert.Same(recorded, Assert.Single(sink.Plays));
        Assert.Equal(QuizScore.Empty, c.Score);
    }

    [Fact]
    public async Task TheSameDecision_AnsweredLiveInTwoRuns_FoldsOnceInEach_AndPracticeInEitherAddsNothing()
    {
        // Practice is per run (SPEC-quiz-history.md §3, Hal, 2026-10-02: "This
        // is on a session basis. I.e. Answering the same quiz decision after a
        // fresh start; that goes into stats."). The one decision is answered
        // live, then practised, in a run, and again in the run a Restart begins:
        // each run's first answer folds, each in its own run, and no practice
        // answer does.
        var problem = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05);
        var c = MakeWithSink(out var sink, problem, TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 1));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitPlayAsync(AltPlay());                 // run 1, live
        var firstRun = ScoredReview(c.Review);
        Assert.Same(firstRun, Assert.Single(sink.Plays));
        await ComeBackAsync(c);
        await c.SubmitPlayAsync(BestPlay());                // run 1, practice
        Assert.Same(firstRun, Assert.Single(sink.Plays));

        await c.RestartAsync(PlayRanking.Equity);
        Assert.Same(problem, c.Current);                    // the same decision, in a new run
        await c.SubmitPlayAsync(BestPlay());                // run 2, live
        var secondRun = ScoredReview(c.Review);
        await ComeBackAsync(c);
        await c.SubmitPlayAsync(AltPlay());                 // run 2, practice

        Assert.Equal(2, sink.Plays.Count);
        Assert.Same(firstRun, sink.Plays[0]);
        Assert.Same(secondRun, sink.Plays[1]);
        Assert.Equal(sink.Plays[0].ProblemKey, sink.Plays[1].ProblemKey); // one position, twice
        Assert.False(sink.Plays[0].IsCorrect);
        Assert.True(sink.Plays[1].IsCorrect);
    }

    [Fact]
    public async Task AnOffListPlay_FoldsNothing()
    {
        // Producer contract: off-list plays are skips, never lifetime
        // submissions — the run reports no answer of record for one, so there
        // is nothing to fold, at its Submit or after.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitPlayAsync(BestPlay());
        var live = Assert.Single(sink.Plays);
        await c.NextAsync();

        await c.SubmitPlayAsync(UnknownPlay());
        OffListReview(c.Review);
        Assert.Same(live, Assert.Single(sink.Plays));

        await c.NextAsync();

        Assert.Same(live, Assert.Single(sink.Plays));
        Assert.Equal(1, c.SkippedCount);
    }

    [Fact]
    public async Task APlayTheRankingDoesNotScore_FoldsNothing()
    {
        // "A skip of record that folds nothing" (SPEC-scoring.md §2a), at its
        // Submit and after — beside a live answer that did fold at its own.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.DepthSplitDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.DepthFirst);
        await c.SubmitPlayAsync(BestPlay());
        var live = Assert.Single(sink.Plays);
        await c.NextAsync();

        await c.SubmitPlayAsync(TestFixtures.OpeningBest()); // not scored under depth first
        Assert.Equal(PlaySubmissionKind.NotScored, Assert.IsType<ProblemReview.Play>(c.Review).Submission.Kind);
        Assert.Same(live, Assert.Single(sink.Plays));

        await c.NextAsync();

        Assert.Same(live, Assert.Single(sink.Plays));
    }

    [Fact]
    public async Task ADeferral_FoldsNothing()
    {
        // ▶ named Skip defers an unanswered problem: nothing of record, so
        // nothing to fold — beside a live answer that did fold.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitPlayAsync(BestPlay());
        var live = Assert.Single(sink.Plays);
        await c.NextAsync();

        await c.NextAsync();

        Assert.Equal(1, c.SkippedCount);
        Assert.Same(live, Assert.Single(sink.Plays));
    }

    [Fact]
    public async Task EndQuizsConversion_OfTheUnresolvedProblems_FoldsNothing()
    {
        // Finishing converts every problem still unresolved — the deferred one
        // and the one on screen — to a skip of record, and a skip folds nothing.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitPlayAsync(BestPlay());
        var live = Assert.Single(sink.Plays);
        await c.NextAsync();
        await c.NextAsync();

        await c.EndQuizAsync();

        Assert.Equal(2, c.SkippedCount);
        Assert.Same(live, Assert.Single(sink.Plays));
    }

    [Fact]
    public async Task APracticeSubmission_FoldsNothing()
    {
        // A practice submission makes nothing of record, so the run reports
        // none and nothing folds — however many cycles, play or cube.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05),
            TestFixtures.CubeDecision(),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 1));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitPlayAsync(AltPlay());
        var recordedPlay = ScoredReview(c.Review);
        await c.NextAsync();
        await c.SubmitCubeAnswerAsync(CubeAnswer.NoDoublePass);
        var recordedCube = CubeReview(c.Review);

        for (var cycle = 0; cycle < 4; cycle++)
        {
            c.GoToFirst();
            await c.SubmitPlayAsync(cycle % 2 == 0 ? BestPlay() : UnknownPlay());
            Assert.True(c.Review!.IsPractice);
            Assert.Same(recordedPlay, Assert.Single(sink.Plays));
        }

        c.GoToLast();
        await c.SubmitCubeAnswerAsync(CubeAnswer.DoubleTake);
        Assert.True(c.Review!.IsPractice);

        Assert.Same(recordedPlay, Assert.Single(sink.Plays));
        Assert.Same(recordedCube, Assert.Single(sink.Cubes));
    }

    [Fact]
    public async Task OffListThenOnListPractice_FoldsNothing()
    {
        // The record and the review disagree hardest here: the record is a skip
        // (no answer of record at all) while the practice review on screen is an
        // on-list scored play. The fold is what the run reports a submission
        // made of record — nothing, both times — never the review's shape.
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitPlayAsync(UnknownPlay()); // of record: a skip
        await ComeBackAsync(c);
        await c.SubmitPlayAsync(BestPlay());    // practice: on-list
        ScoredReview(c.Review);

        await c.NextAsync();

        Assert.Equal(0, sink.TotalFolds);
        Assert.Equal(1, c.SkippedCount);
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

    [Theory]
    [InlineData("Skip")]
    [InlineData("End quiz")]
    public async Task ASourceFault_OnTheAdvanceAfterALiveAnswer_ThenARetry_AddsNoSecondFold(string retry)
    {
        // The repeat fold this leg closes. The draw after a live answer faults;
        // the run has already moved on from the review, so it is left on the
        // answered problem, answering again. Folding as the run advanced, a
        // second Skip or an End quiz there folded the same answer a second time.
        // Folding at Submit, nothing the retry does can reach the sink.
        var source = new FaultingProblemSetSource(
            faultAtDraw: 2, TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        var sink = new FakeProblemStatsSink();
        var c = new QuizController((_, _, _) => TestFixtures.Composed(source), sink, TimeProvider.System);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitPlayAsync(BestPlay());
        var submitted = Assert.Single(sink.Plays);

        await Assert.ThrowsAsync<InvalidOperationException>(() => c.NextAsync());
        Assert.False(c.IsBusy);
        Assert.Null(c.Review);
        Assert.NotNull(c.Current);

        if (retry == "Skip")
            await c.NextAsync();
        else
            await c.EndQuizAsync();

        Assert.True(c.IsFinished);
        Assert.Same(submitted, Assert.Single(sink.Plays));
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
    }

    [Fact]
    public async Task InterleavedQuiz_FoldsEachAnswerAtItsSubmit_InOrder()
    {
        var c = MakeWithSink(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05),
            TestFixtures.CubeDecision(),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        await c.SubmitPlayAsync(AltPlay());
        Assert.Equal(1, sink.TotalFolds);
        await c.NextAsync();
        await c.SubmitCubeAnswerAsync(CubeAnswer.DoubleTake);
        Assert.Equal(2, sink.TotalFolds);
        await c.NextAsync();
        await c.NextAsync(); // third problem deferred — no fold

        Assert.Single(sink.Plays);
        Assert.Single(sink.Cubes);
        Assert.Equal(2, sink.TotalFolds);
    }

    /// <summary>
    /// A source that yields its items and faults on the draw numbered
    /// <c>faultAtDraw</c> (1-based) — the advance that fails after a problem
    /// was answered. A faulted async iterator is finished, so a later draw
    /// reports the source exhausted rather than faulting again.
    /// </summary>
    private sealed class FaultingProblemSetSource(int faultAtDraw, params BgDecisionData[] items) : IProblemSetSource
    {
        public string Name => "Faulting";
        public int? Count => null;

        public async IAsyncEnumerable<BgDecisionData> EnumerateAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var draw = 1; ; draw++)
            {
                await Task.Yield();
                if (draw == faultAtDraw)
                    throw new InvalidOperationException("The source faulted on the draw.");
                if (draw > items.Length) yield break;
                yield return items[draw - 1];
            }
        }
    }

    // -----------------------------------------------------------------------
    //  StateChanged firing
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StateChanged_FiresTwicePerGatedTransition_SubmitIncluded()
    {
        // The gated async transitions each fire exactly twice — busy-on (so
        // pages render the busy affordances before the churn) and busy-off
        // (delivering the end state; PresentNextAsync itself fires nothing).
        // Submit is one of them now that it awaits its write to the lifetime
        // record: its busy-on fire carries the review, its busy-off releases it.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        var snapshots = new List<(bool Busy, bool Reviewing)>();
        c.StateChanged += () => snapshots.Add((c.IsBusy, c.Review is not null));

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity); // +2 — gate on/off around advance to first
        Assert.Equal(2, snapshots.Count);
        await c.SubmitPlayAsync(BestPlay());    // +2 — gate on with the review up, off once the write lands
        Assert.Equal([(true, true), (false, true)], snapshots[2..]);
        await c.NextAsync();                // +2 — gate on/off around advance to second
        await c.NextAsync();             // +2 — gate on/off around skip + advance (exhausts)

        Assert.Equal(8, snapshots.Count);
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
        await c.SubmitPlayAsync(BestPlay()); // in review, one scored answer

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

        await c.NextAsync();
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
        await c.SubmitPlayAsync(BestPlay());
        await c.NextAsync();
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

        await c.NextAsync();
        Assert.Same(d2, c.Current);
        Assert.Equal(3, c.ProblemNumber); // slot 2 consumed silently
        Assert.Equal(c.ProblemCount, c.ProblemNumber); // N == M on the stream's last slot

        // N is the number of the problem on screen (SPEC-quiz-history.md §5),
        // and a finished run has none: the total stands, the number goes.
        await c.NextAsync();
        Assert.True(c.IsFinished);
        Assert.Equal(0, c.ProblemNumber);
        Assert.Equal(3, c.ProblemCount);
    }

    [Fact]
    public async Task ProblemNumber_FollowsTheCursor_RestartResets()
    {
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"), away: 1);
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"), away: 2);
        var c = Make(d1, d2);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitPlayAsync(BestPlay());
        await c.NextAsync();
        Assert.Equal(2, c.ProblemNumber);

        await c.SubmitPlayAsync(BestPlay());
        Assert.Equal(2, c.ProblemNumber); // a review is the same slot
        c.GoBack();
        Assert.Equal(1, c.ProblemNumber); // and the number is the problem on screen
        c.GoToLast();
        Assert.Equal(2, c.ProblemNumber);

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
            await c.NextAsync();
            seen.Add(c.RandomHomeBoardOnRight);
        }

        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public async Task RandomHomeBoardOnRight_HoldsStillForOneProblem_AcrossSubmitAndEveryReturn()
    {
        // One problem, one side — the rule that keeps the board from moving
        // under the user between answering it and reading its solution, and
        // again whenever navigation returns them to it.
        var c = Make(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), away: 1));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        var rolled = c.RandomHomeBoardOnRight;

        await c.SubmitPlayAsync(BestPlay());
        Assert.Equal(rolled, c.RandomHomeBoardOnRight);

        for (var visit = 0; visit < 6; visit++)
        {
            await ComeBackAsync(c);
            Assert.Equal(rolled, c.RandomHomeBoardOnRight);
        }
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
        await c.SubmitPlayAsync(BestPlay());
        Assert.Equal(shown, c.RandomHomeBoardOnRight);
    }
}
