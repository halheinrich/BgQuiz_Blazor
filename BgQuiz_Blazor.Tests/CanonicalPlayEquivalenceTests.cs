using BgDataTypes_Lib;
using BgMoveGen;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// What <c>QuizController.HasNoPlayChoice</c> stands on: how
/// <see cref="MoveGenerator.GeneratePlays"/>' candidate list relates to
/// <see cref="CanonicalPlay"/>, BgDataTypes_Lib's play-equivalence SSOT.
///
/// <para>
/// The rule reads "exactly one legal play", and the cheap way to ask that is
/// <c>legal.Count == 1</c>. That is only correct if the generated list is
/// already distinct under canonical equivalence. <b>It is not</b> — the
/// bear-off case below is the counterexample — so the rule compares entries
/// instead, and these are the three facts that make that the right shape:
/// die orderings of one play do collapse, hits do keep plays apart, and one
/// play can still arrive twice.
/// </para>
///
/// <para>
/// Boards are built as raw Mop arrays and read through
/// <see cref="BoardState.FromMop"/> — the same door the controller uses — so
/// these pins exercise the production path rather than a parallel one. Each
/// position holds only the checkers the case needs; the generator asks nothing
/// of a board but the points, so a two-checker board is as real to it as a
/// thirty-checker one.
/// </para>
/// </summary>
public class CanonicalPlayEquivalenceTests
{
    private static List<Play> PlaysFor(int[] mop, int die1, int die2) =>
        MoveGenerator.GeneratePlays(BoardState.FromMop(mop), die1, die2);

    [Fact]
    public void DieOrderingsOfOnePlay_CollapseToOneCandidate()
    {
        // One on-roll checker on the 13-pt, roll 3-2, nothing in the way. It can
        // travel 13/11/8 (small die first) or 13/10/8 (big die first); with no
        // hit at either intermediate both are the one play 13/8, and
        // CanonicalPlay says so. The generator emits it once — so a position
        // whose only play has several encodings is still one candidate, and the
        // skip rule sees one play.
        var plays = PlaysFor(SoleCheckerOn13(), 3, 2);

        var only = Assert.Single(plays);
        // Both encodings compare equal to what was generated: Play equality is
        // canonical equality, insensitive to which die went first.
        Assert.Equal(TestFixtures.MakePlay((13, 11), (11, 8)), only);
        Assert.Equal(TestFixtures.MakePlay((13, 10), (10, 8)), only);
        Assert.Equal(TestFixtures.MakePlay((13, 8)), only);
    }

    [Fact]
    public void HitAtAnIntermediate_KeepsTheOrderingsApart()
    {
        // The same position with an opponent blot on the 10-pt. Now the orders
        // are different plays: 13/10*/8 puts a checker on the bar and 13/11/8
        // does not, and canonical equivalence is fully hit-sensitive, so they
        // stay two. The half of the rule that must NOT over-skip: this is a real
        // choice and the quiz has to show it.
        var mop = SoleCheckerOn13();
        mop[10] = -1;

        var plays = PlaysFor(mop, 3, 2);

        Assert.Equal(2, plays.Count);
        Assert.NotEqual(plays[0], plays[1]);
    }

    [Fact]
    public void OneCanonicalPlay_CanStillArriveTwice_TheBearOffCounterexample()
    {
        // The fact that decides the rule's shape. On-roll checkers on the 5- and
        // the 4-point and nothing else, roll 6-5: each die bears one checker off,
        // and a bear-off move encodes as (point, 0) whichever die paid for it —
        // so the two die orders are the same move list, and the generator's
        // order-duplicate avoidance (which keys on the moves, not on the dice)
        // does not fire. Two entries, one play.
        //
        // Hence `legal.Count == 1` is not the forced test: it would leave this
        // position quizzed as a decision it does not offer.
        //
        // The observation belongs to BgMoveGen and is booked with the umbrella;
        // BgMoveGen is unchanged here. If this ever goes red because the
        // generator started collapsing the pair, the rule can be simplified —
        // but only after re-establishing that no other shape does the same, and
        // this test going red is the signal to go and do that.
        var mop = new int[26];
        mop[5] = 1;
        mop[4] = 1;

        var plays = PlaysFor(mop, 6, 5);

        Assert.Equal(2, plays.Count);
        Assert.Equal(plays[0], plays[1]);
    }

    /// <summary>
    /// A single on-roll checker on the 13-pt with the whole path home clear,
    /// and one opponent point far away so the board is not one-sided. Shared by
    /// the two ordering cases, which differ only in whether a blot sits on the
    /// 10-pt — the one fact that decides whether the orderings are one play.
    /// </summary>
    private static int[] SoleCheckerOn13()
    {
        var m = new int[26];
        m[13] = 1;
        m[20] = -2;
        return m;
    }
}
