using BgDataTypes_Lib;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Hand-crafted <see cref="BgDecisionData"/> values for controller and page
/// tests. Controller tests don't need physically legal plays — only structural
/// shape matters for the dedup-key matcher. Pass-position fixtures must produce
/// zero legal plays from <c>MoveGenerator.GeneratePlays</c> so the controller's
/// auto-skip path is exercised.
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
    /// Deterministic two-candidate decision: <c>play1</c> at zero loss (best),
    /// <c>play2</c> at <paramref name="play2Loss"/>. Standard Mop, dice (3,1)
    /// for the pass-detection step (not pass — standard start has many plays
    /// for 3-1).
    /// </summary>
    public static BgDecisionData TwoChoiceDecision(
        Play play1, Play play2, double play2Loss = 0.05, string onRoll = "Alice", string opp = "Bob")
    {
        return new BgDecisionData
        {
            Position = new PositionData { Mop = StandardMop() },
            Decision = new DecisionData
            {
                Dice = [3, 1],
                Plays =
                [
                    new PlayCandidate { Play = play1, EquityLoss = 0.0, MoveNotation = "best" },
                    new PlayCandidate { Play = play2, EquityLoss = play2Loss, MoveNotation = "alt" },
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
            Position = new PositionData { Mop = ClosedOutMop() },
            Decision = new DecisionData
            {
                Dice = [1, 2],
                Plays = [],
            },
            Descriptive = new DescriptiveData { OnRollName = "Alice", OpponentName = "Bob" },
        };
    }
}
