using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Client.Components;

/// <summary>
/// The return control on the pages a user visits <i>from</i> somewhere —
/// <c>Settings</c>, <c>Help</c> and <c>Stats</c>: <b>Back to quiz</b> while a
/// quiz is live, otherwise <b>Back to Home</b>, to the setup page (issue
/// <c>halheinrich/backgammon#241</c>). The single owner of that rule and of both
/// labels, so the three pages cannot disagree about either.
///
/// <para>
/// <b>Always present.</b> The pages used to offer a way back only while a
/// quiz was live, which left a visitor with nothing on the page to leave by —
/// a user rejected a release candidate partly on that. There is always
/// somewhere to go back to: the running quiz if there is one, and the setup
/// page, where every quiz begins, if there is not. "Home" is the navigation
/// panel's own word for that page, so the button and the panel name it alike.
/// </para>
///
/// <para>
/// <b>Live</b> is <see cref="QuizController.HasStarted"/> and not
/// <see cref="QuizController.IsFinished"/> — the predicate the pages' conditional
/// buttons used, moved here rather than restated. A finished quiz is not
/// something to go back to: its summary is <c>Done</c>'s, reached from the quiz
/// itself.
/// </para>
///
/// <para>
/// <b>Not Home's own button.</b> <c>Home</c> offers "Back to quiz" only while a
/// quiz is live and nothing otherwise, because Home <i>is</i> the setup page
/// and does not return to itself. It is a different control with a different
/// rule, and deliberately does not render this one.
/// </para>
///
/// <para>
/// Navigation only, like the buttons it replaces: the quiz state it returns
/// to is app-scoped and is not touched by the visit. It reads the controller
/// once per render and does not subscribe to it — nothing on the hosting pages
/// moves the quiz's state while they are shown.
/// </para>
/// </summary>
public partial class ReturnControl : ComponentBase
{
    /// <summary>The route of the running quiz.</summary>
    private const string QuizRoute = "/quiz";

    /// <summary>The route of the setup page, where every quiz begins.</summary>
    private const string HomeRoute = "/";

    /// <summary>
    /// The button's own styling, which every host shares; <see cref="Class"/>
    /// adds the host's placement after it.
    /// </summary>
    private const string OwnClass = "btn btn-primary";

    [Inject]
    private QuizController Controller { get; set; } = default!;

    [Inject]
    private NavigationManager Nav { get; set; } = default!;

    /// <summary>
    /// Layout classes for where the host places the button (a spacing utility
    /// such as <c>mt-3</c>), appended to the button's own styling. The host
    /// positions the control; what it says and where it goes are not the host's.
    /// </summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>Whether a quiz is under way to go back to.</summary>
    private bool QuizIsLive => Controller.HasStarted && !Controller.IsFinished;

    private string Label => QuizIsLive ? "Back to quiz" : "Back to Home";

    private string ButtonClass =>
        string.IsNullOrWhiteSpace(Class) ? OwnClass : $"{OwnClass} {Class}";

    private void Return() => Nav.NavigateTo(QuizIsLive ? QuizRoute : HomeRoute);
}
