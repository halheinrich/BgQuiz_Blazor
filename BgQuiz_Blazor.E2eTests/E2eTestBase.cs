using System.Diagnostics;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Shared plumbing for every e2e scenario: one fresh, isolated
/// <see cref="IBrowserContext"/> + <see cref="IPage"/> per test, and the
/// domain-verbed flow helpers (boot, pick, apply, start, click-a-point) that
/// keep each scenario reading as a user script with the selector knowledge
/// defined exactly once.
///
/// <para>
/// Waiting policy: Playwright auto-wait and explicit <c>Expect</c> assertions
/// only — no sleeps. Every helper that triggers an async app transition ends by
/// awaiting the user-visible consequence of that transition, so callers can
/// chain steps without timing knowledge.
/// </para>
/// </summary>
[Collection(E2eCollection.Name)]
public abstract class E2eTestBase : IAsyncLifetime
{
    /// <summary>
    /// Committed cube-decision fixture — one problem, best action "No Double"
    /// and best taker response "Take", i.e. a best <i>pair</i> of No Double /
    /// Take. The taker half matters to the answer-type breakdown suite, which
    /// reads the bucket a whole cube decision lands in.
    /// </summary>
    protected const string CubeFixture = "BothAnalysis.xgp";

    /// <summary>
    /// Committed checker-play fixture — one problem, a 6-5 roll whose best play
    /// is 24/13 (sub-moves 24/18 then 18/13).
    /// </summary>
    protected const string CheckerFixture = "Opening 32 65 64 31 65.xgp";

    /// <summary>
    /// Committed <b>forced-play</b> checker fixture — one problem, both
    /// checkers on the bar against a board where each die has exactly one open
    /// entry point, so <c>bar/24* bar/20</c> is the only legal play. The whole
    /// point of it is that the quiz must never show it
    /// (<c>halheinrich/backgammon#140</c>); a scenario pairs it with a real
    /// decision and asserts which one the user is asked.
    ///
    /// <para>
    /// Sliced out of the umbrella's own <c>Make20Pt.xg</c> with
    /// <c>XgpExporter</c>, analysis carried through, so it is a record XG could
    /// have written and the parser has nothing special to do with it. Forced
    /// positions are not exotic: about one checker decision in eleven across
    /// the umbrella's corpus is forced or a pass.
    /// </para>
    /// </summary>
    protected const string ForcedFixture = "ForcedPlay.xgp";

    /// <summary>
    /// The committed cube fixtures, each a <b>different position</b> — the pool a
    /// multi-problem scenario is staged from (see
    /// <see cref="PickCubeProblemsAsync"/>). Order here is the order they are
    /// staged in, but no scenario may depend on it: what makes them
    /// interchangeable is that every one of them is a cube decision, not where
    /// they sit.
    ///
    /// <para>
    /// Distinctness is the load-bearing property and it is scarcer than it
    /// looks: the umbrella's <c>DoubleAnalysis.xgp</c> / <c>TakeAnalysis.xgp</c>
    /// are the <i>same</i> position as <see cref="CubeFixture"/> with different
    /// analysis sections, so they are useless here. These three are the distinct
    /// cube positions available; a scenario needing a fourth problem must commit
    /// another genuinely different one.
    /// </para>
    /// </summary>
    private static readonly string[] CubeFixtures =
    [
        CubeFixture,                 // No Double / Take
        "TooGoodAndTake.xgp",        // a different board, also No Double / Take
        "match35253054_2_37.xgp",    // a different board, Double / Pass
    ];

    private readonly PublishedAppFixture _app;
    private readonly PlaywrightFixture _playwright;
    private readonly List<string> _stagedDirs = [];
    private IBrowserContext? _context;

    protected E2eTestBase(PublishedAppFixture app, PlaywrightFixture playwright)
    {
        _app = app;
        _playwright = playwright;
    }

    /// <summary>The page every scenario drives; fresh per test.</summary>
    protected IPage Page { get; private set; } = null!;

    protected string BaseUrl => _app.BaseUrl;

    /// <summary>
    /// Options for the per-test browser context. The base is Playwright's default
    /// (the host machine's locale). This is the single seam through which context
    /// construction is customized, so <see cref="InitializeAsync"/> stays the one
    /// place a context is built — a scenario that must pin a browser locale (e.g.
    /// a comma-decimal culture such as <c>nb-NO</c>) overrides this rather than
    /// building its own context out from under the shared lifecycle.
    /// </summary>
    protected virtual BrowserNewContextOptions ContextOptions => new();

    /// <summary>
    /// JavaScript injected into the context before any page exists (so it runs
    /// ahead of app boot on every navigation). The second customization seam,
    /// parallel to <see cref="ContextOptions"/>. Its one production consumer is
    /// the stats-persistence suite's fake <c>window.showDirectoryPicker</c>:
    /// Playwright cannot drive the native File System Access prompts, so the
    /// FS-Access path is exercised by faking the <i>browser API</i> — never the
    /// app, which ships no test seams — and letting the app's real JS module
    /// run against the fake handles.
    /// </summary>
    protected virtual string? ContextInitScript => null;

    public async Task InitializeAsync()
    {
        _context = await _playwright.Browser.NewContextAsync(ContextOptions);
        _context.SetDefaultTimeout(PlaywrightFixture.DefaultTimeoutMs);
        if (ContextInitScript is { } script)
        {
            // Must precede NewPageAsync: init scripts registered on the context
            // apply only to pages created afterwards.
            await _context.AddInitScriptAsync(script);
        }
        Page = await _context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        if (_context is not null) await _context.DisposeAsync();
        foreach (var dir in _stagedDirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { /* best-effort cleanup of temp staging */ }
        }
    }

    // -----------------------------------------------------------------------
    //  Shared locators — selector knowledge lives here, once
    // -----------------------------------------------------------------------

    /// <summary>
    /// Home's "Choose folder…" button. Doubles as the WASM boot marker: every
    /// routable page renders with <c>prerender: false</c>, so any page content
    /// existing at all proves the runtime is up.
    /// </summary>
    protected ILocator PickFolderButton => Page.Locator("#pickProblemFolder");

    /// <summary>
    /// Home's hidden <c>webkitdirectory</c> fallback input — always in the DOM,
    /// so Playwright can hand it a staged directory directly (the fallback
    /// mechanism's own pick path; no native dialog involved).
    /// </summary>
    protected ILocator FallbackFolderInput => Page.Locator("#problemFolderFallback");

    protected ILocator StartButton => Page.GetByRole(AriaRole.Button, new() { Name = "Start Quiz" });

    protected ILocator SubmitButton => Page.GetByRole(AriaRole.Button, new() { Name = "Submit" });

    /// <summary>The quiz page's fixed-height verdict band (answering prompt / scored verdict).</summary>
    protected ILocator VerdictBand => Page.Locator(".status-verdict");

    /// <summary>Home's one-shot "your quiz was reset by the reload" notice.</summary>
    protected ILocator ReloadNotice =>
        Page.GetByText("Your previous quiz was reset by the page reload");

    /// <summary>
    /// The board diagram's transparent hit-region overlay — the absolutely
    /// positioned <c>&lt;svg&gt;</c> that carries the <c>viewBox</c> and one
    /// <c>&lt;rect&gt;</c> per clickable region. This is the wire surface the
    /// culture-invariance guard inspects (its geometry attributes must never
    /// carry comma decimals).
    /// </summary>
    protected ILocator HitOverlaySvg => Page.Locator(".board-container .bg-diagram > svg");

    /// <summary>
    /// Every hit-region <c>&lt;rect&gt;</c> in render order — points 1..24 first
    /// (see <see cref="ClickBoardPointAsync"/> for the positional contract),
    /// followed by bar/cube/tray/dice.
    /// </summary>
    protected ILocator HitRects => Page.Locator(".board-container .bg-diagram > svg > rect");

    /// <summary>
    /// The bar's hit <c>&lt;rect&gt;</c>. The producer always emits the bar rect
    /// immediately after the 24 point rects, so index 24 (0-based) addresses it —
    /// the same render-order contract <see cref="ClickBoardPointAsync"/> relies on.
    /// The bar is the guaranteed-fractional region (viewBox-space width <c>30.8</c>)
    /// and the exact production repro: a comma-decimal locale once formatted that
    /// width as <c>"30,8"</c>, collapsing the rect to a zero-size non-target.
    /// </summary>
    protected ILocator BarHitRect => HitRects.Nth(24);

    // -----------------------------------------------------------------------
    //  Flow helpers
    // -----------------------------------------------------------------------

    /// <summary>Navigate to Home and wait for the WASM runtime to boot.</summary>
    protected async Task BootHomeAsync()
    {
        await Page.GotoAsync(BaseUrl + "/");
        await Expect(PickFolderButton).ToBeVisibleAsync();
    }

    /// <summary>
    /// Pick a committed fixture as a single-problem <i>folder</i> (staged and
    /// handed to the fallback input by <see cref="StageAndPickAsync"/>). These
    /// scenarios run as no-stats quizzes by construction (the fallback mechanism
    /// has no writable handle); the FS-Access + stats path is covered by
    /// <c>StatsPersistenceTests</c>.
    /// </summary>
    protected Task PickFixtureAsync(string fixtureFileName) =>
        StageAndPickAsync(
            Path.GetFileNameWithoutExtension(fixtureFileName),
            [(fixtureFileName, FixtureBytes(fixtureFileName))]);

    /// <summary>
    /// <see cref="PickFixtureAsync"/> with the staged file <b>renamed</b> —
    /// same committed bytes, a different name on disk. For scenarios whose
    /// subject is the name itself rather than the position: a folder of real
    /// eXtreme Gammon exports holds names far longer than this suite's
    /// fixtures, and the quiz page's own layout contract turns on that length
    /// (<c>SPEC-quiz-view.md</c> §4). Committing a second copy of the same
    /// position under a longer name would be the same staging with a
    /// maintenance cost attached.
    /// </summary>
    /// <param name="fixtureFileName">The committed fixture supplying the bytes.</param>
    /// <param name="stagedFileName">
    /// The name to stage it under, extension included — it must be the same
    /// extension, since that is what decides how the app parses the file.
    /// </param>
    protected Task PickFixtureUnderNameAsync(string fixtureFileName, string stagedFileName)
    {
        if (!string.Equals(
                Path.GetExtension(fixtureFileName),
                Path.GetExtension(stagedFileName),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Staged name '{stagedFileName}' must keep '{fixtureFileName}'s extension — the " +
                "extension is what selects the parse path, so changing it stages a different test.",
                nameof(stagedFileName));
        }

        return StageAndPickAsync(
            Path.GetFileNameWithoutExtension(stagedFileName),
            [(stagedFileName, FixtureBytes(fixtureFileName))]);
    }

    /// <summary>
    /// The multi-problem form of <see cref="PickFixtureAsync"/>: stage
    /// <paramref name="problems"/> of the committed <see cref="CubeFixtures"/>,
    /// so the quiz has several problems to walk through.
    ///
    /// <para>
    /// <b>Distinct fixtures, not copies of one</b> — and that is not a style
    /// choice. This helper used to stage N copies of a single fixture, because a
    /// uniform pool lets a scenario walk the run without knowing which problem
    /// the source hands it first. Since <c>halheinrich/backgammon#84</c> the app
    /// guarantees a quiz never serves the same <i>position</i> twice, so copies
    /// of one file collapse to a single problem: that trick manufactures nothing
    /// any more. Distinct cube fixtures restore what the trick was buying by a
    /// route the app agrees with — every problem is still a cube decision, so
    /// still answerable by the same gesture in any order.
    /// </para>
    /// </summary>
    /// <param name="problems">How many problems the run needs; at most <see cref="CubeFixtures"/>' length.</param>
    protected Task PickCubeProblemsAsync(int problems)
    {
        if (problems < 1 || problems > CubeFixtures.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(problems), problems,
                $"Only {CubeFixtures.Length} distinct cube positions are committed, and a run cannot " +
                "be padded with copies (the app dedupes positions — see this helper's remarks). " +
                "Commit another genuinely different cube fixture to go higher.");
        }

        var staged = CubeFixtures
            .Take(problems)
            .Select(name => (DestName: name, Bytes: FixtureBytes(name)))
            .ToList();

        return StageAndPickAsync("cubes", staged);
    }

    /// <summary>
    /// Pick a folder holding <b>one copy of each</b> named fixture — the
    /// heterogeneous form of <see cref="PickCubeProblemsAsync"/>, for scenarios
    /// about what a folder is <i>made of</i> rather than what walking it does.
    /// Walking scenarios keep to a pool of one kind so they stay independent of
    /// the source's ordering; a scenario reading the answer-type breakdown is
    /// independent of ordering by construction (a distribution has no order), so
    /// a genuinely mixed folder is exactly what it needs.
    /// </summary>
    protected Task PickFixturesAsync(params string[] fixtureFileNames)
    {
        var staged = fixtureFileNames
            .Select(name => (DestName: name, Bytes: FixtureBytes(name)))
            .ToList();

        return StageAndPickAsync("mixed", staged);
    }

    /// <summary>
    /// Pick a single-problem folder whose one file is <b>synthesized in this
    /// run</b> rather than committed — same staging, same real fallback upload,
    /// content that exists only in memory until it is written.
    ///
    /// <para>
    /// It exists for the one fixture this suite cannot commit or fake by
    /// renaming: a real <c>.xg</c> match. Every committed fixture is an
    /// <c>.xgp</c>, and the two branches of the locator's ruling turn on which
    /// of the two a decision came from (<c>SPEC-quiz-view.md</c> §4 ruling
    /// (ii)), so the <c>.xg</c> branch had no fixture at all. Real <c>.xg</c>
    /// exports carry real players' names and cannot enter a public repository;
    /// <see cref="SyntheticXgMatch"/> builds one instead, with names that are
    /// fake by construction.
    /// </para>
    /// </summary>
    /// <param name="stagedFileName">The name to stage the bytes under, extension included.</param>
    /// <param name="bytes">The file's content.</param>
    protected Task PickSynthesizedFileAsync(string stagedFileName, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
            throw new ArgumentException(
                "A synthesized fixture must have content — staging an empty file would put an " +
                "unparseable folder under test and say nothing about the scenario.",
                nameof(bytes));

        return StageAndPickAsync(
            Path.GetFileNameWithoutExtension(stagedFileName), [(stagedFileName, bytes)]);
    }

    /// <summary>
    /// Pick a folder holding <paramref name="copies"/> copies of one fixture —
    /// the original plus numbered duplicates, the shape a re-downloaded match
    /// takes in a real problem folder. Every copy is content-identical, so the
    /// pool collapses to a single position and the pick summary's file count and
    /// the match count deliberately disagree: the scenario umbrella issue #104
    /// exists for.
    /// </summary>
    /// <param name="fixtureFileName">The committed fixture to duplicate.</param>
    /// <param name="copies">
    /// How many copies to stage. At least two — one copy collapses nothing, so a
    /// scenario asking for it is asking for something this helper cannot set up.
    /// </param>
    protected Task PickDuplicatedFixtureAsync(string fixtureFileName, int copies)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(copies, 2);

        string stem = Path.GetFileNameWithoutExtension(fixtureFileName);
        string extension = Path.GetExtension(fixtureFileName);
        byte[] bytes = FixtureBytes(fixtureFileName);
        var staged = Enumerable.Range(0, copies)
            .Select(i => (
                DestName: i == 0 ? fixtureFileName : $"{stem} ({i}){extension}",
                Bytes: bytes))
            .ToList();

        return StageAndPickAsync("duplicates", staged);
    }

    /// <summary>
    /// Copy the given files into a fresh staged temp directory (named after the
    /// scenario, so runs stay distinguishable in failure output) and hand that
    /// directory to the hidden <c>webkitdirectory</c> input — a genuine
    /// directory upload, driving the app's real fallback collection path
    /// (top-level filter, buffering, holder). Waits for the holder-derived folder
    /// summary, which also proves the pick round-trip completed.
    ///
    /// <para>
    /// A staged file is a <b>name and its content</b>, not a path to copy. That
    /// is what the browser is handed either way, and modelling it that way is
    /// what lets a fixture synthesized in memory
    /// (<see cref="PickSynthesizedFileAsync"/>) and a committed one
    /// (<see cref="FixtureBytes"/>) reach the app through this one stager
    /// instead of two.
    /// </para>
    /// </summary>
    private async Task StageAndPickAsync(
        string dirName, IReadOnlyList<(string DestName, byte[] Bytes)> files)
    {
        string stagedDir = Path.Combine(
            Path.GetTempPath(), "bgquiz-e2e", $"{dirName}-{Guid.NewGuid():N}", dirName);
        Directory.CreateDirectory(stagedDir);
        _stagedDirs.Add(Path.GetDirectoryName(stagedDir)!);

        foreach (var (destName, bytes) in files)
            File.WriteAllBytes(Path.Combine(stagedDir, destName), bytes);

        await FallbackFolderInput.SetInputFilesAsync(stagedDir);
        await Expect(Page.GetByText(files.Count == 1 ? "1 problem file" : $"{files.Count} problem files"))
            .ToBeVisibleAsync();
    }

    /// <summary>
    /// Open the filter panel's "more filters" disclosure and wait for it to
    /// land. The panel keeps its error-range section first and always visible;
    /// its other eight sections (player names, decision type, match scores,
    /// move number range, contact type, analysis depth, dice rolls, position
    /// pattern) render only while expanded — absent from the DOM when
    /// collapsed, not merely hidden — so any scenario setting one of those
    /// facets must expand first. Error-range edits, Apply, and Clear filters
    /// need no expansion.
    ///
    /// <para>
    /// The toggle's two labels are pinned here as literals, per this suite's
    /// independent-literal convention: they are what the user reads on the
    /// control, and the flip from one to the other is the user-visible proof
    /// the disclosure opened.
    /// </para>
    /// </summary>
    protected async Task ExpandMoreFiltersAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Show more filters" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Hide more filters" }))
            .ToBeVisibleAsync();
    }

    /// <summary>
    /// Apply the filter panel as-is and wait for the applied state to land
    /// (the "apply filters to enable Start" hint disappears).
    /// </summary>
    protected async Task ApplyFilterAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Apply Filter" }).ClickAsync();
        await Expect(Page.GetByText("Apply the filters above to enable Start")).ToHaveCountAsync(0);
    }

    /// <summary>Click Start Quiz and wait for the quiz page.</summary>
    protected async Task StartQuizAsync()
    {
        await Expect(StartButton).ToBeEnabledAsync();
        await StartButton.ClickAsync();
        await ExpectUrlAsync("/quiz");
    }

    /// <summary>
    /// Add one row to the weighted-mix builder. The fresh row takes the first
    /// unused category and the rows rebalance to an even 100% split, so on the
    /// empty builder this helper requires it is a complete, valid 100%
    /// "Never seen" mix.
    /// </summary>
    protected async Task AddDefaultMixRowAsync()
    {
        await Page.Locator("#mixAddRow").ClickAsync();
        await Expect(Page.Locator(".mix-row")).ToHaveCountAsync(1);
    }

    /// <summary>
    /// Put the on-screen mix in effect by checking <b>"Mix applies"</b> — the
    /// sole activation control — and wait for the checked state to land.
    /// Playwright's CheckAsync auto-waits for the box to be enabled, so this
    /// also implicitly waits out the activation gate (a filter in effect).
    /// </summary>
    protected async Task ActivateMixAsync()
    {
        await Page.Locator("#mixApplies").CheckAsync();
        await Expect(Page.Locator("#mixApplies")).ToBeCheckedAsync();
    }

    /// <summary>
    /// Click one of the board's numbered points (1–24) through the diagram's
    /// transparent hit-region overlay — a real user click on the SVG, driving
    /// BgDiag_Razor's one-click play entry.
    ///
    /// <para>
    /// Region identity is positional: the producer renders one <c>&lt;rect&gt;</c>
    /// per region into the overlay, points 1–24 first (in point order — it
    /// builds and enumerates the region dictionary 1..24) followed by
    /// bar/cube/tray/dice, so index <c>point - 1</c> addresses the point's rect.
    /// The rects carry no identifying attributes, so this render-order contract
    /// is the only test-side handle; if it ever changes, clicks land on the
    /// wrong regions, the play never assembles, and the scenario fails loudly at
    /// its Submit-enabled gate — it cannot silently pass. (A producer-side
    /// <c>data-point</c> attribute would make this contractual; that is a
    /// BgDiag_Razor arc, deliberately not patched from here.)
    /// </para>
    /// </summary>
    protected Task ClickBoardPointAsync(int point) =>
        HitRects.Nth(point - 1).ClickAsync();

    /// <summary>
    /// Answer the current cube problem as "No double" and submit, landing in the
    /// review state (Continue visible). The cube fixture's best action is
    /// No Double, so this is the correct answer.
    /// </summary>
    protected async Task AnswerCubeNoDoubleAsync()
    {
        await Page.GetByRole(AriaRole.Radio, new() { Name = "No double" }).CheckAsync();
        await Expect(SubmitButton).ToBeEnabledAsync();
        await SubmitButton.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Continue" })).ToBeVisibleAsync();
    }

    /// <summary>Continue past the review of the (only) problem and land on Done.</summary>
    protected async Task ContinueToDoneAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await ExpectUrlAsync("/done");
    }

    /// <summary>
    /// Wait for an in-app navigation to land on <paramref name="path"/>.
    /// Deliberately a polling URL assertion, not <c>WaitForURLAsync</c>: Blazor
    /// navigates by <c>pushState</c> (a same-document navigation), and the
    /// navigation-event wait can lose the race when the push lands between the
    /// triggering click and the wait's registration — observed as a rare
    /// timeout with the app already sitting on the target URL.
    /// </summary>
    protected Task ExpectUrlAsync(string path) =>
        Expect(Page).ToHaveURLAsync(BaseUrl + path);

    // -----------------------------------------------------------------------
    //  Retrying measurement — the form the smoke gate owes its geometry pins
    // -----------------------------------------------------------------------

    /// <summary>How long <see cref="ExpectToPassAsync"/> keeps retrying.</summary>
    private static readonly TimeSpan RetryWindow =
        TimeSpan.FromMilliseconds(PlaywrightFixture.DefaultTimeoutMs);

    /// <summary>
    /// Gap between attempts inside <see cref="ExpectToPassAsync"/> — Playwright's
    /// own polling interval, so a retried assertion costs what one of its
    /// <c>Expect</c>s costs.
    /// </summary>
    private static readonly TimeSpan RetryPollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Run <paramref name="assertion"/> until it stops throwing, or
    /// <see cref="RetryWindow"/> elapses — at which point the last attempt's
    /// exception propagates, so the failure message is the assertion's own.
    ///
    /// <para>
    /// <b>Why this exists.</b> A single read taken straight after an action is a
    /// timing assertion in disguise (<c>halheinrich/backgammon#126</c>): it
    /// passes or fails on how fast the runner happened to be, and umbrella CI is
    /// slower than any developer machine. Playwright's <c>Expect</c> family
    /// solves that for claims about ONE element, but a claim relating TWO boxes —
    /// this suite's geometry pins — has no such assertion, and the .NET binding
    /// has no <c>ToPass</c> to wrap one in. This is that missing form, and it is
    /// deliberately the only retry primitive here: the claim stays written once,
    /// in C#, as an ordinary xunit assertion.
    /// </para>
    ///
    /// <para>
    /// <b>And why the delay below is not the sleep the suite forbids.</b> The
    /// determinism rule bans waiting out a transition blindly — a sleep chosen to
    /// be "long enough", which pins the host's speed exactly as the one-shot read
    /// does. This delay waits out nothing: the assertion is re-evaluated
    /// immediately and the loop ends the moment it holds, so a fast machine pays
    /// nothing and a slow one is simply given the time it needs. It is the same
    /// interval, for the same reason, that Playwright's own assertions poll on.
    /// </para>
    /// </summary>
    protected static async Task ExpectToPassAsync(Func<Task> assertion)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                await assertion();
                return;
            }
            catch (Exception) when (elapsed.Elapsed < RetryWindow)
            {
                await Task.Delay(RetryPollInterval);
            }
        }
    }

    /// <summary>
    /// The element's box, once it is on the page with a real size — the guard a
    /// geometry pin needs before it can mean anything.
    ///
    /// <para>
    /// <b>The vacuity it closes.</b> Every "A sits below B" pin in this suite is
    /// arithmetic over two boxes, and a box that is absent or zero-sized makes
    /// that arithmetic trivially true: <c>a.Y >= b.Y + 0</c> holds for a board
    /// that failed to render at all. So the yardstick is checked for being a
    /// yardstick first, and the failure names which element was degenerate rather
    /// than reporting a comparison nobody can read.
    /// </para>
    /// </summary>
    /// <param name="locator">The element to measure.</param>
    /// <param name="what">How the failure message should name it.</param>
    protected static async Task<LocatorBoundingBoxResult> LaidOutBoxAsync(
        ILocator locator, string what)
    {
        var box = await locator.BoundingBoxAsync();
        Assert.True(box is not null, $"{what} is not laid out at all — it has no box.");
        Assert.True(
            box!.Width > 0 && box.Height > 0,
            $"{what} has a degenerate box ({box.Width}x{box.Height}); any geometry "
            + "compared against it would pass for that reason alone.");
        return box;
    }

    /// <summary>Absolute path of a committed fixture in the test output; fails loudly when absent.</summary>
    protected static string FixturePath(string fixtureFileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureFileName);
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"Committed fixture '{fixtureFileName}' is missing from the test output " +
                $"(expected at '{path}'). The suite fails rather than skips — check the " +
                "Fixtures/ content items in BgQuiz_Blazor.E2eTests.csproj.");
        return path;
    }

    /// <summary>Content of a committed fixture, read from the test output.</summary>
    protected static byte[] FixtureBytes(string fixtureFileName) =>
        File.ReadAllBytes(FixturePath(fixtureFileName));
}
