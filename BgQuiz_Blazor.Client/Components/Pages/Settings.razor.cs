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
/// setting deliberately parts company with the other two: it takes hold on the
/// next navigation rather than folding the panel the user is standing in
/// (finding #50, reasoned in <c>QuizSettings.SetKeepNavigationPanelFoldedAsync</c>).
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
/// Only the "Back to quiz" affordance is conditional, on the same
/// <c>HasStarted &amp;&amp; !IsFinished</c> predicate both siblings use. It is
/// the page's answer to the mid-quiz round trip booked on issue #30: the round
/// trip already worked (the settings service and the controller are both
/// app-scoped, so nothing is lost either way), but nothing pointed at it, and a
/// user who changes the board side mid-quiz has no visible way back. The
/// affordance belongs here rather than in the navigation panel because that
/// panel renders statically and cannot know a quiz is live.
/// </para>
/// </summary>
public partial class Settings : ComponentBase
{
    private bool _hydrated;

    /// <summary>Hydrate the settings, then let the controls render against them.</summary>
    protected override async Task OnInitializedAsync()
    {
        await QuizSettings.EnsureHydratedAsync();
        _hydrated = true;
    }

    private Task SetHomeBoardOnRightAsync(bool value) =>
        QuizSettings.SetHomeBoardOnRightAsync(value);

    private Task SetRandomizeSidePerProblemAsync(bool value) =>
        QuizSettings.SetRandomizeSidePerProblemAsync(value);

    private Task SetKeepNavigationPanelFoldedAsync(bool value) =>
        QuizSettings.SetKeepNavigationPanelFoldedAsync(value);

    private void BackToQuiz()
    {
        Nav.NavigateTo("/quiz");
    }
}
