using BgDataTypes_Lib;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="WasmUploadedProblemSetSource"/> — the in-browser,
/// stream-backed problem-set source that replaced the server-disk source. Like
/// the old directory tests these assert shape-level invariants over the
/// umbrella's rotating <c>TestData/xg</c> corpus (re-iterability, filter
/// application), never specific file contents, and skip cleanly when the corpus
/// is empty.
/// </summary>
public class WasmUploadedProblemSetSourceTests
{
    private static string CorpusDirectory =>
        Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "TestData", "xg"));

    /// <summary>Up to <paramref name="take"/> corpus files read into memory as picked files.</summary>
    private static IReadOnlyList<PickedFile> CorpusFiles(int take = 3)
    {
        if (!Directory.Exists(CorpusDirectory)) return [];
        return Directory.EnumerateFiles(CorpusDirectory, "*.xg")
            .Concat(Directory.EnumerateFiles(CorpusDirectory, "*.xgp"))
            .Take(take)
            .Select(p => new PickedFile(Path.GetFileName(p), File.ReadAllBytes(p)))
            .ToList();
    }

    private static WasmUploadedProblemSetSource MakeSource(
        IReadOnlyList<PickedFile> files, DecisionFilterSet? filters = null) =>
        new(files, filters ?? new DecisionFilterSet(), NullLoggerFactory.Instance);

    // -----------------------------------------------------------------------
    //  Construction
    // -----------------------------------------------------------------------

    [Fact]
    public void Ctor_NullFiles_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            new WasmUploadedProblemSetSource(null!, new DecisionFilterSet(), NullLoggerFactory.Instance));

    [Fact]
    public void Ctor_NullFilters_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            new WasmUploadedProblemSetSource([], null!, NullLoggerFactory.Instance));

    [Fact]
    public void Ctor_NullLoggerFactory_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            new WasmUploadedProblemSetSource([], new DecisionFilterSet(), null!));

    // -----------------------------------------------------------------------
    //  Name / Count
    // -----------------------------------------------------------------------

    [Fact]
    public void Name_NoFiles_IsNoFiles() =>
        Assert.Equal("No files", MakeSource([]).Name);

    [Fact]
    public void Name_SingleFile_IsThatFileName() =>
        Assert.Equal("match.xg", MakeSource([new PickedFile("match.xg", [])]).Name);

    [Fact]
    public void Name_MultipleFiles_IsCount() =>
        Assert.Equal("2 files",
            MakeSource([new PickedFile("a.xg", []), new PickedFile("b.xgp", [])]).Name);

    [Fact]
    public void Count_IsNull() => Assert.Null(MakeSource([]).Count);

    // -----------------------------------------------------------------------
    //  Name / extension contract
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EnumerateAsync_ExtensionlessName_ThrowsArgumentException()
    {
        // The stream iterator's DecisionId stamping discriminates the format from
        // the file-name extension, so a name without one is a usage error the
        // iterator rejects when it reaches that entry. The InputFile handler
        // preserves IBrowserFile.Name (extension-bearing) precisely to satisfy
        // this; this guards the failure mode if a name ever loses its extension.
        var src = MakeSource([new PickedFile("noextension", [1, 2, 3])]);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in src.EnumerateAsync()) { }
        });
    }

    // -----------------------------------------------------------------------
    //  Enumeration over the corpus
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EnumerateAsync_OverCorpus_YieldsAtLeastOneDecision()
    {
        var files = CorpusFiles();
        if (files.Count == 0) return; // corpus may be empty in CI

        var src = MakeSource(files);
        var count = 0;
        await foreach (var d in src.EnumerateAsync())
        {
            Assert.NotNull(d.Position);
            Assert.NotNull(d.Decision);
            if (++count >= 3) break;
        }
        Assert.True(count > 0);
    }

    [Fact]
    public async Task EnumerateAsync_IsReIterable()
    {
        // Buffered bytes + fresh MemoryStreams per call: a second enumeration must
        // succeed even though the first read the streams to completion. This is
        // what makes Restart work.
        var files = CorpusFiles();
        if (files.Count == 0) return;

        var src = MakeSource(files);

        var first = await TakeFirstAsync(src);
        var second = await TakeFirstAsync(src);

        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task EnumerateAsync_HonoursFilterSet()
    {
        var files = CorpusFiles();
        if (files.Count == 0) return;

        var filters = new DecisionFilterSet();
        filters.Add(new PlayerFilter(["zzz_no_such_player_zzz"]));

        var src = MakeSource(files, filters);
        var any = false;
        await foreach (var _ in src.EnumerateAsync())
        {
            any = true;
            break;
        }
        Assert.False(any);
    }

    // -----------------------------------------------------------------------
    //  Factory -> source -> controller wire
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FactoryShape_FeedsControllerStart()
    {
        // Pins the path Program.cs wires: a ProblemSetSourceFactory that builds a
        // WasmUploadedProblemSetSource over the picked set drives
        // QuizController.StartAsync to a first problem.
        var files = CorpusFiles();
        if (files.Count == 0) return;

        var picked = new PickedProblemSet();
        picked.Set(files);

        BgQuiz_Blazor.Client.Quiz.ProblemSetSourceFactory factory =
            filters => new WasmUploadedProblemSetSource(picked.Files, filters, NullLoggerFactory.Instance);
        var controller = new QuizController(factory);

        await controller.StartAsync(new FilterConfig());

        Assert.True(controller.HasStarted);
        // A real corpus yields at least one non-pass decision; if every decision
        // happened to be a pass the controller would finish, which is still a
        // valid started state.
        Assert.True(controller.Current is not null || controller.IsFinished);
    }

    [Fact]
    public async Task FactoryShape_ShuffleEnabled_WrapsSourceAndChangesOrder()
    {
        // Pins the shuffle half of the path Program.cs wires: when the user's
        // ShuffleOption is enabled, the factory wraps the WasmUploadedProblemSetSource
        // in a ShuffledProblemSetSource rather than handing the controller the
        // plain source directly. Needs >=2 corpus decisions for a shuffle to be
        // observable at all.
        var files = CorpusFiles();
        if (files.Count == 0) return;

        var picked = new PickedProblemSet();
        picked.Set(files);
        var shuffle = new ShuffleOption();

        BgQuiz_Blazor.Client.Quiz.ProblemSetSourceFactory factory = filters =>
        {
            IProblemSetSource inner = new WasmUploadedProblemSetSource(picked.Files, filters, NullLoggerFactory.Instance);
            return shuffle.Enabled ? new ShuffledProblemSetSource(inner, seed: 42) : inner;
        };

        var unshuffledOrder = await CollectAllAsync(factory(new DecisionFilterSet()));
        if (unshuffledOrder.Count < 2) return; // can't observe a shuffle over <2 items

        shuffle.Set(true);
        var shuffledOrder = await CollectAllAsync(factory(new DecisionFilterSet()));

        Assert.Equal(unshuffledOrder.Count, shuffledOrder.Count);
        Assert.NotEqual(unshuffledOrder, shuffledOrder); // order differs (seeded, so deterministic)
    }

    private static async Task<List<BgDecisionData>> CollectAllAsync(IProblemSetSource src)
    {
        var items = new List<BgDecisionData>();
        await foreach (var d in src.EnumerateAsync())
            items.Add(d);
        return items;
    }

    private static async Task<BgDecisionData?> TakeFirstAsync(WasmUploadedProblemSetSource src)
    {
        await foreach (var d in src.EnumerateAsync())
            return d;
        return null;
    }
}
