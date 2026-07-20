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
/// This page participates in the <see cref="QuizLiveMarker"/> lifecycle on both
/// sides. Reaching it <b>clears</b> the marker — an honest completion has no
/// reload-reset to announce on a later boot (a reload that killed a live quiz
/// never reaches here; Done requires the surviving in-memory controller).
/// <i>Restart</i> then <b>re-sets</b> it, because it makes a quiz live again, so
/// a reload during the restarted quiz is acknowledged exactly like one during a
/// fresh Start. <i>Back to setup</i> sets nothing — it only navigates.
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

    /// <summary>
    /// On load: bounce to <c>/</c> if no quiz has started, otherwise clear the
    /// <see cref="QuizLiveMarker"/>. Reaching Done is honest completion, so there
    /// is no reload-reset for a later boot to announce (Restart re-sets it).
    /// </summary>
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

    /// <summary>
    /// Set when a weighted Restart was refused — the stored mix is re-attempted
    /// on every Restart, and stats can have fallen away since the last run.
    /// Drives the refusal notice with its per-run "Restart without mix"
    /// override; the summary underneath survives by the refusal's own
    /// touches-no-state guarantee. Per-visit outcome state, so a component
    /// field.
    /// </summary>
    private bool _mixRefused;

    private Task RestartAsync() => RestartCoreAsync(ignoreMix: false);

    /// <summary>
    /// The refusal notice's one-click escape: restart this one quiz as
    /// passthrough. Per-run only — the stored mix is untouched and re-applies
    /// on the next Start/Restart that can honor it.
    /// </summary>
    private Task RestartWithoutMixAsync() => RestartCoreAsync(ignoreMix: true);

    private async Task RestartCoreAsync(bool ignoreMix)
    {
        _mixRefused = false;

        var outcome = await Controller.RestartAsync(ignoreMix);

        // Overlapped gesture: the transition gate ignored this call — change
        // nothing; the in-flight Restart owns any navigation and notices.
        if (outcome == QuizStartOutcome.Busy) return;

        if (outcome == QuizStartOutcome.MixRequiresStats)
        {
            // Refused: no quiz state changed, the summary stands, and the
            // notice offers the per-run escape. The marker stays cleared —
            // nothing became live.
            _mixRefused = true;
            return;
        }

        // Restart makes a quiz live again from the same pipeline, so re-set the
        // marker that arriving at Done cleared — a reload during the restarted
        // quiz gets the same reset notice as one during a fresh Start. Without
        // this, the honesty guarantee has a hole one click wide.
        await Marker.MarkLiveAsync();

        Nav.NavigateTo("/quiz");
    }

    private void BackToSetup()
    {
        Nav.NavigateTo("/");
    }
}
