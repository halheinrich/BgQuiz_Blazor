namespace BgQuiz_Blazor.Client.Quiz;

using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using XgFilter_Razor;

/// <summary>
/// The filter surface's route into this app's one storage condition: the
/// <see cref="IFilterStorageRefusalSink"/> registered with
/// <c>AddFilterSurface</c> (halheinrich/backgammon#374), which XgFilter_Razor
/// tells of every browser-storage call of its own that the browser refuses —
/// the remembered selection's read and writes, and the panel's two display
/// preferences.
///
/// <para>
/// <b>An adapter in front of <see cref="BrowserStorageCondition"/>, not a
/// second holder.</b> The condition is one fact however it arose
/// (<c>SPEC-notices.md</c> §2), so the surface's refusals reach the same
/// occurrence BgQuiz's own stores report to, and <c>Home</c>'s one storage
/// notice says it. The condition deduplicates — the first refusal begins it,
/// every later one is the same occurrence — so this forwards every refusal the
/// surface reports, as the producer's contract asks.
/// </para>
///
/// <para>
/// <b>It logs, because nothing else does.</b> Each of BgQuiz's stores logs its
/// own refusal with what it costs that store, and the condition adds no log of
/// its own; the surface logs nothing. So the surface's log line is written
/// here, with the browser's exception attached, and keeping it here is what
/// leaves <see cref="BrowserStorageCondition"/> a holder with no logger.
/// </para>
///
/// <para>
/// <b>App-scoped, like the condition it feeds</b>, so a refusal that arrives
/// after the page that started the call has gone — a commit's write answered
/// after the user navigated away — still lands, and a page mounted later shows
/// it with no further failure. Called on the renderer's synchronization
/// context; it must not throw, and it does nothing that can.
/// </para>
/// </summary>
internal sealed class FilterStorageRefusalSink(
    BrowserStorageCondition condition, ILogger<FilterStorageRefusalSink> logger) : IFilterStorageRefusalSink
{
    /// <inheritdoc />
    public void ReportRefused(JSException refusal)
    {
        logger.LogWarning(refusal,
            "The browser refused a storage call the filter panel made; its filters work for this visit "
            + "but may not be remembered.");
        condition.ReportRefused();
    }
}
