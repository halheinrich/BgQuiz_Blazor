using BgQuiz_Blazor.Client.Quiz;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="QuizLiveMarker"/> where the browser refuses its
/// <c>sessionStorage</c> (issue <c>halheinrich/backgammon#360</c>): its own
/// calls can be refused, and the marker is read in <c>Home</c>'s first
/// render, so a refusal costs the reload notice and nothing more — logged,
/// reported, never thrown. Its lifecycle across the pages is
/// <c>PageTests</c>'. Storage is planned with <see cref="BrowserStoragePlan"/>,
/// so the real <see cref="BrowserStorage"/> runs and no interop call is spelled
/// here; every test verifies the plan.
/// </summary>
public class QuizLiveMarkerTests : BunitContext
{
    private readonly BrowserStorageCondition _storage = new();
    private readonly RecordingLogger<QuizLiveMarker> _log = new();
    private readonly BrowserStoragePlan _plan;

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
    public async Task AReadTheBrowserRefuses_ReadsAsNoQuizLive_AndIsSaid()
    {
        _plan.ExpectRead(BrowserStorageArea.Session, QuizLiveMarker.StorageKey, BrowserStorageReadAnswer.Refused);

        Assert.False(await NewMarker().WasLiveAsync());

        AssertRefusalSaid();
        _plan.Verify();
    }

    [Fact]
    public async Task AWriteTheBrowserRefuses_ThrowsNothing_AndIsSaid()
    {
        _plan.ExpectWrite(
            BrowserStorageArea.Session, QuizLiveMarker.StorageKey, "1", BrowserStorageWriteAnswer.Refused);

        await NewMarker().MarkLiveAsync();

        AssertRefusalSaid();
        _plan.Verify();
    }

    [Fact]
    public async Task ARemovalTheBrowserRefuses_ThrowsNothing_AndIsSaid()
    {
        _plan.ExpectRemove(BrowserStorageArea.Session, QuizLiveMarker.StorageKey, BrowserStorageWriteAnswer.Refused);

        await NewMarker().ClearAsync();

        AssertRefusalSaid();
        _plan.Verify();
    }

    [Fact]
    public async Task ARefusal_DisablesNoLaterCall()
    {
        // No latch (halheinrich/backgammon#374): a refused call is answered
        // as a refusal and the next one is made all the same — here a read
        // refused, then the same read served.
        _plan.ExpectRead(BrowserStorageArea.Session, QuizLiveMarker.StorageKey, BrowserStorageReadAnswer.Refused);
        _plan.ExpectRead(BrowserStorageArea.Session, QuizLiveMarker.StorageKey, BrowserStorageReadAnswer.Stored("1"));
        var marker = NewMarker();

        Assert.False(await marker.WasLiveAsync());
        Assert.True(await marker.WasLiveAsync());

        _plan.Verify();
    }

    [Fact]
    public async Task StorageThatAnswers_ReportsNothing()
    {
        // The control: the same three calls, answered, say nothing anywhere.
        // Any stored value counts as live, the empty string included — only an
        // absent item is "no quiz".
        _plan.ExpectRead(BrowserStorageArea.Session, QuizLiveMarker.StorageKey, BrowserStorageReadAnswer.Stored(""));
        _plan.ExpectRead(BrowserStorageArea.Session, QuizLiveMarker.StorageKey, BrowserStorageReadAnswer.Absent);
        _plan.ExpectWrite(
            BrowserStorageArea.Session, QuizLiveMarker.StorageKey, "1", BrowserStorageWriteAnswer.Succeeded);
        _plan.ExpectRemove(BrowserStorageArea.Session, QuizLiveMarker.StorageKey, BrowserStorageWriteAnswer.Succeeded);
        var marker = NewMarker();

        Assert.True(await marker.WasLiveAsync());
        Assert.False(await marker.WasLiveAsync());
        await marker.MarkLiveAsync();
        await marker.ClearAsync();

        Assert.Empty(_log.Entries);
        Assert.Null(_storage.Occurrence);
        _plan.Verify();
    }
}
