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
/// <b>Show stats.</b> The Show stats icon button, present in both the answering
/// and review states (in the action row's trailing cluster, before End quiz),
/// navigates to <c>/stats</c> — a read-only, live view of the same
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
/// <see cref="HandleRowFit"/>, carries the row-fit module's
/// measurements of the tail's and the cube pills' form, and is released the
/// same way).
/// Attached, the module sets
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
    /// The row-fit module: it measures the action row and its ruler under the
    /// fonts actually rendering, folds the navigation panel by itself where the
    /// row cannot fit beside it, and reports whether the tail fits beside the
    /// row's other controls and whether the cube pills' full form fits
    /// (<see cref="HandleRowFit"/>). Imported beside
    /// <see cref="KeysModulePath"/> and internal for the same reason.
    /// </summary>
    internal const string RowFitModulePath = "./js/actionRowFit.js";

    private BackgammonPlayEntry? _playEntry;
    private Play? _completedPlay;
    private CubeAnswer? _completedCube;

    /// <summary>The imported keyboard module; null until the first render's import lands.</summary>
    private IJSObjectReference? _keys;

    /// <summary>The imported row-fit module; null until the first render's import lands.</summary>
    private IJSObjectReference? _rowFit;

    /// <summary>The reference both modules call back through; created once both are imported, disposed with the page.</summary>
    private DotNetObjectReference<Quiz>? _self;

    /// <summary>The action row, observed by the row-fit module whenever Blazor creates it.</summary>
    private ElementReference _actionRow;

    /// <summary>The row-fit ruler beside it (Quiz.razor), observed with it.</summary>
    private ElementReference _actionRowRuler;

    /// <summary>
    /// The <see cref="ElementReference.Id"/> of the row under observation, so
    /// a render that keeps the row asks for nothing and one that creates it
    /// afresh (a new quiz after the last one ended) observes the new element.
    /// </summary>
    private string? _observedRow;

    /// <summary>
    /// The row-fit module's last report (<see cref="HandleRowFit"/>), or null
    /// before its first: the one fact <see cref="RowFitPending"/>,
    /// <see cref="TailFolded"/> and <see cref="ShortCubeLabels"/> are read
    /// from.
    /// </summary>
    private (bool TailFits, bool FullCubeLabelsFit)? _rowFitReport;

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
    /// shortcut, render the board and its row now that the row can be fitted
    /// (<see cref="RowFitReady"/>), and from then on keep the row under
    /// observation.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await ImportModulesAsync();
            if (RowFitReady) StateHasChanged();
            return;
        }
        await ObserveActionRowAsync();
    }

    /// <summary>
    /// Whether the row-fit module is in, which is when the page renders its
    /// board and row (Quiz.razor): no row is shown that nothing is fitting. The
    /// module's first measurement follows the row's first render in that
    /// frame's animation callbacks, before it paints, and until it lands the
    /// row is in its pending presentation (<see cref="RowFitPending"/>), which
    /// covers nothing. Before the module is in, the page shows its notices
    /// only: a first load's module fetch is the one wait this adds.
    /// </summary>
    private bool RowFitReady => _rowFit is not null;

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
        var rowFit = await JS.InvokeAsync<IJSObjectReference>("import", RowFitModulePath);
        if (_disposed)
        {
            await keys.DisposeAsync();
            await rowFit.DisposeAsync();
            return;
        }

        _keys = keys;
        _rowFit = rowFit;
        _self = DotNetObjectReference.Create(this);
        // The callback's name and the readiness mark's name travel with the
        // reference so each is spelled exactly once, on this side; the module
        // never restates either.
        await _keys.InvokeVoidAsync(
            "attach", _self, nameof(HandleSpaceKeyAsync), QuizKeysMark.AttachedAttribute);
    }

    /// <summary>
    /// Keep the row-fit module measuring the row. It is handed the action row
    /// and its ruler whenever a render created the row: the first render that
    /// has one, and the row of a new quiz after the last one ended. The
    /// recorded id is set before the await, so a render landing during it
    /// repeats nothing. From then on the module follows the row and the ruler
    /// on its own (resizes, the panel's fold, fonts loading). Nothing else asks
    /// it to measure: the ruler holds the same lines for every problem and
    /// every state (Quiz.razor), so a new problem, or a move to its review,
    /// changes nothing the module measures.
    /// </summary>
    private async Task ObserveActionRowAsync()
    {
        if (_rowFit is null || _self is null || Controller.Current is null) return;
        if (_actionRow.Id == _observedRow) return;

        _observedRow = _actionRow.Id;
        await _rowFit.InvokeVoidAsync(
            "observe", _actionRow, _actionRowRuler, _self, nameof(HandleRowFit));
    }

    /// <summary>
    /// The row-fit module's report, two facts measured together against the
    /// row as it stands after the panel's fold, both from the one budget:
    /// whether the tail at full size fits beside the row's other controls
    /// (<paramref name="tailFits"/>), and whether the full-form pills fit
    /// beside it at their widest — both readings of the fourth answer, every
    /// selection (<paramref name="fullCubeLabelsFit"/>). Neither depends on the
    /// problem on screen. The first report ends the pending presentation. It
    /// renders only when the report changes what is on screen. Public and
    /// <see cref="JSInvokableAttribute"/> for the module's sake, as
    /// <see cref="HandleSpaceKeyAsync"/> is; nothing else calls it.
    /// </summary>
    [JSInvokable]
    public void HandleRowFit(bool tailFits, bool fullCubeLabelsFit)
    {
        var before = (RowFitPending, ShortCubeLabels, TailFolded);
        _rowFitReport = (tailFits, fullCubeLabelsFit);
        if ((RowFitPending, ShortCubeLabels, TailFolded) != before) StateHasChanged();
    }

    /// <summary>
    /// <b>Whether the row is waiting for its first measurement</b>
    /// (<c>SPEC-quiz-view.md</c> §4, "One budget from the outset": "No control
    /// is covered at any moment, the first render before any measurement
    /// included"). Until the row-fit module first reports, the row is in its
    /// pending presentation, the narrowest the page has: the tail behind its
    /// "⋯" (<see cref="TailFolded"/>), the pills short
    /// (<see cref="ShortCubeLabels"/>), and the row marked
    /// <c>data-nav-fold-pending</c>, on which the layout folds the navigation
    /// panel (<c>MainLayout.razor.css</c>), so the row has the width the panel
    /// would take. A 641 px window measures to that same presentation, so there
    /// the measurement changes nothing; in a wider window it opens what fits,
    /// in the same frame (<c>wwwroot/js/actionRowFit.js</c> measures in the
    /// frame's animation callbacks, before it paints). Every new page starts
    /// pending: a Show-stats round trip re-creates the page.
    /// </summary>
    private bool RowFitPending => _rowFitReport is null;

    /// <summary>
    /// <b>Whether the action row's tail folds behind its "⋯"</b>
    /// (<see cref="TailMenu"/>; <c>SPEC-quiz-view.md</c> §4,
    /// halheinrich/backgammon#264's widened fourth, Hal, 2026-10-03: "below the
    /// width where the tail fits beside the row's other controls, measured
    /// live, the whole tail folds behind one "⋯" control at the row's far
    /// end"). The measurement is the row-fit module's, under the fonts
    /// actually rendering: the tail folds where the row, after the panel's own
    /// fold, is narrower than the budget the panel folds by — the widest
    /// answer row on the ruler, the gap and the tail's floor. So it is one
    /// width for every state and every problem kind, as the panel's is: the
    /// row's composition never changes between answering and review or from
    /// one problem to the next, only with the window. It is computed from the
    /// tail at full size (the ruler's tail line), never from the presentation
    /// showing, so showing the "⋯" cannot make the row fit and switch it back.
    /// Where there is no side panel (the phone layout, below 641 px) the tail
    /// takes a line of its own instead (halheinrich/backgammon#236), and it
    /// never folds. <b>Folded until measured</b> (<see cref="RowFitPending"/>).
    /// </summary>
    private bool TailFolded => _rowFitReport is not { TailFits: true };

    /// <summary>
    /// <b>Whether the cube pills take their short labels</b>
    /// (<c>SPEC-quiz-view.md</c> §4, "Cube labels abbreviate only when the row
    /// cannot fit them", read under "One budget from the outset"): unless the
    /// row-fit module has measured that the full form fits. The measurement is
    /// the module's, live, under the fonts actually rendering: the ruler's
    /// full-label pills at their widest — the producer's inert copy, both
    /// readings of the fourth answer and every selection — with Submit and the
    /// four, the row's gap and the tail's floor, against the row's own width.
    ///
    /// <para>
    /// <b>The window's, for every decision</b>, like the panel's fold and the
    /// "⋯": a cube problem's first render shows the form the window has
    /// already measured, and choosing a pill never changes it. A consequence,
    /// and a layout policy rather than a labelling rule: the full form must
    /// hold both readings, so where only the narrower Too good reading's full
    /// row would fit, every cube decision shows the short form. <b>Short until
    /// measured</b> (<see cref="RowFitPending"/>).
    /// </para>
    /// </summary>
    private bool ShortCubeLabels => _rowFitReport is not { FullCubeLabelsFit: true };

    /// <summary>
    /// The coordinates the ruler's locator shows: three digits each, so the
    /// tail's floor in the budget holds a game and a move number up to 999
    /// whatever the problem on screen, and the budget does not move from one
    /// problem, or one file, to the next. Wider numbers stay whole (the live
    /// chip never shrinks them); they would only overrun the budget by a digit.
    /// </summary>
    private const int RulerCoordinate = 999;

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
    /// The Show stats icon button's accessible name and tooltip
    /// (SPEC-quiz-view.md §4, halheinrich/backgammon#264's ruling of
    /// 2026-10-03: "Their names stay as their tooltips and accessible names").
    /// </summary>
    internal const string ShowStatsName = "Show stats";

    /// <summary>The End quiz icon button's accessible name and tooltip.</summary>
    internal const string EndQuizName = "End quiz";

    /// <summary>
    /// <b>The one gate on End quiz</b>, read by its button and by its item in
    /// the tail's "⋯" list: the controller is not mid-transition. End quiz is
    /// itself a transition (<see cref="EndQuizAsync"/>); Show stats only
    /// navigates and has no gate.
    /// </summary>
    private bool CanEndQuiz => !Controller.IsBusy;

    /// <summary>
    /// Show stats and End quiz as the tail's "⋯" list offers them
    /// (<see cref="TailMenu"/>), in the tail's order, End quiz last: each
    /// built from its button's name, handler and gate, so an item and its
    /// button cannot differ.
    /// </summary>
    private IReadOnlyList<TailMenuAction> TailMenuActions =>
    [
        new(ShowStatsName, EventCallback.Factory.Create(this, ShowStats), Disabled: false),
        new(EndQuizName, EventCallback.Factory.Create(this, EndQuizAsync), Disabled: !CanEndQuiz),
    ];

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
        if (_rowFit is not null)
        {
            await _rowFit.InvokeVoidAsync("unobserve");
            await _rowFit.DisposeAsync();
            _rowFit = null;
        }
        _self?.Dispose();
        _self = null;
    }
}
