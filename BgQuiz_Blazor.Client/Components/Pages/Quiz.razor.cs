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
/// <see cref="QuizController"/>, routing the board region by <c>Decision.IsCube</c>
/// — checker plays to <see cref="BackgammonPlayEntry"/> (click-driven assembly),
/// cube decisions to a board-only <see cref="BackgammonDiagram"/> whose answer is
/// entered by the <see cref="BackgammonCubeActions"/> radios in the action row —
/// and exposes the per-kind action row.
///
/// <para>
/// <b>Review branch.</b> Mirrors the controller's three-state flow. While
/// <see cref="QuizController.Review"/> is null the page is <i>answering</i> —
/// it renders the entry component and the Submit / Skip / Undo row. Once Submit
/// scores and the controller sets <see cref="QuizController.Review"/>, the page
/// flips to the <i>review</i> view: a read-only <see cref="BackgammonDiagram"/>
/// in <see cref="DiagramMode.Solution"/> (the filled analysis panel, exactly as
/// the PPTX exporter renders it) with the user's answer marked, a compact
/// verdict line, and Continue / Redo / Show stats. Continue advances the
/// controller back to the answering state on the next problem. The review
/// diagram's <c>OnDiceClicked</c> is also bound to <see cref="ContinueAsync"/> —
/// clicking the dice hit-region (already wired for click-driven play assembly
/// during answering) advances past the solution exactly like the Continue
/// button.
/// </para>
///
/// <para>
/// <b>Redo &amp; answer freshness.</b> Redo (review-state only) calls
/// <see cref="QuizController.RedoAsync"/>, which re-opens the problem for
/// practice and clears <see cref="QuizController.Review"/> — the page falls back
/// to the answering branch on the <i>same</i> <see cref="QuizController.Current"/>
/// problem, with a clean answer slate. Clean on the page only: the answer of
/// record stands, and the submission that follows is practice (SPEC-scoring.md
/// §2), which the verdict band says in as many words — see
/// <see cref="VerdictText"/>. The two answer kinds reach the clean slate
/// differently:
/// <list type="bullet">
///   <item><b>Cube</b> — the answer lives in <see cref="_completedCube"/>, which
///   <see cref="HandleStateChanged"/> nulls on every controller transition (Redo
///   included), and that is the whole mechanism. The
///   <see cref="BackgammonCubeActions"/> row is controlled on the <i>pair</i>
///   and holds no state the pair does not express: every pill is a complete
///   <see cref="CubeClaimPair"/> (the four reachable verdicts, SPEC-scoring §3
///   as amended 2026-09-02, halheinrich/backgammon#187), so nulling the field
///   clears whatever is lit. The row carried a <c>@key</c> on the current
///   problem while it was two radio groups — a half-answered row composed to no
///   pair, agreed with the null, and survived a Skip — and lost it with that
///   state: a remount would guard nothing now. Redo reaches the clean slate the
///   way Play does — the review branch already unmounted the row.</item>
///   <item><b>Play</b> — <see cref="BackgammonPlayEntry"/> holds its own
///   in-progress click state and only resets it when the incoming request
///   describes a different problem (same Mop/Dice suppresses the reset). That
///   suppression path is never reached across Redo: Submit already unmounted the
///   entry when the page swapped to the review branch, so Redo's swap back
///   constructs a genuinely new instance unconditionally — Blazor cannot reuse an
///   instance that was not in the prior render, so no <c>@key</c> bump is needed.
///   (An earlier draft added a redo-generation <c>@key</c> defensively; it was
///   removed once a test proved the branch swap alone guarantees a fresh
///   instance — see <c>Quiz_Redo_PlayEntry_RemountsFreshComponent</c> in
///   <c>PageTests</c>.)</item>
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
/// <b>Marking the user's answer.</b> The solution request is built from the
/// answered decision via <see cref="DiagramRequest.Builder"/>'s
/// <c>From(position, decision, descriptive, DiagramMode.Solution)</c>, then the
/// user's marks are overridden from <see cref="QuizController.Review"/>:
/// <c>UserPlayIndex</c> for a checker play (the matched candidate index, or
/// <c>-1</c> off-list so no marker draws), or <c>UserDoubleError</c> /
/// <c>UserTakeError</c> for a cube decision (the two per-half losses driving the
/// "Actual" banner, read off the scored submission the review carries).
/// <c>FromDecisionData</c> is not used here because it would default those
/// marks from the <c>.xg</c>-recorded player rather than the quiz user.
/// </para>
///
/// <para>
/// <b>Submit gating.</b> Submit is enabled once the page holds a complete
/// answer. For a play, <see cref="BackgammonPlayEntry"/>'s <c>OnPlayCompleted</c>
/// fires once all dice are consumed legally, latching <see cref="_completedPlay"/>.
/// For a cube, <see cref="BackgammonCubeActions"/> is one radio group over the
/// four reachable verdict pairs — No double, Double / Take, Double / Pass, Too
/// good (SPEC-scoring.md §3 as amended 2026-09-02, halheinrich/backgammon#187;
/// the model is still a (claim, taker) pair scored per half, only its
/// presentation collapsed to the pairs) — and emits a complete
/// <see cref="CubeClaimPair"/> on every click, which <c>@bind-Value</c> writes
/// into <see cref="_completedCube"/>; a later click re-fires, so the field
/// always holds the latest answer. Gating Submit on the field being non-null is
/// therefore gating it on <i>a pill chosen</i>, lit from the first click. The
/// Too good pill is offered exactly where the producer says the verdict can
/// occur (<see cref="BgDecisionData.CanBeTooGood"/>, passed through as
/// <c>OfferTooGood</c>; withheld at a money position under Jacoby with the cube
/// centred) — this page reads that fact and never re-derives it. Both fields
/// clear on any controller transition (submit / advance / redo / restart) via
/// <see cref="HandleStateChanged"/>; the play latch also clears on undo. The
/// gate itself is <see cref="CanSubmit"/>, one member read by both Submit
/// buttons and by the spacebar.
/// </para>
///
/// <para>
/// <b>The cube verdict speaks claims.</b> The review's verdict line names the
/// doubler half by the claim the user submitted — No Double, Double, or Too
/// Good — and, when that claim is wrong, names the truth claim; a no-double
/// answer to a too-good position, or a too-good answer to a no-double one (the
/// XG "too good to double/Take" position, a No double by ruling), is called out
/// as the right action with the wrong claim rather than as an equity loss of
/// nothing. The incoherent (no double, pass) answer is no longer offered by the
/// row, but the controller's <see cref="QuizController.SubmitCubeAction"/>
/// still accepts any pair, so the trailing clause that explains it stands for
/// an answer arriving that way. See <see cref="CubeVerdict"/>.
/// </para>
///
/// <para>
/// <b>Ending the run early.</b> Both action rows trail with an <b>End quiz</b>
/// button (issue #57) — see <see cref="EndQuizAsync"/> for why it sits at the far
/// end of the row and carries no confirmation. It is the only control here that
/// finishes a run the source has not exhausted; everything about what that leaves
/// behind (an unanswered problem counted as a skip, an answered one kept and
/// folded — the answer of record, not whatever review is on screen) belongs to
/// <see cref="QuizController.EndQuizAsync"/>, which this page merely calls.
/// </para>
///
/// <para>
/// <b>Action row by kind.</b> In the answering state, checker decisions offer
/// Submit / Skip / Undo last / Undo all — the two Undo buttons live for the
/// whole of the entry, disabled only while the controller is busy (see
/// <see cref="UndoLast"/> for why gating them on the entry's <c>@ref</c> made
/// them dead for exactly the window they exist to serve); cube decisions place the
/// <see cref="BackgammonCubeActions"/> radios inline (the answer input, since the
/// board region is board-only) ahead of Submit / Skip — a cube answer has no
/// partial-move state, so Undo does not apply. Both trail with Show stats and
/// End quiz in the row's <c>ms-auto</c> cluster. In the review state both kinds
/// offer Continue / Redo, trailed the same way.
/// </para>
///
/// <para>
/// <b>Every notice above the board dismisses on click, and the mix composition
/// notice also retires on the first answer.</b> Both composition variants — the
/// capless composition-only status line and the length-bound shortfall alert —
/// render from <see cref="QuizController.LastComposition"/>, which lives as long
/// as the run does, so they used to sit above every problem for the whole quiz.
/// They disappear once the user submits their first answer, checker or cube alike
/// (see <see cref="Submit"/>), <i>or</i> the moment the user clicks them
/// (<see cref="DismissComposition"/>): the notice describes how this quiz was
/// built, worth reading before answering and stale chrome after. Either gesture
/// ends it. The stats notices dismiss the same way
/// (<see cref="DismissStats"/>) but have no automatic retirement — a degraded
/// recording context is not something an answer makes stale.
/// </para>
///
/// <para>
/// Every dismissal is recorded in the scoped <see cref="QuizNoticeDismissal"/>
/// holder — <i>not</i> by clearing the controller's telemetry, which still frames
/// the composition notice, carries <see cref="QuizController.ProblemCount"/>, and
/// feeds Home's composed-to-zero wording, and <i>not</i> in a page field, which
/// the <c>Show stats</c> round trip would reset (this page is re-instantiated on
/// in-app navigation). Each is keyed on its notice's current occurrence — the
/// composition instance, the store's
/// <see cref="QuizStatsStore.StatusOccurrence"/> — so the next Start/Restart, or
/// the next stats transition, shows its notice again without any reset call site.
/// </para>
///
/// <para>
/// <b>The affordance is a visible close button plus the whole alert.</b> The
/// large click target is the low-vision one (this arc's whole reason for
/// existing), but a bare clickable region with nothing to look at is
/// undiscoverable — so the standard <c>btn-close</c> renders too, and it is the
/// button, not the region, that carries the keyboard and screen-reader
/// semantics. Bootstrap's own <c>data-bs-dismiss</c> is deliberately not used:
/// it removes the node outside Blazor's knowledge, leaving the renderer's tree
/// disagreeing with the DOM.
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
/// <c>halheinrich/backgammon#150</c> and <c>halheinrich/backgammon#66</c>). Two
/// user settings choose how the review's candidate list is ordered and whether
/// its shallowly analyzed plays are shown at all. Both are producer options that
/// <see cref="BuildSolutionRequest"/> passes through from
/// <see cref="QuizSettings"/>; neither is a page concern beyond that, and
/// neither reaches the answering board, which is
/// <see cref="DiagramMode.Problem"/> and has no candidate list to treat.
/// </para>
///
/// <para>
/// <b>IsFinished transition.</b> Subscribed to
/// <see cref="QuizController.StateChanged"/>. When the controller's
/// <see cref="QuizController.IsFinished"/> flips true (source exhausted on
/// Continue / Skip), the page navigates to <c>/done</c>.
/// </para>
///
/// <para>
/// <b>The spacebar performs the primary action</b> (issue
/// <c>halheinrich/backgammon#149</c>, ruled 2026-09-02: always on, no setting).
/// Space does what clicking the dice already does — Continue at review, Submit
/// while answering once a complete answer has enabled it, nothing while the
/// controller is busy — so the shortcut is a second spelling of an existing
/// unconditional rule, not a new one. The rule is
/// <see cref="PerformPrimaryActionAsync"/>, and it reads the same two gates the
/// buttons render from, <see cref="CanSubmit"/> and <see cref="CanContinue"/>:
/// one expression each, so the keyboard can never enable what the button shows
/// disabled. Which presses reach it is decided in the browser, by
/// <c>wwwroot/js/quizKeys.js</c>, from the event alone (Space, unmodified, not a
/// repeat, focus on nothing that consumes space — see the module's comment for
/// the filter); this is the app's first JS-invokable callback, attached on the
/// first render and detached on disposal, which is why the page is
/// <see cref="IAsyncDisposable"/> now.
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

    private BackgammonPlayEntry? _playEntry;
    private Play? _completedPlay;
    private CubeClaimPair? _completedCube;

    /// <summary>The imported keyboard module; null until the first render's import lands.</summary>
    private IJSObjectReference? _keys;

    /// <summary>The reference the module calls back through; created with the attach, disposed with the page.</summary>
    private DotNetObjectReference<Quiz>? _self;

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
        // Any controller transition advances or restarts the problem; the
        // previously latched answers no longer apply.
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
    /// Attach the spacebar shortcut once the page is in the DOM. First render
    /// only: the module listens on the document, not on any element this page
    /// re-renders, so there is nothing to re-attach. The import is awaited
    /// before anything is created from it, and a disposal that lands during
    /// that await is honoured by releasing the module instead of attaching —
    /// the one ordering that could otherwise leave a listener holding a
    /// disposed reference (a Show-stats round trip re-instantiates this page,
    /// so the window is real).
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        var keys = await JS.InvokeAsync<IJSObjectReference>("import", KeysModulePath);
        if (_disposed)
        {
            await keys.DisposeAsync();
            return;
        }

        _keys = keys;
        _self = DotNetObjectReference.Create(this);
        // The callback's name travels with the reference so it is spelled
        // exactly once, here; the module never restates it.
        await _keys.InvokeVoidAsync("attach", _self, nameof(PerformPrimaryActionAsync));
    }

    /// <summary>
    /// The primary action, as the spacebar asks for it
    /// (<c>halheinrich/backgammon#149</c>): Continue at review, Submit while
    /// answering once <see cref="CanSubmit"/> holds, nothing otherwise — the
    /// same rule a dice click follows, spelled over the same two gates the
    /// buttons render from. Public and <see cref="JSInvokableAttribute"/>
    /// because the module invokes it by name through the
    /// <see cref="DotNetObjectReference{TValue}"/> the attach handed over;
    /// nothing else calls it. Eligibility of the press itself (key, modifiers,
    /// focus) was settled in the browser before this runs.
    /// </summary>
    [JSInvokable]
    public async Task PerformPrimaryActionAsync()
    {
        if (CanContinue)
        {
            await ContinueAsync();
        }
        else if (CanSubmit)
        {
            Submit();
        }
    }

    /// <summary>
    /// <b>The one gate on Submit</b>, read by both Submit buttons and by
    /// <see cref="PerformPrimaryActionAsync"/>: the page is answering (no
    /// review to read), the controller is not mid-transition, and a complete
    /// answer is latched — the play from <see cref="HandlePlayCompleted"/> or
    /// the cube pair from the radios' <c>@bind-Value</c>. The two latches are
    /// mutually exclusive per problem (only one answer instrument renders, and
    /// both clear on every transition), so "either is set" is "this problem's
    /// answer is complete" without the gate needing to know the kind. It used
    /// to be two inline expressions, one per button; the keyboard made a
    /// second reader of the rule, and a second reader is what a single member
    /// is for.
    /// </summary>
    private bool CanSubmit =>
        Controller.Review is null
        && !Controller.IsBusy
        && (_completedCube is not null || _completedPlay is not null);

    /// <summary>
    /// The gate on Continue, beside <see cref="CanSubmit"/> for the same
    /// reader: there is a review to leave and the controller is not busy.
    /// The two are exclusive by construction — a review is either there or
    /// not — which is what lets <see cref="PerformPrimaryActionAsync"/> pick
    /// one action without a third case.
    /// </summary>
    private bool CanContinue =>
        Controller.Review is not null && !Controller.IsBusy;

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
    /// sets <see cref="QuizController.Review"/> and the page normalizes; Redo and
    /// Continue clear it and the page re-maximizes; Undo never leaves the
    /// answering state, so it changes nothing.
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

    private DiagramRequest BuildRenderRequest(BgDataTypes_Lib.BgDecisionData current) =>
        // DiagramMode.Problem hides the analysis panel (the candidate list is the
        // answer the quiz is grading). FromDecisionData is the single canonical
        // data → renderer mapping; using it avoids drift on new fields.
        DiagramRequest.FromDecisionData(current, DiagramMode.Problem, HomeBoardOnRight);

    /// <summary>
    /// Build the review-state solution request: the original answered position
    /// with the filled analysis panel (<see cref="DiagramMode.Solution"/>).
    /// <para>
    /// For a checker play the primary <c>*</c> marks the <em>.xg-recorded played
    /// move</em> and the secondary <c>†</c> marks the <em>quiz user's answer</em>.
    /// <c>Builder.From</c> already sources <c>UserPlayIndex</c> (the <c>*</c>)
    /// from <c>decision.UserPlayIndex</c>, so only
    /// <see cref="DiagramRequest.SecondaryPlayIndex"/> (the <c>†</c>) is set
    /// here, from the answered candidate index. The producer suppresses the
    /// <c>†</c> when it coincides with the recorded play, and an off-list answer
    /// (index <c>-1</c>) draws no <c>†</c> at all.
    /// </para>
    /// <para>
    /// For a cube decision the two per-half equity losses drive the "Actual"
    /// banner row instead.
    /// </para>
    /// <para>
    /// Both answer kinds then carry the user's depth treatment — the candidate
    /// ordering and the hidden-depth ceiling. This is the only request that
    /// does: <see cref="BuildRenderRequest"/> builds a
    /// <see cref="DiagramMode.Problem"/> request, whose panel is blank because
    /// the candidate list is the answer being graded.
    /// </para>
    /// </summary>
    private DiagramRequest BuildSolutionRequest(
        BgDataTypes_Lib.BgDecisionData current, ProblemReview review)
    {
        var builder = DiagramRequest.Builder.From(
            current.Position, current.Decision, current.Descriptive, DiagramMode.Solution,
            HomeBoardOnRight);

        switch (review)
        {
            case ProblemReview.Play play:
                // * (UserPlayIndex, already set by Builder.From from the
                // .xg-recorded play) marks the played move; † marks the quiz
                // answer. The producer suppresses † when it equals the recorded
                // play, and an off-list answer (index -1) draws no †.
                builder.SecondaryPlayIndex = play.UserPlayIndex;
                break;
            case ProblemReview.Cube cube:
                // The two per-half equity losses drive the "Actual" banner row.
                builder.UserDoubleError = cube.Submission.DoublerEquityLoss;
                builder.UserTakeError = cube.Submission.TakerEquityLoss;
                break;
        }

        // The depth treatment, assigned unconditionally rather than behind a
        // branch: with either setting untouched its value is the producer's own
        // default (Equity / null), which the producer defines as the untouched
        // rendering — so passing the default IS passing nothing, and there is no
        // "leave it alone" path that could drift from the "set it" one. What the
        // ordering checkbox means stays in QuizSettings; the hide ceiling means
        // itself — the user picks a level off the producer's own ladder and it
        // travels here unchanged, which is the point of the producer's
        // inclusive-hide shape (halheinrich/backgammon#66).
        builder.CandidateOrdering = Settings.EffectiveCandidateOrdering;
        builder.MaximumHiddenCandidateAnalysisLevel =
            Settings.MaximumHiddenCandidateAnalysisLevel;

        return builder.Build();
    }

    /// <summary>
    /// Compact verdict line summarizing the just-scored answer, prefixed when
    /// the submission was practice.
    ///
    /// <para>
    /// <b>Why practice is named here.</b> SPEC-scoring.md §2 leaves the review
    /// pane's treatment of a practice verdict to this arc. A practice
    /// submission is scored and shown like any other but changes nothing — not
    /// the score panel a few lines below on this same page, not Done, not the
    /// lifetime record — so an unbadged "Correct" beside a score that does not
    /// move reads as a bug rather than as the model working. One clause fixes
    /// that, and says which answer <i>did</i> count rather than only which one
    /// did not. Everything else about the review is untouched: the verdict
    /// wording, <see cref="StatusVerdictColor"/>'s outcome colouring, the
    /// diagram's markers and the Continue / Redo pair are the same, because the
    /// retry's score is what the user redid to see.
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
            ? $"Practice retry — your first answer stands. {ScoredVerdict(review)}"
            : ScoredVerdict(review);

    /// <summary>The scored half of <see cref="VerdictText"/>, per answer kind.</summary>
    private static string ScoredVerdict(ProblemReview review) => review switch
    {
        ProblemReview.Play { OffList: true } =>
            "Off list — your play wasn't among the analyzed candidates. The best play is shown above.",
        ProblemReview.Play { IsCorrect: true } =>
            "Correct — you found the best play.",
        ProblemReview.Play p =>
            $"Not best — your play lost {p.EquityLoss:0.0000} equity. The best play is shown above.",
        ProblemReview.Cube c => CubeVerdict(c.Submission),
        _ => string.Empty,
    };

    /// <summary>
    /// The cube verdict: one segment per half, each named for what the user
    /// submitted — the doubler half by its claim, the taker half by its
    /// action — in the solution diagram's own wording
    /// (<see cref="CubeActionDisplay"/>), plus a trailing explanation when the
    /// submitted pair is the incoherent cell. SPEC-scoring.md §3
    /// (halheinrich/backgammon#86) rules the shape: per-half, claim-wise on
    /// the doubler side.
    ///
    /// <para>
    /// <b>The doubler half names the truth claim when the user's is wrong,
    /// and the taker half does not.</b> The claim axis has three values, so
    /// "incorrect" alone leaves two candidates; the taker axis has two, so
    /// "Take: incorrect" already says Pass. Naming it also covers what the
    /// diagram beside this line says at the action level: the producer's
    /// banner speaks board actions, so a too-good position reads there as
    /// "Best: No Double" while this line says Too Good (the label SSOT arc,
    /// halheinrich/backgammon#185, recomposes the banner over claims and
    /// re-sources these spellings; neither is patched here).
    /// </para>
    ///
    /// <para>
    /// <b>Right action, wrong claim is said in those words, in both
    /// directions.</b> A no-double answer to a too-good position scores
    /// incorrect at +0.000 by ruling — the two claims collapse to the same
    /// board action, so no equity was lost — and so does a too-good answer to
    /// a no-double one, which is the XG "too good to double/Take" position
    /// since SPEC-scoring §3's 2026-09-02 amendment made it a No double by
    /// ruling (halheinrich/backgammon#187: Too Good requires the pass).
    /// Printing "incorrect (lost 0.0000)" there would read as a contradiction;
    /// the line instead says what actually happened, naming the truth claim
    /// either way round. The test is on the board action behind each claim
    /// (<see cref="CubeClaimExtensions.ToCubeAction"/>, the producer's one
    /// spelling of the collapse), not on the loss being zero — a zero loss can
    /// also come from an equity tie between different actions, which is the
    /// ordinary incorrect case.
    /// </para>
    ///
    /// <para>
    /// <b>The incoherent cell is explained, not just marked.</b> (No double,
    /// pass) reveals a misunderstanding a review can name: if the opponent
    /// would pass, cashing beats playing on, so "not good enough to double"
    /// cannot hold. The row no longer offers the cell (the option set is the
    /// four reachable pairs since the 2026-09-02 amendment), but
    /// <see cref="QuizController.SubmitCubeAction"/> accepts any pair, so an
    /// answer arriving that way is still scored per half like any other and
    /// still gets the clause, appended after the two verdicts.
    /// </para>
    /// </summary>
    private static string CubeVerdict(SubmittedCubeAction submission)
    {
        var answer = submission.UserDecision;
        var best = submission.BestDecision;

        string doubler = CubeActionDisplay.Label(answer.Claim) + ": " + (
            submission.DoublerCorrect
                ? "correct"
                : answer.Claim.ToCubeAction() == best.Claim.ToCubeAction()
                    ? $"wrong claim — it's {CubeActionDisplay.Label(best.Claim)} (right action, no equity lost)"
                    : $"incorrect — best is {CubeActionDisplay.Label(best.Claim)} (lost {submission.DoublerEquityLoss:0.0000})");

        string taker = CubeActionDisplay.Label(answer.Taker) + ": " + (
            submission.TakerCorrect
                ? "correct"
                : $"incorrect (lost {submission.TakerEquityLoss:0.0000})");

        string verdict = $"{doubler} · {taker}";
        return answer.IsIncoherent
            ? verdict + " · No double and pass can't both hold: if they'd pass, cashing beats playing on."
            : verdict;
    }

    /// <summary>
    /// Legend for the solution diagram's play markers, listing only the markers
    /// actually drawn: <c>*</c> the .xg-recorded played move (present when the
    /// decision carries a recorded play) and <c>†</c> the quiz answer (present
    /// only when it is on-list and differs from the recorded play — the same
    /// suppression the renderer applies to <see cref="DiagramRequest.SecondaryPlayIndex"/>).
    /// Returns <c>null</c> when no play marker shows (cube reviews, or a play
    /// review with neither a recorded move nor a distinct on-list answer).
    /// </summary>
    private static string? SolutionLegend(ProblemReview review, DecisionData decision)
    {
        if (review is not ProblemReview.Play play) return null;

        var parts = new List<string>(2);
        if (decision.UserPlayIndex >= 0)
            parts.Add("* played");
        if (play.UserPlayIndex >= 0 && play.UserPlayIndex != decision.UserPlayIndex)
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
    private static string StatusText(ProblemReview? review, DecisionData decision) =>
        review is not null
            ? VerdictText(review)
            : decision.IsCube
                ? "Pick the cube decision, then Submit."
                : "Click the board to build your play, then Submit.";

    /// <summary>
    /// Bootstrap alert colour for the status strip's verdict band: outcome
    /// colouring at review, a quiet neutral tone while answering.
    /// </summary>
    private static string StatusVerdictColor(ProblemReview? review) => review switch
    {
        null => "alert-secondary",
        ProblemReview.Play { OffList: true } => "alert-warning",
        ProblemReview.Play { IsCorrect: true } => "alert-success",
        ProblemReview.Play => "alert-danger",
        ProblemReview.Cube { Submission: { DoublerCorrect: true, TakerCorrect: true } } => "alert-success",
        ProblemReview.Cube => "alert-danger",
        _ => "alert-secondary",
    };

    /// <summary>
    /// Dismiss the stats-context notice the user is looking at, keyed on the
    /// occurrence the store is currently reporting rather than on "the stats
    /// notice" as a standing thing. A later transition — or the next run's bind
    /// — mints a new occurrence and shows its notice fresh, which is the point:
    /// a run that records nothing has to say so once per run, not once per app.
    /// </summary>
    private void DismissStats() =>
        Notices.Dismiss(QuizNotice.StatsContext, StatsStore.StatusOccurrence);

    /// <summary>
    /// Dismiss this run's stats-retirement report. Keyed on
    /// <see cref="QuizStatsStore.StatsRetiredOccurrence"/>, which is non-null
    /// only on a run that actually retired a file — and a different token on the
    /// next one that does, so a folder retired later says so on its own.
    /// </summary>
    private void DismissStatsRetired()
    {
        if (StatsStore.StatsRetiredOccurrence is { } occurrence)
            Notices.Dismiss(QuizNotice.StatsRetired, occurrence);
    }

    /// <summary>
    /// Dismiss the composition notice for <paramref name="composition"/> — the
    /// click half of a retirement the first submitted answer also performs (see
    /// <see cref="Submit"/>). Either gesture ends it, and both record the same
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

    private void Submit()
    {
        // Route by which answer is latched. The current decision's kind
        // determines which entry component rendered and therefore which latch
        // is set; the latches are mutually exclusive per problem. Submit scores
        // and enters the review state synchronously — the advance is deferred to
        // Continue — so neither call awaits.
        if (_completedCube is { } cube)
        {
            Controller.SubmitCubeAction(cube);
        }
        else if (_completedPlay is { } play)
        {
            Controller.SubmitPlay(play);
        }
        // The relevant latch is cleared by HandleStateChanged; nothing else to do.

        // The first answer retires the mix composition notice — it described how
        // this quiz was built, which the user has now read and acted on. Gated on
        // Review having been set rather than on having called a Submit: both
        // controller mutators no-op under the transition gate (a Submit landing
        // inside a pending Continue/Skip), and dismissing on a call that scored
        // nothing would drop the notice without the user ever answering. Review
        // non-null is the proof, and it covers an off-list play too — that is a
        // submitted answer with a review to read, just an unscored one. Skip is
        // deliberately not a dismissal: it moves past a problem without answering
        // it, so the composition is still the thing the user hasn't engaged with.
        if (Controller.Review is not null && Controller.LastComposition is { } comp)
        {
            DismissComposition(comp);
        }
    }

    private async Task ContinueAsync()
    {
        await Controller.ContinueAsync();
    }

    private async Task SkipAsync()
    {
        await Controller.SkipCurrentAsync();
    }

    /// <summary>
    /// End the run here and go to the summary (issue #57). One click, acting
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
    /// enclosing <c>!IsCube</c> branch guarantees an entry is rendered, and a
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

    private async Task RedoAsync()
    {
        await Controller.RedoAsync();
    }

    private void ShowStats()
    {
        Nav.NavigateTo("/stats");
    }

    /// <summary>
    /// Tear-down, in the order the dependencies run: unsubscribe from
    /// <see cref="QuizController.StateChanged"/> and
    /// <see cref="QuizStatsStore.StatusChanged"/> so a navigated-away instance
    /// stops re-rendering; then detach the keyboard listener and release the
    /// module, and only then dispose the <see cref="DotNetObjectReference{TValue}"/>
    /// it was calling back through — the reference must outlive the last thing
    /// that could invoke it. <see cref="_disposed"/> covers an import still in
    /// flight (see <see cref="OnAfterRenderAsync"/>). Nothing here can race an
    /// attach: WebAssembly runs the JS of an interop call synchronously, so an
    /// attach whose await is pending has already executed in the browser.
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
        _self?.Dispose();
        _self = null;
    }
}
