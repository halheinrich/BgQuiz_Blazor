using BackgammonDiagram_Lib;
using BgDataTypes_Lib;
using BgDiag_Razor.Components;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// Quiz page: hosts either <see cref="BackgammonPlayEntry"/> (checker plays)
/// or <see cref="BackgammonCubeEntry"/> (cube decisions) against the scoped
/// <see cref="QuizController"/>'s current decision, routing by
/// <c>Decision.IsCube</c>, and exposes the per-kind action row.
///
/// <para>
/// <b>Review branch.</b> Mirrors the controller's three-state flow. While
/// <see cref="QuizController.Review"/> is null the page is <i>answering</i> —
/// it renders the entry component and the Submit / Skip / Undo row. Once Submit
/// scores and the controller sets <see cref="QuizController.Review"/>, the page
/// flips to the <i>review</i> view: a read-only <see cref="BackgammonDiagram"/>
/// in <see cref="DiagramMode.Solution"/> (the filled analysis panel, exactly as
/// the PPTX exporter renders it) with the user's answer marked, a compact
/// verdict line, and Continue / Restart. Continue advances the controller back
/// to the answering state on the next problem.
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
/// <b>Submit gating.</b> Each entry component fires its completion callback
/// only when the answer is complete — <see cref="BackgammonPlayEntry"/>'s
/// <c>OnPlayCompleted</c> once all dice are consumed legally;
/// <see cref="BackgammonCubeEntry"/>'s <c>OnCubeDecisionCompleted</c> once both
/// cube halves are chosen. The page latches the result
/// (<see cref="_completedPlay"/> / <see cref="_completedCube"/>) and enables
/// Submit. Both latches clear on any controller transition (submit / advance /
/// restart) via <see cref="HandleStateChanged"/>; the play latch also clears on
/// undo.
/// </para>
///
/// <para>
/// <b>Action row by kind.</b> In the answering state, checker decisions offer
/// Submit / Skip / Undo last / Undo all / Restart; cube decisions offer Submit /
/// Skip / Restart (a cube answer has no partial-move state, so Undo does not
/// apply). In the review state both kinds offer Continue / Restart.
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

    protected override void OnInitialized()
    {
        Controller.StateChanged += HandleStateChanged;

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
            $"Not best — your play lost {p.EquityLoss:0.000} equity. The best play is shown above.",
        ProblemReview.Cube c =>
            $"Double: {CubeHalfVerdict(c.DoublerCorrect, c.DoublerEquityLoss)} · "
            + $"Take: {CubeHalfVerdict(c.TakerCorrect, c.TakerEquityLoss)}",
        _ => string.Empty,
    };

    private static string CubeHalfVerdict(bool correct, double loss) =>
        correct ? "correct" : $"incorrect (lost {loss:0.000})";

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

    /// <summary>Bootstrap alert class colouring the verdict by outcome.</summary>
    private static string VerdictCssClass(ProblemReview review) => review switch
    {
        ProblemReview.Play { OffList: true } => "alert alert-warning my-3",
        ProblemReview.Play { IsCorrect: true } => "alert alert-success my-3",
        ProblemReview.Play => "alert alert-danger my-3",
        ProblemReview.Cube { DoublerCorrect: true, TakerCorrect: true } => "alert alert-success my-3",
        ProblemReview.Cube => "alert alert-danger my-3",
        _ => "alert my-3",
    };

    private void HandlePlayCompleted(Play play)
    {
        _completedPlay = play;
        StateHasChanged();
    }

    private void HandleCubeCompleted(CubeDecisionPair answer)
    {
        // BackgammonCubeEntry re-fires on every post-completion change, so this
        // always holds the latest complete pair; the user can revise before
        // Submit.
        _completedCube = answer;
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

    private async Task RestartAsync()
    {
        await Controller.RestartAsync();
    }

    public void Dispose()
    {
        Controller.StateChanged -= HandleStateChanged;
    }
}
