using BgDataTypes_Lib.TestSupport;
using BgQuiz_Blazor.Client.Quiz;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="NotesPreferencesStore"/>, the one reader and writer of the notes
/// overlay's placement and size preferences (<c>SPEC-quiz-view.md</c> §4, "The
/// notes overlay's placement is a remembered preference" and "The notes
/// overlay's size is a remembered preference": "missing, malformed, out of
/// range or unavailable reads as unset; a failed write keeps the size in
/// memory for the session"; issues <c>halheinrich/backgammon#344</c> and
/// <c>halheinrich/backgammon#414</c>). Pinned here: the tolerant read, each
/// preference on its own; the wire format byte for byte; a payload from
/// before the size still reading; Reset removing the entry; and a refused
/// write or a late read never costing the session its preferences. Storage is
/// planned with <see cref="BrowserStoragePlan"/> — the store's key is the
/// store's own to name, the interop calls are not — so the real
/// <see cref="BrowserStorage"/> runs, and every test verifies the plan: a call
/// nobody declared fails.
/// </summary>
public class NotesPreferencesStoreTests : BunitContext
{
    private readonly BrowserStoragePlan _plan;

    public NotesPreferencesStoreTests()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        _plan = BrowserStoragePlan.On(JSInterop);
    }

    /// <summary>The app's one storage fact, which a refusal here is reported to (halheinrich/backgammon#360).</summary>
    private readonly BrowserStorageCondition _storage = new();

    private readonly RecordingLogger<ReportingBrowserStorage> _log = new();

    private NotesPreferencesStore NewStore() =>
        new(new ReportingBrowserStorage(new BrowserStorage(JSInterop.JSRuntime), _storage, _log));

    private static NotesPreferences P(double? horizontal, double? vertical, double? width = null, double? height = null) =>
        new(NotesPlacement.Create(horizontal, vertical), NotesSize.Create(width, height));

    /// <summary>A refusal is said twice, and only twice: one warning carrying the browser's exception, in this store's words, and the report.</summary>
    private void AssertRefusalSaid(string operation, string consequence)
    {
        StorageRefusalLog.AssertSaidOnce(_log, operation, NotesPreferencesStore.StorageKey, consequence);
        Assert.True(_storage.HasOccurred);
    }

    private void AssertReadRefusalSaid() => AssertRefusalSaid("read", "the notes open centred at their first size");

    private void AssertWriteRefusalSaid(string operation = "write") =>
        AssertRefusalSaid(operation, "the notes' placement and size are kept for this session only");

    private void ExpectStored(string? json) =>
        _plan.ExpectRead(
            BrowserStorageArea.Local, NotesPreferencesStore.StorageKey,
            json is null ? BrowserStorageReadAnswer.Absent : BrowserStorageReadAnswer.Stored(json));

    private void ExpectWritten(string json, BrowserStorageWriteAnswer answer) =>
        _plan.ExpectWrite(BrowserStorageArea.Local, NotesPreferencesStore.StorageKey, json, answer);

    [Fact]
    public void TheKey_IsItsOwn_InTheXgFamily_AndStillThePlacements()
    {
        // Unchanged by the size's arrival: Help renders it, and stored
        // placements are found under it.
        Assert.Equal("xg_notesPlacement", NotesPreferencesStore.StorageKey);
        Assert.NotEqual(QuizSettings.StorageKey, NotesPreferencesStore.StorageKey);
    }

    [Fact]
    public void BeforeLoading_ThePreferencesAreUnset() => Assert.Equal(NotesPreferences.Unset, NewStore().Current);

    [Theory]
    [InlineData("""{"horizontal":0.25,"vertical":0.75,"width":400,"height":300}""", 0.25, 0.75, 400.0, 300.0)]
    [InlineData("""{"horizontal":0,"vertical":1,"width":null,"height":null}""", 0.0, 1.0, null, null)]
    [InlineData("""{"horizontal":null,"vertical":null,"width":512.5,"height":null}""", null, null, 512.5, null)]
    [InlineData("""{"horizontal":0.625,"vertical":null}""", 0.625, null, null, null)]   // written before the size existed
    [InlineData("""{"vertical":0.125}""", null, 0.125, null, null)]
    [InlineData("""{"height":250}""", null, null, null, 250.0)]
    [InlineData("""{"horizontal":0.5,"vertical":0.5,"width":400,"height":300,"later":"a newer build's field"}""", 0.5, 0.5, 400.0, 300.0)]
    public async Task Load_AWellFormedPayload_IsThePreferences(
        string stored, double? horizontal, double? vertical, double? width, double? height)
    {
        ExpectStored(stored);
        var store = NewStore();

        await store.EnsureLoadedAsync();

        Assert.Equal(P(horizontal, vertical, width, height), store.Current);
        Assert.False(_storage.HasOccurred); // the control: a read that answers reports nothing
        _plan.Verify();
    }

    [Theory]
    [InlineData(null)]                                                // missing
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[0.25, 0.75]")]
    [InlineData("0.5")]
    [InlineData("""{"horizontal":null,"vertical":null}""")]
    [InlineData("""{"horizontal":null,"vertical":null,"width":null,"height":null}""")]
    [InlineData("""{"horizontal":1.5,"vertical":0.25}""")]           // out of range
    [InlineData("""{"horizontal":0.25,"vertical":-0.1}""")]
    [InlineData("""{"horizontal":1e400,"vertical":0.25}""")]
    [InlineData("""{"horizontal":"left","vertical":0.5}""")]          // not a number
    [InlineData("""{"horizontal":0.5,"vertical":true}""")]
    [InlineData("""{"horizontal":{"x":1},"vertical":0.5}""")]
    [InlineData("""{"width":0,"height":300}""")]                      // not a positive length
    [InlineData("""{"width":400,"height":-5}""")]
    [InlineData("""{"width":1e400}""")]
    [InlineData("""{"width":"wide","height":300}""")]
    public async Task Load_AnythingElse_IsUnset_AllOrNothingWithinEachPreference(string? stored)
    {
        ExpectStored(stored);
        var store = NewStore();

        await store.EnsureLoadedAsync();

        Assert.Equal(NotesPreferences.Unset, store.Current);
        _plan.Verify();
    }

    [Theory]
    [InlineData("""{"horizontal":0.25,"vertical":0.75,"width":"wide","height":300}""")]
    [InlineData("""{"horizontal":0.25,"vertical":0.75,"width":0,"height":300}""")]
    [InlineData("""{"horizontal":0.25,"vertical":0.75,"width":400,"height":-1}""")]
    public async Task Load_ABadSizeField_LeavesThePlacementStanding_AndTheSizeUnset(string stored)
    {
        ExpectStored(stored);
        var store = NewStore();

        await store.EnsureLoadedAsync();

        Assert.Equal(P(0.25, 0.75), store.Current);
        _plan.Verify();
    }

    [Theory]
    [InlineData("""{"horizontal":"left","vertical":0.75,"width":400,"height":300}""")]
    [InlineData("""{"horizontal":1.5,"vertical":0.75,"width":400,"height":300}""")]
    [InlineData("""{"horizontal":0.25,"vertical":true,"width":400,"height":300}""")]
    public async Task Load_ABadPlacementField_LeavesTheSizeStanding_AndThePlacementUnset(string stored)
    {
        ExpectStored(stored);
        var store = NewStore();

        await store.EnsureLoadedAsync();

        Assert.Equal(P(null, null, 400, 300), store.Current);
        _plan.Verify();
    }

    [Fact]
    public async Task Load_AReadTheBrowserRefuses_IsUnset_AndDoesNotThrow()
    {
        _plan.ExpectRead(BrowserStorageArea.Local, NotesPreferencesStore.StorageKey, BrowserStorageReadAnswer.Refused);
        var store = NewStore();

        await store.EnsureLoadedAsync();

        Assert.Equal(NotesPreferences.Unset, store.Current);
        AssertReadRefusalSaid();
        _plan.Verify();
    }

    [Fact]
    public async Task Load_ReadsStorageOnce_HoweverManyAsk()
    {
        // One read declared: a second would be an undeclared call, and fail.
        ExpectStored("""{"horizontal":0.25,"vertical":0.75}""");
        var store = NewStore();

        await store.EnsureLoadedAsync();
        await store.EnsureLoadedAsync();

        _plan.Verify();
    }

    [Fact]
    public async Task Set_WritesThePinnedWireFormat()
    {
        // The exact bytes: all four fields always written, an unset axis as
        // JSON null, field order horizontal, vertical, width, height — the
        // placement's two as they were, the size's appended. A change here is
        // a change to a format browsers already hold. Writes match by value,
        // and the order is required, so these literals are the whole statement.
        var first = _plan.ExpectWrite(
            BrowserStorageArea.Local, "xg_notesPlacement",
            """{"horizontal":0.625,"vertical":null,"width":null,"height":null}""",
            BrowserStorageWriteAnswer.Succeeded);
        var second = _plan.ExpectWrite(
            BrowserStorageArea.Local, "xg_notesPlacement",
            """{"horizontal":0,"vertical":1,"width":400,"height":312.5}""",
            BrowserStorageWriteAnswer.Succeeded);
        var third = _plan.ExpectWrite(
            BrowserStorageArea.Local, "xg_notesPlacement",
            """{"horizontal":null,"vertical":null,"width":null,"height":250}""",
            BrowserStorageWriteAnswer.Succeeded);
        _plan.RequireOrder(first, second);
        _plan.RequireOrder(second, third);
        var store = NewStore();

        await store.SetAsync(P(0.625, null));
        await store.SetAsync(P(0, 1, 400, 312.5));
        await store.SetAsync(P(null, null, null, 250));

        Assert.Equal(P(null, null, null, 250), store.Current);
        _plan.Verify();
    }

    [Theory]
    [InlineData(0.0, 1.0, 400.0, 300.0, """{"horizontal":0,"vertical":1,"width":400,"height":300}""")]
    [InlineData(0.1 + 0.2, null, null, null, """{"horizontal":0.30000000000000004,"vertical":null,"width":null,"height":null}""")]
    [InlineData(null, 0.3333333333333333, 676.5, null, """{"horizontal":null,"vertical":0.3333333333333333,"width":676.5,"height":null}""")]
    [InlineData(1.0, 0.000123, null, 128.25, """{"horizontal":1,"vertical":0.000123,"width":null,"height":128.25}""")]
    public async Task WhatIsWritten_ReadsBackAsTheSamePreferences(
        double? horizontal, double? vertical, double? width, double? height, string written)
    {
        // Written by one app boot, read by the next: the literal is what the
        // first store writes (a different value would be an undeclared write)
        // and what the second one reads.
        ExpectWritten(written, BrowserStorageWriteAnswer.Succeeded);
        ExpectStored(written);
        var preferences = P(horizontal, vertical, width, height);

        await NewStore().SetAsync(preferences);
        var next = NewStore();
        await next.EnsureLoadedAsync();

        Assert.Equal(preferences, next.Current);
        _plan.Verify();
    }

    [Fact]
    public async Task SetUnset_RemovesTheEntry()
    {
        // Reset: the browser keeps nothing when both preferences are unset.
        _plan.ExpectRemove(BrowserStorageArea.Local, NotesPreferencesStore.StorageKey, BrowserStorageWriteAnswer.Succeeded);
        var store = NewStore();

        await store.SetAsync(NotesPreferences.Unset);

        Assert.Equal(NotesPreferences.Unset, store.Current);
        _plan.Verify();
    }

    [Fact]
    public async Task OnePreferenceSet_IsWritten_NotRemoved()
    {
        // An unset placement with a set size is a set entry: only both unset removes it.
        ExpectWritten("""{"horizontal":null,"vertical":null,"width":400,"height":null}""", BrowserStorageWriteAnswer.Succeeded);

        await NewStore().SetAsync(P(null, null, 400));

        _plan.Verify();
    }

    [Fact]
    public async Task AWriteTheBrowserRefuses_KeepsBothPreferencesForTheSession()
    {
        ExpectWritten("""{"horizontal":0.75,"vertical":0.25,"width":400,"height":300}""", BrowserStorageWriteAnswer.Refused);
        var store = NewStore();

        await store.SetAsync(P(0.75, 0.25, 400, 300));

        Assert.Equal(P(0.75, 0.25, 400, 300), store.Current);
        AssertWriteRefusalSaid();
        _plan.Verify();
    }

    [Fact]
    public async Task ARemovalTheBrowserRefuses_StillResetsTheSession()
    {
        ExpectWritten("""{"horizontal":0.75,"vertical":0.25,"width":400,"height":300}""", BrowserStorageWriteAnswer.Succeeded);
        _plan.ExpectRemove(BrowserStorageArea.Local, NotesPreferencesStore.StorageKey, BrowserStorageWriteAnswer.Refused);
        var store = NewStore();
        await store.SetAsync(P(0.75, 0.25, 400, 300));

        await store.SetAsync(NotesPreferences.Unset);

        Assert.Equal(NotesPreferences.Unset, store.Current);
        AssertWriteRefusalSaid("removal");
        _plan.Verify();
    }

    [Fact]
    public async Task ARefusedWrite_DisablesNoLaterOne()
    {
        // No latch (halheinrich/backgammon#374): the next gesture is written
        // all the same, and the one refusal is the only thing said.
        ExpectWritten("""{"horizontal":0.75,"vertical":0.25,"width":null,"height":null}""", BrowserStorageWriteAnswer.Refused);
        ExpectWritten("""{"horizontal":0.5,"vertical":0.5,"width":400,"height":null}""", BrowserStorageWriteAnswer.Succeeded);
        var store = NewStore();

        await store.SetAsync(P(0.75, 0.25));
        await store.SetAsync(P(0.5, 0.5, 400));

        AssertWriteRefusalSaid();
        _plan.Verify();
    }

    [Fact]
    public async Task ALoadThatLandsAfterAGesture_DoesNotPutTheStoredValueBack()
    {
        // The read is still in flight when the user's resize lands; the stored
        // value it brings back is older than the resize, so the resize stands.
        var read = _plan.ExpectHeldRead(BrowserStorageArea.Local, NotesPreferencesStore.StorageKey);
        ExpectWritten("""{"horizontal":1,"vertical":0,"width":400,"height":null}""", BrowserStorageWriteAnswer.Succeeded);
        var store = NewStore();
        var loading = store.EnsureLoadedAsync();
        Assert.True(read.IsReached);

        await store.SetAsync(P(1, 0, 400));
        read.Release(BrowserStorageReadAnswer.Stored("""{"horizontal":0.25,"vertical":0.75,"width":600,"height":300}"""));
        await loading.WaitAsync(DefaultWaitTimeout); // the load's continuation has run

        Assert.Equal(P(1, 0, 400), store.Current);
        _plan.Verify();
    }
}
