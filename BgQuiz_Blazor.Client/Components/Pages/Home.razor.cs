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
/// committed mix to passthrough (see <see cref="EndCurrentSetupAsync"/>), so
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
/// folder picked with at least one problem file, and the mix draft agreeing
/// with the committed mix (<see cref="MixDraft.Matches"/> — derived per
/// render, never stored; see <see cref="MixDirty"/>). Everything the gate
/// reads lives in per-app scoped services (<see cref="AppliedFilter"/>,
/// <see cref="PickedProblemFolder"/>, <see cref="AppliedMix"/>,
/// <see cref="MixDraft"/>) rather than transient component fields, so the
/// gate survives in-app navigation — when the page is re-instantiated on
/// navigate-back it re-derives from the holders instead of resetting. On Start the applied <see cref="FilterConfig"/> and
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
/// <b>A pick ends the current setup — at the click.</b> Choosing a folder
/// returns the whole setup surface to its pre-setup state
/// (<see cref="EndCurrentSetupAsync"/>, shared with the <c>Clear</c> affordance,
/// which encodes the same decision): folder and picked slot, saved filters,
/// committed mix and mix draft, filter surface, and every pick-scoped notice
/// and match count.
/// Nothing the user selected against the previous corpus can be assumed to mean
/// the same thing against the next one, so Start is always re-gated by a pick,
/// never inherited across one. Two things deliberately survive:
/// <see cref="ShuffleOption"/>, a presentation-only preference in the same class
/// as the mix panel's persisted rows, and the lifetime-stats slot, whose whole
/// point is to <i>resume</i> when its folder is picked again.
/// </para>
///
/// <para>
/// The reset fires on the <i>gesture</i>, before the picker opens — not on a
/// successful outcome. That is what keeps the OS picker and the browser's
/// permission prompts from playing out over the outgoing setup's populated
/// screen, and it settles the cancelled case by construction: a cancelled pick
/// lands on the initial screen plus <see cref="_cancelledPickNotice"/>, and the
/// folder that was held is gone. Deliberate — the previous folder is
/// <i>not</i> snapshotted and restored, because "choose a folder" ends the
/// current setup whatever the picker then returns. One consequence to expect: a
/// successful pick re-mounts the <see cref="FilterPanel"/> the reset unmounted,
/// so its <c>localStorage</c> restore re-stages the persisted config as dirty on
/// <i>every</i> pick — the already-accepted fresh-load behavior, now routine
/// rather than exceptional (see <see cref="ResetFilterSurface"/>).
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
public partial class Home : ComponentBase, IDisposable
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
    /// <b>Both mechanisms reach it, by different routes.</b> Only
    /// <see cref="IFolderAccess.PickFolderAsync"/> ever <i>reports</i> a
    /// cancellation as an outcome; a dismissed <c>webkitdirectory</c> picker
    /// fires no change event at all, so the fallback's dismissal is caught
    /// instead through that input's own <c>cancel</c> event
    /// (<see cref="HandleFallbackCancelled"/>). Wiring it became necessary when
    /// the setup reset moved to the click: silence there would now leave the user
    /// on a screen the gesture had just emptied. (An empty <i>selection</i> is a
    /// different outcome and lands on <see cref="_emptyFolderNotice"/>.)
    /// </para>
    ///
    /// <para>
    /// The <c>cancel</c> route is best-effort by nature: it depends on the
    /// browser firing that event, which current Chromium, Firefox, and Safari do
    /// but older versions may not. Where it never arrives the outcome degrades to
    /// the silence that preceded it — no wrong statement is made, only a missing
    /// one. Blazor's side is not in doubt: it registers <c>cancel</c> as a
    /// non-bubbling event and attaches a direct listener to the element.
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
    /// <c>beginPick</c> for why the two prompts cannot be collapsed into
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
    /// What the last-applied filter matched, shown near the filters so the user
    /// knows what they selected before starting; <see langword="null"/> when
    /// nothing is shown (before an Apply, after a filter edit, or after a
    /// new/cleared pick). A per-visit affordance, so a component field. Set from
    /// <see cref="QuizController.SummarizeMatchesAsync"/> on Apply — the pre-mix
    /// pool, "decisions that match", not "problems you'll see" (a few
    /// forced-move passes auto-skip at quiz time).
    ///
    /// <para>
    /// <b>One value carries both halves of the display.</b> The count line
    /// renders <see cref="AnswerTypeDistribution.Total"/> and the breakdown
    /// renders the five buckets, so the number and its decomposition come from a
    /// single fold of a single enumeration and cannot disagree — the reason this
    /// field is the distribution rather than an <c>int</c> beside one.
    /// </para>
    ///
    /// <para>
    /// Because the summary is filter-only (<see cref="QuizController.SummarizeMatchesAsync"/>
    /// composes with <see cref="QuizMix.Empty"/>), a committed mix makes it the
    /// pool the quiz is <i>drawn from</i> rather than the quiz itself —
    /// potentially far larger. The markup states that relationship beside the
    /// number whenever <see cref="HasCommittedMix"/>; the summary itself stays
    /// pool-only. Showing the composed length instead would mean composing
    /// against the lifetime stats before Start, which is Start's job and
    /// deliberately not attempted here.
    /// </para>
    /// </summary>
    private AnswerTypeDistribution? _matchSummary;

    /// <summary>
    /// True while <see cref="QuizController.SummarizeMatchesAsync"/> runs on Apply.
    /// The first count after a pick parses the corpus once (warming the shared
    /// cache so Start is then instant), so the whole setup surface disables and
    /// the busy cursor shows — folded into the same fieldset-disable / app-busy
    /// boundary the controller's transition gate drives, which also serializes
    /// the count against a Start (no concurrent parse of the same corpus).
    /// </summary>
    private bool _isCounting;

    /// <summary>
    /// True while this page is running a foreground operation the user must
    /// wait out: the scan-and-buffer half of a folder pick (issues #48), and a
    /// mix commit (#44). One flag, not one per site — the affordance is a
    /// property of the <i>page</i> ("BgQuiz is working, don't touch anything"),
    /// not of the operation, and the operations cannot overlap because the busy
    /// state disables every control that could start a second one. Raised only
    /// through <see cref="EnterBusyAsync"/> / <see cref="RunBusyAsync"/>, which
    /// own the paint-before-the-work discipline.
    /// </summary>
    private bool _busy;

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
    /// The mix half of the start gate, <b>derived</b> on every read — never a
    /// stored flag: the draft is dirty exactly while it fails to build or
    /// builds something other than the committed mix
    /// (<see cref="MixDraft.Matches"/> over <see cref="QuizMix"/> content
    /// equality). A dirty mix would let Start run a mix that differs from what
    /// the panel shows — the filter gate's hazard, mix flavored. The blank
    /// draft builds <see cref="QuizMix.Empty"/> and so matches the passthrough
    /// default: a user who never touches the mix is never gated. Because
    /// draft and holder are sibling Scoped services, the judgment cannot
    /// outlive the state it judges — the stored flag's three wedge variants
    /// (remove-last-row, finding AK's navigate-away, navigate-back-over-
    /// committed) are unrepresentable, and their reconcile choreography is
    /// gone. Gated-but-recoverable is the worst remaining state: whenever this
    /// is true, the panel is showing the divergent draft with Apply or Reset
    /// as the visible way out.
    /// </summary>
    private bool MixDirty => !MixDraft.Matches(AppliedMix.Current);

    /// <summary>
    /// Three gates: filters applied, a folder with problem files picked, and
    /// the mix draft in agreement with the committed mix (see
    /// <see cref="MixDirty"/>).
    /// </summary>
    private bool CanStart => AppliedFilter.IsApplied && Folder.HasFiles && !MixDirty;

    /// <summary>
    /// The mix panel's <i>Apply Mix</i> gate: whether a filter has been applied
    /// for the folder currently picked
    /// (<see cref="AppliedFilter.WasAppliedFor"/> against the live
    /// <see cref="PickedProblemFolder.PickGeneration"/>). Ratified UX
    /// sequencing, not a data-flow requirement — the mix composes over the
    /// filtered pool at <i>Start</i>, so applying one first is legal and
    /// harmless in the pipeline; what it isn't is legible, because the panel
    /// gives no hint that the mix draws from the filter's pool. Gating the
    /// gesture is what states the dependency direction.
    ///
    /// <para>
    /// <b>Derived, and deliberately not coupled to the mix's lifetimes.</b>
    /// Nothing about <see cref="MixDraft"/> or <see cref="AppliedMix"/> changes
    /// for this: the gate is a property of the <i>filter</i> and the <i>pick</i>
    /// alone, read live per render. The (AK) wedge came from a stored judgement
    /// whose inputs lived in a different lifetime; here there is no stored
    /// judgement, and the two facts share the app scope.
    /// </para>
    ///
    /// <para>
    /// <b>Editing the filter does not revoke it</b> (the stamp survives
    /// <see cref="AppliedFilter.Clear"/>): the corpus has been filtered, and
    /// re-gating a mix the user is midway through composing would punish an
    /// unrelated edit. <b>A new pick does</b>, by construction — the generation
    /// bumps. And <i>Reset</i> stays ungated in every state, so a hydrated draft
    /// that gates Start always has its visible way out even before a filter is
    /// applied.
    /// </para>
    /// </summary>
    private bool MixApplyEnabled => AppliedFilter.WasAppliedFor(Folder.PickGeneration);

    /// <summary>
    /// The muted hint the mix panel shows while <see cref="MixApplyEnabled"/>
    /// is false — the host-owned sentence, mirroring
    /// <see cref="SavedFiltersDisabledReason"/>'s contract with
    /// <c>SavedFiltersPanel</c>. It states the <i>reason</i> for the ordering
    /// (the mix draws from the filtered pool), not merely the rule, because the
    /// rule alone is what the user found arbitrary.
    /// </summary>
    private string? MixApplyDisabledReason =>
        MixApplyEnabled
            ? null
            : "Apply the filters above first — the mix draws its problems from the "
              + "filtered pool, so the filters come first.";

    /// <summary>
    /// Whether a non-passthrough mix is committed — the single fact two
    /// unrelated statements on this page derive from (the match count's caveat
    /// and the shuffle checkbox's disabled state, via
    /// <see cref="MixOwnsOrder"/>), so neither can be true while the other
    /// reads the mix differently.
    /// </summary>
    private bool HasCommittedMix => !AppliedMix.Current.IsPassthrough;

    /// <summary>
    /// True while the committed mix has entries: presentation order belongs to
    /// the mix's own Random-order setting, so the standalone Shuffle checkbox
    /// is disabled — but its held value is deliberately left untouched, so
    /// clearing the mix restores the user's prior shuffle preference. A named
    /// <i>consequence</i> of <see cref="HasCommittedMix"/>, not a second copy of
    /// the predicate: the markup that disables the checkbox should say why it is
    /// disabled.
    /// </summary>
    private bool MixOwnsOrder => HasCommittedMix;

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
        // The start gate derives from the mix draft (see MixDirty), and the
        // draft is edited inside MixPanel — a child whose gestures don't pass
        // through this component. Subscribe so any draft change re-renders the
        // gate (the standard Blazor state-container pattern; every mutation
        // happens on the renderer's sync context, so StateHasChanged is safe
        // to hand over directly). Unsubscribed in Dispose.
        MixDraft.Changed += StateHasChanged;

        // Hydrate the user's settings here, where every quiz begins. Nothing on
        // this page renders them, but the Quiz page's board does, on its very
        // first render — and it gets there only through Start, long after this
        // read has landed. Kicking off from the entry point rather than from the
        // consumer is what keeps the board free of a hydration render gate; the
        // call is idempotent, so Quiz awaiting the same task costs nothing.
        await Settings.EnsureHydratedAsync();

        if (await Marker.WasLiveAsync() && !Controller.HasStarted)
        {
            _showReloadNotice = true;
            await Marker.ClearAsync();
        }

        _fsAccessAvailable = await FolderAccess.SupportsDirectoryPickerAsync();
    }

    /// <summary>Detach from the app-scoped draft — the page dies before the Scoped service does.</summary>
    public void Dispose() => MixDraft.Changed -= StateHasChanged;

    /// <summary>
    /// The page's one busy predicate, driving <i>both</i> halves of the busy
    /// affordance — the <c>app-busy</c> progress cursor and the whole-surface
    /// <c>&lt;fieldset disabled&gt;</c> — from a single expression, so the cursor
    /// and the disabled controls can never disagree about whether the page is
    /// working. A union of the independent sources: the controller's transition
    /// gate (Start / Restart), this page's own foreground work
    /// (<see cref="_busy"/>), and the match count, which keeps its own flag
    /// because it also owns a message and a stale-request id
    /// (<see cref="_isCounting"/>) — anything still claiming to be counting must
    /// still read busy.
    /// </summary>
    private bool IsBusy => Controller.IsBusy || _busy || _isCounting;

    /// <summary>
    /// Raise the busy affordance <i>and let it paint</i>, then return.
    ///
    /// <para>
    /// The yield is the whole point, and the trap this method exists to make
    /// unrepeatable: WebAssembly runs Blazor on one thread, so a busy state set
    /// immediately before synchronous — or merely uninterrupted — work never
    /// reaches the screen. <see cref="ComponentBase.StateHasChanged"/> only
    /// <i>queues</i> the render; the queue drains when the thread is handed
    /// back, which <c>await Task.Yield()</c> does. The work that follows must
    /// therefore be genuinely async (every current caller's is: JS interop or a
    /// yielding parse), or it will hold the thread and the paint will land after
    /// the busy state is already over.
    /// </para>
    ///
    /// <para>
    /// Callers that own a whole operation should prefer
    /// <see cref="RunBusyAsync"/>, which pairs this with the lowering. This
    /// bare form exists for the folder pick, whose raise point sits <i>inside</i>
    /// <see cref="IFolderAccess.PickFolderAsync"/> (at the browser-prompt seam)
    /// while its lowering belongs to the whole gesture.
    /// </para>
    /// </summary>
    private async Task EnterBusyAsync()
    {
        _busy = true;
        StateHasChanged();
        await Task.Yield();
    }

    /// <summary>
    /// Run <paramref name="work"/> under the busy affordance: raise it, let it
    /// paint (<see cref="EnterBusyAsync"/>), run, and lower it however the work
    /// ends. The whole-operation form of the page's one busy idiom.
    /// </summary>
    private async Task RunBusyAsync(Func<Task> work)
    {
        await EnterBusyAsync();
        try
        {
            await work();
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// The one "pick a folder" gesture. Probes the mechanism at pick time:
    /// with File System Access, the whole pick (picker, permission,
    /// enumeration, buffering) completes inside <see cref="IFolderAccess"/>;
    /// without it, this click only opens the hidden <c>webkitdirectory</c>
    /// input's picker and the pick arrives later via that input's own change
    /// event (<see cref="HandleFallbackPickedAsync"/>) or, on a dismissal, its
    /// cancel event (<see cref="HandleFallbackCancelled"/>).
    ///
    /// <para>
    /// <b>The setup ends at the click.</b> <see cref="EndCurrentSetupAsync"/>
    /// runs <i>before</i> the mechanism fork, so the screen is back at its
    /// initial no-folder state — guidance up, nothing else disclosed — by the
    /// time the OS picker and the browser's permission prompts appear. They
    /// used to play out over the previous folder's fully-populated screen, which
    /// read as though that setup were still standing behind them. It is the
    /// whole reset (see that method), not a cosmetic one: choosing a folder ends
    /// the current setup whatever the picker then returns.
    /// </para>
    ///
    /// <para>
    /// The reset is inside the <c>try</c>: its one fallible step is the picked-
    /// slot interop, and a browser failure there belongs in the pick-error
    /// banner like every other, never faulting the WebAssembly app.
    /// </para>
    /// </summary>
    private async Task PickFolderAsync()
    {
        try
        {
            await EndCurrentSetupAsync();

            // The authoritative mechanism fork, re-probed per gesture. No
            // guidance state is toggled here: the prompt note is back on screen
            // (the reset above cleared the folder that was hiding it) and hides
            // itself again once this pick leaves a folder held.
            if (await FolderAccess.SupportsDirectoryPickerAsync())
            {
                // EnterBusyAsync is handed *in*, not called here: the pick's
                // browser prompts must not run under a busy state (they are the
                // user's turn, not the app's), and the scan that follows them
                // never yields to the renderer on its own. So the affordance is
                // raised at the seam between the two, from inside the pick — and
                // stays up past the enumeration, the buffering, and the
                // saved-filters read, until the summary this method's caller
                // renders is on screen. Lowered in the finally, which covers the
                // cancelled path (no hook fired — nothing was raised) too.
                var outcome = await FolderAccess.PickFolderAsync(EnterBusyAsync);
                await ApplyPickOutcomeAsync(outcome);
            }
            else
            {
                // Nothing to be busy for yet: this only opens the hidden input's
                // picker and returns. The fallback's work — and its busy state —
                // begins when the browser reports a selection, in
                // HandleFallbackPickedAsync. (Raising it here would have no
                // reliable end: a dismissed fallback picker's cancel event is
                // best-effort, so the page could be left disabled forever.)
                await FolderAccess.TriggerFallbackPickerAsync(_fallbackInput);
            }
        }
        catch (Exception ex)
        {
            Folder.Clear();
            _pickError = ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// The fallback pick landing: the hidden input's FileList is collected and
    /// filtered by the JS module (top-level <c>.xg</c> / <c>.xgp</c> only).
    /// Capability is always <see cref="StatsSaveCapability.BrowserUnsupported"/>
    /// on this mechanism — no writable handle exists.
    ///
    /// <para>
    /// No reset of its own: this is the tail of a gesture
    /// <see cref="PickFolderAsync"/> already ended the setup for.
    /// </para>
    ///
    /// <para>
    /// This <i>is</i> the fallback's busy seam — the whole method runs after the
    /// user has chosen, so unlike the File System Access path there is no
    /// prompt-versus-work boundary to find inside the call. Both mechanisms end
    /// up meaning the same thing by the busy state: the app is processing a
    /// selection the user has made.
    /// </para>
    /// </summary>
    private Task HandleFallbackPickedAsync(ChangeEventArgs _) => RunBusyAsync(async () =>
    {
        try
        {
            await ApplyPickOutcomeAsync(await FolderAccess.CollectFallbackAsync(_fallbackInput));
        }
        catch (Exception ex)
        {
            Folder.Clear();
            _pickError = ex.Message;
        }
    });

    /// <summary>
    /// The hidden <c>webkitdirectory</c> input's dismissal: the fallback
    /// mechanism's cancelled pick, landing on the same
    /// <see cref="_cancelledPickNotice"/> the File System Access mechanism uses
    /// (its wording is cause-agnostic, so it is true here too). Without this the
    /// gesture would end on a screen the click had just reset, with no account
    /// of why — the very silence the notice exists to end.
    /// </summary>
    private void HandleFallbackCancelled()
    {
        _cancelledPickNotice = true;
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
            // view-files permission, which leaves the user needing an
            // explanation — see _cancelledPickNotice. Safe to set on the way out
            // because EndCurrentSetupAsync ran at pick *start*, not here; the
            // screen it left is the initial no-folder one, which this notice now
            // accounts for.
            _cancelledPickNotice = true;
            return;
        }

        if (outcome.Files.Count == 0)
        {
            // Both are already clear — EndCurrentSetupAsync ran at the click and
            // nothing since could have set them. Re-stated so this shared landing
            // carries its own postcondition ("an empty pick holds no folder")
            // rather than inheriting it from whoever called it.
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

    /// <summary>
    /// Return the filter half of the setup surface to its pre-setup state — the
    /// <see cref="AppliedMix.Reset"/> sibling, called from the same place for the
    /// same reason: ending a setup must leave nothing the user selected against
    /// the <i>previous</i> corpus silently in force. Without this a re-pick left
    /// the old filter applied — Start enabled immediately, against a folder whose
    /// files the filter had never been weighed against.
    ///
    /// <para>
    /// Two writes, and the first is <b>not</b> redundant — it is the only one
    /// that clears the applied state. <see cref="FilterPanel.LoadConfig"/> is a
    /// staging gesture like any other, so it raises the panel's applied-state
    /// report; but that report is about the <i>panel</i>, and it says nothing
    /// useful here. It cannot be relied on (a gesture made from the no-folder
    /// state has no mounted panel to raise it, and a filter applied in an
    /// earlier setup would keep satisfying the gate), and where a panel
    /// <i>is</i> mounted it can actively disagree: staging the defaults into a
    /// panel that had committed the defaults is a genuinely clean state, which
    /// the panel reports as such. <see cref="HandleAppliedStateChanged"/>
    /// therefore ignores every report made with no folder held, which is exactly
    /// the state this method runs in — leaving the
    /// <see cref="AppliedFilter.Clear"/> below the last word on both paths.
    /// </para>
    ///
    /// <para>
    /// A <see cref="FilterConfig"/> straight from its parameterless constructor
    /// is exactly what the panel's own <i>Reset</i> button hydrates from, so
    /// "defaults" needs no second definition here.
    /// <see cref="FilterPanel.LoadConfig"/> is staging-only — it persists
    /// nothing — so the user's last-applied filter survives in the panel's
    /// <c>localStorage</c> and still restores on the next boot, mirroring
    /// <see cref="AppliedMix.Reset"/>'s deliberate hands-off treatment of the
    /// stored mix. The reset is about this session's setup not outliving its
    /// corpus, never about defeating the panel's persistence.
    /// </para>
    ///
    /// <para>
    /// <b>Not <c>@key</c>-based, unlike <c>MixPanel</c>.</b> Re-mounting the
    /// filter panel would re-run its first-render <c>localStorage</c> restore and
    /// stage the <i>persisted</i> config — the opposite of defaults. (MixPanel is
    /// keyed for precisely that effect: its re-mount re-hydrates the discarded
    /// draft, re-offering the persisted mix gated by the derived rule.)
    /// That distinction now decides less than it reads: since the reset moved to
    /// the click, <b>every</b> pick unmounts the panel (the gate closes with the
    /// folder) and the successful ones re-mount it, so the persisted config is
    /// re-staged on every pick, exactly as on a fresh load. The
    /// <see cref="AppliedFilter.Clear"/> above is what holds either way — Start
    /// re-gates behind its Apply hint, so a re-staged config is shown but never
    /// claimed as applied. What the un-keyed panel still buys is the
    /// <i>cancelled</i> pick and the <c>Clear</c>, which end with no panel at all
    /// and no restore to re-stage anything.
    /// </para>
    /// </summary>
    private void ResetFilterSurface()
    {
        AppliedFilter.Clear();
        _filterPanel?.LoadConfig(new FilterConfig());
    }

    /// <summary>
    /// End the current setup: return the whole surface to its pre-setup,
    /// no-folder state. The single reset behind <i>both</i> gestures that end a
    /// setup — the <c>Clear</c> affordance, and the <i>start</i> of a pick
    /// gesture (<see cref="PickFolderAsync"/>) — because they encode the same
    /// decision, and two spellings of one decision drift.
    ///
    /// <para>
    /// <b>Everything pick-scoped goes.</b> The folder holder and the JS module's
    /// picked slot, this folder's saved filters, both halves of the mix state
    /// (the committed <see cref="AppliedMix"/> and the <see cref="MixDraft"/>,
    /// see the inline comment), the filter surface
    /// (<see cref="ResetFilterSurface"/>), and every pick-scoped notice
    /// and match count (<see cref="ClearPickNotices"/>). Two things deliberately
    /// survive, and the class summary says why: <see cref="ShuffleOption"/> and
    /// the lifetime-stats slot.
    /// </para>
    ///
    /// <para>
    /// <b><see cref="AppliedFilter"/> is reset here too</b> — superseding an
    /// earlier ruling that <c>Clear</c> should leave it alone as "edit-coupled,
    /// not pick-coupled". Under one shared reset the applied filter is coupled to
    /// neither gesture in particular but to the <i>setup</i>: ending one ends it.
    /// (It stays edit-coupled as well — <see cref="HandleAppliedStateChanged"/>
    /// clears it while the panel reports uncommitted edits. The two rules are
    /// independent, not duplicates.) On the
    /// <c>Clear</c> path this is invisible in the UI, since the panel and Start
    /// are both behind the disclosure gate a cleared folder closes; the value is
    /// that there is now one reset to reason about instead of two that differed
    /// by a line.
    /// </para>
    ///
    /// <para>
    /// <b>Safe mid-quiz.</b> The picked files are read only at Start time (the
    /// source factory reads <see cref="PickedProblemFolder.Files"/> in
    /// <see cref="QuizController.StartAsync"/>) and the JS side clears the
    /// <i>picked</i> slot only — a running quiz's stats context lives in the
    /// <i>active</i> slot, bound at Start, so recording continues untouched until
    /// the next Start re-binds.
    /// </para>
    ///
    /// <para>
    /// The render is deliberate, not incidental: a pick's next step is the OS
    /// picker and the browser's permission prompts, which must not appear over
    /// the outgoing setup's populated screen. <c>StateHasChanged</c>
    /// queues the returned-to-initial paint and the awaited picked-slot interop
    /// yields the thread for it to land — the same paint-before-the-churn idiom
    /// <see cref="HandleFilterConfigApplied"/> uses.
    /// </para>
    /// </summary>
    private async Task EndCurrentSetupAsync()
    {
        Folder.Clear();
        SavedFilters.Reset();
        // Both halves of the mix state end with the setup: the committed mix
        // (a pick means a new corpus and possibly a new stats slot) and the
        // draft's uncommitted edits (made against the outgoing slot — stale
        // noise against the next). Discard also forgets hydration, so an
        // Enabled pick's re-mounted panel re-offers the persisted mix — gated
        // by the derived rule against the now-passthrough holder — while a
        // no-stats pick mounts no panel, re-hydrates nothing, and leaves the
        // blank draft matching the reset holder: the mix plays no part in its
        // Start, with no capability fork in the gate.
        AppliedMix.Reset();
        MixDraft.Discard();
        ResetFilterSurface();
        ClearPickNotices();

        StateHasChanged();
        await FolderAccess.ClearPickedAsync();
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
        // A new/cleared pick changes the corpus, so any match summary is stale;
        // the bumped id also discards a count still in flight from before.
        _matchSummary = null;
        _countRequestId++;
    }

    /// <summary>
    /// The panel's <i>Apply</i> (and <i>Clear filters</i>) commit — the gesture
    /// that moves the committed config.
    ///
    /// <para>
    /// It records the applied state itself rather than leaving that to the
    /// applied-state report that follows it (<see cref="HandleAppliedStateChanged"/>,
    /// which re-affirms the same config a moment later). Two independent rules,
    /// not one written twice: <i>a commit applies the filter</i>, and <i>a panel
    /// whose buffers equal what it committed is still applied</i>. The first
    /// must not depend on the second having fired.
    /// </para>
    /// </summary>
    private async Task HandleFilterConfigApplied(FilterConfig cfg)
    {
        // Record the deliberate applied state on the scoped holder so it
        // survives navigate-back (not a transient field), stamped with the pick
        // it was applied against — the fact the mix gate reads (see
        // MixApplyEnabled).
        AppliedFilter.Set(cfg, Folder.PickGeneration);
        _startError = null;
        _noMatchNotice = null;
        await ShowMatchSummaryAsync(cfg);
    }

    /// <summary>
    /// The panel's applied-state report, raised after <em>every</em> gesture
    /// that touches its edit buffers (a control edit, a
    /// <see cref="FilterPanel.LoadConfig"/> staging, Apply, Clear filters). The
    /// payload is the committed <see cref="FilterConfig"/> the buffers now
    /// equal, or <c>null</c> when they equal none — so it is the whole answer to
    /// "is the panel's selection still the one the user applied?", which is the
    /// filter half of the start gate.
    ///
    /// <para>
    /// <b>Handled statelessly, as the producer's contract requires.</b> The
    /// event is per-gesture, not transition-only: it re-states the current
    /// answer every time rather than firing only on a change, because the
    /// panel's committed config dies with the panel while this page's applied
    /// state survives navigation. So this assigns from the payload and never
    /// diffs it against a remembered previous one.
    /// </para>
    ///
    /// <para>
    /// <b>A clean report re-applies — the point of the whole wiring.</b> An edit
    /// undone back to the applied values is clean again, and the panel says so;
    /// Start re-enables without a re-Apply. It has to: the panel's own Apply is
    /// disabled in exactly that state (there is nothing new to commit), so a
    /// consumer that only ever <i>cleared</i> on an edit would wedge the user
    /// behind two disabled buttons.
    /// </para>
    ///
    /// <para>
    /// <b>No folder held ⇒ nothing to apply against.</b> The panel lives behind
    /// the progressive-disclosure gate, so every <i>user</i> gesture reaches
    /// here with a folder in hand. The one report that doesn't is
    /// <see cref="ResetFilterSurface"/>'s staging, which runs after the folder
    /// is already gone — and staging defaults into a panel that committed the
    /// defaults reports <i>clean</i>, which would otherwise re-apply the filter
    /// the reset had just cleared (and count matches against a corpus being torn
    /// down). Refusing here is what keeps that reset's explicit
    /// <see cref="AppliedFilter.Clear"/> the last word.
    /// </para>
    /// </summary>
    private async Task HandleAppliedStateChanged(FilterConfig? config)
    {
        if (!Folder.HasFiles) return;

        // Any gesture on the panel moots a stale save-as refusal — fixing the
        // offending position pattern is itself one, and so is committing.
        _filterSaveError = null;

        if (config is null)
        {
            // Uncommitted edits pending: a half-edited set re-gates Start, so it
            // must clear the applied state, not just a local flag. Any shown or
            // in-flight match summary described the config now abandoned; the
            // bumped id discards a late-landing result.
            AppliedFilter.Clear();
            _matchSummary = null;
            _countRequestId++;
            return;
        }

        // Idempotence: the report is per-gesture, and a commit raises it right
        // after HandleFilterConfigApplied has already applied the same config
        // and counted it — so re-running the summary here would count the same
        // pool twice (two parses, two busy flashes). Skip the side effect when
        // this config is already the applied one *and* its count is current;
        // re-affirming the holder itself is free and keeps this the one handler
        // that always mirrors the payload.
        var summaryIsCurrent = config.Equals(AppliedFilter.Config)
                               && (_matchSummary is not null || _isCounting);

        // The live generation, deliberately: the report re-affirms the config
        // for the pick it is being made against, which is the same pick the
        // Apply was made for.
        AppliedFilter.Set(config, Folder.PickGeneration);
        if (summaryIsCurrent) return;

        // Clean again after an edit — restore the count the edit dropped.
        // Without this the user has no way back to it: Apply is disabled
        // precisely because there is nothing new to apply.
        await ShowMatchSummaryAsync(config);
    }

    /// <summary>
    /// Show what <paramref name="cfg"/> matches — how many decisions, and what
    /// kinds of answer they call for. The first pass after a pick parses the
    /// corpus once and warms the shared cache, so the Start that follows is
    /// instant — the count is not a separate cost. Summarizing lives in the
    /// controller; Home only stamps a request id (so a stale result can't land)
    /// and drives the busy affordance. Shared by the two paths that can leave
    /// the panel clean — a commit and a re-affirm — so the count is defined
    /// once.
    /// </summary>
    private async Task ShowMatchSummaryAsync(FilterConfig cfg)
    {
        var requestId = ++_countRequestId;
        _matchSummary = null;
        _isCounting = true;
        // Under the page's shared busy affordance, which paints before the
        // (possibly one-time-parse) count begins. _isCounting stays this site's
        // own flag: it also drives the "Counting matching decisions…" line and
        // is subject to the stale-request rule, neither of which the generic
        // affordance knows about.
        await RunBusyAsync(async () =>
        {
            try
            {
                var summary = await Controller.SummarizeMatchesAsync(cfg);
                if (requestId != _countRequestId) return; // superseded — discard
                _matchSummary = summary;
            }
            catch
            {
                // The count is advisory: never let it block Apply or fault the
                // app. Start still validates the config and surfaces any real
                // error.
                if (requestId == _countRequestId) _matchSummary = null;
            }
            finally
            {
                if (requestId == _countRequestId) _isCounting = false;
            }
        });
    }

    /// <summary>
    /// Load a saved filter into the panel: resolve the config from the store's
    /// collection and stage it via <see cref="FilterPanel.LoadConfig"/>. That
    /// staging raises the panel's applied-state report (→
    /// <see cref="HandleAppliedStateChanged"/>), normally <c>null</c> — a load is
    /// a bulk edit, not a commit — so Start re-gates until the user Applies.
    /// Loading the filter that is <i>already</i> applied stages exactly the
    /// committed config, which reports clean and leaves Start armed: nothing
    /// changed, so nothing re-gates. Read-only over the in-memory collection;
    /// nothing is persisted.
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
        // The user committed (Apply, Reset, or the last-row removal — the
        // latter two an explicit apply of the blank mix): adopt into the
        // scoped holder, so the derived gate agrees with the draft again and
        // the choice survives navigate-back. A commit also moots any standing
        // refusal. The panel has already persisted the commit (committed-only
        // persistence, via MixDraft.PersistAsync).
        AppliedMix.Apply(mix);
        _mixRefused = false;
        _startError = null;
        _noMatchNotice = null;
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
