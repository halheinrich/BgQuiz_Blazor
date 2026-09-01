using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Pins <see cref="AnswerTypeDisplay.Buckets"/>'s <b>mapping</b> — which
/// producer field each labelled bucket carries, in which order, and that all
/// six are always present.
///
/// <para>
/// Deliberately not a pin of the label <i>wording</i>: the labels are user-facing
/// copy, so they are pinned as independent literals in the published app by the
/// e2e suite (the copy-pin split). A unit test asserting the same strings the
/// class defines would agree with any wording at all — including a swap that put
/// <c>Double / pass</c>'s count under <c>Double / take</c>'s name, which is
/// exactly the defect this file <i>can</i> catch and does.
/// </para>
/// </summary>
public class AnswerTypeDisplayTests
{
    /// <summary>
    /// Distinct per-bucket counts, so a mis-wired field lands as a wrong number
    /// against a label rather than hiding behind equal values.
    /// </summary>
    private static AnswerTypeDistribution Distinct() => new(
        CheckerPlays: 1, NoDoubleTake: 2, DoubleTake: 3, DoublePass: 4, TooGoodPass: 5, TooGoodTake: 6);

    [Fact]
    public void Buckets_CarryTheProducerFieldsInDeclarationOrder()
    {
        var buckets = AnswerTypeDisplay.Buckets(Distinct());

        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, buckets.Select(b => b.Count).ToArray());
    }

    [Fact]
    public void Buckets_AreSixDistinctNonEmptyLabels()
    {
        // The count is the contract (the record's six fields, none dropped or
        // duplicated); the strings themselves are the e2e suite's business.
        var labels = AnswerTypeDisplay.Buckets(Distinct()).Select(b => b.Label).ToList();

        Assert.Equal(6, labels.Count);
        Assert.All(labels, l => Assert.False(string.IsNullOrWhiteSpace(l)));
        Assert.Equal(6, labels.Distinct().Count());
    }

    [Fact]
    public void Buckets_TheTwoTooGoodVerdicts_AreSeparateRows()
    {
        // The split the claim vocabulary exists for (halheinrich/backgammon#86):
        // Too good / take was uncountable before it and landed under No double
        // / take. Both too-good sides now carry their own producer field, and
        // nothing is folded — the take side is row six and the pass side row
        // five, each reading its own count and neither reading the other's.
        var onlyTake = AnswerTypeDisplay.Buckets(
            AnswerTypeDistribution.Empty with { TooGoodTake = 7 });
        var onlyPass = AnswerTypeDisplay.Buckets(
            AnswerTypeDistribution.Empty with { TooGoodPass = 9 });

        Assert.Equal(7, onlyTake[5].Count);
        Assert.Equal(0, onlyTake[4].Count);
        Assert.Equal(0, onlyTake[1].Count); // and not under No double / take
        Assert.Equal(9, onlyPass[4].Count);
        Assert.Equal(0, onlyPass[5].Count);
    }

    [Fact]
    public void Buckets_EmptyDistribution_StillListsEveryAnswerType()
    {
        // The zero-bucket rule, at its extreme: an empty distribution yields six
        // buckets at zero, not an empty list. Home decides whether an empty
        // *pool* is worth rendering at all; this type never decides that a
        // category is uninteresting because nothing landed in it — the zero is
        // the finding the breakdown exists to show.
        var buckets = AnswerTypeDisplay.Buckets(AnswerTypeDistribution.Empty);

        Assert.Equal(6, buckets.Count);
        Assert.All(buckets, b => Assert.Equal(0, b.Count));
    }

    [Fact]
    public void Buckets_TotalIsNotABucket()
    {
        // Total is the match count and belongs to Home's count line; repeating it
        // in the breakdown would put one number on screen twice under two
        // different meanings. 1+2+3+4+5+6 = 21, which must appear nowhere here.
        var distribution = Distinct();

        Assert.Equal(21, distribution.Total);
        Assert.DoesNotContain(
            AnswerTypeDisplay.Buckets(distribution), b => b.Count == distribution.Total);
    }

    [Fact]
    public void Buckets_NullDistribution_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AnswerTypeDisplay.Buckets(null!));
    }
}
