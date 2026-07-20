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
    /// <summary>Committed cube-decision fixture — one problem, best action "No Double".</summary>
    protected const string CubeFixture = "BothAnalysis.xgp";

    /// <summary>
    /// Committed checker-play fixture — one problem, a 6-5 roll whose best play
    /// is 24/13 (sub-moves 24/18 then 18/13).
    /// </summary>
    protected const string CheckerFixture = "Opening 32 65 64 31 65.xgp";

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
    /// Pick a committed fixture as a <i>folder</i> through the hidden
    /// <c>webkitdirectory</c> input: the fixture is copied into a fresh staged
    /// temp directory (named after the fixture, so scenarios stay
    /// distinguishable in failure output) and the directory is handed to the
    /// input — a genuine directory upload, driving the app's real fallback
    /// collection path (top-level filter, buffering, holder). Waits for the
    /// holder-derived folder summary, which also proves the pick round-trip
    /// completed. These migrated scenarios run as no-stats quizzes by
    /// construction (the fallback mechanism has no writable handle); the
    /// FS-Access + stats path is covered by <c>StatsPersistenceTests</c>.
    /// </summary>
    protected async Task PickFixtureAsync(string fixtureFileName)
    {
        string dirName = Path.GetFileNameWithoutExtension(fixtureFileName);
        string stagedDir = Path.Combine(
            Path.GetTempPath(), "bgquiz-e2e", $"{dirName}-{Guid.NewGuid():N}", dirName);
        Directory.CreateDirectory(stagedDir);
        _stagedDirs.Add(Path.GetDirectoryName(stagedDir)!);
        File.Copy(FixturePath(fixtureFileName), Path.Combine(stagedDir, fixtureFileName));

        await FallbackFolderInput.SetInputFilesAsync(stagedDir);
        await Expect(Page.GetByText("1 problem file")).ToBeVisibleAsync();
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
    /// Add one row to the weighted-mix builder. The fresh row lands as the
    /// default "Never seen" category with its percent auto-completed — on an
    /// empty builder that is a complete, valid 100% never-seen mix.
    /// </summary>
    protected async Task AddDefaultMixRowAsync()
    {
        await Page.Locator("#mixAddRow").ClickAsync();
        await Expect(Page.Locator(".mix-row")).ToHaveCountAsync(1);
    }

    /// <summary>
    /// Apply the mix builder as-is and wait for the commit to land (the
    /// dirty-mix Start hint disappears) — the mix analog of
    /// <see cref="ApplyFilterAsync"/>.
    /// </summary>
    protected async Task ApplyMixAsync()
    {
        await Page.Locator("#mixApply").ClickAsync();
        await Expect(Page.GetByText("Apply or reset the mix above to enable Start")).ToHaveCountAsync(0);
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
}
