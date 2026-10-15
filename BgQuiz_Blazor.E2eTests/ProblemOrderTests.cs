using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The Order on Home, applied by a real Start over the published artifact
/// (<c>SPEC-filtering.md</c> §7; halheinrich/backgammon#402). The controller's
/// hand-off and each order's sequence are pinned in the unit suite; this is
/// the claim only a browser can make — that the control on the page reaches
/// the producer's composer through the real pick, parse and Start, and the
/// quiz asks its problems in the order chosen.
/// </summary>
public sealed class ProblemOrderTests : E2eTestBase
{
    public ProblemOrderTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright, output) { }

    /// <summary>The desktop window the problem locator's chip is laid out whole in (as in <c>ProblemLocatorTests</c>).</summary>
    private const int DesktopWidth = 1280;

    private const int DesktopHeight = 800;

    /// <summary>
    /// One of the Order's four values, by the id Home gives each:
    /// <c>problemOrder</c> and the producer's name for the value.
    /// </summary>
    private ILocator OrderOption(string order) => Page.Locator($"#problemOrder{order}");

    /// <summary>
    /// The problem locator's chip, whose text — its accessible half included —
    /// names the problem's source file whatever the chip's width shows of it
    /// (<c>SPEC-quiz-view.md</c> §4).
    /// </summary>
    private ILocator ProblemSource => Page.Locator(".action-row-tail .problem-locator");

    private ILocator HomeNavLink => Page.GetByRole(AriaRole.Link, new() { Name = ExpectedText.HomeNavLink });

    /// <summary>
    /// A position file and then a match, in that order: <i>As read</i> asks
    /// the position first, because files come in the sample's order; <i>By
    /// move number, ascending</i> asks it last, because a position file's
    /// decision has no move number, so the match's first decision opens the
    /// quiz. Two Starts over one pick, the Order changed between them on Home
    /// — the setting is read when a Start builds the run's source, and
    /// nothing else about the pick changes.
    /// </summary>
    [Fact]
    public async Task TheOrderChosenOnHome_IsTheOrderARealStartAsksIn()
    {
        await Page.SetViewportSizeAsync(DesktopWidth, DesktopHeight);
        await BootHomeAsync();
        await PickFilesInOrderAsync(
            "order",
            (CheckerFixture, FixtureBytes(CheckerFixture)),
            (SyntheticXgMatch.StagedFileName, SyntheticXgMatch.Bytes()));
        await ExpectFilterInEffectAsync();

        await Expect(OrderOption("AsRead")).ToBeCheckedAsync(); // the default, as the setting reads with nothing stored
        await StartQuizAsync();
        await Expect(ProblemSource).ToContainTextAsync(CheckerFixture);

        await HomeNavLink.ClickAsync();
        await ExpectUrlAsync(AppRoute.Home);
        await OrderOption("ByMoveNumberAscending").CheckAsync();
        await Expect(OrderOption("ByMoveNumberAscending")).ToBeCheckedAsync();
        await StartQuizAsync();
        await Expect(ProblemSource).ToContainTextAsync(SyntheticXgMatch.StagedFileName);
    }
}
