namespace BgQuiz_Blazor.Client.Quiz;

using BackgammonDiagram_Lib;
using BgDataTypes_Lib;
using BgGame_Lib;

/// <summary>
/// The one home for the presentation of BgGame_Lib's
/// <see cref="AnswerTypeDistribution"/> — the answer-type breakdown Home shows
/// beside the pre-Start match count: which rows there are, in what order, and
/// where each row's name comes from.
///
/// <para>
/// <b>The split of ownership.</b> Which bucket a decision lands in is the
/// producer's rule and is never re-derived here, and neither is what a cube
/// bucket is <i>called</i>: the four cube rows are named by
/// <see cref="CubeLabels.Label(CubeClaimPair)"/>, the one home for the
/// spelling of a cube answer (halheinrich/backgammon#185), read off the
/// canonical pair each row counts. Only "Checker plays" is this class's own
/// word, having no cube answer to name it. So this type maps the producer's
/// five fields onto five labels and nothing else — it classifies nothing,
/// computes nothing, and spells almost nothing.
/// </para>
///
/// <para>
/// <b>Every bucket, every time — including the zeros.</b> The breakdown exists
/// to answer "what is my collection actually made of?", and a category sitting
/// at zero is the most informative reading it can give (the beta report behind
/// it: a collection suspected of being all takes). Dropping empty buckets would
/// delete exactly the signal, so <see cref="Buckets"/> always returns all five,
/// in a fixed order, and callers render what they are given.
/// </para>
///
/// <para>
/// Order mirrors the producer record's own declaration order: checker plays
/// first, then the four reachable cube verdicts of SPEC-scoring §3 as amended
/// 2026-09-02 (halheinrich/backgammon#187) as the producer declares them —
/// (NoDouble, Take), the two doubles, then (TooGood, Pass). It is the
/// producer's ordering, so there is no second convention to keep in step. The
/// fifth row of the halheinrich/backgammon#86 era, (TooGood, Take), is retired
/// with its verdict (Too Good requires the pass; a position the opponent would
/// take is a no-double by ruling, and counts in the first cube row).
/// </para>
///
/// <para>
/// <see cref="AnswerTypeDistribution.Total"/> is deliberately <b>not</b> a
/// bucket: it is the match count, which Home's count line already renders in
/// its own words. Adding it here would put the same number on screen twice with
/// two different spellings of what it means.
/// </para>
///
/// <para>
/// Kept as its own small class beside <c>MixDisplay</c> rather than folded
/// into it: that one owns the weighted mix's wording, and is not the home for
/// corpus-composition vocabulary.
/// </para>
/// </summary>
internal static class AnswerTypeDisplay
{
    /// <summary>
    /// One labelled bucket of a distribution — the shape Home renders. A
    /// <see langword="readonly record struct"/>: a pair of values with no
    /// identity, compared by value, never mutated.
    /// </summary>
    /// <param name="Label">The user-facing name of the answer type.</param>
    /// <param name="Count">
    /// How many matching decisions call for that answer — <c>0</c> is a
    /// meaningful value, not an absence.
    /// </param>
    internal readonly record struct Bucket(string Label, int Count);

    /// <summary>
    /// The five buckets of <paramref name="distribution"/>, labelled and in
    /// display order. Always five entries; zero-count buckets are included (see
    /// the type's remarks).
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="distribution"/> is null.
    /// </exception>
    public static IReadOnlyList<Bucket> Buckets(AnswerTypeDistribution distribution)
    {
        ArgumentNullException.ThrowIfNull(distribution);
        return
        [
            new Bucket("Checker plays", distribution.CheckerPlays),
            new Bucket(CubeLabels.Label(CubeClaimPair.NoDoubleTake), distribution.NoDoubleTake),
            new Bucket(CubeLabels.Label(CubeClaimPair.DoubleTake), distribution.DoubleTake),
            new Bucket(CubeLabels.Label(CubeClaimPair.DoublePass), distribution.DoublePass),
            new Bucket(CubeLabels.Label(CubeClaimPair.TooGoodPass), distribution.TooGoodPass),
        ];
    }
}
