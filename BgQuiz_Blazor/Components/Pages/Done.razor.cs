using BgQuiz_Blazor.Quiz;
using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Components.Pages;

/// <summary>
/// Phase 1 final-summary page rendered after the active
/// <see cref="QuizController"/> exhausts its source. Two buttons close the
/// loop: restart against the same (already-augmented) filter set, or return
/// to the landing page for fresh filter selection.
///
/// <para>Direct nav to <c>/done</c> with no quiz in progress bounces to <c>/</c>.</para>
/// </summary>
public partial class Done : ComponentBase
{
    protected override void OnInitialized()
    {
        if (!Controller.HasStarted)
        {
            Nav.NavigateTo("/", replace: true);
        }
    }

    private async Task RestartAsync()
    {
        await Controller.RestartAsync();
        Nav.NavigateTo("/quiz");
    }

    private void StartOver()
    {
        Nav.NavigateTo("/");
    }
}
