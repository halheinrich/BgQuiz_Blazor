using System.Collections.Immutable;
using BgDataTypes_Lib;
using BgGame_Lib;
using BgFolderAccess_Razor;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="CachedProblemSetSource"/> — the parse-once layer:
/// first enumeration parses the pick unfiltered and caches the decisions on
/// the holder; every later enumeration (across Starts and Restarts) filters
/// the cached parse. Parse counting rides the logger the source is handed:
/// the pins run corpus-free over unparseable bytes, and a per-file parse
/// failure is logged and skipped — once per file per parse — so the warnings a
/// counting logger sees <i>are</i> the parse count, through the constructor's
/// own seam. (It used to ride a counting list of the picked files; the holder
/// keeps the pick's immutable array now, which no test can instrument.)
/// Corpus-shaped assertions follow the fixture-agnostic <c>TestData/xg</c>
/// rules and skip cleanly on an empty corpus. Every filter pass here runs under
/// an explicit ranking; which ranking decides what "erred by more than x"
/// admits is pinned in <c>PickedFolderSourceFactoryTests</c>.
/// </summary>
public class CachedProblemSetSourceTests
{
    private static string CorpusDirectory =>
        Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "TestData", "xg"));

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

    private static PickedProblemFolder FolderOver(ImmutableArray<PickedFile> files)
    {
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", files, FolderWriteCapability.BrowserUnsupported, []);
        return folder;
    }

    private static CachedProblemSetSource MakeSource(
        PickedProblemFolder folder, DecisionFilterSet? filters = null, ILoggerFactory? loggerFactory = null) =>
        new(folder, filters ?? new DecisionFilterSet(), PlayRanking.Equity,
            loggerFactory ?? NullLoggerFactory.Instance, TimeProvider.System);

    /// <summary>One unparseable file — its parse is one logged skip, which is what <see cref="ParseCounter"/> counts.</summary>
    private static ImmutableArray<PickedFile> Unparseable(string name) => [new PickedFile(name, [1, 2, 3])];

    private static async Task<List<BgDecisionData>> CollectAllAsync(IProblemSetSource src)
    {
        var items = new List<BgDecisionData>();
        await foreach (var d in src.EnumerateAsync())
            items.Add(d);
        return items;
    }

    // -----------------------------------------------------------------------
    //  Construction
    // -----------------------------------------------------------------------

    [Fact]
    public void Ctor_NullArguments_Throw()
    {
        var folder = FolderOver([new PickedFile("a.xg", [1])]);
        Assert.Throws<ArgumentNullException>(() =>
            new CachedProblemSetSource(null!, new DecisionFilterSet(), PlayRanking.Equity, NullLoggerFactory.Instance, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() =>
            new CachedProblemSetSource(folder, null!, PlayRanking.Equity, NullLoggerFactory.Instance, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() =>
            new CachedProblemSetSource(folder, new DecisionFilterSet(), PlayRanking.Equity, null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() =>
            new CachedProblemSetSource(folder, new DecisionFilterSet(), PlayRanking.Equity, NullLoggerFactory.Instance, null!));
    }

    [Fact]
    public void Ctor_UndefinedRanking_IsRefusedAtConstruction()
    {
        // Refused where the source is built — by the parsing iterator's own door
        // — rather than at the first view the filter pass would build, so a bad
        // ranking never reaches an enumeration that has already begun.
        var folder = FolderOver([new PickedFile("a.xg", [1])]);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CachedProblemSetSource(folder, new DecisionFilterSet(), (PlayRanking)99,
                NullLoggerFactory.Instance, TimeProvider.System));
    }

    [Fact]
    public void Name_DelegatesToTheInnerNamingRule()
    {
        Assert.Equal("match.xg", MakeSource(FolderOver([new PickedFile("match.xg", [])])).Name);
        Assert.Equal("2 files", MakeSource(FolderOver([new PickedFile("a.xg", []), new PickedFile("b.xgp", [])])).Name);
    }

    // -----------------------------------------------------------------------
    //  Parse-once (corpus-free: counting is byte-content-agnostic)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TwoSourcesOverOnePick_ParseOnce()
    {
        // The two-Starts shape: each Start builds a fresh source over the
        // same pick. The first parses and caches on the holder; the second
        // must serve from the cache — one parse total.
        var parses = new ParseCounter();
        var folder = FolderOver(Unparseable("a.xg"));

        await CollectAllAsync(MakeSource(folder, loggerFactory: parses));
        Assert.NotNull(folder.Parsed);

        await CollectAllAsync(MakeSource(folder, loggerFactory: parses));

        Assert.Equal(1, parses.Count);
    }

    [Fact]
    public async Task TwoEnumerationsOfOneSource_ParseOnce()
    {
        // The Restart shape: the controller re-enumerates the same source.
        var parses = new ParseCounter();
        var source = MakeSource(FolderOver(Unparseable("a.xg")), loggerFactory: parses);

        await CollectAllAsync(source);
        await CollectAllAsync(source);

        Assert.Equal(1, parses.Count);
    }

    [Fact]
    public async Task ControllerStartTwice_ParsesOnce()
    {
        // The wire shape Program.cs registers: factory → CachedProblemSetSource
        // → controller. Two full Starts, one parse.
        var parses = new ParseCounter();
        var folder = FolderOver(Unparseable("a.xg"));
        ProblemSetSourceFactory factory = (filters, ranking, _) => TestFixtures.Composed(
            new CachedProblemSetSource(folder, filters, ranking, parses, TimeProvider.System));
        var controller = new QuizController(factory, new FakeProblemStatsSink(), TimeProvider.System);

        await controller.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await controller.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.DepthFirst);

        // The second Start runs under the other ranking and still reuses the
        // parse: the cache holds records, which depend on no ranking.
        Assert.Equal(1, parses.Count);
    }

    [Fact]
    public async Task Repick_InvalidatesCache_NextSourceReparses()
    {
        var parses = new ParseCounter();
        var folder = FolderOver(Unparseable("a.xg"));
        await CollectAllAsync(MakeSource(folder, loggerFactory: parses));
        Assert.Equal(1, parses.CountFor("a.xg"));

        // Re-pick (same folder or not — every Set supersedes): the cache is
        // gone and the next Start's source parses the new files.
        folder.Set("Corpus", Unparseable("b.xgp"), FolderWriteCapability.BrowserUnsupported, []);
        Assert.Null(folder.Parsed);

        await CollectAllAsync(MakeSource(folder, loggerFactory: parses));

        Assert.Equal(1, parses.CountFor("b.xgp"));
        Assert.NotNull(folder.Parsed);
        Assert.Equal(1, parses.CountFor("a.xg")); // the old pick was never re-read
    }

    [Fact]
    public async Task RepickAfterConstruction_SourceReplaysItsOwnFiles_WithoutPollutingNewPicksCache()
    {
        // A source built against pick A whose enumeration runs after a
        // re-pick to B (the pick gesture is async): it must still serve A —
        // the quiz that Start began — and its parse of A must not land as
        // B's cache. Its own reference makes a re-enumeration (Restart) of
        // the stale source parse A only once.
        var parses = new ParseCounter();
        var folder = FolderOver(Unparseable("a.xg"));
        var staleSource = MakeSource(folder, loggerFactory: parses);

        folder.Set("Other", Unparseable("b.xgp"), FolderWriteCapability.BrowserUnsupported, []);

        await CollectAllAsync(staleSource);
        await CollectAllAsync(staleSource);

        Assert.Equal(1, parses.CountFor("a.xg")); // parsed its own files, once
        Assert.Equal(0, parses.CountFor("b.xgp")); // and never the new pick's
        Assert.Null(folder.Parsed);                 // B's cache untouched by A's parse
    }

    // -----------------------------------------------------------------------
    //  Filtering over the cached parse (corpus)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FiltersApplyPerEnumeration_OverTheOneCachedParse()
    {
        var corpus = CorpusFiles();
        if (corpus.IsEmpty) return; // corpus may be empty in CI

        var folder = FolderOver(corpus);

        var unfiltered = await CollectAllAsync(MakeSource(folder));
        if (unfiltered.Count == 0) return; // nothing showable in this corpus
        var cache = folder.Parsed;

        // A second Start with an impossible filter reuses the same parse and
        // yields nothing — the filter ran over the cache, not the bytes: the
        // holder still holds the first parse's own array.
        var impossible = new FilterConfig { Players = { "zzz_no_such_player_zzz" } }.Build();
        var filtered = await CollectAllAsync(MakeSource(folder, impossible));

        Assert.Empty(filtered);
        Assert.Same(cache, folder.Parsed); // the second Start re-parsed instead of reusing the cache otherwise
    }

    [Fact]
    public async Task FilteredCachedEnumeration_EqualsFilteredStreamedEnumeration()
    {
        // The equivalence the cache design rests on: per-decision Matches over
        // the unfiltered parse yields exactly what the streaming iterator
        // yields with the same filters wired in (the iterator's skip/advance
        // votes are contractually pure early-exit hints). Pinned shape-level
        // over the rotating corpus with a genuinely partitioning filter.
        var corpus = CorpusFiles();
        if (corpus.IsEmpty) return;

        var filters = new FilterConfig { DecisionType = DecisionTypeOption.CheckerPlaysOnly }.Build();

        var streamed = await CollectAllAsync(
            new WasmUploadedProblemSetSource(
                corpus, filters, PlayRanking.Equity, NullLoggerFactory.Instance, TimeProvider.System));
        var cached = await CollectAllAsync(MakeSource(FolderOver(corpus), filters));

        Assert.Equal(streamed.Select(d => d.Id), cached.Select(d => d.Id));
    }

    // -----------------------------------------------------------------------
    //  The parse's source report rides with the parse (halheinrich/backgammon#368)
    // -----------------------------------------------------------------------

    /// <summary>The suite's readable synthesized match as a picked file (<see cref="TestFixtures.ReadableXg"/>).</summary>
    private static PickedFile Readable(string name = "readable.xg") => TestFixtures.ReadableXg(name);

    /// <summary>A truncation of it the producer refuses (<see cref="TestFixtures.DamagedXg"/>).</summary>
    private static PickedFile Damaged(string name = "damaged.xg") => TestFixtures.DamagedXg(name);

    [Fact]
    public async Task Parse_StoresTheReportWithTheDecisions_NamingTheRejectedFile()
    {
        // A damaged file beside a readable one: the readable file's decisions
        // are the parse, and the report stored with them names the file that
        // was skipped, with the read's own exception as the reason — one
        // value, under the pick's generation.
        var folder = FolderOver([Readable(), Damaged()]);
        var source = MakeSource(folder);

        var decisions = await CollectAllAsync(source);

        var parsed = Assert.IsType<ParsedProblemSet>(folder.Parsed);
        Assert.NotEmpty(decisions);
        Assert.All(decisions, d => Assert.Equal("readable.xg", d.SourceFile));
        Assert.Equal(decisions.Count, parsed.Decisions.Length);

        var report = parsed.Report;
        Assert.True(report.IsComplete);
        Assert.Equal(2, report.AttemptedCount);
        Assert.Equal(1, report.ReadableCount);
        Assert.False(report.AllRejected);
        var rejection = Assert.Single(report.Rejected);
        Assert.Equal("damaged.xg", rejection.SourceName);
        Assert.False(string.IsNullOrWhiteSpace(rejection.Reason.Message));
        Assert.Same(report, source.Report); // the source's own reader reads the parse it holds
    }

    [Fact]
    public async Task OnlyFileDamaged_TheReportSaysAllRejected_AndTheParseHoldsNoDecisions()
    {
        var folder = FolderOver([Damaged("only.xg")]);

        var decisions = await CollectAllAsync(MakeSource(folder));

        Assert.Empty(decisions);
        var report = Assert.IsType<ParsedProblemSet>(folder.Parsed).Report;
        Assert.True(report.AllRejected);
        Assert.Equal("only.xg", Assert.Single(report.Rejected).SourceName);
    }

    [Fact]
    public async Task NoFilesAtAll_IsACompletedWalk_AndNotAllRejected()
    {
        // The empty selection: nothing attempted, so the producer's conclusion
        // is deliberately false — "no file could be read" is not what happened.
        var folder = FolderOver([]);

        await CollectAllAsync(MakeSource(folder));

        var report = Assert.IsType<ParsedProblemSet>(folder.Parsed).Report;
        Assert.True(report.IsComplete);
        Assert.Equal(0, report.AttemptedCount);
        Assert.False(report.AllRejected);
    }

    [Fact]
    public async Task CacheHit_ReusesTheCompletedReport_WithoutWalkingAgain()
    {
        // The second Start, the Restart and the re-count all adopt the
        // holder's parse: the same report object, and no second walk — the
        // damaged file is skipped (and so logged) exactly once.
        var parses = new ParseCounter();
        var folder = FolderOver([Readable(), Damaged()]);
        var first = MakeSource(folder, loggerFactory: parses);
        await CollectAllAsync(first);
        await CollectAllAsync(first); // Restart

        var second = MakeSource(folder, loggerFactory: parses);
        await CollectAllAsync(second); // the next Start, or a re-count

        Assert.Equal(1, parses.CountFor("damaged.xg"));
        Assert.Same(first.Report, second.Report);
        Assert.Same(folder.Parsed!.Report, second.Report);
    }

    [Fact]
    public async Task StaleSource_ReportsItsOwnWalk_NeverTheNewPicks()
    {
        // The stale-pick shape, extended through the report: a source built
        // against pick A whose enumeration runs after the re-pick to B must
        // report A's walk — A's damaged file — publish nothing into B's holder,
        // and never pair A's decisions with B's report; and B's own parse must
        // not touch what A's source reports.
        var folder = FolderOver([Readable("a.xg"), Damaged("a-damaged.xg")]);
        var staleSource = MakeSource(folder);

        folder.Set("Other", [Damaged("b-damaged.xg")], FolderWriteCapability.BrowserUnsupported, []);

        await CollectAllAsync(staleSource);
        var staleReport = Assert.IsType<SourceReport>(staleSource.Report);
        Assert.Equal("a-damaged.xg", Assert.Single(staleReport.Rejected).SourceName);
        Assert.Null(folder.Parsed); // B's cache untouched by A's parse

        await CollectAllAsync(MakeSource(folder));
        var current = Assert.IsType<ParsedProblemSet>(folder.Parsed);
        Assert.Equal("b-damaged.xg", Assert.Single(current.Report.Rejected).SourceName);
        Assert.True(current.Report.AllRejected);
        Assert.Same(staleReport, staleSource.Report); // A's source still reports A's walk
        Assert.NotSame(current.Report, staleSource.Report);
    }

    [Fact]
    public async Task InterruptedParse_StoresNothing_AndTheRetryWalksWithAFreshReport()
    {
        // A cancelled parse installs neither half: the holder stays unparsed
        // and the source reports nothing. The retry succeeds — which it could
        // not if the spent report were offered again, since the producer
        // refuses a report for a second walk — so the one-walk report is
        // demonstrably fresh per attempt, and a partial report is never
        // substituted for a completed one.
        var folder = FolderOver([Readable(), Damaged()]);
        var source = MakeSource(folder);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in source.EnumerateAsync(cancelled.Token)) { }
        });
        Assert.Null(folder.Parsed);
        Assert.Null(source.Report);

        var decisions = await CollectAllAsync(source);

        Assert.NotEmpty(decisions);
        var report = Assert.IsType<ParsedProblemSet>(folder.Parsed).Report;
        Assert.True(report.IsComplete);
        Assert.Same(report, source.Report);
    }
}
