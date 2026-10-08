using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The route landmarks (<see cref="E2eTestBase.AppRoute"/>): what says a page
/// has rendered, as distinct from its URL having arrived
/// (halheinrich/backgammon#372, halheinrich/backgammon#374).
/// </summary>
public sealed class PageRenderedTests : E2eTestBase
{
    public PageRenderedTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    /// <summary>
    /// <b>Each landmark is on its own page and on no other.</b> One journey
    /// through every route, by the gestures a user makes: the navigation
    /// links, Start, Show stats, Back to quiz, an answer and Continue. On each
    /// page, once its own landmark is there, every other route's landmark is
    /// absent. That is the property that keeps a wait for a landmark from
    /// being satisfied by the page a navigation is leaving. The absences
    /// follow the page's own landmark, so none of them can pass on a page that
    /// has not rendered yet.
    /// </summary>
    [Fact]
    public async Task EachRoutesLandmark_IsOnItsOwnPage_AndOnNoOther()
    {
        // Wide enough that Show stats is a button of the row, not an item behind its "⋯".
        await Page.SetViewportSizeAsync(1600, 900);
        await BootHomeAsync();
        await ExpectOnlyTheLandmarkOfAsync(AppRoute.Home);

        await NavLink(ExpectedText.SettingsNavLink).ClickAsync();
        await ExpectOnlyTheLandmarkOfAsync(AppRoute.Settings);

        await NavLink(ExpectedText.HelpNavLink).ClickAsync();
        await ExpectOnlyTheLandmarkOfAsync(AppRoute.Help);

        await NavLink(ExpectedText.HomeNavLink).ClickAsync();
        await ExpectOnlyTheLandmarkOfAsync(AppRoute.Home);

        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();
        await ExpectOnlyTheLandmarkOfAsync(AppRoute.Quiz);

        await Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ShowStatsButton, Exact = true }).ClickAsync();
        await ExpectOnlyTheLandmarkOfAsync(AppRoute.Stats);

        await Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.BackToQuizButton }).ClickAsync();
        await ExpectOnlyTheLandmarkOfAsync(AppRoute.Quiz);

        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();
        await ExpectOnlyTheLandmarkOfAsync(AppRoute.Done);
    }

    private ILocator NavLink(string name) => Page.GetByRole(AriaRole.Link, new() { Name = name, Exact = true });

    /// <summary>
    /// The page at <paramref name="route"/> shows its own landmark, once, and
    /// no other route's.
    /// </summary>
    private async Task ExpectOnlyTheLandmarkOfAsync(AppRoute route)
    {
        await ExpectUrlAsync(route.Path);
        await Expect(route.LandmarkOn(Page)).ToHaveCountAsync(1);
        await Expect(route.LandmarkOn(Page)).ToBeVisibleAsync();
        foreach (var other in AppRoute.All.Where(r => r != route))
            await Expect(other.LandmarkOn(Page)).ToHaveCountAsync(0);
    }
}
