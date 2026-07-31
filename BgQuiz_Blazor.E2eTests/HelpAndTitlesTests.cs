using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The Help route and the document-title contract. Titles were one of the four
/// invisible-to-tests production defects: every page's <c>&lt;PageTitle&gt;</c>
/// looked correct in source while <c>document.title</c> stayed inert, because a
/// bare <c>HeadOutlet</c> participates only in the static render pass. Only a
/// real browser running the booted WASM runtime can observe the fixed behavior,
/// so it is pinned here.
/// </summary>
public sealed class HelpAndTitlesTests : E2eTestBase
{
    public HelpAndTitlesTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    [Fact]
    public async Task HelpRendersAndTitlesChangeAcrossNavigation()
    {
        // Cold visit to /help — the page never redirects, from any state.
        await Page.GotoAsync(BaseUrl + "/help");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "How BgQuiz works" }))
            .ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Pick your folder" }))
            .ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Scoring" }))
            .ToBeVisibleAsync();

        // The facet reference is XgFilter_Razor's FilterHelp, embedded rather
        // than restated. bUnit already pins the embed; what only a browser can
        // show is that the producer component actually renders inside the
        // published WASM app — a component pulled from a project reference that
        // never reached the trimmed output would leave this section silently
        // blank while every unit test stayed green.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Analysis depth", Exact = true }))
            .ToBeVisibleAsync();

        // Post-boot the interactive HeadOutlet must carry the page title (the
        // static fallback alone would leave it at plain "BgQuiz").
        await Expect(Page).ToHaveTitleAsync("BgQuiz — Help");

        // Per-page titles change across in-app navigation, both directions —
        // via the nav menu, whose Help link is the sole /help entry point.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Home" }).ClickAsync();
        await Expect(Page).ToHaveTitleAsync("BgQuiz");

        await Page.GetByRole(AriaRole.Link, new() { Name = "Help" }).ClickAsync();
        await Expect(Page).ToHaveTitleAsync("BgQuiz — Help");
    }
}
