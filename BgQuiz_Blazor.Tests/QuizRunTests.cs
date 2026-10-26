using BgDataTypes_Lib;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The run over a fixed sequence: what is presented, where the cursor is, what each problem's
/// disposition is, and how the totals read. Every transition returns a new run and leaves the
/// old one as it was.
/// </summary>
public class QuizRunTests
{
    private static Play Best() => TestFixtures.OpeningBest();
    private static Play Alt() => TestFixtures.OpeningAlternative();
    private static Play OffList() => TestFixtures.OpeningUnlisted();

    private static CheckerPlayDecision PlayProblem(int n) =>
        TestFixtures.TwoChoiceDecision(Best(), Alt(), play2Loss: 0.05, away: n);

    private static CubeDecision CubeProblem(int n) => TestFixtures.CubeDecision(away: n);

    private static BgDecisionData[] Plays(int count) =>
        [.. Enumerable.Range(1, count).Select(n => (BgDecisionData)PlayProblem(n))];

    /// <summary>A run over <paramref name="sequence"/> with its first problem on screen.</summary>
    private static QuizRun Started(params BgDecisionData[] sequence) =>
        QuizRun.Begin(PlayRanking.Equity, sequence).PresentNext(randomHomeBoardOnRight: false);

    /// <summary>Next at the frontier, then the problem it owes.</summary>
    private static QuizRun MoveOn(QuizRun run, bool side = false)
    {
        var moved = run.Next(out var bringsNewProblem);
        Assert.True(bringsNewProblem, "Expected Next on the frontier to owe a new problem.");
        return moved.PresentNext(side);
    }

    /// <summary>Answered, skipped of record, unresolved frontier; the cursor on the frontier.</summary>
    private static QuizRun ThreePresented(
        out CheckerPlayDecision answered, out CheckerPlayDecision skipped, out CheckerPlayDecision unresolved)
    {
        answered = PlayProblem(1);
        skipped = PlayProblem(2);
        unresolved = PlayProblem(3);
        var run = Started(answered, skipped, unresolved, PlayProblem(4)).SubmitPlay(Alt(), out _);
        run = MoveOn(run).SubmitPlay(OffList(), out _);
        run = MoveOn(run);
        Assert.Equal(
            [ProblemDispositionKind.Answered, ProblemDispositionKind.Skipped, ProblemDispositionKind.Unresolved],
            Kinds(run));
        Assert.Same(run.Presented[2], run.Cursor);
        return run;
    }

    private static QuizRun ThreePresented() => ThreePresented(out _, out _, out _);

    private static QuizRun TwoDeferred()
    {
        var run = MoveOn(MoveOn(Started(Plays(4))));
        Assert.All(Kinds(run), kind => Assert.Equal(ProblemDispositionKind.Unresolved, kind));
        Assert.Same(run.Presented[2], run.Cursor);
        return run;
    }

    private static ProblemDispositionKind[] Kinds(QuizRun run) =>
        [.. run.Presented.Select(problem => problem.Disposition.Kind)];

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

    private static ProblemDisposition[] Dispositions(QuizRun run) =>
        [.. run.Presented.Select(problem => problem.Disposition)];

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

    // ── Begin ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Begin_PresentsNothing_AndHoldsNothingOfRecord_AndKnowsItsCount()
    {
        var run = QuizRun.Begin(PlayRanking.Equity, Plays(3));
        Assert.Empty(run.Presented);
        Assert.Null(run.Cursor);
        Assert.Null(run.Frontier);
        Assert.Null(run.Review);
        Assert.False(run.IsEnded);
        Assert.False(run.IsLive);
        Assert.False(run.IsAnswering);
        Assert.False(run.CanGoBack);
        Assert.False(run.CanGoToLast);
        Assert.True(run.HasMore);
        Assert.Equal(QuizScore.Empty, run.Score);
        Assert.Equal(0, run.SkippedCount);
        Assert.Equal(3, run.ProblemCount);
    }

    [Theory]
    [InlineData(PlayRanking.Equity)]
    [InlineData(PlayRanking.DepthFirst)]
    public void Begin_TakesTheRankingItIsHanded_ThroughEveryTransition(PlayRanking ranking)
    {
        var run = QuizRun.Begin(ranking, Plays(2));
        Assert.Equal(ranking, run.Ranking);
        run = run.PresentNext(false).SubmitPlay(Best(), out _);
        Assert.Equal(ranking, run.Ranking);
        Assert.Equal(ranking, MoveOn(run).End().Ranking);
    }

    [Fact]
    public void Begin_RejectsAnUndefinedRanking_AndANullSequence()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => QuizRun.Begin((PlayRanking)99, Plays(1)));
        Assert.Throws<ArgumentNullException>(() => QuizRun.Begin(PlayRanking.Equity, null!));
    }

    [Fact]
    public void ProblemCount_IsTheSequencesLength_MayBeZero_AndNeverMoves()
    {
        Assert.Equal(0, QuizRun.Begin(PlayRanking.Equity, []).ProblemCount);
        var run = Started(Plays(7));
        Assert.Equal(7, run.ProblemCount);
        run = MoveOn(run).SubmitPlay(Best(), out _);
        Assert.Equal(7, run.ProblemCount);
        Assert.Equal(7, run.GoBack().ProblemCount);
        Assert.Equal(7, run.End().ProblemCount);
    }

    // ── PresentNext ─────────────────────────────────────────────────────────────

    [Fact]
    public void PresentNext_TheFirstProblem_LandsOnIt_UnresolvedAndLive()
    {
        var problem = PlayProblem(1);
        var run = Started(problem, PlayProblem(2));
        var presented = Assert.Single(run.Presented);
        Assert.Same(problem, presented.Problem);
        Assert.Same(presented, run.Cursor);
        Assert.Same(presented, run.Frontier);
        Assert.Same(ProblemDisposition.Unresolved, presented.Disposition);
        Assert.Equal(1, presented.StreamSlot);
        Assert.True(run.IsLive);
        Assert.True(run.IsAnswering);
        Assert.Null(run.Review);
        Assert.False(run.CanGoBack);
        Assert.False(run.CanGoToLast);
    }

    [Fact]
    public void PresentNext_AfterMovingOn_AppendsTheNextOfTheSequence_AndLandsOnIt()
    {
        var sequence = Plays(3);
        var run = MoveOn(Started(sequence).SubmitPlay(Best(), out _));
        Assert.Equal(2, run.Presented.Length);
        Assert.Same(sequence[1], run.Frontier!.Problem);
        Assert.Same(run.Frontier, run.Cursor);
        Assert.Equal(2, run.Cursor!.StreamSlot);
        Assert.Null(run.Review);
        Assert.True(run.CanGoBack);
    }

    [Fact]
    public void PresentNext_OverAnUnresolvedFrontier_DefersIt_UnresolvedBehindTheNewFrontier()
    {
        var sequence = Plays(2);
        var run = MoveOn(Started(sequence));
        Assert.Equal(2, run.Presented.Length);
        Assert.Same(sequence[0], run.Presented[0].Problem);
        Assert.Same(ProblemDisposition.Unresolved, run.Presented[0].Disposition);
        Assert.Same(sequence[1], run.Frontier!.Problem);
        Assert.Same(run.Frontier, run.Cursor);
        Assert.True(run.IsLive);
    }

    [Fact]
    public void PresentNext_WithTheCursorBehindTheFrontier_IsRefused()
    {
        var behind = ThreePresented().GoBack();
        Assert.Throws<InvalidOperationException>(() => behind.PresentNext(false));
    }

    [Fact]
    public void PresentNext_WhenTheSequenceIsExhausted_EndsTheRun()
    {
        var run = Started(Plays(1)).SubmitPlay(Best(), out _);
        Assert.False(run.HasMore);
        var ended = run.Next(out var brings).PresentNext(false);
        Assert.True(brings);
        Assert.True(ended.IsEnded);
        Assert.Null(ended.Cursor);
        Assert.Single(ended.Presented);
    }

    [Fact]
    public void TheSequenceRunningOut_AfterSkipOnTheLastProblem_FinishesWithItSkippedOfRecord()
    {
        var run = Started(Plays(1));
        var ended = run.Next(out _).PresentNext(false);
        Assert.True(ended.IsEnded);
        Assert.Equal([ProblemDispositionKind.Skipped], Kinds(ended));
        Assert.Equal(1, ended.SkippedCount);
    }

    // ── Submitting on the unresolved frontier ──────────────────────────────────

    [Fact]
    public void SubmitPlay_OnTheUnresolvedFrontier_IsTheAnswerOfRecord()
    {
        var run = Started(PlayProblem(1)).SubmitPlay(Alt(), out var made);
        var presented = Assert.Single(run.Presented);
        Assert.Equal(ProblemDispositionKind.Answered, presented.Disposition.Kind);
        Assert.NotNull(made);
        Assert.Same(made, AnswerOf(presented));
        var review = ScoredReview(run.Review);
        Assert.False(review.IsCorrect);
        Assert.False(run.Review!.IsPractice);
        Assert.False(run.IsAnswering);
        Assert.False(run.IsLive);
        Assert.Equal(1, run.Score.PlayDecisions.Submitted);
    }

    [Fact]
    public void SubmitPlay_OffList_OnTheUnresolvedFrontier_IsASkipOfRecord()
    {
        var run = Started(PlayProblem(1)).SubmitPlay(OffList(), out var made);
        Assert.Null(made);
        Assert.Same(ProblemDisposition.Skipped, Assert.Single(run.Presented).Disposition);
        var review = Assert.IsType<ProblemReview.Play>(run.Review);
        Assert.Equal(PlaySubmissionKind.OffList, review.Submission.Kind);
        Assert.Equal(1, run.SkippedCount);
        Assert.Equal(QuizScore.Empty, run.Score);
    }

    [Fact]
    public void SubmitPlay_APlayTheRankingDoesNotScore_IsASkipOfRecord_UnderDepthFirst_AndScoredUnderEquity()
    {
        var decision = TestFixtures.DepthSplitDecision();


        var depthFirst = QuizRun.Begin(PlayRanking.DepthFirst, [decision]).PresentNext(false).SubmitPlay(Best(), out var madeDepthFirst);
        Assert.Null(madeDepthFirst);
        Assert.Same(ProblemDisposition.Skipped, depthFirst.Cursor!.Disposition);
        Assert.Equal(PlaySubmissionKind.NotScored, Assert.IsType<ProblemReview.Play>(depthFirst.Review).Submission.Kind);

        var equity = QuizRun.Begin(PlayRanking.Equity, [decision]).PresentNext(false).SubmitPlay(Best(), out var madeEquity);
        Assert.NotNull(madeEquity);
        Assert.Equal(ProblemDispositionKind.Answered, equity.Cursor!.Disposition.Kind);
    }

    [Fact]
    public void SubmitCubeAnswer_OnTheUnresolvedFrontier_IsTheAnswerOfRecord()
    {
        var run = Started(CubeProblem(1)).SubmitCubeAnswer(CubeAnswer.DoubleTake, out var made);
        var presented = Assert.Single(run.Presented);
        Assert.NotNull(made);
        Assert.Same(made, AnswerOf(presented));
        var review = Assert.IsType<ProblemReview.Cube>(run.Review);
        Assert.Equal(CubeAnswer.DoubleTake, review.Submission.Answer);
        Assert.False(review.IsPractice);
        Assert.Equal(1, run.Score.Total.Submitted);
    }

    [Fact]
    public void Submit_OfTheWrongKind_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Started(CubeProblem(1)).SubmitPlay(Best(), out _));
        Assert.Throws<InvalidOperationException>(() => Started(PlayProblem(1)).SubmitCubeAnswer(CubeAnswer.NoDouble, out _));
    }

    [Fact]
    public void Submit_WhileAReviewIsShowing_OrWithNothingOnScreen_IsRefused()
    {
        var reviewing = Started(PlayProblem(1)).SubmitPlay(Best(), out _);
        Assert.Throws<InvalidOperationException>(() => reviewing.SubmitPlay(Alt(), out _));
        var nothing = QuizRun.Begin(PlayRanking.Equity, Plays(1));
        Assert.Throws<InvalidOperationException>(() => nothing.SubmitPlay(Best(), out _));
        Assert.Throws<InvalidOperationException>(() => nothing.SubmitCubeAnswer(CubeAnswer.NoDouble, out _));
    }

    // ── Practice ────────────────────────────────────────────────────────────────

    [Fact]
    public void SubmitPlay_OnAnAnsweredProblem_IsPractice_AndChangesNothing()
    {
        var answered = Started(PlayProblem(1), PlayProblem(2)).SubmitPlay(Alt(), out _);
        var back = MoveOn(answered, side: false).GoBack();
        Assert.True(back.IsAnswering);
        Assert.False(back.IsLive);

        var practised = back.SubmitPlay(Best(), out var made);

        Assert.Null(made);
        Assert.True(practised.Review!.IsPractice);
        Assert.True(ScoredReview(practised.Review).IsCorrect);
        AssertRecordUnchanged(back, practised);
        Assert.Same(AnswerOf(answered.Presented[0]), AnswerOf(practised.Presented[0]));
    }

    [Fact]
    public void SubmitPlay_OnASkippedProblem_IsPractice_AndTheSkipStands()
    {
        var skipped = Started(Plays(2)).SubmitPlay(OffList(), out _);
        var back = MoveOn(skipped).GoBack();
        var practised = back.SubmitPlay(Best(), out var made);
        Assert.Null(made);
        Assert.True(practised.Review!.IsPractice);
        Assert.Same(ProblemDisposition.Skipped, practised.Presented[0].Disposition);
        Assert.Equal(1, practised.SkippedCount);
    }

    [Fact]
    public void SubmitPlay_OffList_OnAnAnsweredProblem_AddsNoSkip()
    {
        var back = MoveOn(Started(Plays(2)).SubmitPlay(Alt(), out _)).GoBack();
        var practised = back.SubmitPlay(OffList(), out _);
        Assert.Equal(ProblemDispositionKind.Answered, practised.Presented[0].Disposition.Kind);
        Assert.Equal(0, practised.SkippedCount);
        Assert.Equal(PlaySubmissionKind.OffList, Assert.IsType<ProblemReview.Play>(practised.Review).Submission.Kind);
    }

    [Fact]
    public void SubmitCubeAnswer_OnACompletedProblem_IsPractice_AndChangesNothing()
    {
        var answered = Started(CubeProblem(1), PlayProblem(2)).SubmitCubeAnswer(CubeAnswer.NoDouble, out _);
        var back = MoveOn(answered).GoBack();
        var practised = back.SubmitCubeAnswer(CubeAnswer.DoublePass, out var made);
        Assert.Null(made);
        Assert.True(practised.Review!.IsPractice);
        AssertRecordUnchanged(back, practised);
    }

    [Fact]
    public void PracticeCycles_AreUnbounded_AndEquallyRecordless()
    {
        var run = MoveOn(Started(Plays(2)).SubmitPlay(Alt(), out _)).GoBack();
        var score = run.Score;
        for (var i = 0; i < 3; i++)
        {
            run = run.SubmitPlay(i % 2 == 0 ? Best() : Alt(), out var made);
            Assert.Null(made);
            Assert.True(run.Review!.IsPractice);
            run = run.Next(out _).GoBack();   // land on it again, decision showing
            Assert.True(run.IsAnswering);
            Assert.Equal(score, run.Score);
        }
    }

    // ── Deferred problems ───────────────────────────────────────────────────────

    [Fact]
    public void ADeferredProblem_AnsweredOnReturn_IsLive_AndBecomesAnswered()
    {
        var run = TwoDeferred().GoToFirst();
        Assert.True(run.IsLive);
        var answered = run.SubmitPlay(Best(), out var made);
        Assert.NotNull(made);
        Assert.False(answered.Review!.IsPractice);
        Assert.Equal(ProblemDispositionKind.Answered, answered.Presented[0].Disposition.Kind);
        Assert.Equal(1, answered.Score.PlayDecisions.Correct);
    }

    [Fact]
    public void ADeferredProblem_AnsweredOffTheList_BecomesASkipOfRecord_AndStaysCounted()
    {
        var run = TwoDeferred();
        Assert.Equal(2, run.SkippedCount);   // two deferred behind the frontier
        var skipped = run.GoToFirst().SubmitPlay(OffList(), out var made);
        Assert.Null(made);
        Assert.Same(ProblemDisposition.Skipped, skipped.Presented[0].Disposition);
        Assert.Equal(2, skipped.SkippedCount);
    }

    [Fact]
    public void ADeferredCubeProblem_AnsweredOnReturn_IsLive_AndBecomesAnswered()
    {
        var run = MoveOn(Started(CubeProblem(1), PlayProblem(2))).GoBack();
        var answered = run.SubmitCubeAnswer(CubeAnswer.NoDouble, out var made);
        Assert.NotNull(made);
        Assert.False(answered.Review!.IsPractice);
        Assert.Equal(ProblemDispositionKind.Answered, answered.Presented[0].Disposition.Kind);
    }

    // ── Next ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Next_OnADeferredProblem_GoesToTheNextPresentedProblem_AndChangesNothing()
    {
        var run = TwoDeferred().GoToFirst();
        var moved = run.Next(out var brings);
        Assert.False(brings);
        Assert.Same(run.Presented[1], moved.Cursor);
        AssertRecordUnchanged(run, moved);
    }

    [Fact]
    public void Next_OnTheUnresolvedFrontier_CompletesNothing_AndOwesANewProblem()
    {
        var run = Started(Plays(2));
        var moved = run.Next(out var brings);
        Assert.True(brings);
        Assert.Same(ProblemDisposition.Unresolved, moved.Frontier!.Disposition);
        Assert.Single(moved.Presented);
    }

    [Fact]
    public void Next_FromALiveReview_OwesANewProblem_AndTheRecordStands()
    {
        var reviewing = Started(Plays(2)).SubmitPlay(Alt(), out _);
        var moved = reviewing.Next(out var brings);
        Assert.True(brings);
        AssertRecordUnchanged(reviewing, moved);
        Assert.Null(moved.Review);
    }

    [Fact]
    public void Next_BehindTheFrontier_GoesToTheNextPresentedProblem_AndRecordsNothing()
    {
        var run = ThreePresented().GoToFirst();
        var moved = run.Next(out var brings);
        Assert.False(brings);
        Assert.Same(run.Presented[1], moved.Cursor);
        AssertRecordUnchanged(run, moved);
    }

    [Fact]
    public void Next_WithNothingOnScreen_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => QuizRun.Begin(PlayRanking.Equity, Plays(1)).Next(out _));
    }

    [Fact]
    public void NextAddsToSkipCount_HoldsExactlyWhereAPressAddsToTheCount()
    {
        Assert.False(QuizRun.Begin(PlayRanking.Equity, Plays(1)).NextAddsToSkipCount);
        var frontier = Started(Plays(3));
        Assert.True(frontier.NextAddsToSkipCount);
        Assert.False(frontier.SubmitPlay(Best(), out _).NextAddsToSkipCount);
        var behind = MoveOn(frontier).GoBack();
        Assert.False(behind.NextAddsToSkipCount);
    }

    // ── Navigation ──────────────────────────────────────────────────────────────

    [Fact]
    public void Navigation_LandsWhereItSays_AndIsRefusedWhereItIsUnavailable()
    {
        var run = ThreePresented();
        Assert.Same(run.Presented[1], run.GoBack().Cursor);
        Assert.Same(run.Presented[0], run.GoToFirst().Cursor);
        Assert.Same(run.Presented[2], run.GoBack().GoBack().GoToLast().Cursor);

        var first = run.GoToFirst();
        Assert.False(first.CanGoBack);
        Assert.Throws<InvalidOperationException>(() => first.GoBack());
        Assert.Throws<InvalidOperationException>(() => first.GoToFirst());
        Assert.False(run.CanGoToLast);
        Assert.Throws<InvalidOperationException>(() => run.GoToLast());

        var nothing = QuizRun.Begin(PlayRanking.Equity, Plays(1));
        Assert.False(nothing.CanGoBack);
        Assert.False(nothing.CanGoToLast);
        Assert.Throws<InvalidOperationException>(() => nothing.GoBack());
        Assert.Throws<InvalidOperationException>(() => nothing.GoToLast());
    }

    [Fact]
    public void Back_OnTheFirstProblemsReview_ReopensItsQuestion_WhereTheReanswerIsPractice()
    {
        var reviewing = Started(Plays(2)).SubmitPlay(Alt(), out _);
        Assert.True(reviewing.CanGoBack);

        var back = reviewing.GoBack();

        Assert.Same(reviewing.Presented[0], back.Cursor);
        Assert.Null(back.Review);
        Assert.True(back.IsAnswering);
        Assert.False(back.IsLive);
        Assert.False(back.CanGoBack);   // answering the first problem: nothing precedes it
        AssertRecordUnchanged(reviewing, back);

        var practised = back.SubmitPlay(Best(), out var made);
        Assert.Null(made);
        Assert.True(practised.Review!.IsPractice);
        AssertRecordUnchanged(reviewing, practised);
        Assert.Same(AnswerOf(reviewing.Presented[0]), AnswerOf(practised.Presented[0]));
    }

    [Fact]
    public void GoToFirst_OnTheFirstProblemsReview_EqualsBack()
    {
        var reviewing = Started(Plays(2)).SubmitPlay(Alt(), out _);
        var first = reviewing.GoToFirst();
        Assert.Same(reviewing.Presented[0], first.Cursor);
        Assert.Null(first.Review);
        Assert.True(first.IsAnswering);
    }

    [Fact]
    public void FromALaterProblemsReview_BackReopensThatProblem_AndGoToFirstLandsOnTheFirst()
    {
        var reviewing = ThreePresented().SubmitPlay(Best(), out _);
        Assert.Same(reviewing.Presented[2], reviewing.Cursor);

        var back = reviewing.GoBack();
        Assert.Same(reviewing.Presented[2], back.Cursor);
        Assert.Null(back.Review);

        var first = reviewing.GoToFirst();
        Assert.Same(reviewing.Presented[0], first.Cursor);
        Assert.Null(first.Review);
        AssertRecordUnchanged(reviewing, first);
    }

    [Fact]
    public void OnAOneProblemRun_TheProblemCanBeReanswered_AsPractice()
    {
        var reviewing = Started(CubeProblem(1)).SubmitCubeAnswer(CubeAnswer.NoDouble, out var ofRecord);
        Assert.NotNull(ofRecord);
        Assert.False(reviewing.HasMore);
        Assert.True(reviewing.CanGoBack);

        var practised = reviewing.GoBack().SubmitCubeAnswer(CubeAnswer.DoublePass, out var made);

        Assert.Null(made);
        Assert.True(practised.Review!.IsPractice);
        AssertRecordUnchanged(reviewing, practised);
        var ended = practised.Next(out var brings).PresentNext(false);
        Assert.True(brings);
        Assert.Equal(reviewing.Score, ended.ToResult().Score);
    }

    [Fact]
    public void Navigation_LeavesTheRecordStanding_AndNeverPresentsAProblem()
    {
        var run = ThreePresented().GoBack().SubmitPlay(Best(), out _);   // a practice review on the skipped one
        foreach (var landed in new[] { run.GoBack(), run.GoToFirst(), run.GoToLast(), run.Next(out _) })
        {
            AssertRecordUnchanged(run, landed);
            Assert.Equal(run.Presented.Length, landed.Presented.Length);
            Assert.Null(landed.Review);
        }
    }

    [Fact]
    public void EveryLanding_ShowsTheDecision_NeverAReview()
    {
        var staged = ThreePresented();
        var reviewing = staged.SubmitPlay(Best(), out _);
        Assert.NotNull(reviewing.Review);
        Assert.Null(reviewing.GoBack().Review);
        Assert.Null(reviewing.GoToFirst().Review);
        // Behind the frontier by ⏮: from a review, ◀ reopens the problem on screen (2026-10-25, #365).
        Assert.Null(reviewing.GoToFirst().GoToLast().Review);
        Assert.Null(MoveOn(reviewing).Review);
        Assert.Null(staged.GoBack().SubmitPlay(Best(), out _).Next(out _).Review);
    }

    [Fact]
    public void ReturningToAProblem_LandsOnItsDecision_NotOnItsEarlierReview()
    {
        var run = MoveOn(Started(Plays(2)).SubmitPlay(Alt(), out _));
        var back = run.GoBack();
        Assert.Null(back.Review);
        Assert.True(back.IsAnswering);
        Assert.Equal(ProblemDispositionKind.Answered, back.Cursor!.Disposition.Kind);
    }

    // ── End ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void End_ConvertsEveryUnresolvedProblem_WhereverTheCursorIs_AndLeavesTheCompletedAsTheyWere()
    {
        var run = ThreePresented(out _, out _, out _);
        var fromTheFrontier = run.End();
        Assert.Equal([ProblemDispositionKind.Answered, ProblemDispositionKind.Skipped, ProblemDispositionKind.Skipped], Kinds(fromTheFrontier));
        var fromTheFirst = run.GoToFirst().End();
        Assert.Equal(Kinds(fromTheFrontier), Kinds(fromTheFirst));
        Assert.Same(run.Presented[0].Disposition, fromTheFirst.Presented[0].Disposition);
        Assert.True(fromTheFirst.IsEnded);
        Assert.Null(fromTheFirst.Cursor);
        Assert.Null(fromTheFirst.Review);
    }

    [Fact]
    public void End_WithEveryProblemCompleted_AddsNothing_AndBeforeAnythingPresented_EndsAnEmptyRun()
    {
        var completed = Started(PlayProblem(1)).SubmitPlay(Best(), out _).End();
        Assert.Equal([ProblemDispositionKind.Answered], Kinds(completed));
        Assert.Equal(0, completed.SkippedCount);

        var empty = QuizRun.Begin(PlayRanking.Equity, Plays(2)).End();
        Assert.True(empty.IsEnded);
        Assert.Empty(empty.Presented);
        Assert.Equal(0, empty.ToResult().PresentedCount);
        Assert.Equal(2, empty.ToResult().ProblemCount);
    }

    public enum Transition { PresentNext, SubmitPlay, SubmitCubeAnswer, Next, GoToFirst, GoBack, GoToLast, End }

    [Theory]
    [InlineData(Transition.PresentNext)]
    [InlineData(Transition.SubmitPlay)]
    [InlineData(Transition.SubmitCubeAnswer)]
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
            Transition.PresentNext => ended.PresentNext(false),
            Transition.SubmitPlay => ended.SubmitPlay(Best(), out _),
            Transition.SubmitCubeAnswer => ended.SubmitCubeAnswer(CubeAnswer.DoubleTake, out _),
            Transition.Next => ended.Next(out _),
            Transition.GoToFirst => ended.GoToFirst(),
            Transition.GoBack => ended.GoBack(),
            Transition.GoToLast => ended.GoToLast(),
            Transition.End => ended.End(),
            _ => throw new ArgumentOutOfRangeException(nameof(transition)),
        });
    }

    // ── Results and totals ──────────────────────────────────────────────────────

    [Fact]
    public void ToResult_OfAnEndedRun_IsItsTotals_ReadByItsOwnRules()
    {
        var run = QuizRun.Begin(PlayRanking.DepthFirst, Plays(5)).PresentNext(false).SubmitPlay(Alt(), out _);
        run = MoveOn(MoveOn(run));
        var ended = run.End();
        var result = ended.ToResult();
        Assert.Equal(PlayRanking.DepthFirst, result.Ranking);
        Assert.Equal(ended.Score, result.Score);
        Assert.Equal(ended.SkippedCount, result.SkippedCount);
        Assert.Equal(3, result.PresentedCount);
        Assert.Equal(5, result.ProblemCount);
    }

    [Fact]
    public void ToResult_OfARunStillActive_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ThreePresented().ToResult());
        Assert.Throws<InvalidOperationException>(() => QuizRun.Begin(PlayRanking.Equity, Plays(1)).ToResult());
    }

    [Fact]
    public void Score_IsTheAnswersOfRecord_FoldedInPresentedOrder()
    {
        var run = Started(Plays(3)).SubmitPlay(Alt(), out _);
        run = MoveOn(run).SubmitPlay(Best(), out _);
        run = MoveOn(run).SubmitPlay(OffList(), out _);
        var score = run.Score;
        Assert.Equal(2, score.PlayDecisions.Submitted);
        Assert.Equal(1, score.PlayDecisions.Correct);
        Assert.Equal(1, run.SkippedCount);
    }

    [Fact]
    public void SkippedCount_IsTheSkipsOfRecord_PlusTheUnresolvedProblemsBehindTheFrontier()
    {
        var run = Started(Plays(5));
        Assert.Equal(0, run.SkippedCount);                  // the unresolved frontier is not counted
        run = run.Next(out _);
        Assert.Equal(0, run.SkippedCount);                  // Next pressed, nothing landed: still the frontier
        run = run.PresentNext(false);
        Assert.Equal(1, run.SkippedCount);                  // the first is now deferred, behind the frontier
        run = run.SubmitPlay(OffList(), out _);
        Assert.Equal(2, run.SkippedCount);                  // a skip of record beside it
        run = MoveOn(run).SubmitPlay(Best(), out _);
        Assert.Equal(2, run.SkippedCount);                  // an answer adds nothing
        run = MoveOn(run);
        Assert.Equal(2, run.SkippedCount);                  // nor does the new unresolved frontier
        run = run.GoToFirst();
        Assert.Equal(2, run.SkippedCount);                  // moving the cursor changes nothing
        run = run.SubmitPlay(Best(), out _);
        Assert.Equal(1, run.SkippedCount);                  // answering the deferred one takes it off
        Assert.Equal(2, run.End().SkippedCount);            // ending skips the frontier
    }

    [Fact]
    public void Totals_NeverMoveForPractice()
    {
        var run = MoveOn(Started(Plays(2)).SubmitPlay(Alt(), out _)).GoBack();
        var practised = run.SubmitPlay(Best(), out _).Next(out _).GoBack().SubmitPlay(OffList(), out _);
        Assert.Equal(run.Score, practised.Score);
        Assert.Equal(run.SkippedCount, practised.SkippedCount);
    }

    // ── What each presentation keeps ────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheRoll_IsTheOneHandedInWhenTheProblemWasPresented_AndStaysOnEveryReturn(bool side)
    {
        var run = QuizRun.Begin(PlayRanking.Equity, Plays(3)).PresentNext(side).SubmitPlay(Best(), out _);
        Assert.Equal(side, run.Cursor!.RandomHomeBoardOnRight);
        run = MoveOn(run, side: !side);
        run = MoveOn(run, side: side);
        Assert.Equal(side, run.GoToFirst().Cursor!.RandomHomeBoardOnRight);
        Assert.Equal(!side, run.GoToFirst().Next(out _).Cursor!.RandomHomeBoardOnRight);
        Assert.Equal(side, run.Cursor!.RandomHomeBoardOnRight);
    }

    [Fact]
    public void StreamSlot_IsThePositionInTheSequence_AndTheNumberOnScreenIsTheCursors()
    {
        var run = MoveOn(MoveOn(Started(Plays(4))));
        Assert.Equal([1, 2, 3], run.Presented.Select(problem => problem.StreamSlot));
        Assert.Equal(3, run.Cursor!.StreamSlot);
        Assert.Equal(1, run.GoToFirst().Cursor!.StreamSlot);
        Assert.Equal(2, run.GoBack().Cursor!.StreamSlot);
    }

    [Fact]
    public void ATransition_ReturnsTheRunThatFollows_AndLeavesThisOneAsItWas()
    {
        var before = Started(Plays(2));
        var after = before.SubmitPlay(Best(), out _);
        Assert.NotSame(before, after);
        Assert.True(before.IsAnswering);
        Assert.Null(before.Review);
        Assert.Same(ProblemDisposition.Unresolved, before.Cursor!.Disposition);
        Assert.NotNull(after.Review);
    }

    // ── The small types ─────────────────────────────────────────────────────────

    [Fact]
    public void AnswerOfRecord_HandsBackTheSubmissionItWasMadeFrom_AndRefusesNulls()
    {
        var play = TestFixtures.Scored(PlayProblem(1), Best(), PlayRanking.Equity);
        Assert.Same(play, AnswerOfRecord.Of(play).Match(play: p => p, cube: _ => null!));
        var cube = SubmittedCubeAnswer.Score(CubeAnswer.NoDouble, CubeProblem(1));
        Assert.Same(cube, AnswerOfRecord.Of(cube).Match(play: _ => null!, cube: c => c));
        Assert.Throws<ArgumentNullException>(() => AnswerOfRecord.Of((SubmittedPlay)null!));
        Assert.Throws<ArgumentNullException>(() => AnswerOfRecord.Of((SubmittedCubeAnswer)null!));
    }

    [Fact]
    public void ProblemDisposition_HasExactlyThreeStates_AndAnsweredNeedsAnAnswer()
    {
        Assert.Equal(
            [ProblemDispositionKind.Unresolved, ProblemDispositionKind.Answered, ProblemDispositionKind.Skipped],
            Enum.GetValues<ProblemDispositionKind>());
        var answer = AnswerOfRecord.Of(TestFixtures.Scored(PlayProblem(1), Best(), PlayRanking.Equity));
        var answered = ProblemDisposition.Answered(answer);
        Assert.False(ProblemDisposition.Unresolved.IsCompleted);
        Assert.False(ProblemDisposition.Unresolved.TryGetAnswer(out _));
        Assert.True(ProblemDisposition.Skipped.IsCompleted);
        Assert.False(ProblemDisposition.Skipped.TryGetAnswer(out _));
        Assert.True(answered.IsCompleted);
        Assert.True(answered.TryGetAnswer(out var held));
        Assert.Same(answer, held);
        Assert.Throws<ArgumentNullException>(() => ProblemDisposition.Answered(null!));
    }

    [Fact]
    public void PresentedProblem_RefusesWhatNoRunWouldBuild()
    {
        Assert.Throws<ArgumentNullException>(() => new PresentedProblem(null!, 1, false, ProblemDisposition.Unresolved));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PresentedProblem(PlayProblem(1), 0, false, ProblemDisposition.Unresolved));
        Assert.Throws<ArgumentNullException>(() => new PresentedProblem(PlayProblem(1), 1, false, null!));
    }
}
