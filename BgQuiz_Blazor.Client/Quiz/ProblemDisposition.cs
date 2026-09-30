namespace BgQuiz_Blazor.Client.Quiz;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// The three states a presented problem can be in — exactly three
/// (SPEC-quiz-history.md §1, amended 2026-09-30).
/// </summary>
internal enum ProblemDispositionKind
{
    /// <summary>
    /// Nothing of record yet. Only a run's frontier can be here, and it is not
    /// the same as "on screen": the cursor may sit on an earlier problem while
    /// the frontier stays unresolved.
    /// </summary>
    Unresolved = 1,

    /// <summary>Completed with an answer of record (SPEC-scoring.md §2).</summary>
    Answered = 2,

    /// <summary>Completed as a skip of record.</summary>
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
/// <b>A skip carries no cause and no submission.</b> The Skip button, an
/// off-list play, a play the ranking does not score (SPEC-scoring.md §2a) and
/// End quiz on an unresolved frontier all complete a problem the same way, so
/// there is one <see cref="Skipped"/> and it holds nothing. If a later
/// requirement needs the cause or the play, that requirement is ruled first
/// (SPEC-quiz-history.md §1).
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

    /// <summary>Nothing of record yet — the state every problem is presented in.</summary>
    public static ProblemDisposition Unresolved { get; } = new(ProblemDispositionKind.Unresolved, null);

    /// <summary>Completed as a skip of record, whatever brought it about.</summary>
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
