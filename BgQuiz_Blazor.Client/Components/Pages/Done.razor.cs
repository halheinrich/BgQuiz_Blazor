using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Client.Components.Pages;

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
    /// <summary>
    /// Number of distinct problems the user was shown: one per scored checker
    /// play, one per scored cube position, plus user-driven skips.
    ///
    /// <para>
    /// A cube position folds into the score as two decisions (one Double + one
    /// Take), so <c>Score.Total.Submitted</c> counts <em>decisions</em>, not
    /// problems — each cube would be double-counted. Counting checker plays
    /// plus <em>doubler</em> decisions (exactly one per cube position) recovers
    /// the problem count.
    /// </para>
    /// </summary>
    private int ProblemsShown =>
        Controller.Score.PlayDecisions.Submitted
        + Controller.Score.DoubleDecisions.Submitted
        + Controller.SkippedCount;

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
