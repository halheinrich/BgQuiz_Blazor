using BgQuiz_Blazor.Quiz;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="ServerDiskProblemSetSourceFactory"/> — the Phase 1
/// <see cref="ProblemSetSourceFactory"/> implementation that builds a source
/// over the directory held in the per-circuit <see cref="ProblemSetSelection"/>.
/// </summary>
public sealed class ServerDiskProblemSetSourceFactoryTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    /// <summary>Create a uniquely-named temp directory, tracked for cleanup.</summary>
    private string NewTempDir()
    {
        var dir = Directory.CreateTempSubdirectory("bgquiz_factory_test_").FullName;
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    private static ServerDiskProblemSetSourceFactory MakeFactory(ProblemSetSelection selection) =>
        new(selection, NullLoggerFactory.Instance);

    [Fact]
    public void Ctor_NullSelection_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ServerDiskProblemSetSourceFactory(null!, NullLoggerFactory.Instance));
    }

    [Fact]
    public void Ctor_NullLoggerFactory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ServerDiskProblemSetSourceFactory(new ProblemSetSelection(), null!));
    }

    [Fact]
    public void Create_EmptyDirectory_ThrowsInvalidOperation()
    {
        var factory = MakeFactory(new ProblemSetSelection { Directory = string.Empty });
        Assert.Throws<InvalidOperationException>(() => factory.Create(new DecisionFilterSet()));
    }

    [Fact]
    public void Create_WhitespaceDirectory_ThrowsInvalidOperation()
    {
        var factory = MakeFactory(new ProblemSetSelection { Directory = "   " });
        Assert.Throws<InvalidOperationException>(() => factory.Create(new DecisionFilterSet()));
    }

    [Fact]
    public void Create_NonexistentDirectory_ThrowsDirectoryNotFound()
    {
        var phantom = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var factory = MakeFactory(new ProblemSetSelection { Directory = phantom });
        Assert.Throws<DirectoryNotFoundException>(() => factory.Create(new DecisionFilterSet()));
    }

    [Fact]
    public void Create_SelectedDirectory_BuildsSourceOverThatDirectory()
    {
        var dir = NewTempDir();
        var factory = MakeFactory(new ProblemSetSelection { Directory = dir });

        var source = factory.Create(new DecisionFilterSet());

        // Name is the directory leaf — the cheapest observable proof that the
        // factory threaded the selection's directory into the source.
        Assert.Equal(Path.GetFileName(dir), source.Name);
    }

    [Fact]
    public void Create_ReadsSelectionAtInvocationTime_NotConstruction()
    {
        // The whole point of moving the directory into ProblemSetSelection:
        // a change made after the factory is constructed must take effect on
        // the next Create call (i.e. the next quiz-start).
        var early = NewTempDir();
        var late = NewTempDir();
        var selection = new ProblemSetSelection { Directory = early };
        var factory = MakeFactory(selection);

        selection.Directory = late;
        var source = factory.Create(new DecisionFilterSet());

        Assert.Equal(Path.GetFileName(late), source.Name);
    }
}
