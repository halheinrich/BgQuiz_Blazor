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

        // The entries BgQuiz keeps in the browser, named. The literals are
        // deliberately the real key strings — this is the pin that would catch a
        // page rendering an empty <code> or the wrong constant.
        await Expect(body).ToContainTextAsync("xg_quizMix");
        await Expect(body).ToContainTextAsync("xg_quizSettings");
        await Expect(body).ToContainTextAsync("bgquiz.quizLive");

        // ...and the sessionStorage marker described honestly: per-tab, and gone
        // with the tab. Its own XML docs record why it must never be "upgraded"
        // to localStorage, so the user-facing text must not claim otherwise.
        await Expect(body).ToContainTextAsync("belongs to this browser tab alone");
        await Expect(body).ToContainTextAsync("disappears when you close this one");

        // Naming what is stored is only half of "your data stays yours" — the
        // other half is that the reader can act on it (issue #54). This used to
        // be one sentence routing the whole answer through "your browser's
        // developer tools"; it now leads with the setting every reader already
        // has. Three literals, each a claim that could quietly be dropped: the
        // agency, the route being complete (all three go at once), and the
        // answer to the question clearing site data actually raises — whether it
        // takes the reader's own positions with it.
        await Expect(body).ToContainTextAsync("they are yours to delete");
        await Expect(body).ToContainTextAsync("site has stored");
        await Expect(body).ToContainTextAsync("removes all three at once");
        await Expect(body).ToContainTextAsync("files in your problem folder are untouched");

        // The consequence the section is now asked to draw (issue #51): having
        // accounted for everything BgQuiz keeps, it says the thing a reader
        // cannot infer from a list — that closing the tab is safe, mid-quiz
        // included. Ruled as words rather than a button: a "finish and quit"
        // control would invent an obligation the app does not have, so this
        // assertion is the only thing standing between the reassurance and
        // silently disappearing.
        await Expect(body).ToContainTextAsync("safe to close the tab whenever you like");
        await Expect(body).ToContainTextAsync("written to your folder as you answer rather than at the end");
        await Expect(body).ToContainTextAsync("the things that do not outlive the tab");

        // The scope caveat, which is what keeps the reassurance honest: where the
        // browser cannot write into the folder there is no record being kept, so
        // the claim must not read as a promise that one is. Help draws that
        // distinction in full under Lifetime stats; this is the pointer, and the
        // pointer is the part that could quietly be dropped as "obvious".
        await Expect(body).ToContainTextAsync("nothing is being recorded to begin with");
    }

    /// <summary>
    /// The note covering the desktop collapse rail. A beta tester asked for the
    /// side panel gone while quizzing when the control that does exactly that
    /// already existed, so the fix is half affordance and half telling people it
    /// is there — and the telling half is only real if the words are on the page.
    /// Independent literals per the copy-pin split; the behaviour the words
    /// describe is pinned in <c>SidebarCollapseTests</c>.
    /// </summary>
    [Fact]
    public async Task HelpDescribesTheCollapsibleSidePanel()
    {
        await Page.GotoAsync(BaseUrl + "/help");

        var body = Page.Locator("body");

        await Expect(body).ToContainTextAsync("The side panel folds out of the way");
        await Expect(body).ToContainTextAsync("narrow strip down the left edge");

        // The state signal, and both halves of the lifetime — the things a reader
        // cannot work out by looking at the control. The positive half leads: the
        // fold surviving a worked run is the fact that makes the control worth
        // using, and a note carrying only the reset would undersell it.
        await Expect(body).ToContainTextAsync("points the way the panel will move");
        await Expect(body).ToContainTextAsync("stays put while you work through the quiz");
        await Expect(body).ToContainTextAsync("comes back when you move to another page or reload");

        // ...and the way out of that reset, which is the whole reason the fold
        // setting exists. Named by the words on the control, so a reader can find
        // it; the behaviour is pinned in SettingsTests.
        await Expect(body).ToContainTextAsync("Keep the navigation panel folded");

        // The note is about the CONTROL, never an inventory of what the panel
        // contains — prose naming the nav's entries rots the day one is added.
        // The literal this originally guarded with ("Settings") stopped being a
        // usable proxy when #30 landed: the note now legitimately points at the
        // Settings *page* as where the fold option lives, which is a pointer to
        // an option and not a listing of the panel's contents. Home and Help have
        // no such reason to appear, so they are what an inventory would drag in.
        string note = await Page.Locator("li")
            .Filter(new() { HasText = "The side panel folds out of the way" })
            .InnerTextAsync();
        Assert.DoesNotContain("Home", note);
        Assert.DoesNotContain("Help", note);
    }

    /// <summary>
    /// Help's account of the answer-type breakdown. The zero reading is the part
    /// that has to be in words: a user looking at "Too good to double: 0" can
    /// read it as a category that failed to load unless the documentation says
    /// the list is exhaustive and a zero is a fact about their folder.
    /// Independent literals per the copy-pin split; that the paragraph does not
    /// recite the bucket labels themselves is pinned in bUnit, against the
    /// display class that owns them.
    /// </summary>
    [Fact]
    public async Task HelpExplainsTheAnswerTypeBreakdownAndWhatAZeroMeans()
    {
        await Page.GotoAsync(BaseUrl + "/help");

        var body = Page.Locator("body");

        await Expect(body).ToContainTextAsync("breaks the pool down by the kind of");
        await Expect(body).ToContainTextAsync("Every kind is listed every time");
        await Expect(body).ToContainTextAsync("holds none of that kind");
        await Expect(body).ToContainTextAsync("If you suspect your saved positions lean one way");
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
