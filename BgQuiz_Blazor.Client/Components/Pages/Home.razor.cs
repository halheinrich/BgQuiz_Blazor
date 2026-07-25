using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.Components;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// Landing page: problem-set folder selection, filter selection, and the
/// quiz-start gate.
///
/// <para>
/// <b>Progressive disclosure.</b> Only the folder-pick controls (and their
/// pick-status notices) render before a folder with problem files is held;
/// the filters, saved filters, weighted mix, shuffle toggle, and Start button
/// are gated behind <see cref="PickedProblemFolder.HasFiles"/> in the markup.
/// Pre-pick there is nothing to filter, weight, or start, so hiding them keeps
/// the required first step — picking a folder — unmistakable, and makes the
/// filter-half of the start gate true by construction (no panel to apply). The
/// weighted mix carries a <i>further</i> gate: it renders only when the pick can
/// provide the lifetime stats it composes from
/// (<see cref="StatsSaveCapability.Enabled"/>). Under a no-stats pick the mix
/// plays no part in Start — the panel is hidden and every pick resets any
/// committed mix to passthrough (see <see cref="ApplyPickOutcomeAsync"/>), so
/// Start runs plain with no mix gate, warning, or refusal.
/// </para>
///
/// <para>
/// The user picks a local folder with one "Choose folder…" gesture, served by
/// whichever mechanism the browser offers (probed at pick time through
/// <see cref="IFolderAccess"/>): the File System Access directory picker where
/// available — which can also grant the writable handle that enables lifetime
/// stats, and which is guided by the two-step note naming both of that
/// mechanism's permission prompts and what declining each costs, shown from page
/// load until a folder is held so it is read <i>before</i> the prompts arrive
/// (gated on the init-time <see cref="_fsAccessAvailable"/> probe) — or the
/// hidden <c>webkitdirectory</c> input elsewhere (read-only, no prompt to guide;
/// the quiz runs without stats). Either way the folder's top-level <c>.xg</c> /
/// <c>.xgp</c> files are buffered into <see cref="PickedFile"/>s (bytes +
/// extension-bearing names) held in the per-app
/// <see cref="PickedProblemFolder"/>; the bytes are parsed entirely in the
/// browser and never leave it. Buffering up front is what lets the source
/// re-enumerate on Restart. The pick-time
/// <see cref="StatsSaveCapability"/> verdict rides on the holder and drives
/// this page's stats status notice; the stats lifecycle itself is not this
/// page's concern — the controller binds the stats context at Start.
/// </para>
///
/// <para>
/// Start is gated on three conditions: the filters Applied at least once, a
/// folder picked with at least one problem file, and no uncommitted mix edits.
/// All three live in per-app scoped holders (<see cref="AppliedFilter"/>,
/// <see cref="PickedProblemFolder"/>, <see cref="AppliedMix"/>) rather than
/// transient component fields, so the gate survives in-app navigation — when
/// the page is re-instantiated on navigate-back it re-derives from the holders
/// instead of resetting. On Start the applied <see cref="FilterConfig"/> and
/// the committed <see cref="BgGame_Lib.QuizMix"/> are handed to the
/// <see cref="QuizController"/>, whose source factory builds a
/// <see cref="WasmUploadedProblemSetSource"/> over the picked files, and the
/// app navigates to <c>/quiz</c>. Pick failures and
/// <see cref="FilterConfig.Build"/> / source-construction failures are caught
/// and surfaced as banners rather than faulting the WebAssembly app. The two
/// non-failure pick outcomes — a pick that ended holding no folder, and one that
/// held a folder with no problem files — each get their own polite notice rather
/// than silence, so no gesture ever returns the user to an unchanged page with
/// no account of what happened. A
/// weighted start with no lifetime stats is <i>refused</i> as an outcome (the
/// actionable notice with its per-run "Start without mix" override — see
/// <see cref="StartCoreAsync"/>), never silently run unweighted.
/// </para>
///
/// <para>
/// Two statements on this page are about the <i>app</i> rather than about the
/// quiz, and neither is gated on any pick state the setup surface uses. Beside
/// the pick button, <see cref="FolderPickDisplay.SupportedBrowsers"/> names the
/// browsers and devices the pick actually works on — deliberately <b>not</b>
/// behind the <see cref="_fsAccessAvailable"/> probe, because the visitor it is
/// written for (a phone, where the pick may raise nothing at all) is precisely
/// the one that probe excludes. In the footer, <see cref="AppInfo.Version"/>
/// and <see cref="AppInfo.FeedbackMailto"/> render together: the version is what
/// makes a beta report actionable, and putting the feedback link beside it means
/// the two cannot disagree.
/// </para>
///
/// <para>
/// A third, ungated toggle — "Shuffle order" — lives alongside the gate in the
/// per-app <see cref="ShuffleOption"/> holder. It is presentation-only (order,
/// not admission), so it plays no part in <c>CanStart</c>: the source factory
/// reads it live at Start to decide whether to wrap the picked set in a
/// <c>ShuffledProblemSetSource</c>.
/// </para>
/// </summary>
public partial class Home : ComponentBase
{
    private string? _startError;

    /// <summary>
    /// Sibling of <see cref="_startError"/> for pick failures — an unexpected
    /// browser error or a folder past the <see cref="PickedFileLimits"/> caps.
    /// A per-visit failure banner (assertive), like the start error.
    /// </summary>
    private string? _pickError;

    /// <summary>
    /// Set when a completed pick yielded no top-level <c>.xg</c> / <c>.xgp</c>
    /// files — an outcome (polite notice), not a failure: the holder stays
    /// clear and the Start gate stays disabled. Per-visit, so a component field.
    /// </summary>
    private bool _emptyFolderNotice;

    /// <summary>
    /// Set when a pick returned <see cref="FolderPickOutcome.Cancelled"/> — it
    /// ended holding no folder. Sibling of <see cref="_emptyFolderNotice"/>: an
    /// outcome (polite notice), not a failure, with the holder left untouched.
    ///
    /// <para>
    /// This <i>reverses</i> the earlier deliberate silence. Cancellation covers
    /// two causes — the picker was dismissed, or the required view-files
    /// permission was declined — and the second is not the user changing their
    /// mind: it is the load-bearing grant refused, leaving them on an unchanged,
    /// empty page with no explanation. The browser reports both as
    /// <c>AbortError</c>, so they are indistinguishable here; the notice is
    /// worded to be true under either and to stay non-accusatory (see the markup
    /// comment). Distinguishing them is not attempted.
    /// </para>
    ///
    /// <para>
    /// FS-Access-only in practice, and inherently so: only
    /// <see cref="IFolderAccess.PickFolderAsync"/> ever reports a cancellation.
    /// A dismissed <c>webkitdirectory</c> picker fires no change event at all,
    /// and an empty selection lands on <see cref="_emptyFolderNotice"/> instead.
    /// </para>
    ///
    /// <para>Per-visit outcome state, so a component field.</para>
    /// </summary>
    private bool _cancelledPickNotice;

    /// <summary>
    /// The hidden <c>webkitdirectory</c> input the fallback mechanism drives.
    /// The JS module reads its FileList directly (for <c>webkitRelativePath</c>);
    /// this reference is only ever handed across <see cref="IFolderAccess"/>.
    /// </summary>
    private ElementReference _fallbackInput;

    /// <summary>
    /// Whether this browser offers the File System Access directory picker,
    /// probed once in <see cref="OnInitializedAsync"/>. Gates the in-page
    /// guidance for the <i>two</i> browser-anchored permission prompts that
    /// mechanism's pick raises — both easily missed, and each with a very
    /// different cost to declining (see the markup, and <c>folderAccess.js</c>'s
    /// <c>pickDirectory</c> for why the two prompts cannot be collapsed into
    /// one). The guidance is inherently FS-Access-only: the fallback mechanism
    /// raises no permission prompt to guide toward, so showing it there would
    /// promise prompts that never arrive.
    ///
    /// <para>
    /// A deliberate <i>second</i> probe, not a replacement for the pick-time one
    /// in <see cref="PickFolderAsync"/>, and the two have different jobs.
    /// Capability is a property of the moment (see
    /// <see cref="IFolderAccess.SupportsDirectoryPickerAsync"/>), so the
    /// mechanism fork stays per-gesture and authoritative; this one is an
    /// init-time snapshot whose only consequence is whether advisory guidance is
    /// worth rendering — and it must run at init precisely because the guidance
    /// has to be readable <i>before</i> the gesture it describes.
    /// </para>
    ///
    /// <para>Per-visit derived state, so a component field.</para>
    /// </summary>
    private bool _fsAccessAvailable;

    /// <summary>
    /// Sibling of <see cref="_startError"/> for the empty-result <i>outcome</i> —
    /// distinct from the failure the error banner reports. A successful
    /// <see cref="QuizController.StartAsync"/> that leaves the controller already
    /// <see cref="QuizController.IsFinished"/> means the source admitted no
    /// showable problem; rather than bounce silently through <c>/quiz</c> to a
    /// <c>0/0</c> <c>/done</c>, the page stays on <c>/</c> and surfaces this as a
    /// neutral status message (see <see cref="StartQuizAsync"/>). Genuinely
    /// per-visit page state, so a component field — see the holder-vs-field note
    /// in INSTRUCTIONS' Pitfalls.
    /// </summary>
    private string? _noMatchNotice;

    /// <summary>
    /// Set once, on a boot that finds the <see cref="QuizLiveMarker"/> present
    /// with no live quiz in the (freshly-booted) controller — i.e. a full reload
    /// silently reset a quiz that was underway. Drives the polite reset notice.
    /// A per-visit outcome flag, so a component field like the two banners above.
    /// </summary>
    private bool _showReloadNotice;

    /// <summary>
    /// Set when a weighted Start was refused — the committed mix has entries
    /// but no lifetime stats are available. Drives the actionable refusal
    /// notice with its one-click per-run "Start without mix" override.
    /// Genuinely per-visit outcome state, so a component field like the
    /// banners above; cleared on a new pick (capability may change), a mix
    /// commit (the refusal may be moot), and every Start attempt.
    /// </summary>
    private bool _mixRefused;

    /// <summary>
    /// How many decisions the last-applied filter matched, shown near the
    /// filters so the user knows what they selected before starting;
    /// <see langword="null"/> when no count is shown (before an Apply, after a
    /// filter edit, or after a new/cleared pick). A per-visit affordance, so a
    /// component field. Set from <see cref="QuizController.CountMatchesAsync"/>
    /// on Apply — pre-mix pool size, "decisions that match", not "problems
    /// you'll see" (a few forced-move passes auto-skip at quiz time).
    /// </summary>
    private int? _matchCount;

    /// <summary>
    /// True while <see cref="QuizController.CountMatchesAsync"/> runs on Apply.
    /// The first count after a pick parses the corpus once (warming the shared
    /// cache so Start is then instant), so the whole setup surface disables and
    /// the busy cursor shows — folded into the same fieldset-disable / app-busy
    /// boundary the controller's transition gate drives, which also serializes
    /// the count against a Start (no concurrent parse of the same corpus).
    /// </summary>
    private bool _isCounting;

    /// <summary>
    /// Monotonic id stamped on each count request so a stale result never
    /// lands: <see cref="HandleFilterConfigApplied"/> captures it before the
    /// await and discards the count if a newer Apply, a filter edit, or a
    /// re-pick has bumped it since. Defence in depth — the busy fieldset also
    /// blocks a second gesture mid-count — so the count stays correct even if
    /// that busy strategy later changes.
    /// </summary>
    private int _countRequestId;

    /// <summary>
    /// The hosted <see cref="FilterPanel"/>, captured by <c>@ref</c> so Home can
    /// drive the two imperative host-mediated saved-filter operations on it:
    /// <see cref="FilterPanel.LoadConfig"/> (stage a loaded config into the edit
    /// buffers) and <see cref="FilterPanel.TryGetEditedConfig"/> (snapshot the
    /// live buffers for save-as). Null only before the first render.
    /// </summary>
    private FilterPanel? _filterPanel;

    /// <summary>
    /// Set when a save-as was refused because the current filter's position
    /// pattern is unparseable — the one state
    /// <see cref="FilterPanel.TryGetEditedConfig"/> refuses (exactly Apply's
    /// gate). The panel clears its typed name optimistically on raise, so the
    /// host must say why nothing was saved rather than no-op silently. A
    /// per-visit outcome flag; cleared on any filter edit, a new pick, and the
    /// next save attempt.
    /// </summary>
    private string? _filterSaveError;

    /// <summary>
    /// Whether the saved-filters affordance applies to the current pick: a
    /// folder is held and it was picked via a File System Access mechanism (the
    /// only ones exposing a readable directory handle). Gates the panel and all
    /// three saved-filters notices; a fallback pick
    /// (<see cref="StatsSaveCapability.BrowserUnsupported"/>) has none of them.
    ///
    /// <para>
    /// One asymmetry between the two applicable rungs: under
    /// <see cref="StatsSaveCapability.Enabled"/> the section shows even with zero
    /// saved filters (you can save into it), but under
    /// <see cref="StatsSaveCapability.PermissionDenied"/> — where saving is
    /// disabled — an empty collection has nothing to load and nothing to save,
    /// so the section is pure clutter and is hidden. Read-only therefore requires
    /// at least one saved filter to render.
    /// </para>
    /// </summary>
    private bool SavedFiltersApplicable =>
        Folder.HasFiles
        && (Folder.Capability == StatsSaveCapability.Enabled
            || (Folder.Capability == StatsSaveCapability.PermissionDenied
                && SavedFilters.Filters.Count > 0));

    /// <summary>
    /// Whether the current pick has a saved-filters <i>context</i> whose degrade
    /// state is worth reporting — a folder held, picked via a File System Access
    /// mechanism (the only ones exposing a readable directory handle). Gates the
    /// <see cref="SavedFiltersStatus.LoadFailed"/> / <see cref="SavedFiltersStatus.WriteFailed"/>
    /// notices, which — unlike the panel — must <b>not</b> be suppressed by an
    /// empty collection. A LoadFailed notice exists precisely to say the file
    /// couldn't be read and has been left untouched, and LoadFailed always leaves
    /// the in-memory collection empty, so folding the panel's empty-hiding rule
    /// (<see cref="SavedFiltersApplicable"/>) in here would swallow that
    /// data-protection notice every time it fires. WriteFailed is only reachable
    /// under <see cref="StatsSaveCapability.Enabled"/> (persistence is disabled
    /// under PermissionDenied, so no write is attempted), so it is unaffected by
    /// the split either way — routing it here keeps the two degrade notices
    /// symmetric.
    /// </summary>
    private bool SavedFiltersContextApplicable =>
        Folder.HasFiles
        && Folder.Capability is StatsSaveCapability.Enabled or StatsSaveCapability.PermissionDenied;

    /// <summary>
    /// Whether the saved-filters panel may persist (Save / Delete enabled).
    /// Requires the writable grant (<see cref="StatsSaveCapability.Enabled"/>)
    /// and a healthy context: <see cref="StatsSaveCapability.PermissionDenied"/>
    /// is load-only, and a prior write failure stops further writes.
    /// </summary>
    private bool SavedFiltersCanPersist =>
        Folder.Capability == StatsSaveCapability.Enabled
        && SavedFilters.Status == SavedFiltersStatus.Ready;

    /// <summary>
    /// The muted reason the panel shows while persistence is disabled but the
    /// panel is still offered — the steady-state load-only case under
    /// <see cref="StatsSaveCapability.PermissionDenied"/>. A write failure is a
    /// louder, separate alert (rendered by Home), so it is deliberately not
    /// folded in here; <see langword="null"/> leaves the panel's hint absent.
    ///
    /// <para>
    /// The premise is <see cref="FolderPickDisplay.WriteAccessNotGranted"/>, not
    /// a local literal: this is the second surface stating it, and the two had
    /// already drifted once — both said "you declined", which this rung cannot
    /// know (an auto-denied readwrite request lands here too), and only the
    /// stats notice was corrected. Sharing the clause is what keeps the next
    /// correction from being half-applied again.
    /// </para>
    /// </summary>
    private string? SavedFiltersDisabledReason =>
        Folder.Capability == StatsSaveCapability.PermissionDenied
            ? $"{FolderPickDisplay.WriteAccessNotGranted} — saved filters can be "
              + "loaded but not changed or deleted."
            : null;

    /// <summary>
    /// Three gates: filters applied, a folder with problem files picked, and
    /// no uncommitted mix edits (a dirty mix would let Start run a mix that
    /// differs from what the panel shows — the filter gate's hazard, mix
    /// flavored). A blank mix is the valid default and never gates.
    /// </summary>
    private bool CanStart => AppliedFilter.IsApplied && Folder.HasFiles && !AppliedMix.IsDirty;

    /// <summary>
    /// True while the committed mix has entries: presentation order belongs to
    /// the mix's own Random-order setting, so the standalone Shuffle checkbox
    /// is disabled — but its held value is deliberately left untouched, so
    /// clearing the mix restores the user's prior shuffle preference.
    /// </summary>
    private bool MixOwnsOrder => !AppliedMix.Current.IsPassthrough;

    /// <summary>
    /// On boot, surface the reload-reset notice when the marker says a quiz was
    /// live but the controller has none — the signature of a full reload having
    /// rebooted the runtime out from under an in-progress quiz. Then clear the
    /// marker so the notice shows once. Also takes the
    /// <see cref="_fsAccessAvailable"/> snapshot the pick guidance is gated on.
    ///
    /// <para>
    /// The <see cref="QuizController.HasStarted"/> guard is what distinguishes a
    /// reload from in-app navigation back to <c>Home</c> mid-quiz: the latter
    /// keeps the same per-tab controller (quiz still live), so the marker is set
    /// <i>and</i> <c>HasStarted</c> is true — no notice, and the marker is left
    /// in place for a genuine later reload.
    /// </para>
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        if (await Marker.WasLiveAsync() && !Controller.HasStarted)
        {
            _showReloadNotice = true;
            await Marker.ClearAsync();
        }

        _fsAccessAvailable = await FolderAccess.SupportsDirectoryPickerAsync();
    }

    /// <summary>
    /// The one "pick a folder" gesture. Probes the mechanism at pick time:
    /// with File System Access, the whole pick (picker, permission,
    /// enumeration, buffering) completes inside <see cref="IFolderAccess"/>;
    /// without it, this click only opens the hidden <c>webkitdirectory</c>
    /// input's picker and the pick arrives later via that input's own change
    /// event (<see cref="HandleFallbackPickedAsync"/>).
    /// </summary>
    private async Task PickFolderAsync()
    {
        ClearPickNotices();
        try
        {
            // The authoritative mechanism fork, re-probed per gesture. No
            // guidance state is toggled here: the prompt note is already on
            // screen (rendered since init — see _fsAccessAvailable) and hides
            // itself once this pick leaves a folder held.
            if (await FolderAccess.SupportsDirectoryPickerAsync())
            {
                await ApplyPickOutcomeAsync(await FolderAccess.PickFolderAsync());
            }
            else
            {
                await FolderAccess.TriggerFallbackPickerAsync(_fallbackInput);
            }
        }
        catch (Exception ex)
        {
            Folder.Clear();
            _pickError = ex.Message;
        }
    }

    /// <summary>
    /// The fallback pick landing: the hidden input's FileList is collected and
    /// filtered by the JS module (top-level <c>.xg</c> / <c>.xgp</c> only).
    /// Capability is always <see cref="StatsSaveCapability.BrowserUnsupported"/>
    /// on this mechanism — no writable handle exists.
    /// </summary>
    private async Task HandleFallbackPickedAsync(ChangeEventArgs _)
    {
        ClearPickNotices();
        try
        {
            await ApplyPickOutcomeAsync(await FolderAccess.CollectFallbackAsync(_fallbackInput));
        }
        catch (Exception ex)
        {
            Folder.Clear();
            _pickError = ex.Message;
        }
    }

    /// <summary>
    /// The shared landing for both mechanisms' outcomes. A cancelled pick and an
    /// empty folder each leave the holder clear and surface their own polite
    /// outcome notice; otherwise the holder takes the pick, and the rendered
    /// summary + stats status notice derive from it (no transient field to keep
    /// in sync — that desynced on navigate-back, when Home re-instantiated).
    /// </summary>
    private async Task ApplyPickOutcomeAsync(FolderPickOutcome outcome)
    {
        if (outcome.Cancelled)
        {
            // Not silent any more: cancellation also covers a declined
            // view-files permission, which leaves the user on an unchanged empty
            // page needing an explanation — see _cancelledPickNotice. Safe to
            // set on the way out because ClearPickNotices ran at pick *start*,
            // not here.
            _cancelledPickNotice = true;
            return;
        }

        // Every (non-cancelled) pick starts a fresh setup, so it clears any
        // committed mix (Task X): under a no-stats pick the mix must play no
        // part in Start, and under an Enabled pick the re-mounted panel re-offers
        // the stored config as dirty. localStorage is untouched, so the panel's
        // restore can still re-show it. This is what makes the removed
        // "your mix can't be provided" advisory unreachable — a stats-less pick
        // can never coexist with a committed non-blank mix.
        AppliedMix.Reset();

        if (outcome.Files.Count == 0)
        {
            Folder.Clear();
            SavedFilters.Reset();
            _emptyFolderNotice = true;
            return;
        }

        Folder.Set(outcome.DirectoryName, outcome.Files, outcome.Capability);

        // Load this folder's saved-filters document now — a setup-time, picked-
        // slot read. The store guards on capability/handle and degrades on any
        // read failure into its own status, so this never throws or blocks the
        // pick (a fallback pick short-circuits to the no-context state).
        await SavedFilters.LoadForPickAsync();
    }

    private async Task ClearPickedFolderAsync()
    {
        // Discards the pick; the Start gate re-disables by construction
        // (HasFiles goes false) and the holder-derived summary and stats
        // notice disappear. Safe to call mid-quiz: the picked files are read
        // only at Start time (the source factory reads Folder.Files in
        // StartAsync) and the JS side clears the PICKED slot only — a running
        // quiz's stats context lives in the ACTIVE slot, bound at Start, so
        // recording continues untouched until the next Start re-binds.
        Folder.Clear();
        SavedFilters.Reset();
        // AppliedMix.Current is pick-coupled (Task X resets it on every pick), so
        // ending the pick via Clear must also return it to passthrough —
        // completing the "no pick → passthrough" invariant and mirroring the
        // SavedFilters.Reset() above (both are pick-coupled state that clears when
        // the pick ends). AppliedFilter is deliberately NOT reset here: it is
        // edit-coupled, not pick-coupled, so its lifecycle is unchanged.
        AppliedMix.Reset();
        await FolderAccess.ClearPickedAsync();
        ClearPickNotices();
    }

    private void ClearPickNotices()
    {
        _pickError = null;
        _emptyFolderNotice = false;
        _cancelledPickNotice = false;
        _startError = null;
        _noMatchNotice = null;
        _mixRefused = false; // a new pick can change stats capability
        _filterSaveError = null; // a new pick re-derives the saved-filters context
        // A new/cleared pick changes the corpus, so any match count is stale;
        // the bumped id also discards a count still in flight from before.
        _matchCount = null;
        _countRequestId++;
    }

    private async Task HandleFilterConfigApplied(FilterConfig cfg)
    {
        // The user clicked Apply: record the deliberate applied state on the
        // scoped holder so it survives navigate-back (not a transient field).
        AppliedFilter.Set(cfg);
        _startError = null;
        _noMatchNotice = null;

        // Show how many decisions this config matches. The first count after a
        // pick parses the corpus once and warms the shared cache, so the Start
        // that follows is instant — the count is not a separate cost. Counting
        // lives in the controller; Home only stamps a request id (so a stale
        // result can't land) and drives the busy affordance.
        var requestId = ++_countRequestId;
        _matchCount = null;
        _isCounting = true;
        // Paint the busy state before the (possibly one-time-parse) count
        // begins — the same deliberate-yield idiom as the controller's gate.
        StateHasChanged();
        await Task.Yield();
        try
        {
            var count = await Controller.CountMatchesAsync(cfg);
            if (requestId != _countRequestId) return; // superseded — discard
            _matchCount = count;
        }
        catch
        {
            // The count is advisory: never let it block Apply or fault the app.
            // Start still validates the config and surfaces any real error.
            if (requestId == _countRequestId) _matchCount = null;
        }
        finally
        {
            if (requestId == _countRequestId) _isCounting = false;
        }
    }

    private void HandleFiltersDirty()
    {
        // Any filter edit re-gates Start: a half-edited, un-applied set must
        // clear the applied state, not just a local flag.
        AppliedFilter.Clear();
        // Editing also moots a stale save-as refusal — fixing the offending
        // position pattern is itself an edit, so the notice clears with it.
        _filterSaveError = null;
        // …and invalidates any shown or in-flight match count (it described the
        // now-abandoned config); the bumped id discards a late-landing result.
        _matchCount = null;
        _countRequestId++;
    }

    /// <summary>
    /// Load a saved filter into the panel: resolve the config from the store's
    /// collection and stage it via <see cref="FilterPanel.LoadConfig"/>. That
    /// staging raises the panel's dirty signal (→ <see cref="HandleFiltersDirty"/>
    /// → <see cref="AppliedFilter.Clear"/>), so Start re-gates until the user
    /// Applies — a load is a bulk edit, not a commit. Read-only over the
    /// in-memory collection; nothing is persisted.
    /// </summary>
    private void HandleLoadSavedFilter(string name)
    {
        _filterSaveError = null;
        if (SavedFilters.Filters.TryGetConfig(name, out var config))
        {
            _filterPanel?.LoadConfig(config);
        }
    }

    /// <summary>
    /// Save-as: snapshot the panel's live edit buffers and hand them to the
    /// store under <paramref name="name"/> (pre-normalized by the panel). The
    /// snapshot uses Apply's exact gate — it refuses only an unparseable
    /// position pattern — so a refusal surfaces the save-error notice rather
    /// than silently dropping the save the panel already optimistically cleared.
    /// </summary>
    private async Task HandleSaveSavedFilterAsync(string name)
    {
        if (_filterPanel is not null && _filterPanel.TryGetEditedConfig(out var config))
        {
            _filterSaveError = null;
            await SavedFilters.SaveAsync(name, config);
        }
        else
        {
            _filterSaveError =
                "The current filter's position pattern is invalid, so it can't be "
                + "saved — fix it in the filters below, then Save.";
        }
    }

    /// <summary>Delete a saved filter (the panel confirmed) and persist the removal.</summary>
    private async Task HandleDeleteSavedFilterAsync(string name)
    {
        _filterSaveError = null;
        await SavedFilters.DeleteAsync(name);
    }

    private void HandleShuffleToggled(ChangeEventArgs e)
    {
        // A checkbox has no half-edited state, so the toggle is recorded live —
        // no applied/dirty gate the way AppliedFilter needs one.
        ShuffleOption.Set(e.Value is true);
    }

    private void HandleMixApplied(QuizMix mix)
    {
        // The user clicked Apply (or Reset — an explicit apply of the blank
        // mix): commit to the scoped holder so the gate clears and the choice
        // survives navigate-back. A commit also moots any standing refusal.
        AppliedMix.Apply(mix);
        _mixRefused = false;
        _startError = null;
        _noMatchNotice = null;
    }

    private void HandleMixRestored(QuizMix mix)
    {
        // The panel's first-render localStorage restore: reconcile, don't adopt.
        // Gate Start (mark dirty) only when the restore is non-passthrough AND
        // the holder is still at its passthrough default — a fresh load, where
        // the shown-but-uncommitted mix must gate so Start can't run passthrough
        // while a mix is displayed. When the holder already holds a committed
        // mix, this is navigate-back (the Scoped holder survived), so leave it
        // untouched — the user needn't re-Apply what they already applied. The
        // holder-passthrough check is airtight for reachable states: Start (the
        // only way off Home into a quiz) requires !IsDirty, so a committed mix
        // could only have been Applied (holder non-passthrough) or Reset/left
        // blank (holder passthrough, localStorage Empty → this restore is
        // passthrough too), never a non-passthrough restore against a passthrough
        // holder that wasn't a genuine fresh load. This is NOT the removed
        // adopt-on-restore — it never commits the stored mix.
        if (!mix.IsPassthrough && AppliedMix.Current.IsPassthrough)
            AppliedMix.MarkDirty();
    }

    private void HandleMixDirty()
    {
        // Any mix edit re-gates Start until the user commits via Apply.
        AppliedMix.MarkDirty();
    }

    private Task StartQuizAsync() => StartCoreAsync(ignoreMix: false);

    /// <summary>
    /// The refusal notice's one-click escape: run this one quiz as
    /// passthrough. Per-run only — the stored mix is untouched and re-applies
    /// on the next Start that can honor it.
    /// </summary>
    private Task StartWithoutMixAsync() => StartCoreAsync(ignoreMix: true);

    private async Task StartCoreAsync(bool ignoreMix)
    {
        if (AppliedFilter.Config is not { } cfg) return;
        _startError = null;
        _noMatchNotice = null;
        _mixRefused = false;
        try
        {
            var outcome = await Controller.StartAsync(cfg, AppliedMix.Current, ignoreMix);

            // Overlapped gesture: the transition gate ignored this call, so
            // this handler must change nothing — the in-flight Start owns any
            // navigation and notices.
            if (outcome == QuizStartOutcome.Busy) return;

            // The outcome check must precede the IsFinished check: a refused
            // start leaves ALL prior controller state in place, including a
            // stale IsFinished from an earlier finished quiz.
            if (outcome == QuizStartOutcome.MixRequiresStats)
            {
                _mixRefused = true;
                return;
            }

            // StartAsync already advanced to the first showable problem, so an
            // immediately-finished controller means the source yielded nothing
            // the quiz could present. With an active mix the telemetry says
            // whether the composition itself came up empty; otherwise two
            // indistinguishable causes flip this — zero filter matches, or
            // every match auto-skipped as a pass position — so stay on / and
            // surface a neutral outcome notice rather than navigating into a
            // 0/0 /quiz → /done bounce with no hint of why.
            if (Controller.IsFinished)
            {
                _noMatchNotice = Controller.LastComposition is { DrawnCount: 0 }
                    ? "Your mix drew no problems — no decision in these files matched "
                      + "the selected categories against your lifetime stats. Adjust "
                      + "the mix, the filters, or the files."
                    : "No quiz problems matched these filters — adjust the filters or pick different files.";
                return;
            }

            // A live quiz is starting: record it so a mid-quiz full reload (which
            // reboots the WASM runtime and silently discards this quiz) is
            // acknowledged on the next boot rather than dropping the user on a
            // fresh Home. Set only past the empty-result guard — the no-match
            // path above stays on Home with no live quiz to lose.
            await Marker.MarkLiveAsync();

            Nav.NavigateTo("/quiz");
        }
        catch (Exception ex)
        {
            // FilterConfig.Build() validation failure, source construction
            // failure, etc. Surface to the user rather than faulting the app.
            _startError = ex.Message;
        }
    }
}
