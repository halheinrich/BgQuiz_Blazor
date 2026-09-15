using BgDataTypes_Lib;
using ConvertXgToJson_Lib;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The suite's one <c>.xg</c> fixture — a short match, built in memory at run
/// time rather than committed (issue <c>halheinrich/backgammon#125</c>).
///
/// <para>
/// <b>Why it is synthesized.</b> Every committed fixture here is an
/// <c>.xgp</c>, deliberately: a standalone position is one problem, which is
/// what makes each scenario deterministic. But <c>SPEC-quiz-view.md</c> §4's
/// ruling (ii) forks the locator on exactly that distinction — an <c>.xgp</c>
/// shows its file name alone, an <c>.xg</c> shows the file plus
/// <c>Game n · Move m</c> — so the <c>.xg</c> half of a shipped behaviour had
/// no fixture to smoke it against. Real <c>.xg</c> exports are not available
/// to fill the gap: the ones on this machine carry real players' names, and
/// they live under a gitignored path CI has never been able to see. Tracked
/// generator code is the stronger form of "not that": the names are fake
/// because they are written here, the bytes are regenerable by definition, and
/// no binary is committed.
/// </para>
///
/// <para>
/// <b>Everything a pin needs is a parameter here, never an observation.</b>
/// <see cref="CubeGameNumber"/> and <see cref="CubeMoveNumber"/> are derived
/// from what the builder is <i>told</i> to construct — the games staged ahead
/// of the cube's, and the plays staged ahead of the cube within it. A scenario
/// asserting "Game 2 · Move 4" therefore asserts a consequence of this file,
/// not a transcript of what the app happened to render; if the builder's
/// emission ever changes, the scenario fails against a stated expectation
/// instead of quietly agreeing with new output.
/// </para>
///
/// <para>
/// <b>It also carries the suite's decision notes</b>
/// (<c>halheinrich/backgammon#31</c>): the builder takes a per-decision
/// comment and writes it through the same comment table XG does, so both of
/// the file's problems carry one — see <see cref="CubeComment"/> and
/// <see cref="PlayComment"/> — and the review's Notes overlay is smoked
/// against text that crossed the real parse. No committed fixture has a
/// comment, which is what makes those the suite's no-notes case.
/// </para>
///
/// <para>
/// <b>Byte-determinism</b> is <c>XgFileBuilder</c>'s own documented contract
/// (no timestamps, no ids) — so two runs stage identical bytes, and a failure
/// is never "which file did this run get".
/// </para>
/// </summary>
internal static class SyntheticXgMatch
{
    /// <summary>
    /// The name the match is staged under. Longer than the locator's visible
    /// cap on purpose: the chip is then in its <b>widest</b> state, which is
    /// what puts the action row's tail under the width pressure
    /// <c>SPEC-quiz-view.md</c> §4 ruling (i) is about. A name that fitted
    /// would leave the one-line assertion passing without the shrink order
    /// having anything to do.
    /// </summary>
    internal const string StagedFileName = "synthetic-match-2026-04-12.xg";

    /// <summary>
    /// The players. Invented here, in tracked source: that is what makes the
    /// fixture safe for a public repository, and it is a property of how the
    /// file is constructed rather than a claim anyone has to check.
    /// </summary>
    private const string Player1 = "Player One";

    /// <inheritdoc cref="Player1"/>
    private const string Player2 = "Player Two";

    /// <summary>Match length. Short, and irrelevant to every pin — a match is
    /// simply what an <c>.xg</c> is.</summary>
    private const int MatchLength = 5;

    /// <summary>
    /// Games recorded before the one holding the cube decision. One, so the
    /// coordinates the chip shows are <b>non-degenerate</b>: an <c>.xgp</c>'s
    /// synthetic header stamps every record <c>Game 1 · Move 1</c>, and a
    /// fixture that also read 1 · 1 could not tell the two branches of ruling
    /// (ii) apart.
    /// </summary>
    private const int GamesBeforeTheCubeGame = 1;

    /// <summary>Player 1's score entering the cube's game — a match in progress.</summary>
    private const int CubeGameScore1 = 2;

    /// <summary>Player 2's score entering the cube's game.</summary>
    private const int CubeGameScore2 = 0;

    /// <summary>
    /// The opening this fixture's games play out, in order. Unanalysed by
    /// construction: XG records a play whether or not it was rolled out, and
    /// the quiz only ever asks about analysed decisions — so these give the
    /// match its shape and its move numbers while contributing no problem of
    /// their own. The file's problems are the two analysed decisions after
    /// them, in file order (see <see cref="Bytes"/>), which with shuffle off —
    /// every scenario's default — is the order a quiz asks them in.
    /// </summary>
    private static readonly (XgPlayer Player, DiceRoll Dice, Play Play)[] PlaysBeforeTheCube =
    [
        (XgPlayer.Player1, new DiceRoll(3, 1), Play.Create(new(8, 5), new(6, 5))),
        (XgPlayer.Player2, new DiceRoll(6, 5), Play.Create(new(24, 18), new(18, 13))),
        (XgPlayer.Player1, new DiceRoll(5, 4), Play.Create(new(13, 8), new(13, 9))),
    ];

    /// <summary>
    /// The cubeful equities the decision is analysed with, from the doubler's
    /// perspective. Values only — no scenario reads the answer they imply; the
    /// locator is what this fixture exists for.
    /// </summary>
    private static readonly XgCubeEquities CubeEquities =
        new(NoDouble: 0.42, DoubleTake: 0.61, DoubleDrop: 1.0);

    /// <summary>Evaluation depth stamped on the cube analysis.</summary>
    private const int CubePly = 4;

    /// <summary>
    /// The cube decision's comment — the text its review's Notes overlay must
    /// show (<c>halheinrich/backgammon#31</c>). A parameter of the fixture, so
    /// the scenario asserts what the file was told to carry rather than what
    /// the app happened to render. It carries the two shapes real XG comments
    /// have and the ruling turns on: an <b>embedded CRLF</b> (every real
    /// comment has several; the comment table escapes it and the reader
    /// restores it) and a <b>run of spaces</b> aligning a column. Invented here,
    /// like the players.
    /// </summary>
    internal const string CubeComment = "Double, take: the gammons tip it.\r\nND   +0.42     D/T   +0.61";

    /// <summary>
    /// The roll and the play of the file's second problem: <see cref="XgPlayer.Player2"/>,
    /// on roll once the double is taken, making the 5-point from the 8 and the
    /// 6. Analysed with that play as its one candidate, so entering it scores
    /// as correct — the scenario only needs a review to open, not a verdict to
    /// read. Legal from the position the cube game reaches: the builder
    /// validates it against the tracked board.
    /// </summary>
    private static readonly (DiceRoll Dice, Play Play) PlayAfterTheCube =
        (new DiceRoll(3, 1), Play.Create(new(8, 5), new(6, 5)));

    /// <summary>
    /// The checker play's comment — the same two shapes as
    /// <see cref="CubeComment"/>, so both decision kinds' notes are smoked with
    /// a line break in them.
    /// </summary>
    internal const string PlayComment = "Make the 5-point.\r\nSplitting the back checkers is second.";

    /// <summary>
    /// The game number the locator must show: games are numbered in the order
    /// they are added, so the cube's game is the one after
    /// <see cref="GamesBeforeTheCubeGame"/>.
    /// </summary>
    internal const int CubeGameNumber = GamesBeforeTheCubeGame + 1;

    /// <summary>
    /// The move number the locator must show. A cube decision carries the
    /// number of the play it precedes, so with
    /// <see cref="PlaysBeforeTheCube"/> already recorded in that game the cube
    /// sits at the next one.
    /// </summary>
    internal static int CubeMoveNumber => PlaysBeforeTheCube.Length + 1;

    /// <summary>
    /// The match as XG binary bytes: <see cref="GamesBeforeTheCubeGame"/>
    /// complete-looking games, then the game carrying the file's two analysed
    /// decisions, both commented — a cube by <see cref="XgPlayer.Player2"/>,
    /// doubled and taken, and then that player's checker play.
    ///
    /// <para>
    /// <b>The cube comes first, deliberately.</b> File order is quiz order
    /// with shuffle off, so every scenario over this fixture meets the cube
    /// first — the widest answer row there is, which the locator scenarios
    /// need, at the coordinates they pin — and only a scenario that answers
    /// and continues ever reaches the play.
    /// </para>
    /// </summary>
    internal static byte[] Bytes()
    {
        var builder = XgFileBuilder.ForMatch(MatchLength, Player1, Player2);

        for (int i = 0; i < GamesBeforeTheCubeGame; i++)
            Replay(builder.AddGame());

        var cubeGame = builder.AddGame(CubeGameScore1, CubeGameScore2);
        Replay(cubeGame);
        cubeGame.CubeDecision(
            XgPlayer.Player2, CubeEquities, CubePly,
            doublerAction: CubeAction.Double, takerAction: CubeAction.Take,
            comment: CubeComment);
        cubeGame.Play(XgPlayer.Player2, PlayAfterTheCube.Dice, PlayAfterTheCube.Play, comment: PlayComment);

        return XgFileWriter.ToBytes(builder.Build());
    }

    /// <summary>Plays <see cref="PlaysBeforeTheCube"/> into <paramref name="game"/>.</summary>
    private static void Replay(XgGameBuilder game)
    {
        foreach (var (player, dice, play) in PlaysBeforeTheCube)
            game.UnanalysedPlay(player, dice, play);
    }
}
