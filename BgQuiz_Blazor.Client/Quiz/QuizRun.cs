namespace BgQuiz_Blazor.Client.Quiz;

using System.Collections.Immutable;
using BgDataTypes_Lib;
using BgGame_Lib;

/// <summary>
/// One run of the quiz, as pure state: the problems presented so far, where the
/// user is among them, what is of record for each, and the ranking they are all
/// scored under. <b>The model is <c>SPEC-quiz-history.md</c></b> (ratified
/// 2026-09-24, amended 2026-09-30, halheinrich/backgammon#8), with
/// <c>SPEC-scoring.md</c> §2 and §2a for what counts and how a play is scored —
/// read the rules there; this type is where they are enforced, and its members
/// say which rule each one is.
///
/// <para>
/// <b>What it owns.</b>
/// <list type="bullet">
///   <item>The <i>presented sequence</i> (<see cref="Presented"/>), the
///   <i>cursor</i> — the problem on screen (<see cref="Cursor"/>) — and the
///   <i>frontier</i>, the furthest problem presented (<see cref="Frontier"/>).</item>
///   <item>Each problem's <see cref="ProblemDisposition"/>, the one record of
///   what counts for it. Whether a submission is live or practice
///   (<see cref="IsLive"/>) and the running totals (<see cref="Score"/>,
///   <see cref="SkippedCount"/>) are derived from the dispositions on every
///   read and stored nowhere, so neither can drift from the record and a
///   practice submission cannot move them.</item>
///   <item>The run's <see cref="Ranking"/>, fixed when the run begins — which is
///   what makes "one quiz, one ranking" (SPEC-scoring.md §2a) a fact of the
///   type rather than an agreement between its callers.</item>
///   <item>The review on screen (<see cref="Review"/>), which belongs to the
///   cursor's stay on a problem and goes when the cursor leaves it.</item>
///   <item>Per problem, its stream slot and the board side it was given
///   (<see cref="PresentedProblem"/>), and for the run its total when one is
///   known (<see cref="ProblemCount"/>).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>What it does not own.</b> It never reads the problem source, the stats
/// sink or the page, and it has no clock and no randomness: whoever orchestrates
/// the run draws the problems, decides which are shown, takes each board's
/// roll, learns the total, and folds answers into the lifetime record — and
/// hands the run what it needs to know (<see cref="Present"/>,
/// <see cref="WithProblemCount"/>). Scoring <i>is</i> here, because it is a pure
/// function of three things the run holds — the problem on screen, the ranking,
/// and the answer submitted — so no caller can pair a play with another
/// problem or another ranking.
/// </para>
///
/// <para>
/// <b>Immutable.</b> Every transition returns the run that follows and leaves
/// this one as it was, so a reference taken before a transition still describes
/// the run as it then stood, a transition that is refused or throws changes
/// nothing, and the holder of the current run — the controller — replaces it
/// whole.
/// </para>
///
/// <para>
/// <b>A transition the state does not allow throws.</b> Submitting while a
/// review is showing, going back from the first problem, presenting a problem
/// the run did not move on to: each is
/// <see cref="InvalidOperationException"/>, a caller bug rather than an
/// outcome. The gestures a user can repeat or mistime are the controller's to
/// no-op, which it does by reading the state first (<see cref="IsAnswering"/>,
/// <see cref="Review"/>, <see cref="CanGoBack"/>, <see cref="CanGoToLast"/>,
/// <see cref="IsEnded"/>).
/// </para>
/// </summary>
internal sealed class QuizRun
{
    /// <summary>The cursor's value when no problem is on screen.</summary>
    private const int NoCursor = -1;

    private readonly ImmutableArray<PresentedProblem> _presented;

    /// <summary>
    /// Index into <see cref="_presented"/> of the problem on screen, or
    /// <see cref="NoCursor"/> before the first problem is presented and after
    /// the run has ended.
    /// </summary>
    private readonly int _cursor;

    private QuizRun(
        PlayRanking ranking, int? problemCount, ImmutableArray<PresentedProblem> presented,
        int cursor, ProblemReview? review, bool isEnded)
    {
        Ranking = ranking;
        ProblemCount = problemCount;
        _presented = presented;
        _cursor = cursor;
        Review = review;
        IsEnded = isEnded;
    }

    /// <summary>
    /// Begin a run under <paramref name="ranking"/>: nothing presented yet, no
    /// total known. The first <see cref="Present"/> puts its first problem on
    /// screen.
    /// </summary>
    /// <param name="ranking">
    /// The ranking the whole run is scored under, read from the user's setting
    /// by whoever starts the run (SPEC-quiz-history.md §7).
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ranking"/> is not a defined ranking.</exception>
    public static QuizRun Begin(PlayRanking ranking)
    {
        if (!Enum.IsDefined(ranking))
            throw new ArgumentOutOfRangeException(nameof(ranking), ranking, "Not a defined play ranking.");
        return new QuizRun(ranking, problemCount: null, presented: [], NoCursor, review: null, isEnded: false);
    }

    // -----------------------------------------------------------------------
    //  What the run holds
    // -----------------------------------------------------------------------

    /// <summary>
    /// The run's ranking, the same for its whole life: which play is best, each
    /// play's error and whether a play is scored at all are read under it
    /// (SPEC-scoring.md §2a). No transition changes it; a different ranking is
    /// a different run.
    /// </summary>
    public PlayRanking Ranking { get; }

    /// <summary>
    /// The M of "Problem N of M": how many slots the run's problem stream
    /// holds, or null while that is not known — a valid state, in which a page
    /// shows N alone (SPEC-quiz-history.md §5). The run never asks the source;
    /// it is told (<see cref="WithProblemCount"/>), and once told it keeps the
    /// number.
    /// </summary>
    public int? ProblemCount { get; }

    /// <summary>
    /// The presented sequence: every problem shown to the user in this run, in
    /// the order shown. A position passed over silently was never shown, so it
    /// is not here. Only the last entry, the frontier, can be unresolved.
    /// </summary>
    public ImmutableArray<PresentedProblem> Presented => _presented;

    /// <summary>
    /// The cursor — the problem on screen — or null when none is: before the
    /// first problem is presented, and once the run has ended.
    /// </summary>
    public PresentedProblem? Cursor => _cursor == NoCursor ? null : _presented[_cursor];

    /// <summary>
    /// The frontier — the furthest problem presented — or null before the
    /// first. It is the only problem that can be unresolved, and it need not be
    /// the one on screen.
    /// </summary>
    public PresentedProblem? Frontier => _presented.IsEmpty ? null : _presented[^1];

    /// <summary>
    /// The scored outcome of the last submission against <see cref="Cursor"/>,
    /// while the user is still looking at it; null in the answering state and
    /// whenever nothing is on screen. It is <i>displayed</i>, not of record
    /// (<see cref="ProblemReview"/>): what counts is the cursor's
    /// <see cref="PresentedProblem.Disposition"/>. Every transition that moves
    /// the cursor, or moves on from it, discards it, so a landing always shows
    /// the decision and never an earlier review (SPEC-quiz-history.md §3, §5).
    /// </summary>
    public ProblemReview? Review { get; }

    /// <summary>
    /// True once the run is over — ended by the user or by the source running
    /// out. Every presented problem is then completed and nothing is on screen;
    /// the record and the totals stand for the summary to read.
    /// </summary>
    public bool IsEnded { get; }

    // -----------------------------------------------------------------------
    //  What is derived from it
    // -----------------------------------------------------------------------

    /// <summary>
    /// True when the problem on screen is the unresolved frontier — the one
    /// place a submission is <i>live</i>, and the one place ▶ records a skip.
    /// On any completed problem, answered or skipped, a submission is practice
    /// (SPEC-quiz-history.md §3). Read off the cursor's disposition on every
    /// call: there is no live/practice flag to fall out of step with it.
    /// </summary>
    public bool IsLive => Cursor is { Disposition.IsCompleted: false };

    /// <summary>
    /// True in the answering state: a problem is on screen and no review of it
    /// is showing, so a submission is accepted.
    /// </summary>
    public bool IsAnswering => Cursor is not null && Review is null;

    /// <summary>True when there is an earlier problem to go back to — what ⏮ and ◀ need.</summary>
    public bool CanGoBack => _cursor > 0;

    /// <summary>True when the cursor is behind the frontier — what ⏭ needs.</summary>
    public bool CanGoToLast => _cursor != NoCursor && _cursor < _presented.Length - 1;

    /// <summary>
    /// The session score: every answer of record, folded in the order its
    /// problem was presented. Derived from the dispositions on each read
    /// (SPEC-quiz-history.md §1, §7), so it is exactly what the record says —
    /// a practice submission, which writes no disposition, cannot reach it.
    /// </summary>
    public QuizScore Score
    {
        get
        {
            var score = QuizScore.Empty;
            foreach (var problem in _presented)
            {
                if (problem.Disposition.TryGetAnswer(out var answer))
                    score = answer.Match(play: score.Plus, cube: score.Plus);
            }
            return score;
        }
    }

    /// <summary>
    /// How many presented problems were completed as a skip of record — the
    /// Skip gesture, an off-list play, a play the ranking does not score, or a
    /// problem left unresolved when the run ended. Derived from the
    /// dispositions on each read, as <see cref="Score"/> is. A position passed
    /// over silently was never presented and is not counted.
    /// </summary>
    public int SkippedCount
    {
        get
        {
            var skipped = 0;
            foreach (var problem in _presented)
            {
                if (problem.Disposition.Kind == ProblemDispositionKind.Skipped)
                    skipped++;
            }
            return skipped;
        }
    }

    // -----------------------------------------------------------------------
    //  What the orchestration tells it
    // -----------------------------------------------------------------------

    /// <summary>
    /// Record the run's total, <paramref name="problemCount"/> stream slots —
    /// called when, and if, the problem source establishes one: at once for a
    /// source that declares its size, after the first draw for one that learns
    /// it by composing (SPEC-quiz-history.md §5). Stating the number the run
    /// already holds changes nothing.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="problemCount"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The run already holds a different total: once established it is stable
    /// for the run.
    /// </exception>
    public QuizRun WithProblemCount(int problemCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(problemCount);
        if (ProblemCount == problemCount) return this;
        if (ProblemCount is { } established)
            throw new InvalidOperationException(
                $"The run's total is already established as {established}; it cannot become {problemCount}.");
        return new QuizRun(Ranking, problemCount, _presented, _cursor, Review, IsEnded);
    }

    /// <summary>
    /// Present <paramref name="problem"/>: it joins the sequence as the new
    /// frontier, unresolved, and the cursor lands on it in the answering state.
    ///
    /// <para>
    /// <b>Only moving on at the frontier presents a new problem</b>
    /// (SPEC-quiz-history.md §1). So this is accepted as the run's first
    /// presentation, or with the cursor on a completed frontier — where
    /// <see cref="Next"/> leaves it, having recorded the skip if there was one
    /// to record. With the cursor on an earlier problem it would show an unseen
    /// problem early, and over an unresolved frontier it would leave a second
    /// unresolved problem behind; both are refused.
    /// </para>
    /// </summary>
    /// <param name="problem">The decision to show.</param>
    /// <param name="silentlySkippedBefore">
    /// How many stream slots were consumed without being shown since the
    /// previous presentation (or since the run began). The new problem's
    /// <see cref="PresentedProblem.StreamSlot"/> follows from it and the
    /// previous frontier's, so the numbering is derived from the sequence and
    /// no counter is kept beside it.
    /// </param>
    /// <param name="randomHomeBoardOnRight">
    /// The board side rolled for this problem, which the run keeps for its
    /// whole life (<see cref="PresentedProblem.RandomHomeBoardOnRight"/>).
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="problem"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="silentlySkippedBefore"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The run has ended, the cursor is not on the frontier, or the frontier is
    /// unresolved.
    /// </exception>
    public QuizRun Present(BgDecisionData problem, int silentlySkippedBefore, bool randomHomeBoardOnRight)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentOutOfRangeException.ThrowIfNegative(silentlySkippedBefore);
        RefuseWhenEnded();

        var previousSlot = 0;
        if (Frontier is { } frontier)
        {
            if (_cursor != _presented.Length - 1)
                throw new InvalidOperationException(
                    "A new problem is presented only by moving on at the frontier; the cursor is on an earlier problem.");
            if (!frontier.Disposition.IsCompleted)
                throw new InvalidOperationException(
                    "The frontier is unresolved: move on from it first, so that it is completed before a new problem is presented.");
            previousSlot = frontier.StreamSlot;
        }

        var presented = new PresentedProblem(
            problem, previousSlot + silentlySkippedBefore + 1, randomHomeBoardOnRight, ProblemDisposition.Unresolved);
        return new QuizRun(
            Ranking, ProblemCount, _presented.Add(presented), cursor: _presented.Length, review: null, isEnded: false);
    }

    // -----------------------------------------------------------------------
    //  Submit
    // -----------------------------------------------------------------------

    /// <summary>
    /// Score <paramref name="play"/> against the checker play on screen, under
    /// the run's ranking, and show its review. The cursor does not move.
    ///
    /// <para>
    /// <b>Scoring is the producer's, in one call</b>
    /// (<see cref="PlaySubmission.Score"/>): the candidate the play is — found
    /// by identity from the decision's own position, as
    /// <see cref="BoardState.IsSamePlay"/> states it — and that candidate's
    /// error under <see cref="Ranking"/> come out together. This method only
    /// files the outcome.
    /// </para>
    ///
    /// <para>
    /// <b>Live, it writes the record; practice, it writes nothing</b>
    /// (SPEC-quiz-history.md §3, SPEC-scoring.md §2). On the unresolved
    /// frontier a scored play becomes the answer of record, and a play the
    /// ranking does not score, or one that is no candidate, completes the
    /// problem as a skip of record (SPEC-scoring.md §2a). On a completed
    /// problem the submission is scored and reviewed the same way, the review
    /// is marked as practice (<see cref="ProblemReview.IsPractice"/>), and the
    /// disposition — and so the totals — stand untouched. Which it is was read
    /// off the disposition, before anything was written.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The run is not in the answering state, or the problem on screen is a
    /// cube decision, which is answered with <see cref="SubmitCubeAction"/>.
    /// </exception>
    public QuizRun SubmitPlay(Play play)
    {
        var answering = RequireAnswering();
        if (answering.Problem is not CheckerPlayDecision decision)
            throw new InvalidOperationException(
                "The current problem is a cube decision; answer it with SubmitCubeAction.");

        var practice = answering.Disposition.IsCompleted;
        var outcome = PlaySubmission.Score(play, decision, Ranking);
        var review = new ProblemReview.Play(outcome, play) { IsPractice = practice };
        if (practice) return WithReview(review);

        return WithFrontierCompleted(
            outcome.TryGetScored(out var submitted)
                ? ProblemDisposition.Answered(AnswerOfRecord.Of(submitted))
                : ProblemDisposition.Skipped,
            review);
    }

    /// <summary>
    /// Score the cube <paramref name="answer"/> against the cube decision on
    /// screen and show its review. The cursor does not move. Live or practice
    /// exactly as <see cref="SubmitPlay"/> describes; a cube answer is always a
    /// complete, scorable pair, so a live one is always an answer of record and
    /// never a skip.
    ///
    /// <para>
    /// <b>Scoring is the producer's, through its one factory</b>
    /// (<see cref="SubmittedCubeAction.From"/>): the position's derived truth
    /// and both per-half losses are read off the one decision together, and
    /// the record derives per-half correctness from the two pairs
    /// (SPEC-scoring.md §3). The key is the record's own
    /// (<see cref="ProblemKey.From"/>), which every record has; the factory
    /// takes it from its caller (halheinrich/backgammon#285), so it is derived
    /// here.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The run is not in the answering state, or the problem on screen is a
    /// checker-play decision, which is answered with <see cref="SubmitPlay"/>.
    /// </exception>
    public QuizRun SubmitCubeAction(CubeClaimPair answer)
    {
        var answering = RequireAnswering();
        if (answering.Problem is not CubeDecision decision)
            throw new InvalidOperationException(
                "The current problem is a checker-play decision; answer it with SubmitPlay.");

        var practice = answering.Disposition.IsCompleted;
        var submitted = SubmittedCubeAction.From(ProblemKey.From(decision), answer, decision.Decision);
        var review = new ProblemReview.Cube(submitted) { IsPractice = practice };
        return practice
            ? WithReview(review)
            : WithFrontierCompleted(ProblemDisposition.Answered(AnswerOfRecord.Of(submitted)), review);
    }

    /// <summary>
    /// Leave the review and return to the decision on the same problem — the
    /// Redo gesture (SPEC-scoring.md §2). Nothing of record changes: the
    /// problem is completed, so the submission that follows is practice.
    ///
    /// <para>
    /// The navigation model has no such step — there, returning to a problem is
    /// how it is practised — and <c>SPEC-quiz-history.md</c> §2 retires the
    /// Redo button with the leg that adds the navigation controls; this
    /// transition goes with it.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">No review is showing.</exception>
    public QuizRun Redo()
    {
        if (Review is null)
            throw new InvalidOperationException("No review is showing, so there is nothing to redo.");
        return WithCursor(_cursor);
    }

    // -----------------------------------------------------------------------
    //  Move (SPEC-quiz-history.md §2, §4)
    // -----------------------------------------------------------------------

    /// <summary>
    /// ▶ — "the next problem", from either view state.
    ///
    /// <para>
    /// <b>Behind the frontier</b> the cursor moves to the next presented
    /// problem and nothing is recorded. <b>On the frontier</b> the user is
    /// moving on to a problem not yet presented: an unresolved frontier is
    /// completed as a skip of record, the review is discarded, and
    /// <paramref name="bringsNewProblem"/> tells the caller that it now owes
    /// the run either the next problem (<see cref="Present"/>) or, if the
    /// source has none, the end (<see cref="End"/>). Until one of them arrives
    /// the completed frontier stays on screen.
    /// </para>
    /// </summary>
    /// <param name="bringsNewProblem">
    /// True when the press was on the frontier, so a new problem is owed;
    /// false when the cursor moved within the presented sequence.
    /// </param>
    /// <exception cref="InvalidOperationException">No problem is on screen.</exception>
    public QuizRun Next(out bool bringsNewProblem)
    {
        var from = RequireCursor();
        bringsNewProblem = _cursor == _presented.Length - 1;
        if (!bringsNewProblem) return WithCursor(_cursor + 1);

        return from.Disposition.IsCompleted
            ? WithCursor(_cursor)
            : WithFrontierCompleted(ProblemDisposition.Skipped, review: null);
    }

    /// <summary>
    /// ⏮ — land on the first problem presented. Leaves whatever is of record
    /// as it is, an unresolved frontier included.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no earlier problem (<see cref="CanGoBack"/>).</exception>
    public QuizRun GoToFirst()
    {
        RequireEarlierProblem();
        return WithCursor(0);
    }

    /// <summary>
    /// ◀ — land on the problem before the cursor. Leaves whatever is of record
    /// as it is, an unresolved frontier included.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no earlier problem (<see cref="CanGoBack"/>).</exception>
    public QuizRun GoBack()
    {
        RequireEarlierProblem();
        return WithCursor(_cursor - 1);
    }

    /// <summary>⏭ — land on the frontier. Never presents a problem: the frontier is already in the sequence.</summary>
    /// <exception cref="InvalidOperationException">The cursor is already on the frontier, or nothing is on screen (<see cref="CanGoToLast"/>).</exception>
    public QuizRun GoToLast()
    {
        if (!CanGoToLast)
            throw new InvalidOperationException("The cursor is not behind the frontier, so there is no later problem to go to.");
        return WithCursor(_presented.Length - 1);
    }

    /// <summary>
    /// End the run — the End quiz gesture, and what the orchestration does when
    /// the source has no next problem to bring.
    ///
    /// <para>
    /// <b>It acts on the frontier, not the cursor</b> (SPEC-quiz-history.md
    /// §4): an unresolved frontier becomes a skip of record, wherever the user
    /// happens to be looking, and a completed frontier gets nothing added. So
    /// every problem presented is accounted for in the ended run. Nothing is on
    /// screen afterwards.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The run has already ended.</exception>
    public QuizRun End()
    {
        RefuseWhenEnded();
        var presented = Frontier is { Disposition.IsCompleted: false } frontier
            ? _presented.SetItem(_presented.Length - 1, frontier.With(ProblemDisposition.Skipped))
            : _presented;
        return new QuizRun(Ranking, ProblemCount, presented, NoCursor, review: null, isEnded: true);
    }

    // -----------------------------------------------------------------------
    //  Internal
    // -----------------------------------------------------------------------

    /// <summary>
    /// The run with the cursor on <paramref name="index"/> — a landing, which
    /// always shows the decision: whatever review was on screen is discarded
    /// (SPEC-quiz-history.md §3).
    /// </summary>
    private QuizRun WithCursor(int index) =>
        new(Ranking, ProblemCount, _presented, index, review: null, IsEnded);

    /// <summary>The run showing <paramref name="review"/> over the cursor's problem, the record as it was.</summary>
    private QuizRun WithReview(ProblemReview review) =>
        new(Ranking, ProblemCount, _presented, _cursor, review, IsEnded);

    /// <summary>
    /// The run with its frontier completed as <paramref name="disposition"/> —
    /// the one place a disposition is ever written, and only ever over an
    /// unresolved frontier, so nothing of record is rewritten.
    /// </summary>
    private QuizRun WithFrontierCompleted(ProblemDisposition disposition, ProblemReview? review) =>
        new(
            Ranking, ProblemCount,
            _presented.SetItem(_presented.Length - 1, _presented[^1].With(disposition)),
            _cursor, review, IsEnded);

    private void RefuseWhenEnded()
    {
        if (IsEnded)
            throw new InvalidOperationException("The run has ended.");
    }

    private PresentedProblem RequireCursor() =>
        Cursor ?? throw new InvalidOperationException("No problem is on screen.");

    private PresentedProblem RequireAnswering()
    {
        var cursor = RequireCursor();
        if (Review is not null)
            throw new InvalidOperationException(
                "A review is showing; a problem is answered from its decision, not from its review.");
        return cursor;
    }

    private void RequireEarlierProblem()
    {
        if (!CanGoBack)
            throw new InvalidOperationException("There is no earlier problem to go back to.");
    }
}
