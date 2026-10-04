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
/// The arrow buttons are pinned here from 726 px up, the band the three
/// narrowings reach. Below it they did not (measured 2026-10-03, published
/// app, Chromium, Windows Helvetica/Arial): with the panel folded the row was
/// too narrow for the checker answer row and the tail's floor, so on a problem
/// from a <c>.xg</c> file the cluster ran over ▶ and ⏭ — past the row's gap at
/// 641–721 px, over a button's centre at 641–701 px. The fourth, widened —
/// the whole tail behind one "⋯" (Hal, 2026-10-03) — closes that band, and
/// <see cref="InTheOldBand_ATapAtTheCentreOfEveryRowControlReachesIt"/> pins
/// it control by control.
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
        else
        {
            // Landed on the cube problem before anything is folded or read:
            // straight after Start the page can have no row yet, and the
            // navigation's DOM synchronization, which resets the rail, can
            // still be to come (halheinrich/backgammon#333).
            await Expect(CubeAnswers).ToHaveCountAsync(4);
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

    /// <summary>
    /// The problem kinds the old band is pinned over: a checker play and a cube
    /// decision from a <c>.xg</c> file (game and move numbers, the widest tail),
    /// the cube in both readings of its fourth answer, and the same from
    /// <c>.xgp</c> files (a file name alone).
    /// </summary>
    public enum ProblemKind
    {
        XgChecker,
        XgCubeTooGood,
        XgCubeNoDoublePass,
        XgpChecker,
        XgpCubeTooGood,
        XgpCubeNoDoublePass,
    }

    /// <summary>The old band: 641–721 px, where the three narrowings left the tail over ▶ and ⏭.</summary>
    private static readonly int[] OldBandWidths = [641, 661, 681, 701, 721];

    /// <summary>§2's floor height, the tallest the band's claim has to hold at its shortest.</summary>
    private const int FloorHeight = 768;

    /// <summary>Stage and start a quiz whose problem on screen is <paramref name="kind"/>, answering.</summary>
    private async Task StartOnAsync(ProblemKind kind)
    {
        switch (kind)
        {
            case ProblemKind.XgChecker:
            case ProblemKind.XgCubeTooGood:
                await PickSynthesizedFileAsync(SyntheticXgMatch.StagedFileName, SyntheticXgMatch.Bytes());
                break;
            case ProblemKind.XgCubeNoDoublePass:
                await PickSynthesizedFileAsync(SyntheticXgMatch.MoneyStagedFileName, SyntheticXgMatch.MoneySessionBytes());
                break;
            case ProblemKind.XgpChecker:
                await PickFixtureAsync(CheckerFixture);
                break;
            case ProblemKind.XgpCubeTooGood:
                await PickFixtureAsync(TooGoodTakeFixture);
                break;
            case ProblemKind.XgpCubeNoDoublePass:
                await PickFixtureAsync(CubeFixture);
                break;
        }
        await ApplyFilterAsync();
        await StartQuizAsync();
        if (kind == ProblemKind.XgChecker)
        {
            await NavButton(ExpectedText.SkipButton).ClickAsync();
        }

        if (kind is ProblemKind.XgChecker or ProblemKind.XgpChecker)
        {
            await Expect(Page.Locator(".bg-play-entry")).ToBeVisibleAsync();
        }
        else
        {
            // The reading the kind names, by the fourth pill's own name.
            var fourth = kind is ProblemKind.XgCubeTooGood or ProblemKind.XgpCubeTooGood
                ? ExpectedText.TooGoodPill
                : ExpectedText.NoDoublePassPill;
            await Expect(CubePill(fourth)).ToHaveCountAsync(1);
        }
    }

    /// <summary>Answer the problem on screen and submit, landing on its review.</summary>
    private async Task AnswerAsync(ProblemKind kind)
    {
        switch (kind)
        {
            case ProblemKind.XgChecker:
                // The match's checker play, 8/5 6/5 on a 3-1 (SyntheticXgMatch).
                await ClickBoardPointAsync(8);
                await ClickBoardPointAsync(6);
                break;
            case ProblemKind.XgpChecker:
                // The fixture's 6-5, 24/18 then 18/13, as QuizFlowTests enters it.
                await ClickBoardPointAsync(24);
                await ClickBoardPointAsync(18);
                break;
            default:
                await CubePill(ExpectedText.NoDoublePill).CheckAsync();
                break;
        }
        await Expect(SubmitButton).ToBeEnabledAsync();
        await SubmitButton.ClickAsync();
        await Expect(ContinueButton).ToBeVisibleAsync();
    }

    /// <summary>At each old-band width: every row control takes a tap at its centre, and the row is one line.</summary>
    private async Task AssertTheBandAsync(string state)
    {
        foreach (var width in OldBandWidths)
        {
            await Page.SetViewportSizeAsync(width, FloorHeight);
            await Page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
            Assert.True(await ActionRowGeometry.RowControlCountAsync(Page) >= 6, $"{state} at {width}px: the row's controls are there");
            var covered = await ActionRowGeometry.CoveredControlsAsync(Page);
            Assert.True(covered.Length == 0, $"{state} at {width}px: {string.Join("; ", covered)}");
            Assert.Equal(1, (await ActionRowGeometry.FitAsync(Page)).RowLines);
        }
    }

    [Theory]
    [InlineData(ProblemKind.XgChecker, false)]
    [InlineData(ProblemKind.XgChecker, true)]
    [InlineData(ProblemKind.XgCubeTooGood, false)]
    [InlineData(ProblemKind.XgCubeTooGood, true)]
    [InlineData(ProblemKind.XgCubeNoDoublePass, false)]
    [InlineData(ProblemKind.XgCubeNoDoublePass, true)]
    [InlineData(ProblemKind.XgpChecker, false)]
    [InlineData(ProblemKind.XgpChecker, true)]
    [InlineData(ProblemKind.XgpCubeTooGood, false)]
    [InlineData(ProblemKind.XgpCubeTooGood, true)]
    [InlineData(ProblemKind.XgpCubeNoDoublePass, false)]
    [InlineData(ProblemKind.XgpCubeNoDoublePass, true)]
    public async Task InTheOldBand_ATapAtTheCentreOfEveryRowControlReachesIt(ProblemKind kind, bool normalView)
    {
        // Inside §2's floor (641 px wide and up, 768 px tall), with the drawer
        // closed and the panel folded by itself as it is there: answering and
        // at review, every button in the row and every cube pill takes a tap
        // at its centre, and the row is one line — the "⋯" holding the tail
        // wherever it would not fit beside the rest.
        await Page.SetViewportSizeAsync(1280, FloorHeight);
        await BootHomeAsync();
        if (normalView)
        {
            await DisableMaximizeAsync();
            await BootHomeAsync();
        }
        await StartOnAsync(kind);

        await AssertTheBandAsync("answering");

        await AnswerAsync(kind);

        await AssertTheBandAsync("at review");
    }
}
