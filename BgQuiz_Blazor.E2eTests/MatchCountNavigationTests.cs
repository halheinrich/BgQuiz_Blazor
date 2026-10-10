using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The match count across in-app navigation, in a real browser
/// (halheinrich/backgammon#374, Arc 2 item 3). The count used to live on Home
/// and die with it: on the v1.12.1 production build, leaving Home for Settings
/// and coming back showed no count, and a filter that matched nothing no
/// longer closed Start (reproduced 2026-10-09). The count is now held by the
/// app under its inputs, so the returning page shows it, and its known-zero
/// gate, without counting again. What the holder reuses, recounts and
/// discards is pinned in bUnit (<c>MatchCountTests</c>, <c>PageTests</c>);
/// this is the user's path through the real navigation.
/// </summary>
public sealed class MatchCountNavigationTests : E2eTestBase
{
    public MatchCountNavigationTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright, output) { }

    private async Task GoToSettingsAndBackAsync()
    {
        await Page.GetByRole(AriaRole.Link, new() { Name = ExpectedText.SettingsNavLink, Exact = true }).ClickAsync();
        await ExpectPageRenderedAsync(AppRoute.Settings);
        await Page.GetByRole(AriaRole.Link, new() { Name = ExpectedText.HomeNavLink, Exact = true }).ClickAsync();
        await ExpectPageRenderedAsync(AppRoute.Home);
    }

    [Fact]
    public async Task TheCount_IsStillThere_AfterSettingsAndBack()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ExpectFilterInEffectAsync();
        await Expect(Page.GetByText(ExpectedText.DecisionsMatchYourFilters(1))).ToBeVisibleAsync();

        await GoToSettingsAndBackAsync();

        await Expect(Page.GetByText(ExpectedText.DecisionsMatchYourFilters(1))).ToBeVisibleAsync();
        await Expect(StartButton).ToBeEnabledAsync();
    }

    [Fact]
    public async Task AKnownZero_StillClosesStart_AfterSettingsAndBack()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);

        // A player no file carries: the applied filter matches nothing.
        await ExpandFacetRowAsync("Players");
        await Page.Locator("input[aria-describedby='facetHint_Players']").FillAsync("Nobody Anyone Knows");
        await ApplyFilterAsync();
        await Expect(Page.Locator("#noMatchNotice")).ToContainTextAsync(ExpectedText.DecisionsMatchYourFilters(0));
        await Expect(StartButton).ToBeDisabledAsync();

        await GoToSettingsAndBackAsync();

        await Expect(Page.Locator("#noMatchNotice")).ToContainTextAsync(ExpectedText.DecisionsMatchYourFilters(0));
        await Expect(StartButton).ToBeDisabledAsync();
    }
}
