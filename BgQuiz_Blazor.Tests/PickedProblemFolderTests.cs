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
    //  Two identities: the setup a pick begins, the sample a draw holds
    //  (SPEC-filtering.md §4, "The sample, the limits and Resample")
    // -----------------------------------------------------------------------

    private static PickedProblemFolder Holding(string folderName = "Corpus")
    {
        var folder = new PickedProblemFolder();
        folder.Set(folderName, FolderWriteCapability.Enabled, TestFixtures.Sample([File()], (".xg", 5)));
        return folder;
    }

    [Fact]
    public void ANewHolder_HoldsNoSetup_AndASampleOfNothing()
    {
        var folder = new PickedProblemFolder();

        Assert.Null(folder.SetupIdentity);
        Assert.NotNull(folder.SampleIdentity);
        Assert.False(folder.CanResample);
    }

    [Fact]
    public void Set_BeginsANewSetup_AndANewSample_EvenOfTheSameFolder()
    {
        // Every explicit pick ends the setup, so the filter's applied-ness and
        // the mix cannot carry across one — the same folder picked again
        // included, which is why the identity is never the folder's name.
        var folder = Holding();
        var setup = folder.SetupIdentity;
        var sample = folder.SampleIdentity;

        folder.Set("Corpus", FolderWriteCapability.Enabled, TestFixtures.Sample([File()]));

        Assert.NotNull(folder.SetupIdentity);
        Assert.NotSame(setup, folder.SetupIdentity);
        Assert.NotEqual(setup!.FilterSource, folder.SetupIdentity.FilterSource);
        Assert.NotSame(sample, folder.SampleIdentity);
    }

    [Fact]
    public void Resample_KeepsTheSetup_AndReplacesTheSample()
    {
        var folder = Holding();
        folder.StoreParsed(folder.SampleIdentity, TestFixtures.Parsed());
        var setup = folder.SetupIdentity;
        var sample = folder.SampleIdentity;
        var drawn = TestFixtures.Sample([File("other.xg")], (".xg", 5));

        folder.Resample(drawn);

        // The setup's: the folder, its capability and its identity — so the
        // filter source Home reports is the same token, and the filter in
        // effect stands.
        Assert.Same(setup, folder.SetupIdentity);
        Assert.Equal(setup!.FilterSource, folder.SetupIdentity!.FilterSource);
        Assert.Equal("Corpus", folder.FolderName);
        Assert.Equal(FolderWriteCapability.Enabled, folder.Capability);
        // The sample's: its files and report, its identity, and the parse of
        // the files it replaced.
        Assert.Same(drawn, folder.Sample);
        Assert.NotSame(sample, folder.SampleIdentity);
        Assert.Null(folder.Parsed);
    }

    [Fact]
    public void Resample_WithNoFolderHeld_IsRefused_AndChangesNothing()
    {
        var folder = new PickedProblemFolder();
        var sample = folder.SampleIdentity;

        Assert.Throws<InvalidOperationException>(
            () => folder.Resample(TestFixtures.Sample([File()])));

        Assert.False(folder.HasFiles);
        Assert.Null(folder.SetupIdentity);
        Assert.Same(sample, folder.SampleIdentity);
    }

    [Fact]
    public void Resample_OfNoFiles_IsRefused_AndKeepsTheSampleHeld()
    {
        // An empty resample replaces nothing (SPEC-filtering.md §4): Home
        // keeps the held sample, and so does the holder if handed one.
        var folder = Holding();
        var parsed = TestFixtures.Parsed();
        folder.StoreParsed(folder.SampleIdentity, parsed);
        var held = folder.Sample;
        var sample = folder.SampleIdentity;

        var refused = Assert.Throws<ArgumentException>(
            () => folder.Resample(TestFixtures.Sample([])));

        Assert.Equal("sample", refused.ParamName);
        Assert.Same(held, folder.Sample);
        Assert.Same(sample, folder.SampleIdentity);
        Assert.Same(parsed, folder.Parsed);
    }

    [Fact]
    public void Set_OfNoFiles_IsRefused_AndKeepsWhatIsHeld()
    {
        // An empty draw holds no folder; Clear is how nothing is held.
        var folder = Holding();
        var setup = folder.SetupIdentity;
        var sample = folder.SampleIdentity;

        var refused = Assert.Throws<ArgumentException>(
            () => folder.Set("Empty", FolderWriteCapability.Enabled, TestFixtures.Sample([])));

        Assert.Equal("sample", refused.ParamName);
        Assert.Equal("Corpus", folder.FolderName);
        Assert.Same(setup, folder.SetupIdentity);
        Assert.Same(sample, folder.SampleIdentity);
    }

    [Fact]
    public void Clear_EndsTheSetup_AndReplacesTheSample()
    {
        var folder = Holding();
        var sample = folder.SampleIdentity;

        folder.Clear();

        Assert.Null(folder.SetupIdentity);
        Assert.NotSame(sample, folder.SampleIdentity);
        Assert.Empty(folder.Sample.Kinds);
        Assert.False(folder.CanResample);
    }

    [Fact]
    public void CanResample_HoldsExactlyWhileAKindWasOverItsLimit()
    {
        // Offered unless the sample holds every matching file — the producer's
        // derivation from the per-kind report, read and not restated.
        var folder = new PickedProblemFolder();
        folder.Set("Whole", FolderWriteCapability.Enabled, TestFixtures.Sample([File()]));
        Assert.True(folder.Sample.HoldsEveryMatchingFile);
        Assert.False(folder.CanResample);

        folder.Set("Over", FolderWriteCapability.Enabled, TestFixtures.Sample([File()], (".xgp", 1)));
        Assert.False(folder.Sample.HoldsEveryMatchingFile);
        Assert.True(folder.CanResample);

        // A resample that happens to hold every file it found (the folder
        // shrank since) offers no further one.
        folder.Resample(TestFixtures.Sample([File()]));
        Assert.False(folder.CanResample);
    }

    // -----------------------------------------------------------------------
    //  Parse-once cache seam: cache lifecycle = sample lifecycle
    // -----------------------------------------------------------------------

    [Fact]
    public void StoreParsed_CurrentSample_CachesTheParseResult()
    {
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", FolderWriteCapability.Enabled, TestFixtures.Sample([File()]));
        var parsed = TestFixtures.Parsed(TestFixtures.CubeDecision());

        folder.StoreParsed(folder.SampleIdentity, parsed);

        // The very object stored, decisions and report together — so the cache
        // is the parse, not a copy of it, and the rejection record can only
        // ever be the one the parse that produced these decisions made
        // (halheinrich/backgammon#368).
        Assert.Same(parsed, folder.Parsed);
    }

    /// <summary>The three ways a held sample is replaced.</summary>
    public static TheoryData<string> Replacements() => ["pick", "resample", "clear"];

    private static void Replace(PickedProblemFolder folder, string how)
    {
        switch (how)
        {
            case "pick": folder.Set("Other", FolderWriteCapability.Enabled, TestFixtures.Sample([File("b.xgp")])); break;
            case "resample": folder.Resample(TestFixtures.Sample([File("b.xg")])); break;
            case "clear": folder.Clear(); break;
            default: throw new ArgumentOutOfRangeException(nameof(how), how, null);
        }
    }

    [Theory]
    [MemberData(nameof(Replacements))]
    public void StoreParsed_OfAReplacedSample_IsDropped(string how)
    {
        // A parse begun against one sample must never land as the cache of a
        // later one — a pick and a resample are async, so either can complete
        // inside a count's or a Start's own await points.
        var folder = Holding();
        var stale = folder.SampleIdentity;

        Replace(folder, how);
        folder.StoreParsed(stale, TestFixtures.Parsed());

        Assert.Null(folder.Parsed);
    }

    [Theory]
    [MemberData(nameof(Replacements))]
    public void ReplacingTheSample_DropsTheParse_AndChangesItsIdentity(string how)
    {
        var folder = Holding();
        folder.StoreParsed(folder.SampleIdentity, TestFixtures.Parsed());
        var sample = folder.SampleIdentity;

        Replace(folder, how);

        Assert.Null(folder.Parsed);
        Assert.NotSame(sample, folder.SampleIdentity);
    }

    [Fact]
    public void StoreParsed_Null_Throws()
    {
        var folder = new PickedProblemFolder();
        var refused = Assert.Throws<ArgumentNullException>(
            () => folder.StoreParsed(folder.SampleIdentity, null!));
        Assert.Equal("parsed", refused.ParamName);
    }

    [Fact]
    public void StoreParsed_NullSample_Throws()
    {
        var folder = new PickedProblemFolder();
        var refused = Assert.Throws<ArgumentNullException>(
            () => folder.StoreParsed(null!, TestFixtures.Parsed()));
        Assert.Equal("sample", refused.ParamName);
    }
}
