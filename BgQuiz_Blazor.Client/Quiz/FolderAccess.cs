namespace BgQuiz_Blazor.Client.Quiz;

using Microsoft.AspNetCore.Components;

/// <summary>
/// Whether lifetime stats can be saved into the folder the user picked,
/// determined once at pick time and carried on <see cref="PickedProblemFolder"/>.
/// Only <see cref="Enabled"/> permits stats persistence; the other two are the
/// no-stats degrade reasons Home's pick-time status notice distinguishes.
/// </summary>
internal enum StatsSaveCapability
{
    /// <summary>
    /// The folder was picked via the File System Access API and the user
    /// granted readwrite permission — stats will be saved beside the corpus.
    /// </summary>
    Enabled,

    /// <summary>
    /// The browser has no <c>showDirectoryPicker</c> (the
    /// <c>webkitdirectory</c> fallback was used) — files are readable but no
    /// writable handle exists, so the quiz runs without saved stats.
    /// </summary>
    BrowserUnsupported,

    /// <summary>
    /// The browser supports the File System Access API but the user declined
    /// write permission — the quiz runs, stats are not saved.
    /// </summary>
    PermissionDenied,
}

/// <summary>
/// The result of one folder-pick gesture, whichever mechanism served it.
/// <see cref="Cancelled"/> <c>true</c> means the user dismissed the picker —
/// an expected outcome, not an error, carrying no other data. Otherwise
/// <see cref="Files"/> holds the folder's top-level <c>.xg</c> / <c>.xgp</c>
/// files fully buffered (extension-bearing names — the DecisionId-stamping
/// contract), and <see cref="Capability"/> says whether stats can be saved.
/// </summary>
/// <param name="Cancelled">True when the user dismissed the picker.</param>
/// <param name="DirectoryName">The picked folder's leaf name (empty when cancelled).</param>
/// <param name="Files">Top-level problem files, buffered (empty when cancelled).</param>
/// <param name="Capability">Whether stats can be saved into this folder.</param>
internal sealed record FolderPickOutcome(
    bool Cancelled,
    string DirectoryName,
    IReadOnlyList<PickedFile> Files,
    StatsSaveCapability Capability)
{
    /// <summary>The single cancelled outcome — no folder, no files, no capability claim.</summary>
    public static FolderPickOutcome CancelledOutcome { get; } =
        new(Cancelled: true, DirectoryName: "", Files: [], StatsSaveCapability.BrowserUnsupported);
}

/// <summary>
/// The app's one gateway to the browser's folder facilities — directory
/// picking (both mechanisms), buffered file reads, and the stats file's
/// read/write — backed by the <c>folderAccess.js</c> module. Pages and the
/// stats store depend on this interface; only the <see cref="JsFolderAccess"/>
/// implementation touches JS interop, and the browser-side directory handles
/// never cross the boundary at all (they live in JS module state).
///
/// <para>
/// <b>Two-slot model.</b> A pick populates the JS module's <i>picked</i> slot;
/// a quiz start promotes it to the <i>active</i> slot
/// (<see cref="PromoteToActiveAsync"/>), which the stats read/write pair then
/// operates on. The split is what isolates a running quiz from mid-quiz Clear
/// or re-pick: <see cref="ClearPickedAsync"/> resets only the picked slot, so
/// the active quiz keeps its stats handle until the next Start re-binds.
/// </para>
///
/// <para>
/// <b>Error signaling.</b> Expected outcomes are values, never exceptions: a
/// cancelled picker is <see cref="FolderPickOutcome.Cancelled"/>, a write
/// denial is <see cref="StatsSaveCapability.PermissionDenied"/>, a missing
/// stats file is a <c>null</c> read. Unexpected browser failures surface as
/// <see cref="Microsoft.JSInterop.JSException"/> for callers to catch and
/// degrade on.
/// </para>
/// </summary>
internal interface IFolderAccess
{
    /// <summary>
    /// True when the browser offers <c>showDirectoryPicker</c> (the File
    /// System Access path). Probed at pick time, per gesture — capability is a
    /// property of the moment, not of app boot.
    /// </summary>
    ValueTask<bool> SupportsDirectoryPickerAsync();

    /// <summary>
    /// Run the File System Access pick: native directory picker, readwrite
    /// permission request, top-level enumeration, and full buffering of every
    /// <c>.xg</c> / <c>.xgp</c> file. Call only when
    /// <see cref="SupportsDirectoryPickerAsync"/> is true.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The folder's matching files exceed <see cref="PickedFileLimits.MaxFileCount"/>.
    /// </exception>
    Task<FolderPickOutcome> PickFolderAsync();

    /// <summary>
    /// Open the hidden <c>webkitdirectory</c> input's native picker — the
    /// fallback gesture for browsers without File System Access. The eventual
    /// pick arrives via the input's own <c>change</c> event, handled with
    /// <see cref="CollectFallbackAsync"/>; a dismissal fires nothing.
    /// </summary>
    Task TriggerFallbackPickerAsync(ElementReference fallbackInput);

    /// <summary>
    /// Collect the fallback pick from the hidden input's FileList: filter to
    /// top-level <c>.xg</c> / <c>.xgp</c> entries (by <c>webkitRelativePath</c>
    /// depth — the browser hands over the whole tree) and buffer them. The
    /// outcome's capability is always
    /// <see cref="StatsSaveCapability.BrowserUnsupported"/>: this mechanism
    /// yields no writable handle. An empty FileList is a non-cancelled outcome
    /// with zero files.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The folder's matching files exceed <see cref="PickedFileLimits.MaxFileCount"/>.
    /// </exception>
    Task<FolderPickOutcome> CollectFallbackAsync(ElementReference fallbackInput);

    /// <summary>
    /// Promote the picked slot's directory handle to the active-quiz slot.
    /// Returns false when no File System Access handle is picked (fallback
    /// pick, cleared slot, or never picked) — the no-stats signal for the
    /// quiz being started. Called only from the stats store's quiz-begin bind.
    /// </summary>
    ValueTask<bool> PromoteToActiveAsync();

    /// <summary>
    /// Read <see cref="QuizStatsFile.FileName"/> from the <i>active</i> slot's
    /// folder. <c>null</c> means the file doesn't exist yet — a fresh corpus,
    /// not an error.
    /// </summary>
    Task<string?> ReadStatsJsonAsync();

    /// <summary>
    /// Write <paramref name="json"/> as <see cref="QuizStatsFile.FileName"/>
    /// into the <i>active</i> slot's folder, replacing any existing content.
    /// </summary>
    Task WriteStatsJsonAsync(string json);

    /// <summary>
    /// Clear the <i>picked</i> slot only — the active slot persists so a
    /// running quiz keeps recording. Pairs with Home's Clear affordance.
    /// </summary>
    ValueTask ClearPickedAsync();
}
