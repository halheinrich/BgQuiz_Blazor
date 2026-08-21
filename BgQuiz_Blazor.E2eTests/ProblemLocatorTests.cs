using Microsoft.Playwright;
using Xunit.Abstractions;
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
/// cluster shares a line with the instrument before it (§4's ruling (i) —
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
    /// <summary>
    /// xUnit's per-test output sink, for <see cref="ReportRowGeometryAsync"/>
    /// alone. First use of it in this suite: every other scenario says what it
    /// means in assertions, and this one still does — the output is for a
    /// machine this session cannot run on.
    /// </summary>
    private readonly ITestOutputHelper _output;

    public ProblemLocatorTests(
        PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright)
    {
        _output = output;
    }

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

    /// <summary>
    /// The one evaluation behind <see cref="ReportRowGeometryAsync"/> — every
    /// box read in a single frame, so the four of them cannot describe two
    /// different layouts.
    /// </summary>
    private const string GeometryReportScript = """
        () => {
          const r1 = n => Math.round(n * 10) / 10;
          const box = (label, e) => {
            if (!e) return '  ' + label.padEnd(20) + '(absent)';
            const r = e.getBoundingClientRect();
            return '  ' + label.padEnd(20)
              + 'x=' + r1(r.x) + '  y=' + r1(r.y)
              + '  w=' + r1(r.width) + '  h=' + r1(r.height);
          };
          const q = sel => document.querySelector(sel);
          return [
            box('.action-row', q('.action-row')),
            box('.bg-cube-actions', q('.bg-cube-actions')),
            box('last instrument', q('.action-row > :nth-last-child(2)')),
            box('.action-row-tail', q('.action-row-tail')),
            '  body font-family:   ' + getComputedStyle(document.body).fontFamily,
            '  fonts.check Arial:     ' + document.fonts.check('12px Arial'),
            '  fonts.check Helvetica: ' + document.fonts.check('12px Helvetica'),
          ].join(String.fromCharCode(10));
        }
        """;

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
        await AssertClusterSharesTheLastInstrumentsLineAsync();
        await ReportRowGeometryAsync("answering (maximized)");

        await AnswerCubeNoDoubleAsync();

        // Review, normalized: the chip did not move and did not change, which
        // is the "one home, both states" half of the ruling.
        await Expect(Page.Locator(".status-strip")).ToHaveCountAsync(1);
        await AssertChipReadsTheFixtureAsync();
        await AssertChipSitsBelowTheBoardAsync();
        await AssertClusterSharesTheLastInstrumentsLineAsync();
        await ReportRowGeometryAsync("review (normalized)");
    }

    /// <summary>
    /// <b>Diagnostic only — asserts nothing.</b> Writes the action row's
    /// geometry and the browser's resolved font situation to the test output,
    /// once per state.
    ///
    /// <para>
    /// It exists for one environment this session cannot reach. The condition
    /// that turned umbrella CI red lives on Linux Chromium, whose font
    /// fallbacks are not Windows', and the assertions above can only report
    /// whether the contract held — not which of the several mechanisms that can
    /// make this row taller was in play. These four boxes tell them apart: a
    /// <c>.bg-cube-actions</c> box taller than one control is the producer's
    /// pill block wrapping, and the row's <c>align-items: center</c> then moves
    /// every short item down past that block's top with nothing having wrapped;
    /// a last instrument sitting below the row's own top is the instruments
    /// wrapping onto a second flex line; and a cluster below the last
    /// instrument, or taller than it, is the cluster's own CSS not in effect —
    /// the one mechanism that would be a defect here rather than a consequence
    /// of something upstream.
    /// </para>
    ///
    /// <para>
    /// <b>Where it shows up.</b> <c>dotnet test</c>'s console logger prints
    /// captured output for <i>failed</i> tests, which is the case this is for.
    /// A passing run keeps it in the TRX only; <c>--logger
    /// "console;verbosity=detailed"</c> surfaces it there too.
    /// </para>
    /// </summary>
    private async Task ReportRowGeometryAsync(string state)
    {
        var report = await Page.EvaluateAsync<string>(GeometryReportScript);

        _output.WriteLine($"[locator row geometry] {state}");
        _output.WriteLine(report);
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
    /// trailing cluster shares a line with the <b>last answer instrument before
    /// it</b>. A cluster that opened a line of its own would still render every
    /// element the DOM pins look for — it would simply have added a row and
    /// taken the difference out of the board, which is the one thing the
    /// fixed-height contract forbids.
    ///
    /// <para>
    /// <b>Why the LAST instrument and not the first</b> (re-keyed after umbrella
    /// CI run 32520062178 went red on Linux while the same commit passed on
    /// Windows: <c>cluster.Y=779.98, first control.Y=742.80</c>). Measured
    /// locally across every way the row can grow taller, the cluster's top
    /// tracks the last instrument's and nothing else:
    /// </para>
    ///
    /// <list type="bullet">
    ///   <item><description>instruments wrapped onto a second flex line (forced
    ///     with Verdana + 8px letter-spacing, so they needed 981.7px against
    ///     922px): cluster +46px below the first instrument, <b>0 below the
    ///     last</b>;</description></item>
    ///   <item><description>the producer's cube-pill block wrapped internally
    ///     and grew tall (forced by capping its width), so the row's
    ///     <c>align-items: center</c> pushed every short item down past that
    ///     block's top: cluster +20px (two pill lines) or +62px (three) below
    ///     the first instrument, <b>0 below the last</b> in both;</description></item>
    ///   <item><description>and only with the cluster's own
    ///     <c>flex: 1 1 0; min-width: 0</c> removed did it fall below the last
    ///     instrument too (+46px on both counts).</description></item>
    /// </list>
    ///
    /// <para>
    /// So the last-instrument form is the arc's contract stated exactly: it
    /// holds however tall the row gets for reasons upstream of this chip, and
    /// the one thing that breaks it is the cluster being contents-sized again.
    /// That also makes it <b>diagnostic</b> — if CI ever fails this line, the
    /// cause is the cluster's own CSS and not the instruments ahead of it.
    /// </para>
    ///
    /// <para>
    /// <b>What this deliberately does not claim.</b> Not that the row is one
    /// line: that the answer instruments hold one line at a given width is
    /// <c>SPEC-quiz-view.md</c> §2's invariance floor, measured under Windows
    /// font metrics, and it is not this arc's to guarantee — the locator's
    /// contract is that the cluster costs the row no height, which is what the
    /// last-instrument form says. A row-height assertion here was measured and
    /// declined: under a genuinely wider real font (Verdana) the cube
    /// instruments still need only 589.7px of the 922px available at 1280 with
    /// the nav panel showing — 332px of slack — so whatever made CI's row taller
    /// was not instrument width, and a height pin would be pinning the
    /// producer's cube-pill block and the CI image's fonts rather than anything
    /// this arc controls.
    /// </para>
    /// </summary>
    private async Task AssertClusterSharesTheLastInstrumentsLineAsync()
    {
        // The cluster closes the row, which is what makes ":nth-last-child(2)"
        // the instrument immediately before it. Asserted rather than assumed —
        // otherwise a future control appended after the cluster would silently
        // re-point the handle at the cluster's new neighbour and leave this
        // comparing the wrong two boxes.
        await Expect(Page.Locator(".action-row > .action-row-tail:last-child")).ToHaveCountAsync(1);

        var lastInstrument = await Page.Locator(".action-row > :nth-last-child(2)").BoundingBoxAsync();
        var cluster = await Page.Locator(".action-row-tail").BoundingBoxAsync();

        Assert.NotNull(lastInstrument);
        Assert.NotNull(cluster);

        // Level with it — the cluster did not open a line of its own.
        Assert.True(
            Math.Abs(cluster!.Y - lastInstrument!.Y) < lastInstrument.Height,
            "Trailing cluster should share a line with the instrument before it; "
            + $"cluster.Y={cluster.Y}, last instrument.Y={lastInstrument.Y}.");

        // …and no taller than it, which is the other half and not a
        // restatement: a cluster that wrapped INSIDE itself keeps its top on
        // this line and grows downwards, so the levelness above still holds
        // while the row's height has doubled anyway. Measured against a control
        // in the same row rather than a pixel literal, so it says nothing about
        // fonts, viewport or fixture — only that the cluster costs the row no
        // more height than the buttons it sits beside.
        Assert.True(
            cluster.Height <= lastInstrument.Height + 1,
            "Trailing cluster should be one line tall, like the controls beside it; "
            + $"cluster.Height={cluster.Height}, last instrument.Height={lastInstrument.Height}.");
    }
}
