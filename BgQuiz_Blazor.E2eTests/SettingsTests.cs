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

    /// <summary>
    /// The analysis-depth ceiling dropdown (<c>halheinrich/backgammon#66</c>).
    /// By id rather than by its accessible name, deliberately: the name is the
    /// setting's own prose and this suite already keys enough on copy.
    /// </summary>
    private ILocator HiddenLevelSelect => Page.Locator("#settingsHiddenLevel");

    /// <summary>
    /// The shrink-to-fit box the dropdown is measured against
    /// (<c>halheinrich/backgammon#170</c>) — see <c>app.css</c> for what the
    /// pair of classes does.
    /// </summary>
    private ILocator HiddenLevelField => Page.Locator(".hidden-level-field");

    /// <summary>
    /// The block the dropdown used to fill edge to edge, and so the yardstick
    /// for it no longer doing that. Reached through the dropdown rather than by
    /// position, since this page has four fieldsets and two of them are nested.
    /// </summary>
    private ILocator HiddenLevelFieldset =>
        Page.Locator("fieldset:has(#settingsHiddenLevel)");

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
    /// The depth-ceiling dropdown's selection survives a reload — the third
    /// thing about this page only a real browser can show
    /// (<c>halheinrich/backgammon#66</c>).
    ///
    /// <para>
    /// bUnit can pin the <c>value</c> attribute the page renders, and does; it
    /// cannot show that the attribute actually selects an option. Blazor does
    /// not apply <c>value</c> to a <c>&lt;select&gt;</c> the way it applies an
    /// attribute to an input — the options have to exist first, so the runtime
    /// defers it — and a rendered-but-unapplied value is a live-only failure
    /// mode: the user's stored choice would silently show as "Hide nothing"
    /// while the request still carried it. That is the shape the maximize and
    /// fold scenarios above exist for, applied to the one control on this page
    /// that is not a checkbox.
    /// </para>
    ///
    /// <para>
    /// The reload, not just the round trip, is the point: it discards the WASM
    /// runtime and re-hydrates from localStorage, so the selection shown
    /// afterwards came back through the storage format rather than from state
    /// the page never let go of.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DepthCeilingChoice_IsShownSelected_AfterAReload()
    {
        await BootHomeAsync();
        await GoToSettingsAsync();

        // A fresh visitor hides nothing, and the control says so rather than
        // sitting blank.
        await Expect(HiddenLevelSelect).ToHaveValueAsync(string.Empty);
        await Expect(HiddenLevelSelect).ToContainTextAsync("Hide nothing");

        // The ruling's own selection ("show only rollouts"), chosen by the LABEL
        // a user reads — which also proves the label and the value token belong
        // to the same option.
        await HiddenLevelSelect.SelectOptionAsync(new SelectOptionValue { Label = "XG Roller++" });
        await ExpectStoredCeilingAsync("XgRollerPlusPlus");

        await Page.ReloadAsync();
        await Expect(KeepFoldedCheckbox).ToBeVisibleAsync();  // hydration landed

        await Expect(HiddenLevelSelect).ToHaveValueAsync("XgRollerPlusPlus");

        // And cleared the way a user clears it, which is a real gesture and not
        // just the absence of one.
        await HiddenLevelSelect.SelectOptionAsync(new SelectOptionValue { Value = string.Empty });
        await ExpectStoredCeilingAsync(null);

        await Page.ReloadAsync();
        await Expect(KeepFoldedCheckbox).ToBeVisibleAsync();

        await Expect(HiddenLevelSelect).ToHaveValueAsync(string.Empty);
    }

    /// <summary>
    /// The depth-ceiling dropdown is as wide as its own options and no wider
    /// (<c>halheinrich/backgammon#170</c>). Bootstrap's <c>.form-select</c> is
    /// <c>width:100%</c>, so before the fix this control spanned the full width
    /// of the settings column to say a word as short as "XG Roller++".
    ///
    /// <para>
    /// Only a browser can show this. <c>PageTests</c> pins the two classes and
    /// the two rules they name, which is everything bUnit can reach — AngleSharp
    /// evaluates no CSS. What it cannot reach is whether a browser honours them:
    /// a Bootstrap upgrade that marked <c>.form-select</c>'s width
    /// <c>!important</c>, or a stylesheet that stopped being served, would leave
    /// every one of those assertions green and the control back at full width.
    /// </para>
    ///
    /// <para>
    /// <b>Inequalities, not a ratio.</b> The rule the fix implements is 115%,
    /// and the exact figure is deliberately not asserted here: it is the
    /// stylesheet's to state, it is already pinned there as a literal, and a
    /// geometric equality would turn every sub-pixel of platform font rounding
    /// into a red suite. What is asserted instead are the three things that are
    /// true at any font, zoom or window size and false the moment the mechanism
    /// stops working — the ruler is narrower than the column, the select is
    /// narrower than the column, and the select is wider than the ruler. That
    /// last one is what separates this from a plain <c>width:auto</c>: with no
    /// breathing room the two boxes would be identical.
    /// </para>
    ///
    /// <para>
    /// Retried and box-guarded like the point-1 geometry below: a rect read
    /// before layout settles measures nothing, and "nothing is narrower than the
    /// column" is a green for the wrong reason.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DepthCeilingDropdown_IsSizedFromItsOptions_NotFromThePage()
    {
        await Page.SetViewportSizeAsync(DesktopWidth, DesktopHeight);
        await BootHomeAsync();
        await GoToSettingsAsync();

        // The options are all present before anything is measured — the width is
        // read off the widest of them, so a half-rendered list is a smaller
        // control for a reason this test is not about.
        await Expect(HiddenLevelSelect).ToContainTextAsync("Hide nothing");
        await Expect(HiddenLevelSelect).ToContainTextAsync("XG Roller++");

        await ExpectToPassAsync(async () =>
        {
            var fieldset = await LaidOutBoxAsync(HiddenLevelFieldset, "the analysis-panel fieldset");
            var field = await LaidOutBoxAsync(HiddenLevelField, "the dropdown's sizing wrapper");
            var select = await LaidOutBoxAsync(HiddenLevelSelect, "the depth-ceiling dropdown");

            Assert.True(
                field.Width < fieldset.Width / 2,
                $"the sizing wrapper measured the page rather than the options "
                + $"(wrapper {field.Width}, fieldset {fieldset.Width}).");
            Assert.True(
                select.Width < fieldset.Width / 2,
                $"the dropdown is still sized from the page rather than from its "
                + $"options (select {select.Width}, fieldset {fieldset.Width}).");
            Assert.True(
                select.Width > field.Width,
                $"the dropdown got no breathing room past the width its options "
                + $"need (select {select.Width}, wrapper {field.Width}).");
        });
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

        await ExpectPointOneAsync(onTheRight: true,
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

        await ExpectPointOneAsync(onTheRight: false,
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
    ///
    /// <para>
    /// The width is a single read and correct as one
    /// (<c>halheinrich/backgammon#127</c>): it follows the retrying checkbox
    /// assertion above, and the fold is a <c>:checked ~ .sidebar</c> rule with no
    /// transition, so the settled control is the settled width. The mirror below
    /// reads <c>&gt; 0</c> rather than the panel's designed width for
    /// <c>SidebarCollapseTests.PanelWidthAsync</c>'s reason — that the layout
    /// stylesheet applied is <c>EnvironmentFidelityTests</c>' pin, and stating it
    /// again here would be a second source for it.
    /// </para>
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

    /// <summary>
    /// Wait until the stored settings entry carries the given depth ceiling —
    /// the same "the handler ran to completion" evidence
    /// <see cref="ExpectStoredFoldAsync"/> provides, and needed for the same
    /// reason: the assertions that follow are taken after a reload, which would
    /// happily race a write that had not gone out yet and then prove nothing.
    ///
    /// <para>
    /// Key and field are literals per this suite's independent-literal
    /// convention, and so is the token: it is the wire spelling of the level, so
    /// reading it here rather than the label is what makes this a check on the
    /// stored format rather than on the control.
    /// </para>
    /// </summary>
    /// <param name="token">The stored level's member name, or null for none.</param>
    private Task ExpectStoredCeilingAsync(string? token) =>
        Page.WaitForFunctionAsync(
            """
            expected => {
                try {
                    const raw = localStorage.getItem('xg_quizSettings');
                    if (raw === null) return false;
                    const stored = JSON.parse(raw).maximumHiddenCandidateAnalysisLevel;
                    return (stored ?? null) === expected;
                } catch (e) {
                    return false;
                }
            }
            """,
            token);

    private Task<double> PanelWidthAsync() =>
        NavigationPanel.EvaluateAsync<double>("el => el.getBoundingClientRect().width");

    /// <summary>
    /// Which side of the bar the board's point-1 region sits on — i.e. which side
    /// the on-roll player's home board (points 1-6) is drawn on.
    ///
    /// <para>
    /// Measured against the <b>bar</b>, not the diagram's midline: the rendered
    /// SVG also carries the analysis panel down one side, so its centre is
    /// nowhere near the board's. The bar is the board's own middle by
    /// construction, and both rects come from the producer's hit-region overlay
    /// (the geometry a user's clicks actually land on) via the render-order
    /// contract <c>E2eTestBase</c> already documents.
    /// </para>
    ///
    /// <para>
    /// <b>Retried, and both rects required to be laid out</b>
    /// (<c>halheinrich/backgammon#127</c>). This used to be a single read taken
    /// straight after landing back on the quiz page, which is the shape #126
    /// caught elsewhere in this suite: the overlay being visible says the board
    /// rendered, not that this render is the one carrying the new setting. And
    /// the comparison is between two rect centres, so a rect that measured
    /// nothing would still produce a confident true or false — the box guard
    /// makes that case name itself instead.
    /// </para>
    /// </summary>
    /// <param name="onTheRight">The side point 1 must be on.</param>
    /// <param name="because">What that side means, for the failure message.</param>
    private Task ExpectPointOneAsync(bool onTheRight, string because) =>
        ExpectToPassAsync(async () =>
        {
            var bar = await LaidOutBoxAsync(BarHitRect, "the bar's hit region");
            var pointOne = await LaidOutBoxAsync(HitRects.Nth(0), "point 1's hit region");

            bool isRight = pointOne.X + (pointOne.Width / 2) > bar.X + (bar.Width / 2);
            Assert.True(
                isRight == onTheRight,
                $"{because} (point 1 centre x={pointOne.X + (pointOne.Width / 2)}, "
                + $"bar centre x={bar.X + (bar.Width / 2)}).");
        });
}
