using BgDataTypes_Lib;
using BgFolderAccess_Razor;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The position-dedupe layer through the real source composition (umbrella issue
/// <c>halheinrich/backgammon#84</c>): a quiz never serves the same position
/// twice, wherever the copies came from.
///
/// <para>
/// <b>The repro is parse-level, which is why it is owed here.</b>
/// <see cref="DecisionId"/> is file-relative, so the very same match under two
/// filenames yields content-identical decisions carrying <i>distinct</i> ids —
/// nothing upstream of a real parse can reproduce that, and every layer above
/// dedupes by id at best. So these tests stream one fixture file's bytes
/// twice under two source-file names and run them through
/// <see cref="PickedFolderSourceFactory"/>, the composition
/// <c>Program.cs</c> registers. Pre-fix this failed 100% deterministically.
/// </para>
///
/// <para>
/// <b>Why a named fixture and not the corpus.</b> <c>TestData/xg</c> rotates,
/// and the duplicate files that triggered the report have since been deleted
/// from it — a repro pinned there would evaporate. <c>TestData/FixtureFiles</c>
/// is append-only precisely so tests may name a file in it, so a missing
/// fixture is a loud failure here rather than a skip: the skip-if-absent
/// discipline belongs to the rotating corpus alone, and applying it to a repro
/// would let the repro quietly stop existing.
/// </para>
/// </summary>
[Trait("Category", "RequiresFixtureFiles")]
public class PositionDedupeTests
{
    /// <summary>The fixture file whose bytes are re-streamed under two names.</summary>
    private const string FixtureName = "match_40296079.xg";

    /// <summary>
    /// The two source-file names the fixture's bytes arrive under — the shape a
    /// re-downloaded match takes in a real problem folder, which is how the
    /// duplicate-position report was raised.
    /// </summary>
    private const string FirstCopyName = "match_40296079.xg";
    private const string SecondCopyName = "match_40296079 (1).xg";

    /// <summary>Move ≤ 5 — the early-game slice, small and dense in repeated openings.</summary>
    private static FilterConfig EarlyMoves => new() { MoveNumberMax = 5 };

    private static string FixturePath =>
        Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "TestData", "FixtureFiles", FixtureName));

    /// <summary>
    /// One fixture's bytes as two picked files under different names — content
    /// identical, ids distinct. Throws when the fixture is absent; see the class
    /// remarks for why this must not skip.
    /// </summary>
    private static IReadOnlyList<PickedFile> TwoNamedCopies()
    {
        if (!File.Exists(FixturePath))
        {
            throw new FileNotFoundException(
                $"The issue #84 duplicate-position repro needs the FixtureFiles fixture '{FixtureName}'. " +
                "TestData/FixtureFiles is append-only so pinned tests may name files in it; this test " +
                "fails loudly rather than skipping, because a repro that skips is a repro that has " +
                "stopped existing.",
                FixturePath);
        }

        var bytes = File.ReadAllBytes(FixturePath);
        return [new PickedFile(FirstCopyName, bytes), new PickedFile(SecondCopyName, bytes)];
    }

    /// <summary>A holder standing on the two copies, as a landed pick would leave it.</summary>
    private static PickedProblemFolder HolderOverTwoCopies()
    {
        var picked = new PickedProblemFolder();
        picked.Set("duplicates", TwoNamedCopies(), FolderWriteCapability.BrowserUnsupported, []);
        return picked;
    }

    private static ProblemSetSourceFactory FactoryOver(
        PickedProblemFolder picked, ShuffleOption shuffle, IDecisionStatsSink stats) =>
        PickedFolderSourceFactory.Create(
            picked, shuffle, stats, NullLoggerFactory.Instance, TimeProvider.System);

    /// <summary>
    /// What the composition's innermost layer yields — the undeduped, filtered
    /// decisions. Used to establish each test's premise (that the pick really
    /// does hold content-equal copies) and to name the ids dedupe arbitrates
    /// between.
    /// </summary>
    private static Task<List<BgDecisionData>> UndedupedAsync(
        PickedProblemFolder picked, FilterConfig config) =>
        CollectAsync(new CachedProblemSetSource(
            picked, config.Build(), NullLoggerFactory.Instance, TimeProvider.System));

    private static async Task<List<BgDecisionData>> CollectAsync(IProblemSetSource src)
    {
        var items = new List<BgDecisionData>();
        await foreach (var d in src.EnumerateAsync())
            items.Add(d);
        return items;
    }

    // -----------------------------------------------------------------------
    //  The repro: a filtered, capless quiz
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FilteredCaplessQuiz_OverTwoNamedCopies_ServesNoPositionTwice()
    {
        var picked = HolderOverTwoCopies();

        // The premise, asserted rather than assumed: without the dedupe layer
        // this pick genuinely serves positions twice. If a future fixture or
        // parser change made the copies distinguishable, the test below would
        // start passing for a reason that has nothing to do with #84 — so the
        // duplication itself is pinned first.
        var undeduped = await UndedupedAsync(picked, EarlyMoves);
        Assert.True(
            undeduped.Count > undeduped.Select(d => d.Xgid).Distinct(StringComparer.Ordinal).Count(),
            "Premise broken: the two copies no longer yield content-equal decisions, so this " +
            "test can no longer observe the #84 duplication it exists to pin.");

        var controller = new QuizController(
            FactoryOver(picked, new ShuffleOption(), new FakeDecisionStatsSink()),
            new FakeDecisionStatsSink(),
            TimeProvider.System);

        Assert.Equal(QuizStartOutcome.Started, await controller.StartAsync(EarlyMoves, QuizMix.Empty));

        // Drain the whole quiz through the surface a user drives — no cap, so
        // "what the quiz serves" is the entire deduped pool. Skipping is the
        // one advance that needs no answer.
        var served = new List<string>();
        while (!controller.IsFinished)
        {
            served.Add(controller.Current!.Xgid);
            await controller.SkipCurrentAsync();
            Assert.True(served.Count <= undeduped.Count, "The quiz served more problems than the pool holds.");
        }

        Assert.NotEmpty(served);
        Assert.Equal(served.Count, served.Distinct(StringComparer.Ordinal).Count());
    }

    // -----------------------------------------------------------------------
    //  The passthrough path — the mechanism was mix-independent, so the fix is
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlankMixQuiz_OverTwoNamedCopies_DrawsNoPositionTwice(bool shuffled)
    {
        // A blank mix wires no composition layer at all, so what the factory
        // returns *is* the quiz's stream — plain or shuffled. Dedupe sits
        // beneath both, which is the point: one rule for every quiz mode.
        var picked = HolderOverTwoCopies();
        var shuffle = new ShuffleOption();
        if (shuffled) shuffle.Set(true);

        var undeduped = await UndedupedAsync(picked, new FilterConfig());
        Assert.True(
            undeduped.Count > undeduped.Select(d => d.Xgid).Distinct(StringComparer.Ordinal).Count(),
            "Premise broken: the two copies no longer yield content-equal decisions.");

        var factory = FactoryOver(picked, shuffle, new FakeDecisionStatsSink());
        var drawn = await CollectAsync(factory(new FilterConfig().Build(), QuizMix.Empty));

        Assert.NotEmpty(drawn);
        var xgids = drawn.Select(d => d.Xgid).ToList();
        Assert.Equal(xgids.Count, xgids.Distinct(StringComparer.Ordinal).Count());
    }

    // -----------------------------------------------------------------------
    //  Survivor preference through the wire
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SurvivorPreference_TheStatsBearingCopySurvives()
    {
        var picked = HolderOverTwoCopies();

        // Name the two ids dedupe has to arbitrate between: one contested
        // position, its copy from each named file. First occurrence is the
        // first-named file's, since the pick's file order is the parse order.
        var undeduped = await UndedupedAsync(picked, EarlyMoves);
        var contested = undeduped
            .GroupBy(d => d.Xgid, StringComparer.Ordinal)
            .First(g => g.Count() == 2);
        var fromFirstCopy = contested.ElementAt(0).Id;
        var fromSecondCopy = contested.ElementAt(1).Id;
        Assert.Equal(FirstCopyName, fromFirstCopy.Filename);
        Assert.Equal(SecondCopyName, fromSecondCopy.Filename);

        // One sink and one factory across both enumerations: the predicate reads
        // the sink live, so changing the document between draws is exactly the
        // Restart-after-stats-changed case, re-arbitrated against current state
        // rather than a snapshot taken at wiring time.
        var sink = new FakeDecisionStatsSink();
        var factory = FactoryOver(picked, new ShuffleOption(), sink);

        // No document bound: no id is preferred, so first occurrence survives.
        Assert.Equal(fromFirstCopy, await SurvivorOfAsync(factory, contested.Key));

        // Stage the lifetime record on the *second-named* copy — the one
        // first-wins would have dropped, silently emptying the mix pool that
        // history belongs to. It survives instead, so the user's GotWrong
        // history still has a position behind it.
        sink.CurrentDocument = DocumentWithRecordFor(fromSecondCopy);
        Assert.Equal(fromSecondCopy, await SurvivorOfAsync(factory, contested.Key));
    }

    /// <summary>The id of the single decision the composed source yields for <paramref name="xgid"/>.</summary>
    private static async Task<DecisionId> SurvivorOfAsync(ProblemSetSourceFactory factory, string xgid)
    {
        var drawn = await CollectAsync(factory(EarlyMoves.Build(), QuizMix.Empty));
        return Assert.Single(drawn, d => string.Equals(d.Xgid, xgid, StringComparison.Ordinal)).Id;
    }

    /// <summary>
    /// A lifetime-stats document holding one wrong sighting of
    /// <paramref name="id"/> — "this id has history", which is all the survivor
    /// predicate asks. Wrong rather than correct because the history that makes
    /// dropping a copy costly is the history that feeds the GotWrong pool.
    /// </summary>
    private static DecisionStatsDocument DocumentWithRecordFor(DecisionId id) =>
        DecisionStatsDocument.FromStats(
            [new DecisionStats(id, new ScoreSegment(Submitted: 1, Correct: 0, TotalEquityLoss: 0.042), Quizzed)]);

    /// <summary>A fixed fold timestamp — nothing here reads ambient time.</summary>
    private static readonly DateTimeOffset Quizzed = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
}
