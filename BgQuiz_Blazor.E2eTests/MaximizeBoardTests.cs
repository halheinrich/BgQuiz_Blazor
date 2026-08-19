using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The maximize-board mode's primary path in a real browser (issue
/// <c>halheinrich/backgammon#41</c>, conforming to <c>SPEC-quiz-view.md</c> §4):
/// run a quiz and watch the answering composition shed its chrome and the
/// review composition put it back — then untick the setting and watch the
/// chrome stay.
///
/// <para>
/// <b>The setting is driven through the Settings page, not by seeding
/// localStorage.</b> The chain this leg adds is exactly checkbox → service →
/// persisted entry → the quiz page's derivation reading it back, and a seeded
/// entry would skip the first two links of it — the smoke would then pass with
/// the control wired to nothing.
/// </para>
///
/// <para>
/// <b>Which test carries that chain inverted at #113.</b> The mode is the
/// default since 2026-08-19 (<c>SPEC-quiz-view.md</c> §3), so ticking the box
/// no longer moves anything: the maximized scenario simply starts a quiz, and
/// it is <see cref="WithTheSettingOff_AnsweringKeepsItsChrome"/> — the one whose
/// composition a user now has to ask for — that drives the control and so
/// exercises all four links. Same chain, opposite direction; the round trip out
/// to Settings and back to Home for the pick moved with it.
/// </para>
///
/// <para>
/// <b>Chrome presence/absence, never pixels.</b> The board genuinely grows
/// (§2 measured ~28% linearly, ~64% by area at 1440×900), but pinning a
/// measured size here would pin the fixture, the viewport, the fonts, and the
/// producer's geometry all at once and fail for any of them. What the mode
/// promises structurally is which chrome renders; the size follows from the
/// vertical law and the canvas, both of which have their own tests upstream.
/// </para>
/// </summary>
public sealed class MaximizeBoardTests : E2eTestBase
{
    public MaximizeBoardTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    /// <summary>The ongoing-stats strip — suppressed while answering under the mode.</summary>
    private ILocator ScorePanel => Page.Locator(".score-panel");

    /// <summary>The fixed-height legend/verdict strip — suppressed with it.</summary>
    private ILocator StatusStrip => Page.Locator(".status-strip");

    /// <summary>
    /// The XGID badge, in the one home <c>SPEC-quiz-view.md</c> §4 gives it
    /// (issue <c>halheinrich/backgammon#98</c>): the action row's trailing
    /// cluster, in both view modes and both states.
    /// </summary>
    private ILocator XgidBadge => Page.Locator(".action-row-tail .xgid-label");

    private ILocator MaximizeCheckbox => Page.GetByRole(
        AriaRole.Checkbox, new() { Name = "Make the board as large as possible while you answer" });

    /// <summary>
    /// Turn the mode off the way a user does: navigate to Settings and untick the
    /// box. Waits for the unchecked state to land, which is also the service's
    /// write having happened — the control is bound to the property the setter
    /// assigns before it persists.
    ///
    /// <para>
    /// This was <c>EnableMaximizeAsync</c> and ticked the box. Renaming it would
    /// not have been enough: since #113 made the mode the default, the very same
    /// gesture on the very same control turns the mode <i>off</i>, so the helper
    /// changed meaning rather than spelling, and moved to the scenario that wants
    /// the other composition.
    /// </para>
    /// </summary>
    private async Task DisableMaximizeAsync()
    {
        await Page.GetByRole(AriaRole.Link, new() { Name = "Settings" }).ClickAsync();
        await ExpectUrlAsync("/settings");
        await MaximizeCheckbox.UncheckAsync();
        await Expect(MaximizeCheckbox).Not.ToBeCheckedAsync();
    }

    [Fact]
    public async Task MaximizedAnsweringShedsItsChrome_AndReviewPutsItBack()
    {
        // No trip to Settings: since #113 this is what a visitor gets by walking
        // straight in, and that is the more valuable thing to smoke. The control
        // that produces the other composition is driven in the test below.
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();

        // Answering, maximized: no score panel, no status strip — and therefore
        // no "Problem N of M" progress indicator and no neutral prompt, both
        // ratified as accepted consequences of the mode.
        await Expect(ScorePanel).ToHaveCountAsync(0);
        await Expect(StatusStrip).ToHaveCountAsync(0);

        // The board and every answer instrument survive: a cube answer must stay
        // makeable without leaving the maximized view.
        await Expect(Page.Locator(".board-container .bg-diagram")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Radio, new() { Name = "No double" })).ToBeVisibleAsync();

        // And so does the XGID — the badge rides the action row, which this mode
        // keeps. This is the composition #98 came out of: the badge used to
        // overlay a title strip that no longer exists here at all, so "present in
        // the maximized answering view" is the ruling's load-bearing half, and
        // "nowhere on the canvas" is the other.
        await Expect(XgidBadge).ToBeVisibleAsync();
        await Expect(Page.Locator(".board-container .xgid-label")).ToHaveCountAsync(0);
        await AssertBadgeSitsBelowTheBoardAsync();

        await AnswerCubeNoDoubleAsync();

        // Review, normalized: the full composition is back, verdict included, and
        // the Solution-mode panel renders — which is also the proof that the
        // board-only canvas never reached a Solution request (the producer throws
        // on that pairing, so a leak would fault the render, not merely look wrong).
        await Expect(StatusStrip).ToHaveCountAsync(1);
        await Expect(ScorePanel).ToHaveCountAsync(1);
        await Expect(VerdictBand).ToContainTextAsync("No Double: correct · Take: correct");
        await Expect(Page.Locator(".bg-diagram")).ToContainTextAsync("Best: No Double");

        // The badge did not move when the composition did — one home, both
        // states. A badge that teleported per mode would read as a bug, which is
        // the whole of §4's ruling.
        await Expect(XgidBadge).ToBeVisibleAsync();
        await Expect(Page.Locator(".board-container .xgid-label")).ToHaveCountAsync(0);
        await AssertBadgeSitsBelowTheBoardAsync();
    }

    /// <summary>
    /// The badge is off the canvas as real geometry, not merely as DOM position:
    /// its box starts below the board region's. Only a browser can judge this —
    /// the bUnit pins assert the tree, which is not the same claim once the
    /// producer's own CSS is laying the board out.
    /// </summary>
    private async Task AssertBadgeSitsBelowTheBoardAsync()
    {
        var boardBox = await Page.Locator(".board-container").BoundingBoxAsync();
        var badgeBox = await XgidBadge.BoundingBoxAsync();

        Assert.NotNull(boardBox);
        Assert.NotNull(badgeBox);
        Assert.True(badgeBox!.Y >= boardBox!.Y + boardBox.Height,
            $"the XGID badge (y={badgeBox.Y}) must sit below the board region " +
            $"(y={boardBox.Y}, height={boardBox.Height})");
    }

    [Fact]
    public async Task WithTheSettingOff_AnsweringKeepsItsChrome()
    {
        // The other half of the pin pair above: without this, the absence
        // assertions there would be satisfied by chrome that had gone missing for
        // some entirely different reason.
        //
        // It is also the scenario carrying the control's whole chain since #113,
        // which is why the untick is a real gesture on the real page rather than a
        // seeded storage entry: the box is ticked when this arrives, and unticking
        // it has to reach the service, the entry, and the quiz page's derivation
        // for the chrome below to be there at all.
        await BootHomeAsync();
        await DisableMaximizeAsync();

        // Back to Home for the pick — the nav link, since no quiz is running yet
        // and the page's own "Back to quiz" affordance is absent by its
        // predicate. Waited out on Home's own control rather than on the URL:
        // the Home NavLink's href is the app base, so the landing URL's trailing
        // form is the framework's business, and the pick button being there is
        // the readiness this test actually needs.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Home" }).First.ClickAsync();
        await Expect(PickFolderButton).ToBeVisibleAsync();

        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();

        await Expect(ScorePanel).ToHaveCountAsync(1);
        await Expect(StatusStrip).ToHaveCountAsync(1);
        await Expect(VerdictBand).ToContainTextAsync("Pick the cube action, then Submit.");

        // Normal view's half of "one home, both modes" — with the setting off the
        // badge is in exactly the same place, which is what makes it a home
        // rather than a maximize-mode behavior.
        await Expect(XgidBadge).ToBeVisibleAsync();
        await Expect(Page.Locator(".board-container .xgid-label")).ToHaveCountAsync(0);
        await AssertBadgeSitsBelowTheBoardAsync();
    }

    [Fact]
    public async Task TheScoreStripRendersBelowTheActionRow()
    {
        // SPEC-quiz-view.md §5's rider, where only a browser can judge it: the
        // ongoing-stats strip is now the bottom-most chrome on the page. Asserted
        // as real geometry (the panel's box starts below the action row's), not
        // as source order — that is what the bUnit pin already covers, and it is
        // not the same claim once CSS is in play.
        //
        // Judged at REVIEW rather than while answering, and not by turning the
        // maximize mode off: since #113 review is where a default visitor sees
        // this panel at all, so it is where the geometry claim is worth making.
        // The chrome block sits outside the per-state action-row branch, so the
        // rider is the same rider in either state.
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();

        await Expect(ScorePanel).ToBeVisibleAsync();
        var actionRow = Page.Locator(".board-chrome .action-row");
        var rowBox = await actionRow.BoundingBoxAsync();
        var panelBox = await ScorePanel.BoundingBoxAsync();

        Assert.NotNull(rowBox);
        Assert.NotNull(panelBox);
        Assert.True(panelBox!.Y >= rowBox!.Y + rowBox.Height,
            $"the score panel (y={panelBox.Y}) must sit below the action row " +
            $"(y={rowBox.Y}, height={rowBox.Height})");
    }
}
