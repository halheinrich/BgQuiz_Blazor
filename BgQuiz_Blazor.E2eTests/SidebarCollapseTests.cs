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

    /// <summary>
    /// The XGID badge's text node — the identity of the problem currently on
    /// screen, and this suite's per-step "the app advanced" marker. It lives in
    /// the action row's trailing cluster, which every view mode keeps
    /// (<c>SPEC-quiz-view.md</c> §4's one-home ruling), so unlike the score
    /// panel's problem counter it cannot be taken away by a setting that has
    /// nothing to do with the fold.
    /// </summary>
    private ILocator XgidBadgeText => Page.Locator(".action-row-tail .xgid-label-text");

    /// <summary>
    /// The XGID of the problem on screen, once it is there — the value a later
    /// step compares against to prove the run moved on.
    /// </summary>
    private async Task<string> CurrentProblemXgidAsync()
    {
        await Expect(XgidBadgeText).ToBeVisibleAsync();
        string xgid = (await XgidBadgeText.TextContentAsync())!;
        Assert.False(string.IsNullOrWhiteSpace(xgid),
            "the XGID badge is this suite's advance marker and must carry a value");
        return xgid;
    }

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

    /// <summary>
    /// The half of the story a user actually lives with: the reset is triggered
    /// by <i>navigation</i>, not by time or by interacting with the quiz. Submit,
    /// Skip and Continue-within-a-run are in-page WASM re-renders that never
    /// re-render the (static) layout, so the fold survives a whole worked run —
    /// which is exactly the case the tester was in, and why the collapse was
    /// reported as persisting. It gives way only on the navigation that ends the
    /// run.
    ///
    /// <para>
    /// Each step asserts the app actually advanced (the problem on screen changes)
    /// before asserting the panel stayed folded: a click that silently failed to
    /// land would otherwise read as "the fold survived answering" while nothing
    /// had been answered at all.
    /// </para>
    ///
    /// <para>
    /// <b>The advance is read off the XGID badge</b>, not the "Problem N"
    /// counter. The counter rides in the score panel, which the maximize mode
    /// suppresses while answering — and that mode is the default since #113, so
    /// the marker this test's discipline depends on had to be one the view mode
    /// cannot take away. The badge qualifies twice over: it has one home in both
    /// modes (<c>SPEC-quiz-view.md</c> §4) and it names the <i>position</i>, so a
    /// changed badge is a genuinely different problem rather than a counter that
    /// could in principle move without one.
    /// </para>
    ///
    /// <para>
    /// The run begins by folding, navigating, and waiting for the fold to be
    /// undone — see <see cref="WaitForTheEnhancedNavSettleAsync"/>. That is not
    /// part of the scenario; it is how the scenario refuses to race the
    /// navigation's own late DOM synchronization before it starts (issue #46).
    /// </para>
    /// </summary>
    [Fact]
    public async Task FoldSurvivesAWorkedRun_AndGivesWayOnlyToTheNavigationThatEndsIt()
    {
        await Page.SetViewportSizeAsync(DesktopWidth, DesktopHeight);
        await BootHomeAsync();
        await PickCubeProblemsAsync(3);
        await ApplyFilterAsync();

        // Fold BEFORE starting, purely so the navigation's DOM synchronization
        // has something to visibly undo — see WaitForTheEnhancedNavSettleAsync.
        await CollapseRail.ClickAsync();
        await StartQuizAsync();
        await WaitForTheEnhancedNavSettleAsync();

        await CollapseRail.ClickAsync();
        Assert.Equal(0d, await PanelWidthAsync());
        string firstProblem = await CurrentProblemXgidAsync();

        // Skip: advances without scoring, entirely in-page.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Skip" }).ClickAsync();
        await Expect(XgidBadgeText).Not.ToHaveTextAsync(firstProblem);
        string secondProblem = await CurrentProblemXgidAsync();
        Assert.Equal(0d, await PanelWidthAsync());

        // Submit: scores and shows the solution, still in-page.
        await AnswerCubeNoDoubleAsync();
        Assert.Equal(0d, await PanelWidthAsync());

        // Continue through that solution — the step that ends a run when it is
        // the last problem, and here is just an advance.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Expect(XgidBadgeText).Not.ToHaveTextAsync(secondProblem);
        await ExpectUrlAsync("/quiz");
        Assert.Equal(0d, await PanelWidthAsync());
        await Expect(CollapseRail).ToBeCheckedAsync();

        // The last Continue exhausts the source and navigates to /done — and
        // that navigation, not the answering before it, is what unfolds it.
        await AnswerCubeNoDoubleAsync();
        Assert.Equal(0d, await PanelWidthAsync());
        await ContinueToDoneAsync();

        await Expect(CollapseRail).Not.ToBeCheckedAsync();
        Assert.True(await PanelWidthAsync() > 0,
            "the navigation that ends the run brings the panel back");
    }

    /// <summary>
    /// Wait for the navigation just performed to have finished resetting the
    /// rail — the caller having folded it <i>before</i> navigating, so that
    /// reset is observable.
    ///
    /// <para>
    /// <b>Why this is needed (umbrella issue #46).</b> Enhanced navigation's DOM
    /// synchronization is what clears the checkbox, and it does not land with the
    /// navigation: on the deployed host it has been measured arriving ~500ms
    /// later, while on localhost it beats any human or any Playwright click. A
    /// scenario that folds the rail immediately after navigating therefore races
    /// a reset that is still in flight, and fails on a slow host while passing
    /// everywhere else — pinning the host's latency instead of the app's rule.
    /// </para>
    ///
    /// <para>
    /// <b>Why it waits on this and not on a timer or the network.</b> Suite
    /// policy is to wait on an observable consequence, never a sleep and never
    /// network-idle (see <c>ExpectUrlAsync</c> and <c>EmptyFilterBannerTests</c>).
    /// The consequence available here is the reset itself: fold the rail before
    /// the navigation and the sync's arrival is exactly the moment the checkbox
    /// goes back to unchecked. That is app behaviour this suite already pins as a
    /// rule in <see cref="CollapseLastsUntilTheNextNavigationOrReload"/>, so no
    /// test seam, injected observer, or knowledge of Blazor's internals is
    /// needed — and a settle that never arrives fails loudly on the assertion's
    /// own timeout rather than passing for the wrong reason.
    /// </para>
    /// </summary>
    private Task WaitForTheEnhancedNavSettleAsync() =>
        Expect(CollapseRail).Not.ToBeCheckedAsync();

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
