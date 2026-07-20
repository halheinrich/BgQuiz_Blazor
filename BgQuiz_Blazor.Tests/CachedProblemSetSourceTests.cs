using System.Collections;
using BgDataTypes_Lib;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="CachedProblemSetSource"/> — the parse-once layer:
/// first enumeration parses the pick unfiltered and caches the decisions on
/// the holder; every later enumeration (across Starts and Restarts) filters
/// the cached parse. Parse counting rides a counting
/// <see cref="IReadOnlyList{T}"/> over the picked files: the parse is the
/// only thing that enumerates the file list, so its enumeration count <i>is</i>
/// the parse count — no production seam needed. Corpus-shaped assertions
/// follow the fixture-agnostic <c>TestData/xg</c> rules and skip cleanly on
/// an empty corpus; the parse-count and staleness pins run corpus-free over
/// unparseable bytes (a per-file parse failure is logged and skipped, which
/// is all the counting needs).
/// </summary>
public class CachedProblemSetSourceTests
{
    private static string CorpusDirectory =>
        Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "TestData", "xg"));

    private static IReadOnlyList<PickedFile> CorpusFiles(int take = 3)
    {
        if (!Directory.Exists(CorpusDirectory)) return [];
        return Directory.EnumerateFiles(CorpusDirectory, "*.xg")
            .Concat(Directory.EnumerateFiles(CorpusDirectory, "*.xgp"))
            .Take(take)
            .Select(p => new PickedFile(Path.GetFileName(p), File.ReadAllBytes(p)))
            .ToList();
    }

    private static PickedProblemFolder FolderOver(IReadOnlyList<PickedFile> files)
    {
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", files, StatsSaveCapability.BrowserUnsupported);
        return folder;
    }

    private static CachedProblemSetSource MakeSource(
        PickedProblemFolder folder, DecisionFilterSet? filters = null) =>
        new(folder, filters ?? new DecisionFilterSet(), NullLoggerFactory.Instance, TimeProvider.System);

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
            new CachedProblemSetSource(null!, new DecisionFilterSet(), NullLoggerFactory.Instance, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() =>
            new CachedProblemSetSource(folder, null!, NullLoggerFactory.Instance, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() =>
            new CachedProblemSetSource(folder, new DecisionFilterSet(), null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() =>
            new CachedProblemSetSource(folder, new DecisionFilterSet(), NullLoggerFactory.Instance, null!));
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
        // must serve from the cache — one file-list enumeration total.
        var files = new CountingFileList([new PickedFile("a.xg", [1, 2, 3])]);
        var folder = FolderOver(files);

        await CollectAllAsync(MakeSource(folder));
        Assert.NotNull(folder.ParsedDecisions);

        await CollectAllAsync(MakeSource(folder));

        Assert.Equal(1, files.EnumerationCount);
    }

    [Fact]
    public async Task TwoEnumerationsOfOneSource_ParseOnce()
    {
        // The Restart shape: the controller re-enumerates the same source.
        var files = new CountingFileList([new PickedFile("a.xg", [1, 2, 3])]);
        var folder = FolderOver(files);
        var source = MakeSource(folder);

        await CollectAllAsync(source);
        await CollectAllAsync(source);

        Assert.Equal(1, files.EnumerationCount);
    }

    [Fact]
    public async Task ControllerStartTwice_ParsesOnce()
    {
        // The wire shape Program.cs registers: factory → CachedProblemSetSource
        // → controller. Two full Starts, one parse.
        var files = new CountingFileList([new PickedFile("a.xg", [1, 2, 3])]);
        var folder = FolderOver(files);
        ProblemSetSourceFactory factory = (filters, _) =>
            new CachedProblemSetSource(folder, filters, NullLoggerFactory.Instance, TimeProvider.System);
        var controller = new QuizController(factory, new FakeDecisionStatsSink(), TimeProvider.System);

        await controller.StartAsync(new FilterConfig(), QuizMix.Empty);
        await controller.StartAsync(new FilterConfig(), QuizMix.Empty);

        Assert.Equal(1, files.EnumerationCount);
    }

    [Fact]
    public async Task Repick_InvalidatesCache_NextSourceReparses()
    {
        var firstFiles = new CountingFileList([new PickedFile("a.xg", [1, 2, 3])]);
        var folder = FolderOver(firstFiles);
        await CollectAllAsync(MakeSource(folder));
        Assert.Equal(1, firstFiles.EnumerationCount);

        // Re-pick (same folder or not — every Set supersedes): the cache is
        // gone and the next Start's source parses the new files.
        var secondFiles = new CountingFileList([new PickedFile("b.xgp", [4, 5, 6])]);
        folder.Set("Corpus", secondFiles, StatsSaveCapability.BrowserUnsupported);
        Assert.Null(folder.ParsedDecisions);

        await CollectAllAsync(MakeSource(folder));

        Assert.Equal(1, secondFiles.EnumerationCount);
        Assert.NotNull(folder.ParsedDecisions);
        Assert.Equal(1, firstFiles.EnumerationCount); // the old pick was never re-read
    }

    [Fact]
    public async Task RepickAfterConstruction_SourceReplaysItsOwnFiles_WithoutPollutingNewPicksCache()
    {
        // A source built against pick A whose enumeration runs after a
        // re-pick to B (the pick gesture is async): it must still serve A —
        // the quiz that Start began — and its parse of A must not land as
        // B's cache. Its own reference makes a re-enumeration (Restart) of
        // the stale source parse A only once.
        var filesA = new CountingFileList([new PickedFile("a.xg", [1, 2, 3])]);
        var folder = FolderOver(filesA);
        var staleSource = MakeSource(folder);

        folder.Set("Other", [new PickedFile("b.xgp", [4, 5, 6])], StatsSaveCapability.BrowserUnsupported);

        await CollectAllAsync(staleSource);
        await CollectAllAsync(staleSource);

        Assert.Equal(1, filesA.EnumerationCount); // parsed its own files, once
        Assert.Null(folder.ParsedDecisions);      // B's cache untouched by A's parse
    }

    // -----------------------------------------------------------------------
    //  Filtering over the cached parse (corpus)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FiltersApplyPerEnumeration_OverTheOneCachedParse()
    {
        var corpus = CorpusFiles();
        if (corpus.Count == 0) return; // corpus may be empty in CI

        var files = new CountingFileList(corpus);
        var folder = FolderOver(files);

        var unfiltered = await CollectAllAsync(MakeSource(folder));
        if (unfiltered.Count == 0) return; // nothing showable in this corpus

        // A second Start with an impossible filter reuses the same parse and
        // yields nothing — the filter ran over the cache, not the bytes.
        var impossible = new FilterConfig { Players = { "zzz_no_such_player_zzz" } }.Build();
        var filtered = await CollectAllAsync(MakeSource(folder, impossible));

        Assert.Empty(filtered);
        Assert.Equal(1, files.EnumerationCount);
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
        if (corpus.Count == 0) return;

        var filters = new FilterConfig { DecisionType = DecisionTypeOption.CheckerPlaysOnly }.Build();

        var streamed = await CollectAllAsync(
            new WasmUploadedProblemSetSource(corpus, filters, NullLoggerFactory.Instance, TimeProvider.System));
        var cached = await CollectAllAsync(MakeSource(FolderOver(corpus), filters));

        Assert.Equal(streamed.Select(d => d.Id), cached.Select(d => d.Id));
    }

    /// <summary>
    /// File list that counts its enumerations. The parse is the only consumer
    /// that enumerates the picked files (naming reads only <see cref="Count"/>),
    /// so the count observed here is the parse count.
    /// </summary>
    private sealed class CountingFileList(IReadOnlyList<PickedFile> inner) : IReadOnlyList<PickedFile>
    {
        public int EnumerationCount { get; private set; }

        public PickedFile this[int index] => inner[index];
        public int Count => inner.Count;

        public IEnumerator<PickedFile> GetEnumerator()
        {
            EnumerationCount++;
            return inner.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
