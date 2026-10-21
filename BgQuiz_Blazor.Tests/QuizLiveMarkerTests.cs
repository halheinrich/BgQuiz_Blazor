using BgDataTypes_Lib.TestSupport;
using BgQuiz_Blazor.Client.Quiz;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;

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
    private readonly RecordingLogger<ReportingBrowserStorage> _log = new();

    private const string ReadConsequence = "no reload is reported";
    private const string WriteConsequence = "a reload during this quiz will not be explained";
    private const string RemovalConsequence = "the quiz-live mark stays set";

    public QuizLiveMarkerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        _plan = BrowserStoragePlan.On(JSInterop);
    }

    private QuizLiveMarker NewMarker() =>
        new(new ReportingBrowserStorage(new BrowserStorage(JSInterop.JSRuntime), _storage, _log));

    private void AssertRefusalSaid(string operation, string consequence)
    {
        StorageRefusalLog.AssertSaidOnce(_log, operation, QuizLiveMarker.StorageKey, consequence);
        Assert.True(_storage.HasOccurred);
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

        AssertRefusalSaid("read", ReadConsequence);
        _plan.Verify();
    }

    [Fact]
    public async Task ARefusedWrite_OrRemoval_DoesNotThrow_AndIsSaidOnceEach()
    {
        _plan.ExpectWrite(BrowserStorageArea.Session, QuizLiveMarker.StorageKey, "1", BrowserStorageWriteAnswer.Refused);
        var marker = NewMarker();

        await marker.MarkLiveAsync();
        AssertRefusalSaid("write", WriteConsequence);

        _plan.ExpectRemove(BrowserStorageArea.Session, QuizLiveMarker.StorageKey, BrowserStorageWriteAnswer.Refused);
        await marker.ClearAsync();

        Assert.Equal(2, _log.Entries.Count);
        StorageRefusalLog.AssertSaid(_log.Entries[^1], "removal", QuizLiveMarker.StorageKey, RemovalConsequence);
        _plan.Verify();
    }
}
