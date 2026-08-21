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
    /// <summary>
    /// Where a fixture's decision sits in its source, as the converter stamps
    /// it: the file name (with extension, no directory), the 1-based game
    /// number, and the 1-based move number. The three travel together because
    /// they are one fact — a file name with no coordinates locates a file, not
    /// a problem — and they are passed as one parameter so a caller cannot set
    /// two of them and forget the third.
    ///
    /// <para>
    /// <b>It also fixes the record's identity shape</b>, through
    /// <see cref="ToId"/>, and that is the point of the two factories rather
    /// than a constructor. A real record's <c>DecisionId</c> and its
    /// <c>DescriptiveData</c> cannot disagree about where it came from —
    /// <c>SPEC-quiz-view.md</c> §4's ruling (ii) reads the <i>identity's</i>
    /// kind to decide whether the coordinates mean anything — so a fixture that
    /// let a caller pair an <c>.xgp</c> identity with match coordinates would be
    /// staging a record the app cannot produce, and any pin standing on it
    /// would be proving something about nothing.
    /// </para>
    ///
    /// <para>
    /// Nothing here is content identity: <c>ProblemKey</c> derives from the
    /// position and the decision alone, which
    /// <c>TestFixtureContractTests.FixturesDifferingOnlyInWhereTheyCameFrom_AreTheSameProblem</c>
    /// pins across both shapes. These values exist so a test can drive
    /// <c>ProblemLocator</c> through the real page; leaving the parameter unset
    /// leaves the record exactly as every fixture had it before the locator
    /// existed, so a test that says nothing about provenance still renders no
    /// chip.
    /// </para>
    /// </summary>
    internal sealed record SourceLocation
    {
        private SourceLocation(string sourceFile, int game, int moveNumber, bool onePosition)
        {
            SourceFile = sourceFile;
            Game = game;
            MoveNumber = moveNumber;
            IsOnePosition = onePosition;
        }

        /// <summary>
        /// A decision inside a multi-game <c>.xg</c> match — the shape that has
        /// real within-file coordinates, and the one whose numbers agree with
        /// what eXtreme Gammon shows for the position.
        /// </summary>
        public static SourceLocation InMatch(string sourceFile, int game, int moveNumber) =>
            new(sourceFile, game, moveNumber, onePosition: false);

        /// <summary>
        /// A standalone <c>.xgp</c> position file. The coordinates are fixed at
        /// <c>1, 1</c> because that is what the converter really stamps on every
        /// such record — off a synthetic single-game header — and a fixture that
        /// invented different ones would let a pin pass on a value the wire
        /// never produces.
        /// </summary>
        public static SourceLocation OnePosition(string sourceFile) =>
            new(sourceFile, 1, 1, onePosition: true);

        /// <summary>Originating file name, with extension and no directory.</summary>
        public string SourceFile { get; }

        /// <summary>1-based game number within the source.</summary>
        public int Game { get; }

        /// <summary>1-based move number within the game.</summary>
        public int MoveNumber { get; }

        /// <summary>Whether the source file holds this one position and no more.</summary>
        public bool IsOnePosition { get; }

        /// <summary>
        /// The identity a real record drawn from here would carry — the
        /// producer's own two shapes, chosen by this location's own kind.
        /// </summary>
        public DecisionId ToId(bool isCube) =>
            IsOnePosition
                ? new XgpDecisionId(SourceFile)
                : new XgDecisionId(SourceFile, Game, MoveNumber, isCube);
    }

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
    /// The provenance category the general factories stamp: the player names
    /// they were always given, plus <paramref name="location"/> when the caller
    /// supplied one. One helper rather than two initializers, so the play and
    /// cube fixtures cannot come to disagree about what an unset location
    /// means — and the answer is the record's own defaults (no file name,
    /// game 0, move 0), which <c>ProblemLocator</c> reads as "locates nothing".
    /// </summary>
    private static DescriptiveData Describe(
        string onRoll, string opp, SourceLocation? location) =>
        new()
        {
            OnRollName = onRoll,
            OpponentName = opp,
            SourceFile = location?.SourceFile,
            Game = location?.Game ?? 0,
            MoveNumber = location?.MoveNumber ?? 0,
        };

    /// <summary>
    /// Deterministic two-candidate decision: <c>play1</c> at zero loss (best),
    /// <c>play2</c> at <paramref name="play2Loss"/>. Standard Mop, dice (3,1)
    /// for the pass-detection step (not pass — standard start has many plays
    /// for 3-1). <paramref name="recordedPlayIndex"/> is the .xg-recorded played
    /// move (the solution diagram's <c>*</c>); defaults to <c>-1</c> (no recorded
    /// play) so existing callers are unaffected. <paramref name="id"/> overrides the
    /// decision's stable identity for tests that pin how <c>BgDecisionData.Id</c>
    /// flows through submissions; defaults to a shared placeholder.
    /// <paramref name="location"/> stamps where the decision came from (see
    /// <see cref="SourceLocation"/>); unset leaves the record locating nothing,
    /// which is what every fixture said before <c>ProblemLocator</c> existed.
    /// <paramref name="away"/> sets both sides' away score (0 = money game) —
    /// the discriminator for tests needing <i>content-distinct</i> problems.
    /// Away scores participate in <see cref="ProblemKey"/> identity, and unlike
    /// the board or the dice they leave move generation untouched, so a fixture
    /// varied this way stays exactly as playable as the default one.
    ///
    /// <para>
    /// At the default <c>0</c> the fixture is a <b>money</b> position, and money
    /// is the one score whose key spells the Jacoby rule — an unstamped money
    /// record has no key at all (the no-key rung) — so the fixture says which
    /// rule it means. It means Jacoby on; the value is arbitrary here, the stamp
    /// is not. Off money the fact is meaningless, so match fixtures stay
    /// unstamped rather than carrying noise, and the stamp is derived from
    /// <paramref name="away"/> rather than passed in so two fixtures with the
    /// same score cannot disagree about it.
    /// </para>
    /// </summary>
    public static BgDecisionData TwoChoiceDecision(
        Play play1, Play play2, double play2Loss = 0.05, string onRoll = "Alice",
        string opp = "Bob", string xgid = "", int recordedPlayIndex = -1,
        DecisionId? id = null, int away = 0, SourceLocation? location = null)
    {
        return new BgDecisionData
        {
            Id = id ?? location?.ToId(isCube: false) ?? new XgpDecisionId("test.xgp"),
            Xgid = xgid,
            Position = new PositionData
            {
                Mop = StandardMop(),
                OnRollNeeds = away,
                OpponentNeeds = away,
                IsJacoby = away == 0 ? true : null,
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
            Descriptive = Describe(onRoll, opp, location),
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
    /// <see cref="TwoChoiceDecision"/>, and <paramref name="location"/> is
    /// display-only — and supplies the matching identity — exactly as there.
    /// </summary>
    public static BgDecisionData CubeDecision(
        double noDoubleEquity = 0.5, double doubleTakeEquity = 0.7,
        string onRoll = "Alice", string opp = "Bob", string xgid = "",
        DecisionId? id = null, int away = 0, SourceLocation? location = null)
    {
        return new BgDecisionData
        {
            Id = id ?? location?.ToId(isCube: true) ?? new XgpDecisionId("test.xgp"),
            Xgid = xgid,
            Position = new PositionData
            {
                Mop = StandardMop(),
                OnRollNeeds = away,
                OpponentNeeds = away,
                IsJacoby = away == 0 ? true : null,
            },
            Decision = new DecisionData
            {
                IsCube = true,
                NoDoubleEquity = noDoubleEquity,
                DoubleTakeEquity = doubleTakeEquity,
            },
            Descriptive = Describe(onRoll, opp, location),
        };
    }

    /// <summary>
    /// Bear-off-one decision: a single on-roll checker on the 1-pt with dice
    /// (1,1), whose only legal play is 1/off. Drives a deterministic completion
    /// sequence (select the 1-pt, then bear off to the tray) through
    /// <c>BackgammonPlayEntry</c> without hand-picking ambiguous click orderings.
    /// The lone candidate is that play at zero loss, so a completed submit scores
    /// as correct — used to exercise the dice-click → submit wire end-to-end.
    /// Money, like every unscored fixture here, so it stamps the Jacoby rule for
    /// the reason <see cref="TwoChoiceDecision"/> does: this file's fixtures are
    /// real positions, and a real money position with no stamp would silently be
    /// the no-key rung instead.
    /// </summary>
    public static BgDecisionData BearOffOneDecision(
        string onRoll = "Alice", string opp = "Bob")
    {
        var m = new int[26];
        m[1] = 1;
        return new BgDecisionData
        {
            Id = new XgpDecisionId("test.xgp"),
            Position = new PositionData { Mop = m, IsJacoby = true },
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

    /// <summary>
    /// Pass-position decision — controller must auto-skip silently. Money, and
    /// stamped for the same reason <see cref="BearOffOneDecision"/> is: nothing
    /// asks this fixture for its key today, and an unstamped one would quietly
    /// stop having one.
    /// </summary>
    public static BgDecisionData PassDecision()
    {
        return new BgDecisionData
        {
            Id = new XgpDecisionId("test.xgp"),
            Position = new PositionData { Mop = ClosedOutMop(), IsJacoby = true },
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
