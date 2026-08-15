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
        PickedProblemFolder picked, ShuffleOption shuffle) =>
        PickedFolderSourceFactory.Create(
            picked, shuffle, NullLoggerFactory.Instance, TimeProvider.System);

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
            FactoryOver(picked, new ShuffleOption()),
            new FakeProblemStatsSink(),
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

        var factory = FactoryOver(picked, shuffle);
        var drawn = await CollectAsync(factory(new FilterConfig().Build(), QuizMix.Empty));

        Assert.NotEmpty(drawn);
        var xgids = drawn.Select(d => d.Xgid).ToList();
        Assert.Equal(xgids.Count, xgids.Distinct(StringComparer.Ordinal).Count());
    }

    // -----------------------------------------------------------------------
    //  Which copy survives
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FirstOccurrenceSurvives_AcrossTheTwoNamedCopies()
    {
        var picked = HolderOverTwoCopies();

        // Name the two ids dedupe arbitrates between: one contested position,
        // its copy from each named file. The pick's file order is the parse
        // order, so the first-named file holds the first occurrence.
        var undeduped = await UndedupedAsync(picked, EarlyMoves);
        var contested = undeduped
            .GroupBy(d => d.Xgid, StringComparer.Ordinal)
            .First(g => g.Count() == 2);
        Assert.Equal(FirstCopyName, contested.ElementAt(0).Id.Filename);
        Assert.Equal(SecondCopyName, contested.ElementAt(1).Id.Filename);

        // First occurrence, unconditionally — and unconditional is the claim:
        // the survivor once had to be the copy carrying lifetime stats, because
        // stats were keyed by file-relative id and dropping the wrong copy
        // emptied the mix pool that history fed. Content-keyed stats make every
        // copy read and write the same record, so that seam is deleted rather
        // than satisfied here (SPEC-stats-identity.md §4) and nothing about the
        // lifetime record can move this outcome.
        var factory = FactoryOver(picked, new ShuffleOption());
        var drawn = await CollectAsync(factory(EarlyMoves.Build(), QuizMix.Empty));
        var survivor = Assert.Single(
            drawn, d => string.Equals(d.Xgid, contested.Key, StringComparison.Ordinal));
        Assert.Equal(FirstCopyName, survivor.Id.Filename);
    }
}
