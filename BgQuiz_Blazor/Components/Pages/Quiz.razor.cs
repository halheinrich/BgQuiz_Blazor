using BackgammonDiagram_Lib;
using BgDataTypes_Lib;
using BgDiag_Razor.Components;
using BgQuiz_Blazor.Quiz;
using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Components.Pages;

/// <summary>
/// Quiz page: hosts either <see cref="BackgammonPlayEntry"/> (checker plays)
/// or <see cref="BackgammonCubeEntry"/> (cube decisions) against the scoped
/// <see cref="QuizController"/>'s current decision, routing by
/// <c>Decision.IsCube</c>, and exposes the per-kind action row.
///
/// <para>
/// <b>Submit gating.</b> Each entry component fires its completion callback
/// only when the answer is complete — <see cref="BackgammonPlayEntry"/>'s
/// <c>OnPlayCompleted</c> once all dice are consumed legally;
/// <see cref="BackgammonCubeEntry"/>'s <c>OnCubeDecisionCompleted</c> once both
/// cube halves are chosen. The page latches the result
/// (<see cref="_completedPlay"/> / <see cref="_completedCube"/>) and enables
/// Submit. Both latches clear on any controller transition (advance / restart)
/// via <see cref="HandleStateChanged"/>; the play latch also clears on undo.
/// </para>
///
/// <para>
/// <b>Action row by kind.</b> Checker decisions offer Submit / Skip / Undo
/// last / Undo all / Restart; cube decisions offer Submit / Skip / Restart
/// (a cube answer has no partial-move state, so Undo does not apply).
/// </para>
///
/// <para>
/// <b>IsFinished transition.</b> Subscribed to
/// <see cref="QuizController.StateChanged"/>. When the controller's
/// <see cref="QuizController.IsFinished"/> flips true (source exhausted),
/// the page navigates to <c>/done</c>.
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

    private async Task SubmitAsync()
    {
        // Route by which answer is latched. The current decision's kind
        // determines which entry component rendered and therefore which latch
        // is set; the latches are mutually exclusive per problem.
        if (_completedCube is { } cube)
        {
            await Controller.SubmitCubeActionAsync(cube);
        }
        else if (_completedPlay is { } play)
        {
            await Controller.SubmitPlayAsync(play);
        }
        // The relevant latch is cleared by HandleStateChanged; nothing else to do.
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
