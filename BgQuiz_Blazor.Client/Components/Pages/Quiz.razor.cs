using BackgammonDiagram_Lib;
using BgDataTypes_Lib;
using BgDiag_Razor.Components;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;

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
/// <see cref="QuizController.RedoAsync"/>, which reverses the just-submitted
/// answer and clears <see cref="QuizController.Review"/> — the page falls back
/// to the answering branch on the <i>same</i> <see cref="QuizController.Current"/>
/// problem, with a clean answer slate. The two answer kinds get there
/// differently:
/// <list type="bullet">
///   <item><b>Cube</b> — the answer lives in <see cref="_completedCube"/>, which
///   <see cref="HandleStateChanged"/> nulls on every controller transition (Redo
///   included). <see cref="BackgammonCubeActions"/> is strictly controlled off
///   that field, so its radios render unselected the moment it is cleared —
///   remount or not; there is no internal selection state to reset.</item>
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
/// "Actual" banner). <c>FromDecisionData</c> is not used here because it would
/// default those marks from the <c>.xg</c>-recorded player rather than the quiz
/// user.
/// </para>
///
/// <para>
/// <b>Submit gating.</b> Submit is enabled once the page holds a complete
/// answer. For a play, <see cref="BackgammonPlayEntry"/>'s <c>OnPlayCompleted</c>
/// fires once all dice are consumed legally, latching <see cref="_completedPlay"/>.
/// For a cube, <see cref="BackgammonCubeActions"/> emits a complete
/// <see cref="CubeDecisionPair"/> on every selection (one radio sets both halves
/// atomically), which <c>@bind-Value</c> writes into <see cref="_completedCube"/>;
/// switching radios re-fires, so the field always holds the latest answer. Both
/// fields clear on any controller transition (submit / advance / redo / restart)
/// via <see cref="HandleStateChanged"/>; the play latch also clears on undo.
/// </para>
///
/// <para>
/// <b>Ending the run early.</b> Both action rows trail with an <b>End quiz</b>
/// button (issue #57) — see <see cref="EndQuizAsync"/> for why it sits at the far
/// end of the row and carries no confirmation. It is the only control here that
/// finishes a run the source has not exhausted; everything about what that leaves
/// behind (the abandoned problem counted as a skip, a reviewed answer kept and
/// folded) belongs to <see cref="QuizController.EndQuizAsync"/>, which this page
/// merely calls.
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
/// <b>IsFinished transition.</b> Subscribed to
/// <see cref="QuizController.StateChanged"/>. When the controller's
/// <see cref="QuizController.IsFinished"/> flips true (source exhausted on
/// Continue / Skip), the page navigates to <c>/done</c>.
/// </para>
/// </summary>
public partial class Quiz : ComponentBase, IDisposable
{
    private BackgammonPlayEntry? _playEntry;
    private Play? _completedPlay;
    private CubeDecisionPair? _completedCube;

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
    /// (<see cref="QuizController.ActiveMixHasLength"/>): capless, per-entry
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
                builder.UserDoubleError = cube.DoublerEquityLoss;
                builder.UserTakeError = cube.TakerEquityLoss;
                break;
        }

        return builder.Build();
    }

    /// <summary>Compact verdict line summarizing the just-scored answer.</summary>
    private static string VerdictText(ProblemReview review) => review switch
    {
        ProblemReview.Play { OffList: true } =>
            "Off list — your play wasn't among the analyzed candidates. The best play is shown above.",
        ProblemReview.Play { IsCorrect: true } =>
            "Correct — you found the best play.",
        ProblemReview.Play p =>
            $"Not best — your play lost {p.EquityLoss:0.0000} equity. The best play is shown above.",
        ProblemReview.Cube c =>
            $"{CubeActionDisplay.Label(c.Submitted.Doubler)}: "
            + $"{CubeHalfVerdict(c.DoublerCorrect, c.DoublerEquityLoss)} · "
            + $"{CubeActionDisplay.Label(c.Submitted.Taker)}: "
            + $"{CubeHalfVerdict(c.TakerCorrect, c.TakerEquityLoss)}",
        _ => string.Empty,
    };

    private static string CubeHalfVerdict(bool correct, double loss) =>
        correct ? "correct" : $"incorrect (lost {loss:0.0000})";

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
                ? "Pick the cube action, then Submit."
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
        ProblemReview.Cube { DoublerCorrect: true, TakerCorrect: true } => "alert-success",
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
    /// Unsubscribe from <see cref="QuizController.StateChanged"/> and
    /// <see cref="QuizStatsStore.StatusChanged"/> when the page is torn down,
    /// so a navigated-away instance stops re-rendering.
    /// </summary>
    public void Dispose()
    {
        Controller.StateChanged -= HandleStateChanged;
        StatsStore.StatusChanged -= HandleStatsStatusChanged;
    }
}
