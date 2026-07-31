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
///
/// <para>
/// The same discipline governs the filter facets, one tier up: what each filter
/// admits is rendered by <c>XgFilter_Razor</c>'s <c>FilterHelp</c>, embedded at the
/// end of the <i>Choose filters</i> section, and this page writes none of that prose
/// itself. Facet semantics belong to the lib that implements them; a copy here would
/// be a second encoding that drifts silently when the lib's rules change. What stays
/// app-level is everything <c>FilterHelp</c> cannot know: where the panel sits in the
/// start flow, that filters must be applied before Start, what the match count
/// counts, and how a weighted mix draws from it. Prose <c>FilterHelp</c> lacks is
/// extended in <c>XgFilter_Razor</c>, never restored here.
/// </para>
/// </summary>
public partial class Help : ComponentBase
{
    /// <summary>
    /// The <c>id</c> of <c>FilterHelp</c>'s storage section — the one anchor this
    /// page links into. A literal because it belongs to another repo and is not
    /// exported as a constant; the drift that would leave behind is caught where
    /// it can be, by the e2e test that clicks the link and asserts the target
    /// actually comes into view, rather than by anything in this assembly.
    /// </summary>
    private const string PanelStorageAnchor = "fh-what-is-remembered";

    /// <summary>
    /// Href for the data section's pointer into <c>FilterHelp</c>'s
    /// "what the panel remembers" — this page's own URL plus the fragment,
    /// deliberately <b>not</b> the bare <c>#fragment</c> an ordinary page would
    /// use.
    ///
    /// <para>
    /// A fragment-only href resolves against the document's <c>&lt;base
    /// href="/"&gt;</c>, not against the current address, so on <c>/help</c> it
    /// resolves to <c>/#fh-what-is-remembered</c> — the router matches
    /// <c>/</c>, and the reader is dropped on <c>Home</c> instead of moved down
    /// the page they were reading. Observed in a browser, not theorised.
    /// Computing the href from <see cref="NavigationManager.Uri"/> rather than
    /// writing <c>/help#…</c> keeps it correct if the app is ever served from a
    /// sub-path (where the base href is no longer <c>/</c>); the split drops any
    /// fragment already on the address, so re-rendering after the link has been
    /// followed cannot accumulate a second one.
    /// </para>
    /// </summary>
    private string PanelStorageHref =>
        $"{Nav.Uri.Split('#')[0]}#{PanelStorageAnchor}";

    private void BackToQuiz()
    {
        Nav.NavigateTo("/quiz");
    }
}
