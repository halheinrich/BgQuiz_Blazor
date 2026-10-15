namespace BgQuiz_Blazor.Client.Quiz;

using System.Diagnostics.CodeAnalysis;
using BgFolderAccess_Razor;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using XgFilter_Razor;

/// <summary>
/// The app-scoped owner of the folder operations — a pick, a fallback
/// collection, a resample and a Clear — each from its first step through its
/// landing (halheinrich/backgammon#403). One may be in flight at a time, in
/// the whole app, whichever page began it: while one is
/// (<see cref="IsInFlight"/>), no pick, Clear, resample or Start begins from
/// any <c>Home</c>, and neither of <c>Done</c>'s Restarts begins. The pages
/// ask here and refuse at the handler, not only by disabling a control.
///
/// <para>
/// <b>Why it is not the page's.</b> An operation outlives the page that began
/// it: a resample, or a pick's acquisition, can still be running when the user
/// navigates away and back, and the page it began on is gone by then. Held on
/// that page, its exclusion and its progress were a field of one instance, so
/// a remounted <c>Home</c> could begin a competing pick or Clear, and the old
/// operation could then land into the new setup. Held here, they are the
/// app's, and the operation lands the same way whichever page is on screen —
/// or none.
/// </para>
///
/// <para>
/// <b>Exclusion and progress are two facts with two lifetimes.</b>
/// <see cref="IsInFlight"/> is held for the whole operation. On File System
/// Access that begins at the click — the reset, the browser's prompts, the
/// acquisition and the landing — because the folder module binds the chosen
/// folder as soon as the prompts end, before anything here hears of it.
/// <see cref="ShowsProgress"/>, the busy cursor, starts only when the app's
/// own work does: at the accepted-pick hook, since the prompts are the user's
/// turn, and at the start of a resample or a collection. Both end however the
/// operation ends — landed, cancelled, empty, failed or faulted — so a
/// dismissed picker never leaves the app locked.
/// </para>
///
/// <para>
/// <b>The fallback holds nothing while its picker is open.</b> A dismissed
/// <c>webkitdirectory</c> picker may signal nothing at all, so opening it
/// mints a <see cref="FallbackPickIntent"/> and releases the operation. The
/// selection, when the input reports one, is collected only for the intent
/// still current (<see cref="CollectFallbackAsync"/>): any operation begun
/// since — a pick of either mechanism, a resample, a Clear — supersedes it,
/// and the collection takes ownership before it asks the module to collect,
/// which is what binds the picked slot. A superseded selection changes
/// nothing.
/// </para>
///
/// <para>
/// <b>Acquisition and landing are kept apart.</b> Only the folder seam's
/// calls are inside the catch that reports a failed acquisition
/// (<see cref="IsAcquisitionFailure"/>). A pick that fails lets go of the
/// folder the module bound for it, logging a failure of that cleanup without
/// ever reporting it in place of the acquisition's error; a resample that
/// fails keeps everything. Landing is the order <c>SPEC-quiz-history.md</c> §9
/// rules — the run in progress finished, then the sample installed, then the
/// stats probe — and a fault in it surfaces as the bug it is. A sample lands
/// only over the sample its draw began against, so only into the setup its
/// operation began under.
/// </para>
///
/// <para>
/// <b>Outcomes are the caller's to report.</b> Each operation answers a
/// <see cref="FolderOutcome"/> to the page that asked; the page's notices are
/// per-visit by design (<c>SPEC-notices.md</c>), so an operation that outlives
/// its page reports to nobody, while what it landed is the app's to show.
/// <see cref="Changed"/> tells every page that something here moved — an
/// operation began, the busy cursor rose, a sample landed, an operation ended
/// — so the page on screen re-renders and asks for the count of what is held.
/// </para>
///
/// <para>
/// Not thread-safe: every member is called on the renderer's synchronization
/// context, as every component event and lifecycle method already is.
/// </para>
/// </summary>
internal sealed class FolderOperations(
    IFolderAccess folderAccess,
    PickedProblemFolder folder,
    QuizController controller,
    FilterSetup filterSetup,
    QuizStatsStore statsStore,
    MixDraft mixDraft,
    ILogger<FolderOperations> logger)
{
    /// <summary>The operation in flight, or null.</summary>
    private Operation? _current;

    /// <summary>The fallback picker opened last and still awaiting its selection, or null.</summary>
    private FallbackPickIntent? _intent;

    /// <summary>
    /// Whether a folder operation is in flight. While it is, no pick, Clear,
    /// resample or Start begins, and neither of Done's Restarts does.
    /// </summary>
    public bool IsInFlight => _current is not null;

    /// <summary>
    /// Whether the app's own work on a folder is under way — the busy cursor.
    /// Raised at the accepted-pick hook, or at the start of a resample or a
    /// collection; lowered when the operation ends. Never during the browser's
    /// prompts.
    /// </summary>
    public bool ShowsProgress { get; private set; }

    /// <summary>
    /// Raised whenever <see cref="IsInFlight"/> or <see cref="ShowsProgress"/>
    /// moves, and as a sample lands or a setup ends — on the renderer's
    /// context. Subscribers unsubscribe when they are disposed.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// The one "choose a folder" gesture. The click ends the current setup
    /// (<see cref="EndSetupAsync"/>) and probes the mechanism. On File System
    /// Access it then holds the operation through the browser's prompts, the
    /// acquisition and the landing. On the fallback it opens the hidden input's
    /// picker, mints the intent its selection must match, and holds nothing
    /// while the picker is open.
    /// </summary>
    /// <param name="fallbackInput">The hidden <c>webkitdirectory</c> input the fallback mechanism opens.</param>
    /// <returns>
    /// What the gesture came to: refused (an operation was in flight), landed,
    /// cancelled, a folder with no problem files, a failed acquisition, or —
    /// on the fallback — awaiting the selection, with its intent.
    /// </returns>
    public async Task<FolderOutcome> PickAsync(ElementReference fallbackInput)
    {
        if (!TryBegin(out var operation)) return FolderOutcome.Refused;
        try
        {
            FolderPickOutcome picked;
            try
            {
                await EndSetupAsync();
                if (!await folderAccess.SupportsDirectoryPickerAsync())
                {
                    _intent = new FallbackPickIntent();
                    var intent = _intent;
                    await folderAccess.TriggerFallbackPickerAsync(fallbackInput);
                    return FolderOutcome.AwaitingSelection(intent);
                }

                operation.DrawingAgainst(folder.SampleIdentity);
                picked = await folderAccess.PickFolderAsync(ShowProgressAsync);
            }
            catch (Exception failure) when (IsAcquisitionFailure(failure))
            {
                _intent = null;
                await ReleaseFailedPickAsync(failure);
                return FolderOutcome.Failed(failure);
            }

            return await LandPickAsync(picked, operation);
        }
        finally
        {
            End();
        }
    }

    /// <summary>
    /// The fallback's selection, reported by the input's change event: collect
    /// it for <paramref name="intent"/> and land it, holding the operation from
    /// before the collection binds the picked slot through the landing.
    /// Refused — changing nothing, collecting nothing — unless
    /// <paramref name="intent"/> is still the current one and no operation is
    /// in flight.
    /// </summary>
    /// <param name="intent">The intent the picker that reported was opened under.</param>
    /// <param name="fallbackInput">The hidden input whose FileList is collected.</param>
    public async Task<FolderOutcome> CollectFallbackAsync(FallbackPickIntent intent, ElementReference fallbackInput)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!ReferenceEquals(intent, _intent) || !TryBegin(out var operation)) return FolderOutcome.Refused;
        try
        {
            await ShowProgressAsync();
            FolderPickOutcome collected;
            try
            {
                operation.DrawingAgainst(folder.SampleIdentity);
                collected = await folderAccess.CollectFallbackAsync(fallbackInput);
            }
            catch (Exception failure) when (IsAcquisitionFailure(failure))
            {
                await ReleaseFailedPickAsync(failure);
                return FolderOutcome.Failed(failure);
            }

            return await LandPickAsync(collected, operation);
        }
        finally
        {
            End();
        }
    }

    /// <summary>
    /// The fallback picker's dismissal, reported by the input's cancel event:
    /// ends <paramref name="intent"/> if it is still the current one.
    /// </summary>
    /// <returns>Whether it was — and so whether the dismissal is the page's to report.</returns>
    public bool DismissFallback(FallbackPickIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!ReferenceEquals(intent, _intent)) return false;
        _intent = null;
        return true;
    }

    /// <summary>
    /// Resample (<c>SPEC-filtering.md</c> §4): draw the held folder again,
    /// without the picker, and land the new sample — the setup kept, the
    /// sample replaced. Busy throughout, since no prompt precedes the work. A
    /// failure to acquire keeps everything and reports as a failed pick does;
    /// an empty draw replaces nothing.
    /// </summary>
    public async Task<FolderOutcome> ResampleAsync()
    {
        if (!TryBegin(out var operation)) return FolderOutcome.Refused;
        try
        {
            await ShowProgressAsync();
            FolderSample sample;
            try
            {
                operation.DrawingAgainst(folder.SampleIdentity);
                sample = await folderAccess.ResampleAsync();
            }
            catch (Exception failure) when (IsAcquisitionFailure(failure))
            {
                return FolderOutcome.Failed(failure);
            }

            if (sample.Files.IsEmpty) return FolderOutcome.NoFiles;
            await LandAsync(operation, () => folder.Resample(sample));
            return FolderOutcome.Landed;
        }
        finally
        {
            End();
        }
    }

    /// <summary>
    /// The Clear affordance: end the current setup (<see cref="EndSetupAsync"/>)
    /// as its own short operation, so nothing lands into a setup being ended.
    /// A run in progress is not ended: Clear replaces no sample.
    /// </summary>
    /// <returns>Whether the setup was ended — false when an operation was in flight.</returns>
    public async Task<bool> ClearAsync()
    {
        if (!TryBegin(out _)) return false;
        try
        {
            await EndSetupAsync();
            return true;
        }
        finally
        {
            End();
        }
    }

    /// <summary>
    /// Whether <paramref name="failure"/>, raised by the folder seam while a
    /// sample was being acquired, is a failed acquisition — reported as a
    /// failed pick or resample. Every exception is, except one: the seam's
    /// caller error, <see cref="InvalidOperationException"/>, which means the
    /// folder was released under the draw or none was held — a fact about this
    /// app and never about the folder — so it is not dressed up as a pick the
    /// user could retry, and surfaces as the bug it is. The seam documents two
    /// failures of the folder (<see cref="PickedFileTooLargeException"/>, and
    /// <see cref="Microsoft.JSInterop.JSException"/> for the browser), but what
    /// reaches here from across the interop boundary is wider — an
    /// <see cref="EndOfStreamException"/> from a stream that ends mid-copy, a
    /// <see cref="System.Text.Json.JsonException"/> from a reply that is not the
    /// wire shape — and each is the folder failing to arrive. Only acquisition
    /// is ever judged by this: landing is outside every catch that asks it.
    /// </summary>
    private static bool IsAcquisitionFailure(Exception failure) => failure is not InvalidOperationException;

    /// <summary>
    /// A pick's draw, landed or accounted for: a cancelled pick and a folder
    /// with no problem files leave the holder as the click left it — clear —
    /// and replace no sample, so a run in progress goes on; anything else lands
    /// as a new setup.
    /// </summary>
    private async Task<FolderOutcome> LandPickAsync(FolderPickOutcome picked, Operation operation)
    {
        if (picked.Cancelled) return FolderOutcome.Cancelled;
        if (picked.Sample.Files.IsEmpty) return FolderOutcome.NoFiles;
        await LandAsync(operation, () => folder.Set(picked.DirectoryName, picked.Capability, picked.Sample));
        return FolderOutcome.Landed;
    }

    /// <summary>
    /// Land a new sample, acquired and known non-empty, in the order
    /// <c>SPEC-quiz-history.md</c> §9 rules: the run in progress is finished as
    /// End quiz finishes it, its heavy references released and the live-quiz
    /// marker cleared (<see cref="QuizController.EndForNewSampleAsync"/>); then
    /// <paramref name="install"/> puts the sample on the holder, which lets the
    /// old one go with its parse — over the sample the draw began against and
    /// no other; then the setup is reported to the filter owner (a pick's new
    /// setup ends the old one there, a resample's kept setup changes nothing)
    /// and the stats probe runs on the sample installed. Pages ask for the
    /// count when the operation tells them it has ended, after the probe, so no
    /// render of theirs lands inside the probe's await.
    ///
    /// <para>
    /// No rollback after the install. A parse of the new sample that fails —
    /// the count's or a Start's — leaves it held and the previous run finished
    /// with its summary intact.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The holder no longer holds the sample the draw began against — an
    /// operation landing into a setup it did not begin under, which the
    /// exclusion makes this type's bug.
    /// </exception>
    private async Task LandAsync(Operation operation, Action install)
    {
        await controller.EndForNewSampleAsync();
        if (!ReferenceEquals(folder.SampleIdentity, operation.DrawnAgainst))
            throw new InvalidOperationException(
                "A sample lands only over the sample its draw began against; the held sample changed while the draw was in flight.");
        install();
        filterSetup.ReportSource(folder.SetupIdentity?.FilterSource);
        await statsStore.RefreshPickedStatsAsync();
    }

    /// <summary>
    /// End the current setup: the holder and the filter owner's source, the
    /// mix draft (<see cref="MixDraft.Discard"/> — the stored mix survives), and
    /// the module's picked slot. <see cref="Changed"/> fires before the
    /// release, so the page shows the setup ended before the picker or the
    /// browser's prompts appear. The release is the one step that can fail.
    /// </summary>
    private async Task EndSetupAsync()
    {
        folder.Clear();
        filterSetup.ReportSource(null);
        // The draft's in-memory reset, not a deletion: the stored rows outlive
        // the setup and re-hydrate into the next panel mount (MixDraft.Discard
        // says why nothing — no consent, no capability fork — sits beside it).
        mixDraft.Discard();
        Changed?.Invoke();
        await folderAccess.ClearPickedAsync();
    }

    /// <summary>
    /// A pick that failed to acquire holds no folder on either side of the
    /// folder seam: the holder is left clear, and the folder the module bound
    /// for the pick — before its draw — is released. The active slot, a running
    /// quiz's stats context, is untouched. A release that fails too is logged
    /// and never reported in place of <paramref name="failure"/>.
    /// </summary>
    private async Task ReleaseFailedPickAsync(Exception failure)
    {
        folder.Clear();
        filterSetup.ReportSource(null);
        try
        {
            await folderAccess.ClearPickedAsync();
        }
        catch (Exception cleanupFault)
        {
            logger.LogWarning(cleanupFault,
                "Releasing the folder of a failed pick failed too; the pick's own error is the one reported: {Failure}",
                failure.Message);
        }
    }

    /// <summary>Raise the busy cursor and let it paint before the work that follows.</summary>
    private async Task ShowProgressAsync()
    {
        ShowsProgress = true;
        Changed?.Invoke();
        await Task.Yield();
    }

    /// <summary>
    /// Begin an operation unless one is in flight. Beginning supersedes a
    /// fallback picker still awaiting its selection.
    /// </summary>
    private bool TryBegin([NotNullWhen(true)] out Operation? operation)
    {
        if (_current is not null)
        {
            operation = null;
            return false;
        }

        _intent = null;
        operation = _current = new Operation();
        Changed?.Invoke();
        return true;
    }

    /// <summary>End the operation in flight, however it ended.</summary>
    private void End()
    {
        _current = null;
        ShowsProgress = false;
        Changed?.Invoke();
    }

    /// <summary>One operation in flight, and the sample its draw began against.</summary>
    private sealed class Operation
    {
        private SampleIdentity? _drawnAgainst;

        /// <summary>The sample the draw began against — what a landing replaces.</summary>
        public SampleIdentity DrawnAgainst => _drawnAgainst
            ?? throw new InvalidOperationException("The operation lands before it has drawn.");

        /// <summary>Record the sample held as the draw begins.</summary>
        public void DrawingAgainst(SampleIdentity sample) => _drawnAgainst = sample;
    }
}

/// <summary>
/// One opening of the fallback's picker, awaiting the selection the hidden
/// input reports (<see cref="FolderOperations.CollectFallbackAsync"/>). Opaque,
/// and equal only to itself: a selection is collected only for the intent still
/// current, which any later operation supersedes.
/// </summary>
internal sealed class FallbackPickIntent;

/// <summary>What a folder operation came to, for the page that asked to report.</summary>
internal enum FolderOutcomeKind
{
    /// <summary>A new sample landed.</summary>
    Landed,

    /// <summary>Nothing began: another operation was in flight, or the fallback intent was superseded.</summary>
    Refused,

    /// <summary>The picker was dismissed, or the required grant declined.</summary>
    Cancelled,

    /// <summary>The draw found no problem files; nothing was replaced.</summary>
    NoFiles,

    /// <summary>The acquisition failed (<see cref="FolderOutcome.Failure"/>).</summary>
    Failed,

    /// <summary>The fallback picker is open (<see cref="FolderOutcome.Intent"/>).</summary>
    AwaitingSelection,
}

/// <summary>
/// What a folder operation came to (<see cref="FolderOperations"/>), for the
/// page that asked to report: its kind, and the failure or the fallback intent
/// where the kind carries one. Built only by its factories, so a kind never
/// arrives without what it carries.
/// </summary>
internal sealed class FolderOutcome
{
    private FolderOutcome(FolderOutcomeKind kind, Exception? failure = null, FallbackPickIntent? intent = null)
    {
        Kind = kind;
        Failure = failure;
        Intent = intent;
    }

    /// <summary>A new sample landed.</summary>
    public static FolderOutcome Landed { get; } = new(FolderOutcomeKind.Landed);

    /// <summary>Nothing began.</summary>
    public static FolderOutcome Refused { get; } = new(FolderOutcomeKind.Refused);

    /// <summary>The picker was dismissed.</summary>
    public static FolderOutcome Cancelled { get; } = new(FolderOutcomeKind.Cancelled);

    /// <summary>The draw found no problem files.</summary>
    public static FolderOutcome NoFiles { get; } = new(FolderOutcomeKind.NoFiles);

    /// <summary>The acquisition failed with <paramref name="failure"/>.</summary>
    public static FolderOutcome Failed(Exception failure) =>
        new(FolderOutcomeKind.Failed, failure ?? throw new ArgumentNullException(nameof(failure)));

    /// <summary>The fallback picker is open under <paramref name="intent"/>.</summary>
    public static FolderOutcome AwaitingSelection(FallbackPickIntent intent) =>
        new(FolderOutcomeKind.AwaitingSelection, intent: intent ?? throw new ArgumentNullException(nameof(intent)));

    /// <summary>What it came to.</summary>
    public FolderOutcomeKind Kind { get; }

    /// <summary>The acquisition's failure, for <see cref="FolderOutcomeKind.Failed"/>; otherwise null.</summary>
    public Exception? Failure { get; }

    /// <summary>The fallback intent, for <see cref="FolderOutcomeKind.AwaitingSelection"/>; otherwise null.</summary>
    public FallbackPickIntent? Intent { get; }
}
