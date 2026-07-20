using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="PickedProblemFolder"/> — the holder whose
/// <see cref="PickedProblemFolder.Summary"/> is the single source of truth for
/// how a pick describes itself, and whose <see cref="PickedProblemFolder.Capability"/>
/// carries the pick-time stats verdict across in-app navigation. Deriving both
/// here (rather than in transient page fields) is what keeps them honest across
/// navigate-back; <see cref="PageTests"/> pins the page-render half.
/// </summary>
public class PickedProblemFolderTests
{
    private static PickedFile File(string name = "match.xg") => new(name, [1, 2, 3]);

    [Fact]
    public void Summary_NothingPicked_IsNull() =>
        Assert.Null(new PickedProblemFolder().Summary);

    [Fact]
    public void Summary_SingleFile_NamesFolderAndCountsSingular()
    {
        var folder = new PickedProblemFolder();
        folder.Set("MyMatches", [File()], StatsSaveCapability.Enabled);

        Assert.Equal("'MyMatches' — 1 problem file", folder.Summary);
    }

    [Fact]
    public void Summary_MultipleFiles_NamesFolderAndCountsPlural()
    {
        var folder = new PickedProblemFolder();
        folder.Set("MyMatches", [File("a.xg"), File("b.xgp")], StatsSaveCapability.Enabled);

        Assert.Equal("'MyMatches' — 2 problem files", folder.Summary);
    }

    [Fact]
    public void Summary_AfterClear_IsNullAgain()
    {
        var folder = new PickedProblemFolder();
        folder.Set("MyMatches", [File()], StatsSaveCapability.Enabled);
        folder.Clear();

        Assert.Null(folder.Summary);
    }

    [Fact]
    public void Set_RetainsCapabilityAndFolderName()
    {
        // The capability is the pick-time verdict Home's status notice re-derives
        // after navigate-back — the holder must retain it verbatim.
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", [File()], StatsSaveCapability.PermissionDenied);

        Assert.Equal(StatsSaveCapability.PermissionDenied, folder.Capability);
        Assert.Equal("Corpus", folder.FolderName);
        Assert.True(folder.HasFiles);
    }

    [Fact]
    public void Clear_ResetsEverything()
    {
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", [File()], StatsSaveCapability.Enabled);

        folder.Clear();

        Assert.False(folder.HasFiles);
        Assert.Empty(folder.Files);
        Assert.Null(folder.FolderName);
        Assert.Equal(StatsSaveCapability.BrowserUnsupported, folder.Capability);
    }

    [Fact]
    public void Set_NullArguments_Throw()
    {
        var folder = new PickedProblemFolder();
        Assert.Throws<ArgumentNullException>(
            () => folder.Set(null!, [File()], StatsSaveCapability.Enabled));
        Assert.Throws<ArgumentNullException>(
            () => folder.Set("Corpus", null!, StatsSaveCapability.Enabled));
    }

    // -----------------------------------------------------------------------
    //  Parse-once cache seam: cache lifecycle = pick lifecycle
    // -----------------------------------------------------------------------

    [Fact]
    public void StoreParsed_CurrentGeneration_CachesDecisions()
    {
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", [File()], StatsSaveCapability.Enabled);
        var parsed = new List<BgDataTypes_Lib.BgDecisionData>();

        folder.StoreParsed(folder.PickGeneration, parsed);

        Assert.Same(parsed, folder.ParsedDecisions);
    }

    [Fact]
    public void StoreParsed_SupersededGeneration_IsDropped()
    {
        // A parse begun against one pick must never land as the cache of a
        // later pick — the pick gesture is async, so a re-pick can complete
        // inside a Start's own await points.
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", [File()], StatsSaveCapability.Enabled);
        var staleGeneration = folder.PickGeneration;

        folder.Set("Other", [File("b.xgp")], StatsSaveCapability.Enabled); // supersedes
        folder.StoreParsed(staleGeneration, []);

        Assert.Null(folder.ParsedDecisions);
    }

    [Fact]
    public void Set_InvalidatesParseCacheAndBumpsGeneration()
    {
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", [File()], StatsSaveCapability.Enabled);
        folder.StoreParsed(folder.PickGeneration, []);
        var generation = folder.PickGeneration;

        folder.Set("Corpus", [File()], StatsSaveCapability.Enabled); // re-pick, even of the same folder

        Assert.Null(folder.ParsedDecisions);
        Assert.NotEqual(generation, folder.PickGeneration);
    }

    [Fact]
    public void Clear_InvalidatesParseCacheAndBumpsGeneration()
    {
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", [File()], StatsSaveCapability.Enabled);
        folder.StoreParsed(folder.PickGeneration, []);
        var generation = folder.PickGeneration;

        folder.Clear();

        Assert.Null(folder.ParsedDecisions);
        Assert.NotEqual(generation, folder.PickGeneration);
    }

    [Fact]
    public void StoreParsed_NullDecisions_Throws()
    {
        var folder = new PickedProblemFolder();
        Assert.Throws<ArgumentNullException>(
            () => folder.StoreParsed(folder.PickGeneration, null!));
    }
}
