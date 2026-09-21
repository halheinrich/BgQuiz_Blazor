using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// Settings page: a plain view over the app-scoped <c>QuizSettings</c> service.
/// Every control writes straight through to the service, which records and
/// persists the change on the spot — there is deliberately no Apply button, no
/// draft, and no dirty state to reconcile (see the service's own docs for why
/// that lifetime split is the one thing this page must not grow).
///
/// <para>
/// Recording immediately is not the same as showing immediately, and the fold
/// setting deliberately parts company with every other one: it takes hold on the
/// next navigation rather than folding the panel the user is standing in
/// (finding halheinrich/backgammon#50, reasoned in <c>QuizSettings.SetKeepNavigationPanelFoldedAsync</c>).
/// The page's job in that split is the words — the fold's fine print states the
/// deferral, so a user who sees nothing happen is not left reading it as a
/// failure.
/// </para>
///
/// <para>
/// The page therefore holds exactly one piece of state: whether hydration has
/// landed. It gates the controls so none of them can paint a default that the
/// stored settings are about to overwrite. In practice the gate is invisible —
/// <c>Home</c> hydrates at app start and the task is complete by the time anyone
/// navigates here — but a cold deep link to <c>/settings</c> is a real entry
/// point, and it is the one visit that would otherwise show the wrong state.
/// </para>
///
/// <para>
/// <c>@rendermode InteractiveWebAssembly(prerender: false)</c> is mandatory, as
/// on every routable page here: the scoped services these controls bind to do
/// not exist during a server prerender pass.
/// </para>
///
/// <para>
/// Like <see cref="Help"/> — and unlike <see cref="Stats"/> — this page never
/// redirects: settings are reachable from any state, including a cold bookmark.
/// So its way back is always present, and the shared <see cref="ReturnControl"/>
/// decides where it goes: <b>Back to quiz</b> while a quiz is live, <b>Back to
/// Home</b> otherwise (issue <c>halheinrich/backgammon#241</c>). The first half
/// is the page's answer to the mid-quiz round trip booked on issue
/// halheinrich/backgammon#30: the round trip already worked (the settings
/// service and the controller are both app-scoped, so nothing is lost either
/// way), but nothing pointed at it, and a user who changes the board side
/// mid-quiz had no visible way back. The second half closes the gap that
/// remained — with no quiz live the page offered no on-page way out at all. The
/// control belongs here rather than in the navigation panel because that panel
/// renders statically and cannot know a quiz is live.
/// </para>
/// </summary>
public partial class Settings : ComponentBase
{
    private bool _hydrated;

    /// <summary>Hydrate the settings, then let the controls render against them.</summary>
    protected override async Task OnInitializedAsync()
    {
        await UserSettings.EnsureHydratedAsync();
        _hydrated = true;
    }

    private Task SetHomeBoardOnRightAsync(bool value) =>
        UserSettings.SetHomeBoardOnRightAsync(value);

    private Task SetRandomizeSidePerProblemAsync(bool value) =>
        UserSettings.SetRandomizeSidePerProblemAsync(value);

    private Task SetMaximizeBoardWhileAnsweringAsync(bool value) =>
        UserSettings.SetMaximizeBoardWhileAnsweringAsync(value);

    private Task SetSortAnalysisByDepthFirstAsync(bool value) =>
        UserSettings.SetSortAnalysisByDepthFirstAsync(value);

    /// <summary>
    /// The hide ceiling, from the <c>&lt;select&gt;</c>'s posted token. The
    /// token vocabulary is <c>QuizSettings</c>'s — the same pair of members the
    /// option values are written with and the stored payload is read with — so
    /// the page maps nothing and cannot spell "hide nothing" a second way.
    /// </summary>
    private Task SetMaximumHiddenLevelAsync(string? token) =>
        UserSettings.SetMaximumHiddenCandidateAnalysisLevelAsync(
            QuizSettings.LevelFromToken(token));

    private Task SetWeightQuizzesByStatsAsync(bool value) =>
        UserSettings.SetWeightQuizzesByStatsAsync(value);

    private Task SetKeepNavigationPanelFoldedAsync(bool value) =>
        UserSettings.SetKeepNavigationPanelFoldedAsync(value);
}
