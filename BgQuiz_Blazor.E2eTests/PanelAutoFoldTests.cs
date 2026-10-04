using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The navigation panel folds by itself where the quiz page's action row cannot
/// fit beside it (SPEC-quiz-view.md §4, halheinrich/backgammon#264's ruling of
/// 2026-10-03: "The navigation panel folds by itself below the width where the
/// row fits beside it. Opened again there, it is a drawer over the page"). The
/// width is the page's own live measurement — the budget off its row-fit ruler
/// plus the chrome around the row — read in the browser by
/// <see cref="ActionRowGeometry"/>, so nothing here spells a pixel boundary.
/// </summary>
/// <remarks>
/// <para>
/// The fold is layout state, never the user's preference: crossing below the
/// width and back leaves the user's fold and the stored "Keep the navigation
/// panel folded" setting as they were (navFold.js holds the fold and saves the
/// user's while it lasts).
/// </para>
/// <para>
/// Reopened below the width, the panel is a drawer (Hal, 2026-10-03: "yes,
/// make it a drawer"). It opens only from the rail, never by itself; Escape, a
/// click outside it, or the rail closes it; opening it changes no preference;
/// while it is open it may cover the page, and once it is closed every row
/// control is reachable again. That replaced "Opening it again there leaves
/// no control covered", which the old pin here asserted without ever
/// hit-testing while the panel was open (the consultant's finding on
/// halheinrich/backgammon#8, comment 5966011660).
/// </para>
/// </remarks>
public sealed class PanelAutoFoldTests : E2eTestBase
{
    public PanelAutoFoldTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    /// <summary>
    /// A click on the page outside every control: the board's title strip, at
    /// its right-hand end, which no hit region covers and the drawer never
    /// reaches.
    /// </summary>
    private async Task ClickTheBoardsTitleStripAsync()
    {
        var board = await LaidOutBoxAsync(Page.Locator(".board-container"), "board");
        await Page.Mouse.ClickAsync(board.X + board.Width - 40, board.Y + 8);
    }

    private ILocator CollapseRail =>
        Page.GetByRole(AriaRole.Checkbox, new() { Name = ExpectedText.HideNavigationPanelCheckbox });

    private ILocator Panel => Page.Locator(".sidebar");

    private Task<bool> AutoFoldedAsync() =>
        Page.EvaluateAsync<bool>("() => document.documentElement.hasAttribute('data-nav-autofold')");

    private Task<string?> StoredSettingsAsync() =>
        Page.EvaluateAsync<string?>("() => localStorage.getItem('xg_quizSettings')");

    private async Task StartOnTheMatchAsync()
    {
        await Page.SetViewportSizeAsync(1000, 800);
        await BootHomeAsync();
        await PickSynthesizedFileAsync(SyntheticXgMatch.StagedFileName, SyntheticXgMatch.Bytes());
        await ApplyFilterAsync();
        await StartQuizAsync();
        await Expect(Page.Locator(".action-row .bg-cube-actions")).ToBeVisibleAsync();
    }

    /// <summary>Resize, then let the page's fit run before anything is read.</summary>
    private async Task ResizeAsync(int width, int height = 800)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
    }

    [Fact]
    public async Task BelowTheMeasuredWidth_ThePanelFoldsByItself_AndAboveItReturnsAsTheUserLeftIt()
    {
        await StartOnTheMatchAsync();
        Assert.False(await AutoFoldedAsync());
        var foldWidth = await ActionRowGeometry.FoldWidthAsync(Page);
        Assert.InRange(foldWidth, 641, 1200);   // measured at 1000, so inside its layout band

        await ResizeAsync((int)Math.Floor(foldWidth) - 1);
        Assert.True(await AutoFoldedAsync());
        await Expect(CollapseRail).ToBeCheckedAsync();   // the control says hidden, which is true
        Assert.Equal(0, (await Panel.BoundingBoxAsync())!.Width);
        Assert.Empty(await ActionRowGeometry.CoveredControlsAsync(Page));

        await ResizeAsync((int)Math.Ceiling(foldWidth) + 1);
        Assert.False(await AutoFoldedAsync());
        await Expect(CollapseRail).Not.ToBeCheckedAsync();
        Assert.True((await Panel.BoundingBoxAsync())!.Width > 0);
        Assert.Empty(await ActionRowGeometry.CoveredControlsAsync(Page));
    }

    [Fact]
    public async Task CrossingBelowAndBack_LeavesTheUsersFold_AndTheStoredSetting_AsTheyWere()
    {
        await StartOnTheMatchAsync();
        var stored = await StoredSettingsAsync();

        // The user folded it: still folded after the crossing, and not by the
        // auto-fold any more.
        await CollapseRail.ClickAsync();
        await Expect(CollapseRail).ToBeCheckedAsync();
        await ResizeAsync(800);
        Assert.True(await AutoFoldedAsync());
        await ResizeAsync(1280);
        Assert.False(await AutoFoldedAsync());
        await Expect(CollapseRail).ToBeCheckedAsync();

        // The user showed it again: showing after the crossing.
        await CollapseRail.ClickAsync();
        await Expect(CollapseRail).Not.ToBeCheckedAsync();
        await ResizeAsync(800);
        Assert.True(await AutoFoldedAsync());
        await ResizeAsync(1280);
        await Expect(CollapseRail).Not.ToBeCheckedAsync();

        // The stored setting was never written by any of it.
        Assert.Equal(stored, await StoredSettingsAsync());
    }

    [Fact]
    public async Task WithKeepFoldedOn_CrossingBelowAndBack_LeavesThePanelFolded_AndTheSettingOn()
    {
        await Page.SetViewportSizeAsync(1280, 800);
        await BootHomeAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = ExpectedText.SettingsNavLink }).ClickAsync();
        await ExpectUrlAsync("/settings");
        var keepFolded = Page.GetByRole(AriaRole.Checkbox, new() { Name = ExpectedText.KeepNavigationPanelFoldedSetting });
        await keepFolded.CheckAsync();
        await Expect(keepFolded).ToBeCheckedAsync();
        await BootHomeAsync();
        await PickSynthesizedFileAsync(SyntheticXgMatch.StagedFileName, SyntheticXgMatch.Bytes());
        await ApplyFilterAsync();
        await StartQuizAsync();
        // The problem first: straight after Start the page can have no row to
        // fit, and the box is still Home's until the navigation's DOM
        // synchronization resets it and the stored fold is applied again
        // (halheinrich/backgammon#333).
        await Expect(Page.Locator(".action-row .bg-cube-actions")).ToBeVisibleAsync();
        await Expect(CollapseRail).ToBeCheckedAsync();
        var stored = await StoredSettingsAsync();

        await ResizeAsync(800);
        Assert.True(await AutoFoldedAsync());
        await ResizeAsync(1280);

        Assert.False(await AutoFoldedAsync());
        await Expect(CollapseRail).ToBeCheckedAsync();
        Assert.Equal(stored, await StoredSettingsAsync());
        Assert.Contains("\"keepNavigationPanelFolded\":true", stored);
    }

    /// <summary>How the drawer is closed — the three ways the ruling names.</summary>
    public enum DrawerClose
    {
        Escape,
        ClickOutside,
        Rail,
    }

    /// <summary>Open the drawer from the rail and check it is a drawer: over the page, taking no width from the row.</summary>
    private async Task OpenTheDrawerAsync(double rowWidth)
    {
        await CollapseRail.ClickAsync();
        await Expect(CollapseRail).Not.ToBeCheckedAsync();
        Assert.True(await AutoFoldedAsync());   // still the narrow layout
        Assert.True((await Panel.BoundingBoxAsync())!.Width > 0, "the drawer shows");
        Assert.Equal("fixed", await Panel.EvaluateAsync<string>("e => getComputedStyle(e).position"));
        Assert.Equal(rowWidth, (await ActionRowGeometry.FitAsync(Page)).Row, 0.5);   // no width taken from the row
    }

    private async Task CloseTheDrawerAsync(DrawerClose how)
    {
        switch (how)
        {
            case DrawerClose.Escape:
                await Page.Keyboard.PressAsync("Escape");
                break;
            case DrawerClose.ClickOutside:
                await ClickTheBoardsTitleStripAsync();
                break;
            case DrawerClose.Rail:
                await CollapseRail.ClickAsync();
                break;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReopenedBelowTheWidth_ThePanelIsADrawer_AndEachCloseLeavesEveryRowControlReachable(bool cube)
    {
        // A cube problem (every answer pill hit-tested) and a checker problem
        // (Undo all and Undo last among the controls). The drawer is opened
        // three times and closed each of the three ways, and after each close
        // every control in the row takes a tap at its centre. The user's fold
        // and the stored setting are never touched.
        await StartOnTheMatchAsync();
        if (!cube)
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.SkipButton }).ClickAsync();
            await Expect(Page.Locator(".bg-play-entry")).ToBeVisibleAsync();
        }
        var stored = await StoredSettingsAsync();
        await ResizeAsync(800);
        Assert.True(await AutoFoldedAsync());
        var row = (await ActionRowGeometry.FitAsync(Page)).Row;
        var controls = await ActionRowGeometry.RowControlCountAsync(Page);
        Assert.True(controls >= (cube ? 4 + 1 + 4 : 2 + 1 + 4), $"the row's controls are there to test ({controls})");

        foreach (var how in Enum.GetValues<DrawerClose>())
        {
            await OpenTheDrawerAsync(row);

            await CloseTheDrawerAsync(how);

            await Expect(CollapseRail).ToBeCheckedAsync();
            Assert.Equal(0, (await Panel.BoundingBoxAsync())!.Width);
            Assert.True(await AutoFoldedAsync());
            Assert.Equal(controls, await ActionRowGeometry.RowControlCountAsync(Page));
            var covered = await ActionRowGeometry.CoveredControlsAsync(Page);
            Assert.True(covered.Length == 0, $"after closing by {how}: {string.Join("; ", covered)}");
        }

        // No preference moved: widened again, the panel returns as the user
        // left it (showing), and the stored setting was never written.
        Assert.Equal(stored, await StoredSettingsAsync());
        await ResizeAsync(1280);
        Assert.False(await AutoFoldedAsync());
        await Expect(CollapseRail).Not.ToBeCheckedAsync();
        Assert.Equal(stored, await StoredSettingsAsync());
    }

    [Fact]
    public async Task WhileTheDrawerIsOpen_ItMayCoverTheRow_AndAClickOutsideIt_ClosesIt_AndLandsWhereItWasAimed()
    {
        // The ruling lets the open drawer cover part of the page — and it
        // does: the cube pills sit at the row's left, under it. A click on the
        // row outside the drawer closes it and lands on what it was aimed at.
        await StartOnTheMatchAsync();
        await ResizeAsync(800);
        await OpenTheDrawerAsync((await ActionRowGeometry.FitAsync(Page)).Row);
        Assert.NotEmpty(await ActionRowGeometry.CoveredControlsAsync(Page));

        await Page.Locator(".action-row .bg-cube-action").Last.ClickAsync();

        await Expect(CollapseRail).ToBeCheckedAsync();
        await Expect(Page.Locator(".action-row input[type=radio]").Last).ToBeCheckedAsync();
        Assert.Empty(await ActionRowGeometry.CoveredControlsAsync(Page));
    }

    [Fact]
    public async Task BelowTheWidth_TheDrawerOpensOnlyFromTheRail()
    {
        // Nothing but the rail opens it: not a resize at the narrow width, not
        // a move to another problem, not the row re-fitting. Every change of
        // the rail's box or of <html>'s attributes is watched for a moment
        // with the drawer open.
        await StartOnTheMatchAsync();
        await ResizeAsync(800);
        await Expect(CollapseRail).ToBeCheckedAsync();
        await Page.EvaluateAsync(@"() => {
            const rail = document.querySelector('.sidebar-toggle-checkbox');
            window.__drawerOpened = 0;
            const watch = () => {
              if (!rail.checked && document.documentElement.hasAttribute('data-nav-autofold')) window.__drawerOpened++;
            };
            new MutationObserver(watch).observe(document.documentElement, { attributes: true, subtree: true, childList: true });
            requestAnimationFrame(function tick() { watch(); requestAnimationFrame(tick); });
          }");

        foreach (var width in new[] { 760, 700, 641, 800 })
        {
            await ResizeAsync(width);
        }
        await Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.SkipButton }).ClickAsync();
        await Expect(Page.Locator(".bg-play-entry")).ToBeVisibleAsync();
        await Page.EvaluateAsync(@"async () => {
            for (let i = 0; i < 10; i++) {
              window.dispatchEvent(new Event('resize'));
              await new Promise(r => requestAnimationFrame(r));
            }
          }");

        await Expect(CollapseRail).ToBeCheckedAsync();
        Assert.Equal(0, await Page.EvaluateAsync<int>("() => window.__drawerOpened"));
    }

    [Fact]
    public async Task Escape_InTheReviewsNotes_ClosesTheNotes_AndLeavesTheDrawer()
    {
        // Escape closes the active surface only. At review, with the drawer
        // open, the notes opened by keyboard (a click would be outside the
        // drawer and close it): Escape closes the notes, not the drawer too;
        // the next Escape closes the drawer.
        await StartOnTheMatchAsync();
        await ResizeAsync(800);
        await AnswerCubeNoDoubleAsync();
        var notes = Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.NotesButton, Exact = true });
        await Expect(notes).ToBeVisibleAsync();
        await OpenTheDrawerAsync((await ActionRowGeometry.FitAsync(Page)).Row);
        await notes.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        var dialog = Page.GetByRole(AriaRole.Dialog, new() { Name = ExpectedText.NotesButton });
        await Expect(dialog).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(CollapseRail).Not.ToBeCheckedAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Expect(CollapseRail).ToBeCheckedAsync();
    }

    [Fact]
    public async Task SpaceAndEnter_InTheDrawer_NeitherSubmitNorMoveTheQuizOn()
    {
        // An answer chosen, so the page's Space shortcut would press Submit
        // from the body. The drawer open, and a click on its plain background
        // — which leaves focus on nothing in particular — then Space and
        // Enter: the quiz stays where it is, and the drawer stays open.
        await StartOnTheMatchAsync();
        await ResizeAsync(800);
        await CubePill(ExpectedText.NoDoublePill).CheckAsync();
        await Expect(SubmitButton).ToBeEnabledAsync();
        await OpenTheDrawerAsync((await ActionRowGeometry.FitAsync(Page)).Row);
        var panel = (await Panel.BoundingBoxAsync())!;
        await Page.Mouse.ClickAsync(panel.X + panel.Width / 2, panel.Y + panel.Height - 20);
        await Expect(CollapseRail).Not.ToBeCheckedAsync();
        Assert.Equal("BODY", await Page.EvaluateAsync<string>("() => document.activeElement.tagName"));

        await Page.Keyboard.PressAsync("Space");
        await Page.Keyboard.PressAsync("Enter");

        await Expect(CollapseRail).Not.ToBeCheckedAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ContinueButton })).ToHaveCountAsync(0);
        await Expect(SubmitButton).ToBeEnabledAsync();
        await ExpectUrlAsync("/quiz");

        await Page.Keyboard.PressAsync("Escape");   // and Escape closes it
        await Expect(CollapseRail).ToBeCheckedAsync();
    }

    [Fact]
    public async Task AtTheFoldWidth_RepeatedResizesDoNotOscillate()
    {
        // The decision rests on the row the panel would leave if it showed,
        // which the fold itself does not change, so it cannot flip back and
        // forth: count every change of the auto-fold mark while resize events
        // repeat at the boundary on either side, and while the width steps
        // across it.
        await StartOnTheMatchAsync();
        var foldWidth = await ActionRowGeometry.FoldWidthAsync(Page);
        var below = (int)Math.Floor(foldWidth) - 1;
        var above = (int)Math.Ceiling(foldWidth) + 1;
        await Page.EvaluateAsync(@"() => {
            window.__autoFoldChanges = 0;
            new MutationObserver(() => window.__autoFoldChanges++)
              .observe(document.documentElement, { attributes: true, attributeFilter: ['data-nav-autofold'] });
          }");

        foreach (var width in new[] { below, above })
        {
            await ResizeAsync(width);
            var changes = await Page.EvaluateAsync<int>("() => window.__autoFoldChanges");
            await Page.EvaluateAsync(@"async () => {
                for (let i = 0; i < 25; i++) {
                  window.dispatchEvent(new Event('resize'));
                  await new Promise(r => requestAnimationFrame(r));
                }
              }");
            Assert.Equal(changes, await Page.EvaluateAsync<int>("() => window.__autoFoldChanges"));
        }

        await Page.EvaluateAsync("() => { window.__autoFoldChanges = 0; }");
        foreach (var width in new[] { below, above, below, above })
        {
            await ResizeAsync(width);
        }
        Assert.Equal(4, await Page.EvaluateAsync<int>("() => window.__autoFoldChanges"));
    }
}
