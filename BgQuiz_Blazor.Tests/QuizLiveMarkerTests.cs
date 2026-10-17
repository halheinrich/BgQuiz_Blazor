using BgQuiz_Blazor.Client.Quiz;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="QuizLiveMarker"/>: the live mark in the tab's <c>sessionStorage</c>, under its
/// one key, and the degradation when the browser refuses — a refused read is no quiz having
/// been live, a refused write or removal leaves things as they were, each said once and
/// reported, none thrown. Storage is planned, so the real <see cref="BrowserStorage"/> runs
/// and a call nobody declared fails.
/// </summary>
public class QuizLiveMarkerTests : BunitContext
{
    private readonly BrowserStoragePlan _plan;
    private readonly BrowserStorageCondition _storage = new();
    private readonly RecordingLogger<QuizLiveMarker> _log = new();

    public QuizLiveMarkerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        _plan = BrowserStoragePlan.On(JSInterop);
    }

    private QuizLiveMarker NewMarker() => new(new BrowserStorage(JSInterop.JSRuntime), _log, _storage);

    private void AssertRefusalSaid()
    {
        var entry = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<JSException>(entry.Exception);
        Assert.NotNull(_storage.Occurrence);
    }

    [Fact]
    public async Task MarkLive_WritesTheMark_InTheTabsSessionStorage()
    {
        _plan.ExpectWrite(BrowserStorageArea.Session, QuizLiveMarker.StorageKey, "1", BrowserStorageWriteAnswer.Succeeded);

        await NewMarker().MarkLiveAsync();

        _plan.Verify();
        Assert.Empty(_log.Entries);
    }

    [Fact]
    public async Task Clear_RemovesTheMark()
    {
        _plan.ExpectRemove(BrowserStorageArea.Session, QuizLiveMarker.StorageKey, BrowserStorageWriteAnswer.Succeeded);

        await NewMarker().ClearAsync();

        _plan.Verify();
    }

    [Fact]
    public async Task WasLive_ReadsTheMark_StoredOrAbsent()
    {
        _plan.ExpectRead(BrowserStorageArea.Session, QuizLiveMarker.StorageKey, BrowserStorageReadAnswer.Stored("1"));
        Assert.True(await NewMarker().WasLiveAsync());
        _plan.Verify();

        _plan.ExpectRead(BrowserStorageArea.Session, QuizLiveMarker.StorageKey, BrowserStorageReadAnswer.Absent);
        Assert.False(await NewMarker().WasLiveAsync());
        _plan.Verify();
    }

    [Fact]
    public async Task ARefusedRead_IsNoQuizHavingBeenLive_SaidOnce()
    {
        _plan.ExpectRead(BrowserStorageArea.Session, QuizLiveMarker.StorageKey, BrowserStorageReadAnswer.Refused);

        Assert.False(await NewMarker().WasLiveAsync());

        AssertRefusalSaid();
        _plan.Verify();
    }

    [Fact]
    public async Task ARefusedWrite_OrRemoval_DoesNotThrow_AndIsSaidOnceEach()
    {
        _plan.ExpectWrite(BrowserStorageArea.Session, QuizLiveMarker.StorageKey, "1", BrowserStorageWriteAnswer.Refused);
        var marker = NewMarker();

        await marker.MarkLiveAsync();
        AssertRefusalSaid();

        _plan.ExpectRemove(BrowserStorageArea.Session, QuizLiveMarker.StorageKey, BrowserStorageWriteAnswer.Refused);
        await marker.ClearAsync();

        Assert.Equal(2, _log.Entries.Count);
        _plan.Verify();
    }
}
