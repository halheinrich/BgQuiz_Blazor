using BgDataTypes_Lib;
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
/// app navigates to <c>/quiz</c>. Pick failures and
/// <see cref="FilterConfig.Build"/> / source-construction failures are caught
/// and surfaced as banners rather than faulting the WebAssembly app. The two
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
/// truncations, stats capability and the retirement forecast, which survive
/// navigation with the pick they describe — bind to the app-scoped
/// <see cref="QuizNoticeDismissal"/> keyed on
/// <see cref="PickedProblemFolder.PickOccurrence"/>, and the browser-storage
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
/// returns the whole setup surface to its pre-setup state
/// (<see cref="EndCurrentSetupAsync"/>, shared with the <c>Clear</c> affordance,
/// which encodes the same decision): folder and picked slot, the mix draft, the
/// filter setup (by reporting the source's end to <see cref="FilterSetup"/>),
/// and every pick-scoped notice. The mix
/// setting is deliberately untouched — it is a choice, and choices outlive
/// setups (§4).
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
/// A third, ungated toggle — "Shuffle order" — lives alongside the gate in the
/// per-app <see cref="ShuffleOption"/> holder. It is presentation-only (order,
/// not admission), so it plays no part in <c>CanStart</c>: the source factory
/// reads it live at Start to decide whether to wrap the picked set in a
/// <c>ShuffledProblemSetSource</c>.
/// </para>
///
/// <para>
/// Below the setup surface — outside the busy <c>fieldset</c>, since it only
/// navigates — sits a conditional <b>"Back to quiz"</b> button, on the
/// <c>HasStarted &amp;&amp; !IsFinished</c> predicate (issue halheinrich/backgammon#58). Home is the
/// third page a user can reach mid-quiz and the last one that had no way back.
/// The visit itself was always safe — see <see cref="BackToQuiz"/> — so the
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
    /// Sibling of <see cref="_startError"/> for pick failures — an unexpected
    /// browser error, or a file past the <see cref="PickedFileLimits.MaxFileBytes"/>
    /// cap. A per-visit failure notice (assertive), like the start error, and
    /// the notice's whole state: dismissing it clears this field, and the next
    /// failure assigns it again and shows fresh.
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
    /// True while this page is running a foreground operation the user must
    /// wait out: the scan-and-buffer half of a folder pick (issue
    /// halheinrich/backgammon#48). One flag, not one per site — the affordance
    /// is a property of the <i>page</i> ("BgQuiz is working, don't touch
    /// anything"), not of the operation. Raised only through
    /// <see cref="EnterBusyAsync"/> / <see cref="RunBusyAsync"/>, which own the
    /// paint-before-the-work discipline. The match count's running state is the
    /// count holder's own (<see cref="MatchCount.IsCounting"/>), because the
    /// count outlives this page; <see cref="IsBusy"/> reads it only while the
    /// count is the pick's first parse (<see cref="IsParsingThePick"/>).
    /// </summary>
    private bool _busy;

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
    /// the pick, named by its generation counter — or <see langword="null"/>
    /// while no folder with problem files is held. <b>Minted here and nowhere
    /// else</b>: what this page reports to <see cref="FilterSetup"/>
    /// (<see cref="ReportFilterSource"/>), what every gate asks the owner's
    /// snapshot about (<see cref="FilterInEffect"/>) and what the match count
    /// is keyed by (<see cref="CurrentCountInputs"/>) all read this one
    /// property, so the report and the reads cannot encode the pick
    /// differently. <see langword="null"/> means "no source exists", which is
    /// the only thing the owner may be told it means — never "not yet known":
    /// the holder's state is read synchronously and is always known.
    /// </summary>
    private FilterSourceToken? FilterSource =>
        Folder.HasFiles ? FilterSourceToken.FromGeneration(Folder.PickGeneration) : null;

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
    /// generation bumps, the owner is told, and the key stops matching. A new
    /// instance on every read, so nothing this page does with it reaches the
    /// owner.
    /// </para>
    /// </summary>
    private FilterConfig? FilterInEffect =>
        FilterSource is { } source ? FilterSetup.Current.ConfigInEffectFor(source) : null;

    /// <summary>
    /// The settled count of what is on screen — the pick, the filter in effect
    /// and the ranking — or <see langword="null"/> when nothing is in effect,
    /// the count is still running, or it failed. The one reading the count
    /// line, the zero box, the known-zero gate and the no-match fallback all
    /// take, so none of them can show or gate on a count of other inputs.
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
    private MatchSummary? CurrentMatchSummary => MatchCount.SummaryFor(CurrentCountInputs(Settings.Ranking));

    /// <summary>
    /// The match count's inputs for what is on screen under
    /// <paramref name="ranking"/>, or <see langword="null"/> when no filter is
    /// in effect for the pick — the key <see cref="MatchCount"/> holds its
    /// result under. Read off the owner's current snapshot, which the inputs
    /// keep, so the key is a stable reading and never a copy of the selection.
    /// </summary>
    private MatchCountInputs? CurrentCountInputs(PlayRanking ranking) =>
        FilterSource is { } source ? MatchCountInputs.For(FilterSetup.Current, source, ranking) : null;

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
        _filterAttachment?.Dispose();
    }

    /// <summary>
    /// The page's one busy predicate, driving <i>both</i> halves of the busy
    /// affordance — the <c>app-busy</c> progress cursor and the whole-surface
    /// <c>&lt;fieldset disabled&gt;</c> — from a single expression, so the cursor
    /// and the disabled controls can never disagree about whether the page is
    /// working. A union of the independent sources: the controller's transition
    /// gate (Start / Restart), this page's own foreground work
    /// (<see cref="_busy"/>), and a match count that is parsing the pick
    /// (<see cref="IsParsingThePick"/>).
    ///
    /// <para>
    /// <b>Not every count.</b> The count is no longer a gesture's: it runs on
    /// its own whenever the filter in effect changes — and an edit that empties
    /// a box puts the empty selection in effect (halheinrich/backgammon#266).
    /// Disabling the fieldset for such a count would disable the box the user
    /// is typing in, and a focused control that is disabled loses its focus,
    /// against the ruled "typing keeps its focus and caret" (SPEC-filtering.md
    /// §1). Those counts read a parse the pick already holds, so they are
    /// short, parse nothing, and the holder supersedes a stale one; they leave
    /// the controls — Start included — alone and show only the counting line.
    /// The first count after a pick is different — it parses the corpus, the
    /// one long stretch, and it runs before anyone is typing — so it keeps the
    /// whole-surface busy state, which also keeps a Start or a second count
    /// from parsing the same corpus beside it.
    /// </para>
    /// </summary>
    private bool IsBusy => Controller.IsBusy || _busy || IsParsingThePick;

    /// <summary>
    /// Whether the running match count is the pick's first parse: a count is
    /// running and no completed parse of this pick is held yet
    /// (<see cref="PickedProblemFolder.Parsed"/>, which the parse stores and
    /// every pick and Clear drops).
    /// </summary>
    private bool IsParsingThePick => MatchCount.IsCounting && Folder.Parsed is null;

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
            ClearFolder();
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
            ClearFolder();
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
    /// (<see cref="QuizStatsStore.RefreshPickedStatsAsync"/>, issue halheinrich/backgammon#87). It
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

        if (outcome.Files.IsEmpty)
        {
            // Already clear — EndCurrentSetupAsync ran at the click and nothing
            // since could have set it. Re-stated so this shared landing carries
            // its own postcondition ("an empty pick holds no folder") rather
            // than inheriting it from whoever called it.
            ClearFolder();
            _emptyFolderNotice = true;
            return;
        }

        // The truncation report rides onto the holder with the files it describes,
        // so the notice that renders it lives exactly as long as the partial pick
        // it is about — including across navigate-back, which a page field would
        // not survive.
        //
        // Reporting the new source ends the outgoing filter setup in the owner
        // (the click's ClearFolder already did, with null) and begins this
        // pick's. The render this triggers also mounts the FilterSurface behind
        // the HasFiles gate, and the composite loads this folder's saved-filters
        // document itself — a setup-time, degrade-tolerant read through the
        // picked-slot storage adapter (null under a fallback pick, so no
        // context). Nothing to await for that: saved-filters trouble can never
        // block a pick.
        Folder.Set(outcome.DirectoryName, outcome.Files, outcome.Capability, outcome.Truncations);
        ReportFilterSource();

        // Now that the holder describes this pick, ask whether a mix can mean
        // anything for it. Degrade-tolerant end to end (see the store), so this
        // await can neither fail the pick nor surface a notice of its own.
        await StatsStore.RefreshPickedStatsAsync();
    }

    /// <summary>
    /// Empty the folder holder and tell the filter setup's owner no source is
    /// held — one step, so no path that drops the folder can leave the owner
    /// believing in the pick it held.
    /// </summary>
    private void ClearFolder()
    {
        Folder.Clear();
        ReportFilterSource();
    }

    /// <summary>
    /// Report the source this page holds now (<see cref="FilterSource"/>) to
    /// <see cref="FilterSetup"/> — at page initialization, and after every
    /// change to the folder holder, whether or not the filter surface is
    /// mounted. A different source ends the filter setup (a new generation,
    /// the applied baseline dropped, the draft kept); the same one does
    /// nothing. This is the whole of the filter half of ending a setup.
    /// </summary>
    private void ReportFilterSource() => FilterSetup.ReportSource(FilterSource);

    /// <summary>
    /// End the current setup: return the whole surface to its pre-setup,
    /// no-folder state. The single reset behind <i>both</i> gestures that end a
    /// setup — the <c>Clear</c> affordance, and the <i>start</i> of a pick
    /// gesture (<see cref="PickFolderAsync"/>) — because they encode the same
    /// decision, and two spellings of one decision drift.
    ///
    /// <para>
    /// <b>Everything pick-scoped goes.</b> The folder holder and the JS module's
    /// picked slot, the mix draft (<see cref="MixDraft.Discard"/> —
    /// see the inline comment; the <i>stored</i> mix deliberately survives),
    /// the filter setup, and every pick-scoped notice
    /// (<see cref="ClearPickNotices"/>). The filter setup ends by
    /// <see cref="ClearFolder"/>'s report: the owner drops the applied
    /// baseline and keeps the draft, so the selection on screen survives into
    /// the next pick and must be in effect there by the owner's rules, never
    /// by this page's. The match count needs no line either: it is keyed by
    /// the pick, so no count of the outgoing pick can be read for the next
    /// one. The saved-filters context lives in the hosted
    /// <c>FilterSurface</c>, which rebuilds it when the owner's generation
    /// moves. Two things deliberately survive, and the class summary says why:
    /// <see cref="ShuffleOption"/> and the lifetime-stats slot.
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
    /// <see cref="EnterBusyAsync"/> uses.
    /// </para>
    /// </summary>
    private async Task EndCurrentSetupAsync()
    {
        ClearFolder();
        // The mix's rows outlive the setup (§4), so this is the draft's
        // in-memory reset and not a deletion: Discard blanks the builder and
        // forgets hydration — deliberately without touching localStorage — so
        // a mix-capable pick's re-mounted panel re-hydrates the stored
        // last-valid mix. Nothing sits beside it any more. The consent revoke
        // that did is gone with the bit: visibility is derived per render from
        // a standing setting and the incoming pick's own stats fact
        // (SPEC-filtering.md §5, "Visible means in effect"), so a mix in effect
        // for the outgoing folder cannot survive into a folder without stats —
        // the derivation simply reads false there, with no state to reset and
        // no capability fork in the gate. Into a folder that HAS stats it does
        // carry, and applies, which is the ruling.
        MixDraft.Discard();
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
    }

    /// <summary>
    /// The filter setup's observer (<see cref="FilterSetup.Attach"/>): told of
    /// the current snapshot as this page attaches, then of every real change —
    /// an edit, a commit, a restoration settling, a source reported. It only
    /// schedules: an observer must not call the owner and records nothing
    /// here, because this page reads the owner's <see cref="FilterSetup.Current"/>
    /// whenever it needs the setup. The scheduled work re-renders the page and
    /// re-asks for the match count (<see cref="SyncMatchCountAsync"/>).
    /// </summary>
    private void OnFilterSetupPublished() => _ = InvokeAsync(FilterSetupChangedAsync);

    private async Task FilterSetupChangedAsync()
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
    /// pick, the filter in effect for it and the user's ranking. Equal inputs
    /// reuse the held count — settled, or still running — which is what lets a
    /// navigate-back show its count and keep its known-zero gate at once;
    /// different inputs recount, superseding any count of older ones. Nothing
    /// is asked while no filter is in effect (an edit pending, a restoration
    /// still being read, no pick): the held count simply is not the current
    /// one, so nothing shows, and an edit undone back to the counted filter
    /// finds it again.
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

    private void HandleShuffleToggled(ChangeEventArgs e)
    {
        // A checkbox has no half-edited state, so the toggle is recorded live —
        // no applied/edited distinction of the kind the filter setup keeps.
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
            var outcome = await Controller.StartAsync(cfg, mix, await UserRankingAsync(), ignoreMix);

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
