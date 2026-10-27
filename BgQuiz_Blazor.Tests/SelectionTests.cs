using BgDataTypes_Lib;
using BgDataTypes_Lib.TestSupport;
using BgFolderAccess_Razor;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The selection's filter and the error it hands the by-error order: one filter set, read
/// for both, under the selection's ranking (SPEC-filtering.md §7, §8;
/// halheinrich/backgammon#440, halheinrich/backgammon#423).
/// </summary>
public class SelectionTests
{
    /// <summary>Alice doubled at a doubler's error of 0.10 and Bob passed at a taker's of 0.30.</summary>
    private static CubeDecision TakerErred(int away = 0) =>
        TestFixtures.RecordedCubeDecision(0.8, 0.7, CubeAction.Double, CubeAction.Pass, away: away);

    /// <summary>Alice doubled at a doubler's error of 0.30 and Bob took, correctly.</summary>
    private static CubeDecision DoublerErred(int away = 0) =>
        TestFixtures.RecordedCubeDecision(1.0, 0.7, CubeAction.Double, CubeAction.Take, away: away);

    private static async Task<Selection> SelectAsync(
        FilterConfig config, PlayRanking ranking, params BgDecisionData[] problems)
    {
        var setup = new QuizSetup(
            new FakeFolderContext("Corpus", FolderWriteCapability.Enabled),
            TestFixtures.Sample([TestFixtures.ReadableXg()]),
            new LoadedProblems([.. problems], TestFixtures.WalkedReport(), forcedPlaysSetAside: 0),
            StatsProbe.None,
            FilterSourceToken.FromGeneration(1));
        return await Selection.ComputeAsync(
            setup, config, ranking, new CooperativeYielder(new ManualTimeProvider()), CancellationToken.None);
    }

    private static Task<Selection> SelectAsync(FilterConfig config, params BgDecisionData[] problems) =>
        SelectAsync(config, PlayRanking.Equity, problems);

    [Fact]
    public async Task ANameFacet_AdmitsTheListedTakersDouble_AndNotTheOpponentsNoDouble()
    {
        var doubled = TakerErred(away: 0);
        var undoubled = TestFixtures.RecordedCubeDecision(0.5, 0.7, CubeAction.NoDouble, takerAction: null, away: 5);
        Assert.NotEqual(ProblemKey.From(doubled), ProblemKey.From(undoubled));
        Assert.Equal(2, (await SelectAsync(new FilterConfig(), doubled, undoubled)).Pool.Count);   // both survive unfiltered

        var selection = await SelectAsync(new FilterConfig { Players = ["Bob"] }, doubled, undoubled);

        Assert.Same(doubled, Assert.Single(selection.Pool));
    }

    [Fact]
    public async Task RecordedError_WithTheTakerListed_IsTheTakersError()
    {
        var record = TakerErred();
        Assert.Equal(0.10, record.Decision.DoublerActionError(CubeAction.Double), 10);
        Assert.Equal(0.30, record.Decision.TakerActionError(CubeAction.Pass), 10);

        var selection = await SelectAsync(new FilterConfig { Players = ["Bob"] }, record);

        Assert.Equal(0.30, selection.RecordedError(record)!.Value, 10);
    }

    [Fact]
    public async Task RecordedError_WithNoNames_IsTheDoublersError()
    {
        var record = TakerErred();

        var selection = await SelectAsync(new FilterConfig(), record);

        Assert.Equal(0.10, selection.RecordedError(record)!.Value, 10);
    }

    [Fact]
    public async Task RecordedError_WithBothPlayersListed_IsTheLargerQualifyingError()
    {
        var takerLarger = TakerErred(away: 0);      // doubler 0.10, taker 0.30
        var doublerLarger = DoublerErred(away: 5);  // doubler 0.30, taker 0.00
        Assert.Equal(0.0, doublerLarger.Decision.TakerActionError(CubeAction.Take), 10);

        var selection = await SelectAsync(new FilterConfig { Players = ["Alice", "Bob"] }, takerLarger, doublerLarger);

        Assert.Equal(2, selection.Pool.Count);
        Assert.Equal(0.30, selection.RecordedError(takerLarger)!.Value, 10);
        Assert.Equal(0.30, selection.RecordedError(doublerLarger)!.Value, 10);
    }

    [Fact]
    public async Task RecordedError_RefusesNull()
    {
        var selection = await SelectAsync(new FilterConfig(), TakerErred());

        Assert.Throws<ArgumentNullException>(() => selection.RecordedError(null!));
    }

    /// <summary>
    /// The fixture of the BgGame_Lib test 5e replaced (546f8f5,
    /// <c>ByError_ReadsTheErrorUnderTheGivenRanking</c>): a 2-ply candidate rated 0.10 and
    /// the user's 3-ply play at 0.05, so the play errs by 0.05 under equity and is the
    /// best, at no error, depth first.
    /// </summary>
    private static CheckerPlayDecision RankingDependent() =>
        TestRecords.CheckerPlay(
            id: new XgDecisionId("a.xg", Game: 1, MoveNumber: 1, IsCube: false),
            decision: TestRecords.CheckerPlayData(
                plays:
                [
                    TestRecords.Candidate(analysisLevel: AnalysisLevel.Ply2, equity: 0.10),
                    TestRecords.Candidate(play: [new(13, 10), new(6, 5)], analysisLevel: AnalysisLevel.Ply3, equity: 0.05),
                ],
                userPlayIndex: 1));

    /// <summary>The same fixture's other record: an unlisted play at 0.03, at another score so it is a problem of its own.</summary>
    private static CheckerPlayDecision Unstated() =>
        TestRecords.CheckerPlay(
            id: new XgDecisionId("b.xg", Game: 1, MoveNumber: 1, IsCube: false),
            position: TestRecords.Position(session: TestRecords.MatchSession(length: 5, onRollNeeds: 5, opponentNeeds: 5)),
            decision: TestRecords.CheckerPlayData(userPlayIndex: null, unlistedPlayError: 0.03));

    [Theory]
    [InlineData(PlayRanking.Equity, 0.05, true)]
    [InlineData(PlayRanking.DepthFirst, 0.0, false)]
    public async Task RecordedError_ReadsUnderTheSelectionsRanking_AndTheOrderFollows(
        PlayRanking ranking, double expected, bool rankingDependentFirst)
    {
        var rankingDependent = RankingDependent();
        var unstated = Unstated();
        Assert.NotEqual(ProblemKey.From(rankingDependent), ProblemKey.From(unstated));

        var selection = await SelectAsync(new FilterConfig(), ranking, rankingDependent, unstated);

        Assert.Equal(2, selection.Pool.Count);
        Assert.Equal(expected, selection.RecordedError(rankingDependent)!.Value, 10);
        Assert.Equal(0.03, selection.RecordedError(unstated)!.Value, 10);
        var sequence = await ProblemSequence.BuildAsync(
            selection.Pool, QuizMix.Empty, ProblemOrder.ByErrorLargestFirst, selection.RecordedError,
            stats: null, DateTimeOffset.UnixEpoch, new Random(0), new CooperativeYielder(new ManualTimeProvider()));
        Assert.Equal(
            rankingDependentFirst ? [rankingDependent.Id, unstated.Id] : [unstated.Id, rankingDependent.Id],
            sequence.Problems.Select(problem => problem.Id));
    }
}
