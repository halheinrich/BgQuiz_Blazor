using BgQuiz_Blazor.Client.Quiz;
using Bunit;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="QuizLiveMarker"/> where the browser refuses its
/// <c>sessionStorage</c> (issue <c>halheinrich/backgammon#360</c>): its own
/// calls can be refused, and the marker is read in <c>Home</c>'s first
/// render, so a refusal costs the reload notice and nothing more — logged,
/// reported, never thrown. Its lifecycle across the
/// pages is <c>PageTests</c>'. Extends <see cref="BunitContext"/> only for the
/// JSInterop double.
/// </summary>
public class QuizLiveMarkerTests : BunitContext
{
    private readonly BrowserStorageCondition _storage = new();
    private readonly RecordingLogger<QuizLiveMarker> _log = new();

    public QuizLiveMarkerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
    }

    private QuizLiveMarker NewMarker() => new(JSInterop.JSRuntime, _log, _storage);

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
        JSInterop.Setup<string?>("sessionStorage.getItem", QuizLiveMarker.StorageKey)
            .SetException(new JSException("SecurityError: The operation is insecure."));

        Assert.False(await NewMarker().WasLiveAsync());

        AssertRefusalSaid();
    }

    [Fact]
    public async Task AWriteTheBrowserRefuses_ThrowsNothing_AndIsSaid()
    {
        JSInterop.SetupVoid("sessionStorage.setItem", _ => true)
            .SetException(new JSException("SecurityError: The operation is insecure."));

        await NewMarker().MarkLiveAsync();

        AssertRefusalSaid();
    }

    [Fact]
    public async Task ARemovalTheBrowserRefuses_ThrowsNothing_AndIsSaid()
    {
        JSInterop.SetupVoid("sessionStorage.removeItem", QuizLiveMarker.StorageKey)
            .SetException(new JSException("SecurityError: The operation is insecure."));

        await NewMarker().ClearAsync();

        AssertRefusalSaid();
    }

    [Fact]
    public async Task StorageThatAnswers_ReportsNothing()
    {
        // The control: the same three calls, answered, say nothing anywhere.
        JSInterop.Setup<string?>("sessionStorage.getItem", QuizLiveMarker.StorageKey).SetResult("1");
        JSInterop.SetupVoid("sessionStorage.setItem", _ => true).SetVoidResult();
        JSInterop.SetupVoid("sessionStorage.removeItem", QuizLiveMarker.StorageKey).SetVoidResult();
        var marker = NewMarker();

        Assert.True(await marker.WasLiveAsync());
        await marker.MarkLiveAsync();
        await marker.ClearAsync();

        Assert.Empty(_log.Entries);
        Assert.Null(_storage.Occurrence);
    }
}
