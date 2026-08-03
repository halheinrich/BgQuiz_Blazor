using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The Settings page and the two things about it that only a real browser can
/// show: that the home-board side actually moves the rendered board, and that
/// the fold setting survives navigation and reload.
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

        // Defaults, as a fresh visitor sees them: home board right, nothing else on.
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
    /// </summary>
    [Fact]
    public async Task HomeBoardSideMovesTheRenderedBoard_AndTheQuizSurvivesTheRoundTrip()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();

        Assert.True(await PointOneIsRightOfTheBarAsync(),
            "the default setting puts the home board — points 1-6 — on the right");

        await GoToSettingsAsync();
        await HomeBoardLeftRadio.CheckAsync();

        // Back to the quiz that was left running — the settings service is
        // app-scoped, so the round trip costs no quiz state.
        await Page.GoBackAsync();
        await ExpectUrlAsync("/quiz");
        await Expect(Page.GetByText("Problem 1")).ToBeVisibleAsync();

        Assert.False(await PointOneIsRightOfTheBarAsync(),
            "with the home board on the left, point 1 must render left of the bar");
    }

    /// <summary>
    /// The fold setting's whole point: unlike the rail's own click, the choice
    /// outlives navigation. Pinned against the app's own
    /// <c>NavigationManager</c> path (Start Quiz — what a user hits mid-quiz, and
    /// what <c>SidebarCollapseTests</c> shows resetting a hand-folded rail) and
    /// against a full reload, which is what the stored entry carries it through.
    ///
    /// <para>
    /// Every navigation here is driven from the page body rather than the nav
    /// menu, of necessity: once the setting is on, the panel is folded and its
    /// links are unclickable. That is the feature working, and it is also why the
    /// setting needs a page of its own to be turned back off from.
    /// </para>
    /// </summary>
    [Fact]
    public async Task KeepFoldedSurvivesNavigationAndReload()
    {
        await Page.SetViewportSizeAsync(DesktopWidth, DesktopHeight);
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await GoToSettingsAsync();

        await KeepFoldedCheckbox.CheckAsync();

        // Applied on the spot — no navigation needed to see it take effect.
        await ExpectFoldedAsync();

        // Back to Home: an in-app navigation, the case the rail alone loses to.
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

        // Turning it back off releases the panel just as immediately — the
        // setting is not a one-way door. Reached by URL because the nav is
        // folded away, exactly as a user would have to.
        await Page.GotoAsync(BaseUrl + "/settings");
        await Expect(KeepFoldedCheckbox).ToBeCheckedAsync();  // it round-tripped through storage
        await ExpectFoldedAsync();

        await KeepFoldedCheckbox.UncheckAsync();

        await Expect(CollapseCheckbox).Not.ToBeCheckedAsync();
        Assert.True(await PanelWidthAsync() > 0);
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
