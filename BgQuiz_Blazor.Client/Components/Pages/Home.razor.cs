using BgDataTypes_Lib;
using BgFolderAccess_Razor;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
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
/// the filters, saved filters, weighted mix, the Order, and the Start button
/// are gated behind <see cref="PickedProblemFolder.HasFiles"/> in the markup.
/// Pre-pick there is nothing to filter, weight, or start, so hiding them keeps
/// the required first step — picking a folder — unmistakable, and makes the
/// filter-half of the start gate true by construction (no panel to apply). The
/// weighted mix carries a <i>further</i> gate, and it is the one derivation
/// rather than a second reading of anything: it renders only while
/// <see cref="MixVisibility.IsVisible"/> — the user's standing
/// <see cref="QuizSettings.WeightQuizzesByStats"/> setting is on <i>and</i> this
/// folder already holds a stats record with something in it
/// (<c>SPEC-filtering.md</c> §5, "Visible means in effect", ruled 2026-09-07;
/// issue <c>halheinrich/backgammon#181</c>). That same fact is what
/// <see cref="EffectiveMix"/> reads, so the panel being on screen and the rows
/// being in effect are one fact: a visible mix always applies and a hidden one
/// never does. Where it is false the mix plays no part in Start — the panel is
/// hidden and the effective mix is the passthrough — so Start runs plain with no
/// mix gate, warning, or refusal. Nothing is shown disabled and no reason is
/// offered: the non-mount <i>is</i> the answer, and the accepted consequence is
/// that a brand-new folder offers no mix until its own first quiz creates the
/// stats a mix would weight by.
/// </para>
///
/// <para>
/// That predicate reads a probe of the picked folder's stats document, read at
/// two points (<see cref="QuizStatsStore.RefreshPickedStatsAsync"/>): every
/// sample's landing (<see cref="FolderOperations"/>), and this page's own
/// initialization — which is what lets the answer change after the quiz that
/// created the record, since returning here re-instantiates the page. The
/// saved-filters persist gate is deliberately <b>not</b> routed through it and
/// stays capability-only: saved filters have nothing to do with a stats record,
/// and a folder with none must still be able to save them.
/// </para>
///
/// <para>
/// That same probe carries the pick band's second stats fact: whether the
/// document it read is one a quiz would retire
/// (<see cref="QuizStatsStore.ForecastStatsSetAsideName"/>, issue
/// <c>halheinrich/backgammon#146</c>). The notice it feeds is a <i>forecast</i>
/// — the set-aside itself stays at the quiz bind, where write permission is
/// settled and where the Quiz page reports it — so this page states a
/// consequence before the action it belongs to, and mutates nothing.
/// </para>
///
/// <para>
/// Two more forecasts come out of the same probe (issues
/// <c>halheinrich/backgammon#260</c>, <c>halheinrich/backgammon#261</c>): the
/// stats file exists and can't be read
/// (<see cref="QuizStatsStore.ForecastStatsUnreadable"/>), or can't be written
/// (<see cref="QuizStatsStore.ForecastStatsUnwritable"/>). Either means the
/// next quiz records nothing, so either replaces the stats-location promise
/// (<see cref="StatsWillNotRecord"/>) rather than standing beside it.
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
/// Start is gated on five conditions (<see cref="CanStart"/>): a filter in
/// effect for the pick on screen (<see cref="FilterInEffect"/> —
/// <see cref="FilterSetup"/>'s reading: the selection applied and not since
/// edited, or the ready empty selection, which needs no Apply;
/// halheinrich/backgammon#266), a folder picked with at least one problem
/// file, a pick whose parse did not reject every file
/// (<see cref="PickedProblemFolder.Parsed"/>'s report not <c>AllRejected</c>
/// — the holder's fact, standing until the pick changes;
/// halheinrich/backgammon#368), a
/// filtered pool not <i>known</i> to be empty (<see cref="CurrentMatchSummary"/>
/// with <c>Total: 0</c> — known-zero only, so a null or still-computing
/// summary never gates and the no-match outcome notice stays the backstop
/// for races), and an effective mix (<see cref="EffectiveMix"/> non-null —
/// null means the panel is visible over an invalid draft, the one mix state
/// that gates; a hidden mix never does, per the spec's §5). Each
/// gate has its own sibling hint stating its reason. Everything the gate
/// reads lives in per-app scoped services (<see cref="FilterSetup"/>,
/// <see cref="MatchCount"/>, <see cref="PickedProblemFolder"/>,
/// <see cref="MixVisibility"/>, <see cref="MixDraft"/>) rather than transient
/// component fields, so the gate survives in-app navigation — when the page
/// is re-instantiated on navigate-back it re-derives from the holders instead
/// of resetting. On Start the in-effect <see cref="FilterConfig"/> and the effective
/// <see cref="BgGame_Lib.QuizMix"/> — the on-screen draft's build while the
/// panel is visible, the passthrough otherwise — are handed to the
/// <see cref="QuizController"/>, whose source factory builds a
/// <see cref="WasmUploadedProblemSetSource"/> over the picked files, and the
/// app navigates to <c>/quiz</c>. A pick's or a resample's failure to acquire —
/// any exception from the folder seam but its caller error
/// (<see cref="FolderOperations"/>) — and <see cref="FilterConfig.Build"/> /
/// source-construction failures are surfaced as banners rather than faulting
/// the WebAssembly app. The two
/// non-failure pick outcomes — a pick that ended holding no folder, and one that
/// held a folder with no problem files — each get their own polite notice rather
/// than silence, so no gesture ever returns the user to an unchanged page with
/// no account of what happened. Every box on this page renders through the
/// shared <c>Notice</c>, and which of them dismiss is the umbrella's
/// <c>SPEC-notices.md</c> classification, not colour. Two of them read the
/// pick's parse straight off the holder and never dismiss
/// (halheinrich/backgammon#368): the all-rejected box, a gate reason that
/// replaces the zero count when no picked file could be read, and the
/// rejected-files record beside the count line, the account of what the
/// selection lost when some could not — both retired only by the pick that
/// made them, since nothing after the parse walks the files again. The owner
/// of each dismissible occurrence holds its dismissal (§2): the holder-backed
/// pick-band notices —
/// truncations, stats capability and the forecasts, which survive
/// navigation with the sample they describe — bind to the app-scoped
/// <see cref="QuizNoticeDismissal"/> keyed on
/// <see cref="PickedProblemFolder.SampleIdentity"/>, and the browser-storage
/// notice, bound to the same holder keyed on
/// <see cref="BrowserStorageCondition.Occurrence"/> (issue
/// <c>halheinrich/backgammon#360</c>: one fact, fed by the hosted filter
/// panel's report and this app's own stores, lasting the visit); the per-visit pair
/// (<see cref="_cancelledPickNotice"/>, <see cref="_emptyFolderNotice"/>) and
/// the two errors (<see cref="_pickError"/>, <see cref="_startError"/>) bind to
/// their own fields, which are each box's whole state; and the reload-reset
/// notice lets the component hold the bit, its occurrence dying with this page
/// instance (<see cref="_showReloadNotice"/>). The weighted-start refusal and
/// the no-match notice are gate reasons and do not dismiss. A
/// weighted start with no lifetime stats is <i>refused</i> as an outcome (the
/// actionable notice with its per-run "Start without mix" override — see
/// <see cref="StartCoreAsync"/>), never silently run unweighted.
/// </para>
///
/// <para>
/// <b>A pick ends the current setup — at the click.</b> Choosing a folder
/// returns the whole setup surface to its pre-setup state (the reset
/// <see cref="FolderOperations"/> begins a pick with, and <c>Clear</c> is,
/// since the two encode the same decision): folder and picked slot, the mix
/// draft, the filter setup (by reporting the source's end to
/// <see cref="FilterSetup"/>), and every pick-scoped notice
/// (<see cref="ClearPickNotices"/>, this page's half). The mix
/// setting is deliberately untouched — it is a choice, and choices outlive
/// setups (§4).
/// Nothing the user selected against the previous corpus can be assumed to mean
/// the same thing against the next one, so Start is always re-gated by a pick,
/// never inherited across one. Two things deliberately survive: the Order
/// (<see cref="QuizSettings.Order"/>), a remembered choice like the ranking,
/// and the lifetime-stats slot, whose whole point is to <i>resume</i> when its
/// folder is picked again.
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
/// <b>Resample keeps the setup and replaces the sample</b>
/// (<c>SPEC-filtering.md</c> §4, "The sample, the limits and Resample";
/// <see cref="ResampleAsync"/>): another draw of the held folder, offered
/// beside it while another draw could hold different files. The filter in
/// effect, the mix and the saved-filters document stand, because they follow
/// the setup's identity (<see cref="PickedProblemFolder.SetupIdentity"/>);
/// the parse, the count, the pick-time notices and the stats probe are fresh,
/// because they follow the sample's.
/// </para>
///
/// <para>
/// <b>A new sample ends the run in progress — at the landing</b>
/// (<c>SPEC-quiz-history.md</c> §9; <see cref="FolderOperations"/>), a pick's
/// and a resample's alike: once a non-empty sample is acquired, the run is
/// finished as End quiz finishes it and its heavy references released, then
/// the sample is installed, and only then can a count or a Start parse it.
/// Nothing that replaces no sample ends anything: a cancelled picker, an
/// empty folder, Clear, a failed pick or resample, an empty resample. The
/// setup ends at the click; the run ends only when files arrive. Afterwards
/// the page says the quiz was finished and links to its summary on Done.
/// </para>
///
/// <para>
/// <b>A folder operation is the app's, not this page's</b>
/// (<see cref="FolderOperations"/>; halheinrich/backgammon#403). A pick or a
/// resample can outlive the page it began on, so its exclusion, its busy
/// cursor and its landing live in that app-scoped owner, and this page only
/// asks: it refuses a pick, a Clear, a resample or a Start at the handler
/// while an operation is in flight — whichever page began it — and disables
/// those controls too; it shows the operation's progress in its busy state;
/// and it reports what an operation it asked for came to on its per-visit
/// notices (<see cref="Report"/>). A remounted page therefore shows a pick or a
/// resample still running, and the sample it lands, as its own.
/// </para>
///
/// <para>
/// <b>The filter half of that reset is one report.</b> The selection's state
/// lives in the app-scoped <see cref="FilterSetup"/>, not in the
/// <c>FilterSurface</c> this page hosts, so it does not care that the surface
/// sits behind the same <c>HasFiles</c> gate as the rest of the setup surface
/// and unmounts with it. This page reports its source wherever it latches one
/// (<see cref="ReportFilterSource"/>, after every change to the folder
/// holder): a different source ends the filter setup in the owner — a new
/// generation, the applied baseline dropped, the draft kept — whether or not
/// the surface is mounted, and the same source again (a remount, a
/// navigate-back) changes nothing. There is no other filter choreography
/// here: no clear of an applied filter, no mount gate, no copy-back of a
/// restored selection. The gate and the filter to run are read off the
/// owner's snapshot, source-relatively (<see cref="FilterInEffect"/>).
/// </para>
///
/// <para>
/// <b>The match count is keyed, and outlives the page.</b> What the filter in
/// effect matches is held by the app-scoped <see cref="MatchCount"/> under the
/// pick, the filter in effect and the ranking
/// (<see cref="MatchCountInputs"/>). This page asks for the count of its
/// current inputs on every mount and every filter snapshot
/// (<see cref="SyncMatchCountAsync"/>); equal inputs reuse the count — so a
/// navigate-back shows it, and the known-zero gate it feeds, at once — and
/// different ones recount, superseding any count still running. It renders
/// only a count of its current inputs (<see cref="CurrentMatchSummary"/>).
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
/// <b>The Order</b> sits beside Start, ungated and always available
/// (<c>SPEC-filtering.md</c> §7): one control with the producer's four orders,
/// a remembered choice in <see cref="QuizSettings.Order"/>. It is presentation
/// only — order, not admission — so it plays no part in <c>CanStart</c>, and
/// nothing on this page derives from it: the count never sorts. Start hands
/// it to the controller with the ranking, which applies it where the run's
/// source is built; a mix that applies never disables it, because the mix
/// selects and the order presents (§5).
/// </para>
///
/// <para>
/// Below the setup surface — outside the busy <c>fieldset</c>, since it only
/// navigates — sits a conditional <b>"Back to quiz"</b> button, on the
/// <c>HasStarted &amp;&amp; !IsFinished</c> predicate (issue halheinrich/backgammon#58). Home is the
/// third page a user can reach mid-quiz and the last one that had no way back.
/// The visit itself is safe — see <see cref="BackToQuiz"/>; only a new sample
/// landing ends the quiz, and then there is nothing to go back to — so the
/// affordance is the whole change. It is deliberately <i>not</i> the
/// <see cref="ReturnControl"/> <see cref="Help"/>, <see cref="Settings"/> and
/// <see cref="Stats"/> render (issue halheinrich/backgammon#241): that control
/// falls back to "Back to Home" with no quiz live, and this page is Home.
/// </para>
/// </summary>
public partial class Home : ComponentBase, IDisposable
{
    private string? _startError;

    /// <summary>
    /// Sibling of <see cref="_startError"/> for a pick's or a resample's failure
    /// to acquire — a browser failure, a file past the
    /// <see cref="PickedFileLimits.MaxFileBytes"/> cap, a stream that ends
    /// mid-copy, a broken reply (<see cref="FolderOperations"/>). A per-visit failure notice
    /// (assertive), like the start error, and
    /// the notice's whole state: dismissing it clears this field, and the next
    /// failure assigns it again and shows fresh.
    /// A folder past the <i>count</i> caps is not a failure: it truncates and
    /// reports (<see cref="PickedProblemFolder.Sample"/>'s per-kind report).
    /// </summary>
    private string? _pickError;

    /// <summary>
    /// Set when a completed pick yielded no top-level <c>.xg</c> / <c>.xgp</c>
    /// files — an outcome (polite notice), not a failure: the holder stays
    /// clear and the Start gate stays disabled. Per-visit, so a component field.
    /// </summary>
    private bool _emptyFolderNotice;

    /// <summary>
    /// Set when a resample drew no problem files — the folder's files were
    /// moved or deleted since the pick. An outcome (polite notice), not a
    /// failure: an empty resample replaces nothing and ends no run
    /// (<c>SPEC-filtering.md</c> §4), so the held sample stays in use and the
    /// notice says so. Per-visit, and the notice's whole state, like
    /// <see cref="_emptyFolderNotice"/>.
    /// </summary>
    private bool _emptyResampleNotice;

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
    /// (issue halheinrich/backgammon#116, observed live on a WebView-wrapping browser) — and only the
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
    /// <para>
    /// Per-visit outcome state, so a component field — and the notice's whole
    /// state, dismissal included: the notice binds its dismissed state to this
    /// field, so dismissing clears it, and it is deliberately not routed
    /// through <see cref="QuizNoticeDismissal"/>. The notice describes a gesture
    /// that left nothing behind and dies with the visit by construction, and
    /// <see cref="ClearPickNotices"/> already retires it on the next gesture, so
    /// an occurrence token would have nothing to outlive.
    /// <see cref="_emptyFolderNotice"/> is held the same way, for the same
    /// reasons.
    /// </para>
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
    ///
    /// <para>
    /// The notice's occurrence lives exactly as long as this page instance: the
    /// marker is cleared in the same step that sets this, so a navigate-back
    /// re-instantiates the page with the flag false. The notice's own component
    /// therefore holds its dismissal — nothing here records one, and no
    /// app-scoped owner exists or is wanted, since one would outlive the
    /// one-shot notice it describes (<c>SPEC-notices.md</c> §2).
    /// </para>
    /// </summary>
    private bool _showReloadNotice;

    /// <summary>
    /// Set when a weighted Start was refused — the effective mix has entries
    /// but no lifetime stats are available. Drives the actionable refusal
    /// notice with its one-click per-run "Start without mix" override.
    /// Genuinely per-visit outcome state, so a component field like the
    /// banners above; cleared on a new pick (capability may change) and on
    /// every Start attempt. The third clear point went with the consent bit
    /// and needed no replacement: the setting that turns the mix off now lives
    /// on another page, so re-deciding means leaving this one, and a fresh
    /// mount clears this field for free.
    /// </summary>
    private bool _mixRefused;

    /// <summary>
    /// This page's attachment to <see cref="FilterSetup"/>, made in
    /// <see cref="OnInitializedAsync"/>: every published change re-renders the
    /// page and re-asks for the match count (<see cref="OnFilterSetupPublished"/>).
    /// The page keeps no copy of what it is told — the gate, the filter to run
    /// and the count's inputs are read off the owner's
    /// <see cref="FilterSetup.Current"/> snapshot at the moment they are
    /// needed. Disposed in <see cref="Dispose"/>, which stops the snapshots and
    /// cancels nothing.
    /// </summary>
    private IDisposable? _filterAttachment;

    /// <summary>
    /// The fallback picker this page's click opened, awaiting the selection the
    /// hidden input will report (<see cref="HandleFallbackPickedAsync"/>), or
    /// null. The owner collects only for the intent still current, so one kept
    /// here past a newer operation is merely stale: it collects nothing.
    /// </summary>
    private FallbackPickIntent? _fallbackIntent;

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
    private IDocumentStorage? Storage =>
        Folder.Capability is FolderWriteCapability.Enabled or FolderWriteCapability.PermissionDenied
            ? DocumentStorage
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
    /// diverge (<c>SPEC-filtering.md</c> §5: there is no committed copy).
    ///
    /// <para>
    /// <b>Visible means in effect</b> (§5, ruled 2026-09-07). The condition is
    /// <see cref="MixVisibility.IsVisible"/> — the same fact the markup renders
    /// the panel from, read here rather than restated — so "the panel is on
    /// screen" and "the rows on screen are what Start composes with" are one
    /// fact and cannot disagree. Hidden, the mix is simply not in effect: the
    /// passthrough runs and nothing about the draft, however divergent from
    /// whatever ran last, gates anything. Visible, the effect is the on-screen
    /// draft's build: <see cref="QuizMix.Empty"/> for the blank draft (blank is
    /// the blank mix in effect — passthrough, ruled, not an error), and
    /// <see langword="null"/> exactly when the draft fails to validate — the
    /// one mix state that gates Start, with the panel left standing because
    /// only the user fixes it or turns the setting off.
    /// </para>
    /// </summary>
    private QuizMix? EffectiveMix =>
        MixVisibility.IsVisible ? MixDraft.Build() : QuizMix.Empty;

    /// <summary>
    /// This page's identity for the corpus a filter can be applied against —
    /// the setup held, as its pick's identity spells it
    /// (<see cref="SetupIdentity.FilterSource"/>) — or <see langword="null"/>
    /// while no folder with problem files is held. The setup's, not the
    /// sample's: a resample keeps the filter in effect, and every explicit
    /// pick, even of the same folder, ends it (<c>SPEC-filtering.md</c> §4).
    /// What this page reports to <see cref="FilterSetup"/>
    /// (<see cref="ReportFilterSource"/>), what every gate asks the owner's
    /// snapshot about (<see cref="FilterInEffect"/>) and the selection the
    /// match count is keyed by (<see cref="CurrentCountInputs"/>) all read this
    /// one property, so the report and the reads cannot encode the setup
    /// differently. <see langword="null"/> means "no source exists", which is
    /// the only thing the owner may be told it means — never "not yet known":
    /// the holder's state is read synchronously and is always known.
    /// </summary>
    private FilterSourceToken? FilterSource => Folder.SetupIdentity?.FilterSource;

    /// <summary>
    /// The filter in effect for the pick on screen right now, or
    /// <see langword="null"/> when none is — the single fact this page's whole
    /// filter story reads: Start's filter gate (<see cref="CanStart"/>), its
    /// hint, and the config <see cref="StartCoreAsync"/> actually runs.
    ///
    /// <para>
    /// <b>The owner's answer, whole.</b> <see cref="FilterSetupSnapshot.ConfigInEffectFor"/>
    /// is the selection applied for this pick and not since edited, or the
    /// ready empty selection, which needs no Apply (halheinrich/backgammon#266);
    /// this page adds no empty-filter exception of its own and no "Apply
    /// required" rule. Source-relative by construction, so a config applied
    /// against an earlier pick expires without anyone clearing anything: the
    /// setup's identity changes, the owner is told, and the key stops matching. A new
    /// instance on every read, so nothing this page does with it reaches the
    /// owner.
    /// </para>
    /// </summary>
    private FilterConfig? FilterInEffect =>
        FilterSource is { } source ? FilterSetup.Current.ConfigInEffectFor(source) : null;

    /// <summary>
    /// What the count holder knows of what is on screen — the setup, the
    /// sample, the filter in effect and the ranking: being counted, counted, or nothing
    /// (<see cref="MatchCountReading"/>; nothing too when no filter is in
    /// effect). The one read of the count this page makes: the counting line
    /// and the first-parse busy decision take its activity
    /// (<see cref="IsParsingThePick"/>), everything else its result
    /// (<see cref="CurrentMatchSummary"/>). A request for other inputs — left
    /// running by a Clear, a re-pick, a resample or an edit out of effect — is invisible
    /// here, so it can neither show a result nor make the page busy
    /// (halheinrich/backgammon#374).
    /// </summary>
    private MatchCountReading CurrentCount => MatchCount.ReadingFor(CurrentCountInputs(Settings.Ranking));

    /// <summary>
    /// The settled count of what is on screen (<see cref="CurrentCount"/>), or
    /// <see langword="null"/> when nothing is in effect, the count is still
    /// running, or it failed. The one result the count line, the zero box, the
    /// known-zero gate and the no-match fallback all take, so none of them can
    /// show or gate on a count of other inputs.
    ///
    /// <para>
    /// <b>One value carries every half of the display.</b> The count line
    /// renders <see cref="AnswerTypeDistribution.Total"/> off
    /// <see cref="MatchSummary.AnswerTypes"/>, the breakdown renders that same
    /// distribution's five buckets, and the collapse sentence renders
    /// <see cref="MatchSummary.DuplicatesCollapsed"/> — one fold of one
    /// enumeration, so the number, its decomposition and what it left out
    /// cannot disagree. The pre-mix pool, "decisions that match", not
    /// "problems you'll see" (positions offering no play choice auto-skip at
    /// quiz time).
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
    private MatchSummary? CurrentMatchSummary => CurrentCount.Summary;

    /// <summary>
    /// The match count's inputs for what is on screen under
    /// <paramref name="ranking"/>, or <see langword="null"/> when no filter is
    /// in effect for the setup — the key <see cref="MatchCount"/> holds its
    /// result under. Read off the owner's current snapshot, which the inputs
    /// keep, so the key is a stable reading and never a copy of the selection;
    /// and keyed by the sample held (<see cref="PickedProblemFolder.SampleIdentity"/>)
    /// as well as the setup, so a resample recounts while the filter in effect
    /// stands.
    /// </summary>
    private MatchCountInputs? CurrentCountInputs(PlayRanking ranking) =>
        FilterSource is { } source
            ? MatchCountInputs.For(FilterSetup.Current, source, Folder.SampleIdentity, ranking)
            : null;

    /// <summary>
    /// Five gates, each with its own sibling hint in the markup: a filter in
    /// effect for this pick, a folder with problem files picked, a pick whose
    /// parse did not reject every file, a filtered pool not <i>known</i> to be
    /// empty, and an effective mix (see <see cref="EffectiveMix"/> — null
    /// exactly when the mix panel is visible over an invalid draft).
    ///
    /// <para>
    /// <b>The all-rejected gate reads the holder, not the count</b>
    /// (halheinrich/backgammon#368). A pick whose every file the parse refused
    /// has nothing to quiz on whatever the filters say, and that is a property
    /// of the selection — standing through a filter edit, a recount and a
    /// count that fails — so the gate reads
    /// <see cref="PickedProblemFolder.Parsed"/>'s completed report, which
    /// only a re-pick or Clear retires, and the page's all-rejected box reads
    /// the same member: the gate and the one thing saying why it is dark
    /// cannot come apart. The producer draws <c>AllRejected</c> only from a
    /// completed walk over at least one file, so the gate is never vacuous: an
    /// unparsed pick, an empty one and an interrupted parse all leave it open.
    /// The policy, accepted as such at the 2026-10-07 review: a successfully
    /// completed parse is sufficient for the gate and its box, with no
    /// successful count required — including when counting then fails, or
    /// when Start performs the first parse (see <see cref="StartCoreAsync"/>,
    /// which then lets the box explain the finished-at-once outcome).
    /// </para>
    ///
    /// <para>
    /// The pool gate is <b>known-zero only</b>, deliberately: it reads the
    /// advisory <see cref="CurrentMatchSummary"/> where it happens to be
    /// resolved with <c>Total: 0</c>, and a null or still-computing summary
    /// gates nothing — the gate takes no async dependency. A count still
    /// running cannot be raced (the page's busy state disables the whole setup
    /// fieldset while it runs), so a null summary here means the count failed
    /// or nothing is in effect; the no-match outcome notice in
    /// <see cref="StartCoreAsync"/> is the backstop for that Start, and says
    /// only that nothing could be presented, since the page does not know why.
    /// Because the count is keyed and outlives this page, a navigate-back over
    /// unchanged inputs reads the same zero and the gate stays closed. The mix
    /// surface is deliberately <i>not</i> pool-gated (rows are dir-independent
    /// choices, and pool-gating activation would freeze a checked box when a
    /// re-apply empties the pool); the composed-to-zero outcome stays the
    /// backstop for a non-empty pool whose mix reaches nothing.
    /// </para>
    /// </summary>
    private bool CanStart =>
        FilterInEffect is not null
        && Folder.HasFiles
        && Folder.Parsed is not { Report.AllRejected: true }
        && CurrentMatchSummary is not { AnswerTypes.Total: 0 }
        && EffectiveMix is not null;

    /// <summary>
    /// Whether either pick-time stats forecast is in force — the file exists
    /// and can't be read (<see cref="QuizStatsStore.ForecastStatsUnreadable"/>)
    /// or can't be written (<see cref="QuizStatsStore.ForecastStatsUnwritable"/>)
    /// — so the next quiz will record nothing. Derived from the same two store
    /// properties the forecast notices render from, never held beside them, so
    /// the stats-location promise it suppresses cannot disagree with them.
    /// </summary>
    private bool StatsWillNotRecord =>
        StatsStore.ForecastStatsUnreadable || StatsStore.ForecastStatsUnwritable;

    /// <summary>
    /// Whether a non-passthrough mix is in effect right now — the panel visible
    /// <i>and</i> the on-screen mix building to something with entries — the
    /// fact the match count's caveat derives from. Live per keystroke, since
    /// the effect follows the screen: turning the setting off, clearing the
    /// rows, or breaking the mix (null build) each read as "no mix in effect"
    /// the moment they happen.
    /// </summary>
    private bool MixInEffect => EffectiveMix is { IsPassthrough: false };

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
        // The start gate derives from the mix draft (see EffectiveMix), which
        // is moved inside MixPanel — a child whose gestures don't pass through
        // this component. Subscribe so any change re-renders the gate (the
        // standard Blazor state-container pattern; every mutation happens on
        // the renderer's sync context, so the handler is safe to hand over
        // directly). Unsubscribed in Dispose.
        //
        // Visibility's other half — the setting — needs no subscription: it
        // moves only on the Settings page, which this page cannot be mounted
        // beside, so a change is always followed by a fresh mount here.
        MixDraft.Changed += StateHasChanged;

        // The storage notice reads the app's one storage fact, which a store
        // can begin after this page has rendered — the mix panel's hydration
        // runs from the child's init — so the page re-renders when it does
        // (halheinrich/backgammon#360). Unsubscribed in Dispose.
        StorageCondition.Began += StateHasChanged;

        // The match count outlives this page and settles on its own schedule,
        // so the page re-renders when it starts or settles. Unsubscribed in
        // Dispose.
        MatchCount.Changed += StateHasChanged;

        // A folder operation is the app's, and can begin, show progress, land
        // or end while this page is mounted whichever page began it: re-render,
        // and ask for the count a landing re-keyed. Unsubscribed in Dispose.
        Operations.Changed += OnOperationsChanged;

        // Tell the filter setup's owner which source this page holds, as the
        // host contract asks at page initialization. An unchanged source — a
        // navigate-back over the same pick — costs nothing and keeps the
        // setup; the owner cannot otherwise know a source the page latched
        // while it was not observing.
        ReportFilterSource();

        // Observe the setup: the owner delivers the current snapshot before
        // Attach returns, which is what asks for this mount's match count, and
        // then each real change. Disposed in Dispose.
        _filterAttachment = FilterSetup.Attach(_ => OnFilterSetupPublished());

        // Hydrate the user's settings here, where every quiz begins. Nothing on
        // this page renders them, but the Quiz page's board does, on its very
        // first render — and it gets there only through Start, long after this
        // read has landed. Kicking off from the entry point rather than from the
        // consumer is what keeps the board free of a hydration render gate; the
        // call is idempotent, so Quiz awaiting the same task costs nothing. This
        // page reads one setting itself, the ranking it hands the count and the
        // Start, and awaits the same task where it does (UserRankingAsync).
        await Settings.EnsureHydratedAsync();

        if (await Marker.WasLiveAsync() && !Controller.HasStarted)
        {
            _showReloadNotice = true;
            await Marker.ClearAsync();
        }

        _fsAccessAvailable = await FolderAccess.SupportsDirectoryPickerAsync();

        // The mix predicate's other reading point (issue halheinrich/backgammon#87). A pick refreshes
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
    /// Detach from the app-scoped holders this page observes — the page dies
    /// before the Scoped services do. Detaching cancels nothing: a match count
    /// still running settles into its holder, and a filter write still pending
    /// reports any refusal to the app's sink.
    ///
    /// <para>
    /// No consent-bit handler any more: its side job — retiring a standing
    /// weighted-start refusal when the user re-decided what the mix should be
    /// doing — needs no replacement, because the setting that replaced it can
    /// only be changed from another page, and <see cref="_mixRefused"/> is a
    /// component field that a fresh mount clears for free. The notice's other
    /// clear points — a new pick, every Start attempt — are unchanged.
    /// </para>
    /// </summary>
    public void Dispose()
    {
        MixDraft.Changed -= StateHasChanged;
        StorageCondition.Began -= StateHasChanged;
        MatchCount.Changed -= StateHasChanged;
        Operations.Changed -= OnOperationsChanged;
        _filterAttachment?.Dispose();
    }

    /// <summary>
    /// The page's one busy predicate, driving <i>both</i> halves of the busy
    /// affordance — the <c>app-busy</c> progress cursor and the whole-surface
    /// <c>&lt;fieldset disabled&gt;</c> — from a single expression, so the cursor
    /// and the disabled controls can never disagree about whether the page is
    /// working. A union of the independent sources: the controller's transition
    /// gate (Start / Restart), a folder operation's own work — the app's, not
    /// this page's, so a remounted page shows it too
    /// (<see cref="FolderOperations.ShowsProgress"/>) — and a match count that
    /// is parsing the pick (<see cref="IsParsingThePick"/>). A folder
    /// operation's exclusion is a separate fact
    /// (<see cref="FolderOperations.IsInFlight"/>): it holds through a pick's
    /// prompts, where no progress shows, and it disables only the controls
    /// that would begin an operation or a run.
    ///
    /// <para>
    /// <b>Not every count.</b> The count is no longer a gesture's: it runs on
    /// its own whenever the filter in effect changes — and an edit that empties
    /// a box puts the empty selection in effect (halheinrich/backgammon#266).
    /// Disabling the fieldset for such a count would disable the box the user
    /// is typing in, and a focused control that is disabled loses its focus,
    /// against the ruled "typing keeps its focus and caret" (SPEC-filtering.md
    /// §1). Those counts read a parse the pick already holds, so they parse
    /// nothing, and a count and a Start each enumerate their own stack over
    /// that parse, sharing nothing a Start could race — the reason they may
    /// leave the controls, Start included, alone, however long one runs; they
    /// show only the counting line, and the holder supersedes a stale one.
    /// The first count after a pick is different — it parses the corpus, and
    /// it runs before anyone is typing — so it keeps the whole-surface busy
    /// state, which also keeps a Start or a second count from parsing the same
    /// corpus beside it. The policy is Hal's ruling (halheinrich/backgammon#374,
    /// comment 6090571891): cached recounts leave the controls usable, and the
    /// current pick's first parse remains busy — "current" being the count of
    /// the inputs on screen (<see cref="IsParsingThePick"/>).
    /// </para>
    /// </summary>
    private bool IsBusy => Controller.IsBusy || Operations.ShowsProgress || IsParsingThePick;

    /// <summary>
    /// Whether the count of what is on screen is the pick's first parse: it is
    /// running (<see cref="CurrentCount"/>) and no completed parse of this pick
    /// is held yet (<see cref="PickedProblemFolder.Parsed"/>, which the parse
    /// stores and every pick and Clear drops). Only the current inputs'
    /// count can say so: a recount left running when its pick was cleared
    /// also finds no parse held, and must not hold the next setup — the
    /// folder picker included — busy for work nobody is waiting on.
    /// </summary>
    private bool IsParsingThePick => CurrentCount.IsCounting && Folder.Parsed is null;

    /// <summary>
    /// The one "pick a folder" gesture. <see cref="Operations"/> runs it — the
    /// app-scoped owner of every folder operation, since a pick can outlive
    /// this page — and this page refuses it at once while an operation is in
    /// flight (the owner refuses too), clears its own per-visit notices, and
    /// reports what the gesture came to (<see cref="Report"/>).
    ///
    /// <para>
    /// <b>The setup ends at the click.</b> The owner's first step returns the
    /// whole setup surface to its initial no-folder state — guidance up,
    /// nothing else disclosed — and tells this page so before the OS picker and
    /// the browser's permission prompts appear, so they never play out over the
    /// previous folder's populated screen. It is the whole reset, not a
    /// cosmetic one: choosing a folder ends the current setup whatever the
    /// picker then returns. With File System Access the whole pick — picker,
    /// permission, enumeration, buffering and the landing — completes inside
    /// that one operation; without it, the click opens the hidden
    /// <c>webkitdirectory</c> input's picker, and the pick arrives later through
    /// that input's change event (<see cref="HandleFallbackPickedAsync"/>) or,
    /// on a dismissal, its cancel event (<see cref="HandleFallbackCancelled"/>).
    /// </para>
    /// </summary>
    private async Task PickFolderAsync()
    {
        if (Operations.IsInFlight) return;
        ClearPickNotices();
        Report(await Operations.PickAsync(_fallbackInput), resample: false);
    }

    /// <summary>
    /// The fallback pick landing: the hidden input's FileList, collected and
    /// filtered by the JS module (top-level <c>.xg</c> / <c>.xgp</c> only) for
    /// the pick intent this page's click opened the picker under. Capability is
    /// always <see cref="FolderWriteCapability.BrowserUnsupported"/> on this
    /// mechanism — no writable handle exists.
    ///
    /// <para>
    /// No reset of its own: this is the tail of a gesture
    /// <see cref="PickFolderAsync"/> already ended the setup for. The owner
    /// collects only for the intent still current, taking the operation before
    /// the collection binds the picked slot, so a selection reported after a
    /// newer operation began changes nothing. The collection is the app's own
    /// work from the start — the user has already chosen — so the busy state
    /// covers all of it.
    /// </para>
    /// </summary>
    private async Task HandleFallbackPickedAsync(ChangeEventArgs _)
    {
        if (_fallbackIntent is not { } intent) return;
        Report(await Operations.CollectFallbackAsync(intent, _fallbackInput), resample: false);
    }

    /// <summary>
    /// The hidden <c>webkitdirectory</c> input's dismissal: the fallback
    /// mechanism's cancelled pick, landing on the same
    /// <see cref="_cancelledPickNotice"/> the File System Access mechanism uses
    /// (its wording is cause-agnostic, so it is true here too) — when it is the
    /// dismissal of the picker still awaited; a superseded one says nothing.
    /// Without this the gesture would end on a screen the click had just reset,
    /// with no account of why — the very silence the notice exists to end.
    /// </summary>
    private void HandleFallbackCancelled()
    {
        if (_fallbackIntent is { } intent && Operations.DismissFallback(intent))
            _cancelledPickNotice = true;
    }

    /// <summary>
    /// Resample (<c>SPEC-filtering.md</c> §4, "The sample, the limits and
    /// Resample"): <see cref="Operations"/> draws the held folder again, without
    /// the picker, and lands the new sample — the setup kept, the sample
    /// replaced. Offered only while <see cref="PickedProblemFolder.CanResample"/>,
    /// and refused at once while an operation is in flight. A failure to
    /// acquire keeps everything and reports as a failed pick does; an empty
    /// draw replaces nothing and says so (<see cref="_emptyResampleNotice"/>).
    /// </summary>
    private async Task ResampleAsync()
    {
        if (Operations.IsInFlight) return;
        // The last gesture's outcome notices go: this one gets its own account.
        _pickError = null;
        _emptyResampleNotice = false;
        Report(await Operations.ResampleAsync(), resample: true);
    }

    /// <summary>
    /// The <c>Clear</c> affordance: end the current setup
    /// (<see cref="FolderOperations.ClearAsync"/>, the same reset a pick's click
    /// begins with), refused at once while an operation is in flight. The
    /// mix's stored rows, the Order and the lifetime-stats slot survive; a run
    /// in progress is not ended, since Clear replaces no sample.
    /// </summary>
    private async Task ClearSetupAsync()
    {
        if (Operations.IsInFlight) return;
        ClearPickNotices();
        await Operations.ClearAsync();
    }

    /// <summary>
    /// Report what a folder operation this page asked for came to, on this
    /// page's per-visit notices: a failed acquisition on the pick-error box; a
    /// dismissed picker on the cancelled notice; a draw with no problem files
    /// on the empty-folder or the empty-resample notice; an opened fallback
    /// picker by keeping its intent for the selection to come. A landing needs
    /// no notice — what landed is on the holder, and every page renders it —
    /// and a refusal changed nothing to report.
    /// </summary>
    private void Report(FolderOutcome outcome, bool resample)
    {
        switch (outcome.Kind)
        {
            case FolderOutcomeKind.Failed:
                _pickError = outcome.Failure?.Message;
                break;
            case FolderOutcomeKind.Cancelled:
                _cancelledPickNotice = true;
                break;
            case FolderOutcomeKind.NoFiles when resample:
                _emptyResampleNotice = true;
                break;
            case FolderOutcomeKind.NoFiles:
                _emptyFolderNotice = true;
                break;
            case FolderOutcomeKind.AwaitingSelection:
                _fallbackIntent = outcome.Intent;
                break;
            case FolderOutcomeKind.Landed:
            case FolderOutcomeKind.Refused:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome.Kind, null);
        }
    }

    /// <summary>
    /// <see cref="FolderOperations.Changed"/>: an operation began or ended, the
    /// busy cursor rose, a sample landed or a setup ended — whichever page
    /// asked for it. Re-render now, so the busy cursor paints before the work
    /// that follows, and ask for the count of what is held, which a landing
    /// has re-keyed (<see cref="ScheduleCountSync"/>).
    /// </summary>
    private void OnOperationsChanged()
    {
        StateHasChanged();
        ScheduleCountSync();
    }

    /// <summary>
    /// Report the source this page holds now (<see cref="FilterSource"/>) to
    /// <see cref="FilterSetup"/> at page initialization, as the host contract
    /// asks: the same source again changes nothing, a different one ends the
    /// filter setup. Every change to the holder after that is reported by the
    /// operation that made it (<see cref="FolderOperations"/>).
    /// </summary>
    private void ReportFilterSource() => FilterSetup.ReportSource(FilterSource);

    private void ClearPickNotices()
    {
        _pickError = null;
        _emptyFolderNotice = false;
        _emptyResampleNotice = false;
        _cancelledPickNotice = false;
        _startError = null;
        _noMatchNotice = null;
        _mixRefused = false; // a new pick can change stats capability
    }

    /// <summary>
    /// The filter setup's observer (<see cref="FilterSetup.Attach"/>): told of
    /// the current snapshot as this page attaches, then of every real change —
    /// an edit, a commit, a restoration settling, a source reported. It only
    /// schedules (<see cref="ScheduleCountSync"/>): an observer must not call
    /// the owner and records nothing here, because this page reads the owner's
    /// <see cref="FilterSetup.Current"/> whenever it needs the setup.
    /// </summary>
    private void OnFilterSetupPublished() => ScheduleCountSync();

    /// <summary>
    /// Schedule a re-render and a fresh ask for the match count
    /// (<see cref="SyncMatchCountAsync"/>) on the renderer's context, without
    /// waiting for the count, which can run long and settles into its holder
    /// on its own. The count is keyed by the setup's filter and by the sample,
    /// so both of the things that move them schedule it: a filter snapshot
    /// (<see cref="OnFilterSetupPublished"/>) and a folder operation's change,
    /// a sample's landing among them (<see cref="OnOperationsChanged"/>).
    /// </summary>
    private void ScheduleCountSync() => _ = InvokeAsync(RenderAndSyncCountAsync);

    private async Task RenderAndSyncCountAsync()
    {
        try
        {
            StateHasChanged();
            await SyncMatchCountAsync();
        }
        catch (Exception e)
        {
            // Nothing here is expected to fail — the count holder catches its
            // own failures — so anything that does is a bug, and goes to the
            // renderer's error path as this page's, not to a task nobody reads.
            await DispatchExceptionAsync(e);
        }
    }

    /// <summary>
    /// Ask <see cref="MatchCount"/> for the count of what is on screen: the
    /// setup, the sample held, the filter in effect for it and the user's ranking. Equal inputs
    /// reuse the held count — settled, or still running — which is what lets a
    /// navigate-back show its count and keep its known-zero gate at once;
    /// different inputs recount, superseding any count of older ones. Nothing
    /// is asked while no filter is in effect (an edit pending, a restoration
    /// still being read, no pick): the held count simply is not the current
    /// one, so nothing shows — a request it left running included, which
    /// this page reads only for its current inputs (<see cref="CurrentCount"/>)
    /// — and an edit undone back to the counted filter finds it again.
    ///
    /// <para>
    /// Runs on every mount and every filter snapshot. The ranking comes from
    /// the Settings page, which this page cannot be mounted beside, so a
    /// change to it is always followed by a fresh mount here — and a recount.
    /// A recount is a new count for the user to read, so the outcome notices
    /// of the last Start, which described other inputs, go with it.
    /// </para>
    /// </summary>
    private async Task SyncMatchCountAsync()
    {
        var ranking = await UserRankingAsync();
        if (CurrentCountInputs(ranking) is not { } inputs) return;

        if (!inputs.Equals(MatchCount.Inputs))
        {
            _startError = null;
            _noMatchNotice = null;
        }

        await MatchCount.EnsureAsync(inputs);
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
        // No run begins while a folder operation is in flight, whichever page
        // began it: it would start over a sample about to be replaced, or over
        // none. Refused here, where the gesture is entered, whatever the
        // button shows.
        if (Operations.IsInFlight) return;
        if (FilterInEffect is not { } cfg) return;
        // The effective mix is the on-screen draft's build while the mix
        // panel is visible, the passthrough otherwise (see EffectiveMix). Null
        // means visible-and-invalid — CanStart is dark and its hint says why,
        // so this early return is the backstop for programmatic dispatch only,
        // same as the filter guard above.
        if (EffectiveMix is not { } mix) return;
        _startError = null;
        _noMatchNotice = null;
        _mixRefused = false;
        try
        {
            // The ranking and the Order as the user's settings stand at the
            // click: the run takes both where it begins (SPEC-filtering.md
            // §7), and the hydration UserRankingAsync awaits serves both.
            var ranking = await UserRankingAsync();
            var outcome = await Controller.StartAsync(cfg, mix, ranking, Settings.Order, ignoreMix);

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
            // the quiz could present — stay on / with an outcome notice rather
            // than navigating into a 0/0 /quiz → /done bounce with no hint of
            // why. It says only what this page knows (halheinrich/backgammon#262):
            // with an active mix the telemetry says whether the composition
            // came up empty; otherwise a zero count never gets here (Start is
            // dark at zero, and #noMatchNotice already says why), so a KNOWN
            // non-zero count means every match was auto-skipped for offering no
            // play choice. An unknown count — it threw, which leaves Start live
            // — could be either, and the sentence claims neither.
            //
            // Unless this Start's parse has just established that no selected
            // problem file could be read (halheinrich/backgammon#368): the
            // accepted policy lets a completed parse publish the all-rejected
            // box and darken Start without a successful count, and that is
            // exactly the path here — the advisory count failed without a
            // parse, Start stayed live, and Start performed the first parse.
            // The box and its file list are the whole explanation; the generic
            // and mix-empty fallbacks are suppressed, since adjusting filters
            // or the mix cannot repair unreadable files, and both stay for
            // selections with readable files.
            if (Controller.IsFinished)
            {
                if (Folder.Parsed is { Report.AllRejected: true }) return;

                _noMatchNotice = Controller.LastComposition is { DrawnCount: 0 }
                    ? "Your mix drew no problems — no decision in these files matched "
                      + "the selected categories against your lifetime stats. Adjust "
                      + "the mix, the filters, or the files."
                    : CurrentMatchSummary is { AnswerTypes.Total: > 0 }
                        ? "Every decision matching these filters was skipped for offering "
                          + "no play choice — adjust the filters or pick different files."
                        : "No quiz problems could be presented — try again, or adjust the "
                          + "filters or pick different files.";
                return;
            }

            // A live quiz is starting. The controller marked it live as the run
            // became current (QuizLiveMarker), so a mid-quiz full reload is
            // acknowledged on the next boot; the no-match path above committed a
            // run that had already ended, which it marked as nothing to lose.
            Nav.NavigateTo("/quiz");
        }
        catch (Exception ex)
        {
            // FilterConfig.Build() validation failure, source construction
            // failure, etc. Surface to the user rather than faulting the app.
            // A Start that faults keeps the quiz that was running
            // (SPEC-quiz-history.md §8), so "Back to quiz" still returns to it.
            _startError = ex.Message;
        }
    }

    /// <summary>
    /// The user's ranking, as the count and the Start hand it to the controller
    /// (<see cref="QuizSettings.Ranking"/>; SPEC-scoring.md §2a). Both read it
    /// through this, so the count describes the pool the Start will draw under
    /// the same ranking, and each awaits the settings' one hydration first: the
    /// task is idempotent and complete long before either gesture in practice,
    /// but a pick made while the first read was still in flight could otherwise
    /// count under the default rather than the stored choice — awaiting makes
    /// that ordering structural instead of a timing argument.
    /// </summary>
    private async Task<PlayRanking> UserRankingAsync()
    {
        await Settings.EnsureHydratedAsync();
        return Settings.Ranking;
    }

    /// <summary>
    /// Return to the problem a live quiz is sitting on (issue halheinrich/backgammon#58), rendered
    /// under the <c>HasStarted &amp;&amp; !IsFinished</c> predicate — the live
    /// half of what <see cref="ReturnControl"/> does on the other pages.
    ///
    /// <para>
    /// Navigation only: the quiz state it returns to is app-scoped and is not
    /// at risk from the visit — the picked files are read at Start, so the
    /// setup surface's gestures that replace no sample leave a running quiz
    /// alone (ending a setup touches only the JS <i>picked</i> slot, pinned).
    /// The one that does end it is a new sample landing
    /// (<see cref="FolderOperations"/>, <c>SPEC-quiz-history.md</c> §9), after
    /// which this is not rendered: there is no live quiz to return to. There
    /// is deliberately no guard, no confirmation and no warning around the
    /// round trip; what was missing was only the way back.
    /// </para>
    /// </summary>
    private void BackToQuiz()
    {
        Nav.NavigateTo("/quiz");
    }
}
