using BgQuiz_Blazor.Client.Quiz;
using Microsoft.JSInterop;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="SavedFiltersStore"/> lifecycle: the pick-time load (picked slot),
/// save/delete persistence, and the degrade guarantees it shares with
/// <see cref="QuizStatsStore"/> — a read/parse failure never writes over the
/// user's file, a write failure stops writing without faulting, and only the
/// File System Access mechanisms are read at all.
/// </summary>
public class SavedFiltersStoreTests
{
    private static PickedProblemFolder PickedFolder(
        StatsSaveCapability capability = StatsSaveCapability.Enabled)
    {
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", [new PickedFile("a.xgp", [1, 2, 3])], capability);
        return folder;
    }

    private static SavedFiltersStore MakeStore(
        FakeFolderAccess fake, PickedProblemFolder? folder = null) =>
        new(fake, folder ?? PickedFolder());

    /// <summary>A two-entry collection with a distinguishable config on "Race".</summary>
    private static string TwoEntryJson() =>
        NamedFilterCollection.Empty
            .With("Race", new FilterConfig { Players = ["Magriel"] })
            .With("Blitz", new FilterConfig())
            .ToJson();

    // -----------------------------------------------------------------------
    //  LoadForPickAsync — the pick-time read
    // -----------------------------------------------------------------------

    [Fact]
    public void InitialStatus_IsDisabled()
    {
        var store = MakeStore(new FakeFolderAccess());
        Assert.Equal(SavedFiltersStatus.Disabled, store.Status);
        Assert.Empty(store.Filters.Names);
    }

    [Fact]
    public async Task LoadForPick_BrowserUnsupported_DisabledWithoutReading()
    {
        // A fallback pick has no directory handle to read. The short-circuit
        // must not even attempt a read — proven by a read that would throw:
        // if it ran, the status would be LoadFailed, not Disabled.
        var fake = new FakeFolderAccess { FiltersReadException = new JSException("must not read") };
        var store = MakeStore(fake, PickedFolder(StatsSaveCapability.BrowserUnsupported));

        await store.LoadForPickAsync();

        Assert.Equal(SavedFiltersStatus.Disabled, store.Status);
    }

    [Fact]
    public async Task LoadForPick_NothingPicked_Disabled()
    {
        // Defensive: a cleared/never-populated holder defaults to a non-FS
        // capability, so the store short-circuits to Disabled.
        var store = MakeStore(new FakeFolderAccess(), new PickedProblemFolder());

        await store.LoadForPickAsync();

        Assert.Equal(SavedFiltersStatus.Disabled, store.Status);
    }

    [Fact]
    public async Task LoadForPick_NoFile_ReadySeededEmpty()
    {
        // null read = a fresh folder: Ready over the empty collection, and the
        // load itself never writes (the first Save writes the file into being).
        var fake = new FakeFolderAccess { FiltersJson = null };
        var store = MakeStore(fake);

        await store.LoadForPickAsync();

        Assert.Equal(SavedFiltersStatus.Ready, store.Status);
        Assert.Empty(store.Filters.Names);
        Assert.Empty(fake.FiltersWrites);
    }

    [Fact]
    public async Task LoadForPick_ValidFile_ReadyWithCollection()
    {
        var fake = new FakeFolderAccess { FiltersJson = TwoEntryJson() };
        var store = MakeStore(fake);

        await store.LoadForPickAsync();

        Assert.Equal(SavedFiltersStatus.Ready, store.Status);
        // Canonical (name-sorted) order comes from the collection, not the store.
        Assert.Equal(["Blitz", "Race"], store.Filters.Names);
        Assert.True(store.Filters.TryGetConfig("Race", out var race));
        Assert.Equal(["Magriel"], race!.Players);
    }

    [Fact]
    public async Task LoadForPick_PermissionDenied_StillReadsReady()
    {
        // Load-only: PermissionDenied is read via the pick gesture's implicit
        // read grant, so the collection still loads (saving is gated elsewhere).
        var fake = new FakeFolderAccess { FiltersJson = TwoEntryJson() };
        var store = MakeStore(fake, PickedFolder(StatsSaveCapability.PermissionDenied));

        await store.LoadForPickAsync();

        Assert.Equal(SavedFiltersStatus.Ready, store.Status);
        Assert.Equal(["Blitz", "Race"], store.Filters.Names);
    }

    [Fact]
    public async Task LoadForPick_CorruptFile_LoadFailed()
    {
        // Non-null but unparseable (TryFromJson false) → LoadFailed.
        var fake = new FakeFolderAccess { FiltersJson = "{ not valid json" };
        var store = MakeStore(fake);

        await store.LoadForPickAsync();

        Assert.Equal(SavedFiltersStatus.LoadFailed, store.Status);
    }

    [Fact]
    public async Task LoadForPick_ReadThrows_LoadFailed()
    {
        // The browser failed the read (FS error, or read genuinely withheld) —
        // the read-failure-tolerant path that keeps load-only non-load-bearing.
        var fake = new FakeFolderAccess { FiltersReadException = new JSException("read failed") };
        var store = MakeStore(fake);

        await store.LoadForPickAsync();

        Assert.Equal(SavedFiltersStatus.LoadFailed, store.Status);
    }

    [Fact]
    public async Task CorruptFile_SaveIsNoOp_FileNeverOverwritten()
    {
        // The zero-writes guarantee: after a corrupt load a save must not run,
        // so the user's file (which we couldn't parse) is never clobbered.
        var fake = new FakeFolderAccess { FiltersJson = "{ not valid json" };
        var store = MakeStore(fake);
        await store.LoadForPickAsync();

        await store.SaveAsync("Race", new FilterConfig());

        Assert.Equal(SavedFiltersStatus.LoadFailed, store.Status);
        Assert.Empty(fake.FiltersWrites);
        Assert.Empty(store.Filters.Names);
    }

    // -----------------------------------------------------------------------
    //  SaveAsync / DeleteAsync — persistence
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Save_AddsEntry_AndPersistsRoundTrippableJson()
    {
        var fake = new FakeFolderAccess { FiltersJson = null };
        var store = MakeStore(fake);
        await store.LoadForPickAsync();

        await store.SaveAsync("Race", new FilterConfig { Players = ["Magriel"] });

        Assert.True(store.Filters.Contains("Race"));
        var written = Assert.Single(fake.FiltersWrites);
        Assert.True(NamedFilterCollection.TryFromJson(written, out var reloaded));
        Assert.Equal(["Race"], reloaded.Names);
    }

    [Fact]
    public async Task Save_WriteThrows_WriteFailed_KeepsInMemory_StopsWriting()
    {
        var fake = new FakeFolderAccess
        {
            FiltersJson = null,
            FiltersWriteException = new JSException("write failed"),
        };
        var store = MakeStore(fake);
        await store.LoadForPickAsync();

        await store.SaveAsync("Race", new FilterConfig());

        // The fold happened in memory (the pick list stays truthful) but the
        // status flips and further writes are refused.
        Assert.Equal(SavedFiltersStatus.WriteFailed, store.Status);
        Assert.True(store.Filters.Contains("Race"));

        await store.SaveAsync("Blitz", new FilterConfig());
        Assert.False(store.Filters.Contains("Blitz")); // second save no-ops
    }

    [Fact]
    public async Task Delete_RemovesEntry_AndPersists()
    {
        var fake = new FakeFolderAccess { FiltersJson = TwoEntryJson() };
        var store = MakeStore(fake);
        await store.LoadForPickAsync();

        await store.DeleteAsync("Race");

        Assert.Equal(["Blitz"], store.Filters.Names);
        var written = Assert.Single(fake.FiltersWrites);
        Assert.True(NamedFilterCollection.TryFromJson(written, out var reloaded));
        Assert.Equal(["Blitz"], reloaded.Names);
    }

    [Fact]
    public async Task Reset_ReturnsToDisabledEmpty()
    {
        var fake = new FakeFolderAccess { FiltersJson = TwoEntryJson() };
        var store = MakeStore(fake);
        await store.LoadForPickAsync();

        store.Reset();

        Assert.Equal(SavedFiltersStatus.Disabled, store.Status);
        Assert.Empty(store.Filters.Names);
    }
}
