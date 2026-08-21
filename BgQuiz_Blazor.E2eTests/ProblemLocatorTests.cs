using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The problem's locator in a real browser (issue
/// <c>halheinrich/backgammon#115</c>, conforming to <c>SPEC-quiz-view.md</c>
/// §4): the chip that names the file a position came from, on the one fixture
/// the ruling was written for — a <b>money</b> decision, which no match score
/// frames and which, while answering maximized, nothing else on the page names
/// at all.
///
/// <para>
/// <b>Two things only a browser can judge, and they are why this scenario
/// exists.</b> The bUnit pins assert the render tree; they cannot see that the
/// cluster shares a line with the answer instruments (§4's ruling (i) —
/// shrink, never wrap), because AngleSharp evaluates no CSS and the whole
/// mechanism is flex sizing. And they cannot see that the chip sits below the
/// board rather than over it. Both are asserted here as geometry.
/// </para>
///
/// <para>
/// <b>Literals, not constants</b> — this suite references no app assembly by
/// design, so the file name the chip renders is spelled out. That is what makes
/// it a pin on the derivation rather than a restatement of it: the file is
/// staged as <c>long-money-session-2026-04-12.xgp</c> and the chip must turn
/// that into <c>long-mon…26-04-12</c> — extension dropped, middle elided — all
/// on its own.
/// </para>
///
/// <para>
/// <b>Why the coordinates are absent here, and where that branch is covered.</b>
/// An <c>.xgp</c> is a standalone position, so §4's ruling (ii) gives it the
/// file name alone — the file <i>is</i> the locator. Every committed fixture in
/// this suite is an <c>.xgp</c> (deliberately: single-decision files are what
/// make each scenario one problem long), and a real multi-game <c>.xg</c> match
/// carries real players' names, so the <c>Game n · Move m</c> branch is pinned
/// in the unit suite against hand-built records rather than smoked here.
/// </para>
/// </summary>
public sealed class ProblemLocatorTests : E2eTestBase
{
    public ProblemLocatorTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    /// <summary>
    /// The tight desktop viewport §4's ruling (i) was measured at, fixed here
    /// rather than left to the runner's default: the shrink order only has work
    /// to do where the row is short of width, and a wider default would leave
    /// this passing without ever exercising it.
    /// </summary>
    private const int DesktopWidth = 1280;

    private const int DesktopHeight = 800;

    /// <summary>
    /// The committed money cube fixture, staged under a name the length of a
    /// real eXtreme Gammon export. The length is the point, twice over: it is
    /// what makes the middle-truncation observable at all, and it is what puts
    /// the trailing cluster under enough width pressure for §4's ruling (i) to
    /// have anything to do — under this suite's own short fixture names the
    /// cluster fits whatever the CSS says, and the first-line assertion below
    /// would pass on a cluster that could not shrink.
    /// </summary>
    private const string StagedFileName = "long-money-session-2026-04-12.xgp";

    /// <summary>
    /// What the chip must make of that name: the extension dropped, the middle
    /// elided, the two ends kept. Spelled out rather than derived — this suite
    /// references no app assembly, so an independent literal is the only way to
    /// pin a derivation instead of restating it.
    /// </summary>
    private const string ExpectedFileName = "long-mon…26-04-12";

    /// <summary>The locator, in the one home §4 gives it.</summary>
    private ILocator Chip => Page.Locator(".action-row-tail .problem-locator");

    private ILocator ChipFileName => Page.Locator(".problem-locator-file");

    private ILocator ChipCoordinates => Page.Locator(".problem-locator-where");

    [Fact]
    public async Task MoneyProblem_IsNamedByItsSourceFile_AnsweringAndReview()
    {
        await Page.SetViewportSizeAsync(DesktopWidth, DesktopHeight);
        await BootHomeAsync();
        await PickFixtureUnderNameAsync(CubeFixture, StagedFileName);
        await ApplyFilterAsync();
        await StartQuizAsync();

        // Answering, maximized (the default since #113). This is the
        // composition the ruling came out of: the producer's title strip is
        // gone, the score panel with it, and a money decision has no score to
        // be framed by — so the chip is the only thing on the page that says
        // where this position came from.
        await Expect(Page.Locator(".status-strip")).ToHaveCountAsync(0);
        await AssertChipReadsTheFixtureAsync();
        await AssertChipSitsBelowTheBoardAsync();
        await AssertClusterSharesTheRowsFirstLineAsync();

        await AnswerCubeNoDoubleAsync();

        // Review, normalized: the chip did not move and did not change, which
        // is the "one home, both states" half of the ruling.
        await Expect(Page.Locator(".status-strip")).ToHaveCountAsync(1);
        await AssertChipReadsTheFixtureAsync();
        await AssertChipSitsBelowTheBoardAsync();
        await AssertClusterSharesTheRowsFirstLineAsync();
    }

    /// <summary>
    /// The chip's text, exactly: the staged name with its extension dropped
    /// and its middle elided, and — because the fixture is a standalone
    /// position — no game or move number beside it (§4's ruling (ii)). The
    /// untruncated name, extension and all, is still on the page for a screen
    /// reader, which is the third assertion: what the truncation hides it must
    /// not lose.
    /// </summary>
    private async Task AssertChipReadsTheFixtureAsync()
    {
        await Expect(Chip).ToBeVisibleAsync();
        await Expect(ChipFileName).ToHaveTextAsync(ExpectedFileName);
        await Expect(ChipCoordinates).ToHaveCountAsync(0);
        await Expect(Page.Locator(".problem-locator .visually-hidden"))
            .ToHaveTextAsync(StagedFileName);
    }

    /// <summary>
    /// Off the canvas as real geometry, not merely as DOM position: the chip's
    /// box starts below the board region's. The same claim the XGID badge's own
    /// smoke makes, and for the same reason — §4 homes both in the bottom row
    /// precisely so neither obscures board content.
    /// </summary>
    private async Task AssertChipSitsBelowTheBoardAsync()
    {
        var board = await Page.Locator(".board-container").BoundingBoxAsync();
        var chip = await Chip.BoundingBoxAsync();

        Assert.NotNull(board);
        Assert.NotNull(chip);
        Assert.True(
            chip!.Y >= board!.Y + board.Height,
            $"Locator chip should start below the board region; chip.Y={chip.Y}, board bottom={board.Y + board.Height}.");
    }

    /// <summary>
    /// §4's ruling (i), as the only observation that can actually catch it: the
    /// trailing cluster sits on the action row's FIRST line, level with the
    /// answer instruments. A cluster that wrapped would still render every
    /// element the DOM pins look for — it would simply have doubled the row's
    /// height and taken the difference out of the board, which is the one thing
    /// the fixed-height contract forbids.
    ///
    /// <para>
    /// <b>What it does and does not discriminate</b>, checked by mutation
    /// rather than assumed. Take the cluster's <c>flex-basis: 0</c> away and
    /// this fails on this fixture at this viewport, which is the mechanism's
    /// load-bearing half. Take its <c>min-width: 0</c> away and it still
    /// passes — a standalone position's chip is a file name and nothing else,
    /// so it can shrink to nothing and the cluster's floor never binds. That
    /// half of the ruling only bites where the chip also carries coordinates,
    /// i.e. on a multi-game <c>.xg</c> match, which this suite has no fixture
    /// for; it is held instead by the stylesheet pin in <c>PageTests</c> and by
    /// the measurement recorded against it.
    /// </para>
    ///
    /// <para>
    /// Asserted as a relationship between two boxes rather than as a row height
    /// in pixels: a height literal would pin the fonts, the fixture and the
    /// viewport all at once and fail for any of them, while "same line as the
    /// first control" is true at every width where the row has not wrapped for
    /// reasons of its own.
    /// </para>
    /// </summary>
    private async Task AssertClusterSharesTheRowsFirstLineAsync()
    {
        var firstControl = await Page.Locator(".action-row > :first-child").BoundingBoxAsync();
        var cluster = await Page.Locator(".action-row-tail").BoundingBoxAsync();

        Assert.NotNull(firstControl);
        Assert.NotNull(cluster);
        Assert.True(
            Math.Abs(cluster!.Y - firstControl!.Y) < cluster.Height,
            $"Trailing cluster should share the action row's first line; cluster.Y={cluster.Y}, first control.Y={firstControl.Y}.");
    }
}
