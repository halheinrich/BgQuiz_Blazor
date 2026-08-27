using Microsoft.Playwright;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The problem's locator in a real browser (issues
/// <c>halheinrich/backgammon#115</c> and <c>halheinrich/backgammon#125</c>,
/// conforming to <c>SPEC-quiz-view.md</c> §4): the chip that says where a
/// position came from, over <b>both</b> branches of the ruling — a committed
/// <c>.xgp</c>, which the file name alone locates, and a synthesized
/// <c>.xg</c> match, which needs <c>Game n · Move m</c> beside it.
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
/// on its own. The match scenario keeps the same posture: its labels and
/// separator are literals here, and its numbers come from what
/// <see cref="SyntheticXgMatch"/> was told to build.
/// </para>
///
/// <para>
/// <b>Why the money fixture carries the ruling, and the match fixture the
/// fork.</b> The money decision is what §4's ruling was written for: no score
/// frames it, and while answering maximized the title strip — the only other
/// surface naming the file — is gone. An <c>.xgp</c> is a standalone position,
/// so ruling (ii) gives it the file name alone. The match half was the gap
/// (<c>halheinrich/backgammon#125</c>): every committed fixture is an
/// <c>.xgp</c>, real <c>.xg</c> exports carry real players' names and live
/// where CI cannot see them, so the branch that shows coordinates — and the
/// shrink order at the tail's widest — had never been smoked at all.
/// <see cref="SyntheticXgMatch"/> closes it with a match built at run time.
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
    /// The control the cluster follows while <b>answering</b> a cube problem —
    /// the row is the cube radios, Submit, Skip, then the cluster. Named by the
    /// label a user reads, per this suite's independent-literal posture, and
    /// because a name is the thing a failure message can be honest about; the
    /// page needs no new handle for it.
    /// </summary>
    private const string SkipButton = "Skip";

    /// <summary>
    /// And the control it follows at <b>review</b>, where the row is Continue,
    /// Redo, then the cluster. Two names rather than one selector because the
    /// row genuinely has two compositions — pretending otherwise is what a
    /// positional selector does.
    /// </summary>
    private const string RedoButton = "Redo";

    /// <summary>
    /// The one evaluation behind <see cref="ReportRowGeometryAsync"/> — every
    /// box read in a single frame, so the four of them cannot describe two
    /// different layouts.
    /// </summary>
    private const string GeometryReportScript = """
        () => {
          const r1 = n => Math.round(n * 10) / 10;
          const geom = e => {
            const r = e.getBoundingClientRect();
            return 'x=' + r1(r.x) + '  y=' + r1(r.y)
              + '  w=' + r1(r.width) + '  h=' + r1(r.height);
          };
          const box = (label, e) =>
            '  ' + label.padEnd(20) + (e ? geom(e) : '(absent)');

          // Every child in order, with what it IS as well as where it is —
          // the log has to be able to say which element a positional selector
          // would have picked, not just that the numbers disagreed.
          const children = (label, parent) => {
            if (!parent) return ['  ' + label + ': (absent)'];
            const kids = [...parent.children];
            if (!kids.length) return ['  ' + label + ': (no element children)'];
            return ['  ' + label + ' (' + kids.length + ' children, in order):']
              .concat(kids.map((k, i) =>
                '    [' + i + '] <' + k.tagName.toLowerCase() + '>'
                  + ' display=' + getComputedStyle(k).display
                  + '  ' + geom(k)
                  + '  text="' + (k.textContent || '').trim().slice(0, 24) + '"'
                  + '  class="' + (k.className || '') + '"'));
          };

          const q = sel => document.querySelector(sel);
          return []
            .concat([
              '  viewport:            ' + window.innerWidth + 'x' + window.innerHeight
                + '  devicePixelRatio=' + window.devicePixelRatio,
              '  body font-family:    ' + getComputedStyle(document.body).fontFamily,
              '  fonts.check Arial:     ' + document.fonts.check('12px Arial'),
              '  fonts.check Helvetica: ' + document.fonts.check('12px Helvetica'),
              box('.action-row', q('.action-row')),
              box('.bg-cube-actions', q('.bg-cube-actions')),
              box('.action-row-tail', q('.action-row-tail')),
            ])
            .concat(children('.action-row', q('.action-row')))
            .concat(children('.bg-cube-actions', q('.bg-cube-actions')))
            .join(String.fromCharCode(10));
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

        // The geometry is read BEFORE anything can throw and written in a
        // finally, so a failing assertion cannot swallow the one piece of
        // evidence that explains it. See ReportRowGeometryAsync.
        string answeringGeometry = await CaptureRowGeometryAsync();
        try
        {
            await AssertChipReadsTheFixtureAsync();
            await AssertChipSitsBelowTheBoardAsync();
            await AssertClusterSharesTheLineOfAsync(SkipButton);
        }
        finally
        {
            ReportRowGeometry("answering (maximized)", answeringGeometry);
        }

        await AnswerCubeNoDoubleAsync();

        // Review, normalized: the chip did not move and did not change, which
        // is the "one home, both states" half of the ruling.
        await Expect(Page.Locator(".status-strip")).ToHaveCountAsync(1);

        string reviewGeometry = await CaptureRowGeometryAsync();
        try
        {
            await AssertChipReadsTheFixtureAsync();
            await AssertChipSitsBelowTheBoardAsync();
            await AssertClusterSharesTheLineOfAsync(RedoButton);
        }
        finally
        {
            ReportRowGeometry("review (normalized)", reviewGeometry);
        }
    }

    /// <summary>
    /// The other branch of §4's ruling (ii), on the fixture that only exists at
    /// run time: a decision from a real <b>match</b> file is located by its file
    /// name <i>and</i> its coordinates within that file
    /// (<c>halheinrich/backgammon#125</c>).
    ///
    /// <para>
    /// <b>The coordinates are the subject, and they are asserted as text.</b>
    /// The numbers come from <see cref="SyntheticXgMatch"/>'s construction
    /// parameters, the labels and separator are this suite's own literals, and
    /// the two are joined here — so the pin fails at a stated expectation if the
    /// builder's emission ever moves, and it cannot be satisfied by whatever the
    /// app rendered. Both halves being present is also the discriminant: this
    /// same page shows the file name <i>alone</i> for the committed
    /// <c>.xgp</c> above, so the pair of scenarios is what proves the fork is a
    /// fork and not a constant.
    /// </para>
    ///
    /// <para>
    /// <b>And it smokes the shrink ruling, which nothing else did.</b> §4 ruling
    /// (i) orders the tail's give: the XGID's text goes first, down to its copy
    /// button, then the locator's file name — <i>the numbers never</i>. This is
    /// the widest the tail ever gets (a name past the visible cap, plus
    /// coordinates), and the two assertions below are that ruling's two halves
    /// at that width: the coordinates read in full, and the cluster still costs
    /// the row no line. A shrink order that gave the numbers away would fail the
    /// first; one that gave nothing away would fail the second.
    /// </para>
    /// </summary>
    [Fact]
    public async Task MatchProblem_IsLocatedByGameAndMove_WhileAnswering()
    {
        await Page.SetViewportSizeAsync(DesktopWidth, DesktopHeight);
        await BootHomeAsync();
        await PickSynthesizedFileAsync(
            SyntheticXgMatch.StagedFileName, SyntheticXgMatch.Bytes());
        await ApplyFilterAsync();
        await StartQuizAsync();

        // The maximized answering composition, as above: no status strip, no
        // title strip, nothing but this chip saying where the problem came from.
        await Expect(Page.Locator(".status-strip")).ToHaveCountAsync(0);

        string geometry = await CaptureRowGeometryAsync();
        try
        {
            await AssertChipLocatesTheMatchDecisionAsync();
            await AssertChipSitsBelowTheBoardAsync();
            await AssertClusterSharesTheLineOfAsync(SkipButton);
        }
        finally
        {
            ReportRowGeometry("answering (maximized), synthesized .xg", geometry);
        }
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
    private Task<string> CaptureRowGeometryAsync() =>
        Page.EvaluateAsync<string>(GeometryReportScript);

    private void ReportRowGeometry(string state, string report)
    {
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
    /// The chip on a match decision: the file name half present, and beside it
    /// the coordinates in full — <c>Game n · Move m</c>, neither number elided.
    /// The expected string is assembled from
    /// <see cref="SyntheticXgMatch.CubeGameNumber"/> and
    /// <see cref="SyntheticXgMatch.CubeMoveNumber"/>, which the fixture derives
    /// from what it was told to build, and from the labels and separator spelled
    /// out here per this suite's independent-literal posture.
    ///
    /// <para>
    /// The file-name half is asserted <b>present</b> rather than re-derived: the
    /// middle-truncation rule is the money scenario's pin above, and restating
    /// it here would be a second source for one fact.
    /// </para>
    /// </summary>
    private async Task AssertChipLocatesTheMatchDecisionAsync()
    {
        await Expect(Chip).ToBeVisibleAsync();
        await Expect(ChipFileName).ToBeVisibleAsync();
        await Expect(ChipCoordinates).ToHaveTextAsync(
            $"Game {SyntheticXgMatch.CubeGameNumber} · Move {SyntheticXgMatch.CubeMoveNumber}");
        await Expect(Page.Locator(".problem-locator .visually-hidden"))
            .ToHaveTextAsync(SyntheticXgMatch.StagedFileName);
    }

    /// <summary>
    /// Off the canvas as real geometry, not merely as DOM position: the chip's
    /// box starts below the board region's. The same claim the XGID badge's own
    /// smoke makes, and for the same reason — §4 homes both in the bottom row
    /// precisely so neither obscures board content.
    ///
    /// <para>
    /// Retried, and the board required to have a real box before it is used as
    /// one: the comparison is arithmetic against that box, so a board that
    /// failed to render measures zero high and satisfies it trivially. Both
    /// halves, and the argument for them, are
    /// <c>MaximizeBoardTests.AssertBadgeSitsBelowTheBoardAsync</c>'s
    /// (<c>halheinrich/backgammon#127</c>) — the two pins make the same claim
    /// about two chips and would have gone quiet in the same way.
    /// </para>
    /// </summary>
    private Task AssertChipSitsBelowTheBoardAsync() => ExpectToPassAsync(async () =>
    {
        var board = await LaidOutBoxAsync(Page.Locator(".board-container"), "the board region");
        var chip = await LaidOutBoxAsync(Chip, "the locator chip");

        Assert.True(
            chip.Y >= board.Y + board.Height,
            $"Locator chip should start below the board region; chip.Y={chip.Y}, board bottom={board.Y + board.Height}.");
    });

    /// <summary>
    /// §4's ruling (i), as the only observation that can actually catch it: the
    /// trailing cluster shares a line with <b>the named control immediately
    /// before it</b> — Skip while answering, Redo at review. A cluster that
    /// opened a line of its own would still render every element the DOM pins
    /// look for; it would simply have added a row and taken the difference out
    /// of the board, which is the one thing the fixed-height contract forbids.
    ///
    /// <para>
    /// <b>Why the control before it and not the first in the row</b> (re-keyed
    /// after umbrella CI run 32520062178 went red on Linux while the same commit
    /// passed on Windows: <c>cluster.Y=779.98, first control.Y=742.80</c>).
    /// Measured locally across every way the row can grow taller, the cluster's
    /// top tracks its immediate predecessor's and nothing else:
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
    /// <b>And why the control is named rather than counted</b> (the second red,
    /// run 32530552576: <c>cluster.Y=779.98, last instrument.Y=751.80</c>).
    /// The positional form said the two boxes disagreed but not what it had
    /// measured, and on Linux <c>:nth-last-child(2)</c> resolved to something
    /// under 28px tall — which is not the 38px Skip this machine sees. A named
    /// control fails on its own identity first, and the geometry block emitted
    /// ahead of these assertions carries the row's whole child list, so the log
    /// says which element it was.
    /// </para>
    ///
    /// <para>
    /// So this form is the arc's contract stated exactly: it holds however tall
    /// the row gets for reasons upstream of this chip, and the one thing that
    /// breaks it is the cluster being contents-sized again.
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
    private async Task AssertClusterSharesTheLineOfAsync(string controlName)
    {
        // The control is NAMED, and then confirmed to be the one the cluster
        // actually follows. Both steps matter, and the second is the lesson of
        // the second CI red: this used to read `.action-row > :nth-last-child(2)`
        // and trust it, so when that resolved to something unexpected on Linux
        // the only symptom was two boxes failing to line up, with nothing in
        // the message saying what had been measured. Now a wrong element fails
        // here, naming itself, and the geometry block above says what it was.
        await Expect(Page.Locator(".action-row > .action-row-tail:last-child")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".action-row > :nth-last-child(2)")).ToHaveTextAsync(controlName);

        // Retried, and both boxes required to be real ones
        // (halheinrich/backgammon#127). The two Expects above prove the row's
        // SHAPE has landed, which is not yet a statement about its geometry —
        // this runs on the render that follows a Start or a Submit, and the two
        // reds this pin has already had were both about a slower machine's
        // layout. The control is also the yardstick for both assertions: one
        // measuring zero would turn the first into a demand for exact equality,
        // and one measuring the whole row would make the second true of a
        // cluster that had wrapped three times.
        await ExpectToPassAsync(async () =>
        {
            var control = await LaidOutBoxAsync(
                Page.GetByRole(AriaRole.Button, new() { Name = controlName }), controlName);
            var cluster = await LaidOutBoxAsync(
                Page.Locator(".action-row-tail"), "the trailing cluster");

            // Level with it — the cluster did not open a line of its own.
            Assert.True(
                Math.Abs(cluster.Y - control.Y) < control.Height,
                $"Trailing cluster should share a line with {controlName}; cluster.Y={cluster.Y}, "
                + $"{controlName}.Y={control.Y}, {controlName}.Height={control.Height}.");

            // …and no taller than it, which is the other half and not a
            // restatement: a cluster that wrapped INSIDE itself keeps its top on
            // this line and grows downwards, so the levelness above still holds
            // while the row's height has doubled anyway. Measured against a
            // control in the same row rather than a pixel literal, so it says
            // nothing about fonts, viewport or fixture — only that the cluster
            // costs the row no more height than the button it sits beside.
            Assert.True(
                cluster.Height <= control.Height + 1,
                $"Trailing cluster should be one line tall, like {controlName} beside it; "
                + $"cluster.Height={cluster.Height}, {controlName}.Height={control.Height}.");
        });
    }
}
