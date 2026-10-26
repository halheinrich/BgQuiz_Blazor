namespace BgQuiz_Blazor.Client.Quiz;

using System.Collections.Immutable;
using BgDataTypes_Lib;
using BgGame_Lib;

/// <summary>
/// One run of a quiz over a prepared sequence of problems: what has been presented, where
/// the cursor is, each problem's disposition, and the review on screen. Immutable; every
/// operation returns the run that results. The sequence is fixed at <see cref="Begin"/>, so
/// <see cref="ProblemCount"/> is known throughout.
/// </summary>
internal sealed class QuizRun
{
    private const int NoCursor = -1;

    private readonly IReadOnlyList<BgDecisionData> _sequence;
    private readonly ImmutableArray<PresentedProblem> _presented;
    private readonly int _cursor;

    private QuizRun(
        PlayRanking ranking, IReadOnlyList<BgDecisionData> sequence, ImmutableArray<PresentedProblem> presented,
        int cursor, ProblemReview? review, bool isEnded)
    {
        Ranking = ranking;
        _sequence = sequence;
        _presented = presented;
        _cursor = cursor;
        Review = review;
        IsEnded = isEnded;
    }

    /// <summary>A run over <paramref name="sequence"/>, nothing presented yet.</summary>
    public static QuizRun Begin(PlayRanking ranking, IReadOnlyList<BgDecisionData> sequence)
    {
        if (!Enum.IsDefined(ranking))
            throw new ArgumentOutOfRangeException(nameof(ranking), ranking, "Not a defined play ranking.");
        ArgumentNullException.ThrowIfNull(sequence);
        return new QuizRun(ranking, sequence, presented: [], NoCursor, review: null, isEnded: false);
    }

    public PlayRanking Ranking { get; }

    /// <summary>How many problems the run holds in all.</summary>
    public int ProblemCount => _sequence.Count;

    public ImmutableArray<PresentedProblem> Presented => _presented;
    public PresentedProblem? Cursor => _cursor == NoCursor ? null : _presented[_cursor];
    public PresentedProblem? Frontier => _presented.IsEmpty ? null : _presented[^1];
    public ProblemReview? Review { get; }
    public bool IsEnded { get; }

    /// <summary>Whether the sequence holds a problem not yet presented.</summary>
    public bool HasMore => _presented.Length < _sequence.Count;

    public bool IsLive => Cursor is { Disposition.IsCompleted: false };
    public bool IsAnswering => Cursor is not null && Review is null;
    /// <summary>
    /// Whether ◀ and ⏮ have somewhere to go: a problem is on screen and either an earlier
    /// problem exists or a review is showing, whose own question ◀ reopens (SPEC-quiz-history §2).
    /// </summary>
    public bool CanGoBack => Cursor is not null && (_cursor > 0 || Review is not null);

    public bool CanGoToLast => _cursor != NoCursor && _cursor < _presented.Length - 1;

    /// <summary>Whether Next would leave the problem on screen unanswered: it is at the frontier and not completed.</summary>
    public bool NextAddsToSkipCount =>
        _cursor != NoCursor && _cursor == _presented.Length - 1 && !_presented[_cursor].Disposition.IsCompleted;

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

    /// <summary>
    /// Present the next problem of the sequence at the frontier, or end the run when none
    /// remains. Only valid with the cursor at the frontier (or nothing presented yet).
    /// </summary>
    public QuizRun PresentNext(bool randomHomeBoardOnRight)
    {
        RefuseWhenEnded();
        if (Frontier is not null && _cursor != _presented.Length - 1)
        {
            throw new InvalidOperationException(
                "A new problem is presented only by moving on at the frontier; the cursor is on an earlier problem.");
        }

        if (!HasMore) return End();
        var presented = new PresentedProblem(
            _sequence[_presented.Length], _presented.Length + 1, randomHomeBoardOnRight, ProblemDisposition.Unresolved);
        return new QuizRun(
            Ranking, _sequence, _presented.Add(presented), cursor: _presented.Length, review: null, isEnded: false);
    }

    public QuizRun SubmitPlay(Play play, out AnswerOfRecord? answerOfRecord)
    {
        var answering = RequireAnswering();
        if (answering.Problem is not CheckerPlayDecision decision)
        {
            throw new InvalidOperationException(
                "The current problem is a cube decision; answer it with SubmitCubeAnswer.");
        }

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

    public QuizRun SubmitCubeAnswer(CubeAnswer answer, out AnswerOfRecord? answerOfRecord)
    {
        var answering = RequireAnswering();
        if (answering.Problem is not CubeDecision decision)
        {
            throw new InvalidOperationException(
                "The current problem is a checker-play decision; answer it with SubmitPlay.");
        }

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

    /// <summary>Move on: to the next presented problem, or, at the frontier, towards a new one (<paramref name="bringsNewProblem"/>).</summary>
    public QuizRun Next(out bool bringsNewProblem)
    {
        RequireCursor();
        bringsNewProblem = _cursor == _presented.Length - 1;
        return WithCursor(bringsNewProblem ? _cursor : _cursor + 1);
    }

    /// <summary>Land on the first problem's decision; from the first problem's own review, that reopens its question.</summary>
    public QuizRun GoToFirst()
    {
        RequireBackTarget();
        return WithCursor(0);
    }

    /// <summary>
    /// From a review, reopen the same problem's decision, where a completed problem is answered
    /// again as practice; while answering, land on the previous problem.
    /// </summary>
    public QuizRun GoBack()
    {
        RequireBackTarget();
        return WithCursor(Review is not null ? _cursor : _cursor - 1);
    }

    public QuizRun GoToLast()
    {
        if (!CanGoToLast)
            throw new InvalidOperationException("The cursor is not behind the frontier, so there is no later problem to go to.");
        return WithCursor(_presented.Length - 1);
    }

    /// <summary>End the run: every unresolved problem becomes a skip.</summary>
    public QuizRun End()
    {
        RefuseWhenEnded();
        var presented = _presented;
        for (var i = 0; i < presented.Length; i++)
        {
            if (!presented[i].Disposition.IsCompleted)
                presented = presented.SetItem(i, presented[i].With(ProblemDisposition.Skipped));
        }

        return new QuizRun(Ranking, _sequence, presented, NoCursor, review: null, isEnded: true);
    }

    public QuizResult ToResult() =>
        IsEnded
            ? new QuizResult(Ranking, Score, SkippedCount, _presented.Length, ProblemCount)
            : throw new InvalidOperationException("Only a run that has ended has a result.");

    private QuizRun WithCursor(int index) =>
        new(Ranking, _sequence, _presented, index, review: null, IsEnded);

    private QuizRun WithReview(ProblemReview review) =>
        new(Ranking, _sequence, _presented, _cursor, review, IsEnded);

    private QuizRun WithCursorCompleted(ProblemDisposition disposition, ProblemReview review) =>
        new(
            Ranking, _sequence,
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
        {
            throw new InvalidOperationException(
                "A review is showing; a problem is answered from its decision, not from its review.");
        }

        return cursor;
    }

    private void RequireBackTarget()
    {
        if (!CanGoBack)
        {
            throw new InvalidOperationException(
                "There is nothing to go back to: no problem is on screen, or the first problem is being answered.");
        }
    }
}
