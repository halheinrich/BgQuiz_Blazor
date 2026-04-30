using BgQuiz_Blazor.Quiz;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

public class ServerDiskProblemSetSourceTests
{
    /// <summary>
    /// Path to the umbrella's fixture-agnostic <c>TestData/xg/</c>. Files come
    /// and go; tests here assert shape-level invariants only (re-iterability,
    /// filter application), never specific file contents.
    /// </summary>
    private static string CorpusDirectory =>
        Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "TestData", "xg"));

    [Fact]
    public void Ctor_NullDirectory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ServerDiskProblemSetSource(null!, new DecisionFilterSet()));
    }

    [Fact]
    public void Ctor_EmptyDirectory_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ServerDiskProblemSetSource(string.Empty, new DecisionFilterSet()));
    }

    [Fact]
    public void Ctor_WhitespaceDirectory_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ServerDiskProblemSetSource("   ", new DecisionFilterSet()));
    }

    [Fact]
    public void Ctor_NullFilters_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ServerDiskProblemSetSource(CorpusDirectory, null!));
    }

    [Fact]
    public void Ctor_NonexistentDirectory_ThrowsDirectoryNotFound()
    {
        var phantom = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.Throws<DirectoryNotFoundException>(() =>
            new ServerDiskProblemSetSource(phantom, new DecisionFilterSet()));
    }

    [Fact]
    public void Name_IsDirectoryLeaf()
    {
        var src = new ServerDiskProblemSetSource(CorpusDirectory, new DecisionFilterSet());
        Assert.Equal("xg", src.Name);
    }

    [Fact]
    public void Name_StripsTrailingSeparator()
    {
        var src = new ServerDiskProblemSetSource(CorpusDirectory + Path.DirectorySeparatorChar, new DecisionFilterSet());
        Assert.Equal("xg", src.Name);
    }

    [Fact]
    public void Count_IsNull()
    {
        var src = new ServerDiskProblemSetSource(CorpusDirectory, new DecisionFilterSet());
        Assert.Null(src.Count);
    }

    [Fact]
    public async Task EnumerateAsync_OverCorpus_YieldsAtLeastOneDecision()
    {
        if (!Directory.Exists(CorpusDirectory) ||
            !Directory.EnumerateFiles(CorpusDirectory, "*.xg").Any())
            return; // corpus may be empty in CI; this is a shape-level test only

        var src = new ServerDiskProblemSetSource(CorpusDirectory, new DecisionFilterSet());
        var count = 0;
        await foreach (var d in src.EnumerateAsync())
        {
            Assert.NotNull(d.Position);
            Assert.NotNull(d.Decision);
            count++;
            if (count >= 3) break;
        }
        Assert.True(count > 0);
    }

    [Fact]
    public async Task EnumerateAsync_IsReIterable()
    {
        if (!Directory.Exists(CorpusDirectory) ||
            !Directory.EnumerateFiles(CorpusDirectory, "*.xg").Any())
            return;

        var src = new ServerDiskProblemSetSource(CorpusDirectory, new DecisionFilterSet());

        var firstPass = await TakeFirstAsync(src);
        var secondPass = await TakeFirstAsync(src);

        Assert.NotNull(firstPass);
        Assert.NotNull(secondPass);
    }

    [Fact]
    public async Task EnumerateAsync_HonoursFilterSet()
    {
        if (!Directory.Exists(CorpusDirectory) ||
            !Directory.EnumerateFiles(CorpusDirectory, "*.xg").Any())
            return;

        // Player name unlikely to match any record in the rotating corpus.
        var filters = new DecisionFilterSet();
        filters.Add(new PlayerFilter(["zzz_no_such_player_zzz"]));

        var src = new ServerDiskProblemSetSource(CorpusDirectory, filters);
        var any = false;
        await foreach (var _ in src.EnumerateAsync())
        {
            any = true;
            break;
        }
        Assert.False(any);
    }

    private static async Task<BgDataTypes_Lib.BgDecisionData?> TakeFirstAsync(
        ServerDiskProblemSetSource src)
    {
        await foreach (var d in src.EnumerateAsync())
            return d;
        return null;
    }
}
