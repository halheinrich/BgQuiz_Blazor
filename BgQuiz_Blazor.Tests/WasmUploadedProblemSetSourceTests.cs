using BgDataTypes_Lib;
using BgGame_Lib;
using BgFolderAccess_Razor;
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
        new(files, filters ?? new DecisionFilterSet(), NullLoggerFactory.Instance, TimeProvider.System);

    // -----------------------------------------------------------------------
    //  Construction
    // -----------------------------------------------------------------------

    [Fact]
    public void Ctor_NullFiles_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            new WasmUploadedProblemSetSource(null!, new DecisionFilterSet(), NullLoggerFactory.Instance, TimeProvider.System));

    [Fact]
    public void Ctor_NullFilters_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            new WasmUploadedProblemSetSource([], null!, NullLoggerFactory.Instance, TimeProvider.System));

    [Fact]
    public void Ctor_NullLoggerFactory_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            new WasmUploadedProblemSetSource([], new DecisionFilterSet(), null!, TimeProvider.System));

    [Fact]
    public void Ctor_NullClock_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            new WasmUploadedProblemSetSource([], new DecisionFilterSet(), NullLoggerFactory.Instance, null!));

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
        // iterator rejects when it reaches that entry. Both folder-pick paths
        // preserve the browser's extension-bearing entry names precisely to
        // satisfy this; this guards the failure mode if a name ever loses its
        // extension.
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

        // Composed through FilterConfig.Build() — the intent surface. The
        // concrete filter classes are internal to XgFilter_Lib, so naming a
        // player is how a consumer asks for a player filter.
        var filters = new FilterConfig { Players = { "zzz_no_such_player_zzz" } }.Build();

        var src = MakeSource(files, filters);
        var any = false;
        await foreach (var _ in src.EnumerateAsync())
        {
            any = true;
            break;
        }
        Assert.False(any);
    }

    // Composition-level tests — the factory the client registers, the layer
    // order it wires, and how a controller drives it — live with the type that
    // owns that composition, in PickedFolderSourceFactoryTests. What stays here
    // is this source's own contract: naming, extensions, filter application,
    // re-iterability.

    private static async Task<BgDecisionData?> TakeFirstAsync(WasmUploadedProblemSetSource src)
    {
        await foreach (var d in src.EnumerateAsync())
            return d;
        return null;
    }
}
