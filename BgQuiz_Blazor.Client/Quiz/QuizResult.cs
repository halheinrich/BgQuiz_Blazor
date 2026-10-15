namespace BgQuiz_Blazor.Client.Quiz;

using BgDataTypes_Lib;
using BgGame_Lib;

/// <summary>
/// What a finished run keeps of itself: its result, and nothing heavy
/// (<c>SPEC-quiz-history.md</c> §9, "A finished run holds its result and its
/// replay, nothing heavy"). The ranking it was scored under, its score, its
/// skip count and how many problems it presented — the summary Done and
/// Stats read — and the stream's total once it was established (§5: the run
/// owns M and it stays stable), with no problem, decision, review or record
/// behind them.
///
/// <para>
/// <b>One representation.</b> While a run is active it is a
/// <see cref="QuizRun"/>, and its totals are derived from the dispositions on
/// every read (§7). When it finishes — by End quiz, by the source running
/// out, or by a new sample — the controller condenses the ended run into
/// this (<see cref="QuizRun.ToResult"/>) and lets the run go, so the
/// presented decisions it held are released with it
/// (halheinrich/backgammon#407 measured that retention as what stopped a
/// second parse fitting). Nothing here can be derived again or changed: the
/// totals are the run's own rules, read once at the end, when no further
/// transition can move them.
/// </para>
///
/// <para>
/// What a Restart replays — the filters and the mix — and the source's name
/// and composition telemetry are not here: they are what the run held beyond
/// itself (§8), and the controller keeps them beside this, its stream and
/// source released.
/// </para>
/// </summary>
internal sealed class QuizResult
{
    /// <summary>
    /// The result a finished run condenses to. <see cref="QuizRun.ToResult"/>
    /// is the one caller: the totals are the run's to derive.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ranking"/> is not a defined ranking; a count is
    /// negative; or more problems were skipped than were presented.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="score"/> is null.</exception>
    internal QuizResult(PlayRanking ranking, QuizScore score, int skippedCount, int presentedCount, int? problemCount)
    {
        if (!Enum.IsDefined(ranking))
            throw new ArgumentOutOfRangeException(nameof(ranking), ranking, "Not a defined play ranking.");
        ArgumentNullException.ThrowIfNull(score);
        ArgumentOutOfRangeException.ThrowIfNegative(presentedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(skippedCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(skippedCount, presentedCount);
        if (problemCount is { } total) ArgumentOutOfRangeException.ThrowIfNegative(total, nameof(problemCount));
        Ranking = ranking;
        Score = score;
        SkippedCount = skippedCount;
        PresentedCount = presentedCount;
        ProblemCount = problemCount;
    }

    /// <summary>The ranking the run was scored under (SPEC-scoring.md §2a).</summary>
    public PlayRanking Ranking { get; }

    /// <summary>The session score: the run's answers of record.</summary>
    public QuizScore Score { get; }

    /// <summary>The skips of record — every problem the run finished with unanswered included (SPEC-quiz-history.md §4, §5).</summary>
    public int SkippedCount { get; }

    /// <summary>How many problems the run presented — Done's "problems shown".</summary>
    public int PresentedCount { get; }

    /// <summary>
    /// The M of "Problem N of M" the run established, if it established one
    /// (<see cref="QuizRun.ProblemCount"/>): owned by the run and stable for
    /// it (SPEC-quiz-history.md §5), so a finished run keeps it.
    /// </summary>
    public int? ProblemCount { get; }
}
