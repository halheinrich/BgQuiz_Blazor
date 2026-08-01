using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The desktop navigation-panel collapse. The control is pure CSS by necessity —
/// <c>MainLayout</c> cannot be interactive (see <c>MainLayoutTests</c>' class
/// docs), so the state is a checkbox and the collapse is a
/// <c>:checked ~ .sidebar</c> rule. bUnit's AngleSharp has no CSS engine and can
/// pin only the DOM contract that rule depends on; whether the panel actually
/// folds, whether the control says which state it is in, and how long the choice
/// lasts are observable only in a real browser, so they are pinned here.
/// </summary>
public sealed class SidebarCollapseTests : E2eTestBase
{
    public SidebarCollapseTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    /// <summary>
    /// Comfortably past the 641px breakpoint the rail lives behind — below it the
    /// rail is <c>display:none</c> and mobile's own navbar-toggler is the control.
    /// Set explicitly rather than inherited from Playwright's default viewport, so
    /// a default change can never silently move these scenarios to the far side of
    /// the breakpoint and leave them passing for the wrong reason.
    /// </summary>
    private const int DesktopWidth = 1280;

    private const int DesktopHeight = 800;

    /// <summary>
    /// The collapse rail, addressed by its accessible name — the same handle a
    /// screen-reader user has. Until this change the control carried only a
    /// <c>title</c>, so the name is itself part of what shipped.
    /// </summary>
    private ILocator CollapseRail =>
        Page.GetByRole(AriaRole.Checkbox, new() { Name = "Hide navigation panel" });

    private ILocator NavigationPanel => Page.Locator(".sidebar");

    [Fact]
    public async Task RailFoldsTheNavigationPanelAway_AndItsChevronReversesToMatch()
    {
        await Page.SetViewportSizeAsync(DesktopWidth, DesktopHeight);
        await BootHomeAsync();

        await Expect(CollapseRail).ToBeVisibleAsync();
        await Expect(CollapseRail).Not.ToBeCheckedAsync();
        Assert.True(await PanelWidthAsync() > 0, "the panel should start out showing");

        string expandedChevron = await RailChevronAsync();
        Assert.NotEqual("none", expandedChevron);

        await CollapseRail.ClickAsync();

        await Expect(CollapseRail).ToBeCheckedAsync();
        Assert.Equal(0d, await PanelWidthAsync());

        // The affordance's whole point (umbrella issue #29): the control reads as
        // a control and says which state it is in. The artwork is craft and
        // deliberately not pinned — that it CHANGES with the state is the
        // contract, and a chevron frozen in one direction is the regression.
        Assert.NotEqual(expandedChevron, await RailChevronAsync());

        // ...and it is a toggle, not a one-way door.
        await CollapseRail.ClickAsync();
        await Expect(CollapseRail).Not.ToBeCheckedAsync();
        Assert.True(await PanelWidthAsync() > 0);
    }

    /// <summary>
    /// How long the choice lasts — the claim Help makes to the reader, pinned
    /// rather than assumed. The layout renders statically, so Blazor's enhanced
    /// navigation re-renders it on every route change and its DOM synchronization
    /// resets the checkbox; a full reload resets it for the ordinary reason.
    /// Exercised through the app's own <c>NavigationManager</c> path (Start Quiz)
    /// rather than an anchor click, because those are different code paths and
    /// only the former is what a user hits mid-quiz.
    /// </summary>
    [Fact]
    public async Task CollapseLastsUntilTheNextNavigationOrReload()
    {
        await Page.SetViewportSizeAsync(DesktopWidth, DesktopHeight);
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();

        await CollapseRail.ClickAsync();
        Assert.Equal(0d, await PanelWidthAsync());

        await StartQuizAsync();

        await Expect(CollapseRail).Not.ToBeCheckedAsync();
        Assert.True(await PanelWidthAsync() > 0,
            "in-app navigation brings the panel back — Help tells the reader so");

        await CollapseRail.ClickAsync();
        Assert.Equal(0d, await PanelWidthAsync());

        await Page.ReloadAsync();
        await Expect(PickFolderButton).ToBeVisibleAsync();

        await Expect(CollapseRail).Not.ToBeCheckedAsync();
        Assert.True(await PanelWidthAsync() > 0,
            "a reload brings the panel back — Help tells the reader so");
    }

    private Task<double> PanelWidthAsync() =>
        NavigationPanel.EvaluateAsync<double>("el => el.getBoundingClientRect().width");

    /// <summary>
    /// The rail's chevron as the browser resolves it. Computed style, not the
    /// markup: the state signal is drawn entirely by CSS, so nothing in the DOM
    /// would show a stuck glyph.
    /// </summary>
    private Task<string> RailChevronAsync() =>
        CollapseRail.EvaluateAsync<string>("el => getComputedStyle(el).backgroundImage");
}
