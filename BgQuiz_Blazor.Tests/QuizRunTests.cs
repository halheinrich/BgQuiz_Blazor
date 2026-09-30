using BgDataTypes_Lib;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The run model, pinned as the pure state it is — no problem source, no stats
/// sink, no page: a run is begun, handed problems, and moved. The model is
/// <c>SPEC-quiz-history.md</c> (ratified 2026-09-24, amended 2026-09-30,
/// halheinrich/backgammon#8), and the sections below follow its own: the
/// sequence and the dispositions (§1), live and practice (§3), the transitions
/// and End quiz (§2, §4), what survives navigation (§5), and the ranking (§7).
///
/// <para>
/// Most of what is pinned here no page reaches yet: <see cref="QuizController"/>
/// wires a run that only moves forward. That is the point of the suite — the
/// cursor moving back and forth, practice on every completed problem, End quiz
/// from an earlier problem, the board's side on return — has to hold before
/// the navigation controls are built on it. What the controller does wire is
/// pinned a second time, through it, by <see cref="QuizControllerTests"/>.
/// </para>
/// </summary>
public class QuizRunTests
{
    private static Play Best() => TestFixtures.OpeningBest();
    private static Play Alt() => TestFixtures.OpeningAlternative();
    private static Play OffList() => TestFixtures.OpeningUnlisted();

    /// <summary>
    /// A checker play whose second candidate loses 0.05; <paramref name="n"/>
    /// makes its content distinct, so a test can tell its problems apart.
    /// </summary>
    private static CheckerPlayDecision PlayProblem(int n) =>
        TestFixtures.TwoChoiceDecision(Best(), Alt(), play2Loss: 0.05, away: n);

    /// <summary>A cube decision whose best answer is Double / Take; <paramref name="n"/> makes its content distinct.</summary>
    private static CubeDecision CubeProblem(int n) => TestFixtures.CubeDecision(away: n);

    private static QuizRun Begin(PlayRanking ranking = PlayRanking.Equity) => QuizRun.Begin(ranking);

    /// <summary>Present <paramref name="problem"/> with no slot passed over before it.</summary>
    private static QuizRun Show(QuizRun run, BgDecisionData problem, bool side = false) =>
        run.Present(problem, silentlySkippedBefore: 0, randomHomeBoardOnRight: side);

    /// <summary>▶ on the frontier, which must owe a new problem, and then that problem.</summary>
    private static QuizRun MoveOnTo(QuizRun run, BgDecisionData problem, bool side = false)
    {
        var moved = run.Next(out var bringsNewProblem);
        Assert.True(bringsNewProblem, "Expected ▶ on the frontier to owe a new problem.");
        return Show(moved, problem, side);
    }

    /// <summary>
    /// The staged run most of the navigation pins stand on. Three problems
    /// presented: the first <b>answered</b> with the second-best play (so its
    /// record costs 0.05 and a correct practice answer would show), the second
    /// <b>skipped</b>, and the third — the frontier — <b>unresolved</b> and on
    /// screen.
    /// </summary>
    private static QuizRun ThreePresented(
        out CheckerPlayDecision answered, out CheckerPlayDecision skipped, out CheckerPlayDecision unresolved)
    {
        answered = PlayProblem(1);
        skipped = PlayProblem(2);
        unresolved = PlayProblem(3);

        var run = Show(Begin(), answered).SubmitPlay(Alt());
        run = MoveOnTo(run, skipped);
        run = MoveOnTo(run, unresolved);

        Assert.Equal(3, run.Presented.Length);
        Assert.Same(run.Presented[2], run.Cursor);
        return run;
    }

    private static QuizRun ThreePresented() => ThreePresented(out _, out _, out _);

    /// <summary>The scored submission a play review shows — the producer's outcome read by its case, never compared.</summary>
    private static SubmittedPlay ScoredReview(ProblemReview? review)
    {
        var play = Assert.IsType<ProblemReview.Play>(review);
        Assert.True(play.Submission.TryGetScored(out var submitted),
            $"Expected a scored review; the outcome was {play.Submission.Kind}.");
        return submitted;
    }

    private static AnswerOfRecord AnswerOf(PresentedProblem problem)
    {
        Assert.Equal(ProblemDispositionKind.Answered, problem.Disposition.Kind);
        Assert.True(problem.Disposition.TryGetAnswer(out var answer));
        return answer;
    }

    /// <summary>The checker-play answer of record of <paramref name="problem"/>, asserting it has one.</summary>
    private static SubmittedPlay PlayOfRecord(PresentedProblem problem) =>
        AnswerOf(problem).Match(
            play: submitted => submitted,
            cube: _ => throw new Xunit.Sdk.XunitException("Expected a checker-play answer of record; it is a cube pair."));

    /// <summary>The cube answer of record of <paramref name="problem"/>, asserting it has one.</summary>
    private static SubmittedCubeAction CubeOfRecord(PresentedProblem problem) =>
        AnswerOf(problem).Match(
            play: _ => throw new Xunit.Sdk.XunitException("Expected a cube answer of record; it is a checker play."),
            cube: submitted => submitted);

    /// <summary>The dispositions of the whole sequence, by reference: what "nothing of record changed" compares.</summary>
    private static ProblemDisposition[] Dispositions(QuizRun run) =>
        [.. run.Presented.Select(problem => problem.Disposition)];

    /// <summary>Every disposition is the same instance as before — nothing of record was written or rewritten.</summary>
    private static void AssertRecordUnchanged(QuizRun before, QuizRun after)
    {
        var was = Dispositions(before);
        var now = Dispositions(after);
        Assert.Equal(was.Length, now.Length);
        for (var i = 0; i < was.Length; i++)
            Assert.Same(was[i], now[i]);
        Assert.Equal(before.Score, after.Score);
        Assert.Equal(before.SkippedCount, after.SkippedCount);
    }

    // -----------------------------------------------------------------------
    //  Beginning a run
    // -----------------------------------------------------------------------

    [Fact]
    public void Begin_PresentsNothing_AndHoldsNothingOfRecord()
    {
        var run = Begin();

        Assert.Empty(run.Presented);
        Assert.Null(run.Cursor);
        Assert.Null(run.Frontier);
        Assert.Null(run.Review);
        Assert.False(run.IsEnded);
        Assert.False(run.IsLive);
        Assert.False(run.IsAnswering);
        Assert.False(run.CanGoBack);
        Assert.False(run.CanGoToLast);
        Assert.Equal(QuizScore.Empty, run.Score);
        Assert.Equal(0, run.SkippedCount);
        Assert.Null(run.ProblemCount);
    }

    [Theory]
    [InlineData(PlayRanking.Equity)]
    [InlineData(PlayRanking.DepthFirst)]
    public void Begin_TakesTheRankingItIsHanded(PlayRanking ranking)
    {
        Assert.Equal(ranking, QuizRun.Begin(ranking).Ranking);
    }

    [Fact]
    public void Begin_UndefinedRanking_Throws()
    {
        // No run, no ranking — and no default standing in for a malformed one.
        Assert.Throws<ArgumentOutOfRangeException>(() => QuizRun.Begin((PlayRanking)99));
    }

    // -----------------------------------------------------------------------
    //  §1 · The presented sequence, the cursor and the frontier
    // -----------------------------------------------------------------------

    [Fact]
    public void Present_TheFirstProblem_LandsOnIt_UnresolvedAndLive()
    {
        var problem = PlayProblem(1);

        var run = Show(Begin(), problem);

        var presented = Assert.Single(run.Presented);
        Assert.Same(problem, presented.Problem);
        Assert.Same(presented, run.Cursor);
        Assert.Same(presented, run.Frontier);
        Assert.Same(ProblemDisposition.Unresolved, presented.Disposition);
        Assert.True(run.IsLive);
        Assert.True(run.IsAnswering);
        Assert.Null(run.Review);
    }

    [Fact]
    public void Present_AfterMovingOn_AppendsTheNewFrontier_AndLandsOnIt()
    {
        var first = PlayProblem(1);
        var second = PlayProblem(2);

        var run = MoveOnTo(Show(Begin(), first).SubmitPlay(Best()), second);

        Assert.Equal(2, run.Presented.Length);
        Assert.Same(first, run.Presented[0].Problem);
        Assert.Same(second, run.Presented[1].Problem);
        Assert.Same(run.Presented[1], run.Cursor);
        Assert.Same(run.Presented[1], run.Frontier);
        Assert.True(run.IsLive);
    }

    [Fact]
    public void Present_FromALiveReview_DiscardsTheReview_AndShowsTheNewDecision()
    {
        // The completed frontier, its review still on screen: presenting the
        // next problem is moving on from it, and the landing shows a decision.
        var run = Show(Begin(), PlayProblem(1)).SubmitPlay(Best());
        Assert.NotNull(run.Review);

        run = Show(run, PlayProblem(2));

        Assert.Null(run.Review);
        Assert.True(run.IsAnswering);
        Assert.Same(run.Presented[1], run.Cursor);
    }

    [Fact]
    public void Present_OverAnUnresolvedFrontier_IsRefused()
    {
        // Only the frontier may be unresolved: a new problem over an unresolved
        // one would leave an unresolved problem behind the frontier.
        var run = Show(Begin(), PlayProblem(1));

        Assert.Throws<InvalidOperationException>(() => Show(run, PlayProblem(2)));
    }

    [Fact]
    public void Present_WithTheCursorBehindTheFrontier_IsRefused()
    {
        // "Navigation never presents an unseen problem early": only moving on
        // at the frontier brings a new problem. The frontier here is completed,
        // so the cursor's position is the one thing refusing it.
        var run = MoveOnTo(Show(Begin(), PlayProblem(1)).SubmitPlay(Best()), PlayProblem(2)).SubmitPlay(Best());
        run = run.GoBack();
        Assert.True(run.Frontier!.Disposition.IsCompleted);

        Assert.Throws<InvalidOperationException>(() => Show(run, PlayProblem(3)));
    }

    [Fact]
    public void Present_NullProblem_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Begin().Present(null!, 0, randomHomeBoardOnRight: false));
    }

    [Fact]
    public void Present_NegativeSilentSkips_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Begin().Present(PlayProblem(1), silentlySkippedBefore: -1, randomHomeBoardOnRight: false));
    }

    [Fact]
    public void OnlyTheFrontier_IsEverUnresolved()
    {
        // Walked through every way the run moves: at each step, every problem
        // behind the frontier holds something of record.
        static void AssertOnlyTheFrontierMayBeUnresolved(QuizRun run)
        {
            for (var i = 0; i < run.Presented.Length - 1; i++)
                Assert.True(run.Presented[i].Disposition.IsCompleted, $"Problem {i} is behind the frontier and unresolved.");
        }

        var run = Show(Begin(), PlayProblem(1));
        AssertOnlyTheFrontierMayBeUnresolved(run);

        run = MoveOnTo(run, CubeProblem(2));                       // ▶ on an unresolved frontier
        AssertOnlyTheFrontierMayBeUnresolved(run);

        run = MoveOnTo(run.SubmitCubeAction(CubeClaimPair.DoubleTake), PlayProblem(3));
        AssertOnlyTheFrontierMayBeUnresolved(run);

        run = run.GoToFirst();                                     // the frontier left unresolved behind the cursor's back
        AssertOnlyTheFrontierMayBeUnresolved(run);
        Assert.False(run.Frontier!.Disposition.IsCompleted);

        run = run.SubmitPlay(Best()).GoToLast();                   // practice on the way
        AssertOnlyTheFrontierMayBeUnresolved(run);

        run = run.End();
        Assert.All(run.Presented, problem => Assert.True(problem.Disposition.IsCompleted));
    }

    // -----------------------------------------------------------------------
    //  §1, §3 · A live submission writes the record
    // -----------------------------------------------------------------------

    [Fact]
    public void SubmitPlay_OnTheUnresolvedFrontier_IsTheAnswerOfRecord()
    {
        var problem = PlayProblem(1);
        var run = Show(Begin(), problem);

        run = run.SubmitPlay(Alt());

        var review = Assert.IsType<ProblemReview.Play>(run.Review);
        Assert.False(review.IsPractice);
        var shown = ScoredReview(review);
        Assert.False(shown.IsCorrect);
        Assert.Equal(0.05, shown.EquityLoss, 6);
        Assert.Equal(1, shown.MatchedCandidateIndex);
        Assert.Equal(ProblemKey.From(problem), shown.ProblemKey);

        // The record is the very submission the review shows: one scoring, so
        // what counts and what was displayed cannot disagree.
        Assert.Same(shown, PlayOfRecord(run.Cursor!));

        // The cursor has not moved, and the problem is no longer live.
        Assert.Same(problem, run.Cursor!.Problem);
        Assert.False(run.IsLive);
        Assert.False(run.IsAnswering);
    }

    [Fact]
    public void SubmitPlay_OffList_OnTheUnresolvedFrontier_IsASkipOfRecord()
    {
        var run = Show(Begin(), PlayProblem(1)).SubmitPlay(OffList());

        var review = Assert.IsType<ProblemReview.Play>(run.Review);
        Assert.Equal(PlaySubmissionKind.OffList, review.Submission.Kind);
        Assert.False(review.IsPractice);
        Assert.True(OffList().IsSameEncoding(review.UserPlay));
        Assert.Same(ProblemDisposition.Skipped, run.Cursor!.Disposition);
        Assert.Equal(QuizScore.Empty, run.Score);
        Assert.Equal(1, run.SkippedCount);
    }

    [Fact]
    public void SubmitPlay_APlayTheRankingDoesNotScore_OnTheUnresolvedFrontier_IsASkipOfRecord()
    {
        // SPEC-scoring.md §2a: under depth first the 3-ply 8/5 6/5 was analyzed
        // less deeply than the best and rated higher, so it is not scored.
        var run = Show(Begin(PlayRanking.DepthFirst), TestFixtures.DepthSplitDecision()).SubmitPlay(Best());

        var review = Assert.IsType<ProblemReview.Play>(run.Review);
        Assert.Equal(PlaySubmissionKind.NotScored, review.Submission.Kind);
        Assert.Equal(1, review.CandidateIndex);
        Assert.Same(ProblemDisposition.Skipped, run.Cursor!.Disposition);
        Assert.Equal(QuizScore.Empty, run.Score);
        Assert.Equal(1, run.SkippedCount);
    }

    [Fact]
    public void SubmitCubeAction_OnTheUnresolvedFrontier_IsTheAnswerOfRecord()
    {
        var problem = CubeProblem(7);
        var run = Show(Begin(), problem).SubmitCubeAction(CubeClaimPair.NoDoublePass);

        var review = Assert.IsType<ProblemReview.Cube>(run.Review);
        Assert.False(review.IsPractice);
        Assert.Equal(CubeClaimPair.NoDoublePass, review.Submission.UserDecision);
        Assert.Equal(ProblemKey.From(problem), review.Submission.ProblemKey);
        Assert.Same(review.Submission, CubeOfRecord(run.Cursor!));

        // Built through the producer's one factory, field for field.
        Assert.Equal(
            SubmittedCubeAction.From(ProblemKey.From(problem), CubeClaimPair.NoDoublePass, problem.Decision),
            review.Submission);
        Assert.False(run.IsLive);
    }

    public enum SkipCause
    {
        TheSkipGesture,
        AnOffListPlay,
        APlayTheRankingDoesNotScore,
        EndQuiz,
    }

    [Theory]
    [InlineData(SkipCause.TheSkipGesture)]
    [InlineData(SkipCause.AnOffListPlay)]
    [InlineData(SkipCause.APlayTheRankingDoesNotScore)]
    [InlineData(SkipCause.EndQuiz)]
    public void ASkipOfRecord_CarriesNoCauseAndNoSubmission(SkipCause cause)
    {
        // Ruling 1 (2026-09-30): every way of completing a problem without an
        // answer writes the same state. Whatever caused it, the disposition is
        // the one Skipped, holding nothing — so nothing downstream can come to
        // depend on a cause or a play the model does not keep.
        var run = Show(Begin(PlayRanking.DepthFirst), TestFixtures.DepthSplitDecision());

        run = cause switch
        {
            SkipCause.TheSkipGesture => run.Next(out _),
            SkipCause.AnOffListPlay => run.SubmitPlay(OffList()),
            SkipCause.APlayTheRankingDoesNotScore => run.SubmitPlay(Best()),
            SkipCause.EndQuiz => run.End(),
            _ => throw new ArgumentOutOfRangeException(nameof(cause)),
        };

        var disposition = Assert.Single(run.Presented).Disposition;
        Assert.Same(ProblemDisposition.Skipped, disposition);
        Assert.Equal(ProblemDispositionKind.Skipped, disposition.Kind);
        Assert.False(disposition.TryGetAnswer(out var answer));
        Assert.Null(answer);
        Assert.Equal(1, run.SkippedCount);
        Assert.Equal(QuizScore.Empty, run.Score);
    }

    [Fact]
    public void SubmitPlay_OnACubeDecision_Throws()
    {
        // The page routes each kind to its own instrument, so this is a caller
        // bug, and it fails loud rather than filing a cube as an off-list play.
        var run = Show(Begin(), CubeProblem(1));

        Assert.Throws<InvalidOperationException>(() => run.SubmitPlay(Best()));
    }

    [Fact]
    public void SubmitCubeAction_OnACheckerPlay_Throws()
    {
        var run = Show(Begin(), PlayProblem(1));

        Assert.Throws<InvalidOperationException>(() => run.SubmitCubeAction(CubeClaimPair.DoubleTake));
    }

    [Fact]
    public void Submit_WhileAReviewIsShowing_IsRefused()
    {
        // §4's table: from either review state Submit is no transition at all.
        var play = Show(Begin(), PlayProblem(1)).SubmitPlay(Best());
        var cube = Show(Begin(), CubeProblem(1)).SubmitCubeAction(CubeClaimPair.DoubleTake);

        Assert.Throws<InvalidOperationException>(() => play.SubmitPlay(Alt()));
        Assert.Throws<InvalidOperationException>(() => cube.SubmitCubeAction(CubeClaimPair.NoDoublePass));
    }

    [Fact]
    public void Submit_WithNothingOnScreen_IsRefused()
    {
        var notYetPresented = Begin();
        var ended = Show(Begin(), PlayProblem(1)).End();

        Assert.Throws<InvalidOperationException>(() => notYetPresented.SubmitPlay(Best()));
        Assert.Throws<InvalidOperationException>(() => notYetPresented.SubmitCubeAction(CubeClaimPair.DoubleTake));
        Assert.Throws<InvalidOperationException>(() => ended.SubmitPlay(Best()));
        Assert.Throws<InvalidOperationException>(() => ended.SubmitCubeAction(CubeClaimPair.DoubleTake));
    }

    // -----------------------------------------------------------------------
    //  §3 · Live or practice, derived from the disposition
    // -----------------------------------------------------------------------

    [Fact]
    public void IsLive_OnlyOnTheUnresolvedFrontier_WhereverTheCursorHasBeen()
    {
        // "Unresolved" is not "on screen": with the cursor on an earlier
        // problem the frontier stays unresolved, and live is read off the
        // problem under the cursor each time — there is no flag to go stale.
        var run = ThreePresented();
        Assert.True(run.IsLive);

        run = run.GoBack();                       // on the skipped problem
        Assert.False(run.IsLive);
        Assert.Same(ProblemDisposition.Unresolved, run.Frontier!.Disposition);

        run = run.GoToFirst();                    // on the answered problem
        Assert.False(run.IsLive);
        Assert.Same(ProblemDisposition.Unresolved, run.Frontier!.Disposition);

        run = run.GoToLast();                     // back on the frontier, still unresolved
        Assert.True(run.IsLive);

        run = run.SubmitPlay(Best());             // and now completed
        Assert.False(run.IsLive);
        run = run.Redo();
        Assert.False(run.IsLive);
    }

    [Fact]
    public void SubmitPlay_OnAnAnsweredProblem_IsPractice_AndChangesNothing()
    {
        var answered = Show(Begin(), PlayProblem(1)).SubmitPlay(Alt());   // of record: not best, 0.05 lost
        var recorded = PlayOfRecord(answered.Cursor!);

        var practised = answered.Redo().SubmitPlay(Best());               // practice: the best play

        // Scored on its own merits and reviewed, marked as practice…
        var review = Assert.IsType<ProblemReview.Play>(practised.Review);
        Assert.True(review.IsPractice);
        Assert.True(ScoredReview(review).IsCorrect);
        Assert.Equal(0.0, ScoredReview(review).EquityLoss, 6);

        // …and recorded nowhere: the first answer stands, and so do the totals.
        Assert.Same(recorded, PlayOfRecord(practised.Cursor!));
        AssertRecordUnchanged(answered, practised);
        Assert.Equal(0, practised.Score.Total.Correct);
        Assert.Equal(0.05, practised.Score.Total.TotalEquityLoss, 6);
    }

    [Fact]
    public void SubmitPlay_OnASkippedProblem_IsPractice_AndTheSkipStands()
    {
        // The strongest form: the record is a skip and the practice answer is
        // on the list and correct. It scores nothing, and the skip stays a skip.
        var skipped = Show(Begin(), PlayProblem(1)).SubmitPlay(OffList());

        var practised = skipped.Redo().SubmitPlay(Best());

        var review = Assert.IsType<ProblemReview.Play>(practised.Review);
        Assert.True(review.IsPractice);
        Assert.True(ScoredReview(review).IsCorrect);
        Assert.Same(ProblemDisposition.Skipped, practised.Cursor!.Disposition);
        AssertRecordUnchanged(skipped, practised);
        Assert.Equal(QuizScore.Empty, practised.Score);
        Assert.Equal(1, practised.SkippedCount);
    }

    [Fact]
    public void SubmitPlay_OffList_OnAnAnsweredProblem_AddsNoSkip()
    {
        // Recordless in the other direction too: a practice play that misses
        // the list must not mint a skip on a problem that is already answered.
        var answered = Show(Begin(), PlayProblem(1)).SubmitPlay(Best());

        var practised = answered.Redo().SubmitPlay(OffList());

        var review = Assert.IsType<ProblemReview.Play>(practised.Review);
        Assert.True(review.IsPractice);
        Assert.Equal(PlaySubmissionKind.OffList, review.Submission.Kind);
        AssertRecordUnchanged(answered, practised);
        Assert.Equal(0, practised.SkippedCount);
        Assert.Equal(1, practised.Score.Total.Correct);
    }

    [Fact]
    public void SubmitCubeAction_OnACompletedProblem_IsPractice_AndChangesNothing()
    {
        var answered = Show(Begin(), CubeProblem(1)).SubmitCubeAction(CubeClaimPair.NoDoublePass);   // of record: both halves wrong
        var recorded = CubeOfRecord(answered.Cursor!);

        var practised = answered.Redo().SubmitCubeAction(CubeClaimPair.DoubleTake);                  // practice: both right

        var review = Assert.IsType<ProblemReview.Cube>(practised.Review);
        Assert.True(review.IsPractice);
        Assert.Equal(CubeClaimPair.DoubleTake, review.Submission.UserDecision);
        Assert.Same(recorded, CubeOfRecord(practised.Cursor!));
        AssertRecordUnchanged(answered, practised);
        Assert.Equal(0, practised.Score.Total.Correct);
    }

    [Fact]
    public void APracticeSubmission_OfTheVeryPlayOnRecord_IsStillPractice()
    {
        // Ruling 2 (2026-09-30): the run identifies a problem by its place and
        // keeps its disposition; nothing compares submissions. So re-entering
        // the play that is of record is a practice submission like any other —
        // a second scoring, shown and discarded — and the record is still the
        // first instance, never swapped for an equal one.
        var answered = Show(Begin(), PlayProblem(1)).SubmitPlay(Best());
        var recorded = PlayOfRecord(answered.Cursor!);

        var practised = answered.Redo().SubmitPlay(Best());

        var review = Assert.IsType<ProblemReview.Play>(practised.Review);
        Assert.True(review.IsPractice);
        Assert.NotSame(recorded, ScoredReview(review));
        Assert.Same(recorded, PlayOfRecord(practised.Cursor!));
        Assert.Equal(1, practised.Score.Total.Submitted);
    }

    [Fact]
    public void PracticeCycles_AreUnbounded_AndEquallyRecordless()
    {
        var answered = Show(Begin(), PlayProblem(1)).SubmitPlay(Alt());
        var run = answered;

        for (var cycle = 0; cycle < 5; cycle++)
        {
            run = run.Redo().SubmitPlay(cycle % 2 == 0 ? Best() : OffList());
            Assert.True(run.Review!.IsPractice);
            AssertRecordUnchanged(answered, run);
        }
    }

    [Fact]
    public void AnEarlierProblem_IsPractice_WhetherItWasAnsweredOrSkipped()
    {
        // The behaviour no page reaches yet: returning to a completed problem
        // by moving the cursor. Both completed dispositions are practice, and
        // the frontier the user walked away from is still unresolved after.
        var staged = ThreePresented();

        var onSkipped = staged.GoBack().SubmitPlay(Best());
        Assert.True(onSkipped.Review!.IsPractice);
        Assert.True(ScoredReview(onSkipped.Review).IsCorrect);
        AssertRecordUnchanged(staged, onSkipped);

        var onAnswered = onSkipped.GoBack().SubmitPlay(Best());
        Assert.True(onAnswered.Review!.IsPractice);
        Assert.True(ScoredReview(onAnswered.Review).IsCorrect);
        AssertRecordUnchanged(staged, onAnswered);

        Assert.Same(ProblemDisposition.Unresolved, onAnswered.Frontier!.Disposition);
    }

    // -----------------------------------------------------------------------
    //  Redo — today's return to a decision (SPEC-scoring.md §2)
    // -----------------------------------------------------------------------

    [Fact]
    public void Redo_ReturnsToTheDecision_OnTheSameProblem_AndTheRecordStands()
    {
        var reviewed = Show(Begin(), PlayProblem(1)).SubmitPlay(Alt());

        var redone = reviewed.Redo();

        Assert.Null(redone.Review);
        Assert.True(redone.IsAnswering);
        Assert.Same(reviewed.Cursor, redone.Cursor);
        AssertRecordUnchanged(reviewed, redone);
    }

    [Fact]
    public void Redo_WithNoReviewShowing_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => Show(Begin(), PlayProblem(1)).Redo());
        Assert.Throws<InvalidOperationException>(() => Begin().Redo());
    }

    // -----------------------------------------------------------------------
    //  §2, §4 · ▶ — "the next problem", everywhere
    // -----------------------------------------------------------------------

    [Fact]
    public void Next_OnTheUnresolvedFrontier_RecordsTheSkip_AndOwesANewProblem()
    {
        var run = Show(Begin(), PlayProblem(1));

        var moved = run.Next(out var bringsNewProblem);

        Assert.True(bringsNewProblem);
        Assert.Same(ProblemDisposition.Skipped, moved.Frontier!.Disposition);
        Assert.Equal(1, moved.SkippedCount);

        // Until the new problem arrives the completed frontier stays on screen,
        // as a decision.
        Assert.Same(moved.Frontier, moved.Cursor);
        Assert.True(moved.IsAnswering);
        Assert.False(moved.IsLive);
    }

    [Fact]
    public void Next_FromALiveReview_OwesANewProblem_AndTheRecordStands()
    {
        // Continue: the frontier was just completed by a submission, so moving
        // on adds nothing to the record — in particular no skip.
        var reviewed = Show(Begin(), PlayProblem(1)).SubmitPlay(Alt());

        var moved = reviewed.Next(out var bringsNewProblem);

        Assert.True(bringsNewProblem);
        Assert.Null(moved.Review);
        AssertRecordUnchanged(reviewed, moved);
        Assert.Equal(0, moved.SkippedCount);
    }

    [Fact]
    public void Next_FromACompletedFrontiersDecision_OwesANewProblem_AndRecordsNothing()
    {
        // Practice answering at the frontier — today, Skip pressed after a Redo.
        // The problem is answered, so no skip is counted on top of it.
        var redone = Show(Begin(), PlayProblem(1)).SubmitPlay(Best()).Redo();

        var moved = redone.Next(out var bringsNewProblem);

        Assert.True(bringsNewProblem);
        AssertRecordUnchanged(redone, moved);
        Assert.Equal(0, moved.SkippedCount);
    }

    [Fact]
    public void Next_BehindTheFrontier_GoesToTheNextPresentedProblem_AndRecordsNothing()
    {
        var staged = ThreePresented();
        var onFirst = staged.GoToFirst();

        var onSecond = onFirst.Next(out var bringsNewProblem);

        Assert.False(bringsNewProblem);
        Assert.Same(onSecond.Presented[1], onSecond.Cursor);
        Assert.Equal(3, onSecond.Presented.Length);
        AssertRecordUnchanged(staged, onSecond);

        // And from there onto the frontier itself — presented already, so still
        // no new problem, and still unresolved: ▶ reaching it records nothing.
        var onFrontier = onSecond.Next(out bringsNewProblem);

        Assert.False(bringsNewProblem);
        Assert.Same(onFrontier.Frontier, onFrontier.Cursor);
        Assert.True(onFrontier.IsLive);
        AssertRecordUnchanged(staged, onFrontier);
    }

    [Fact]
    public void Next_FromAPracticeReview_GoesToTheNextProblem()
    {
        // §4: on a practice review ▶ is Continue, and Continue there is "the
        // next problem" — the one after the cursor, not a new one.
        var staged = ThreePresented();
        var practiceReview = staged.GoToFirst().SubmitPlay(Best());
        Assert.True(practiceReview.Review!.IsPractice);

        var moved = practiceReview.Next(out var bringsNewProblem);

        Assert.False(bringsNewProblem);
        Assert.Null(moved.Review);
        Assert.Same(moved.Presented[1], moved.Cursor);
        AssertRecordUnchanged(staged, moved);
    }

    [Fact]
    public void Next_WithNothingOnScreen_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => Begin().Next(out _));
        Assert.Throws<InvalidOperationException>(() => Show(Begin(), PlayProblem(1)).End().Next(out _));
    }

    // -----------------------------------------------------------------------
    //  §2, §4 · ⏮ ◀ ⏭
    // -----------------------------------------------------------------------

    [Fact]
    public void GoBack_LandsOnTheProblemBeforeTheCursor()
    {
        var run = ThreePresented().GoBack();

        Assert.Same(run.Presented[1], run.Cursor);
        Assert.True(run.CanGoBack);
        Assert.True(run.CanGoToLast);

        run = run.GoBack();

        Assert.Same(run.Presented[0], run.Cursor);
    }

    [Fact]
    public void GoToFirst_LandsOnTheFirstProblem()
    {
        var run = ThreePresented().GoToFirst();

        Assert.Same(run.Presented[0], run.Cursor);
        Assert.True(run.CanGoToLast);
    }

    [Fact]
    public void GoToLast_LandsOnTheFrontier()
    {
        var run = ThreePresented().GoToFirst().GoToLast();

        Assert.Same(run.Frontier, run.Cursor);
        Assert.True(run.CanGoBack);
    }

    [Fact]
    public void OnTheFirstProblem_GoingBackIsUnavailable_AndRefused()
    {
        // "⏮ and ◀ are unavailable at the first problem."
        var onFirst = ThreePresented().GoToFirst();
        var onlyOne = Show(Begin(), PlayProblem(1));

        Assert.False(onFirst.CanGoBack);
        Assert.Throws<InvalidOperationException>(() => onFirst.GoBack());
        Assert.Throws<InvalidOperationException>(() => onFirst.GoToFirst());
        Assert.False(onlyOne.CanGoBack);
        Assert.Throws<InvalidOperationException>(() => onlyOne.GoBack());
    }

    [Fact]
    public void OnTheFrontier_GoingToLastIsUnavailable_AndRefused()
    {
        // "⏭ goes to the frontier and is unavailable there."
        var onFrontier = ThreePresented();

        Assert.False(onFrontier.CanGoToLast);
        Assert.Throws<InvalidOperationException>(() => onFrontier.GoToLast());
    }

    [Fact]
    public void Navigation_WithNothingOnScreen_IsUnavailable_AndRefused()
    {
        foreach (var run in new[] { Begin(), ThreePresented().End() })
        {
            Assert.False(run.CanGoBack);
            Assert.False(run.CanGoToLast);
            Assert.Throws<InvalidOperationException>(() => run.GoBack());
            Assert.Throws<InvalidOperationException>(() => run.GoToFirst());
            Assert.Throws<InvalidOperationException>(() => run.GoToLast());
        }
    }

    [Fact]
    public void Navigation_FromTheUnresolvedFrontier_LeavesItUnresolved()
    {
        // §4, live answering: ⏮ ◀ leave the frontier unresolved — walking away
        // from a problem is not skipping it.
        var staged = ThreePresented();

        foreach (var left in new[] { staged.GoBack(), staged.GoToFirst() })
        {
            Assert.Same(ProblemDisposition.Unresolved, left.Frontier!.Disposition);
            AssertRecordUnchanged(staged, left);
        }
    }

    [Fact]
    public void Navigation_FromALiveReview_LeavesTheRecordStanding()
    {
        // §4, live review: the frontier was just completed by a submission;
        // navigating away and back changes nothing of it.
        var reviewed = ThreePresented().SubmitPlay(Alt());
        var recorded = PlayOfRecord(reviewed.Frontier!);

        var returned = reviewed.GoToFirst().GoToLast();

        Assert.Same(recorded, PlayOfRecord(returned.Frontier!));
        AssertRecordUnchanged(reviewed, returned);
    }

    [Fact]
    public void Navigation_NeverPresentsAProblem_AndNeverWritesTheRecord()
    {
        var staged = ThreePresented();

        var walked = staged.GoBack().GoBack().Next(out _).GoToFirst().GoToLast().GoBack().GoToLast();

        Assert.Equal(3, walked.Presented.Length);
        for (var i = 0; i < 3; i++)
            Assert.Same(staged.Presented[i], walked.Presented[i]);
    }

    public enum Landing
    {
        Back,
        First,
        Last,
        NextPresented,
        Redo,
        MovingOnFromTheFrontier,
        ANewProblem,
    }

    [Theory]
    [InlineData(Landing.Back)]
    [InlineData(Landing.First)]
    [InlineData(Landing.Last)]
    [InlineData(Landing.NextPresented)]
    [InlineData(Landing.Redo)]
    [InlineData(Landing.MovingOnFromTheFrontier)]
    [InlineData(Landing.ANewProblem)]
    public void EveryLanding_ShowsTheDecision_NeverAReview(Landing landing)
    {
        // §3: "Every landing shows the decision, never the solution." Each move
        // starts from a review on screen — so a review that survived the move
        // would be there to see — and lands in the answering state. The last
        // two start from the frontier's own review, where ▶ and a new problem
        // are taken from; the rest from a practice review on the middle problem.
        var staged = ThreePresented();
        var reviewing = landing is Landing.MovingOnFromTheFrontier or Landing.ANewProblem
            ? staged.SubmitPlay(Best())
            : staged.GoBack().SubmitPlay(Best());
        Assert.NotNull(reviewing.Review);

        var landed = landing switch
        {
            Landing.Back => reviewing.GoBack(),
            Landing.First => reviewing.GoToFirst(),
            Landing.Last => reviewing.GoToLast(),
            Landing.NextPresented => reviewing.Next(out _),
            Landing.Redo => reviewing.Redo(),
            Landing.MovingOnFromTheFrontier => reviewing.Next(out _),
            Landing.ANewProblem => Show(reviewing, PlayProblem(4)),
            _ => throw new ArgumentOutOfRangeException(nameof(landing)),
        };

        Assert.Null(landed.Review);
        Assert.True(landed.IsAnswering);
    }

    [Fact]
    public void ReturningToAProblem_LandsOnItsDecision_NotOnItsEarlierReview()
    {
        // §5: review state is ephemeral, live or practice alike. The answer of
        // record persists; the review that showed it does not come back.
        var reviewed = ThreePresented().SubmitPlay(Best());     // a live review on the frontier
        Assert.NotNull(reviewed.Review);

        var returned = reviewed.GoBack().GoToLast();

        Assert.Same(returned.Frontier, returned.Cursor);
        Assert.Null(returned.Review);
        Assert.Equal(ProblemDispositionKind.Answered, returned.Frontier!.Disposition.Kind);
    }

    // -----------------------------------------------------------------------
    //  §4 · End quiz acts on the frontier, not the cursor
    // -----------------------------------------------------------------------

    [Fact]
    public void End_OnAnUnresolvedFrontier_CompletesItAsASkipOfRecord()
    {
        var staged = ThreePresented();

        var ended = staged.End();

        Assert.True(ended.IsEnded);
        Assert.Same(ProblemDisposition.Skipped, ended.Frontier!.Disposition);
        Assert.Equal(staged.SkippedCount + 1, ended.SkippedCount);
        Assert.Equal(staged.Score, ended.Score);
    }

    [Fact]
    public void End_FromAnEarlierProblem_StillCompletesTheUnresolvedFrontier()
    {
        // Ruling 6 (2026-09-30): the frontier becomes a skip of record "even
        // when the user is currently viewing a past problem" — and the problem
        // they were viewing is left exactly as it was.
        var staged = ThreePresented();
        var onFirst = staged.GoToFirst();
        Assert.NotSame(onFirst.Frontier, onFirst.Cursor);

        var ended = onFirst.End();

        Assert.Same(ProblemDisposition.Skipped, ended.Frontier!.Disposition);
        Assert.Same(staged.Presented[0].Disposition, ended.Presented[0].Disposition);
        Assert.Same(staged.Presented[1].Disposition, ended.Presented[1].Disposition);
        Assert.Equal(staged.SkippedCount + 1, ended.SkippedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void End_WithACompletedFrontier_AddsNothing(bool fromAnEarlierProblem)
    {
        // "If the frontier is already complete, End Quiz adds nothing" — from
        // its own review, or from an earlier problem with a practice review up.
        var completed = ThreePresented().SubmitPlay(Alt());
        var from = fromAnEarlierProblem ? completed.GoToFirst().SubmitPlay(Best()) : completed;

        var ended = from.End();

        Assert.True(ended.IsEnded);
        AssertRecordUnchanged(completed, ended);
    }

    [Fact]
    public void End_LeavesNothingOnScreen_AndTheRecordToBeRead()
    {
        var reviewed = ThreePresented().SubmitPlay(Alt());

        var ended = reviewed.End();

        Assert.Null(ended.Cursor);
        Assert.Null(ended.Review);
        Assert.False(ended.IsAnswering);
        Assert.False(ended.IsLive);
        Assert.Equal(3, ended.Presented.Length);
        Assert.Equal(reviewed.Score, ended.Score);
    }

    [Fact]
    public void End_BeforeAnythingWasPresented_EndsAnEmptyRun()
    {
        // The source that admits no problem: the run ends with nothing in it.
        var ended = Begin().End();

        Assert.True(ended.IsEnded);
        Assert.Empty(ended.Presented);
        Assert.Equal(0, ended.SkippedCount);
    }

    [Fact]
    public void EveryPresentedProblem_IsAccountedFor_OnceTheRunHasEnded()
    {
        // "So every problem presented is accounted for on Done": each one is an
        // answer or a skip, so the two totals add up to the sequence.
        var cube = CubeProblem(2);
        var run = MoveOnTo(Show(Begin(), PlayProblem(1)).SubmitPlay(Best()), cube)
            .SubmitCubeAction(CubeClaimPair.DoubleTake);
        run = MoveOnTo(run, PlayProblem(3));
        run = MoveOnTo(run, PlayProblem(4));

        var ended = run.GoToFirst().End();

        Assert.All(ended.Presented, problem => Assert.True(problem.Disposition.IsCompleted));
        var answered = ended.Presented.Count(problem => problem.Disposition.Kind == ProblemDispositionKind.Answered);
        Assert.Equal(2, answered);
        Assert.Equal(2, ended.SkippedCount);
        Assert.Equal(ended.Presented.Length, answered + ended.SkippedCount);
    }

    public enum Transition
    {
        Present,
        SubmitPlay,
        SubmitCubeAction,
        Redo,
        Next,
        GoToFirst,
        GoBack,
        GoToLast,
        End,
    }

    [Theory]
    [InlineData(Transition.Present)]
    [InlineData(Transition.SubmitPlay)]
    [InlineData(Transition.SubmitCubeAction)]
    [InlineData(Transition.Redo)]
    [InlineData(Transition.Next)]
    [InlineData(Transition.GoToFirst)]
    [InlineData(Transition.GoBack)]
    [InlineData(Transition.GoToLast)]
    [InlineData(Transition.End)]
    public void AnEndedRun_AcceptsNoTransition(Transition transition)
    {
        var ended = ThreePresented().End();

        Assert.Throws<InvalidOperationException>(() => transition switch
        {
            Transition.Present => Show(ended, PlayProblem(4)),
            Transition.SubmitPlay => ended.SubmitPlay(Best()),
            Transition.SubmitCubeAction => ended.SubmitCubeAction(CubeClaimPair.DoubleTake),
            Transition.Redo => ended.Redo(),
            Transition.Next => ended.Next(out _),
            Transition.GoToFirst => ended.GoToFirst(),
            Transition.GoBack => ended.GoBack(),
            Transition.GoToLast => ended.GoToLast(),
            Transition.End => ended.End(),
            _ => throw new ArgumentOutOfRangeException(nameof(transition)),
        });
    }

    // -----------------------------------------------------------------------
    //  §5 · The totals, derived from the dispositions
    // -----------------------------------------------------------------------

    [Fact]
    public void Score_IsTheAnswersOfRecord_FoldedInPresentedOrder()
    {
        var play = PlayProblem(1);
        var cube = CubeProblem(2);

        var run = Show(Begin(), play).SubmitPlay(Alt());
        var playOfRecord = PlayOfRecord(run.Cursor!);
        run = MoveOnTo(run, cube).SubmitCubeAction(CubeClaimPair.NoDoublePass);
        var cubeOfRecord = CubeOfRecord(run.Cursor!);
        run = MoveOnTo(run, PlayProblem(3)).Next(out _);             // and a skip, which scores nothing

        Assert.Equal(QuizScore.Empty.Plus(playOfRecord).Plus(cubeOfRecord), run.Score);
        Assert.Equal(1, run.Score.PlayDecisions.Submitted);
        Assert.Equal(1, run.Score.DoubleDecisions.Submitted);
        Assert.Equal(1, run.Score.TakeDecisions.Submitted);
        Assert.Equal(0.05, run.Score.PlayDecisions.TotalEquityLoss, 6);
        Assert.Equal(1, run.SkippedCount);
    }

    [Fact]
    public void SkippedCount_CountsEverySkipOfRecord_AndNothingElse()
    {
        var run = Show(Begin(), PlayProblem(1)).SubmitPlay(OffList());   // a skip by an off-list play
        run = MoveOnTo(run, PlayProblem(2));
        run = MoveOnTo(run, PlayProblem(3)).SubmitPlay(Best());          // a skip by ▶, then an answer
        run = MoveOnTo(run, PlayProblem(4));

        Assert.Equal(2, run.SkippedCount);                               // the unresolved frontier is not one

        Assert.Equal(3, run.End().SkippedCount);                         // until the run ends on it
    }

    [Fact]
    public void Totals_NeverMoveForPractice()
    {
        // §5: "The running totals are derived from the dispositions, so they
        // never move for practice." Every completed problem of the run is
        // practised here, scored and off the list, on plays and on a cube.
        var cube = CubeProblem(2);
        var run = MoveOnTo(Show(Begin(), PlayProblem(1)).SubmitPlay(Alt()), cube)
            .SubmitCubeAction(CubeClaimPair.NoDoublePass);
        run = MoveOnTo(run, PlayProblem(3));
        var staged = MoveOnTo(run, PlayProblem(4));                      // answered, answered, skipped, unresolved
        var score = staged.Score;
        var skipped = staged.SkippedCount;

        var practised = staged
            .GoToFirst().SubmitPlay(Best())                              // a better play than the record's
            .Next(out _).SubmitCubeAction(CubeClaimPair.DoubleTake)      // the right cube answer
            .Next(out _).SubmitPlay(Best())                              // a scored play on the skipped problem
            .Redo().SubmitPlay(OffList())                                // and an off-list one
            .GoToFirst().SubmitPlay(OffList());                          // off-list on an answered problem

        Assert.True(practised.Review!.IsPractice);
        Assert.Equal(score, practised.Score);
        Assert.Equal(skipped, practised.SkippedCount);
        AssertRecordUnchanged(staged, practised);
    }

    // -----------------------------------------------------------------------
    //  §5 · The board side survives the life of the run
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheBoardSide_IsTheOneHandedInWhenTheProblemWasPresented(bool side)
    {
        // Both values, so neither can pass by being a default: the run rolls
        // nothing and keeps what it was given.
        var run = Show(Begin(), PlayProblem(1), side);

        Assert.Equal(side, run.Cursor!.RandomHomeBoardOnRight);
    }

    [Fact]
    public void TheBoardSide_OfEachProblem_IsTheSameOnEveryReturnToIt()
    {
        // Ruling 7 (2026-09-30): "Returning to a problem must not randomly flip
        // its board." Three problems with sides right, left, right — adjacent
        // ones differ, so a side read off the wrong entry would show.
        bool[] sides = [true, false, true];
        var run = Show(Begin(), PlayProblem(1), sides[0]).SubmitPlay(Best());
        run = MoveOnTo(run, PlayProblem(2), sides[1]);
        run = MoveOnTo(run, PlayProblem(3), sides[2]);

        void AssertTheCursorShows(int index)
        {
            Assert.Same(run.Presented[index], run.Cursor);
            Assert.Equal(sides[index], run.Cursor!.RandomHomeBoardOnRight);
        }

        AssertTheCursorShows(2);
        run = run.GoBack();
        AssertTheCursorShows(1);
        run = run.SubmitPlay(Best());                 // through a practice review…
        AssertTheCursorShows(1);
        run = run.GoToFirst();
        AssertTheCursorShows(0);
        run = run.Next(out _);
        AssertTheCursorShows(1);
        run = run.GoToLast().SubmitPlay(Alt());       // …and the frontier's completion
        AssertTheCursorShows(2);
        run = run.Redo().GoToFirst().GoToLast();
        AssertTheCursorShows(2);

        Assert.Equal(sides, run.End().Presented.Select(problem => problem.RandomHomeBoardOnRight));
    }

    // -----------------------------------------------------------------------
    //  §5 · "Problem N of M"
    // -----------------------------------------------------------------------

    [Fact]
    public void StreamSlot_CountsTheStreamsSlots_SoASilentlySkippedOneShowsAsAGap()
    {
        // N is derived from the sequence: each problem's slot follows from the
        // one before it and the slots passed over in between. No counter is
        // handed in, only how many were skipped since the last presentation.
        var run = Begin().Present(PlayProblem(1), silentlySkippedBefore: 1, randomHomeBoardOnRight: false);
        Assert.Equal(2, run.Cursor!.StreamSlot);                        // slot 1 was passed over

        run = run.Next(out _).Present(PlayProblem(2), silentlySkippedBefore: 0, randomHomeBoardOnRight: false);
        Assert.Equal(3, run.Cursor!.StreamSlot);

        run = run.Next(out _).Present(PlayProblem(3), silentlySkippedBefore: 2, randomHomeBoardOnRight: false);
        Assert.Equal(6, run.Cursor!.StreamSlot);                        // slots 4 and 5 were passed over

        Assert.Equal([2, 3, 6], run.Presented.Select(problem => problem.StreamSlot));
    }

    [Fact]
    public void TheNumberOnScreen_IsTheCursorsSlot_WhereverTheCursorGoes()
    {
        var run = Begin().Present(PlayProblem(1), silentlySkippedBefore: 0, randomHomeBoardOnRight: false);
        run = run.Next(out _).Present(PlayProblem(2), silentlySkippedBefore: 1, randomHomeBoardOnRight: false);
        run = run.Next(out _).Present(PlayProblem(3), silentlySkippedBefore: 0, randomHomeBoardOnRight: false);
        Assert.Equal(4, run.Cursor!.StreamSlot);

        Assert.Equal(3, run.GoBack().Cursor!.StreamSlot);
        Assert.Equal(1, run.GoToFirst().Cursor!.StreamSlot);
        Assert.Equal(3, run.GoToFirst().Next(out _).Cursor!.StreamSlot);
        Assert.Equal(4, run.GoToFirst().GoToLast().Cursor!.StreamSlot);
    }

    [Fact]
    public void ProblemCount_IsUnknownUntilItIsEstablished_AndThatIsAValidState()
    {
        // A source that never states a total: the run is quizzed to its end
        // with M unknown throughout.
        var run = ThreePresented();
        Assert.Null(run.ProblemCount);

        Assert.Null(run.SubmitPlay(Best()).GoToFirst().End().ProblemCount);
    }

    [Fact]
    public void ProblemCount_EstablishedWhenTheRunBegins_StaysThroughEveryTransition()
    {
        // The source that already knows its size.
        var run = Begin().WithProblemCount(7);
        Assert.Equal(7, run.ProblemCount);

        run = Show(run, PlayProblem(1)).SubmitPlay(Best());
        Assert.Equal(7, run.ProblemCount);
        run = MoveOnTo(run.Redo(), PlayProblem(2));
        Assert.Equal(7, run.ProblemCount);
        run = run.GoBack().GoToLast();
        Assert.Equal(7, run.ProblemCount);
        Assert.Equal(7, run.End().ProblemCount);
    }

    [Fact]
    public void ProblemCount_CanBeEstablishedOnceTheFirstDrawHasHappened()
    {
        // The weighted mix learns its size by composing, so the total arrives
        // after the run began — here, after its first problem is on screen.
        var run = Show(Begin(), PlayProblem(1));
        Assert.Null(run.ProblemCount);

        run = run.WithProblemCount(3);

        Assert.Equal(3, run.ProblemCount);
        Assert.Same(run.Presented[0], run.Cursor);        // and nothing else moved
        Assert.True(run.IsLive);
    }

    [Fact]
    public void ProblemCount_OnceEstablished_IsStable()
    {
        var run = Begin().WithProblemCount(7);

        // Saying the same number again is not a change…
        Assert.Same(run, run.WithProblemCount(7));
        // …and a different one is refused, not adopted.
        Assert.Throws<InvalidOperationException>(() => run.WithProblemCount(8));
        Assert.Equal(7, run.ProblemCount);
    }

    [Fact]
    public void ProblemCount_MayBeZero_ButNotNegative()
    {
        // A composition that drew nothing is a total of zero, which is a fact;
        // a negative total is not.
        Assert.Equal(0, Begin().WithProblemCount(0).ProblemCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => Begin().WithProblemCount(-1));
    }

    // -----------------------------------------------------------------------
    //  §7 · The run's ranking
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(PlayRanking.Equity, false, 0.05)]
    [InlineData(PlayRanking.DepthFirst, true, 0.0)]
    public void SubmitPlay_ScoresUnderTheRunsRanking(PlayRanking ranking, bool correct, double loss)
    {
        // The depth-split fixture, where the two rankings disagree: the rollout
        // 13/10 6/5 loses 0.05 to the 3-ply play by equity and is the best when
        // the deepest analysis ranks first. The run is handed no ranking at
        // Submit — it scores under its own.
        var run = Show(Begin(ranking), TestFixtures.DepthSplitDecision()).SubmitPlay(Alt());

        var submitted = PlayOfRecord(run.Cursor!);
        Assert.Equal(ranking, submitted.Ranking);
        Assert.Equal(correct, submitted.IsCorrect);
        Assert.Equal(loss, submitted.EquityLoss, 6);
    }

    [Theory]
    [InlineData(PlayRanking.Equity)]
    [InlineData(PlayRanking.DepthFirst)]
    public void TheRanking_IsTheRuns_ThroughEveryTransition(PlayRanking ranking)
    {
        // Ruling 3 (2026-09-30): "immutable run-scoped semantic state". Every
        // kind of transition is taken, and the ranking read after each.
        var run = QuizRun.Begin(ranking).WithProblemCount(3);
        Assert.Equal(ranking, run.Ranking);

        run = Show(run, PlayProblem(1));
        Assert.Equal(ranking, run.Ranking);
        run = run.SubmitPlay(Best());
        Assert.Equal(ranking, run.Ranking);
        run = run.Redo();
        Assert.Equal(ranking, run.Ranking);
        run = run.Next(out _);
        Assert.Equal(ranking, run.Ranking);
        run = Show(run, CubeProblem(2)).SubmitCubeAction(CubeClaimPair.DoubleTake);
        Assert.Equal(ranking, run.Ranking);
        run = MoveOnTo(run, PlayProblem(3));
        Assert.Equal(ranking, run.Ranking);
        run = run.GoBack();
        Assert.Equal(ranking, run.Ranking);
        run = run.GoToFirst();
        Assert.Equal(ranking, run.Ranking);
        run = run.GoToLast();
        Assert.Equal(ranking, run.Ranking);
        run = run.End();
        Assert.Equal(ranking, run.Ranking);
    }

    // -----------------------------------------------------------------------
    //  The run is immutable
    // -----------------------------------------------------------------------

    [Fact]
    public void ATransition_ReturnsTheRunThatFollows_AndLeavesThisOneAsItWas()
    {
        // What lets the holder of a run replace it whole: a reference taken
        // before a transition still describes the run as it then stood.
        var before = ThreePresented();
        var presented = before.Presented;
        var cursor = before.Cursor;
        var score = before.Score;
        var skipped = before.SkippedCount;

        _ = before.SubmitPlay(Best());
        _ = before.Next(out _);
        _ = before.GoBack();
        _ = before.GoToFirst();
        _ = before.WithProblemCount(9);
        _ = before.End();

        Assert.Equal(presented, before.Presented);
        Assert.Same(cursor, before.Cursor);
        Assert.Same(ProblemDisposition.Unresolved, before.Frontier!.Disposition);
        Assert.Null(before.Review);
        Assert.Null(before.ProblemCount);
        Assert.False(before.IsEnded);
        Assert.True(before.IsLive);
        Assert.Equal(score, before.Score);
        Assert.Equal(skipped, before.SkippedCount);
    }

    // -----------------------------------------------------------------------
    //  The record's own types
    // -----------------------------------------------------------------------

    [Fact]
    public void AnswerOfRecord_HandsBackTheSubmissionItWasMadeFrom()
    {
        var play = TestFixtures.Scored(PlayProblem(1), Best(), PlayRanking.Equity);
        var cubeProblem = CubeProblem(1);
        var cube = SubmittedCubeAction.From(ProblemKey.From(cubeProblem), CubeClaimPair.DoubleTake, cubeProblem.Decision);

        Assert.Same(play, AnswerOfRecord.Of(play).Match<object>(submitted => submitted, submitted => submitted));
        Assert.Same(cube, AnswerOfRecord.Of(cube).Match<object>(submitted => submitted, submitted => submitted));

        // Each kind reaches its own branch and only that one.
        Assert.Equal("play", AnswerOfRecord.Of(play).Match(_ => "play", _ => "cube"));
        Assert.Equal("cube", AnswerOfRecord.Of(cube).Match(_ => "play", _ => "cube"));
    }

    [Fact]
    public void AnswerOfRecord_NullArguments_Throw()
    {
        var answer = AnswerOfRecord.Of(TestFixtures.Scored(PlayProblem(1), Best(), PlayRanking.Equity));

        Assert.Throws<ArgumentNullException>(() => AnswerOfRecord.Of((SubmittedPlay)null!));
        Assert.Throws<ArgumentNullException>(() => AnswerOfRecord.Of((SubmittedCubeAction)null!));
        Assert.Throws<ArgumentNullException>(() => answer.Match<int>(null!, _ => 0));
        Assert.Throws<ArgumentNullException>(() => answer.Match<int>(_ => 0, null!));
    }

    [Fact]
    public void ProblemDisposition_HasExactlyThreeStates()
    {
        Assert.Equal(
            new[] { ProblemDispositionKind.Unresolved, ProblemDispositionKind.Answered, ProblemDispositionKind.Skipped },
            Enum.GetValues<ProblemDispositionKind>());

        var answer = AnswerOfRecord.Of(TestFixtures.Scored(PlayProblem(1), Best(), PlayRanking.Equity));
        var answered = ProblemDisposition.Answered(answer);

        Assert.Equal(ProblemDispositionKind.Unresolved, ProblemDisposition.Unresolved.Kind);
        Assert.False(ProblemDisposition.Unresolved.IsCompleted);
        Assert.False(ProblemDisposition.Unresolved.TryGetAnswer(out _));

        Assert.Equal(ProblemDispositionKind.Skipped, ProblemDisposition.Skipped.Kind);
        Assert.True(ProblemDisposition.Skipped.IsCompleted);
        Assert.False(ProblemDisposition.Skipped.TryGetAnswer(out _));

        Assert.Equal(ProblemDispositionKind.Answered, answered.Kind);
        Assert.True(answered.IsCompleted);
        Assert.True(answered.TryGetAnswer(out var held));
        Assert.Same(answer, held);
    }

    [Fact]
    public void ProblemDisposition_AnsweredWithNoAnswer_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ProblemDisposition.Answered(null!));
    }

    [Fact]
    public void PresentedProblem_RefusesWhatNoRunWouldBuild()
    {
        Assert.Throws<ArgumentNullException>(
            () => new PresentedProblem(null!, 1, false, ProblemDisposition.Unresolved));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PresentedProblem(PlayProblem(1), 0, false, ProblemDisposition.Unresolved));
        Assert.Throws<ArgumentNullException>(
            () => new PresentedProblem(PlayProblem(1), 1, false, null!));
    }
}
