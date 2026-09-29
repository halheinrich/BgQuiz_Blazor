using System.Collections.Immutable;
using BgDataTypes_Lib;
using BgGame_Lib;
using BgFolderAccess_Razor;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        Assert.NotNull(folder.ParsedDecisions);

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
        Assert.Null(folder.ParsedDecisions);

        await CollectAllAsync(MakeSource(folder, loggerFactory: parses));

        Assert.Equal(1, parses.CountFor("b.xgp"));
        Assert.NotNull(folder.ParsedDecisions);
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
        Assert.Null(folder.ParsedDecisions);        // B's cache untouched by A's parse
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
        var cache = folder.ParsedDecisions;

        // A second Start with an impossible filter reuses the same parse and
        // yields nothing — the filter ran over the cache, not the bytes: the
        // holder still holds the first parse's own array.
        var impossible = new FilterConfig { Players = { "zzz_no_such_player_zzz" } }.Build();
        var filtered = await CollectAllAsync(MakeSource(folder, impossible));

        Assert.Empty(filtered);
        Assert.True(cache == folder.ParsedDecisions, "the second Start re-parsed instead of reusing the cache");
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

    /// <summary>
    /// A logger factory that counts the parse's per-file skip warnings. The pins
    /// hand the source unparseable bytes, which the parse logs once per file per
    /// parse and skips, so a file's warning count is how many times it was
    /// parsed. The warning's state carries the file name as its <c>File</c>
    /// value (the iterator's own structured argument), which is what lets a pin
    /// tell two picks' parses apart.
    /// </summary>
    private sealed class ParseCounter : ILoggerFactory
    {
        private readonly List<string?> _files = [];

        /// <summary>Every parse counted, whichever file.</summary>
        public int Count => _files.Count;

        /// <summary>How many times <paramref name="fileName"/> was parsed.</summary>
        public int CountFor(string fileName) => _files.Count(f => f == fileName);

        public ILogger CreateLogger(string categoryName) => new Counting(this);

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }

        private sealed class Counting(ParseCounter owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel != LogLevel.Warning) return;
                var file = state is IReadOnlyList<KeyValuePair<string, object?>> values
                    ? values.FirstOrDefault(v => v.Key == "File").Value as string
                    : null;
                owner._files.Add(file);
            }
        }
    }
}
