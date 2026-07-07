using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// Read-only mid-quiz stats view: the same <see cref="ScorePanel"/> /
/// <see cref="ScoreBreakdown"/> pair <c>Done</c> shows at the end, rendered here
/// against the live, in-progress <see cref="QuizController"/> instead. Reachable
/// from <c>Quiz</c>'s "Show stats" button in both the answering and review
/// states.
///
/// <para>
/// The controller is a per-tab scoped instance that survives in-app navigation
/// and this page never mutates it (no Submit / Continue / Skip call), so
/// <c>/quiz</c> → <c>/stats</c> → <c>/quiz</c> leaves <see cref="QuizController.Current"/>
/// and <see cref="QuizController.Review"/> exactly as they were — there is no
/// state to persist or restore.
/// </para>
///
/// <para>
/// Direct nav to <c>/stats</c> with no quiz in progress bounces to <c>/</c>; with
/// the quiz already finished it bounces to <c>/done</c>, mirroring <c>Quiz</c>'s
/// own start/finish guards.
/// </para>
/// </summary>
public partial class Stats : ComponentBase
{
    protected override void OnInitialized()
    {
        if (!Controller.HasStarted)
        {
            Nav.NavigateTo("/", replace: true);
            return;
        }

        if (Controller.IsFinished)
        {
            Nav.NavigateTo("/done", replace: true);
        }
    }

    private void BackToQuiz()
    {
        Nav.NavigateTo("/quiz");
    }
}
