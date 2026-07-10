using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// Phase 1 final-summary page rendered after the active
/// <see cref="QuizController"/> exhausts its source. Two buttons close the
/// loop: <i>Restart with same filters</i> re-runs the same (already-augmented)
/// filter set, and <i>Back to setup</i> navigates to <c>Home</c>. The latter is
/// navigation only — the start-gate holders persist across it, so <c>Home</c>
/// arrives armed with the same picks and filters; the label describes that
/// navigation rather than promising a reset the button doesn't perform.
///
/// <para>Direct nav to <c>/done</c> with no quiz in progress bounces to <c>/</c>.</para>
///
/// <para>
/// Reaching this page is the quiz ending as intended, so it clears the
/// <see cref="QuizLiveMarker"/>: an honest completion has no reload-reset to
/// announce on a later boot. (A reload that killed a live quiz never reaches
/// here — Done requires the surviving in-memory controller.)
/// </para>
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

    protected override async Task OnInitializedAsync()
    {
        if (!Controller.HasStarted)
        {
            Nav.NavigateTo("/", replace: true);
            return;
        }

        // Honest completion — no reload-reset to announce later. Clear the marker
        // (set on Start) so a subsequent boot doesn't misread a finished quiz as
        // one a reload interrupted.
        await Marker.ClearAsync();
    }

    private async Task RestartAsync()
    {
        await Controller.RestartAsync();
        Nav.NavigateTo("/quiz");
    }

    private void BackToSetup()
    {
        Nav.NavigateTo("/");
    }
}
