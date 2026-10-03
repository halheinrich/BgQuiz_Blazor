using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The navigation panel folds by itself where the quiz page's action row cannot
/// fit beside it (SPEC-quiz-view.md §4, halheinrich/backgammon#264's ruling of
/// 2026-10-03: "The navigation panel folds by itself below the width where the
/// row fits beside it. Opening it again there leaves no control covered."). The
/// width is the page's own live measurement — the budget off its row-fit ruler
/// plus the chrome around the row — read in the browser by
/// <see cref="ActionRowGeometry"/>, so nothing here spells a pixel boundary.
/// </summary>
/// <remarks>
/// The fold is layout state, never the user's preference: crossing below the
/// width and back leaves the user's fold and the stored "Keep the navigation
/// panel folded" setting as they were (navFold.js holds the fold and saves the
/// user's while it lasts). Reopened below the width, the panel is an overlay
/// that takes no width from the row.
/// </remarks>
public sealed class PanelAutoFoldTests : E2eTestBase
{
    public PanelAutoFoldTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

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

    [Fact]
    public async Task ReopenedBelowTheWidth_ThePanelIsAnOverlay_AndTheRowKeepsItsWidth()
    {
        await StartOnTheMatchAsync();
        await ResizeAsync(800);
        Assert.True(await AutoFoldedAsync());
        var before = await ActionRowGeometry.FitAsync(Page);

        await CollapseRail.ClickAsync();   // open it again, below the width

        await Expect(CollapseRail).Not.ToBeCheckedAsync();
        Assert.True(await AutoFoldedAsync());   // still the narrow layout
        var panel = (await Panel.BoundingBoxAsync())!;
        Assert.True(panel.Width > 0, "the panel shows");
        Assert.Equal("fixed", await Panel.EvaluateAsync<string>("e => getComputedStyle(e).position"));
        var after = await ActionRowGeometry.FitAsync(Page);
        Assert.Equal(before.Row, after.Row, 0.5);   // the overlay took no width from the row
        Assert.True(await ActionRowGeometry.TailOverrunAsync(Page) <= 0.5, "the row's own controls stay clear of its tail");

        await CollapseRail.ClickAsync();   // and closes from the rail as it opened
        await Expect(CollapseRail).ToBeCheckedAsync();
        Assert.Equal(0, (await Panel.BoundingBoxAsync())!.Width);
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
