using BackgammonDiagram_Lib;
using BgDataTypes_Lib;
using BgDiag_Razor.Components;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// Quiz page: renders the current decision against the scoped
/// <see cref="QuizController"/>, routing the board region by the record's kind
/// — checker plays to <see cref="BackgammonPlayEntry"/> (click-driven assembly),
/// cube decisions to a board-only <see cref="BackgammonDiagram"/> whose answer is
/// entered by the <see cref="BackgammonCubeActions"/> radios in the action row —
/// and exposes the per-kind action row.
///
/// <para>
/// <b>Review branch.</b> Mirrors the controller's two view states. While
/// <see cref="QuizController.Review"/> is null the page is <i>answering</i> —
/// it renders the entry component and the answering row. Once Submit scores and
/// the controller sets <see cref="QuizController.Review"/>, the page flips to
/// the <i>review</i> view: a read-only <see cref="BackgammonDiagram"/> in
/// <see cref="DiagramMode.Solution"/> (the filled analysis panel, exactly as
/// the PPTX exporter renders it) with the user's answer marked, a compact
/// verdict line, and Continue. Continue — like ▶, which is Continue there —
/// goes to the next problem's answering state. The review diagram's
/// <c>OnDiceClicked</c> is also bound to <see cref="NextAsync"/> — clicking the
/// dice hit-region (already wired for click-driven play assembly during
/// answering) advances past the solution exactly like the Continue button.
/// </para>
///
/// <para>
/// <b>Navigation, and why every landing starts clean.</b> ⏮ ◀ ▶ ⏭
/// (<c>SPEC-quiz-history.md</c> §2) move among the problems already presented,
/// and ▶ at the frontier brings the next one. Every landing is the answering
/// state (§3), with nothing entered and nothing latched for Submit, wherever it
/// lands and from whichever view state — a problem answered earlier included,
/// where a submission is practice and its review is marked so (see
/// <see cref="VerdictText"/>). The page reaches the clean slate the same way
/// for every control, with no review branch needed in between:
/// <list type="bullet">
///   <item><b>Both latches</b> — <see cref="_completedPlay"/> and
///   <see cref="_completedCube"/> — are nulled by
///   <see cref="HandleStateChanged"/> on every controller transition, and every
///   move fires one. The <see cref="BackgammonCubeActions"/> row is controlled
///   on the <i>answer</i> and holds no state the answer does not express: every
///   pill is one whole <see cref="CubeAnswer"/>, so nulling the field clears
///   whatever is lit.</item>
///   <item><b>The play entry</b> holds its own in-progress click state and
///   resets it only when the incoming request describes a different position —
///   and two problems of one run can share a position. So the entry is keyed
///   on the problem's place in the run (its stream slot,
///   <see cref="QuizController.ProblemNumber"/>), and every landing mounts a
///   fresh one.</item>
///   <item><b>The notes overlay</b> lives only in the review branch, and every
///   landing is the answering state, so it is closed by being unmounted.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Show stats.</b> A "Show stats" button, present in both the answering and
/// review states (in the trailing <c>ms-auto</c> slot of each state's action
/// row), navigates to <c>/stats</c> — a read-only, live view of the same
/// <see cref="QuizController"/> mid-quiz. Because the controller is a per-tab
/// scoped instance that survives in-app navigation, returning to <c>/quiz</c>
/// resumes at the same problem with no state to persist or restore.
/// </para>
///
/// <para>
/// <b>Every board is the decision's own request, under the run's ranking.</b>
/// The answering board, the play entry and the solution are all
/// <see cref="DiagramRequest.ForDecision"/> over the current record, with
/// <see cref="QuizController.Ranking"/> — the ranking the run filters and
/// scores with (SPEC-scoring.md §2a) — so the solution's best play, order and
/// rank numbers are the ones the verdict beside it was scored against, and no
/// producer default stands in. The request holds the record and copies nothing
/// out of it; this page varies only its options (the mode, the side, the hide
/// ceiling, the † mark).
/// </para>
///
/// <para>
/// <b>Marking the user's answer.</b> The record's own recorded play draws the
/// primary <c>*</c>; the quiz user's answer is the secondary <c>†</c>,
/// <see cref="DiagramRequest.SecondaryPlayIndex"/>, set from the review's
/// <see cref="ProblemReview.Play.CandidateIndex"/> — the candidate the play is,
/// scored or not, and none off the list, so no mark draws. A cube review
/// marks nothing: the panel's "Actual" line is the recorded players' actions,
/// read off the record, and the quiz user's answer is named by the verdict
/// line instead.
/// </para>
///
/// <para>
/// <b>Submit gating.</b> Submit is enabled once the page holds a complete
/// answer. For a play, <see cref="BackgammonPlayEntry"/>'s <c>OnPlayCompleted</c>
/// fires once all dice are consumed legally, latching <see cref="_completedPlay"/>.
/// For a cube, <see cref="BackgammonCubeActions"/> is one radio group over the
/// four answers (<see cref="CubeAnswer"/>, SPEC-scoring.md §3 as amended on
/// halheinrich/backgammon#326), all four offered at every cube decision. It is
/// handed the decision on screen as its <c>Decision</c>, from which it labels
/// each pill — the fourth reads Too good or No double / Pass by the
/// decision's own reading — so this page names no answer and re-derives no
/// gammon fact. It emits the chosen <see cref="CubeAnswer"/> on every click,
/// which <c>@bind-Value</c> writes into <see cref="_completedCube"/>; a later
/// click re-fires, so the field always holds the latest answer. Gating Submit
/// on the field being non-null is therefore gating it on <i>a pill
/// chosen</i>, lit from the first click. Both fields clear on any controller
/// transition (submit / every move / restart) via
/// <see cref="HandleStateChanged"/>; the play latch also clears on undo. The
/// gate itself is <see cref="CanSubmit"/>, one member read by both Submit
/// buttons and by the spacebar, which presses Submit whenever it is lit (see
/// below).
/// </para>
///
/// <para>
/// <b>The cube verdict judges the whole answer, in one line</b> (SPEC-scoring.md
/// §3, "The tie"): it names the user's answer with its label at the decision,
/// says whether it is correct, and when it is not, what it lost and which
/// answers were best. See <see cref="CubeVerdict"/>.
/// </para>
///
/// <para>
/// <b>Ending the run early.</b> Both action rows trail with an <b>End quiz</b>
/// button (issue halheinrich/backgammon#57) — see <see cref="EndQuizAsync"/> for why it sits at the far
/// end of the row and carries no confirmation. It is the only control here that
/// finishes a run the source has not exhausted; everything about what that leaves
/// behind (an unanswered problem counted as a skip, an answered one kept — the
/// answer of record, not whatever review is on screen, already in the lifetime
/// record since its Submit) belongs to
/// <see cref="QuizController.EndQuizAsync"/>, which this page merely calls.
/// </para>
///
/// <para>
/// <b>The action row</b> is <c>SPEC-quiz-view.md</c> §4's composition under
/// quiz navigation. Answering a checker play: Undo all and Undo last lead, then
/// Submit — the two Undo buttons live for the whole of the entry, disabled only
/// while the controller is busy (see <see cref="UndoLast"/> for why gating them
/// on the entry's <c>@ref</c> made them dead for exactly the window they exist
/// to serve). Answering a cube decision: the <see cref="BackgammonCubeActions"/>
/// radios lead (the answer input, since the board region is board-only), then
/// Submit — a cube answer has no partial-move state, so Undo does not apply. At
/// review: Continue. Then, in every state, ⏮ ◀ ▶ ⏭ from one render site; then,
/// at review, the <see cref="DecisionNotes"/> control when the decision carries
/// a comment (§4's 2026-09-15 amendment, <c>halheinrich/backgammon#31</c>);
/// then the trailing cluster with Show stats and End quiz. The Skip and Redo
/// buttons are retired (<c>SPEC-quiz-history.md</c> §2): ▶ took Skip's place
/// and the navigation buttons Redo's.
/// </para>
///
/// <para>
/// <b>Every notice above the board dismisses on click, and the mix composition
/// notice also retires on the first answer.</b> Both composition variants — the
/// capless composition-only status line and the length-bound shortfall alert —
/// render from <see cref="QuizController.LastComposition"/>, which lives as long
/// as the run does, so they used to sit above every problem for the whole quiz.
/// They disappear once the user submits their first answer, checker or cube alike
/// (see <see cref="SubmitAsync"/>), <i>or</i> the moment the user clicks them
/// (<see cref="DismissComposition"/>): the notice describes how this quiz was
/// built, worth reading before answering and stale chrome after. Either gesture
/// ends it. The stats notices dismiss the same way but have no automatic
/// retirement — a degraded recording context is not something an answer makes
/// stale.
/// </para>
///
/// <para>
/// Every notice renders through the shared <c>Notice</c>, which owns the
/// affordance — the visible close button and the whole box as the large,
/// low-vision-friendly target (<c>SPEC-notices.md</c> §3). This page owns only
/// who holds each dismissal: every one binds to the scoped
/// <see cref="QuizNoticeDismissal"/> holder — <i>not</i> by clearing the
/// controller's telemetry, which still frames the composition notice, carries
/// <see cref="QuizController.ProblemCount"/>, and feeds Home's composed-to-zero
/// wording, and <i>not</i> a page field or the component's own bit, either of
/// which the <c>Show stats</c> round trip would reset (this page is
/// re-instantiated on in-app navigation). Each is keyed on its notice's current
/// occurrence — the composition instance, the store's
/// <see cref="QuizStatsStore.StatusOccurrence"/> and
/// <see cref="QuizStatsStore.StatsRetiredOccurrence"/> — so the next
/// Start/Restart, or the next stats transition, shows its notice again without
/// any reset call site. The "No quiz in progress" notice is a gate reason — the
/// page's whole content in that state — and does not dismiss.
/// </para>
///
/// <para>
/// <b>The maximize-board mode</b> (issue <c>halheinrich/backgammon#41</c>,
/// conforming to <c>SPEC-quiz-view.md</c> §4). With the user's
/// <see cref="QuizSettings.MaximizeBoardWhileAnswering"/> setting on, the
/// <i>answering</i> composition drops everything below the notices except the
/// action row — score panel and status strip suppressed — and renders the board
/// on a board-only canvas. Both legs are required: §2's measurement found that
/// suppressing chrome alone changes the rendered canvas not at all, because the
/// panel-padded 16:9 canvas is width-bound, so the freed height is unusable
/// until the canvas itself stops allocating the blank panel. Review normalizes
/// back to the full composition, because it needs the panel and needs it
/// filled. The action row keeps every instrument, cube radios included, so every
/// answer stays makeable without leaving the maximized view.
/// <see cref="MaximizedAnswering"/> is the whole of the mode's state; see it for
/// why nothing stores it.
/// </para>
///
/// <para>
/// <b>The solution's depth treatment</b> (issues
/// <c>halheinrich/backgammon#150</c>, <c>halheinrich/backgammon#282</c> and
/// <c>halheinrich/backgammon#66</c>). The ranking orders and numbers the
/// review's candidate list, and it is the run's, never read from the settings
/// here — a ranking changed mid-run reaches the next run, so the list and the
/// verdict cannot disagree. The hide ceiling, which changes no score, is the
/// user's live setting (<see cref="QuizSettings.MaximumHiddenCandidateAnalysisLevel"/>),
/// passed through unchanged. Neither is a page concern beyond that, and the
/// ceiling never reaches the answering board, which is
/// <see cref="DiagramMode.Problem"/> and has no candidate list to treat.
/// </para>
///
/// <para>
/// <b>IsFinished transition.</b> Subscribed to
/// <see cref="QuizController.StateChanged"/>. When the controller's
/// <see cref="QuizController.IsFinished"/> flips true (the source exhausted on
/// ▶, or End quiz), the page navigates to <c>/done</c>.
/// </para>
///
/// <para>
/// <b>The spacebar presses Submit, or ▶</b> (issue
/// <c>halheinrich/backgammon#149</c>, ruled 2026-09-02: always on, no setting;
/// amended by <c>halheinrich/backgammon#200</c>; the rule is now
/// <c>SPEC-quiz-history.md</c> §2's, with Hal's clarification of 2026-10-02).
/// It presses Submit when Submit is lit — a complete checker play, or a cube
/// answer chosen — and ▶ otherwise: on the solution view, where ▶ is
/// Continue, as clicking the dice is; and in every answering state with Submit
/// dark, whether ▶ is named Skip or Next. While the controller is busy it does
/// nothing. The rule is <see cref="HandleSpaceKeyAsync"/>, and it owns neither
/// half of any branch: <i>whether</i> Space acts is the button's own gate
/// (<see cref="CanSubmit"/>, <see cref="CanGoNext"/>), and <i>what</i> it does
/// is the button's own method (<see cref="SubmitAsync"/>,
/// <see cref="NextAsync"/>), so the key and the button can never differ in
/// busy gating, in what is recorded, or in how the run moves. Which presses
/// reach it is decided in the browser, by <c>wwwroot/js/quizKeys.js</c>, from
/// the event alone (Space, unmodified, not a repeat, focus on nothing that
/// consumes space — see the module's comment for the filter); this was the
/// app's first JS-invokable callback, attached on the first render and
/// detached on disposal, which is why the page is
/// <see cref="IAsyncDisposable"/> (the second,
/// <see cref="HandleActionRowResized"/>, carries the action row's width for
/// the cube pills' form, and is released the same way). Attached, the module sets
/// <see cref="QuizKeysMark.AttachedAttribute"/> on the document element — the
/// readiness signal the browser tests wait on before they press
/// (<c>halheinrich/backgammon#198</c>).
/// </para>
/// </summary>
public partial class Quiz : ComponentBase, IAsyncDisposable
{
    /// <summary>
    /// The keyboard module, imported from this project's static web assets
    /// (served at the app root). Relative to the document, as the folder
    /// module's path was when it lived here. Internal so the bUnit fixture
    /// plans the very import the page makes rather than restating the path.
    /// </summary>
    internal const string KeysModulePath = "./js/quizKeys.js";

    /// <summary>
    /// The module that reports the action row's width, which the cube pills'
    /// form is decided from (<see cref="CubeLabelsAbbreviate"/>). Imported
    /// beside <see cref="KeysModulePath"/> and internal for the same reason.
    /// </summary>
    internal const string RowWidthModulePath = "./js/actionRowWidth.js";

    private BackgammonPlayEntry? _playEntry;
    private Play? _completedPlay;
    private CubeAnswer? _completedCube;

    /// <summary>The imported keyboard module; null until the first render's import lands.</summary>
    private IJSObjectReference? _keys;

    /// <summary>The imported row-width module; null until the first render's import lands.</summary>
    private IJSObjectReference? _rowWidth;

    /// <summary>The reference both modules call back through; created once both are imported, disposed with the page.</summary>
    private DotNetObjectReference<Quiz>? _self;

    /// <summary>The action row, observed by the row-width module whenever Blazor creates it.</summary>
    private ElementReference _actionRow;

    /// <summary>
    /// The <see cref="ElementReference.Id"/> of the row under observation, so
    /// a render that keeps the row asks for nothing and one that creates it
    /// afresh (a new quiz after the last one ended) observes the new element.
    /// </summary>
    private string? _observedRow;

    /// <summary>
    /// The action row's width as last reported by the browser; null until the
    /// first report, which leaves the pills in their full form.
    /// </summary>
    private double? _actionRowWidth;

    /// <summary>Set by <see cref="DisposeAsync"/>, so an import still in flight at disposal releases rather than attaches.</summary>
    private bool _disposed;

    /// <summary>
    /// The two canvases this page ever asks for, shared rather than rebuilt per
    /// render: <see cref="DiagramOptions"/> is all-<c>init</c>, so an instance is
    /// immutable and two static readonly ones cost nothing and cannot drift.
    /// <see cref="FullCanvas"/> is the producer's own defaults — the canvas every
    /// state used before this arc, and still every state's canvas with the
    /// maximize setting off.
    /// </summary>
    private static readonly DiagramOptions FullCanvas = new();

    /// <summary>
    /// The maximized-answering canvas: the analysis panel's blank allocation
    /// dropped, so the freed height actually reaches the board proper
    /// (SPEC-quiz-view.md §2 — suppressing chrome alone measured as changing the
    /// canvas <i>not at all</i>, because the 16:9 canvas is width-bound).
    ///
    /// <para>
    /// <b>Problem mode only.</b> The producer throws
    /// <see cref="ArgumentException"/> from both <c>RenderSvg</c> and
    /// <c>GetHitRegions</c> for a <see cref="DiagramMode.Solution"/> request
    /// carrying this preset — Solution exists to show the filled panel. The
    /// guard is not a check anywhere; it is <see cref="BoardOptions"/>'s
    /// derivation, which cannot select this while a review is being rendered.
    /// </para>
    /// </summary>
    private static readonly DiagramOptions BoardOnlyCanvas =
        new() { Aspect = AspectPreset.BoardOnly };

    /// <summary>
    /// Whether the composition fell short of what the mix requested — the
    /// overall draw missed the target (requested length exceeded reachable
    /// supply), or any entry's pool ran dry and its share was redistributed
    /// (possible even when the overall count was met). Drives the shortfall
    /// alert above the board — consulted only for a length-bound mix
    /// (the composition's own <see cref="BgGame_Lib.MixComposition.HasRequestedLength"/>):
    /// capless, per-entry
    /// <c>Requested</c> is apportionment of the pool union rather than a user
    /// ask, so an outdrawn entry is not "short" and the page renders the
    /// composition-only status line instead.
    /// </summary>
    private static bool HasShortfall(BgGame_Lib.MixComposition comp) =>
        comp.DrawnCount < comp.TargetCount || comp.Entries.Any(e => e.Drawn < e.Requested);

    /// <summary>
    /// On load: make sure the user's settings are hydrated (the board's side
    /// comes from them, and the <i>first</i> render must already have it — see
    /// below), subscribe to <see cref="QuizController.StateChanged"/> so the page
    /// re-renders on each transition, then apply the same start/finish guards
    /// <c>Stats</c> uses — bounce to <c>/</c> with no quiz in progress, to
    /// <c>/done</c> if the source is already exhausted.
    ///
    /// <para>
    /// The hydration await is all but free and is deliberately not a
    /// <c>_hydrated</c> render gate: <c>Home</c> — which every quiz passes
    /// through, and which this page bounces back to when it has not — kicked the
    /// same idempotent task off long before, so what is awaited here is an
    /// already-completed task. Blazor renders nothing extra for that, which is
    /// exactly why the board cannot paint on the default side and flip a frame
    /// later.
    /// </para>
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        await Settings.EnsureHydratedAsync();

        Controller.StateChanged += HandleStateChanged;
        // Re-render on stats-context transitions too (Ready → WriteFailed is
        // the one that can happen mid-quiz), so the stats notice appears the
        // moment the write-back degrades.
        StatsStore.StatusChanged += HandleStatsStatusChanged;

        // Direct nav to /quiz with no quiz in progress: bounce to Home.
        if (!Controller.HasStarted)
        {
            Nav.NavigateTo("/", replace: true);
            return;
        }

        // Direct nav to /quiz when the source is already exhausted: send to /done.
        if (Controller.IsFinished)
        {
            Nav.NavigateTo("/done", replace: true);
        }
    }

    private void HandleStateChanged()
    {
        // Every controller transition moves the cursor, puts a review up, or
        // begins a run; the previously latched answers no longer apply. This is
        // what makes every landing start with nothing latched for Submit.
        _completedPlay = null;
        _completedCube = null;

        if (Controller.IsFinished)
        {
            Nav.NavigateTo("/done");
            return;
        }

        InvokeAsync(StateHasChanged);
    }

    private void HandleStatsStatusChanged()
    {
        InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Import the page's two modules on the first render, attach the spacebar
    /// shortcut, and from then on keep the action row under observation.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender) await ImportModulesAsync();
        await ObserveActionRowAsync();
    }

    /// <summary>
    /// The first render's half of <see cref="OnAfterRenderAsync"/>: both
    /// imports, then the keyboard module's attach. Once only, because the
    /// keyboard module listens on the document, not on any element this page
    /// re-renders, so there is nothing to re-attach. The imports are awaited
    /// before anything is created from them, and a disposal that lands during
    /// those awaits is honoured by releasing the modules instead of attaching
    /// — the one ordering that could otherwise leave a listener holding a
    /// disposed reference (a Show-stats round trip re-instantiates this page,
    /// so the window is real).
    /// </summary>
    private async Task ImportModulesAsync()
    {
        var keys = await JS.InvokeAsync<IJSObjectReference>("import", KeysModulePath);
        var rowWidth = await JS.InvokeAsync<IJSObjectReference>("import", RowWidthModulePath);
        if (_disposed)
        {
            await keys.DisposeAsync();
            await rowWidth.DisposeAsync();
            return;
        }

        _keys = keys;
        _rowWidth = rowWidth;
        _self = DotNetObjectReference.Create(this);
        // The callback's name and the readiness mark's name travel with the
        // reference so each is spelled exactly once, on this side; the module
        // never restates either.
        await _keys.InvokeVoidAsync(
            "attach", _self, nameof(HandleSpaceKeyAsync), QuizKeysMark.AttachedAttribute);
    }

    /// <summary>
    /// Hand the action row to the row-width module whenever this render
    /// created it: once the modules are in, a problem is on screen (the row
    /// renders with it), and the row is not the element already observed.
    /// The observed id is recorded before the await, so a render landing
    /// during it does not observe the same row twice.
    /// </summary>
    private async Task ObserveActionRowAsync()
    {
        if (_rowWidth is null || _self is null || Controller.Current is null) return;
        if (_actionRow.Id == _observedRow) return;

        _observedRow = _actionRow.Id;
        await _rowWidth.InvokeVoidAsync(
            "observe", _actionRow, _self, nameof(HandleActionRowResized));
    }

    /// <summary>
    /// The row-width module's report: the action row is now
    /// <paramref name="width"/> pixels wide. It renders only when the report
    /// changes the form of the pills on screen, so a window being dragged
    /// wider re-renders the page once, at the switch-over, not once a frame.
    /// Public and <see cref="JSInvokableAttribute"/> for the module's sake, as
    /// <see cref="HandleSpaceKeyAsync"/> is; nothing else calls it.
    /// </summary>
    [JSInvokable]
    public void HandleActionRowResized(double width)
    {
        var before = PillsAbbreviated;
        _actionRowWidth = width;
        if (PillsAbbreviated != before) StateHasChanged();
    }

    /// <summary>
    /// The form of the cube pills on screen: null when no cube decision is
    /// being answered (no pills render), else whether they abbreviate.
    /// </summary>
    private bool? PillsAbbreviated =>
        Controller.Current is CubeDecision cube && Controller.Review is null
            ? ShortCubeLabels(cube)
            : null;

    /// <summary>
    /// What the row's pills render with, as <c>ShortLabels</c>: the rule
    /// applied to the row's last reported width.
    /// </summary>
    private bool ShortCubeLabels(CubeDecision cube) => CubeLabelsAbbreviate(_actionRowWidth, cube);

    /// <summary>
    /// <b>Whether the cube pills take their short labels</b>
    /// (<c>SPEC-quiz-view.md</c> §4, "The action row under quiz navigation":
    /// "Cube labels abbreviate only when the row cannot fit them", the
    /// switch-over measured, never guessed): when the action row is narrower
    /// than the full-label row needs at <paramref name="decision"/>. With no
    /// width reported yet the labels stay full, the producer's default.
    ///
    /// <para>
    /// <b>It keys on the row's own width</b>, as the browser reports it,
    /// rather than the viewport's: folding the navigation panel widens the
    /// row at a fixed viewport (by 250 px at 1280), and only the row's width says what
    /// the row can hold in both fold states. <b>And on the fourth answer's
    /// reading</b>, because its label is the one that differs between
    /// decisions: Too good where gammons are possible, No double / Pass where
    /// they are not (<see cref="CubeDecision.ClaimOf"/>), the decision's
    /// reading as the label home renders it. No label is spelled here.
    /// </para>
    /// </summary>
    internal static bool CubeLabelsAbbreviate(double? actionRowWidth, CubeDecision decision) =>
        actionRowWidth is { } width && width < FullCubeRowWidth(decision);

    /// <summary>
    /// The narrowest action row that holds the full-label cube row at
    /// <paramref name="decision"/>, its fourth answer read as the decision
    /// reads it: <see cref="FullCubeRowWidthTooGood"/> or
    /// <see cref="FullCubeRowWidthNoDoublePass"/>.
    /// </summary>
    private static double FullCubeRowWidth(CubeDecision decision) =>
        decision.ClaimOf(CubeAnswer.NoDoublePass) == CubeClaim.TooGood
            ? FullCubeRowWidthTooGood
            : FullCubeRowWidthNoDoublePass;

    /// <summary>
    /// The narrowest action row, in CSS pixels, that holds the cube row with
    /// its full labels when the fourth reads <b>No double / Pass</b> — the
    /// longest set. Measured, not computed (leg 4 of
    /// <c>halheinrich/backgammon#8</c>, 2026-10-02, published app, Chromium,
    /// Windows, the app's Helvetica/Arial stack): the leading controls at
    /// their widest selection, the No double / Pass pill bolded — the four
    /// pills 476.5, Submit 101.8 and the navigation group 148.0 with their
    /// gaps, 742.3 in all — then the row's 8 px gap, then the trailing
    /// cluster at the floor of its shrink order, 251.0 (the XGID's copy button
    /// with its gap, Show stats and End quiz, with the cluster's gaps; the
    /// locator chip gives up all of its width there). One pixel narrower and
    /// the cluster runs over the navigation buttons. It is a measurement of
    /// that stack: a wider font needs a wider row, so this number is
    /// re-measured whenever anything in the row is restyled or relabelled.
    /// </summary>
    internal const double FullCubeRowWidthNoDoublePass = 1001.3;

    /// <summary>
    /// The same measurement with the fourth reading <b>Too good</b>: the
    /// leading controls at their widest, the Too good pill bolded — the four
    /// pills 419.3 — 685.1 in all, then the 8 px gap and the 251.0 cluster.
    /// See <see cref="FullCubeRowWidthNoDoublePass"/> for what was measured
    /// and what it holds for.
    /// </summary>
    internal const double FullCubeRowWidthTooGood = 944.1;

    /// <summary>
    /// What a Space press does, once the browser has found it eligible
    /// (<c>halheinrich/backgammon#149</c> as amended by
    /// <c>halheinrich/backgammon#200</c>; <c>SPEC-quiz-history.md</c> §2, with
    /// Hal's clarification of 2026-10-02): what the Submit button does when it
    /// is lit, and otherwise what ▶ does — Continue on the solution view, Skip
    /// or Next while answering; nothing when no button would act. It adds no
    /// condition and no action of its own — each branch is a button's gate
    /// guarding that button's method — so the key cannot enable what a button
    /// shows disabled, nor do anything a click would not. The order of the two
    /// branches is the ruling itself, not a condition of the key's: Submit is
    /// lit only where ▶ is too, and the rule says Submit first.
    ///
    /// <para>
    /// <b>It renders as a click does.</b> Blazor re-renders a component after
    /// every UI event its markup handles — once the handler's synchronous part
    /// has run, and again when its task completes — so a button's method may
    /// change page state without asking for a render. A JS-invoked callback is
    /// not a UI event and gets neither render, so this one asks for both
    /// itself. That is the framework's half of pressing a button, not an action
    /// of the key's: without it a Space submit left the mix composition notice
    /// on screen, since <see cref="SubmitAsync"/> retires the notice after the
    /// controller's own state change has already rendered (pinned by
    /// <c>Quiz_Space_AndTheSubmitButton_ShareOneAction</c>).
    /// </para>
    ///
    /// Public and <see cref="JSInvokableAttribute"/> because the module invokes
    /// it by name through the <see cref="DotNetObjectReference{TValue}"/> the
    /// attach handed over; nothing else calls it. Eligibility of the press
    /// itself (key, modifiers, repeat, focus) was settled in the browser
    /// before this runs.
    /// </summary>
    [JSInvokable]
    public async Task HandleSpaceKeyAsync()
    {
        var pressing =
            CanSubmit ? SubmitAsync()
            : CanGoNext ? NextAsync()
            : Task.CompletedTask;

        StateHasChanged();
        await pressing;
        StateHasChanged();
    }

    /// <summary>
    /// <b>The one gate on Submit</b>, read by both Submit buttons and by
    /// <see cref="HandleSpaceKeyAsync"/>: the page is
    /// answering (no review to read), the controller is not mid-transition,
    /// and a complete answer is latched — the play from
    /// <see cref="HandlePlayCompleted"/> or the cube answer from the radios'
    /// <c>@bind-Value</c>. The two latches are mutually exclusive per problem
    /// (only one answer instrument renders, and both clear on every
    /// transition), so "either is set" is "this problem's answer is complete"
    /// without the gate needing to know the kind.
    /// </summary>
    private bool CanSubmit =>
        Controller.Review is null
        && !Controller.IsBusy
        && (_completedCube is not null || _completedPlay is not null);

    /// <summary>
    /// <b>The one gate on Continue</b>, the review state's primary button:
    /// there is a review to leave and the controller is not busy. Continue does
    /// what ▶ does there (<see cref="NextAsync"/>), so the two are lit together
    /// on the solution view; Space reaches the same method through
    /// <see cref="CanGoNext"/>.
    /// </summary>
    private bool CanContinue =>
        Controller.Review is not null && !Controller.IsBusy;

    /// <summary>
    /// <b>The one gate on ⏮ and ◀</b>, which share it: there is an earlier
    /// problem (<see cref="QuizController.CanGoBack"/>, the run's fact) and the
    /// controller is not busy (SPEC-quiz-history.md §2).
    /// </summary>
    private bool CanGoBack =>
        Controller.CanGoBack && !Controller.IsBusy;

    /// <summary>
    /// <b>The one gate on ▶</b>, read by the button and by
    /// <see cref="HandleSpaceKeyAsync"/>: a problem is on screen and the
    /// controller is not busy — ▶ is unavailable only while busy
    /// (SPEC-quiz-history.md §2), in both view states.
    /// </summary>
    private bool CanGoNext =>
        Controller.Current is not null && !Controller.IsBusy;

    /// <summary>
    /// <b>The one gate on ⏭</b>: the problem on screen is behind the frontier
    /// (<see cref="QuizController.CanGoToLast"/>, the run's fact) and the
    /// controller is not busy (SPEC-quiz-history.md §2).
    /// </summary>
    private bool CanGoToLast =>
        Controller.CanGoToLast && !Controller.IsBusy;

    /// <summary>The accessible name of the navigation buttons' group.</summary>
    internal const string NavigationGroupName = "Problems";

    /// <summary>⏮'s accessible name and tooltip (SPEC-quiz-history.md §2).</summary>
    internal const string GoToFirstName = "Go to first";

    /// <summary>◀'s accessible name and tooltip.</summary>
    internal const string BackName = "Back";

    /// <summary>▶'s accessible name and tooltip where a press adds nothing to the skip count.</summary>
    internal const string NextName = "Next";

    /// <summary>▶'s accessible name and tooltip where a press would add to the skip count.</summary>
    internal const string SkipName = "Skip";

    /// <summary>⏭'s accessible name and tooltip.</summary>
    internal const string GoToLastName = "Go to last";

    /// <summary>
    /// ▶'s accessible name and tooltip: Skip wherever a press would add to the
    /// skip count, Next otherwise, so the icon never hides a counted skip
    /// (SPEC-quiz-history.md §2). Which applies is the run's rule, read off the
    /// controller (<see cref="QuizController.NextAddsToSkipCount"/>) and never
    /// rebuilt here from the cursor, the frontier and a disposition.
    /// </summary>
    private string NextButtonName =>
        Controller.NextAddsToSkipCount ? SkipName : NextName;

    /// <summary>
    /// The side this problem's board renders on, for <b>every</b> branch below.
    /// The rule that composes the user's two side settings lives in one member
    /// (<see cref="QuizSettings.EffectiveHomeBoardOnRight"/>) and reaches the
    /// renderer through this one property, so the answering branches and the
    /// solution branch cannot disagree — a board that flipped in some views but
    /// not others would read as a bug, and it is the kind that survives review by
    /// looking like three correct call sites.
    /// </summary>
    private bool HomeBoardOnRight =>
        Settings.EffectiveHomeBoardOnRight(Controller.RandomHomeBoardOnRight);

    /// <summary>
    /// <b>The view mode, derived — never stored.</b> True when the page is in
    /// SPEC-quiz-view.md §4's <i>maximized answering</i> composition: the user
    /// asked for the maximize mode <i>and</i> there is no review to read. That is
    /// the whole state machine this feature adds, which is to say none:
    /// <c>mode = f(the setting, answering | review)</c>, re-derived on every
    /// render from two facts that already exist.
    ///
    /// <para>
    /// <b>No holder, no page field, no "currently maximized" bit</b> (§6). A
    /// second copy of the mode is a divergence from the model rather than an
    /// implementation detail — it is the thing that would let the chrome and the
    /// canvas disagree about which composition is on screen, and the thing a
    /// navigation round trip could then desynchronize.
    /// </para>
    ///
    /// <para>
    /// Every consequence reads this one member: the markup suppresses the score
    /// panel and the status strip on it, and <see cref="BoardOptions"/> picks the
    /// canvas from it. The transitions fall out with no special cases — Submit
    /// sets <see cref="QuizController.Review"/> and the page normalizes; every
    /// landing clears it, whichever control lands it, and the page
    /// re-maximizes; Undo never leaves the answering state, so it changes
    /// nothing.
    /// </para>
    ///
    /// <para>
    /// Notices are deliberately <b>not</b> gated on this: they render in both
    /// modes and are dismissed by the user instead (§4's notices ruling). The mix
    /// notice retires on the first answer, so a mode that suppressed it while
    /// answering would mean it is never seen at all; the stats notices report
    /// degraded recording, which must be seen.
    /// </para>
    /// </summary>
    private bool MaximizedAnswering =>
        Settings.MaximizeBoardWhileAnswering && Controller.Review is null;

    /// <summary>
    /// The canvas every board branch renders against — the
    /// <see cref="HomeBoardOnRight"/> pattern applied to the second thing all
    /// three branches must agree about. One place decides, so play answering,
    /// cube answering, and the solution cannot disagree; three correct-looking
    /// call sites is exactly how that class of bug survives review.
    ///
    /// <para>
    /// It is also the <see cref="BoardOnlyCanvas"/> safety property, stated
    /// structurally rather than as a check: <see cref="MaximizedAnswering"/>
    /// requires <see cref="QuizController.Review"/> to be null, and the review
    /// branch is exactly the branch that renders a
    /// <see cref="DiagramMode.Solution"/> request — so the preset the producer
    /// throws on can never reach the request it throws for.
    /// </para>
    /// </summary>
    private DiagramOptions BoardOptions => MaximizedAnswering ? BoardOnlyCanvas : FullCanvas;

    /// <summary>
    /// The answering request — for the cube's board-only diagram and the play
    /// entry alike: the decision's own request under the run's ranking, in
    /// <see cref="DiagramMode.Problem"/> (the request's default), which hides
    /// the analysis panel, since the candidate list is the answer the quiz is
    /// grading. The entry draws its working board from it
    /// (<see cref="DiagramRequest.WithWorkingBoard"/>, the entry's own doing),
    /// with the decision's presentation.
    /// </summary>
    private DiagramRequest BuildRenderRequest(BgDecisionData current) =>
        DiagramRequest.ForDecision(current, Controller.Ranking) with
        {
            HomeBoardOnRight = HomeBoardOnRight,
        };

    /// <summary>
    /// Build the review-state solution request: the answered decision's own
    /// request under the run's ranking, with the filled analysis panel
    /// (<see cref="DiagramMode.Solution"/>).
    /// <para>
    /// For a checker play the primary <c>*</c> marks the <em>.xg-recorded played
    /// move</em> — the record's own, which the request reads — and the
    /// secondary <c>†</c> marks the <em>quiz user's answer</em>,
    /// <see cref="DiagramRequest.SecondaryPlayIndex"/>, set from the review's
    /// <see cref="ProblemReview.Play.CandidateIndex"/>. The producer suppresses
    /// the <c>†</c> when it coincides with the recorded play, and an off-list
    /// answer — no candidate, so no index — draws no <c>†</c> at all. A cube
    /// review sets no mark (see the type's remarks).
    /// </para>
    /// <para>
    /// Both answer kinds carry the hidden-depth ceiling, assigned
    /// unconditionally rather than behind a branch: untouched, its value is the
    /// producer's own default (null), which the producer defines as hiding
    /// nothing — so passing the default IS passing nothing, and there is no
    /// "leave it alone" path that could drift from the "set it" one. The
    /// ceiling means itself — the user picks a level off the producer's own
    /// ladder and it travels here unchanged, which is the point of the
    /// producer's inclusive-hide shape (halheinrich/backgammon#66). The order
    /// is the run's ranking, which <see cref="BuildRenderRequest"/> states too,
    /// though its <see cref="DiagramMode.Problem"/> panel draws no list.
    /// </para>
    /// </summary>
    private DiagramRequest BuildSolutionRequest(BgDecisionData current, ProblemReview review)
    {
        var request = BuildRenderRequest(current) with
        {
            Mode = DiagramMode.Solution,
            MaximumHiddenCandidateAnalysisLevel = Settings.MaximumHiddenCandidateAnalysisLevel,
        };

        return review is ProblemReview.Play play
            ? request with { SecondaryPlayIndex = play.CandidateIndex }
            : request;
    }

    /// <summary>
    /// What a practice review's verdict begins with:
    /// <c>SPEC-quiz-history.md</c> §3's words, "Practice — " (Hal, 2026-10-02),
    /// followed by the verdict as a live review would show it.
    /// </summary>
    internal const string PracticePrefix = "Practice — ";

    /// <summary>
    /// Compact verdict line summarizing the just-scored answer, marked when the
    /// submission was practice.
    ///
    /// <para>
    /// <b>Why practice is marked.</b> A practice submission is scored and shown
    /// like any other but changes nothing — not the score panel a few lines
    /// below on this same page, not Done, not the lifetime record — so an
    /// unbadged "Correct" beside a score that does not move reads as a bug
    /// rather than as the model working. <c>SPEC-quiz-history.md</c> §3 rules
    /// the mark: the verdict begins <see cref="PracticePrefix"/>. It is short
    /// on purpose — the longer clause it replaced hid the end of the verdict in
    /// the desktop band (halheinrich/backgammon#329). Everything else about the
    /// review is untouched: the verdict wording, <see cref="StatusVerdictColor"/>'s
    /// outcome colouring and the diagram's markers are the same, because the
    /// retry's score is what the user came back to see.
    /// </para>
    ///
    /// <para>
    /// It rides in the band's existing text rather than as a badge or a third
    /// strip line: <c>.status-strip</c> is a fixed-height contract (board size
    /// depends on it), and text clamps inside that where a new element would
    /// have to be argued not to grow it.
    /// </para>
    /// </summary>
    private static string VerdictText(ProblemReview review) =>
        review.IsPractice
            ? PracticePrefix + ScoredVerdict(review)
            : ScoredVerdict(review);

    /// <summary>The scored half of <see cref="VerdictText"/>, per answer kind.</summary>
    private static string ScoredVerdict(ProblemReview review) => review switch
    {
        ProblemReview.Play play => PlayVerdict(play),
        ProblemReview.Cube cube => CubeVerdict(cube),
        _ => string.Empty,
    };

    /// <summary>
    /// The review of a play the run's ranking does not score —
    /// <c>SPEC-scoring.md</c> §2a's text, verbatim (ruled 2026-09-26 on
    /// <c>halheinrich/backgammon#282</c>). Only depth first leaves a candidate
    /// unscored, which is why the sentence may name its ranking.
    /// </summary>
    internal const string NotScoredVerdict =
        "Not scored under depth-first ranking: this play was analyzed less deeply than the best play, and at that depth it rated higher.";

    /// <summary>
    /// The checker-play verdict, one per outcome of the producer's scoring
    /// (<see cref="PlaySubmission"/>): correct, or not best with its error; not
    /// scored, in the ruled words; or off the list.
    ///
    /// <para>
    /// <b>Correct says only that</b> (SPEC-scoring.md §2a, Hal, 2026-10-02 on
    /// halheinrich/backgammon#326: "Correct."). Whether a play is correct is
    /// the producer's <see cref="SubmittedPlay.IsCorrect"/>, which is true when
    /// its error shows as <c>0.0000</c>; a correct play need not be the best
    /// one, so the line does not say it was. The error a wrong play shows is
    /// written by <see cref="EquityDisplay.FormatLoss"/>, the one display of a
    /// loss, so what is shown and what is judged cannot disagree.
    /// </para>
    ///
    /// <para>
    /// <b>Off the list, it names the play</b> (<c>halheinrich/backgammon#274</c>:
    /// "When a play is not on the list, show what that play is"). The play is
    /// spelled by the producer's one notation,
    /// <see cref="BgDataTypes_Lib.Play.ToNotation"/> — the formatter the
    /// candidate list above is written by — so the reader can compare the two
    /// at a glance and see what the app read their clicks as. It is named in
    /// the verdict and nowhere else: marking it on the diagram, or listing it
    /// among the candidates, is a separate ruling.
    /// </para>
    /// </summary>
    private static string PlayVerdict(ProblemReview.Play review) => review.Submission.Match(
        scored: submitted => submitted.IsCorrect
            ? "Correct."
            : $"Not best — your play lost {EquityDisplay.FormatLoss(submitted.EquityLoss)} equity. The best play is shown above.",
        notScored: _ => NotScoredVerdict,
        offList: () =>
            $"Off list — your play, {review.UserPlay.ToNotation()}, wasn't among the analyzed candidates. The best play is shown above.");

    /// <summary>
    /// What separates the answers of the cube verdict's Best list: a comma,
    /// as the solution diagram's own Best line separates them.
    /// </summary>
    private const string BestListSeparator = ", ";

    /// <summary>
    /// The cube verdict: one line, judging the whole answer
    /// (SPEC-scoring.md §3, "The tie"; Hal, 2026-10-02 on
    /// halheinrich/backgammon#326: "Yes, one line like that") —
    /// <c>Correct — Double / Take.</c>, or
    /// <c>Not best — Double / Take lost 0.1234. Best: Double / Pass.</c>
    ///
    /// <para>
    /// <b>Everything in it is read off the review's one scored answer and the
    /// decision it was scored at</b> (<see cref="ProblemReview.Cube"/>), and
    /// nothing is decided here:
    /// <list type="bullet">
    ///   <item>whether the answer is correct is the producer's
    ///   <see cref="SubmittedCubeAnswer.IsCorrect"/>, judged on the whole
    ///   cost, so an answer whose two parts each show as <c>0.0000</c> can
    ///   still read Not best;</item>
    ///   <item>each answer is named by its label at that decision
    ///   (<see cref="CubeLabels.Label(CubeAnswer, CubeDecision)"/>), so the
    ///   fourth answer reads Too good or No double / Pass as the decision
    ///   reads it;</item>
    ///   <item>the loss is the whole answer's cost, written by
    ///   <see cref="EquityDisplay.FormatLoss"/>;</item>
    ///   <item>the Best list is the decision's
    ///   <see cref="CubeDecision.ZeroCostAnswers"/>, in its order — the very
    ///   set the diagram's Best line lists, so at a tie it names every
    ///   answer that costs nothing (halheinrich/backgammon#293).</item>
    /// </list>
    /// The two parts of the cost are not named: they are the Double and Take
    /// rows' diagnostics, and No double's implied take is never charged, so it
    /// is never presented as an assessed take.
    /// </para>
    /// </summary>
    private static string CubeVerdict(ProblemReview.Cube review)
    {
        var submission = review.Submission;
        var decision = review.Decision;
        var label = CubeLabels.Label(submission.Answer, decision);
        if (submission.IsCorrect)
            return $"Correct — {label}.";

        var best = string.Join(
            BestListSeparator,
            decision.ZeroCostAnswers.Select(answer => CubeLabels.Label(answer, decision)));
        return $"Not best — {label} lost {EquityDisplay.FormatLoss(submission.Cost.Total)}. Best: {best}.";
    }

    /// <summary>
    /// Legend for the solution diagram's play markers, listing only the markers
    /// actually drawn: <c>*</c> the .xg-recorded played move (present when the
    /// decision records a play) and <c>†</c> the quiz answer (present only when
    /// it is a candidate and differs from the recorded play — the same
    /// suppression the renderer applies to <see cref="DiagramRequest.SecondaryPlayIndex"/>).
    /// Returns <c>null</c> when no play marker shows (cube reviews, or a play
    /// review with neither a recorded move nor a distinct listed answer).
    /// </summary>
    private static string? SolutionLegend(ProblemReview review, BgDecisionData decision)
    {
        if (review is not ProblemReview.Play play || decision is not CheckerPlayDecision checkerPlay)
            return null;

        var recorded = checkerPlay.Decision.UserPlayIndex;
        var parts = new List<string>(2);
        if (recorded is not null)
            parts.Add("* played");
        if (play.CandidateIndex is int answer && answer != recorded)
            parts.Add("† your answer");

        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }

    /// <summary>
    /// Text for the status strip's verdict band: the scored verdict at review,
    /// a neutral state-appropriate prompt while answering. The strip renders at
    /// a fixed height (see <c>.status-strip</c> in <c>app.css</c>) so chrome
    /// height, and therefore board size, is invariant across states and
    /// questions <i>within a view mode</i>; only the content swaps. Under
    /// <see cref="MaximizedAnswering"/> the strip — this prompt included — is
    /// not rendered at all, which is the one place that invariance is
    /// deliberately crossed.
    /// </summary>
    private static string StatusText(ProblemReview? review, BgDecisionData decision) =>
        review is not null
            ? VerdictText(review)
            : decision.Match(
                checkerPlay: _ => "Click the board to build your play, then Submit.",
                cube: _ => "Pick the cube decision, then Submit.");

    /// <summary>
    /// Bootstrap alert colour for the status strip's verdict band: outcome
    /// colouring at review, a quiet neutral tone while answering. A scored
    /// answer is coloured by the same fact its verdict states — the producer's
    /// <c>IsCorrect</c>, for a cube answer judged on the whole cost. The two
    /// unscored play outcomes share the warning tone: each is a skip of record
    /// (SPEC-scoring.md §2 and §2a), neither right nor wrong.
    /// </summary>
    private static string StatusVerdictColor(ProblemReview? review) => review switch
    {
        null => "alert-secondary",
        ProblemReview.Play play => play.Submission.Match(
            scored: submitted => submitted.IsCorrect ? "alert-success" : "alert-danger",
            notScored: _ => "alert-warning",
            offList: () => "alert-warning"),
        ProblemReview.Cube cube => cube.Submission.IsCorrect ? "alert-success" : "alert-danger",
        _ => "alert-secondary",
    };

    /// <summary>
    /// Dismiss the composition notice for <paramref name="composition"/> — the
    /// gesture half of a retirement the first submitted answer also performs (see
    /// <see cref="SubmitAsync"/>). Either gesture ends it, and both record the same
    /// dismissal against the same key, so there is no ordering between them to
    /// get wrong.
    /// </summary>
    private void DismissComposition(BgGame_Lib.MixComposition composition) =>
        Notices.Dismiss(QuizNotice.Composition, composition);

    private void HandlePlayCompleted(Play play)
    {
        _completedPlay = play;
        StateHasChanged();
    }

    /// <summary>
    /// Submit — the Submit buttons' action, the answering dice click's for a
    /// checker play (<c>OnSubmitRequested</c>), and the spacebar's while Submit
    /// is lit (<see cref="HandleSpaceKeyAsync"/>, <c>halheinrich/backgammon#200</c>
    /// as amended 2026-09-24). The one owner of what submitting does, the
    /// composition notice's retirement included: the key calls this method
    /// rather than the controller, so a change here reaches the key and the
    /// buttons together.
    ///
    /// <para>
    /// The task completes when the controller has written the answer to the
    /// lifetime record (<see cref="QuizController.SubmitPlayAsync"/>); the
    /// review is on screen, its controls busy, before that.
    /// </para>
    /// </summary>
    private async Task SubmitAsync()
    {
        // Route by which answer is latched. The current decision's kind
        // determines which entry component rendered and therefore which latch
        // is set; the latches are mutually exclusive per problem. The relevant
        // latch is cleared by HandleStateChanged.
        var submitting =
            _completedCube is { } cube ? Controller.SubmitCubeAnswerAsync(cube)
            : _completedPlay is { } play ? Controller.SubmitPlayAsync(play)
            : Task.CompletedTask;

        // The first answer retires the mix composition notice — it described how
        // this quiz was built, which the user has now read and acted on. Gated on
        // Review having been set rather than on having called a Submit: both
        // controller mutators no-op under the transition gate (a Submit landing
        // inside a pending ▶), and dismissing on a call that scored
        // nothing would drop the notice without the user ever answering. Review
        // non-null is the proof, and it covers an off-list play too — that is a
        // submitted answer with a review to read, just an unscored one. Moving
        // on with ▶ is deliberately not a dismissal: it passes a problem without
        // answering it, so the composition is still the thing the user hasn't
        // engaged with.
        //
        // Read before the write is awaited: the controller puts the review up
        // before its call returns, so the notice goes in the same render as the
        // review arrives rather than once the write has landed.
        if (Controller.Review is not null && Controller.LastComposition is { } comp)
        {
            DismissComposition(comp);
        }

        await submitting;
    }

    /// <summary>
    /// ▶ — the ▶ button's action, the Continue button's (Continue is ▶ on the
    /// solution view), the review dice click's, and the spacebar's whenever
    /// Submit is dark (<see cref="HandleSpaceKeyAsync"/>). The one owner of what
    /// moving on does: the key calls this method rather than the controller,
    /// so a change here reaches the key and the buttons together.
    /// </summary>
    private Task NextAsync() => Controller.NextAsync();

    /// <summary>⏮ — the button's action.</summary>
    private void GoToFirst() => Controller.GoToFirst();

    /// <summary>◀ — the button's action.</summary>
    private void GoBack() => Controller.GoBack();

    /// <summary>⏭ — the button's action.</summary>
    private void GoToLast() => Controller.GoToLast();

    /// <summary>
    /// End the run here and go to the summary (issue halheinrich/backgammon#57). One click, acting
    /// immediately: the confirmation the issue first sketched was ruled out, so
    /// the only thing standing between a stray click and a finished quiz is
    /// where the button sits — the far end of the action row, past Show stats.
    ///
    /// <para>
    /// No navigation of its own. <see cref="QuizController.EndQuizAsync"/> flips
    /// <see cref="QuizController.IsFinished"/>, and this page already redirects
    /// to <c>/done</c> on that in <see cref="HandleStateChanged"/> — the same
    /// route a run that reaches its last problem takes, which is what makes an
    /// early end land as an ordinary finish rather than a second kind of ending.
    /// </para>
    /// </summary>
    private async Task EndQuizAsync()
    {
        await Controller.EndQuizAsync();
    }

    /// <summary>
    /// Roll back the last committed move in the entry being assembled.
    ///
    /// <para>
    /// <b>Enabled whenever the controller isn't busy</b> — deliberately not
    /// gated on <see cref="_playEntry"/> being assigned. Blazor assigns an
    /// <c>@ref</c> only <i>after</i> the render that creates the component, so a
    /// <c>_playEntry is null</c> term made the answering branch's first render
    /// disable both Undo buttons; and because
    /// <see cref="BackgammonPlayEntry"/> raises no callback until the play is
    /// complete, nothing re-rendered this page during assembly to re-evaluate
    /// it. The buttons therefore stayed disabled for the entire entry and
    /// enabled only at <see cref="HandlePlayCompleted"/> — exactly when Undo
    /// stops being wanted. (The symptom looked intermittent because Blazor never
    /// nulls a component ref on unmount: from the second play problem onward the
    /// stale-but-non-null ref rendered them enabled. It returned on the first
    /// play problem of a run and after every <c>Show stats</c> round trip, which
    /// re-instantiates this page.) Nothing about write capability was ever
    /// involved, despite where it was first observed.
    /// </para>
    ///
    /// <para>
    /// Dropping the term is safe on both counts the branch already settles: the
    /// enclosing checker-play branch guarantees an entry is rendered, and a
    /// click can only arrive after that render assigned the ref. Undo on an
    /// entry with nothing entered is a documented no-op in the producer, so
    /// always-enabled is honest rather than a promise the click discovers is
    /// empty. Enabled-<i>iff</i>-undoable would be more honest still, but it
    /// needs two producer surfaces <see cref="BackgammonPlayEntry"/> does not
    /// expose — a <c>CanUndo</c> predicate <i>and</i> a per-click change
    /// notification, without which any predicate read here is stale from the
    /// first render. That is booked as a producer change, not worked around
    /// here.
    /// </para>
    /// </summary>
    private void UndoLast()
    {
        _playEntry?.UndoLast();
        // The component doesn't notify us of internal undos; the latched
        // completed play is no longer valid post-undo.
        _completedPlay = null;
    }

    /// <summary>
    /// Restore the entry's initial position. Same enablement rule and rationale
    /// as <see cref="UndoLast"/>.
    /// </summary>
    private void UndoAll()
    {
        _playEntry?.UndoAll();
        _completedPlay = null;
    }

    private void ShowStats()
    {
        Nav.NavigateTo("/stats");
    }

    /// <summary>
    /// Tear-down, in the order the dependencies run: unsubscribe from
    /// <see cref="QuizController.StateChanged"/> and
    /// <see cref="QuizStatsStore.StatusChanged"/> so a navigated-away instance
    /// stops re-rendering; then detach the keyboard listener, stop the row's
    /// observer and release both modules, and only then dispose the
    /// <see cref="DotNetObjectReference{TValue}"/> they were calling back
    /// through — the reference must outlive the last thing that could invoke
    /// it. <see cref="_disposed"/> covers an import still in flight (see
    /// <see cref="ImportModulesAsync"/>). Nothing here can race an attach or an
    /// observe: WebAssembly runs the JS of an interop call synchronously, so
    /// one whose await is pending has already executed in the browser.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        Controller.StateChanged -= HandleStateChanged;
        StatsStore.StatusChanged -= HandleStatsStatusChanged;

        if (_keys is not null)
        {
            await _keys.InvokeVoidAsync("detach");
            await _keys.DisposeAsync();
            _keys = null;
        }
        if (_rowWidth is not null)
        {
            await _rowWidth.InvokeVoidAsync("unobserve");
            await _rowWidth.DisposeAsync();
            _rowWidth = null;
        }
        _self?.Dispose();
        _self = null;
    }
}
