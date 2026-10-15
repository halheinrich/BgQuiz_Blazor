using BgFolderAccess_Razor;
using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="PickedProblemFolder"/> — the holder whose
/// <see cref="PickedProblemFolder.Summary"/> is the single source of truth for
/// how a pick describes itself (composed from
/// <see cref="PickedProblemFolder.DisplayName"/>, the one spelling of the quoted
/// folder name), and whose <see cref="PickedProblemFolder.Capability"/>
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
        folder.Set("MyMatches", FolderWriteCapability.Enabled, TestFixtures.Sample([File()]));

        Assert.Equal("'MyMatches' — 1 problem file", folder.Summary);
    }

    [Fact]
    public void Summary_MultipleFiles_NamesFolderAndCountsPlural()
    {
        var folder = new PickedProblemFolder();
        folder.Set("MyMatches", FolderWriteCapability.Enabled, TestFixtures.Sample([File("a.xg"), File("b.xgp")]));

        Assert.Equal("'MyMatches' — 2 problem files", folder.Summary);
    }

    [Fact]
    public void Summary_AfterClear_IsNullAgain()
    {
        var folder = new PickedProblemFolder();
        folder.Set("MyMatches", FolderWriteCapability.Enabled, TestFixtures.Sample([File()]));
        folder.Clear();

        Assert.Null(folder.Summary);
    }

    [Fact]
    public void DisplayName_NothingPicked_IsNull() =>
        Assert.Null(new PickedProblemFolder().DisplayName);

    [Fact]
    public void DisplayName_QuotesTheFolderName()
    {
        var folder = new PickedProblemFolder();
        folder.Set("MyMatches", FolderWriteCapability.Enabled, TestFixtures.Sample([File()]));

        Assert.Equal("'MyMatches'", folder.DisplayName);
    }

    [Fact]
    public void DisplayName_AfterClear_IsNullAgain()
    {
        var folder = new PickedProblemFolder();
        folder.Set("MyMatches", FolderWriteCapability.Enabled, TestFixtures.Sample([File()]));
        folder.Clear();

        Assert.Null(folder.DisplayName);
    }

    [Fact]
    public void Summary_ComposesFromDisplayName()
    {
        // halheinrich/backgammon#199: the quoting has one owner and Summary
        // reads it. What this pin can see is agreement, not the reading: with
        // today's quoting a Summary carrying its own copy produces the same
        // string. It fails the day the two come apart — DisplayName re-quoted
        // and a stray copy left in Summary — which the literal pins, each
        // updated alongside its own member, would let through. That there is
        // no second copy to drift is the acceptance survey's claim (the quote
        // pattern is spelled once in the tree), not this test's.
        var folder = new PickedProblemFolder();
        folder.Set("MyMatches", FolderWriteCapability.Enabled, TestFixtures.Sample([File("a.xg"), File("b.xgp")]));

        Assert.Equal($"{folder.DisplayName} — 2 problem files", folder.Summary);
    }

    [Fact]
    public void Set_RetainsCapabilityAndFolderName()
    {
        // The capability is the pick-time verdict Home's status notice re-derives
        // after navigate-back — the holder must retain it verbatim.
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", FolderWriteCapability.PermissionDenied, TestFixtures.Sample([File()]));

        Assert.Equal(FolderWriteCapability.PermissionDenied, folder.Capability);
        Assert.Equal("Corpus", folder.FolderName);
        Assert.True(folder.HasFiles);
    }

    [Fact]
    public void Clear_ResetsEverything()
    {
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", FolderWriteCapability.Enabled, TestFixtures.Sample([File()]));

        folder.Clear();

        Assert.False(folder.HasFiles);
        Assert.Empty(folder.Files);
        Assert.Null(folder.FolderName);
        Assert.Equal(FolderWriteCapability.BrowserUnsupported, folder.Capability);
    }

    [Fact]
    public void Set_NullName_Throws()
    {
        var folder = new PickedProblemFolder();
        Assert.Throws<ArgumentNullException>(
            () => folder.Set(null!, FolderWriteCapability.Enabled, TestFixtures.Sample([File()])));
    }

    [Fact]
    public void Set_NullSample_Throws()
    {
        // The sample is taken whole — the files and the report that says what
        // they are of the folder — so a caller cannot hand over one without the
        // other. (A default array inside it is the producer's record's to refuse.)
        var folder = new PickedProblemFolder();

        var thrown = Assert.Throws<ArgumentNullException>(
            () => folder.Set("Corpus", FolderWriteCapability.Enabled, null!));

        Assert.Equal("sample", thrown.ParamName);
        Assert.False(folder.HasFiles); // nothing was stored
    }

    // -----------------------------------------------------------------------
    //  Parse-once cache seam: cache lifecycle = pick lifecycle
    // -----------------------------------------------------------------------

    [Fact]
    public void StoreParsed_CurrentGeneration_CachesTheParseResult()
    {
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", FolderWriteCapability.Enabled, TestFixtures.Sample([File()]));
        var parsed = TestFixtures.Parsed(TestFixtures.CubeDecision());

        folder.StoreParsed(folder.PickGeneration, parsed);

        // The very object stored, decisions and report together — so the cache
        // is the parse, not a copy of it, and the rejection record can only
        // ever be the one the parse that produced these decisions made
        // (halheinrich/backgammon#368).
        Assert.Same(parsed, folder.Parsed);
    }

    [Fact]
    public void StoreParsed_SupersededGeneration_IsDropped()
    {
        // A parse begun against one pick must never land as the cache of a
        // later pick — the pick gesture is async, so a re-pick can complete
        // inside a Start's own await points.
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", FolderWriteCapability.Enabled, TestFixtures.Sample([File()]));
        var staleGeneration = folder.PickGeneration;

        folder.Set("Other", FolderWriteCapability.Enabled, TestFixtures.Sample([File("b.xgp")])); // supersedes
        folder.StoreParsed(staleGeneration, TestFixtures.Parsed());

        Assert.Null(folder.Parsed);
    }

    [Fact]
    public void Set_InvalidatesParseCacheAndBumpsGeneration()
    {
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", FolderWriteCapability.Enabled, TestFixtures.Sample([File()]));
        folder.StoreParsed(folder.PickGeneration, TestFixtures.Parsed());
        var generation = folder.PickGeneration;

        folder.Set("Corpus", FolderWriteCapability.Enabled, TestFixtures.Sample([File()])); // re-pick, even of the same folder

        Assert.Null(folder.Parsed);
        Assert.NotEqual(generation, folder.PickGeneration);
    }

    [Fact]
    public void Clear_InvalidatesParseCacheAndBumpsGeneration()
    {
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", FolderWriteCapability.Enabled, TestFixtures.Sample([File()]));
        folder.StoreParsed(folder.PickGeneration, TestFixtures.Parsed());
        var generation = folder.PickGeneration;

        folder.Clear();

        Assert.Null(folder.Parsed);
        Assert.NotEqual(generation, folder.PickGeneration);
    }

    [Fact]
    public void StoreParsed_Null_Throws()
    {
        var folder = new PickedProblemFolder();
        var refused = Assert.Throws<ArgumentNullException>(
            () => folder.StoreParsed(folder.PickGeneration, null!));
        Assert.Equal("parsed", refused.ParamName);
    }
}
