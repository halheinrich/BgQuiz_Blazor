namespace BgQuiz_Blazor.Client.Quiz;

using BgUiPrimitives_Razor;

/// <summary>
/// The mark that a quiz is live in this tab, kept where a full reload cannot erase it. The
/// coordinator is its one writer, since it is the one owner of whether a run is live; Home
/// reads it on a boot that finds no run at all, says the reload reset the quiz, and clears
/// it. The honesty slice, not reload-resume: it explains the loss, it does not prevent it.
/// </summary>
internal interface IQuizLiveMarker
{
    /// <summary>Record that a quiz is live in this tab. Never throws for storage.</summary>
    ValueTask MarkLiveAsync();

    /// <summary>Record that no quiz is live in this tab. Never throws for storage.</summary>
    ValueTask ClearAsync();

    /// <summary>Whether a quiz was marked live and never cleared: after a boot, the sign of a reload mid-quiz. Never throws for storage.</summary>
    ValueTask<bool> WasLiveAsync();
}

/// <summary>
/// <see cref="IQuizLiveMarker"/> over the browser's <c>sessionStorage</c>, deliberately not
/// <c>localStorage</c>: per tab, surviving a reload and dying with the tab, which is exactly the
/// mark's meaning; a shared area would have a fresh tab announce a reset for a quiz it never
/// ran. Storage that refuses costs only the reset notice: a refused read reads as no quiz having
/// been live, a refused write or removal leaves things as they were, each is logged and reported
/// to <see cref="BrowserStorageCondition"/> by <see cref="ReportingBrowserStorage"/>, and none
/// throws.
/// </summary>
internal sealed class QuizLiveMarker(ReportingBrowserStorage storage) : IQuizLiveMarker
{
    internal const string StorageKey = "bgquiz.quizLive";

    private readonly ReportingBrowserStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));

    /// <inheritdoc />
    public async ValueTask MarkLiveAsync() =>
        await _storage.WriteAsync(BrowserStorageArea.Session, StorageKey, "1",
            "a reload during this quiz will not be explained");

    /// <inheritdoc />
    public async ValueTask ClearAsync() =>
        await _storage.RemoveAsync(BrowserStorageArea.Session, StorageKey,
            "the quiz-live mark may remain set");

    /// <inheritdoc />
    public async ValueTask<bool> WasLiveAsync()
    {
        var read = await _storage.ReadAsync(BrowserStorageArea.Session, StorageKey, "no reload is reported");
        return read.IsStored;
    }
}
