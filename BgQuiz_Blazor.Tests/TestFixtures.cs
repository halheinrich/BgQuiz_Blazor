using BgDataTypes_Lib;
using BgDataTypes_Lib.TestSupport;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The decisions controller and page tests quiz on, built through the
/// producer's own record builders (<see cref="TestRecords"/>) — the one way a
/// test builds a record, so no fixture here restates the records' construction
/// rules (halheinrich/backgammon#273).
///
/// <para>
/// <b>Every play in them is legal from its position</b> — makeable with its
/// roll, not merely valid by the records' play rule, which leaves the dice to
/// the move generator. The fixtures once listed plays no roll could produce
/// (<c>8/5 8/5</c> on a 3-1), which matched only because play equality was a
/// comparison of encodings; plays are compared by the position they reach
/// now, so a fixture's plays must be ones a board could actually be played
/// into. <c>TestFixtureContractTests</c> holds every fixture to that. The
/// <i>boards</i> of the auto-skip fixtures are exact too: what
/// <c>MoveGenerator.GeneratePlays</c> derives from the board and the dice is
/// the whole fact <c>QuizController.HasNoPlayChoice</c> reads.
/// </para>
///
/// <para>
/// <b>Every fixture is a decision position</b> — a checker of each side on the
/// board or bar — because no other record can be built
/// (<c>PositionData</c>'s invariant; SPEC-stats-identity.md §2, amended
/// 2026-09-27). So every fixture has a <see cref="ProblemKey"/>, by the
/// producer's guarantee: a test takes one with <see cref="ProblemKey.From"/>.
/// </para>
/// </summary>
internal static class TestFixtures
{
    /// <summary>
    /// Where a fixture's decision sits in its source: a file, and — for a
    /// decision inside a match — its game and move. The record states these
    /// through its identity alone (<c>BgDecisionData.SourceFile</c>,
    /// <c>Game</c> and <c>MoveNumber</c> derive from its <see cref="DecisionId"/>),
    /// so a location is exactly the identity it builds; it exists so a test
    /// states the place and leaves the identity's kind half — cube or play —
    /// to the fixture that knows which it is building.
    ///
    /// <para>
    /// Nothing here is content identity: <c>ProblemKey</c> derives from the
    /// position and the decision alone, which
    /// <c>TestFixtureContractTests.FixturesDifferingOnlyInWhereTheyCameFrom_AreTheSameProblem</c>
    /// pins across both shapes.
    /// </para>
    /// </summary>
    internal sealed record SourceLocation
    {
        private readonly int? _game;
        private readonly int? _moveNumber;

        private SourceLocation(string sourceFile, int? game, int? moveNumber)
        {
            SourceFile = sourceFile;
            _game = game;
            _moveNumber = moveNumber;
        }

        /// <summary>
        /// A decision inside a multi-game <c>.xg</c> match — the shape that has
        /// within-file coordinates, and the one whose numbers agree with what
        /// eXtreme Gammon shows for the position.
        /// </summary>
        public static SourceLocation InMatch(string sourceFile, int game, int moveNumber) =>
            new(sourceFile, game, moveNumber);

        /// <summary>
        /// A standalone <c>.xgp</c> position file, which belongs to no game and
        /// so has no coordinates at all (halheinrich/backgammon#124).
        /// </summary>
        public static SourceLocation OnePosition(string sourceFile) => new(sourceFile, null, null);

        /// <summary>Originating file name, with extension and no directory.</summary>
        public string SourceFile { get; }

        /// <summary>
        /// The identity a real record drawn from here would carry — the
        /// producer's own two shapes, chosen by this location's kind.
        /// </summary>
        public DecisionId ToId(bool isCube) =>
            _game is int game && _moveNumber is int moveNumber
                ? new XgDecisionId(SourceFile, game, moveNumber, isCube)
                : new XgpDecisionId(SourceFile);
    }

    /// <summary>The identity every fixture carries unless the test states one: a standalone position.</summary>
    private static readonly DecisionId DefaultId = new XgpDecisionId("test.xgp");

    /// <summary>
    /// The score context for <paramref name="away"/>: 0 is a money session
    /// under the Jacoby rule (XG's default), anything else a match of that
    /// length with both players that many away — its opening score. The away
    /// scores take part in <see cref="ProblemKey"/> identity and leave move
    /// generation untouched, which is what makes <paramref name="away"/> the
    /// way to build <i>content-distinct</i> fixtures that play alike.
    /// </summary>
    private static Session SessionFor(int away) =>
        away == 0
            ? TestRecords.MoneySession()
            : TestRecords.MatchSession(length: away, onRollNeeds: away, opponentNeeds: away);

    /// <summary>
    /// The descriptive category for a record identified by <paramref name="id"/>:
    /// the player names, the comment, and the standard-start fact a game has
    /// and a standalone position does not (the records refuse a mismatch).
    /// </summary>
    private static DescriptiveData Describe(DecisionId id, string onRoll, string opp, string? comment) =>
        TestRecords.Descriptive(
            onRollName: onRoll,
            opponentName: opp,
            isStandardStart: id is XgpDecisionId ? null : true,
            comment: comment);

    /// <summary>
    /// The opening 3-1's best play, <c>8/5 6/5</c> — the default best
    /// candidate of <see cref="TwoChoiceDecision"/>.
    /// </summary>
    public static Play OpeningBest() => Play.Create(new(8, 5), new(6, 5));

    /// <summary>
    /// A second legal play of the opening 3-1, <c>13/10 6/5</c> — the default
    /// second candidate of <see cref="TwoChoiceDecision"/>.
    /// </summary>
    public static Play OpeningAlternative() => Play.Create(new(13, 10), new(6, 5));

    /// <summary>
    /// A legal play of the opening 3-1 that <see cref="TwoChoiceDecision"/>
    /// never lists, <c>24/20</c> — so submitting it is an off-list play.
    /// </summary>
    public static Play OpeningUnlisted() => Play.Create(new(24, 21), new(21, 20));

    /// <summary>
    /// Deterministic two-candidate checker play: the opening 3-1 from the
    /// standard start, <paramref name="play1"/> the best (equity 0) and
    /// <paramref name="play2"/> worse by <paramref name="play2Loss"/>. Both are
    /// analyzed at the same depth, so the two rankings agree on everything —
    /// which is what lets the many tests about quiz flow say nothing about the
    /// ranking; <see cref="DepthSplitDecision"/> is the fixture where they part.
    /// Both plays must be legal for a 3-1 from the standard start (the three
    /// <c>Opening…</c> helpers are).
    ///
    /// <para>
    /// <paramref name="recordedPlayIndex"/> is the .xg-recorded played move (the
    /// solution diagram's <c>*</c>); null — the default — records none.
    /// <paramref name="id"/> overrides the decision's identity; otherwise
    /// <paramref name="location"/> supplies one, and failing both it is a
    /// standalone position. <paramref name="away"/> picks the score (see
    /// <see cref="SessionFor"/>): 0 is money, the default.
    /// <paramref name="comment"/> is the decision's XG comment, the text
    /// <c>DecisionNotes</c> displays (<c>halheinrich/backgammon#31</c>); null —
    /// the default — is "no notes".
    /// </para>
    /// </summary>
    public static CheckerPlayDecision TwoChoiceDecision(
        Play play1, Play play2, double play2Loss = 0.05, string onRoll = "Alice",
        string opp = "Bob", int? recordedPlayIndex = null,
        DecisionId? id = null, int away = 0, SourceLocation? location = null,
        string? comment = null)
    {
        var identity = id ?? location?.ToId(isCube: false) ?? DefaultId;
        return TestRecords.CheckerPlay(
            id: identity,
            position: TestRecords.Position(session: SessionFor(away)),
            decision: TestRecords.CheckerPlayData(
                dice: [3, 1],
                plays:
                [
                    TestRecords.Candidate(play: play1, equity: 0.0),
                    TestRecords.Candidate(play: play2, equity: -play2Loss),
                ],
                userPlayIndex: recordedPlayIndex),
            descriptive: Describe(identity, onRoll, opp, comment));
    }

    /// <summary>
    /// The fixture where the two rankings part (SPEC-scoring.md §2a): the
    /// opening 3-1 with three candidates, stored in this order —
    /// <list type="number">
    /// <item><see cref="OpeningAlternative"/>, <c>13/10 6/5</c>, rolled out
    /// (1296 trials, 3-ply), equity 0.00;</item>
    /// <item><see cref="OpeningBest"/>, <c>8/5 6/5</c>, a 3-ply evaluation,
    /// equity +0.05;</item>
    /// <item><c>24/23 13/10</c>, a 3-ply evaluation, equity −0.10.</item>
    /// </list>
    /// Under <see cref="PlayRanking.Equity"/> the 3-ply <c>8/5 6/5</c> is best,
    /// the rollout loses 0.05 and <c>24/23 13/10</c> 0.15. Under
    /// <see cref="PlayRanking.DepthFirst"/> the rollout is best, <c>24/23
    /// 13/10</c> loses 0.10, and <c>8/5 6/5</c> — analyzed less deeply than the
    /// best and rating higher there — is <b>not scored</b>. So every pin that
    /// runs on it tells the two rankings apart, and would fail if its path fell
    /// back to the producers' default, <see cref="PlayRanking.Equity"/>.
    /// <paramref name="recordedPlayIndex"/> is the .xg-recorded play, which the
    /// problem filter's error range reads; null — the default — records none.
    /// </summary>
    public static CheckerPlayDecision DepthSplitDecision(int away = 0, int? recordedPlayIndex = null) =>
        TestRecords.CheckerPlay(
            id: DefaultId,
            position: TestRecords.Position(session: SessionFor(away)),
            decision: TestRecords.CheckerPlayData(
                dice: [3, 1],
                plays:
                [
                    TestRecords.Candidate(
                        play: OpeningAlternative(), equity: 0.0,
                        analysisMode: AnalysisMode.Rollout, rolloutTrials: 1296),
                    TestRecords.Candidate(play: OpeningBest(), equity: 0.05),
                    TestRecords.Candidate(play: DepthSplitThirdPlay(), equity: -0.10),
                ],
                userPlayIndex: recordedPlayIndex),
            descriptive: Describe(DefaultId, "Alice", "Bob", comment: null));

    /// <summary>
    /// <see cref="DepthSplitDecision"/>'s third candidate, <c>24/23 13/10</c>:
    /// scored under both rankings, with a different error under each.
    /// </summary>
    public static Play DepthSplitThirdPlay() => Play.Create(new(24, 23), new(13, 10));

    /// <summary>
    /// Deterministic cube decision in the opening position. With the defaults
    /// (<paramref name="noDoubleEquity"/> 0.5, <paramref name="doubleTakeEquity"/>
    /// 0.7) the truth is <see cref="CubeAnswer.DoubleTake"/>, which costs
    /// nothing; No double costs <c>doubleTakeEquity - noDoubleEquity</c> (0.20)
    /// and Double / Pass <c>1 - doubleTakeEquity</c> (0.30) — the producer's
    /// costs (<see cref="CubeDecision.CostOf"/>), stated here only to read the
    /// pins. The record states no played action. <paramref name="id"/>,
    /// <paramref name="location"/>, <paramref name="away"/> and
    /// <paramref name="comment"/> mean what they do on
    /// <see cref="TwoChoiceDecision"/>.
    /// <paramref name="cubeOwner"/> defaults to <see cref="CubeOwner.OnRoll"/>
    /// (a turned cube, on 2), so on the default money fixture — Jacoby on, as
    /// <paramref name="away"/> 0 states it — gammons are possible and the
    /// fourth answer reads Too good; pass <see cref="CubeOwner.Centered"/> (a
    /// cube on 1) for a money position under Jacoby with the cube in the
    /// middle, where gammons are not possible
    /// (<see cref="CubeDecision.GammonsPossible"/>) and the fourth answer
    /// reads No double / Pass.
    /// </summary>
    public static CubeDecision CubeDecision(
        double noDoubleEquity = 0.5, double doubleTakeEquity = 0.7,
        string onRoll = "Alice", string opp = "Bob",
        DecisionId? id = null, int away = 0, SourceLocation? location = null,
        CubeOwner cubeOwner = CubeOwner.OnRoll, string? comment = null)
    {
        var identity = id ?? location?.ToId(isCube: true) ?? DefaultId;
        return TestRecords.Cube(
            id: identity,
            position: TestRecords.Position(
                cubeSize: cubeOwner == CubeOwner.Centered ? 1 : 2,
                cubeOwner: cubeOwner,
                session: SessionFor(away)),
            decision: TestRecords.CubeData(
                noDoubleEquity: noDoubleEquity,
                doubleTakeEquity: doubleTakeEquity,
                userDoublerAction: null,
                userTakerAction: null),
            descriptive: Describe(identity, onRoll, opp, comment));
    }

    /// <summary>
    /// One-click checker decision: on-roll checkers on the 12- and 9-points with
    /// dice (6,5), against opponent points on 7, 4 and 1. The 5 plays nowhere
    /// (12/7 and 9/4 are both blocked) and neither 6 leaves a 5 to follow, so
    /// must-use-the-larger-die leaves exactly <b>two</b> legal plays, each one
    /// move long: 12/6 and 9/3. Clicking the 12-pt therefore completes a whole
    /// play in a single click — a deterministic completion through
    /// <c>BackgammonPlayEntry</c> with no ambiguous click ordering to hand-pick.
    /// The lone candidate is 12/6, so a completed submit scores as correct —
    /// used to exercise the dice-click → submit wire end-to-end.
    ///
    /// <para>
    /// <b>Two legal plays is the load-bearing property, not a detail.</b> A
    /// position offering one is auto-skipped before it can reach a page
    /// (<c>QuizController.HasNoPlayChoice</c>, halheinrich/backgammon#140), so
    /// the obvious one-click fixture — a lone checker on the 1-pt bearing off —
    /// can no longer be shown at all. Anything staged here to drive the play
    /// entry <i>through the controller</i> must offer a choice.
    /// </para>
    /// </summary>
    public static CheckerPlayDecision OneClickPlayDecision(
        string onRoll = "Alice", string opp = "Bob") =>
        OneCandidate(DefaultId, TwoLegalPlaysBoard(), [6, 5], Play.Create(new(12, 6)), onRoll, opp);

    /// <summary>
    /// The <see cref="OneClickPlayDecision"/> board: two legal plays, 12/6 and
    /// 9/3. Shared with <see cref="ForcedPlayDecision"/>, which blocks the 3-pt
    /// to take the second away — one board, one blocker apart, so the pair
    /// differs in exactly the fact the skip rule reads.
    /// </summary>
    private static int[] TwoLegalPlaysCounts()
    {
        var m = new int[26];
        m[12] = 1; m[9] = 1;
        m[7] = -2; m[4] = -2; m[1] = -2;
        return m;
    }

    private static BoardPosition TwoLegalPlaysBoard() => new(TwoLegalPlaysCounts());

    /// <summary>
    /// Forced non-double: <see cref="TwoLegalPlaysBoard"/> with the 3-pt blocked
    /// too, so 9/3 is gone and 12/6 is the only legal play on a (6,5). The play
    /// <i>moves something</i> — the case halheinrich/backgammon#140 is about,
    /// and the one <see cref="PassDecision"/> does not cover. The controller
    /// must auto-skip it exactly as it skips a pass.
    /// </summary>
    public static CheckerPlayDecision ForcedPlayDecision()
    {
        var m = TwoLegalPlaysCounts();
        m[3] = -2;
        return OneCandidate(new XgpDecisionId("forced.xgp"), new BoardPosition(m), [6, 5], Play.Create(new(12, 6)));
    }

    /// <summary>
    /// Forced double: a lone on-roll checker on the 24-pt with dice (6,6), the
    /// 12-pt blocked. 24/18 is the only move and nothing follows it, so the roll
    /// admits one play even though three of its four dice go unplayed. Doubles
    /// take a different generation path through BgMoveGen than the non-double
    /// <see cref="ForcedPlayDecision"/> exercises, which is why both are staged.
    /// </summary>
    public static CheckerPlayDecision ForcedDoubleDecision()
    {
        var m = new int[26];
        m[24] = 1;
        m[12] = -2;
        return OneCandidate(new XgpDecisionId("forced-double.xgp"), new BoardPosition(m), [6, 6], Play.Create(new(24, 18)));
    }

    /// <summary>
    /// A forced bear-off: on-roll checkers on the 5- and 4-points, dice (6,5),
    /// and an opponent checker back on the on-roll player's 24-point, out of
    /// the way. Both on-roll checkers come off whichever die pays for which,
    /// and a bear-off move encodes as <c>(point, 0)</c> either way, so the roll
    /// admits one legal play — the position offers no choice and must
    /// auto-skip.
    ///
    /// <para>
    /// Not an arbitrary forced position. It is the one board whose candidate
    /// <i>count</i> has actually moved: <c>MoveGenerator.GeneratePlays</c>
    /// returned this single play <b>twice</b> until
    /// halheinrich/backgammon#141 fixed it, which is why the skip rule spent a
    /// while counting canonical plays rather than list entries
    /// (halheinrich/backgammon#140). Kept staged where the controller can be
    /// driven over it, so the skip is pinned end-to-end on the shape most
    /// likely to regress. The opponent checker is what makes it a decision
    /// position — the board it replaced had the opponent borne off entirely,
    /// which no record can stand on now.
    /// </para>
    /// </summary>
    public static CheckerPlayDecision ForcedBearOffDecision()
    {
        var m = new int[26];
        m[5] = 1; m[4] = 1;
        m[24] = -1;
        return OneCandidate(
            new XgpDecisionId("forced-bearoff.xgp"), new BoardPosition(m), [6, 5],
            Play.Create(new(5, 0), new(4, 0)));
    }

    /// <summary>
    /// Pass-position decision — the controller must auto-skip it silently. The
    /// on-roll player is on the bar against a fully closed opponent board
    /// (points 19–24 two each), so no roll enters; the one candidate is the
    /// pass, the empty play.
    /// </summary>
    public static CheckerPlayDecision PassDecision()
    {
        var m = new int[26];
        m[25] = 1;
        for (int p = 19; p <= 24; p++) m[p] = -2;
        return OneCandidate(DefaultId, new BoardPosition(m), [1, 2], []);
    }

    /// <summary>
    /// A money-session checker play off <paramref name="board"/> with
    /// <paramref name="dice"/>, listing <paramref name="candidates"/> in that
    /// order, each 0.05 worse than the one before — so the first is the best
    /// under either ranking (they share one depth). For a test whose subject
    /// is a particular position — a hit, a point made on a blot — that the
    /// opening 3-1 cannot stage. Every candidate must be legal for the roll
    /// from the board.
    /// </summary>
    public static CheckerPlayDecision CheckerPlayOn(
        BoardPosition board, IReadOnlyList<int> dice, params Play[] candidates) =>
        TestRecords.CheckerPlay(
            id: DefaultId,
            position: TestRecords.Position(mop: board, session: SessionFor(0)),
            decision: TestRecords.CheckerPlayData(
                dice: dice,
                plays: [.. candidates.Select((play, i) => TestRecords.Candidate(play: play, equity: -0.05 * i))],
                userPlayIndex: null),
            descriptive: Describe(DefaultId, "Alice", "Bob", comment: null));

    /// <summary>A money-session checker play off <paramref name="board"/> with one candidate, <paramref name="play"/>.</summary>
    private static CheckerPlayDecision OneCandidate(
        DecisionId id, BoardPosition board, IReadOnlyList<int> dice, Play play,
        string onRoll = "Alice", string opp = "Bob") =>
        TestRecords.CheckerPlay(
            id: id,
            position: TestRecords.Position(mop: board, session: SessionFor(0)),
            decision: TestRecords.CheckerPlayData(
                dice: dice,
                plays: [TestRecords.Candidate(play: play, equity: 0.0)],
                userPlayIndex: null),
            descriptive: Describe(id, onRoll, opp, comment: null));

    /// <summary>
    /// The scored submission of <paramref name="play"/> against
    /// <paramref name="decision"/> under <paramref name="ranking"/> — the one way
    /// a test gets a <see cref="SubmittedPlay"/>, since the producer's scoring
    /// (<see cref="PlaySubmission.Score"/>) is the one way anything does: the
    /// candidate, the error, the verdict and the key come out of it together.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="play"/> does not score under <paramref name="ranking"/> —
    /// a skip, which yields no submission; a test wanting one scores the play
    /// itself.
    /// </exception>
    public static SubmittedPlay Scored(CheckerPlayDecision decision, Play play, PlayRanking ranking) =>
        PlaySubmission.Score(play, decision, ranking).TryGetScored(out var submitted)
            ? submitted
            : throw new InvalidOperationException(
                $"{play.ToNotation()} does not score against this fixture under {ranking}.");

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

    /// <summary>
    /// A <see cref="SourceReport"/> as the real
    /// <see cref="FilteredDecisionIterator"/> leaves it after a completed walk
    /// over <paramref name="streams"/> — this repo's one way to a populated
    /// report (halheinrich/backgammon#368). The report's writers are the
    /// producer's and <c>internal</c> to it, deliberately: a test that needs a
    /// rejection runs the real iterator over a stream the producer refuses,
    /// so the facts are read off the real read path, never fabricated. Over
    /// no streams it is the completed report of an empty walk — nothing
    /// attempted, nothing rejected, not all-rejected — which is the honest
    /// report for a substitute stack with no parse layer.
    /// </summary>
    public static SourceReport WalkedReport(params XgFileStream[] streams)
    {
        var report = new SourceReport();
        var iterator = new FilteredDecisionIterator(
            new DecisionFilterSet(), PlayRanking.Equity, NullLogger<FilteredDecisionIterator>.Instance);
        foreach (var _ in iterator.IterateXgStreamDiagrams(streams, report)) { }
        return report;
    }

    /// <summary>
    /// A parse result over <paramref name="decisions"/> nobody walked a file
    /// for — the completed report of an empty walk beside them — for seeding a
    /// holder's cache (<c>PickedProblemFolder.StoreParsed</c>) where the test's
    /// subject is what happens <i>over</i> a parsed pick, not the parse.
    /// </summary>
    public static ParsedProblemSet Parsed(params BgDecisionData[] decisions) =>
        new([.. decisions], WalkedReport());
}
