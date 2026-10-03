using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// One budget from the outset (SPEC-quiz-view.md §4, Hal, 2026-10-03: "Fix it
/// now"): every switch the action row makes — the panel's fold, the "⋯", the
/// labels' form — reads one budget for every state; it holds before the first
/// problem of any kind, the cube pills measured at their widest without a cube
/// decision; and no control is covered at any moment, the first render before
/// any measurement included.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two instruments, both on the browser's side</b> (the context's init
/// script; the app ships no seam for them):
/// </para>
/// <list type="bullet">
///   <item><b>A frame hold.</b> The row-fit module measures only in a frame's
///   animation callbacks (<c>actionRowFit.js</c>), so holding
///   <c>requestAnimationFrame</c> holds every measurement and everything it
///   applies — the panel's fold and the report to the page — not merely the
///   report. Released, the held callbacks run in the next real frame.</item>
///   <item><b>A coverage sampler.</b> A MutationObserver that, after every DOM
///   change while it runs, hit-tests the centre of every row control and
///   counts the row's lines: "no control is covered at any moment", checked at
///   every moment the DOM could be painted in, not only where a test happens
///   to look.</item>
/// </list>
/// <para>
/// A first load also holds the row-fit module's fetch (a route), which is the
/// longest a first load waits before it can measure: the page shows no row
/// meanwhile.
/// </para>
/// </remarks>
public sealed class OneBudgetTests : E2eTestBase
{
    public OneBudgetTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    protected override string? ContextInitScript => """
        (() => {
          // The frame hold: requestAnimationFrame queues while held, and the
          // queue runs in the next real frame on release.
          const raf = window.requestAnimationFrame.bind(window);
          const caf = window.cancelAnimationFrame.bind(window);
          const queue = new Map();
          let held = false, next = -1;
          window.requestAnimationFrame = cb => {
            if (!held) return raf(cb);
            const id = next--;
            queue.set(id, cb);
            return id;
          };
          window.cancelAnimationFrame = id => { if (!queue.delete(id)) caf(id); };
          window.__frames = {
            hold() { held = true; },
            release() {
              held = false;
              const callbacks = [...queue.values()];
              queue.clear();
              for (const cb of callbacks) raf(cb);
            },
            get queued() { return queue.size; },
            settle: () => new Promise(r => raf(() => raf(r))),
          };

          // The coverage sampler.
          const sample = { running: false, samples: 0, violations: [], first: null, observer: null };
          const name = c => c.getAttribute('aria-label')
            || c.querySelector('input')?.getAttribute('aria-label') || c.textContent.trim();
          function check() {
            const row = document.querySelector('.action-row');
            if (!row) return;
            sample.samples++;
            const covered = [];
            for (const c of row.querySelectorAll('button, .bg-cube-action')) {
              if (c.closest('[role=menu]')) continue;
              const was = c.disabled;
              if (c instanceof HTMLButtonElement) c.disabled = false;
              const r = c.getBoundingClientRect();
              const e = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
              if (c instanceof HTMLButtonElement) c.disabled = was;
              if (!c.contains(e)) covered.push(name(c) + ' <- ' + (e ? (e.getAttribute('aria-label') || e.className || e.tagName) : 'nothing'));
            }
            const primary = row.querySelector('.btn-lg');
            const lines = primary ? Math.round(row.getBoundingClientRect().height / primary.getBoundingClientRect().height) : 1;
            const state = {
              pending: row.hasAttribute('data-nav-fold-pending'),
              autoFolded: document.documentElement.hasAttribute('data-nav-autofold'),
              panel: Math.round(document.querySelector('.sidebar').getBoundingClientRect().width),
              tailFolded: row.querySelector('.action-row-tail > .tail-menu') !== null,
              pills: [...row.querySelectorAll('.bg-cube-action')].map(p => p.textContent.trim()).join('|'),
            };
            if (sample.first === null) sample.first = state;
            if (covered.length > 0 || lines !== 1) {
              sample.violations.push(JSON.stringify(state) + ' lines=' + lines + ' covered=[' + covered.join('; ') + ']');
            }
            // The hit-test lifted and restored `disabled`: drop those records.
            sample.observer?.takeRecords();
          }
          window.__coverage = {
            start() {
              sample.observer = new MutationObserver(check);
              sample.observer.observe(document.documentElement, { subtree: true, childList: true, attributes: true, characterData: true });
              check();
            },
            stop() { sample.observer?.disconnect(); sample.observer = null; },
            get report() { return JSON.stringify({ samples: sample.samples, violations: sample.violations, first: sample.first }); },
          };
        })();
        """;

    private sealed record Coverage(int Samples, string[] Violations, JsonElement? First);

    private async Task<Coverage> CoverageAsync() =>
        JsonSerializer.Deserialize<Coverage>(
            await Page.EvaluateAsync<string>("() => window.__coverage.report"),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private Task SettleAsync() => Page.EvaluateAsync("() => window.__frames.settle()");

    private async Task ResizeAsync(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await SettleAsync();
    }

    private ILocator Row => Page.Locator(".action-row");

    private ILocator More =>
        Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.MoreButton, Exact = true });

    private ILocator CollapseRail =>
        Page.GetByRole(AriaRole.Checkbox, new() { Name = ExpectedText.HideNavigationPanelCheckbox });

    private ILocator LivePillCaptions => Page.Locator(".action-row .bg-cube-actions label");

    /// <summary>The problem a quiz starts on.</summary>
    public enum FirstProblem
    {
        /// <summary>A checker play from a <c>.xg</c> match (<see cref="SyntheticXgMatch.CheckerFirstBytes"/>).</summary>
        XgChecker,

        /// <summary>A cube decision from a <c>.xg</c> match, reading Too good (<see cref="SyntheticXgMatch.Bytes"/>).</summary>
        XgCube,

        /// <summary>A <c>.xgp</c> position: a cube decision reading No double / Pass (<see cref="E2eTestBase.CubeFixture"/>).</summary>
        Xgp,
    }

    /// <summary>How the quiz page is entered.</summary>
    public enum Entry
    {
        /// <summary>The quiz's start: the page's first load in the run.</summary>
        FirstLoad,

        /// <summary>Back from Show stats, which re-creates the page.</summary>
        ReturnFromStats,
    }

    private async Task PickAsync(FirstProblem problem)
    {
        switch (problem)
        {
            case FirstProblem.XgChecker:
                await PickSynthesizedFileAsync(SyntheticXgMatch.CheckerFirstStagedFileName, SyntheticXgMatch.CheckerFirstBytes());
                break;
            case FirstProblem.XgCube:
                await PickSynthesizedFileAsync(SyntheticXgMatch.StagedFileName, SyntheticXgMatch.Bytes());
                break;
            case FirstProblem.Xgp:
                await PickFixtureAsync(CubeFixture);
                break;
        }
        await ApplyFilterAsync();
    }

    private async Task ExpectTheFirstProblemAsync(FirstProblem problem)
    {
        if (problem == FirstProblem.XgChecker)
            await Expect(Page.Locator(".bg-play-entry")).ToBeVisibleAsync();
        else
            await Expect(LivePillCaptions).ToHaveCountAsync(4);
    }

    /// <summary>The board's laid-out box, rounded to a tenth of a pixel.</summary>
    private async Task<(double X, double Y, double W, double H)> BoardAsync()
    {
        var b = await LaidOutBoxAsync(Page.Locator(".board-container .bg-diagram"), "the board");
        return (Math.Round(b.X, 1), Math.Round(b.Y, 1), Math.Round(b.Width, 1), Math.Round(b.Height, 1));
    }

    [Theory]
    [InlineData(FirstProblem.XgChecker, Entry.FirstLoad)]
    [InlineData(FirstProblem.XgChecker, Entry.ReturnFromStats)]
    [InlineData(FirstProblem.XgCube, Entry.FirstLoad)]
    [InlineData(FirstProblem.XgCube, Entry.ReturnFromStats)]
    [InlineData(FirstProblem.Xgp, Entry.FirstLoad)]
    [InlineData(FirstProblem.Xgp, Entry.ReturnFromStats)]
    public async Task AtTheFloor_WithThePanelShowing_NothingIsCovered_WhileTheFirstFitIsHeld_OrAfter(
        FirstProblem problem, Entry entry)
    {
        // 641 x 768, §2's floor corner, the user's panel preference showing.
        await Page.SetViewportSizeAsync(641, 768);
        await BootHomeAsync();
        await PickAsync(problem);
        await Expect(CollapseRail).Not.ToBeCheckedAsync();

        if (entry == Entry.FirstLoad)
        {
            // Hold the row-fit module's fetch, and every frame's measurement.
            var module = new TaskCompletionSource();
            await Page.RouteAsync("**/js/actionRowFit*.js", async route =>
            {
                await module.Task;
                await route.ContinueAsync();
            });
            await Page.EvaluateAsync("() => { window.__frames.hold(); window.__coverage.start(); }");
            await StartQuizAsync();
            await ExpectUrlAsync("/quiz");

            // No row is shown that nothing can fit: none while the module waits.
            await Expect(Row).ToHaveCountAsync(0);
            module.SetResult();
        }
        else
        {
            await StartQuizAsync();
            await ExpectTheFirstProblemAsync(problem);
            await SettleAsync();
            await More.ClickAsync();
            await Page.GetByRole(AriaRole.Menuitem, new() { Name = ExpectedText.ShowStatsButton, Exact = true }).ClickAsync();
            await ExpectUrlAsync("/stats");
            var back = Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.BackToQuizButton });
            await Expect(back).ToBeVisibleAsync();
            await Expect(CollapseRail).Not.ToBeCheckedAsync();
            await Page.EvaluateAsync("() => { window.__frames.hold(); window.__coverage.start(); }");
            await back.ClickAsync();
            await ExpectUrlAsync("/quiz");
        }

        // Phase 1: the first fit held. The row is in its pending presentation,
        // which covers nothing, adds no line, and leaves the user's preference
        // as it was (the rail still says showing) while the layout folds the
        // panel for it.
        await ExpectTheFirstProblemAsync(problem);
        var held = await ActionRowGeometry.FitAsync(Page);
        Assert.True(held.Pending, "the row is unmeasured while its fit is held");
        Assert.True(await Page.EvaluateAsync<int>("() => window.__frames.queued") > 0, "a fit is waiting on the held frame");
        Assert.Equal(1, held.RowLines);
        Assert.True(held.TailFolded);
        Assert.Equal(0, (await Panel.BoundingBoxAsync())!.Width);
        await Expect(CollapseRail).Not.ToBeCheckedAsync();
        var coveredHeld = await ActionRowGeometry.CoveredControlsAsync(Page);
        Assert.True(coveredHeld.Length == 0, "held: " + string.Join("; ", coveredHeld));
        var boardHeld = await BoardAsync();

        // Phase 2: released, the measured presentation.
        await Page.EvaluateAsync("() => window.__frames.release()");
        await SettleAsync();
        var fitted = await ActionRowGeometry.FitAsync(Page);
        Assert.False(fitted.Pending);
        Assert.Equal(1, fitted.RowLines);
        var coveredFitted = await ActionRowGeometry.CoveredControlsAsync(Page);
        Assert.True(coveredFitted.Length == 0, "fitted: " + string.Join("; ", coveredFitted));
        // At the floor the measured presentation is the pending one, so the
        // board did not change either: the pending state kept §2's floor.
        Assert.Equal(boardHeld, await BoardAsync());
        // Measured, the panel is folded by itself, which checks the rail (the
        // control says hidden, which is true); the user's own fold is saved
        // behind it (navFold.js), untouched.
        Assert.True(fitted.AutoFolded);
        await Expect(CollapseRail).ToBeCheckedAsync();

        // And at every moment between, as the sampler saw it.
        await Page.EvaluateAsync("() => window.__coverage.stop()");
        var coverage = await CoverageAsync();
        Assert.True(coverage.Samples > 0, "the sampler saw the row");
        Assert.True(coverage.Violations.Length == 0, string.Join(Environment.NewLine, coverage.Violations));
    }

    private ILocator Panel => Page.Locator(".sidebar");

    [Fact]
    public async Task AtAWideWindow_TheFirstFitOpensWhatFits_ForTheRowThePanelWillLeave()
    {
        // 1100 x 800 with the panel showing (it shows from 930 px), on a cube
        // decision from a .xg money session, reading No double / Pass: its
        // full row is the widest there is, and its locator has coordinates.
        // Pending, the layout hides the panel by style, so the row's box is
        // 180 px wider than the row the panel will leave; the first fit
        // decides for the latter, from the panel's declared state, and so
        // opens the panel and the tail but keeps the pills short (the full
        // form needs more than this window leaves beside the panel). A first
        // fit that read the pending box would report the full form, and the
        // row it rendered would run its tail over the pills once the panel's
        // width returned, for a frame the sampler sees.
        await Page.SetViewportSizeAsync(1100, 800);
        await BootHomeAsync();
        await PickSynthesizedFileAsync(SyntheticXgMatch.MoneyStagedFileName, SyntheticXgMatch.MoneySessionBytes());
        await ApplyFilterAsync();
        await Page.EvaluateAsync("() => { window.__frames.hold(); window.__coverage.start(); }");
        await StartQuizAsync();
        await Expect(LivePillCaptions).ToHaveCountAsync(4);

        var held = await ActionRowGeometry.FitAsync(Page);
        Assert.True(held.Pending);
        Assert.Empty(await ActionRowGeometry.CoveredControlsAsync(Page));

        await Page.EvaluateAsync("() => window.__frames.release()");
        await SettleAsync();

        var fitted = await ActionRowGeometry.FitAsync(Page);
        Assert.False(fitted.Pending);
        Assert.False(fitted.AutoFolded);
        Assert.False(fitted.TailFolded);
        Assert.True(fitted.FullCubeRow > fitted.Row, $"the full form needs {fitted.FullCubeRow} of a {fitted.Row} row");
        await Expect(LivePillCaptions.Nth(0)).ToHaveTextAsync("ND");
        Assert.Empty(await ActionRowGeometry.CoveredControlsAsync(Page));
        await Page.EvaluateAsync("() => window.__coverage.stop()");
        var coverage = await CoverageAsync();
        Assert.True(coverage.Violations.Length == 0, string.Join(Environment.NewLine, coverage.Violations));
    }

    /// <summary>The three switches at the current viewport, as the page measured them.</summary>
    private sealed record Switches(bool PanelFolded, bool TailFolded, bool FullForm);

    private async Task<Switches> SwitchesAsync()
    {
        var fit = await ActionRowGeometry.FitAsync(Page);
        return new(fit.AutoFolded, fit.TailFolded, fit.FullCubeRow <= fit.Row);
    }

    /// <summary>
    /// Wider pills than checker rows: under this, the short cube row is the
    /// widest answer row, so a budget that learned it only at the first cube
    /// problem would move there. It reaches the live pills and the ruler's
    /// copies alike, as a font would.
    /// </summary>
    private const string WiderPills = ".bg-cube-action { letter-spacing: 0.35em; }";

    private async Task StartCheckerFirstAsync(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await BootHomeAsync();
        await PickAsync(FirstProblem.XgChecker);
        await StartQuizAsync();
        await ExpectTheFirstProblemAsync(FirstProblem.XgChecker);
        await Page.AddStyleTagAsync(new() { Content = WiderPills });
        await SettleAsync();
        await SettleAsync();
    }

    /// <summary>
    /// Every viewport width in [641, 2000] where one of the three switches
    /// flips, scanned on the page as it stands: the first width of each new
    /// state, to the pixel.
    /// </summary>
    private async Task<List<(string Switch, int Width)>> FlipsAsync(int height)
    {
        async Task<Switches> At(int w)
        {
            await ResizeAsync(w, height);
            return await SwitchesAsync();
        }

        var flips = new List<(string, int)>();
        var last = await At(641);
        for (var w = 649; w <= 2000; w += 8)
        {
            var now = await At(w);
            if (now == last) continue;
            // Refine to the pixel inside (w - 8, w].
            var lo = w - 8;
            var hi = w;
            while (hi - lo > 1)
            {
                var mid = (lo + hi) / 2;
                if (await At(mid) == last) lo = mid; else hi = mid;
            }
            var refined = await At(hi);
            if (refined.PanelFolded != last.PanelFolded) flips.Add(("panel", hi));
            if (refined.TailFolded != last.TailFolded) flips.Add(("tail", hi));
            if (refined.FullForm != last.FullForm) flips.Add(("labels", hi));
            last = await At(w);
        }
        return flips;
    }

    [Fact]
    public async Task ARunThatStartsOnACheckerPlay_KeepsItsBoardAndEverySwitch_WhenItsFirstCubeProblemArrives()
    {
        // Under pills wider than the checker row, so the short cube row is the
        // widest answer row: a budget that learned it only when the first cube
        // problem arrived would move the panel's fold and the "⋯" there, and
        // the board with them, and a labels' form decided per decision would
        // render the first cube short before measuring it. At a width either
        // side of each switch, the first cube problem changes none of it, and
        // its first render already shows the measured form.
        const int height = 800;
        await StartCheckerFirstAsync(2000, height);
        var leads = await Page.EvaluateAsync<double[]>(@"() =>
            [...document.querySelectorAll('.action-row-ruler [data-ruler=lead]')].map(l => l.getBoundingClientRect().width)");
        Assert.True(leads[1] > leads[0], $"the pills, short, are the widest answer row here: {string.Join(", ", leads)}");
        var flips = await FlipsAsync(height);
        Assert.Contains(flips, f => f.Switch == "panel");
        Assert.Contains(flips, f => f.Switch == "tail");
        Assert.Contains(flips, f => f.Switch == "labels");

        foreach (var width in flips.SelectMany(f => new[] { f.Width - 1, f.Width }).Where(w => w >= 641).Distinct().Order())
        {
            await StartCheckerFirstAsync(width, height);
            var before = await SwitchesAsync();
            var boardBefore = await BoardAsync();
            await Page.EvaluateAsync(@"() => {
                window.__firstPills = null;
                const watch = new MutationObserver(() => {
                  const captions = [...document.querySelectorAll('.action-row .bg-cube-actions label')].map(l => l.textContent.trim());
                  if (captions.length > 0 && window.__firstPills === null) { window.__firstPills = captions.join('|'); watch.disconnect(); }
                });
                watch.observe(document.body, { subtree: true, childList: true, characterData: true });
              }");

            await NavButton(ExpectedText.SkipButton).ClickAsync();

            await Expect(LivePillCaptions).ToHaveCountAsync(4);
            await SettleAsync();
            var after = await SwitchesAsync();
            Assert.True(before == after, $"at {width}px: {before} -> {after}");
            Assert.Equal(boardBefore, await BoardAsync());
            var first = await Page.EvaluateAsync<string>("() => window.__firstPills");
            var full = before.FullForm;
            Assert.True(first.Contains("No double") == full,
                $"at {width}px the first render of the first cube problem showed {first}, measured {(full ? "full" : "short")}");
            Assert.Equal(first, string.Join("|", await LivePillCaptions.AllInnerTextsAsync()));
        }
    }

    /// <summary>Two synthesized files, one cube decision in each reading of the fourth answer, each followed by a checker play.</summary>
    private async Task PickBothReadingsAsync()
    {
        await PickSynthesizedFilesAsync("readings",
            (SyntheticXgMatch.StagedFileName, SyntheticXgMatch.Bytes()),
            (SyntheticXgMatch.MoneyStagedFileName, SyntheticXgMatch.MoneySessionBytes()));
        await ApplyFilterAsync();
    }

    [Fact]
    public async Task ACubeProblemsFirstRender_ShowsTheFormTheWindowMeasured_InTheOtherReadingToo()
    {
        // The labels' form is the window's: at a width that holds the full
        // form, the next cube problem — the other reading of the fourth
        // answer, a checker play between — renders full from its first render.
        await Page.SetViewportSizeAsync(1600, 900);
        await BootHomeAsync();
        await PickBothReadingsAsync();
        await StartQuizAsync();
        await Expect(LivePillCaptions).ToHaveCountAsync(4);
        await SettleAsync();
        await Expect(LivePillCaptions.Nth(0)).ToHaveTextAsync(ExpectedText.NoDoublePill);
        var firstFourth = await LivePillCaptions.Nth(3).InnerTextAsync();

        await NavButton(ExpectedText.SkipButton).ClickAsync();
        await Expect(Page.Locator(".bg-play-entry")).ToBeVisibleAsync();
        await Page.EvaluateAsync(@"() => {
            window.__firstPills = null;
            const watch = new MutationObserver(() => {
              const captions = [...document.querySelectorAll('.action-row .bg-cube-actions label')].map(l => l.textContent.trim());
              if (captions.length > 0 && window.__firstPills === null) { window.__firstPills = captions.join('|'); watch.disconnect(); }
            });
            watch.observe(document.body, { subtree: true, childList: true, characterData: true });
          }");
        await NavButton(ExpectedText.SkipButton).ClickAsync();
        await Expect(LivePillCaptions).ToHaveCountAsync(4);

        var first = await Page.EvaluateAsync<string>("() => window.__firstPills");
        var otherFourth = firstFourth == ExpectedText.TooGoodPill ? ExpectedText.NoDoublePassPill : ExpectedText.TooGoodPill;
        Assert.Equal(
            string.Join("|", ExpectedText.NoDoublePill, ExpectedText.DoubleTakePill, ExpectedText.DoublePassPill, otherFourth),
            first);
    }

    /// <summary>One live or copied pill row, by content: its captions, which pill is selected, its width and height, and a pill's height.</summary>
    private sealed record PillRow(string Captions, int Selected, double Width, double Height, double PillHeight);

    private const string ReadPillRow = @"r => {
        const pills = [...r.querySelectorAll('.bg-cube-action')];
        return JSON.stringify({
          Captions: pills.map(p => p.textContent.trim()).join('|'),
          Selected: pills.findIndex(p => p.classList.contains('bg-cube-action-selected')),
          Width: r.getBoundingClientRect().width,
          Height: r.getBoundingClientRect().height,
          PillHeight: pills[0].getBoundingClientRect().height });
      }";

    private async Task<PillRow> LiveRowAsync() =>
        JsonSerializer.Deserialize<PillRow>(
            await Page.Locator(".action-row .bg-cube-actions").EvaluateAsync<string>(ReadPillRow))!;

    private async Task<List<PillRow>> CopiesAsync(string form) =>
        [.. (await Page.Locator($".action-row-ruler [data-ruler-pills={form}] > .bg-cube-actions").EvaluateAllAsync<string[]>(
            $"rows => rows.map({ReadPillRow})"))
            .Select(json => JsonSerializer.Deserialize<PillRow>(json)!)];

    [Fact]
    public async Task TheRulersCopies_MeasureTheLiveRow_EveryStateInBothForms()
    {
        // The producer's inert copy stands in for the live row the budget
        // cannot see yet. In this page, under its fonts: the live row at a
        // gammons-possible and a gammons-not-possible decision, in each form,
        // with nothing selected and each answer selected in turn, matched to
        // the copy showing the same captions and the same selected pill (by
        // content, never by position) — the same width, each one line high;
        // and each ruler root exactly as wide as the widest live row in its
        // form.
        await Page.SetViewportSizeAsync(1600, 900);
        await BootHomeAsync();
        await PickBothReadingsAsync();
        await StartQuizAsync();
        await Expect(LivePillCaptions).ToHaveCountAsync(4);

        var widest = new Dictionary<string, double> { ["full"] = 0, ["short"] = 0 };
        var seen = new HashSet<string>();
        foreach (var (form, width, height) in new[] { ("full", 1600, 900), ("short", 800, 768) })
        {
            await ResizeAsync(width, height);
            var copies = await CopiesAsync(form);
            Assert.Equal(10, copies.Count);
            foreach (var cube in new[] { 1, 2 })
            {
                // Land on the cube problem afresh, which leaves nothing
                // selected: the first pass walks forward over the checker play
                // between; the second goes to the first and the last.
                if (form == "full" && cube == 2)
                {
                    await NavButton(ExpectedText.SkipButton).ClickAsync();
                    await Expect(Page.Locator(".bg-play-entry")).ToBeVisibleAsync();
                    await NavButton(ExpectedText.SkipButton).ClickAsync();
                }
                else if (form == "short")
                {
                    await NavButton(cube == 1 ? ExpectedText.GoToFirstButton : ExpectedText.GoToLastButton).ClickAsync();
                }
                await Expect(LivePillCaptions).ToHaveCountAsync(4);
                await Expect(LivePillCaptions.Nth(0)).ToHaveTextAsync(form == "full" ? ExpectedText.NoDoublePill : "ND");
                await SettleAsync();
                var fourth = await LivePillCaptions.Nth(3).InnerTextAsync();
                var names = new[] { ExpectedText.NoDoublePill, ExpectedText.DoubleTakePill, ExpectedText.DoublePassPill,
                    (fourth is "TG" or "Too good") ? ExpectedText.TooGoodPill : ExpectedText.NoDoublePassPill };

                foreach (var selection in new string?[] { null }.Concat(names))
                {
                    if (selection is not null)
                    {
                        await CubePill(selection).CheckAsync();
                        await Expect(CubePill(selection)).ToBeCheckedAsync();
                    }
                    var live = await LiveRowAsync();
                    Assert.Equal(live.PillHeight, live.Height, 0.5);
                    var match = Assert.Single(copies, c => c.Captions == live.Captions && c.Selected == live.Selected);
                    Assert.Equal(live.Width, match.Width, 2);
                    Assert.Equal(match.PillHeight, match.Height, 0.5);
                    widest[form] = Math.Max(widest[form], live.Width);
                    seen.Add($"{form}:{live.Captions}:{live.Selected}");
                }
            }
        }

        Assert.Equal(20, seen.Count);   // 2 readings x 5 selections, in each form
        foreach (var form in new[] { "full", "short" })
        {
            var root = await Page.Locator($".action-row-ruler [data-ruler-pills={form}]")
                .EvaluateAsync<double>("e => e.getBoundingClientRect().width");
            Assert.Equal(widest[form], root, 2);
        }
    }

    [Fact]
    public async Task WhereTheReviewRowIsTheWidest_TheBudgetAndTheSwitchesFollowIt()
    {
        // Review is a state, and the budget holds every state's row: the
        // review row (Continue, the four, Notes) is a ruler line like the two
        // answer rows. Widen Notes in this test only — on a checker play being
        // answered, so neither a review nor a Notes control is on screen — and
        // the review row becomes the widest lead; the budget is then that
        // row, the gap and the tail's floor, and the panel's fold and the
        // "⋯" move with it at the same viewport.
        await Page.SetViewportSizeAsync(1000, 800);
        await BootHomeAsync();
        await PickAsync(FirstProblem.XgChecker);
        await StartQuizAsync();
        await ExpectTheFirstProblemAsync(FirstProblem.XgChecker);
        await SettleAsync();
        var before = await ActionRowGeometry.FitAsync(Page);
        var foldBefore = await ActionRowGeometry.FoldWidthAsync(Page);
        Assert.False(before.AutoFolded);
        Assert.Equal(0, await Page.Locator(".action-row .decision-notes-toggle").CountAsync());

        await Page.AddStyleTagAsync(new() { Content = ".decision-notes-toggle { padding-inline: 10rem; }" });
        await SettleAsync();
        await SettleAsync();

        var leads = await Page.EvaluateAsync<double[]>(@"() =>
            [...document.querySelectorAll('.action-row-ruler [data-ruler=lead]')].map(l => l.getBoundingClientRect().width)");
        var review = await Page.Locator(".action-row-ruler [data-ruler-review]")
            .EvaluateAsync<double>("e => e.getBoundingClientRect().width");
        Assert.Equal(leads.Max(), review);
        Assert.True(review > leads[0], $"the review row {review} is the widest, past the checker row {leads[0]}");
        var after = await ActionRowGeometry.FitAsync(Page);
        var floor = await Page.Locator(".action-row-ruler [data-ruler=tail]").EvaluateAsync<double>("e => e.getBoundingClientRect().width");
        Assert.Equal(review + after.Gap + floor, after.Budget, 0.5);
        Assert.True(after.Budget > before.Budget);
        Assert.True(after.AutoFolded, "the panel folds for the wider review row, at the same viewport");
        Assert.True(await ActionRowGeometry.FoldWidthAsync(Page) > foldBefore + 100);

        // And the "⋯": below the review row's own switch, the tail folds.
        await ResizeAsync((int)Math.Floor(await ActionRowGeometry.TailFoldWidthAsync(Page)) - 1, 800);
        Assert.True((await ActionRowGeometry.FitAsync(Page)).TailFolded);
        Assert.Empty(await ActionRowGeometry.CoveredControlsAsync(Page));
    }

    [Fact]
    public async Task TheRulersNotes_MeasuresTheLiveControl_ClosedAndOpen()
    {
        // At review on a decision with a comment, the live Notes control and
        // the ruler's copy are the same width, closed and open.
        await Page.SetViewportSizeAsync(1280, 800);
        await BootHomeAsync();
        await PickAsync(FirstProblem.XgCube);
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        var live = Page.Locator(".action-row .decision-notes-toggle");
        await Expect(live).ToHaveCountAsync(1);
        var copy = await Page.Locator(".action-row-ruler [data-ruler-review] .decision-notes-toggle")
            .EvaluateAsync<double>("e => e.getBoundingClientRect().width");

        Assert.Equal(copy, await live.EvaluateAsync<double>("e => e.getBoundingClientRect().width"), 2);
        await live.ClickAsync();
        await Expect(live).ToHaveAttributeAsync("aria-expanded", "true");
        Assert.Equal(copy, await live.EvaluateAsync<double>("e => e.getBoundingClientRect().width"), 2);
    }

    [Fact]
    public async Task TheRulersCopiesCarryTheLivePillsClasses_AndTheLiveSelectorsReachOnlyTheLiveRow()
    {
        // The copies are the same markup as the live pills, classes included,
        // so a selector keyed on the classes alone reaches twenty rows and
        // eighty pills more than the one live row. Every live-pill query this
        // suite and the app make is scoped to .action-row, which holds the
        // live row only: these are the numbers that would tell otherwise.
        await Page.SetViewportSizeAsync(1280, 800);
        await BootHomeAsync();
        await PickAsync(FirstProblem.XgCube);
        await StartQuizAsync();
        await Expect(LivePillCaptions).ToHaveCountAsync(4);

        Assert.Equal(21, await Page.Locator(".bg-cube-actions").CountAsync());
        Assert.Equal(84, await Page.Locator(".bg-cube-action").CountAsync());
        Assert.Equal(1, await Page.Locator(".action-row .bg-cube-actions").CountAsync());
        Assert.Equal(4, await Page.Locator(".action-row .bg-cube-action").CountAsync());
        Assert.Equal(4, await Page.GetByRole(AriaRole.Radio).CountAsync());
        Assert.Equal(0, await Page.Locator(".action-row .bg-cube-actions-ruler").CountAsync());
        // The live row's controls: four pills, Submit, the four, and the tail's
        // copy button, Show stats and End quiz.
        Assert.Equal(4 + 1 + 4 + 3, await ActionRowGeometry.RowControlCountAsync(Page));
    }
}
