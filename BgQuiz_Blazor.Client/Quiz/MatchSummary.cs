namespace BgQuiz_Blazor.Client.Quiz;

using BgGame_Lib;

/// <summary>
/// What <see cref="QuizController.SummarizeMatchesAsync"/> reports about a
/// filter config: the pool it admits, decomposed by answer type, together with
/// how many duplicate records the pool's dedupe layer collapsed on the way to
/// that number.
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
    /// enumeration.
    /// </summary>
    /// <param name="answerTypes">The matching pool, bucketed by answer type.</param>
    /// <param name="duplicatesCollapsed">
    /// How many matching records were dropped as duplicates of a position
    /// already in <paramref name="answerTypes"/>. Zero when nothing collapsed.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="answerTypes"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duplicatesCollapsed"/> is negative.</exception>
    internal MatchSummary(AnswerTypeDistribution answerTypes, int duplicatesCollapsed)
    {
        ArgumentNullException.ThrowIfNull(answerTypes);
        ArgumentOutOfRangeException.ThrowIfNegative(duplicatesCollapsed);
        AnswerTypes = answerTypes;
        DuplicatesCollapsed = duplicatesCollapsed;
    }

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
