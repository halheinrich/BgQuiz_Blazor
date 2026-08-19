using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The Settings page and the two things about it that only a real browser can
/// show: that the home-board side actually moves the rendered board, and that
/// the fold setting defers to the next navigation and then survives every
/// navigation and reload after it.
///
/// <para>
/// Both are structurally invisible to the other layers. bUnit can pin which
/// <c>HomeBoardOnRight</c> the page asks the producer for, but not where the
/// checkers end up; and the fold is applied by an authored script running
/// outside the WASM runtime, against a statically rendered layout, on Blazor's
/// <c>enhancedload</c> — none of which exists in a bUnit render or in the
/// in-process host pipeline tests.
/// </para>
/// </summary>
public sealed class SettingsTests : E2eTestBase
{
    public SettingsTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    /// <summary>Comfortably past the 641px breakpoint the collapse rail lives behind.</summary>
    private const int DesktopWidth = 1280;

    private const int DesktopHeight = 800;

    private ILocator NavigationPanel => Page.Locator(".sidebar");

    /// <summary>The layout's collapse checkbox — the control the applier writes.</summary>
    private ILocator CollapseCheckbox => Page.Locator(".sidebar-toggle-checkbox");

    private ILocator KeepFoldedCheckbox =>
        Page.GetByRole(AriaRole.Checkbox, new() { Name = "Keep the navigation panel folded" });

    private ILocator HomeBoardLeftRadio => Page.GetByRole(AriaRole.Radio, new() { Name = "Left" });

    private ILocator HomeBoardRightRadio => Page.GetByRole(AriaRole.Radio, new() { Name = "Right" });

    /// <summary>The page's way back into a running quiz — absent when none is.</summary>
    private ILocator BackToQuizButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = "Back to quiz" });

    private async Task GoToSettingsAsync()
    {
        await Page.GetByRole(AriaRole.Link, new() { Name = "Settings" }).ClickAsync();
        await ExpectUrlAsync("/settings");
        await Expect(KeepFoldedCheckbox).ToBeVisibleAsync();
    }

    [Fact]
    public async Task SettingsPageIsReachableFromTheNav_AndCarriesItsOwnTitle()
    {
        await BootHomeAsync();

        await GoToSettingsAsync();

        await Expect(Page).ToHaveTitleAsync("BgQuiz — Settings");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Settings" })).ToBeVisibleAsync();

        // The side and fold defaults, as a fresh visitor sees them. (The maximize
        // default is on since #113 and is pinned by MaximizeBoardTests, which
        // reads it where it has a consequence — on the quiz page.)
        await Expect(HomeBoardRightRadio).ToBeCheckedAsync();
        await Expect(HomeBoardLeftRadio).Not.ToBeCheckedAsync();
        await Expect(KeepFoldedCheckbox).Not.ToBeCheckedAsync();
    }

    /// <summary>
    /// The home-board side, observed where it matters: on the board. Point 1
    /// belongs to the on-roll player's home board, so flipping the setting has to
    /// carry it across the bar — see <see cref="PointOneIsRightOfTheBarAsync"/>.
    /// The setting is changed mid-quiz and the quiz walked back into, which is
    /// also how a user will judge the choice: by looking at a real position.
    ///
    /// <para>
    /// The return leg goes through the page's own <b>Back to quiz</b> button
    /// (issue #30), which is what a user actually has. It used to go through
    /// <c>GoBack</c> — browser history — and that passed while the page offered
    /// no way back at all, which is precisely the gap the dogfood pass reported:
    /// the round trip worked and nothing pointed at it. Driving the affordance
    /// pins all three claims at once — that the button is there mid-quiz, that
    /// the run survives the trip (both services are app-scoped), and that the new
    /// side is on the board when the user lands back on it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task HomeBoardSideMovesTheRenderedBoard_AndBackToQuizResumesTheRun()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();

        Assert.True(await PointOneIsRightOfTheBarAsync(),
            "the default setting puts the home board — points 1-6 — on the right");

        await GoToSettingsAsync();
        await HomeBoardLeftRadio.CheckAsync();

        // Back to the quiz that was left running, by the page's own affordance.
        await BackToQuizButton.ClickAsync();
        await ExpectUrlAsync("/quiz");
        // The board is laid out again — the gate the geometry read below needs.
        // It used to wait on the "Problem 1" counter, which rides in the score
        // panel and is suppressed while answering under the maximize mode (the
        // default since #113); the overlay this reads its rects from is the
        // closer gate anyway.
        await Expect(HitOverlaySvg).ToBeVisibleAsync();

        Assert.False(await PointOneIsRightOfTheBarAsync(),
            "with the home board on the left, point 1 must render left of the bar");
    }

    /// <summary>
    /// The other half of the affordance's predicate, in the browser: with no quiz
    /// running the button is simply absent. Cheap to pin here and worth it — the
    /// bUnit tests assert the same thing against a controller a test built, while
    /// this asserts it on the page a first-time visitor actually lands on.
    /// </summary>
    [Fact]
    public async Task SettingsOffersNoWayBackWhenNoQuizIsRunning()
    {
        await BootHomeAsync();

        await GoToSettingsAsync();

        await Expect(BackToQuizButton).ToHaveCountAsync(0);
    }

    /// <summary>
    /// The fold setting's whole point: unlike the rail's own click, the choice
    /// outlives navigation. Pinned against the app's own
    /// <c>NavigationManager</c> path (Start Quiz — what a user hits mid-quiz, and
    /// what <c>SidebarCollapseTests</c> shows resetting a hand-folded rail) and
    /// against a full reload, which is what the stored entry carries it through.
    ///
    /// <para>
    /// It also pins the <b>asymmetry</b> finding #50 settled — on defers to the
    /// next navigation, off unfolds now. This test previously asserted the fold
    /// landing on the spot, which was the shipped contract until the ruling: the
    /// setting describes how pages <i>start</i>, and folding the page the user is
    /// standing in strands them behind a panel that just vanished. The rule that
    /// literal protected is unchanged and still asserted below — the choice must
    /// visibly take hold — it now takes hold one navigation later, and only the
    /// unfold direction still has to be immediate.
    /// </para>
    ///
    /// <para>
    /// Every navigation here is driven from the page body rather than the nav
    /// menu, of necessity: once the setting has taken hold the panel is folded
    /// and its links are unclickable. That is the feature working, and it is also
    /// why the setting needs a page of its own to be turned back off from.
    /// </para>
    /// </summary>
    [Fact]
    public async Task KeepFoldedDefersToTheNextNavigation_ThenSurvivesNavigationAndReload()
    {
        await Page.SetViewportSizeAsync(DesktopWidth, DesktopHeight);
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await GoToSettingsAsync();

        await KeepFoldedCheckbox.CheckAsync();

        // Recorded at once — waiting for the write is what makes the assertion
        // below an observation rather than a race with a handler yet to run.
        await ExpectStoredFoldAsync(true);

        // ...and the panel the user is standing in stays exactly where it is.
        await ExpectUnfoldedAsync();

        // Back to Home: an in-app navigation, the case the rail alone loses to —
        // and the navigation the deferred fold takes hold on.
        await Page.GoBackAsync();
        await Expect(PickFolderButton).ToBeVisibleAsync();
        await ExpectFoldedAsync();

        // Start Quiz: the app's NavigationManager path, and the one a user is on
        // when the fold matters most.
        await StartQuizAsync();
        await ExpectFoldedAsync();

        // A full reload discards the quiz, so this lands back on Home — and the
        // fold still survives both the boot and the bounce that follows it.
        await Page.ReloadAsync();
        await Expect(PickFolderButton).ToBeVisibleAsync();
        await ExpectFoldedAsync();

        // Turning it back off releases the panel on the spot — the deferral is
        // the on direction's alone, because a folded panel offers the user no
        // navigation to defer to. Reached by URL because the nav is folded away,
        // exactly as a user would have to.
        await Page.GotoAsync(BaseUrl + "/settings");
        await Expect(KeepFoldedCheckbox).ToBeCheckedAsync();  // it round-tripped through storage
        await ExpectFoldedAsync();

        await KeepFoldedCheckbox.UncheckAsync();

        await ExpectUnfoldedAsync();
    }

    /// <summary>
    /// Assert the panel is folded — both halves, because they can fail apart: the
    /// checkbox is what the applier writes, and the width is what the CSS does
    /// with it.
    /// </summary>
    private async Task ExpectFoldedAsync()
    {
        await Expect(CollapseCheckbox).ToBeCheckedAsync();
        Assert.Equal(0d, await PanelWidthAsync());
    }

    /// <summary>The mirror of <see cref="ExpectFoldedAsync"/>: panel open, both halves.</summary>
    private async Task ExpectUnfoldedAsync()
    {
        await Expect(CollapseCheckbox).Not.ToBeCheckedAsync();
        Assert.True(await PanelWidthAsync() > 0);
    }

    /// <summary>
    /// Wait until the stored settings entry carries the given fold value — the
    /// evidence that the toggle's handler ran to completion, since the write
    /// precedes anything else the setter does.
    ///
    /// <para>
    /// Needed only by the deferred direction, and needed there precisely because
    /// the assertion that follows is a <i>negative</i> one: "the panel did not
    /// fold" is trivially true of a page whose click has not been processed yet,
    /// so without this the test would pass on a broken app. The key and field are
    /// literals per this suite's independent-literal convention — the same reason
    /// <c>HelpAndTitlesTests</c> spells the key out rather than reading the
    /// constant the page renders from.
    /// </para>
    /// </summary>
    private Task ExpectStoredFoldAsync(bool folded) =>
        Page.WaitForFunctionAsync(
            """
            expected => {
                try {
                    const raw = localStorage.getItem('xg_quizSettings');
                    return raw !== null
                        && JSON.parse(raw).keepNavigationPanelFolded === expected;
                } catch (e) {
                    return false;
                }
            }
            """,
            folded);

    private Task<double> PanelWidthAsync() =>
        NavigationPanel.EvaluateAsync<double>("el => el.getBoundingClientRect().width");

    /// <summary>
    /// Whether the board's point-1 region sits right of the bar — i.e. whether
    /// the on-roll player's home board (points 1-6) is drawn on the right.
    ///
    /// <para>
    /// Measured against the <b>bar</b>, not the diagram's midline: the rendered
    /// SVG also carries the analysis panel down one side, so its centre is
    /// nowhere near the board's. The bar is the board's own middle by
    /// construction, and both rects come from the producer's hit-region overlay
    /// (the geometry a user's clicks actually land on) via the render-order
    /// contract <c>E2eTestBase</c> already documents.
    /// </para>
    /// </summary>
    private async Task<bool> PointOneIsRightOfTheBarAsync()
    {
        var bar = await BarHitRect.BoundingBoxAsync()
            ?? throw new InvalidOperationException("the bar's hit region is not laid out");
        var pointOne = await HitRects.Nth(0).BoundingBoxAsync()
            ?? throw new InvalidOperationException("point 1's hit region is not laid out");

        return pointOne.X + (pointOne.Width / 2) > bar.X + (bar.Width / 2);
    }
}
