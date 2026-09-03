namespace BgQuiz_Blazor.Client.Quiz;

using BgGame_Lib;

/// <summary>
/// The one home for user-facing wording of BgGame_Lib's
/// <see cref="AnswerTypeDistribution"/> — the answer-type breakdown Home shows
/// beside the pre-Start match count.
///
/// <para>
/// <b>The split of ownership.</b> Which bucket a decision lands in is the
/// producer's rule and is never re-derived here; what a bucket is <i>called</i>
/// is this app's copy, because the labels have to read as a quiz-taker's
/// vocabulary rather than as record property names. So this type maps the
/// producer's five fields onto five host-owned labels and nothing else — it
/// classifies nothing and computes nothing.
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
/// 2026-09-02 (halheinrich/backgammon#187) as the producer declares them — no
/// double / take, the two doubles, then too good / pass. It is the producer's
/// ordering, so there is no second convention to keep in step. The "Too good /
/// take" row of the halheinrich/backgammon#86 era is retired with its verdict
/// (Too Good requires the pass; a position the opponent would take is No
/// double / Take by ruling, and counts there). The cube labels still spell each
/// verdict as "claim / taker response"; the label SSOT arc
/// (halheinrich/backgammon#185) re-sources them to the one label home, so
/// this leg deliberately does not re-spell them.
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
/// Kept as its own small class beside <see cref="CubeActionDisplay"/> and
/// <c>MixDisplay</c> rather than folded into either: those own the review
/// verdict's claim/action labels and the weighted mix's wording respectively,
/// and neither is the home for corpus-composition vocabulary.
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
            new Bucket("No double / take", distribution.NoDoubleTake),
            new Bucket("Double / take", distribution.DoubleTake),
            new Bucket("Double / pass", distribution.DoublePass),
            new Bucket("Too good / pass", distribution.TooGoodPass),
        ];
    }
}
