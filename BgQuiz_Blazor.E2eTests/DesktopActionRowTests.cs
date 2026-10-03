using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The action row's controls stay reachable in the desktop band
/// (halheinrich/backgammon#264, SPEC-quiz-view.md §4: "the row narrows; the
/// board keeps its height", ruled 2026-10-03). Before the ruling the row's
/// trailing cluster covered Submit at 716–761 px and an arrow button at
/// 641–946 px, with the navigation panel showing. With the panel folding by
/// itself, Show stats and End quiz as icons, and the locator's short form,
/// these pins tap the centre of each at the old worst widths: both answer
/// kinds, both view modes, and the panel as the user left it, showing or
/// folded.
/// </summary>
/// <remarks>
/// <para>
/// The arrow buttons are pinned from 726 px up. Below that the three
/// narrowings do not reach (measured 2026-10-03, published app, Chromium,
/// Windows Helvetica/Arial): with the panel folded the row is too narrow for
/// the checker answer row and the tail's floor, so on a problem from a
/// <c>.xg</c> file the cluster runs over ▶ and ⏭ — past the row's gap at
/// 641–721 px, over a button's centre at 641–701 px. That band is the
/// leg's report to Hal, whose call the banked fourth narrowing is.
/// </para>
/// <para>
/// Each scenario walks the widths in one session: the row is re-fitted at each
/// resize, which is the behaviour under test, and a tap's real proof — a
/// click that Playwright's own hit test must agree reaches Submit — is taken
/// once, at the narrowest width.
/// </para>
/// </remarks>
public sealed class DesktopActionRowTests : E2eTestBase
{
    public DesktopActionRowTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    private static readonly int[] SubmitWidths = [716, 731, 746, 761, 800, 900];

    private static readonly int[] ArrowWidths = [726, 768, 800, 850, 900, 946];

    private static readonly string[] ArrowNames =
        [ExpectedText.GoToFirstButton, ExpectedText.BackButton, ExpectedText.SkipButton, ExpectedText.GoToLastButton];

    private ILocator ContinueButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ContinueButton });

    private ILocator CollapseRail =>
        Page.GetByRole(AriaRole.Checkbox, new() { Name = ExpectedText.HideNavigationPanelCheckbox });

    /// <summary>Whether the control named <paramref name="name"/> is among those a centre tap misses.</summary>
    private static bool IsCovered(IEnumerable<string> covered, string name) =>
        covered.Any(c => c.StartsWith(name + " <- ", StringComparison.Ordinal));

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    public async Task AtTheOldWorstWidths_ATapAtTheCentreOfSubmitAndOfEachArrowReachesIt(
        bool cube, bool normalView, bool panelFolded)
    {
        // The synthesized .xg match: its cube decision, then its checker play —
        // a problem with game and move numbers, the widest tail there is.
        await Page.SetViewportSizeAsync(1280, 800);
        await BootHomeAsync();
        if (normalView)
        {
            await DisableMaximizeAsync();
            await BootHomeAsync();
        }
        await PickSynthesizedFileAsync(SyntheticXgMatch.StagedFileName, SyntheticXgMatch.Bytes());
        await ApplyFilterAsync();
        await StartQuizAsync();
        if (!cube)
        {
            await NavButton(ExpectedText.SkipButton).ClickAsync();
            await Expect(Page.Locator(".bg-play-entry")).ToBeVisibleAsync();
        }
        if (panelFolded)
        {
            await CollapseRail.ClickAsync();
            await Expect(CollapseRail).ToBeCheckedAsync();
        }

        foreach (var width in SubmitWidths.Union(ArrowWidths).Order())
        {
            await Page.SetViewportSizeAsync(width, 800);
            await Page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
            var covered = await ActionRowGeometry.CoveredControlsAsync(Page);
            if (SubmitWidths.Contains(width))
            {
                Assert.False(IsCovered(covered, ExpectedText.SubmitButton), $"Submit covered at {width}px: {string.Join("; ", covered)}");
            }
            if (ArrowWidths.Contains(width))
            {
                Assert.All(ArrowNames, name =>
                    Assert.False(IsCovered(covered, name), $"{name} covered at {width}px: {string.Join("; ", covered)}"));
                Assert.Equal(1, (await ActionRowGeometry.FitAsync(Page)).RowLines);   // and no line added
            }
        }

        // The real gesture, at the narrowest width: an answer lights Submit,
        // and a click with no force reaches it.
        await Page.SetViewportSizeAsync(SubmitWidths[0], 800);
        if (cube)
        {
            await CubePill(ExpectedText.NoDoublePill).CheckAsync();
        }
        else
        {
            // The match's checker play, 8/5 6/5 on a 3-1 (SyntheticXgMatch), as
            // the one-click source-advance model takes it.
            await ClickBoardPointAsync(8);
            await ClickBoardPointAsync(6);
        }
        await Expect(SubmitButton).ToBeEnabledAsync();
        await SubmitButton.ClickAsync();
        await Expect(ContinueButton).ToBeVisibleAsync();
    }
}
