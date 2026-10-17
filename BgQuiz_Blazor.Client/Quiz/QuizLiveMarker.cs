namespace BgQuiz_Blazor.Client.Quiz;

using BgUiPrimitives_Razor;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

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
/// to <see cref="BrowserStorageCondition"/>, and none throws.
/// </summary>
internal sealed class QuizLiveMarker(BrowserStorage browser, ILogger<QuizLiveMarker> logger, BrowserStorageCondition storage)
    : IQuizLiveMarker
{
    internal const string StorageKey = "bgquiz.quizLive";

    private readonly BrowserStorage _browser = browser ?? throw new ArgumentNullException(nameof(browser));
    private readonly ILogger<QuizLiveMarker> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly BrowserStorageCondition _storage = storage ?? throw new ArgumentNullException(nameof(storage));

    /// <inheritdoc />
    public async ValueTask MarkLiveAsync()
    {
        var written = await _browser.WriteAsync(BrowserStorageArea.Session, StorageKey, "1");
        if (written.IsRefused)
            Refused(written.Refusal, "could not be set; a reload during this quiz will not be explained");
    }

    /// <inheritdoc />
    public async ValueTask ClearAsync()
    {
        var removed = await _browser.RemoveAsync(BrowserStorageArea.Session, StorageKey);
        if (removed.IsRefused)
            Refused(removed.Refusal, "could not be cleared");
    }

    /// <inheritdoc />
    public async ValueTask<bool> WasLiveAsync()
    {
        var read = await _browser.ReadAsync(BrowserStorageArea.Session, StorageKey);
        if (read.IsRefused)
            Refused(read.Refusal, "could not be read; no reload is reported");
        return read.IsStored;
    }

    private void Refused(JSException refusal, string consequence)
    {
        _logger.LogWarning(refusal, "The quiz-live marker in browser storage ({Key}) {Consequence}.", StorageKey, consequence);
        _storage.ReportRefused();
    }
}
