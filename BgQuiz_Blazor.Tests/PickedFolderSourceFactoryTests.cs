using System.Collections.Immutable;
using BgDataTypes_Lib;
using BgFolderAccess_Razor;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="PickedFolderSourceFactory"/> — the production source
/// composition. These call <c>Create</c> itself, so what they pin is what
/// <c>Program.cs</c> registers; they were previously written against a
/// hand-typed copy of the DI lambda, which is exactly the arrangement the named
/// type exists to end.
///
/// <para>
/// Like the corpus tests they assert shape-level invariants over the umbrella's
/// rotating <c>TestData/xg</c> corpus and skip cleanly when it is empty. The
/// position-dedupe layer's own behaviour needs content-equal copies to observe
/// at all, so it is pinned deterministically against a committed fixture in
/// <see cref="PositionDedupeTests"/>.
/// </para>
/// </summary>
public class PickedFolderSourceFactoryTests
{
    private static string CorpusDirectory =>
        Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "TestData", "xg"));

    /// <summary>Up to <paramref name="take"/> corpus files read into memory as picked files.</summary>
    private static ImmutableArray<PickedFile> CorpusFiles(int take = 3)
    {
        if (!Directory.Exists(CorpusDirectory)) return [];
        return
        [
            .. Directory.EnumerateFiles(CorpusDirectory, "*.xg")
                .Concat(Directory.EnumerateFiles(CorpusDirectory, "*.xgp"))
                .Take(take)
                .Select(p => new PickedFile(Path.GetFileName(p), [.. File.ReadAllBytes(p)])),
        ];
    }

    /// <summary>A holder standing on <paramref name="files"/>, as a landed pick would leave it.</summary>
    private static PickedProblemFolder HolderOver(ImmutableArray<PickedFile> files)
    {
        var picked = new PickedProblemFolder();
        picked.Set("corpus", files, FolderWriteCapability.BrowserUnsupported, []);
        return picked;
    }

    private static ProblemSetSourceFactory FactoryOver(
        PickedProblemFolder picked, ShuffleOption shuffle) =>
        PickedFolderSourceFactory.Create(
            picked, shuffle, NullLoggerFactory.Instance, TimeProvider.System);

    // -----------------------------------------------------------------------
    //  Argument validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_NullPicked_Throws() =>
        Assert.Throws<ArgumentNullException>(() => PickedFolderSourceFactory.Create(
            null!, new ShuffleOption(), NullLoggerFactory.Instance, TimeProvider.System));

    [Fact]
    public void Create_NullShuffle_Throws() =>
        Assert.Throws<ArgumentNullException>(() => PickedFolderSourceFactory.Create(
            new PickedProblemFolder(), null!, NullLoggerFactory.Instance, TimeProvider.System));

    [Fact]
    public void Create_NullLoggerFactory_Throws() =>
        Assert.Throws<ArgumentNullException>(() => PickedFolderSourceFactory.Create(
            new PickedProblemFolder(), new ShuffleOption(), null!, TimeProvider.System));

    [Fact]
    public void Create_NullClock_Throws() =>
        Assert.Throws<ArgumentNullException>(() => PickedFolderSourceFactory.Create(
            new PickedProblemFolder(), new ShuffleOption(), NullLoggerFactory.Instance, null!));

    // -----------------------------------------------------------------------
    //  Factory -> source -> controller wire
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FactoryShape_FeedsControllerStart()
    {
        // The whole path Program.cs wires: the registered composition over the
        // picked set drives QuizController.StartAsync to a first problem.
        var files = CorpusFiles();
        if (files.IsEmpty) return; // corpus may be empty in CI

        var factory = FactoryOver(HolderOver(files), new ShuffleOption());
        var controller = new QuizController(factory, new FakeProblemStatsSink(), TimeProvider.System);

        await controller.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        Assert.True(controller.HasStarted);
        // A real corpus yields at least one non-pass decision; if every decision
        // happened to be a pass the controller would finish, which is still a
        // valid started state.
        Assert.True(controller.Current is not null || controller.IsFinished);
    }

    [Fact]
    public async Task FactoryShape_ActiveMix_SuppressesShuffleWrap()
    {
        // Pins the arbitration rule the composition carries: an active mix owns
        // presentation order (its RandomOrder toggle), so the shuffle decorator
        // must not wrap under it — a shuffled inner would silently break
        // RandomOrder:false's source-order determinism. With shuffle ON but a
        // non-blank mix, the factory hands back the unshuffled stack:
        // enumeration order equals the shuffle-OFF order.
        var files = CorpusFiles();
        if (files.IsEmpty) return;

        var picked = HolderOver(files);
        var shuffle = new ShuffleOption();
        var factory = FactoryOver(picked, shuffle);
        var activeMix = new QuizMix([new QuizMixEntry(QuizCategory.EverythingElse, 100)]);

        // The baseline is the factory's own shuffle-OFF order rather than a bare
        // parse, so the comparison isolates the shuffle decorator from every
        // other layer in the stack. It has to be: the stack dedupes positions,
        // and the corpus repeats early positions across matches, so a raw-parse
        // baseline would differ here for a reason that has nothing to do with
        // shuffle arbitration.
        var plainOrder = await CollectAllAsync(factory(new DecisionFilterSet(), PlayRanking.Equity, QuizMix.Empty));
        if (plainOrder.Count < 2) return; // suppression unobservable over <2 items

        shuffle.Set(true);
        var underMixOrder = await CollectAllAsync(factory(new DecisionFilterSet(), PlayRanking.Equity, activeMix));

        Assert.Equal(plainOrder.Select(d => d.Id), underMixOrder.Select(d => d.Id));
    }

    [Fact]
    public async Task ShuffleDecorator_Enabled_WrapsSourceAndChangesOrder()
    {
        // The one test here that does NOT go through PickedFolderSourceFactory,
        // deliberately: observing a shuffle deterministically needs
        // ShuffledProblemSetSource's *seeded* ctor, and production uses the
        // unseeded one on purpose (reproducibility is a test-only concern) — so
        // pinning the wrap through the real composition could only be done with
        // a random permutation, i.e. a test that flakes when the shuffle lands
        // on the identity. The composition's own arbitration rule is pinned
        // against the real thing by the sibling test above; what this one adds
        // is that a wrapped source reorders at all.
        var files = CorpusFiles();
        if (files.IsEmpty) return;

        var picked = HolderOver(files);
        var shuffle = new ShuffleOption();
        ProblemSetSourceFactory seededFactory = (filters, ranking, mix) =>
        {
            IProblemSetSource inner = new CachedProblemSetSource(
                picked, filters, ranking, NullLoggerFactory.Instance, TimeProvider.System);
            return TestFixtures.Composed(
                mix.IsPassthrough && shuffle.Enabled ? new ShuffledProblemSetSource(inner, seed: 42) : inner);
        };

        var unshuffledOrder = await CollectAllAsync(seededFactory(new DecisionFilterSet(), PlayRanking.Equity, QuizMix.Empty));
        if (unshuffledOrder.Count < 2) return; // can't observe a shuffle over <2 items

        shuffle.Set(true);
        var shuffledOrder = await CollectAllAsync(seededFactory(new DecisionFilterSet(), PlayRanking.Equity, QuizMix.Empty));

        Assert.Equal(unshuffledOrder.Count, shuffledOrder.Count);
        Assert.NotEqual(unshuffledOrder, shuffledOrder); // order differs (seeded, so deterministic)
    }

    // -----------------------------------------------------------------------
    //  The ranking reaches the filter (SPEC-scoring.md §2a)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(PlayRanking.Equity, 1)]
    [InlineData(PlayRanking.DepthFirst, 0)]
    public async Task ErrorRange_ReadsThePlayersErrorUnderTheRankingTheFactoryIsHanded(
        PlayRanking ranking, int expected)
    {
        // "The problem filter's 'erred by more than x' uses the player's error
        // under the same setting" (SPEC-scoring.md §2a). The record's recorded
        // play is the rollout: under equity it lost 0.05 to the 3-ply best, so
        // an ErrorMin of 0.01 admits it; under depth first it IS the best, error
        // 0, and the filter refuses it. Driven through the production
        // composition — Create, over the real parse-once layer, whose cache is
        // seeded so it adopts the record instead of parsing bytes — so each row
        // fails if any layer between the delegate's argument and the filter
        // pass dropped the ranking for a default of its own: the DepthFirst row
        // would then admit the record, and a DepthFirst default would fail the
        // Equity row.
        var picked = HolderOver([new PickedFile("seeded.xg", [1])]);
        picked.StoreParsed(
            picked.PickGeneration, TestFixtures.Parsed(TestFixtures.DepthSplitDecision(recordedPlayIndex: 0)));
        var factory = FactoryOver(picked, new ShuffleOption());
        var erredMoreThanAHundredth = new FilterConfig { ErrorMin = 0.01 }.Build();

        var pool = await CollectAllAsync(factory(erredMoreThanAHundredth, ranking, QuizMix.Empty));

        Assert.Equal(expected, pool.Count);
    }

    // -----------------------------------------------------------------------
    //  The stack reports what its parse could not read (halheinrich/backgammon#368)
    // -----------------------------------------------------------------------

    /// <summary>A holder over the readable synthesized match and a truncation of it.</summary>
    private static PickedProblemFolder HolderOverOneReadableAndOneDamaged() =>
        HolderOver([TestFixtures.ReadableXg(), TestFixtures.DamagedXg()]);

    private static QuizController ControllerOver(PickedProblemFolder picked, ILoggerFactory? parses = null) =>
        new(PickedFolderSourceFactory.Create(
                picked, new ShuffleOption(), parses ?? NullLoggerFactory.Instance, TimeProvider.System),
            new FakeProblemStatsSink(), TimeProvider.System);

    [Fact]
    public async Task Count_OverAReadableAndADamagedFile_CountsTheReadable_AndNamesTheRejected()
    {
        // Through the production composition and the controller's own count:
        // the readable file's decisions are counted, the damaged file is named
        // with the read's own reason, and the report the summary carries is the
        // very one stored with the parse under the pick's generation.
        var picked = HolderOverOneReadableAndOneDamaged();

        var summary = await ControllerOver(picked).SummarizeMatchesAsync(new FilterConfig(), PlayRanking.Equity);

        Assert.True(summary.AnswerTypes.Total > 0, "the readable file's decisions were not counted");
        var rejection = Assert.Single(summary.Sources.Rejected);
        Assert.Equal("damaged.xg", rejection.SourceName);
        Assert.False(string.IsNullOrWhiteSpace(rejection.Reason.Message));
        Assert.Equal(1, summary.Sources.ReadableCount);
        Assert.False(summary.Sources.AllRejected);
        Assert.Same(summary.Sources, Assert.IsType<ParsedProblemSet>(picked.Parsed).Report);
    }

    [Fact]
    public async Task RecountAndStart_WalkNothingAgain_AndCarryTheSameFacts()
    {
        // The facts exist once per pick, at the parse: a second count and a
        // Start after it adopt the cached parse — one skip logged for the
        // damaged file in total — and every reader hands back the same report.
        var parses = new ParseCounter();
        var picked = HolderOverOneReadableAndOneDamaged();
        var controller = ControllerOver(picked, parses);

        var first = await controller.SummarizeMatchesAsync(new FilterConfig(), PlayRanking.Equity);
        var second = await controller.SummarizeMatchesAsync(new FilterConfig(), PlayRanking.DepthFirst);
        await controller.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        Assert.Equal(1, parses.CountFor("damaged.xg"));
        Assert.Same(first.Sources, second.Sources);
        Assert.Same(first.Sources, Assert.IsType<ParsedProblemSet>(picked.Parsed).Report);
        Assert.True(controller.HasStarted);
    }

    [Fact]
    public async Task NewPick_ReplacesTheFacts_AndLeavesTheOldSummarysUntouched()
    {
        var picked = HolderOverOneReadableAndOneDamaged();
        var controller = ControllerOver(picked);
        var before = await controller.SummarizeMatchesAsync(new FilterConfig(), PlayRanking.Equity);

        picked.Set("other", [TestFixtures.DamagedXg("other-damaged.xg")], FolderWriteCapability.BrowserUnsupported, []);
        var after = await controller.SummarizeMatchesAsync(new FilterConfig(), PlayRanking.Equity);

        Assert.Equal("other-damaged.xg", Assert.Single(after.Sources.Rejected).SourceName);
        Assert.True(after.Sources.AllRejected);
        Assert.Same(after.Sources, Assert.IsType<ParsedProblemSet>(picked.Parsed).Report);
        Assert.Equal("damaged.xg", Assert.Single(before.Sources.Rejected).SourceName); // a record of its own walk
    }

    [Fact]
    public async Task StalePicksStack_ReportsItsOwnWalk_NeverTheNewPicks()
    {
        // The stale-pick shape through the composed reader: a stack built
        // against pick A and enumerated after the re-pick to B reports A's walk
        // (A's damaged file), publishes nothing into B's holder, and never
        // pairs A's decisions with B's report; B's own parse then leaves A's
        // stack reporting A.
        var picked = HolderOver([TestFixtures.ReadableXg("a.xg"), TestFixtures.DamagedXg("a-damaged.xg")]);
        var factory = FactoryOver(picked, new ShuffleOption());
        var staleStack = factory(new DecisionFilterSet(), PlayRanking.Equity, QuizMix.Empty);

        picked.Set("other", [TestFixtures.DamagedXg("b-damaged.xg")], FolderWriteCapability.BrowserUnsupported, []);

        var decisions = await CollectAllAsync(staleStack);
        Assert.NotEmpty(decisions);
        Assert.All(decisions, d => Assert.Equal("a.xg", d.SourceFile));
        var staleReport = staleStack.GetSourceReport();
        Assert.NotNull(staleReport);
        Assert.Equal("a-damaged.xg", Assert.Single(staleReport.Rejected).SourceName);
        Assert.Null(picked.Parsed); // B's cache untouched by A's parse

        var current = await ControllerOver(picked).SummarizeMatchesAsync(new FilterConfig(), PlayRanking.Equity);

        Assert.Equal("b-damaged.xg", Assert.Single(current.Sources.Rejected).SourceName);
        Assert.Same(staleReport, staleStack.GetSourceReport());
        Assert.NotSame(current.Sources, staleStack.GetSourceReport());
    }

    [Fact]
    public async Task RejectedBesideReadableWithZeroMatches_IsNeverClassifiedAllRejected()
    {
        // A filter nothing passes over a readable file plus a damaged one: the
        // count is zero AND the record names the rejected file, but the
        // selection had a readable file, so AllRejected is false — the
        // no-match explanation and the partial record are both owed, and
        // neither is lost inside the other.
        var picked = HolderOverOneReadableAndOneDamaged();
        var impossible = new FilterConfig { Players = { "zzz_no_such_player_zzz" } };

        var summary = await ControllerOver(picked).SummarizeMatchesAsync(impossible, PlayRanking.Equity);

        Assert.Equal(0, summary.AnswerTypes.Total);
        Assert.False(summary.Sources.AllRejected);
        Assert.Equal("damaged.xg", Assert.Single(summary.Sources.Rejected).SourceName);
    }

    [Fact]
    public async Task EveryFileDamaged_CountsZero_AndSaysSo()
    {
        var picked = HolderOver([TestFixtures.DamagedXg("one.xg"), TestFixtures.DamagedXg("two.xg")]);

        var summary = await ControllerOver(picked).SummarizeMatchesAsync(new FilterConfig(), PlayRanking.Equity);

        Assert.Equal(0, summary.AnswerTypes.Total);
        Assert.True(summary.Sources.AllRejected);
        Assert.Equal(["one.xg", "two.xg"], summary.Sources.Rejected.Select(r => r.SourceName));
    }

    private static async Task<List<BgDecisionData>> CollectAllAsync(ComposedProblemSource composed)
    {
        var items = new List<BgDecisionData>();
        await foreach (var d in composed.Source.EnumerateAsync())
            items.Add(d);
        return items;
    }
}
