using BgDataTypes_Lib;
using BgMoveGen;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// What <c>QuizController.HasNoPlayChoice</c> stands on: that
/// <see cref="MoveGenerator.GeneratePlays"/>' candidate list holds one play
/// per distinct position a legal play reaches.
///
/// <para>
/// The rule reads "exactly one legal play", and asks it the cheap way, as
/// <c>legal.Count == 1</c>. That is correct only while the generated list is
/// already distinct — which is <b>BgMoveGen's contract</b>, stated on
/// <see cref="MoveGenerator.GeneratePlays"/>, not this app's assumption. How
/// two plays compare is the producer's too: from a starting position, by the
/// position each reaches, stated once on <see cref="BoardState.IsSamePlay"/>
/// and not here. These are consumer-side pins on the contract, three facts
/// wide: die orderings of one play collapse, a hit keeps plays apart, and the
/// one shape that ever broke distinctness now arrives once. A producer
/// regression would not throw here — it would quietly quiz a position that
/// offers no decision — so the wire is worth pinning at the layer that reads
/// it.
/// </para>
///
/// <para>
/// Each position holds only the checkers the case needs; the generator asks
/// nothing of a board but the points, so a two-checker board is as real to it
/// as a thirty-checker one. The boards are working positions, not decisions,
/// so one side may have no checker at all.
/// </para>
/// </summary>
public class GeneratedPlayDistinctnessTests
{
    private static List<Play> PlaysFor(BoardState start, int die1, int die2) =>
        MoveGenerator.GeneratePlays(start, die1, die2);

    [Fact]
    public void DieOrderingsOfOnePlay_CollapseToOneCandidate()
    {
        // One on-roll checker on the 13-pt, roll 3-2, nothing in the way. It can
        // travel 13/11/8 (small die first) or 13/10/8 (big die first); with no
        // hit at either intermediate both reach the one position, so they are
        // the one play 13/8. The generator emits it once — so a position whose
        // only play has several encodings is still one candidate, and the skip
        // rule sees one play.
        var start = new BoardState(SoleCheckerOn13());
        var plays = PlaysFor(start, 3, 2);

        var only = Assert.Single(plays);
        // Every encoding is the generated play, from this position.
        Assert.True(start.IsSamePlay(Play.Create(new(13, 11), new(11, 8)), only));
        Assert.True(start.IsSamePlay(Play.Create(new(13, 10), new(10, 8)), only));
        Assert.True(start.IsSamePlay(Play.Create(new(13, 8)), only));
    }

    [Fact]
    public void HitAtAnIntermediate_KeepsTheOrderingsApart()
    {
        // The same position with an opponent blot on the 10-pt. Now the orders
        // reach different positions: 13/10*/8 puts a checker on the bar and
        // 13/11/8 does not, so they stay two. The half of the rule that must
        // NOT over-skip: this is a real choice and the quiz has to show it.
        var counts = SoleCheckerOn13Counts();
        counts[10] = -1;
        var start = new BoardState(new BoardPosition(counts));

        var plays = PlaysFor(start, 3, 2);

        Assert.Equal(2, plays.Count);
        Assert.False(start.IsSamePlay(plays[0], plays[1]));
    }

    [Fact]
    public void TwoDieBearOff_EmitsTheOnePlayOnce()
    {
        // The shape that decides whether `legal.Count == 1` can be the rule at
        // all. On-roll checkers on the 5- and the 4-point and nothing else,
        // roll 6-5: each die bears one checker off, and a bear-off move encodes
        // as (point, 0) whichever die paid for it — so the two die orders build
        // the same move list, and duplicate avoidance keyed on the moves rather
        // than on the dice has to reason about that case explicitly or emit the
        // play twice. It did emit it twice, which is why this rule spent a
        // while counting canonical plays instead of entries
        // (halheinrich/backgammon#140's workaround); the producer was fixed on
        // halheinrich/backgammon#141 and the workaround is retired.
        //
        // Deliberately the same assertion BgMoveGen's own suite makes, kept
        // here too: this is the consumer whose correctness now rests on it, and
        // a regression would be wrong *silently* — an extra entry throws
        // nothing, it just quizzes a position that offers no decision.
        var counts = new int[26];
        counts[5] = 1;
        counts[4] = 1;
        var start = new BoardState(new BoardPosition(counts));

        var plays = PlaysFor(start, 6, 5);

        var only = Assert.Single(plays);
        // Single alone would also pass if the generator dropped both entries
        // and emitted some other play; naming the play rules that out.
        Assert.True(start.IsSamePlay(Play.Create(new(5, 0), new(4, 0)), only));
    }

    /// <summary>
    /// A single on-roll checker on the 13-pt with the whole path home clear,
    /// and one opponent point far away so the board is not one-sided. Shared by
    /// the two ordering cases, which differ only in whether a blot sits on the
    /// 10-pt — the one fact that decides whether the orderings are one play.
    /// </summary>
    private static int[] SoleCheckerOn13Counts()
    {
        var m = new int[26];
        m[13] = 1;
        m[20] = -2;
        return m;
    }

    private static BoardPosition SoleCheckerOn13() => new(SoleCheckerOn13Counts());
}
