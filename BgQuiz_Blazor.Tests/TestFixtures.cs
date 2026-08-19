using BgDataTypes_Lib;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Hand-crafted <see cref="BgDecisionData"/> values for controller and page
/// tests. Controller tests don't need physically legal plays — only the play's
/// canonical shape matters for the equality matcher. Pass-position fixtures must
/// produce zero legal plays from <c>MoveGenerator.GeneratePlays</c> so the
/// controller's auto-skip path is exercised.
/// </summary>
internal static class TestFixtures
{
    /// <summary>Standard backgammon starting position (Mop array, 26 entries).</summary>
    public static int[] StandardMop()
    {
        var m = new int[26];
        m[6] = 5;  m[8] = 3;  m[13] = 5;  m[24] = 2;
        m[19] = -5; m[17] = -3; m[12] = -5; m[1] = -2;
        return m;
    }

    /// <summary>
    /// Pass-position Mop: on-roll player on the bar against a fully closed
    /// opponent home board (points 19-24 each have two opponent checkers).
    /// Combined with any dice, <c>MoveGenerator.GeneratePlays</c> returns
    /// zero plays — no entry square exists.
    /// </summary>
    public static int[] ClosedOutMop()
    {
        var m = new int[26];
        m[25] = 1;
        for (int p = 19; p <= 24; p++) m[p] = -2;
        return m;
    }

    public static Play MakePlay(params (int from, int to)[] moves)
    {
        var play = new Play();
        foreach (var (from, to) in moves)
            play.Add(new Move(from, to));
        return play;
    }

    /// <summary>
    /// The content identity of <paramref name="decision"/>, through the
    /// producer's single derivation factory — the only way a test may obtain a
    /// <see cref="ProblemKey"/>. Hand-assembling a canonical string here would
    /// be a second derivation site that can disagree with the app's, which is
    /// exactly what the type's one-factory rule forbids.
    ///
    /// <para>
    /// Throws when the fixture has no derivable key: every fixture in this file
    /// carries real, physically-possible facts, so an underivable one is a
    /// broken fixture rather than a scenario. Tests that mean to exercise the
    /// no-key rung build the malformed record where they use it and pass
    /// <see langword="null"/> themselves.
    /// </para>
    /// </summary>
    public static ProblemKey KeyOf(BgDecisionData decision) =>
        ProblemKey.TryDerive(decision, out var key)
            ? key
            : throw new InvalidOperationException(
                "Fixture has no derivable ProblemKey — its facts are malformed or degenerate.");

    /// <summary>
    /// Deterministic two-candidate decision: <c>play1</c> at zero loss (best),
    /// <c>play2</c> at <paramref name="play2Loss"/>. Standard Mop, dice (3,1)
    /// for the pass-detection step (not pass — standard start has many plays
    /// for 3-1). <paramref name="recordedPlayIndex"/> is the .xg-recorded played
    /// move (the solution diagram's <c>*</c>); defaults to <c>-1</c> (no recorded
    /// play) so existing callers are unaffected. <paramref name="id"/> overrides the
    /// decision's stable identity for tests that pin how <c>BgDecisionData.Id</c>
    /// flows through submissions; defaults to a shared placeholder.
    /// <paramref name="away"/> sets both sides' away score (0 = money game) —
    /// the discriminator for tests needing <i>content-distinct</i> problems.
    /// Away scores participate in <see cref="ProblemKey"/> identity, and unlike
    /// the board or the dice they leave move generation untouched, so a fixture
    /// varied this way stays exactly as playable as the default one.
    /// </summary>
    public static BgDecisionData TwoChoiceDecision(
        Play play1, Play play2, double play2Loss = 0.05, string onRoll = "Alice",
        string opp = "Bob", string xgid = "", int recordedPlayIndex = -1,
        DecisionId? id = null, int away = 0)
    {
        return new BgDecisionData
        {
            Id = id ?? new XgpDecisionId("test.xgp"),
            Xgid = xgid,
            Position = new PositionData
            {
                Mop = StandardMop(),
                OnRollNeeds = away,
                OpponentNeeds = away,
            },
            Decision = new DecisionData
            {
                Dice = [3, 1],
                Plays =
                [
                    new PlayCandidate { Play = play1, EquityLoss = 0.0, MoveNotation = "best" },
                    new PlayCandidate { Play = play2, EquityLoss = play2Loss, MoveNotation = "alt" },
                ],
                BestPlayIndex = 0,
                UserPlayIndex = recordedPlayIndex,
            },
            Descriptive = new DescriptiveData { OnRollName = onRoll, OpponentName = opp },
        };
    }

    /// <summary>
    /// Deterministic cube decision. With the defaults
    /// (<paramref name="noDoubleEquity"/> 0.5, <paramref name="doubleTakeEquity"/>
    /// 0.7) the best answer is (<c>Double</c>, <c>Take</c>) at zero loss on both
    /// halves; the opposite answer loses
    /// <c>doubleTakeEquity - noDoubleEquity</c> (0.20) on the doubler half and
    /// <c>1 - doubleTakeEquity</c> (0.30) on the taker half. Dice are left at the
    /// data-layer cube invariant ([0, 0]). <paramref name="id"/> overrides the
    /// decision's stable identity for tests that pin how <c>BgDecisionData.Id</c>
    /// flows through submissions; defaults to a shared placeholder.
    /// <paramref name="away"/> discriminates content identity exactly as on
    /// <see cref="TwoChoiceDecision"/>.
    /// </summary>
    public static BgDecisionData CubeDecision(
        double noDoubleEquity = 0.5, double doubleTakeEquity = 0.7,
        string onRoll = "Alice", string opp = "Bob", string xgid = "",
        DecisionId? id = null, int away = 0)
    {
        return new BgDecisionData
        {
            Id = id ?? new XgpDecisionId("test.xgp"),
            Xgid = xgid,
            Position = new PositionData
            {
                Mop = StandardMop(),
                OnRollNeeds = away,
                OpponentNeeds = away,
            },
            Decision = new DecisionData
            {
                IsCube = true,
                NoDoubleEquity = noDoubleEquity,
                DoubleTakeEquity = doubleTakeEquity,
            },
            Descriptive = new DescriptiveData { OnRollName = onRoll, OpponentName = opp },
        };
    }

    /// <summary>
    /// Bear-off-one decision: a single on-roll checker on the 1-pt with dice
    /// (1,1), whose only legal play is 1/off. Drives a deterministic completion
    /// sequence (select the 1-pt, then bear off to the tray) through
    /// <c>BackgammonPlayEntry</c> without hand-picking ambiguous click orderings.
    /// The lone candidate is that play at zero loss, so a completed submit scores
    /// as correct — used to exercise the dice-click → submit wire end-to-end.
    /// </summary>
    public static BgDecisionData BearOffOneDecision(
        string onRoll = "Alice", string opp = "Bob")
    {
        var m = new int[26];
        m[1] = 1;
        return new BgDecisionData
        {
            Id = new XgpDecisionId("test.xgp"),
            Position = new PositionData { Mop = m },
            Decision = new DecisionData
            {
                Dice = [1, 1],
                Plays =
                [
                    // ToPt 0 = bear off; the entry's completed 1/off play matches
                    // this candidate by canonical Play equality ((1, 0)).
                    new PlayCandidate { Play = MakePlay((1, 0)), EquityLoss = 0.0, MoveNotation = "1/off" },
                ],
                BestPlayIndex = 0,
            },
            Descriptive = new DescriptiveData { OnRollName = onRoll, OpponentName = opp },
        };
    }

    /// <summary>Pass-position decision — controller must auto-skip silently.</summary>
    public static BgDecisionData PassDecision()
    {
        return new BgDecisionData
        {
            Id = new XgpDecisionId("test.xgp"),
            Position = new PositionData { Mop = ClosedOutMop() },
            Decision = new DecisionData
            {
                Dice = [1, 2],
                Plays = [],
            },
            Descriptive = new DescriptiveData { OnRollName = "Alice", OpponentName = "Bob" },
        };
    }

    /// <summary>
    /// A <see cref="ComposedProblemSource"/> over <paramref name="source"/> —
    /// what a substitute <c>ProblemSetSourceFactory</c> hands back where the
    /// test's subject is the source and not the stack's dedupe telemetry.
    ///
    /// <para>
    /// <paramref name="duplicatesCollapsed"/> defaults to <c>0</c> because a
    /// substitute stack has no dedupe layer and so genuinely collapses nothing
    /// — an honest report, not a stub. A test about the magnitude passes the
    /// number its stack should report: the composed pair is the contract the
    /// controller consumes, so driving it directly is what pins the wire.
    /// </para>
    /// </summary>
    public static ComposedProblemSource Composed(
        IProblemSetSource source, int duplicatesCollapsed = 0) =>
        new(source, () => duplicatesCollapsed);
}
