using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The cube pills' short form as the browser renders it
/// (<c>SPEC-quiz-view.md</c> §4, "The action row under quiz navigation"):
/// "Cube labels abbreviate only when the row cannot fit them", and in the
/// short form each pill shows its short label, is named by the short label
/// with the full one after it — "ND (No double)", so the name contains the
/// text shown — and keeps the full label as its tooltip. The page sets the
/// producer's <c>ShortLabels</c> from the action row's measured width, and a
/// misspelt parameter would compile and do nothing (the component captures
/// unmatched attributes), so only a real render can show the form took.
/// </summary>
/// <remarks>
/// Both scenarios use the committed cube fixture, where gammons are not
/// possible and the fourth answer reads No double / Pass, the longest set.
/// A 1220 px viewport with the navigation panel showing leaves an 862 px row,
/// narrower than the full set needs beside the tail (914 px under Windows
/// Helvetica/Arial, measured live by the page and printed by
/// <see cref="StartOnTheCubeFixtureAsync"/>, 2026-10-16); with the panel
/// folded the row gains the panel's 250 px (MainLayout.razor.css) and is
/// 1112 px, wide enough. The page measures both under the fonts actually
/// rendering, so these hold on any stack with a margin of over 50 px each
/// way. Not the suite's default 1280 px: since the tail lost Show stats
/// (halheinrich/backgammon#421) the full set fits there, 914 px in a 922 px
/// row.
/// </remarks>
public sealed class CubeLabelsTests : E2eTestBase
{
    public CubeLabelsTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright, output) { }

    private ILocator CollapseRail =>
        Page.GetByRole(AriaRole.Checkbox, new() { Name = ExpectedText.HideNavigationPanelCheckbox });

    private ILocator Pills => Page.Locator(".action-row .bg-cube-actions").GetByRole(AriaRole.Radio);

    private ILocator Captions => Page.Locator(".action-row .bg-cube-actions label");

    private static readonly string[] FullLabels =
        [ExpectedText.NoDoublePill, ExpectedText.DoubleTakePill, ExpectedText.DoublePassPill, ExpectedText.NoDoublePassPill];

    private static readonly string[] ShortCaptions = ["ND", "D/T", "D/P", "NP"];

    /// <summary>A viewport where the row is too narrow for the full set beside the tail with the panel showing, and wide enough with it folded (the remarks above).</summary>
    private const int ViewportWidth = 1220;

    private async Task StartOnTheCubeFixtureAsync()
    {
        await Page.SetViewportSizeAsync(ViewportWidth, 800);
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ExpectFilterInEffectAsync();
        await StartQuizAsync();
        await Expect(Pills).ToHaveCountAsync(4);
        // The measured row: pending, the pills are short whatever the fit
        // decides, so a short form seen then is no reading of the switch
        // (halheinrich/backgammon#333).
        await ExpectRowFittedAsync();
        var fit = await ActionRowGeometry.FitAsync(Page);
        Output.WriteLine(
            $"row {fit.Row}px with the panel showing ({fit.PanelWidthIfShown}px); the full-label row needs {fit.FullCubeRow}px");
    }

    private async Task ExpectShortFormAsync()
    {
        await Expect(Captions).ToHaveTextAsync(ShortCaptions);
        for (var i = 0; i < FullLabels.Length; i++)
        {
            await Expect(Pills.Nth(i)).ToHaveAccessibleNameAsync($"{ShortCaptions[i]} ({FullLabels[i]})");
            await Expect(Captions.Nth(i)).ToHaveAttributeAsync("title", FullLabels[i]);
        }
    }

    private async Task ExpectFullFormAsync()
    {
        await Expect(Captions).ToHaveTextAsync(FullLabels);
        for (var i = 0; i < FullLabels.Length; i++)
        {
            await Expect(Pills.Nth(i)).ToHaveAccessibleNameAsync(FullLabels[i]);
            await Expect(Captions.Nth(i)).ToHaveAttributeAsync("title", FullLabels[i]);
        }
    }

    [Fact]
    public async Task WhereTheRowIsTooNarrow_ThePillsShowTheShortForm_AndAnswerAsTheirFullSelves()
    {
        await StartOnTheCubeFixtureAsync();

        await ExpectShortFormAsync();

        // A short pill is still its whole answer: the verdict names it by its
        // full label, as the review always does.
        await AnswerCubeAsync(ExpectedText.NoDoublePassPill);
        await Expect(VerdictBand).ToHaveTextAsync("Not best — No double / Pass lost 1.3251. Best: No double.");
    }

    [Fact]
    public async Task FoldingTheNavigationPanel_WidensTheRow_AndTheFullFormReturns_ThenLeavesAgain()
    {
        // The switch keys on the row's own width, not the viewport's: the
        // viewport never changes here, and the form follows the fold both ways.
        await StartOnTheCubeFixtureAsync();
        await ExpectShortFormAsync();

        await CollapseRail.ClickAsync();
        await Expect(CollapseRail).ToBeCheckedAsync();
        await ExpectFullFormAsync();

        await CollapseRail.ClickAsync();
        await Expect(CollapseRail).Not.ToBeCheckedAsync();
        await ExpectShortFormAsync();
    }
}
