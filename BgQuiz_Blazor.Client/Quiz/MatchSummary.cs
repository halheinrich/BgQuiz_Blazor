namespace BgQuiz_Blazor.Client.Quiz;

using BgGame_Lib;
using XgFilter_Lib;

/// <summary>
/// What <see cref="QuizController.SummarizeMatchesAsync"/> reports about a
/// filter config: the pool it admits, decomposed by answer type, together with
/// how many duplicate records the pool's dedupe layer collapsed on the way to
/// that number, and the source report of the parse the pool was drawn from —
/// which picked files it attempted, and which it could not read.
///
/// <para>
/// <b>Why the magnitude travels with the distribution.</b> The count on screen
/// is a count of distinct positions, and a user comparing it to their file
/// count does the subtraction and reads the difference as a bug
/// (halheinrich/backgammon#104). The collapse magnitude is what answers that,
/// and it is only meaningful about the very enumeration the distribution was
/// folded from — a magnitude paired with a different pass would be a number
/// describing something the reader is not looking at. One value, one
/// enumeration, so the two halves cannot disagree.
/// </para>
///
/// <para>
/// <b>Why the source report travels with it too</b>
/// (halheinrich/backgammon#368). A count over a selection with a rejected file
/// in it is a count over an incomplete selection, and "0 decisions match" over
/// a selection whose every file was refused is not a filter outcome at all. The
/// summary therefore carries the <see cref="SourceReport"/> of the parse the
/// stack counted over — a reference to the very report the parse-once cache
/// retains with its decisions, never a copy — so what the count says and what
/// the parse found are read off one object. It is always a completed walk's
/// (the constructor refuses an incomplete one), and it compares by identity:
/// one walk, one report, so two summaries over one parse carry the same one.
/// </para>
///
/// <para>
/// <b>The count itself is not repeated here.</b> The pool's size is
/// <see cref="AnswerTypeDistribution.Total"/> and stays there: the producer's
/// fold contract already makes it fall out of the classification pass, and a
/// forwarding <c>Total</c> on this type would put a second spelling of the same
/// number in front of every caller (§ "there is no second surface for it").
/// </para>
/// </summary>
internal sealed record MatchSummary
{
    /// <summary>
    /// Pair a folded distribution with the collapse magnitude of the same
    /// enumeration and the source report of the parse it drew from.
    /// </summary>
    /// <param name="answerTypes">The matching pool, bucketed by answer type.</param>
    /// <param name="duplicatesCollapsed">
    /// How many matching records were dropped as duplicates of a position
    /// already in <paramref name="answerTypes"/>. Zero when nothing collapsed.
    /// </param>
    /// <param name="sources">
    /// The completed report of the parse the pool was drawn from — see
    /// <see cref="Sources"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="answerTypes"/> or <paramref name="sources"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duplicatesCollapsed"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="sources"/> is not complete — a walk that stopped early counted nothing.</exception>
    internal MatchSummary(AnswerTypeDistribution answerTypes, int duplicatesCollapsed, SourceReport sources)
    {
        ArgumentNullException.ThrowIfNull(answerTypes);
        ArgumentOutOfRangeException.ThrowIfNegative(duplicatesCollapsed);
        ArgumentNullException.ThrowIfNull(sources);
        if (!sources.IsComplete)
        {
            throw new ArgumentException(
                "A summary describes a completed walk: a report whose walk stopped early or threw " +
                "cannot say what the pool was drawn from.",
                nameof(sources));
        }

        AnswerTypes = answerTypes;
        DuplicatesCollapsed = duplicatesCollapsed;
        Sources = sources;
    }

    /// <summary>
    /// What the parse behind this count did with the picked files: how many it
    /// attempted and read, each file it rejected by the name the picker gave
    /// it with the read's own exception, and whether every file was rejected
    /// (<see cref="SourceReport.AllRejected"/>, drawn only from a completed
    /// walk). The same object the parse-once cache retains with the decisions
    /// it counted — a re-count or a Start over the same pick carries this very
    /// report, since nothing walks the files again.
    /// </summary>
    internal SourceReport Sources { get; }

    /// <summary>
    /// The matching pool bucketed by the kind of answer each decision calls for.
    /// Its <see cref="AnswerTypeDistribution.Total"/> is the match count.
    /// </summary>
    internal AnswerTypeDistribution AnswerTypes { get; }

    /// <summary>
    /// How many further matching records repeated a position already counted in
    /// <see cref="AnswerTypes"/> and were therefore left out of it — the
    /// difference between what the filters admitted and what the pool holds.
    /// <c>0</c> means nothing collapsed, which is a result and not an absence.
    /// </summary>
    internal int DuplicatesCollapsed { get; }
}
