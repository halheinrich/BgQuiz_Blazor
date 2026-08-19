using BgFolderAccess_Razor;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;

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
/// weighted mix carries a <i>further</i> gate, and it is the one shared
/// predicate rather than a second reading of the pick: it renders only while
/// <see cref="QuizStatsStore.CanWeightMix"/> — this folder can save stats
/// <i>and</i> already holds a record with something in it (issue #87). Where
/// that is false the mix plays no part in Start — the panel is hidden and every
/// pick revokes the mix consent (see
/// <see cref="EndCurrentSetupAsync"/>), so Start runs plain with no mix gate,
/// warning, or refusal. Nothing is shown disabled and no reason is offered: the
/// non-mount <i>is</i> the answer, and the accepted consequence is that a
/// brand-new folder offers no mix until its own first quiz creates the stats a
/// mix would weight by.
/// </para>
///
/// <para>
/// That predicate reads a probe of the picked folder's stats document, and this
/// page owns both of its reading points
/// (<see cref="QuizStatsStore.RefreshPickedStatsAsync"/>): every successful
/// pick's landing (<see cref="ApplyPickOutcomeAsync"/>), and this page's own
/// initialization — which is what lets the answer change after the quiz that
/// created the record, since returning here re-instantiates the page. The
/// saved-filters persist gate is deliberately <b>not</b> routed through it and
/// stays capability-only: saved filters have nothing to do with a stats record,
/// and a folder with none must still be able to save them.
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
/// <see cref="FolderWriteCapability"/> verdict rides on the holder and drives
/// this page's stats status notice; the stats lifecycle itself is not this
/// page's concern — the controller binds the stats context at Start.
/// </para>
///
/// <para>
/// Start is gated on four conditions (<see cref="CanStart"/>): a filter in
/// effect for the pick on screen (<see cref="FilterInEffect"/> — applied, and
/// not since edited), a folder picked with at least one problem file, a
/// filtered pool not <i>known</i> to be empty (<see cref="_matchSummary"/>
/// with <c>Total: 0</c> — known-zero only, so a null or still-computing
/// summary never gates and the no-match outcome notice stays the backstop
/// for races), and an effective mix (<see cref="EffectiveMix"/> non-null —
/// null means "Mix applies" is checked over an invalid draft, the one mix
/// state that gates; an unchecked mix never does, per the spec's §5). Each
/// gate has its own sibling hint stating its reason. Everything the gate
/// reads lives in per-app scoped services (<see cref="AppliedFilter"/>,
/// <see cref="PickedProblemFolder"/>, <see cref="MixConsent"/>,
/// <see cref="MixDraft"/>) rather than transient component fields, so the
/// gate survives in-app navigation — when the page is re-instantiated on
/// navigate-back it re-derives from the holders instead of resetting. On
/// Start the applied <see cref="FilterConfig"/> and the effective
/// <see cref="BgGame_Lib.QuizMix"/> — the on-screen draft's build when
/// consented, the passthrough otherwise — are handed to the
/// <see cref="QuizController"/>, whose source factory builds a
/// <see cref="WasmUploadedProblemSetSource"/> over the picked files, and the
/// app navigates to <c>/quiz</c>. Pick failures and
/// <see cref="FilterConfig.Build"/> / source-construction failures are caught
/// and surfaced as banners rather than faulting the WebAssembly app. The two
/// non-failure pick outcomes — a pick that ended holding no folder, and one that
/// held a folder with no problem files — each get their own polite notice rather
/// than silence, so no gesture ever returns the user to an unchanged page with
/// no account of what happened. Every outcome/status notice in the pick band
/// dismisses on a click (issue #107, the Quiz page's affordance): the
/// holder-backed pair — truncations and stats capability, which survive
/// navigation with the pick they describe — record their dismissal in the
/// app-scoped <see cref="QuizNoticeDismissal"/> keyed on
/// <see cref="PickedProblemFolder.PickOccurrence"/>, while the per-visit pair
/// clear their own fields (see <see cref="DismissCancelledPick"/>). The red
/// pick-error banner alone stays undismissible: it is a failure report
/// (<c>role="alert"</c>), not an outcome. A
/// weighted start with no lifetime stats is <i>refused</i> as an outcome (the
/// actionable notice with its per-run "Start without mix" override — see
/// <see cref="StartCoreAsync"/>), never silently run unweighted.
/// </para>
///
/// <para>
/// <b>A pick ends the current setup — at the click.</b> Choosing a folder
/// returns the whole setup surface to its pre-setup state
/// (<see cref="EndCurrentSetupAsync"/>, shared with the <c>Clear</c> affordance,
/// which encodes the same decision): folder and picked slot, the mix consent
/// bit and the mix draft, the applied filter, and every pick-scoped notice and
/// match count.
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
/// current setup whatever the picker then returns.
/// </para>
///
/// <para>
/// <b>The filter half of that reset rides on the composite's lifecycle.</b>
/// The <c>FilterSurface</c> this page hosts lives behind the same
/// <c>HasFiles</c> gate as the rest of the setup surface, so ending a setup
/// <i>unmounts</i> it and a successful pick mounts a fresh instance — whose
/// first parameters-set initializes against the new pick's token and re-reads
/// that folder's saved-filters document, and whose fresh panel has committed
/// nothing, so Apply is re-armed with no host reset call. (Its
/// <c>localStorage</c> restore re-stages the persisted config as dirty on
/// every pick — the accepted fresh-load behavior.) The composite's own
/// source-change rule is therefore <i>dormant</i> in this host: it runs only
/// when the bound token changes on a mounted instance, and this page's
/// gestures always change the token across an unmount. The one thing neither
/// remount nor rule covers — clearing the holder's applied config, whose
/// staleness the Start gate reads — is the single line of filter choreography
/// <see cref="EndCurrentSetupAsync"/> keeps host-side.
/// </para>
///
/// <para>
/// Two statements on this page are about the <i>app</i> rather than about the
/// quiz, and neither is gated on any pick state the setup surface uses. Beside
/// the pick button, <see cref="FolderPickDisplay.SupportedBrowsers"/> names what
/// the pick actually needs from a browser — deliberately <b>not</b> behind the
/// <see cref="_fsAccessAvailable"/> probe, because the visitor it is written for
/// (a browser that can serve neither mechanism, where the pick may raise nothing
/// at all) is precisely the one that probe excludes. In the footer, <see cref="AppInfo.Version"/>
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
///
/// <para>
/// Below the setup surface — outside the busy <c>fieldset</c>, since it only
/// navigates — sits the same conditional <b>"Back to quiz"</b> button
/// <see cref="Help"/> and <see cref="Settings"/> carry, on the same
/// <c>HasStarted &amp;&amp; !IsFinished</c> predicate (issue #58). Home is the
/// third page a user can reach mid-quiz and the last one that had no way back.
/// The visit itself was always safe — see <see cref="BackToQuiz"/> — so the
/// affordance is the whole change.
/// </para>
/// </summary>
public partial class Home : ComponentBase, IDisposable
{
    private string? _startError;

    /// <summary>
    /// Sibling of <see cref="_startError"/> for pick failures — an unexpected
    /// browser error, or a file past the <see cref="PickedFileLimits.MaxFileBytes"/>
    /// cap. A per-visit failure banner (assertive), like the start error.
    /// A folder past the <i>count</i> caps is not a failure: it truncates and
    /// reports (<see cref="PickedProblemFolder.Truncations"/>).
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
    /// three causes — the picker was dismissed, the required view-files
    /// permission was declined, or a present-but-inert
    /// <c>showDirectoryPicker</c> aborted without ever opening a chooser
    /// (issue #116, observed live on a WebView-wrapping browser) — and only the
    /// first is the user changing their mind: the second is the load-bearing
    /// grant refused, the third a gesture that did nothing at all, each leaving
    /// them on an unchanged, empty page with no explanation. The browser
    /// reports all of them as <c>AbortError</c>, so they are indistinguishable
    /// here; the notice is worded to be true under every cause — conditional
    /// advice, plus the FS-Access-branch dead-chooser tail — and to stay
    /// non-accusatory (see the markup comment). Distinguishing them is not
    /// attempted.
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
    /// probed once in <see cref="OnInitializedAsync"/>. Gates <b>both branches</b>
    /// of the pre-pick advice, each saying what its branch means — one snapshot,
    /// two consequences, so the page can never show both or neither.
    ///
    /// <para>
    /// <b>True</b> — the in-page guidance for the <i>two</i> browser-anchored
    /// permission prompts that mechanism's pick raises, both easily missed, each
    /// with a very different cost to declining (see the markup, and
    /// <c>folderAccess.js</c>'s <c>beginPick</c> for why the two prompts cannot
    /// be collapsed into one). Inherently FS-Access-only: the fallback mechanism
    /// raises no permission prompt to guide toward, so showing it there would
    /// promise prompts that never arrive.
    /// </para>
    ///
    /// <para>
    /// <b>False</b> — the account of a pick gesture that may do nothing at all.
    /// The hidden <c>webkitdirectory</c> input is then the only mechanism left,
    /// and whether a browser <i>honors</i> it cannot be feature-detected: the
    /// attribute exists on the input object even where the picker never opens
    /// (halheinrich/backgammon#108). So this branch does not detect a dead
    /// gesture — nothing can. It detects that the <i>undetectable</i> mechanism
    /// is the one that will run, and the notice it gates is a conditional that
    /// asserts nothing about the browser rendering it. Honest hedge over false
    /// certainty; see the markup for the full reasoning.
    /// </para>
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
    /// Set when a weighted Start was refused — the effective mix has entries
    /// but no lifetime stats are available. Drives the actionable refusal
    /// notice with its one-click per-run "Start without mix" override.
    /// Genuinely per-visit outcome state, so a component field like the
    /// banners above; cleared on a new pick (capability may change), a "Mix
    /// applies" toggle (the user has re-decided, so the refusal may be moot —
    /// see <see cref="HandleConsentChanged"/>), and every Start attempt.
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
    /// composes with <see cref="QuizMix.Empty"/>), a mix in effect makes it the
    /// pool the quiz is <i>drawn from</i> rather than the quiz itself —
    /// potentially far larger. The markup states that relationship beside the
    /// number whenever <see cref="MixInEffect"/>; the summary itself stays
    /// pool-only. Showing the composed length instead would mean composing
    /// against the lifetime stats before Start, which is Start's job and
    /// deliberately not attempted here.
    /// </para>
    ///
    /// <para>
    /// A resolved summary with <c>Total: 0</c> also <b>gates Start</b> (see
    /// <see cref="CanStart"/> — the known-zero pool rule): the count stays
    /// advisory in every other respect, but a pool the page has just told the
    /// user is empty is not one a Start click should dead-end against.
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
    /// wait out: the scan-and-buffer half of a folder pick (issue #48), and
    /// the match count. One flag, not one per site — the affordance is a
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
    /// The <c>Storage</c> the hosted <c>FilterSurface</c> gets: the picked-slot
    /// adapter while the pick's capability exposes a readable directory handle
    /// (<see cref="FolderWriteCapability.Enabled"/> — save and load — or
    /// <see cref="FolderWriteCapability.PermissionDenied"/>, whose pick gesture
    /// grants read even though the readwrite request was declined), and
    /// <see langword="null"/> otherwise: a fallback pick
    /// (<see cref="FolderWriteCapability.BrowserUnsupported"/>) has no handle to
    /// read a document from, which the composite renders as no saved-filters
    /// section at all. Everything downstream of this fact — panel visibility,
    /// the empty-and-read-only clutter rule, the LoadFailed / WriteFailed
    /// degrade notices and their copy — is the composite's now; this page
    /// supplies only the capability rulings the seam was designed to carry.
    /// The instance is app-scoped so the bound reference is stable: the
    /// composite rebuilds its store when the reference changes.
    /// </summary>
    private IFilterDocumentStorage? FilterStorage =>
        Folder.Capability is FolderWriteCapability.Enabled or FolderWriteCapability.PermissionDenied
            ? FilterDocumentStorage
            : null;

    /// <summary>
    /// The host wording half of the composite's persist gate, bound to
    /// <c>FilterSurface.PersistDisabledReason</c> beside
    /// <c>CanPersist = (Capability == Enabled)</c> — the steady-state load-only
    /// case under <see cref="FolderWriteCapability.PermissionDenied"/>. The
    /// composite forwards it only while the host's half disables persisting (a
    /// write failure is explained by its own louder, producer-owned notice), so
    /// this stays exactly the FS-Access sentence only this host can know.
    ///
    /// <para>
    /// The premise is <see cref="FolderPickDisplay.WriteAccessNotGranted"/>, not
    /// a local literal: this is the second surface stating it, and the two had
    /// already drifted once — both said "you declined", which this rung cannot
    /// know (a browser that refused to ask for the write grant lands here too),
    /// and only the
    /// stats notice was corrected. Sharing the clause is what keeps the next
    /// correction from being half-applied again.
    /// </para>
    /// </summary>
    private string? SavedFiltersDisabledReason =>
        Folder.Capability == FolderWriteCapability.PermissionDenied
            ? $"{FolderPickDisplay.WriteAccessNotGranted} — saved filters can be "
              + "loaded but not changed or deleted."
            : null;

    /// <summary>
    /// The mix that would run if Start were clicked now, <b>derived</b> on
    /// every read — the one mix fact everything downstream reads
    /// (<see cref="CanStart"/>, its hint, <see cref="MixInEffect"/>, and the
    /// hand-off in <see cref="StartCoreAsync"/>), so screen and effect cannot
    /// diverge (<c>SPEC-filtering.md</c> §5, Fork B: there is no committed
    /// copy). Unchecked, the mix is simply not in effect — the passthrough
    /// runs and nothing about the draft, however divergent from whatever ran
    /// last, gates anything. Checked, the effect is the on-screen draft's
    /// build: <see cref="QuizMix.Empty"/> for the blank draft (checked-but-
    /// inert, ruled — vacuous consent is passthrough, not an error), and
    /// <see langword="null"/> exactly when the draft fails to validate — the
    /// one mix state that gates Start, with the box left checked because it
    /// records intent and only the user moves it.
    /// </summary>
    private QuizMix? EffectiveMix =>
        MixConsent.Applies ? MixDraft.Build() : QuizMix.Empty;

    /// <summary>
    /// This page's identity for the corpus a filter can be applied against —
    /// the pick, named by its generation counter. <b>Minted here and nowhere
    /// else</b>: the hosted <c>FilterSurface</c>'s <c>Source</c> binding (what
    /// a commit is keyed to) and every gate that asks
    /// <see cref="AppliedFilter.ConfigFor"/> (what a read is compared against)
    /// both read this one property, so the two sides of the key cannot encode
    /// the pick differently. Two inline mints used to state that agreement in
    /// prose; one property makes it structural.
    /// </summary>
    private FilterSourceToken CurrentFilterSource =>
        FilterSourceToken.FromGeneration(Folder.PickGeneration);

    /// <summary>
    /// The filter in effect for the pick on screen right now, or
    /// <see langword="null"/> when none is — the single fact this page's whole
    /// filter story reads: Start's filter gate (<see cref="CanStart"/>), its
    /// hint, the mix's activation gate (<see cref="MixActivationEnabled"/>), and the
    /// config <see cref="StartCoreAsync"/> actually runs. Source-relative by
    /// construction, so a config applied against an earlier pick expires
    /// without anyone clearing anything: the generation bumps and the key stops
    /// matching. Nothing here can answer "has this folder ever been filtered" —
    /// that fact no longer exists in the model (the spec's §3).
    /// </summary>
    private FilterConfig? FilterInEffect => AppliedFilter.ConfigFor(CurrentFilterSource);

    /// <summary>
    /// Four gates, each with its own sibling hint in the markup: a filter in
    /// effect for this pick, a folder with problem files picked, a filtered
    /// pool not <i>known</i> to be empty, and an effective mix (see
    /// <see cref="EffectiveMix"/> — null exactly when "Mix applies" is checked
    /// over an invalid draft).
    ///
    /// <para>
    /// The pool gate is <b>known-zero only</b>, deliberately: it reads the
    /// advisory <see cref="_matchSummary"/> where it happens to be resolved
    /// with <c>Total: 0</c>, and a null or still-computing summary gates
    /// nothing — the gate takes no async dependency, and the existing
    /// no-match outcome notice in <see cref="StartCoreAsync"/> remains the
    /// backstop for a Start that races the count. The mix surface is
    /// deliberately <i>not</i> pool-gated (rows are dir-independent choices,
    /// and pool-gating activation would freeze a checked box when a re-apply
    /// empties the pool); the composed-to-zero outcome stays the backstop for
    /// a non-empty pool whose mix reaches nothing.
    /// </para>
    /// </summary>
    private bool CanStart =>
        FilterInEffect is not null
        && Folder.HasFiles
        && _matchSummary is not { Total: 0 }
        && EffectiveMix is not null;

    /// <summary>
    /// The "Mix applies" checkbox's <i>check</i> gate: whether a filter is in
    /// effect for this pick <i>now</i> (<see cref="FilterInEffect"/>). Ratified
    /// UX sequencing, not a data-flow requirement — the mix composes over the
    /// filtered pool at <i>Start</i>, so activating one first would be legal
    /// and harmless in the pipeline; what it isn't is legible, because the
    /// panel gives no hint that the mix draws from the filter's pool. Gating
    /// the gesture is what states the dependency direction.
    ///
    /// <para>
    /// <b>The same fact Start reads — the spec's Fork A, ruled strict.</b>
    /// Activation requires the filter in effect at this moment, so editing the
    /// filter darkens the <i>check</i> gesture until the filter is re-applied,
    /// exactly as it darkens Start. Accepted cost: mid-composition friction.
    /// What it buys is that no fact of the form "this folder was filtered at
    /// some point" survives anywhere in the model (§3). Nothing can <i>run</i>
    /// wrong in the window either way, since Start is dead while the filter is
    /// dirty. Gates checking only — an already-checked box stays operable
    /// (unchecking is the universal way out and is never taken away), and the
    /// bit itself is untouched: the app flips consent in neither direction.
    /// </para>
    ///
    /// <para>
    /// <b>Derived, and deliberately not coupled to the mix's lifetimes.</b>
    /// Nothing about <see cref="MixDraft"/> or <see cref="MixConsent"/> takes
    /// part: the gate is a property of the <i>filter</i> and the <i>pick</i>
    /// alone, read live per render. A new pick revokes it by construction —
    /// the generation bumps and <see cref="FilterInEffect"/> stops matching.
    /// And <i>Clear mix</i> stays ungated in every state: it is a way out,
    /// never a way in. Deliberately <b>not</b> pool-gated either (see
    /// <see cref="CanStart"/>).
    /// </para>
    /// </summary>
    private bool MixActivationEnabled => FilterInEffect is not null;

    /// <summary>
    /// The muted hint the mix panel shows while
    /// <see cref="MixActivationEnabled"/> is false and the box is unchecked —
    /// the host-owned sentence, mirroring
    /// <see cref="SavedFiltersDisabledReason"/>'s contract with the composite's
    /// saved-filters half. It states the <i>reason</i> for the ordering
    /// (the mix draws from the filtered pool), not merely the rule, because the
    /// rule alone is what the user found arbitrary.
    /// </summary>
    private string? MixActivationDisabledReason =>
        MixActivationEnabled
            ? null
            : "Apply the filters above first — the mix draws its problems from the "
              + "filtered pool, so the filters come first.";

    /// <summary>
    /// Whether a non-passthrough mix is in effect right now — checked <i>and</i>
    /// the on-screen mix builds to something with entries — the single fact two
    /// unrelated statements on this page derive from (the match count's caveat
    /// and the shuffle checkbox's disabled state, via
    /// <see cref="MixOwnsOrder"/>), so neither can be true while the other
    /// reads the mix differently. Live per keystroke now, since the effect
    /// follows the screen: unchecking, clearing the rows, or breaking the mix
    /// (null build) each read as "no mix in effect" the moment they happen.
    /// </summary>
    private bool MixInEffect => EffectiveMix is { IsPassthrough: false };

    /// <summary>
    /// True while a non-passthrough mix is in effect: presentation order
    /// belongs to the mix's own Random-order setting, so the standalone
    /// Shuffle checkbox is disabled — but its held value is deliberately left
    /// untouched, so turning the mix off restores the user's prior shuffle
    /// preference. A named <i>consequence</i> of <see cref="MixInEffect"/>,
    /// not a second copy of the predicate: the markup that disables the
    /// checkbox should say why it is disabled.
    /// </summary>
    private bool MixOwnsOrder => MixInEffect;

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
        // The start gate derives from the mix draft and the consent bit (see
        // EffectiveMix), and both are moved inside MixPanel — a child whose
        // gestures don't pass through this component. Subscribe so any change
        // re-renders the gate (the standard Blazor state-container pattern;
        // every mutation happens on the renderer's sync context, so the
        // handlers are safe to hand over directly). Unsubscribed in Dispose.
        MixDraft.Changed += StateHasChanged;
        MixConsent.Changed += HandleConsentChanged;

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

        // The mix predicate's other reading point (issue #87). A pick refreshes
        // it, but two things can move it with no pick in sight, and both land
        // here: a folder already held when this page is re-instantiated
        // (navigate-back), and — the one that matters — a quiz run since the
        // last probe, which may have written the very first stats record this
        // folder has. That is what makes "no mix until its first quiz creates
        // stats" resolve on the way back from that quiz rather than waiting for
        // a re-pick the user has no reason to make.
        await StatsStore.RefreshPickedStatsAsync();
    }

    /// <summary>
    /// The consent bit moved — the "Mix applies" gesture. Re-render like any
    /// draft change, and retire a standing weighted-start refusal: the toggle
    /// is the model's nearest analog of the commit that used to moot it (the
    /// user has re-decided what the mix should be doing, so a notice about the
    /// previous decision is stale). The notice's other clear points — a new
    /// pick, every Start attempt — are unchanged.
    /// </summary>
    private void HandleConsentChanged()
    {
        _mixRefused = false;
        StateHasChanged();
    }

    /// <summary>Detach from the app-scoped mix services — the page dies before the Scoped services do.</summary>
    public void Dispose()
    {
        MixDraft.Changed -= StateHasChanged;
        MixConsent.Changed -= HandleConsentChanged;
    }

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
                // stays up past the enumeration and the buffering, until the
                // summary this method's caller renders is on screen. (The
                // saved-filters read moved out of this stretch: the composite
                // runs it on its own mount, after this render.) Lowered in the
                // finally, which covers the cancelled path (no hook fired —
                // nothing was raised) too.
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
    /// Capability is always <see cref="FolderWriteCapability.BrowserUnsupported"/>
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
    ///
    /// <para>
    /// Async again — one await, and only on the path that keeps a folder: the
    /// mix predicate's pick-time probe
    /// (<see cref="QuizStatsStore.RefreshPickedStatsAsync"/>, issue #87). It
    /// runs <i>after</i> <see cref="PickedProblemFolder.Set"/> so it probes the
    /// generation it is about, and before this method returns so no render can
    /// fall between the two: the panel's mount decision is made once, against a
    /// resolved probe, rather than made twice with the section popping in. (The
    /// probe's generation stamp makes the ordering safe rather than merely
    /// tidy — a probe stamped with a superseded pick expires instead of
    /// answering for the wrong folder.) The two no-folder outcomes need no
    /// probe: they return with the holder clear, and a cleared holder fails the
    /// predicate's capability half outright.
    /// </para>
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
            // Already clear — EndCurrentSetupAsync ran at the click and nothing
            // since could have set it. Re-stated so this shared landing carries
            // its own postcondition ("an empty pick holds no folder") rather
            // than inheriting it from whoever called it.
            Folder.Clear();
            _emptyFolderNotice = true;
            return;
        }

        // The truncation report rides onto the holder with the files it describes,
        // so the notice that renders it lives exactly as long as the partial pick
        // it is about — including across navigate-back, which a page field would
        // not survive.
        //
        // The render this triggers also mounts a fresh FilterSurface behind the
        // HasFiles gate, and the composite loads this folder's saved-filters
        // document itself — a setup-time, degrade-tolerant read through the
        // picked-slot storage adapter (null under a fallback pick, so no
        // context). Nothing to await for that: saved-filters trouble can never
        // block a pick.
        Folder.Set(outcome.DirectoryName, outcome.Files, outcome.Capability, outcome.Truncations);

        // Now that the holder describes this pick, ask whether a mix can mean
        // anything for it. Degrade-tolerant end to end (see the store), so this
        // await can neither fail the pick nor surface a notice of its own.
        await StatsStore.RefreshPickedStatsAsync();
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
    /// picked slot, the mix consent bit and the mix draft (the
    /// <see cref="MixConsent"/> revoke and <see cref="MixDraft.Discard"/> —
    /// see the inline comment; the <i>stored</i> mix deliberately survives),
    /// the applied filter (see below), and every pick-scoped notice
    /// and match count (<see cref="ClearPickNotices"/>). The saved-filters
    /// context needs no line here: it lives in the hosted <c>FilterSurface</c>,
    /// which the <c>HasFiles</c> gate unmounts when <see cref="PickedProblemFolder.Clear"/>
    /// renders — its store, notices, and any typed state die with it, and a
    /// successful pick's fresh mount re-reads the new folder's document. Two
    /// things deliberately survive, and the class summary says why:
    /// <see cref="ShuffleOption"/> and the lifetime-stats slot.
    /// </para>
    ///
    /// <para>
    /// <b>The <see cref="AppliedFilter.Clear"/> is the one line of filter
    /// choreography left host-side, and it is now residue-dropping rather than
    /// gate-closing.</b> It used to be load-bearing: the applied config was
    /// readable absolutely, so a config applied against the outgoing corpus
    /// would have stayed in force across the gap the composite cannot cover
    /// (its source-change rule runs on a <i>mounted</i> component receiving a
    /// changed parameter, and this page's token changes all happen across an
    /// unmount — <see cref="PickedProblemFolder.Clear"/> closes the
    /// <c>HasFiles</c> gate before the composite could observe the new token,
    /// and the eventual re-mount's first parameters-set is, by the producer's
    /// ruled pin, initialization only). Source-keying closed that hazard
    /// structurally: <see cref="PickedProblemFolder.Clear"/> bumps the
    /// generation on the line above, so <see cref="FilterInEffect"/> is already
    /// null for every subsequent read whether or not this line runs. What the
    /// line still does is drop the pair itself, so no config outlives the setup
    /// that applied it even unreachably — which is exactly the end-of-setup
    /// call <see cref="AppliedFilter.Clear"/> documents as its purpose, made
    /// here because this is where this host's setups end.
    /// </para>
    ///
    /// <para>
    /// <b><see cref="AppliedFilter"/> is reset here too</b> — superseding an
    /// earlier ruling that <c>Clear</c> should leave it alone as "edit-coupled,
    /// not pick-coupled". Under one shared reset the applied filter is coupled to
    /// neither gesture in particular but to the <i>setup</i>: ending one ends it.
    /// (It stays edit-coupled as well — the composite clears it while the panel
    /// reports uncommitted edits. The two rules are independent, not
    /// duplicates.) On the <c>Clear</c> path this is invisible in the UI, since
    /// the panel and Start are both behind the disclosure gate a cleared folder
    /// closes; the value is that there is one reset to reason about.
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
        // The mix's consent dies with the setup; its rows do not (§4: your
        // choices outlive the setup, your consent does not). Revoke is
        // UNCONDITIONAL, which is what settles issue #87's third ruling with
        // no code: a mix in effect for the outgoing folder cannot survive
        // into one whose predicate is false, because consent survives into no
        // folder at all. Discard blanks the draft and forgets hydration —
        // deliberately without touching localStorage — so a mix-capable
        // pick's re-mounted panel re-hydrates the stored last-valid mix,
        // visible but inert until the user re-checks the box; a pick that
        // can't mean a mix mounts no panel, re-hydrates nothing, and the
        // revoked consent keeps the mix out of its Start with no capability
        // fork in the gate.
        MixConsent.Revoke();
        MixDraft.Discard();
        // Drop the applied pair outright — see the method summary for why this
        // is residue-dropping now rather than the gate close it once was. The
        // user's last-applied filter is untouched in the panel's own
        // localStorage (a later mount re-stages it, shown but never claimed as
        // applied), so this clears the session's claim, not the panel's
        // persistence.
        AppliedFilter.Clear();
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
        // A new/cleared pick changes the corpus, so any match summary is stale;
        // the bumped id also discards a count still in flight from before.
        _matchSummary = null;
        _countRequestId++;
    }

    /// <summary>
    /// The composite's re-raise of the panel's <i>Apply</i> (and <i>Clear
    /// filters</i>) commit — the gesture that moves the committed config. By the
    /// time this fires, <c>FilterSurface</c> has already recorded the applied
    /// state on the shared <see cref="AppliedFilter"/> holder, keyed to the
    /// pick's source token (the producer rule: <i>a commit applies the
    /// filter</i>, honored before the host hears about it) — so this handler
    /// owns only the host side effects the composite can't know: the outcome
    /// notices a new commit moots, and the match summary.
    /// </summary>
    private async Task HandleFilterConfigApplied(FilterConfig cfg)
    {
        _startError = null;
        _noMatchNotice = null;
        await ShowMatchSummaryAsync(cfg);
    }

    /// <summary>
    /// The composite's re-raise of the panel's applied-state report, raised
    /// after <em>every</em> gesture that touches its edit buffers (a control
    /// edit, a saved-filter load's staging, Apply, Clear filters). The payload
    /// is the committed <see cref="FilterConfig"/> the buffers now equal, or
    /// <c>null</c> when they equal none — so it is the whole answer to "is the
    /// panel's selection still the one the user applied?", which is the filter
    /// half of the start gate.
    ///
    /// <para>
    /// <b>The holder is no longer this handler's business.</b>
    /// <c>FilterSurface</c> mirrors the payload onto <see cref="AppliedFilter"/>
    /// (Set on a clean report, Clear on <c>null</c>) before re-raising, so by
    /// the time this runs the gate is already correct — a clean report has
    /// re-applied (an edit undone back to the applied values re-enables Start
    /// without a re-Apply, which it must: the panel's own Apply is disabled in
    /// exactly that state) and an uncommitted-edits report has re-gated. What
    /// remains host-side is the match summary, handled statelessly per the
    /// producer's per-gesture contract: react to the payload, never diff it
    /// against a remembered previous one.
    /// </para>
    ///
    /// <para>
    /// The <see cref="PickedProblemFolder.HasFiles"/> guard is defence in
    /// depth, not a live path: the composite lives behind the
    /// progressive-disclosure gate, so every report originates with a folder
    /// held. Should a report ever straggle past a teardown (the state
    /// <see cref="EndCurrentSetupAsync"/> leaves), counting matches against a
    /// corpus being torn down is the wrong side effect, and the setup-end's
    /// explicit <see cref="AppliedFilter.Clear"/> must stay the last word.
    /// </para>
    /// </summary>
    private async Task HandleAppliedStateChanged(FilterConfig? config)
    {
        if (!Folder.HasFiles) return;

        if (config is null)
        {
            // Uncommitted edits pending: the composite already cleared the
            // holder (Start is re-gated). Any shown or in-flight match summary
            // described the config now abandoned; the bumped id discards a
            // late-landing result.
            _matchSummary = null;
            _countRequestId++;
            return;
        }

        // Idempotence: the report is per-gesture, and a commit raises it right
        // after HandleFilterConfigApplied has already counted the same config —
        // so re-running the summary here would count the same pool twice (two
        // parses, two busy flashes). The payload always equals the holder's
        // config here (the composite assigned it from this very report), so
        // currency is what the summary state answers: a summary is shown, or a
        // count for it is in flight.
        if (_matchSummary is not null || _isCounting) return;

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
    /// Dismiss the truncated-pick report for the pick on screen, keyed on the
    /// holder's occurrence token (issue #107): navigating away and back finds
    /// the same token and stays dismissed, while the next pick mints a fresh
    /// one and reports its own truncations — with no reset call site to forget.
    /// </summary>
    private void DismissTruncations() =>
        Notices.Dismiss(QuizNotice.PickTruncations, Folder.PickOccurrence);

    /// <summary>
    /// Dismiss the stats-capability notice for the pick on screen — whichever
    /// of the three mutually exclusive branches is showing (they share the
    /// slot; see <see cref="QuizNotice.PickStatsCapability"/>). Same token
    /// discipline as <see cref="DismissTruncations"/>.
    /// </summary>
    private void DismissStatsCapability() =>
        Notices.Dismiss(QuizNotice.PickStatsCapability, Folder.PickOccurrence);

    /// <summary>
    /// Dismiss the cancelled-pick notice by clearing its own per-visit field —
    /// deliberately not routed through <see cref="QuizNoticeDismissal"/>: the
    /// notice describes a gesture that left nothing behind, dies with the
    /// visit by construction, and <see cref="ClearPickNotices"/> already
    /// retires it on the next gesture, so an occurrence token would have
    /// nothing to outlive. The click affordance is what issue #107 adds; the
    /// lifetime was already right.
    /// </summary>
    private void DismissCancelledPick() => _cancelledPickNotice = false;

    /// <summary>
    /// Dismiss the empty-folder notice — <see cref="DismissCancelledPick"/>'s
    /// sibling, for the same reasons.
    /// </summary>
    private void DismissEmptyFolder() => _emptyFolderNotice = false;

    private void HandleShuffleToggled(ChangeEventArgs e)
    {
        // A checkbox has no half-edited state, so the toggle is recorded live —
        // no applied/dirty gate the way AppliedFilter needs one.
        ShuffleOption.Set(e.Value is true);
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
        if (FilterInEffect is not { } cfg) return;
        // The effective mix is the on-screen draft's build when "Mix applies"
        // is checked, the passthrough otherwise (see EffectiveMix). Null means
        // checked-and-invalid — CanStart is dark and its hint says why, so
        // this early return is the backstop for programmatic dispatch only,
        // same as the filter guard above.
        if (EffectiveMix is not { } mix) return;
        _startError = null;
        _noMatchNotice = null;
        _mixRefused = false;
        try
        {
            var outcome = await Controller.StartAsync(cfg, mix, ignoreMix);

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

    /// <summary>
    /// Return to the problem a live quiz is sitting on (issue #58) — the same
    /// one-line handler behind the identical affordance on <see cref="Help"/>
    /// and <see cref="Settings"/>, rendered under the same
    /// <c>HasStarted &amp;&amp; !IsFinished</c> predicate.
    ///
    /// <para>
    /// Navigation only: the quiz state it returns to is app-scoped and was never
    /// at risk from the visit — the picked files are read at Start, so nothing on
    /// this page's setup surface reaches a running quiz (<c>EndCurrentSetupAsync</c>
    /// touches only the JS <i>picked</i> slot, pinned). There is deliberately no
    /// guard, no confirmation and no warning around the round trip; what was
    /// missing was only the way back.
    /// </para>
    /// </summary>
    private void BackToQuiz()
    {
        Nav.NavigateTo("/quiz");
    }
}
