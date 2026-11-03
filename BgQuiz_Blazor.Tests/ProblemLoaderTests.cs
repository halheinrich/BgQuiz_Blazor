using System.Collections.Immutable;
using BgDataTypes_Lib;
using BgFolderAccess_Razor;
using BgQuiz_Blazor.Client.Quiz;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// What a pick survives, read off the real walk (halheinrich/backgammon#514): the loader keeps
/// every decision the walk yields, and what the walk leaves out is its report's — a file it
/// could not read, a game whose standing the session refuses, a play holding a corrupt
/// candidate, and the forced plays it passes by. A forced play and an illegal play are no
/// problem (Hal's rulings), so neither is an omission. The files are built through the
/// producer's builder and its test support (<see cref="XgFileEdits"/>), so nothing here
/// fabricates a report.
/// </summary>
public class ProblemLoaderTests
{
    private static Task<LoadedProblems> LoadAsync(params PickedFile[] files) =>
        ProblemLoader.LoadAsync([.. files], NullLoggerFactory.Instance, new ManualTimeProvider(), progress: null, CancellationToken.None);

    /// <summary>The identifiers of the loaded problems <paramref name="keep"/> holds, every one by default, in load order.</summary>
    private static DecisionId[] Ids(LoadedProblems loaded, Func<BgDecisionData, bool>? keep = null) =>
        [.. loaded.Problems.Where(keep ?? (_ => true)).Select(problem => problem.Id)];

    [Fact]
    public async Task ARefusedGameBetweenValidOnes_LeavesTheirDecisions_UnderTheirOwnIdentifiers_AndIsOnTheReport()
    {
        var match = TestFixtures.OpeningMatch(games: 3);
        var whole = await LoadAsync(TestFixtures.PickedXg(match, "match.xg"));
        var loaded = await LoadAsync(TestFixtures.PickedXg(XgFileEdits.WithRefusedGames(match, 2), "match.xg"));

        Assert.Equal(6, whole.Problems.Length);   // the premise: three games of two decisions
        Assert.Equal(Ids(whole, problem => problem.Game != 2), Ids(loaded));
        var omission = Assert.Single(loaded.Report.Omissions);
        Assert.Equal(("match.xg", 2, true, XgOmissionKind.InvalidStanding),
            (omission.SourceFile, omission.Game, omission.IsWholeGame, omission.Kind));
        Assert.Empty(loaded.Report.Rejected);
        Assert.Equal(0, loaded.Report.ForcedPlayCount);
    }

    [Fact]
    public async Task ACorruptCandidate_LeavesTheDecisionsAroundIt_UnderTheirOwnIdentifiers_AndIsOnTheReport()
    {
        var match = TestFixtures.OpeningMatch(games: 2);
        var whole = await LoadAsync(TestFixtures.PickedXg(match, "match.xg"));
        var loaded = await LoadAsync(TestFixtures.PickedXg(XgFileEdits.WithCorruptCandidate(match, game: 2, move: 1), "match.xg"));

        Assert.Equal(Ids(whole, problem => problem is not { Game: 2, MoveNumber: 1 }), Ids(loaded));
        Assert.Equal(3, loaded.Problems.Length);
        var omission = Assert.Single(loaded.Report.Omissions);
        Assert.Equal(("match.xg", 2, 1, XgOmissionKind.CorruptCandidate),
            (omission.SourceFile, omission.Game, omission.MoveNumber, omission.Kind));
        Assert.Empty(loaded.Report.Rejected);
    }

    [Fact]
    public async Task AnIllegalPlay_LeavesTheDecisionsAroundIt_WithNoOmissionAndNoForcedCount()
    {
        var match = TestFixtures.Match(game => game
            .Play(XgPlayer.Player1, new DiceRoll(3, 1), Play.Create(new(8, 5), new(6, 5)))
            .IllegalPlay(XgPlayer.Player2, new DiceRoll(6, 5))
            .Play(XgPlayer.Player1, new DiceRoll(4, 2), Play.Create(new(8, 4), new(6, 4))));

        var loaded = await LoadAsync(TestFixtures.PickedXg(match, "illegal.xg"));

        Assert.Equal([1, 3], loaded.Problems.Select(problem => problem.MoveNumber));
        Assert.Empty(loaded.Report.Omissions);
        Assert.Equal(0, loaded.Report.ForcedPlayCount);
    }

    [Fact]
    public async Task AForcedPlay_IsNoProblem_ButCountedOnce_AndNoOmission()
    {
        var loaded = await LoadAsync(TestFixtures.PickedXg(TestFixtures.Match(game => game.WithForcedPlay()), "forced.xg"));

        Assert.Empty(loaded.Problems);
        Assert.Equal(1, loaded.Report.ForcedPlayCount);
        Assert.Empty(loaded.Report.Omissions);
        Assert.False(loaded.Report.AllRejected);
    }

    [Fact]
    public async Task ADance_IsCountedAsForced()
    {
        // Hal's ruling: a dance is forced, with no kind of its own — the generator states no
        // legal move as one empty play — so the count holds it beside a forced play.
        var loaded = await LoadAsync(TestFixtures.PickedXg(
            TestFixtures.Match(game => game.WithForcedPlay(), game => game.WithDance()), "forced.xg"));

        Assert.Empty(loaded.Problems);
        Assert.Equal(2, loaded.Report.ForcedPlayCount);
        Assert.Empty(loaded.Report.Omissions);
    }

    [Fact]
    public async Task EveryGameOmitted_LeavesNoProblem_TheOmissionsOnTheReport_AndTheFileRead()
    {
        var loaded = await LoadAsync(TestFixtures.PickedXg(
            XgFileEdits.WithRefusedGames(TestFixtures.OpeningMatch(games: 2), 1, 2), "refused.xg"));

        Assert.Empty(loaded.Problems);
        Assert.Equal([1, 2], loaded.Report.Omissions.Select(omission => omission.Game));
        Assert.All(loaded.Report.Omissions, omission => Assert.True(omission.IsWholeGame));
        // Decoded, not rejected: what the walk left out is content, and AllRejected is about
        // decoding alone (SourceReport's two kinds of fact).
        Assert.False(loaded.Report.AllRejected);
        Assert.Equal(1, loaded.Report.ReadableCount);
        Assert.Empty(loaded.Report.Rejected);
    }

    [Fact]
    public async Task ARefusedGameBesideADamagedFile_IsReportedBesideTheRejection_AndTheRestIsKept()
    {
        var match = TestFixtures.OpeningMatch(games: 3);
        var whole = await LoadAsync(TestFixtures.PickedXg(match, "refused.xg"));

        var loaded = await LoadAsync(
            TestFixtures.PickedXg(XgFileEdits.WithRefusedGames(match, 2), "refused.xg"),
            TestFixtures.DamagedXg("damaged.xg"));

        Assert.Equal(Ids(whole, problem => problem.Game != 2), Ids(loaded));
        Assert.Equal("damaged.xg", Assert.Single(loaded.Report.Rejected).SourceName);
        var omission = Assert.Single(loaded.Report.Omissions);
        Assert.Equal(("refused.xg", 2), (omission.SourceFile, omission.Game));
        Assert.Equal(2, loaded.Report.AttemptedCount);
        Assert.False(loaded.Report.AllRejected);
    }
}
