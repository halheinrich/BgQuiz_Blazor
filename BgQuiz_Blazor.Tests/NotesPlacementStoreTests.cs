using BgQuiz_Blazor.Client.Quiz;
using Bunit;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="NotesPlacementStore"/>, the placement preference's one reader
/// and writer (<c>SPEC-quiz-view.md</c> §4, "The notes overlay's placement is a
/// remembered preference": "storage that is missing, malformed, out of range
/// or unavailable reads as unset, a failed write keeps the preference in
/// memory for the session, and neither ever stops the notes"; issue
/// <c>halheinrich/backgammon#344</c>). Pinned here: the tolerant read, the
/// wire format byte for byte, Reset removing the entry, and a failed write or
/// a late read never costing the session its placement. Extends
/// <see cref="BunitContext"/> only for the JSInterop double behind storage.
/// </summary>
public class NotesPlacementStoreTests : BunitContext
{
    public NotesPlacementStoreTests()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
    }

    /// <summary>The app's one storage fact, which a refusal here is reported to (halheinrich/backgammon#360).</summary>
    private readonly BrowserStorageCondition _storage = new();

    private readonly RecordingLogger<NotesPlacementStore> _log = new();

    private NotesPlacementStore NewStore() => new(JSInterop.JSRuntime, _log, _storage);

    /// <summary>A refusal is said twice, and only twice: one warning carrying the browser's exception, and the report.</summary>
    private void AssertRefusalSaid()
    {
        var entry = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<JSException>(entry.Exception);
        Assert.NotNull(_storage.Occurrence);
    }

    private void StageStored(string? json) =>
        JSInterop.Setup<string?>("localStorage.getItem", NotesPlacementStore.StorageKey).SetResult(json);

    private IReadOnlyList<JSRuntimeInvocation> Writes() =>
        [.. JSInterop.Invocations.Where(i => i.Identifier is "localStorage.setItem" or "localStorage.removeItem")];

    [Fact]
    public void TheKey_IsItsOwn_InTheXgFamily()
    {
        Assert.Equal("xg_notesPlacement", NotesPlacementStore.StorageKey);
        Assert.NotEqual(QuizSettings.StorageKey, NotesPlacementStore.StorageKey);
    }

    [Fact]
    public void BeforeLoading_ThePreferenceIsUnset() => Assert.Equal(NotesPlacement.Unset, NewStore().Current);

    [Theory]
    [InlineData("""{"horizontal":0.25,"vertical":0.75}""", 0.25, 0.75)]
    [InlineData("""{"horizontal":0,"vertical":1}""", 0.0, 1.0)]
    [InlineData("""{"horizontal":0.625,"vertical":null}""", 0.625, null)]
    [InlineData("""{"vertical":0.125}""", null, 0.125)]
    [InlineData("""{"horizontal":0.5,"vertical":0.5,"later":"a newer build's field"}""", 0.5, 0.5)]
    public async Task Load_AWellFormedPayload_IsThePreference(string stored, double? horizontal, double? vertical)
    {
        StageStored(stored);
        var store = NewStore();

        await store.EnsureLoadedAsync();

        Assert.Equal(NotesPlacement.Create(horizontal, vertical), store.Current);
        Assert.Null(_storage.Occurrence); // the control: a read that answers reports nothing
    }

    [Theory]
    [InlineData(null)]                                                // missing
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[0.25, 0.75]")]
    [InlineData("0.5")]
    [InlineData("""{"horizontal":null,"vertical":null}""")]
    [InlineData("""{"horizontal":1.5,"vertical":0.25}""")]           // out of range
    [InlineData("""{"horizontal":0.25,"vertical":-0.1}""")]
    [InlineData("""{"horizontal":1e400,"vertical":0.25}""")]
    [InlineData("""{"horizontal":"left","vertical":0.5}""")]          // not a number
    [InlineData("""{"horizontal":0.5,"vertical":true}""")]
    [InlineData("""{"horizontal":{"x":1},"vertical":0.5}""")]
    public async Task Load_AnythingElse_IsUnset_AllOrNothing(string? stored)
    {
        StageStored(stored);
        var store = NewStore();

        await store.EnsureLoadedAsync();

        Assert.Equal(NotesPlacement.Unset, store.Current);
    }

    [Fact]
    public async Task Load_StorageThatThrows_IsUnset_AndDoesNotThrow()
    {
        JSInterop.Setup<string?>("localStorage.getItem", NotesPlacementStore.StorageKey)
            .SetException(new JSException("SecurityError: storage is unavailable"));
        var store = NewStore();

        await store.EnsureLoadedAsync();

        Assert.Equal(NotesPlacement.Unset, store.Current);
        AssertRefusalSaid();
    }

    [Fact]
    public async Task Load_ReadsStorageOnce_HoweverManyAsk()
    {
        StageStored("""{"horizontal":0.25,"vertical":0.75}""");
        var store = NewStore();

        await store.EnsureLoadedAsync();
        await store.EnsureLoadedAsync();

        Assert.Single(JSInterop.Invocations["localStorage.getItem"]);
    }

    [Fact]
    public async Task Set_WritesThePinnedWireFormat()
    {
        // The exact bytes: both fields always written, an unset axis as JSON
        // null, field order horizontal then vertical. A change here is a change
        // to a format browsers already hold.
        JSInterop.SetupVoid("localStorage.setItem", _ => true).SetVoidResult();
        var store = NewStore();

        await store.SetAsync(NotesPlacement.Create(0.625, null));
        await store.SetAsync(NotesPlacement.Create(0, 1));

        var writes = Writes();
        Assert.Equal(2, writes.Count);
        Assert.Equal(new object?[] { "xg_notesPlacement", """{"horizontal":0.625,"vertical":null}""" }, writes[0].Arguments);
        Assert.Equal(new object?[] { "xg_notesPlacement", """{"horizontal":0,"vertical":1}""" }, writes[1].Arguments);
        Assert.Equal(NotesPlacement.Create(0, 1), store.Current);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(0.1 + 0.2, null)]
    [InlineData(null, 0.3333333333333333)]
    [InlineData(1.0, 0.000123)]
    public async Task WhatIsWritten_ReadsBackAsTheSamePlacement(double? horizontal, double? vertical)
    {
        JSInterop.SetupVoid("localStorage.setItem", _ => true).SetVoidResult();
        var placement = NotesPlacement.Create(horizontal, vertical);
        await NewStore().SetAsync(placement);
        var written = (string?)Writes().Single().Arguments[1];

        StageStored(written);
        var next = NewStore();
        await next.EnsureLoadedAsync();

        Assert.Equal(placement, next.Current);
    }

    [Fact]
    public async Task SetUnset_RemovesTheEntry()
    {
        // Reset: the browser keeps nothing for an unset preference.
        JSInterop.SetupVoid("localStorage.removeItem", NotesPlacementStore.StorageKey).SetVoidResult();
        var store = NewStore();

        await store.SetAsync(NotesPlacement.Unset);

        var write = Assert.Single(Writes());
        Assert.Equal("localStorage.removeItem", write.Identifier);
        Assert.Equal(NotesPlacement.Unset, store.Current);
    }

    [Fact]
    public async Task AWriteThatFails_KeepsThePlacementForTheSession()
    {
        JSInterop.SetupVoid("localStorage.setItem", _ => true)
            .SetException(new JSException("QuotaExceededError"));
        var store = NewStore();

        await store.SetAsync(NotesPlacement.Create(0.75, 0.25));

        Assert.Equal(NotesPlacement.Create(0.75, 0.25), store.Current);
        AssertRefusalSaid();
    }

    [Fact]
    public async Task ARemovalThatFails_StillResetsTheSession()
    {
        JSInterop.SetupVoid("localStorage.removeItem", NotesPlacementStore.StorageKey)
            .SetException(new JSException("SecurityError"));
        JSInterop.SetupVoid("localStorage.setItem", _ => true).SetVoidResult();
        var store = NewStore();
        await store.SetAsync(NotesPlacement.Create(0.75, 0.25));

        await store.SetAsync(NotesPlacement.Unset);

        Assert.Equal(NotesPlacement.Unset, store.Current);
        AssertRefusalSaid();
    }

    [Fact]
    public async Task ALoadThatLandsAfterAMove_DoesNotPutTheStoredValueBack()
    {
        // The read is still in flight when the user's move lands; the stored
        // value it brings back is older than the move, so the move stands.
        var read = JSInterop.Setup<string?>("localStorage.getItem", NotesPlacementStore.StorageKey);
        JSInterop.SetupVoid("localStorage.setItem", _ => true).SetVoidResult();
        var store = NewStore();
        var loading = store.EnsureLoadedAsync();

        await store.SetAsync(NotesPlacement.Create(1, 0));
        read.SetResult("""{"horizontal":0.25,"vertical":0.75}""");
        await loading;

        Assert.Equal(NotesPlacement.Create(1, 0), store.Current);
    }
}
