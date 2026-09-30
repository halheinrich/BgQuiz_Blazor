namespace BgQuiz_Blazor.Client.Quiz;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// The three states a presented problem can be in — exactly three
/// (SPEC-quiz-history.md §1, amended 2026-09-30).
/// </summary>
internal enum ProblemDispositionKind
{
    /// <summary>
    /// No answer of record, and no solution has been exposed. It is not the
    /// same as "on screen", and it is not confined to a run's frontier: a
    /// problem the user moved on from without answering stays here, behind the
    /// frontier, until it is answered or the run finishes.
    /// </summary>
    Unresolved = 1,

    /// <summary>Completed with an answer of record (SPEC-scoring.md §2).</summary>
    Answered = 2,

    /// <summary>
    /// Completed as a skip of record: its solution was shown without an answer
    /// that scores, or the run finished with it unresolved.
    /// </summary>
    Skipped = 3,
}

/// <summary>
/// What has been recorded against one presented problem: nothing yet, an
/// answer of record, or a skip of record (SPEC-quiz-history.md §1). <b>It is
/// the single source</b> for everything the run says about the problem's
/// record — whether a submission there is live or practice, and the running
/// totals, are read off it and stored nowhere beside it.
///
/// <para>
/// <b>What decides is whether the problem has been completed and its solution
/// exposed</b>, not whether the user once pressed a control called Skip. The
/// Skip gesture completes nothing: it leaves the problem
/// <see cref="Unresolved"/>, and a submission made on returning to it is live.
/// A problem becomes <see cref="Skipped"/> in two ways only — a submission
/// whose review shows the solution without scoring (an off-list play, or a play
/// the ranking does not score, SPEC-scoring.md §2a), and the run finishing
/// while the problem is still unresolved.
/// </para>
///
/// <para>
/// <b>A skip carries no cause and no submission.</b> Both ways complete a
/// problem alike, so there is one <see cref="Skipped"/> and it holds nothing;
/// nor is there a "solution seen" mark, which is what being completed already
/// says. If a later requirement needs the cause or the play, that requirement
/// is ruled first (SPEC-quiz-history.md §1).
/// </para>
///
/// <para>
/// <b>Reference-equal.</b> Nothing compares dispositions — the run reads
/// <see cref="Kind"/> and the answer — so this type defines no equality;
/// the two states that carry nothing are single instances.
/// </para>
/// </summary>
internal sealed class ProblemDisposition
{
    private readonly AnswerOfRecord? _answer;

    private ProblemDisposition(ProblemDispositionKind kind, AnswerOfRecord? answer)
    {
        Kind = kind;
        _answer = answer;
    }

    /// <summary>
    /// Nothing of record yet and no solution exposed — the state every problem
    /// is presented in, and stays in until it is answered or the run finishes.
    /// </summary>
    public static ProblemDisposition Unresolved { get; } = new(ProblemDispositionKind.Unresolved, null);

    /// <summary>Completed as a skip of record, whichever of the two ways brought it about.</summary>
    public static ProblemDisposition Skipped { get; } = new(ProblemDispositionKind.Skipped, null);

    /// <summary>Completed with <paramref name="answer"/> as the answer of record.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="answer"/> is null.</exception>
    public static ProblemDisposition Answered(AnswerOfRecord answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return new ProblemDisposition(ProblemDispositionKind.Answered, answer);
    }

    /// <summary>Which of the three states this is.</summary>
    public ProblemDispositionKind Kind { get; }

    /// <summary>
    /// True once the problem holds something of record, an answer or a skip:
    /// from then on a submission against it is practice, and nothing can
    /// change what it holds.
    /// </summary>
    public bool IsCompleted => Kind != ProblemDispositionKind.Unresolved;

    /// <summary>
    /// Yields the answer of record when there is one — exactly when
    /// <see cref="Kind"/> is <see cref="ProblemDispositionKind.Answered"/>.
    /// </summary>
    /// <param name="answer">The answer of record, or null for an unresolved or skipped problem.</param>
    /// <returns>True exactly for an answered problem.</returns>
    public bool TryGetAnswer([NotNullWhen(true)] out AnswerOfRecord? answer)
    {
        answer = _answer;
        return answer is not null;
    }
}
