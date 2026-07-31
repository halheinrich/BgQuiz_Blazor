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

    /// <summary>
    /// The data-ownership and storage assurance a beta tester asked for. Content
    /// is pinned here as independent literals (the wiring — that the two key
    /// names are rendered from the constants that write them — is asserted in
    /// bUnit), per the copy-pin split: a test that read the same constant the
    /// page reads would agree with any wording at all, including none.
    /// </summary>
    [Fact]
    public async Task HelpStatesDataOwnershipAndNamesTheStorageBgQuizKeeps()
    {
        await Page.GotoAsync(BaseUrl + "/help");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Your data stays yours" }))
            .ToBeVisibleAsync();

        var body = Page.Locator("body");

        // The ownership claim itself: the files are the reader's, they are parsed
        // in the browser, and there is no account and no server to send them to.
        await Expect(body).ToContainTextAsync("Your positions and matches are yours");
        await Expect(body).ToContainTextAsync("parses them inside your browser");
        await Expect(body).ToContainTextAsync("never uploaded");
        await Expect(body).ToContainTextAsync("no account to create");

        // The two entries BgQuiz keeps in the browser, named. The literals are
        // deliberately the real key strings — this is the pin that would catch a
        // page rendering an empty <code> or the wrong constant.
        await Expect(body).ToContainTextAsync("xg_quizMix");
        await Expect(body).ToContainTextAsync("bgquiz.quizLive");

        // ...and the sessionStorage marker described honestly: per-tab, and gone
        // with the tab. Its own XML docs record why it must never be "upgraded"
        // to localStorage, so the user-facing text must not claim otherwise.
        await Expect(body).ToContainTextAsync("belongs to this browser tab alone");
        await Expect(body).ToContainTextAsync("disappears when you close this one");
    }

    /// <summary>
    /// The pointer into XgFilter_Razor's own storage section has to actually
    /// land. A bare fragment href goes through Blazor's navigation interception
    /// in the running app, so "the anchor is in the markup" is not evidence that
    /// clicking it moves the reader — only a real browser can show that, and a
    /// dead pointer is worse than no pointer at all.
    /// </summary>
    [Fact]
    public async Task HelpDataSectionAnchorScrollsToTheFilterPanelsStorageSection()
    {
        await Page.GotoAsync(BaseUrl + "/help");

        var target = Page.Locator("#fh-what-is-remembered");
        await Expect(target).ToBeVisibleAsync();

        // The target sits far below the fold on load; proving the click moved the
        // page means proving it was NOT in view first.
        Assert.False(await IsInViewportAsync(target));

        await Page.GetByRole(AriaRole.Link, new() { Name = "What the panel remembers" }).ClickAsync();

        await Expect(target).ToBeVisibleAsync();
        Assert.True(await IsInViewportAsync(target));
        Assert.EndsWith("#fh-what-is-remembered", Page.Url);
    }

    /// <summary>
    /// Whether the element's box currently intersects the viewport. Playwright's
    /// <c>IsVisibleAsync</c> answers "rendered and not hidden", which is true for
    /// a heading ten screens down — it cannot distinguish "the anchor worked"
    /// from "the anchor did nothing", which is exactly the question here.
    /// </summary>
    private static async Task<bool> IsInViewportAsync(ILocator locator) =>
        await locator.EvaluateAsync<bool>(
            """
            el => {
                const r = el.getBoundingClientRect();
                return r.top < window.innerHeight && r.bottom > 0;
            }
            """);
}
