using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// End-user documentation for the quiz: the six beats of the flow (pick files →
/// filters → answering → scoring → review → stats/done) plus the handful of
/// semantics a user cannot discover by clicking around — auto-skipped pass
/// positions, off-list plays scoring as skips, a cube position counting as two
/// decisions, the dice-click shortcut, and reload-resets-everything.
///
/// <para>
/// A <c>.Client</c> WASM page rather than a static host page: a mid-quiz
/// Help → Back round trip must not disturb the WASM runtime holding the quiz
/// state, exactly as <see cref="Stats"/>'s round trip must not. It is interactive
/// only for the Back button, and <c>prerender: false</c> is mandatory regardless —
/// <see cref="QuizController"/> does not exist during a server prerender.
/// </para>
///
/// <para>
/// Unlike <see cref="Stats"/> this page never redirects: help is reachable from
/// any state, including before a quiz has started and from a browser bookmark.
/// Only the "Back to quiz" affordance is conditional, on the same
/// <c>HasStarted &amp;&amp; !IsFinished</c> predicate <see cref="Stats"/> guards
/// with — with no quiz in progress there is nowhere to go back to. Nothing on the
/// page changes while the user reads it, so it does not subscribe to
/// <see cref="QuizController.StateChanged"/>.
/// </para>
///
/// <para>
/// The stated file caps are rendered from <see cref="PickedFileLimits"/> — the same
/// constants <c>Home</c> enforces the pick against — rather than restated as prose,
/// so the page cannot document a rule the picker no longer applies.
/// </para>
/// </summary>
public partial class Help : ComponentBase
{
    private void BackToQuiz()
    {
        Nav.NavigateTo("/quiz");
    }
}
