using BackgammonDiagram_Lib;
using BgDataTypes_Lib;
using BgDiag_Razor.Components;
using BgQuiz_Blazor.Quiz;
using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Components.Pages;

/// <summary>
/// Phase 1 quiz page: hosts <see cref="BackgammonPlayEntry"/> against the
/// scoped <see cref="QuizController"/>'s current decision and exposes the
/// Submit / Skip / Undo / Restart action row.
///
/// <para>
/// <b>Submit gating.</b> <see cref="BackgammonPlayEntry"/> fires
/// <c>OnPlayCompleted</c> only when all dice have been consumed legally;
/// the page latches that play in <see cref="_completedPlay"/> and enables
/// the Submit button. After submission (or undo, or advance), the latch
/// clears.
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
        // previously latched play no longer applies.
        _completedPlay = null;

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

    private async Task SubmitAsync()
    {
        if (_completedPlay is not { } play) return;
        await Controller.SubmitPlayAsync(play);
        // _completedPlay is cleared by HandleStateChanged; nothing else to do.
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
