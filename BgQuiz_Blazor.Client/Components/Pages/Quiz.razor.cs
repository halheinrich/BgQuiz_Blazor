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
/// <b>Action row by kind.</b> In the answering state, checker decisions offer
/// Submit / Skip / Undo last / Undo all; cube decisions place the
/// <see cref="BackgammonCubeActions"/> radios inline (the answer input, since the
/// board region is board-only) ahead of Submit / Skip — a cube answer has no
/// partial-move state, so Undo does not apply. Both trail with Show stats in the
/// row's <c>ms-auto</c> slot. In the review state both kinds offer Continue /
/// Redo, trailed the same way by Show stats.
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
    private readonly DiagramOptions _diagramOptions = new();

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
    /// On load: subscribe to <see cref="QuizController.StateChanged"/> so the
    /// page re-renders on each transition, then apply the same start/finish
    /// guards <c>Stats</c> uses — bounce to <c>/</c> with no quiz in progress, to
    /// <c>/done</c> if the source is already exhausted.
    /// </summary>
    protected override void OnInitialized()
    {
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

    private static DiagramRequest BuildRenderRequest(BgDataTypes_Lib.BgDecisionData current) =>
        // DiagramMode.Problem hides the analysis panel (the candidate list is the
        // answer the quiz is grading). FromDecisionData is the single canonical
        // data → renderer mapping; using it avoids drift on new fields.
        DiagramRequest.FromDecisionData(current, DiagramMode.Problem);

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
    private static DiagramRequest BuildSolutionRequest(
        BgDataTypes_Lib.BgDecisionData current, ProblemReview review)
    {
        var builder = DiagramRequest.Builder.From(
            current.Position, current.Decision, current.Descriptive, DiagramMode.Solution);

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
    /// a neutral state-appropriate prompt while answering. The strip is always
    /// rendered (fixed height — see <c>.status-strip</c> in <c>app.css</c>) so
    /// chrome height, and therefore board size, is state-invariant; only the
    /// content swaps.
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
    }

    private async Task ContinueAsync()
    {
        await Controller.ContinueAsync();
    }

    private async Task SkipAsync()
    {
        await Controller.SkipCurrentAsync();
    }

    private void UndoLast()
    {
        _playEntry?.UndoLast();
        // The component doesn't notify us of internal undos; the latched
        // completed play is no longer valid post-undo.
        _completedPlay = null;
    }

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
