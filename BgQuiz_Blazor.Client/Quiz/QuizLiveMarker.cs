namespace BgQuiz_Blazor.Client.Quiz;

using Microsoft.JSInterop;

/// <summary>
/// Per-app marker recording that a quiz is currently <i>live</i> in this browser
/// tab, backed by the browser's <c>sessionStorage</c> through
/// <see cref="IJSRuntime"/>. BgQuiz's first JS-interop <i>service</i> — the
/// clipboard and localStorage calls elsewhere are inline in their components;
/// this one is encapsulated because it has a lifecycle (set / read / clear)
/// spread across two pages, and the storage choice carries a subtle constraint
/// (below) worth stating once.
///
/// <para>
/// <b>Why it exists.</b> A full browser reload re-boots the WASM runtime and
/// silently discards all in-memory quiz state (<see cref="QuizController"/> and
/// the start-gate holders all reset — reload-survival is a deferred arc). The
/// user lands on a fresh <c>Home</c> with no hint their quiz vanished. This
/// marker is the one thing that <i>does</i> survive a reload, so a fresh boot
/// that finds it can honestly say "your quiz was reset by the reload" rather
/// than pretending nothing happened. This is the honesty slice, not
/// reload-resume: it explains the loss, it does not prevent it.
/// </para>
///
/// <para>
/// Lifetime: <b>Scoped</b> — the same per-app (one-per-tab in WASM) lifetime as
/// the start-gate holders. It is <b>set</b> wherever a quiz becomes live —
/// <c>Home</c> on a successful Start and <c>Done</c> on Restart — and
/// <b>cleared</b> by <c>Done</c> on honest completion and by <c>Home</c>'s
/// boot-read once it has shown the notice. <c>Home</c> reads it on boot. The read
/// is gated on
/// <see cref="QuizController.HasStarted"/> being <c>false</c> — a set marker with
/// a <i>live</i> controller is in-app navigation back to <c>Home</c> mid-quiz
/// (the runtime, and the quiz, survived), not a reload, so no notice fires.
/// </para>
///
/// <para>
/// <b>Storage is <c>sessionStorage</c>, deliberately — not <c>localStorage</c>.</b>
/// <c>sessionStorage</c> is per-tab: it survives a reload but is invisible to
/// other tabs and dies with the tab — exactly the marker's semantics.
/// <c>localStorage</c> is shared across all tabs of the origin, so a quiz live in
/// tab A would set a marker that a freshly-opened tab B reads on its first boot,
/// making B falsely announce "your quiz was reset" for a quiz it never ran. Do
/// not "upgrade" this to <c>localStorage</c>.
/// </para>
/// </summary>
internal sealed class QuizLiveMarker
{
    /// <summary>
    /// The <c>sessionStorage</c> key. Namespaced so it can't collide with the
    /// filter panel's own <c>localStorage</c> keys (a different store anyway).
    ///
    /// <para>
    /// <b><c>internal</c>, and named for its sibling.</b> <c>Help</c>'s data
    /// section names this entry to the user and renders it from here, so the
    /// name a reader verifies in devtools cannot drift from the name this type
    /// actually writes — the same discipline <see cref="QuizStatsFile.FileName"/>
    /// and <see cref="PickedFileLimits"/> established, and the posture
    /// <c>FilterPanel</c>'s own key constants take for <c>FilterHelp</c>. It is
    /// widened exactly as far as that one doc surface needs: <c>internal</c>,
    /// never <c>public</c> (the test project sees it through
    /// <c>InternalsVisibleTo</c>). It was renamed from <c>Key</c> when it became
    /// documented surface, to match <see cref="MixDraft.StorageKey"/> — the two
    /// are rendered side by side in that section, and a documented pair reading
    /// <c>Key</c> / <c>StorageKey</c> would invite the reader to look for a
    /// distinction that isn't there.
    /// </para>
    /// </summary>
    internal const string StorageKey = "bgquiz.quizLive";

    private readonly IJSRuntime _js;

    public QuizLiveMarker(IJSRuntime js)
    {
        _js = js ?? throw new ArgumentNullException(nameof(js));
    }

    /// <summary>Record that a quiz is now live in this tab.</summary>
    public ValueTask MarkLiveAsync() =>
        _js.InvokeVoidAsync("sessionStorage.setItem", StorageKey, "1");

    /// <summary>
    /// True when the marker is present — i.e. a quiz <i>was</i> live in this tab
    /// (before a reload, if the caller has confirmed the runtime is fresh). Any
    /// stored value counts; only <see cref="MarkLiveAsync"/> ever writes one.
    /// </summary>
    public async ValueTask<bool> WasLiveAsync() =>
        await _js.InvokeAsync<string?>("sessionStorage.getItem", StorageKey) is not null;

    /// <summary>
    /// Clear the marker — on honest quiz completion (Done) or once the reset
    /// notice has been shown, so it fires only once per reload.
    /// </summary>
    public ValueTask ClearAsync() =>
        _js.InvokeVoidAsync("sessionStorage.removeItem", StorageKey);
}
