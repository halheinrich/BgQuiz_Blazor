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
}
