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
///   what counts for it. Only a live submission and the run finishing write
///   one; ▶ and the other moves never do, so a problem the user moves on from
///   unanswered stays unresolved, and several can be unresolved at once.
///   Whether a submission is live or practice (<see cref="IsLive"/>) and the
///   running totals (<see cref="Score"/>, <see cref="SkippedCount"/>) are
///   derived from the dispositions on every read and stored nowhere, so
///   neither can drift from the record and a practice submission cannot move
///   them.</item>
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
/// problem or another ranking, or a cube answer with another decision.
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
    /// is not here. Any number of its problems may be unresolved while the run
    /// is active — the frontier, and every earlier problem the user moved on
    /// from without completing; none is once the run has ended
    /// (<see cref="End"/>).
    /// </summary>
    public ImmutableArray<PresentedProblem> Presented => _presented;

    /// <summary>
    /// The cursor — the problem on screen — or null when none is: before the
    /// first problem is presented, and once the run has ended.
    /// </summary>
    public PresentedProblem? Cursor => _cursor == NoCursor ? null : _presented[_cursor];

    /// <summary>
    /// The frontier — the furthest problem presented — or null before the
    /// first. It means "furthest", nothing more (SPEC-quiz-history.md §1): it
    /// need not be the problem on screen, it need not be unresolved, and it is
    /// not the only problem that can be. It changes only when a new problem is
    /// presented.
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
    /// out. Every presented problem is then completed — a finished run holds no
    /// unresolved problem (SPEC-quiz-history.md §4) — and nothing is on screen;
    /// the record and the totals stand for the summary to read.
    /// </summary>
    public bool IsEnded { get; }

    // -----------------------------------------------------------------------
    //  What is derived from it
    // -----------------------------------------------------------------------

    /// <summary>
    /// True when the problem on screen is unresolved, so a submission here is
    /// <i>live</i> — of record. That is the frontier while it is unresolved,
    /// and equally any earlier problem the user deferred and has come back to.
    /// On a completed problem, answered or skipped, a submission is practice
    /// (SPEC-quiz-history.md §3). Read off the cursor's disposition on every
    /// call: there is no live/practice flag to fall out of step with it, and
    /// where the problem sits in the sequence does not enter into it.
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
    /// True when ▶ pressed here would add to <see cref="SkippedCount"/> — the
    /// fact ▶'s name follows: it is called Skip exactly where this holds, and
    /// Next everywhere else (SPEC-quiz-history.md §2), so the icon never hides
    /// a counted skip.
    ///
    /// <para>
    /// That is the cursor on the frontier while the frontier is unresolved.
    /// ▶ there defers the problem, and the count takes it in once a later
    /// problem is presented — or, if the source has none, once the run's
    /// finishing converts it (§5). Anywhere else ▶ adds nothing: behind the
    /// frontier it moves the cursor and a deferred problem there is already
    /// counted; on a completed problem, which every review is of, there is
    /// nothing left to skip. Derived on every read, like the count itself.
    /// </para>
    /// </summary>
    public bool NextAddsToSkipCount =>
        _cursor != NoCursor && _cursor == _presented.Length - 1 && !_presented[_cursor].Disposition.IsCompleted;

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
    /// The session's skip count: the problems completed as a skip of record,
    /// plus the unresolved problems behind the frontier
    /// (SPEC-quiz-history.md §5). Derived from the dispositions and the
    /// sequence on each read, as <see cref="Score"/> is; nothing is stored to
    /// compute it. A position passed over silently was never presented and is
    /// not counted.
    ///
    /// <para>
    /// <b>A deferred problem is a provisional skip.</b> It counts from the
    /// moment a later problem is presented, which is what puts it behind the
    /// frontier, and for as long as it stays unresolved. Answered live later,
    /// it stops counting here and its answer reaches <see cref="Score"/>
    /// instead, so the problem is counted once. Left unresolved until the run
    /// ends, it is converted to a skip of record and goes on counting.
    /// </para>
    ///
    /// <para>
    /// <b>The unresolved frontier is not counted</b>, and that includes the
    /// moment after ▶ has been pressed on it and before the next problem
    /// arrives: nothing records that a press happened, so until a problem is
    /// presented beyond it the frontier is simply a problem not yet answered.
    /// </para>
    /// </summary>
    public int SkippedCount
    {
        get
        {
            var skipped = 0;
            var frontier = _presented.Length - 1;
            for (var i = 0; i < _presented.Length; i++)
            {
                var kind = _presented[i].Disposition.Kind;
                if (kind == ProblemDispositionKind.Skipped
                    || (kind == ProblemDispositionKind.Unresolved && i < frontier))
                {
                    skipped++;
                }
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
    /// presentation, or with the cursor on the frontier — where
    /// <see cref="Next"/> leaves it. With the cursor on an earlier problem it
    /// would show an unseen problem early, and is refused.
    /// </para>
    ///
    /// <para>
    /// <b>It completes nothing.</b> The frontier it moves on from keeps the
    /// disposition it has. If that is unresolved, the problem is now deferred:
    /// still unresolved, behind the new frontier, open to a live answer if the
    /// user comes back to it — and, from this moment, counted in
    /// <see cref="SkippedCount"/> until it is answered.
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
    /// The run has ended, or the cursor is not on the frontier.
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
    /// (SPEC-quiz-history.md §3, SPEC-scoring.md §2). On an unresolved problem
    /// — the frontier, or an earlier one the user deferred — a scored play
    /// becomes the answer of record, and a play the ranking does not score, or
    /// one that is no candidate, completes the problem as a skip of record
    /// (SPEC-scoring.md §2a), because its review shows the solution. On a
    /// completed problem the submission is scored and reviewed the same way,
    /// the review is marked as practice
    /// (<see cref="ProblemReview.IsPractice"/>), and the disposition — and so
    /// the totals — stand untouched. Which it is was read off the disposition,
    /// before anything was written.
    /// </para>
    ///
    /// <para>
    /// <b>It says what it made of record</b>, through
    /// <paramref name="answerOfRecord"/>, so the caller that folds answers into
    /// the lifetime record folds exactly what this submission recorded and
    /// decides nothing itself — not whether the submission was live, and not
    /// what the review shows (SPEC-scoring.md §2: the answer of record folds at
    /// the first submission itself).
    /// </para>
    /// </summary>
    /// <param name="play">The play the user submitted, as entered.</param>
    /// <param name="answerOfRecord">
    /// The answer of record this submission made — the very instance the
    /// problem's disposition now holds — or null when it made none: a practice
    /// submission, which records nothing, and a live play that completes the
    /// problem as a skip of record, which carries no answer.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The run is not in the answering state, or the problem on screen is a
    /// cube decision, which is answered with <see cref="SubmitCubeAnswer"/>.
    /// </exception>
    public QuizRun SubmitPlay(Play play, out AnswerOfRecord? answerOfRecord)
    {
        var answering = RequireAnswering();
        if (answering.Problem is not CheckerPlayDecision decision)
            throw new InvalidOperationException(
                "The current problem is a cube decision; answer it with SubmitCubeAnswer.");

        var practice = answering.Disposition.IsCompleted;
        var outcome = PlaySubmission.Score(play, decision, Ranking);
        var review = new ProblemReview.Play(outcome, play) { IsPractice = practice };
        answerOfRecord = null;
        if (practice) return WithReview(review);

        if (!outcome.TryGetScored(out var submitted))
            return WithCursorCompleted(ProblemDisposition.Skipped, review);

        answerOfRecord = AnswerOfRecord.Of(submitted);
        return WithCursorCompleted(ProblemDisposition.Answered(answerOfRecord), review);
    }

    /// <summary>
    /// Score the cube <paramref name="answer"/> — one of the four — at the cube
    /// decision on screen and show its review. The cursor does not move. Live
    /// or practice exactly as <see cref="SubmitPlay"/> describes, and it says
    /// what it made of record the same way; every cube answer is scored, so a
    /// live one is always an answer of record and never a skip.
    ///
    /// <para>
    /// <b>Scoring is the producer's, in one call</b>
    /// (<see cref="SubmittedCubeAnswer.Score"/>, made by the review it is
    /// shown in, <see cref="ProblemReview.Cube"/>): the problem's key, the
    /// truth and the answer's cost are all read off the decision on screen,
    /// and whether the answer is correct is derived from that cost
    /// (SPEC-scoring.md §3). This method only files the outcome — the
    /// review's own <see cref="ProblemReview.Cube.Submission"/>, so the answer
    /// of record and the review on screen are one scored answer.
    /// </para>
    /// </summary>
    /// <param name="answer">The cube answer the user submitted.</param>
    /// <param name="answerOfRecord">
    /// The answer of record this submission made — the very instance the
    /// problem's disposition now holds — or null for a practice submission,
    /// which records nothing.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The run is not in the answering state, or the problem on screen is a
    /// checker-play decision, which is answered with <see cref="SubmitPlay"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="answer"/> is not one of the four answers.
    /// </exception>
    public QuizRun SubmitCubeAnswer(CubeAnswer answer, out AnswerOfRecord? answerOfRecord)
    {
        var answering = RequireAnswering();
        if (answering.Problem is not CubeDecision decision)
            throw new InvalidOperationException(
                "The current problem is a checker-play decision; answer it with SubmitPlay.");

        var practice = answering.Disposition.IsCompleted;
        var review = new ProblemReview.Cube(answer, decision) { IsPractice = practice };
        if (practice)
        {
            answerOfRecord = null;
            return WithReview(review);
        }

        answerOfRecord = AnswerOfRecord.Of(review.Submission);
        return WithCursorCompleted(ProblemDisposition.Answered(answerOfRecord), review);
    }

    // -----------------------------------------------------------------------
    //  Move (SPEC-quiz-history.md §2, §4)
    // -----------------------------------------------------------------------

    /// <summary>
    /// ▶ — "the next problem", from either view state. <b>It completes
    /// nothing</b>, wherever it is pressed (SPEC-quiz-history.md §1): the
    /// problem it leaves keeps the disposition it has, and an unresolved one
    /// stays unresolved.
    ///
    /// <para>
    /// <b>Behind the frontier</b> the cursor moves to the next presented
    /// problem. <b>On the frontier</b> the user is moving on to a problem not
    /// yet presented: the review, if one is showing, is discarded, and
    /// <paramref name="bringsNewProblem"/> tells the caller that it now owes
    /// the run either the next problem (<see cref="Present"/>) or, if the
    /// source has none, the end (<see cref="End"/>). Until one of them arrives
    /// the frontier stays the frontier and stays on screen — nothing in the run
    /// says a press happened. An unresolved frontier left this way is
    /// <i>deferred</i> once <see cref="Present"/> puts a problem beyond it;
    /// if the run ends instead, ending converts it.
    /// </para>
    /// </summary>
    /// <param name="bringsNewProblem">
    /// True when the press was on the frontier, so a new problem is owed;
    /// false when the cursor moved within the presented sequence.
    /// </param>
    /// <exception cref="InvalidOperationException">No problem is on screen.</exception>
    public QuizRun Next(out bool bringsNewProblem)
    {
        RequireCursor();
        bringsNewProblem = _cursor == _presented.Length - 1;
        return WithCursor(bringsNewProblem ? _cursor : _cursor + 1);
    }

    /// <summary>
    /// ⏮ — land on the first problem presented. Leaves every disposition as it
    /// is, the unresolved ones included.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no earlier problem (<see cref="CanGoBack"/>).</exception>
    public QuizRun GoToFirst()
    {
        RequireEarlierProblem();
        return WithCursor(0);
    }

    /// <summary>
    /// ◀ — land on the problem before the cursor. Leaves every disposition as
    /// it is, the unresolved ones included.
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
    /// Finish the run — however it finishes: the End quiz gesture, or the
    /// orchestration finding that the source has no next problem to bring.
    ///
    /// <para>
    /// <b>A finished run contains no unresolved problem</b>
    /// (SPEC-quiz-history.md §4). Every problem still unresolved — the
    /// frontier, and any the user deferred, wherever the cursor happens to be —
    /// becomes a skip of record; completed problems get nothing added. The
    /// conversion is this transition's, not any one caller's, so both ways of
    /// finishing leave every presented problem accounted for. Nothing is on
    /// screen afterwards.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The run has already ended.</exception>
    public QuizRun End()
    {
        RefuseWhenEnded();
        var presented = _presented;
        for (var i = 0; i < presented.Length; i++)
        {
            if (!presented[i].Disposition.IsCompleted)
                presented = presented.SetItem(i, presented[i].With(ProblemDisposition.Skipped));
        }
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
    /// The run with the problem on screen completed as
    /// <paramref name="disposition"/>, its <paramref name="review"/> showing —
    /// what a live submission comes to. With <see cref="End"/>, one of the two
    /// places a disposition is written, and both write only over an unresolved
    /// problem, so nothing of record is ever rewritten.
    /// </summary>
    private QuizRun WithCursorCompleted(ProblemDisposition disposition, ProblemReview review) =>
        new(
            Ranking, ProblemCount,
            _presented.SetItem(_cursor, _presented[_cursor].With(disposition)),
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
