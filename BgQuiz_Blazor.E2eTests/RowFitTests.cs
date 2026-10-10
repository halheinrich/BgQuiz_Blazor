using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The action row's fit is measured live, under the fonts actually rendering
/// (SPEC-quiz-view.md §4, halheinrich/backgammon#264's ruling of 2026-10-03,
/// and the cube labels' switch): the panel's fold and the pills' form follow
/// the page's own measurement, not a width measured once under one font
/// stack. Old pixel boundaries could not show that, so these change the font
/// metrics at a fixed viewport and watch the measurement and both switches
/// move with them.
/// </summary>
/// <remarks>
/// "Changed font metrics" is letter-spacing on the page's text — wider text in
/// whatever font renders, on any machine the suite runs on. Each scenario uses
/// the synthesized .xg match, whose first problem is a cube decision where
/// gammons are possible (the fourth reads Too good).
/// </remarks>
public sealed class RowFitTests : E2eTestBase
{
    public RowFitTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright, output) { }

    /// <summary>Wider text, everywhere on the page: the changed font metrics.</summary>
    private const string WiderText = "body { letter-spacing: 0.2em; }";

    private ILocator Captions => Page.Locator(".action-row .bg-cube-actions label");

    private async Task StartOnTheMatchAsync(int width)
    {
        await Page.SetViewportSizeAsync(width, 800);
        await BootHomeAsync();
        await PickSynthesizedFileAsync(SyntheticXgMatch.StagedFileName, SyntheticXgMatch.Bytes());
        await ExpectFilterInEffectAsync();
        await StartQuizAsync();
        await Expect(Captions).ToHaveCountAsync(4);
    }

    /// <summary>Two animation frames: the page's fit after a resize or a font change has run.</summary>
    private Task SettleAsync() =>
        Page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");

    [Fact]
    public async Task WiderText_RaisesTheMeasuredBudget_AndThePanelFoldsAtTheSameViewport()
    {
        // A viewport just wide enough for the row beside the panel: 30 px of
        // the panel-showing row to spare, from the page's own measurement.
        await StartOnTheMatchAsync(1000);
        var foldWidth = await ActionRowGeometry.FoldWidthAsync(Page);
        await Page.SetViewportSizeAsync((int)Math.Ceiling(foldWidth) + 30, 800);
        await SettleAsync();
        var before = await ActionRowGeometry.FitAsync(Page);
        Assert.False(before.AutoFolded);

        await Page.AddStyleTagAsync(new() { Content = WiderText });
        await SettleAsync();

        var after = await ActionRowGeometry.FitAsync(Page);
        Assert.True(after.Budget > before.Budget, $"budget {before.Budget} -> {after.Budget}");
        Assert.True(after.AutoFolded, "the panel folds for the wider row");
        Assert.Empty(await ActionRowGeometry.UnreachableControlsAsync(Page));
        Assert.True(await ActionRowGeometry.TailOverrunAsync(Page) <= 0.5);
    }

    [Fact]
    public async Task WiderText_RaisesTheFullFormsNeed_AndThePillsAbbreviateAtTheSameViewport()
    {
        // A row wide enough for the full form with room to spare, measured.
        await StartOnTheMatchAsync(1600);
        await Expect(Captions.Nth(3)).ToHaveTextAsync(ExpectedText.TooGoodPill);
        var before = await ActionRowGeometry.FitAsync(Page);
        var spare = before.Row - before.FullCubeRow;
        Assert.True(spare > 0);

        // Narrow the viewport to leave the full form only a little room, then
        // widen the text by more than that room.
        await Page.SetViewportSizeAsync(1600 - (int)Math.Floor(spare) + 20, 800);
        await SettleAsync();
        await Expect(Captions.Nth(3)).ToHaveTextAsync(ExpectedText.TooGoodPill);

        await Page.AddStyleTagAsync(new() { Content = WiderText });

        await Expect(Captions.Nth(3)).ToHaveTextAsync("TG");
        var after = await ActionRowGeometry.FitAsync(Page);
        Assert.True(after.FullCubeRow > after.Row, $"full form needs {after.FullCubeRow}, row is {after.Row}");
        Assert.Empty(await ActionRowGeometry.UnreachableControlsAsync(Page));
    }

    [Fact]
    public async Task AtTheSwitch_TheWidestBoldSelection_StillFits()
    {
        // The full form's need is measured at its widest selection — the
        // fourth answer, bold. Put the row exactly at that need, so the full
        // form shows with nothing to spare, then select that answer: nothing
        // runs over, because the bold was already in the measurement.
        await StartOnTheMatchAsync(1600);
        // The measured row: pending, the panel is folded by style and the row
        // is wider by the panel's width, so a width read off it would place
        // the viewport that far short of the need (halheinrich/backgammon#333).
        await ExpectRowFittedAsync();
        var fit = await ActionRowGeometry.FitAsync(Page);
        await Page.SetViewportSizeAsync(1600 - (int)Math.Floor(fit.Row - fit.FullCubeRow), 800);
        await SettleAsync();
        await Expect(Captions.Nth(3)).ToHaveTextAsync(ExpectedText.TooGoodPill);

        await CubePill(ExpectedText.TooGoodPill).CheckAsync();

        await Expect(CubePill(ExpectedText.TooGoodPill)).ToBeCheckedAsync();
        await Expect(Captions.Nth(3)).ToHaveTextAsync(ExpectedText.TooGoodPill);   // choosing never changes the form
        Assert.True(await ActionRowGeometry.TailOverrunAsync(Page) <= 0.5,
            $"the tail runs {await ActionRowGeometry.TailOverrunAsync(Page)}px over the bold row");
        Assert.Empty(await ActionRowGeometry.UnreachableControlsAsync(Page));
    }

    [Fact]
    public async Task OnFirstLoad_AndWhenTheFontsChangeAfterIt_NoControlIsCovered()
    {
        // First load at §2's floor corner, 641 x 768, where the row fits only
        // with the panel folded and the tail behind its "⋯": the page's first
        // fit has applied both before anything can be tapped (OneBudgetTests
        // holds that fit and taps the row before it, too). Then a font
        // finishing loading after the page is up (simulated: the wider text
        // arrives 300 ms later, on its own) re-fits the row as it lands.
        await Page.SetViewportSizeAsync(641, 768);
        await BootHomeAsync();
        await PickSynthesizedFileAsync(SyntheticXgMatch.StagedFileName, SyntheticXgMatch.Bytes());
        await ExpectFilterInEffectAsync();
        await StartQuizAsync();
        await ExpectKeyboardShortcutReadyAsync();
        // The first fit, awaited: the readiness mark is set before the row is
        // rendered, so it says nothing of the row (halheinrich/backgammon#333).
        await ExpectRowFittedAsync();
        Assert.Empty(await ActionRowGeometry.UnreachableControlsAsync(Page));
        Assert.True((await ActionRowGeometry.FitAsync(Page)).AutoFolded);
        var before = await ActionRowGeometry.FitAsync(Page);

        await Page.EvaluateAsync($@"() => setTimeout(() => {{
              const style = document.createElement('style');
              style.id = 'zz-late-font';
              style.textContent = '{WiderText}';
              document.head.appendChild(style);
            }}, 300)");
        await Expect(Page.Locator("#zz-late-font")).ToHaveCountAsync(1);
        await SettleAsync();
        await SettleAsync();

        Assert.True((await ActionRowGeometry.FitAsync(Page)).Budget > before.Budget, "the measurement followed the font");
        Assert.Empty(await ActionRowGeometry.UnreachableControlsAsync(Page));
        Assert.True(await ActionRowGeometry.TailOverrunAsync(Page) <= 0.5);
    }

    [Fact]
    public async Task ShowStatsAndEndQuiz_AreIconButtons_NamedAndTitledByTheirWords()
    {
        // halheinrich/backgammon#264's ruling: "Their names stay as their
        // tooltips and accessible names, and End quiz keeps the far end."
        await StartOnTheMatchAsync(1280);
        foreach (var name in new[] { ExpectedText.ShowStatsButton, ExpectedText.EndQuizButton })
        {
            var button = Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
            await Expect(button).ToHaveCountAsync(1);
            await Expect(button).ToHaveAttributeAsync("title", name);
            await Expect(button).ToHaveTextAsync("");
        }
        await Expect(Page.Locator(".action-row button").Last).ToHaveAccessibleNameAsync(ExpectedText.EndQuizButton);
    }
}
