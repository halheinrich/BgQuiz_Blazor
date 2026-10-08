using System.Diagnostics;
using System.Text.Json;
using Microsoft.Playwright;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// A browser that refuses BgQuiz its storage (issue
/// <c>halheinrich/backgammon#360</c>): the published app boots, says so in
/// one condition notice on Home, and runs a quiz. The refusal is the shape a
/// browser blocking site data gives — reading <c>window.localStorage</c> or
/// <c>window.sessionStorage</c> itself throws a <c>SecurityError</c> — stood
/// in for at the API, as the folder picker's fake stands in for its API; the
/// app's interop, its stores' guards and the hosted filter panel's own guard
/// all meet it for real. What each store does under it, and the occurrence
/// the notice's dismissal follows, are <c>PageTests</c>' and the stores' own
/// suites'; this is the one place they run together in a browser.
///
/// <para>
/// <b>What "nothing went unhandled" checks</b>
/// (<see cref="AssertNothingUnhandledButTheFrameworksStartUpReadAsync"/>).
/// The capture sees <b>every</b> unhandled error and promise rejection the page
/// raises — a window <c>error</c> event or an <c>unhandledrejection</c>, storage
/// or not — and records each with its message, its error name and, for a
/// refusal of the stand-in's own, the stack of the read that met it. The
/// allowance admits exactly one thing: the framework's own unguarded storage
/// read in its WebAssembly start-up (<see cref="IsTheFrameworksStartUpRead"/>),
/// a rejected <c>SecurityError</c> from the stand-in whose read was made by
/// <c>blazor.web.js</c> inside <see cref="FrameworksStartUpFunction"/>, at most
/// once. Everything else recorded fails the check, and the Blazor error banner
/// and a .NET "Unhandled exception" on the console fail it too.
/// <see cref="TheUnhandledCheck_FailsOnAnUnrelatedFailure_AndPassesWithoutOne"/>
/// is its control: an unrelated rejection or throw injected into the same page
/// fails it, and the page without one passes.
/// </para>
/// </summary>
public sealed class BrowserStorageRefusedTests : E2eTestBase
{
    /// <summary>xUnit's per-test output sink, for the remount report's evidence.</summary>
    private readonly ITestOutputHelper _output;

    public BrowserStorageRefusedTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright)
    {
        _output = output;
    }

    private ILocator StorageNotice => Page.Locator("#storageUnavailableNotice");

    /// <summary>The warning Home logs each time the hosted filter panel reports the refusal.</summary>
    private const string PanelReportWarning = "The filter panel reports that the browser refused its storage";

    /// <summary>
    /// How many times the hosted filter panel has reported the refusal to
    /// Home, read off the warning Home logs for each report (the panel logs
    /// nothing itself) — the evidence that a remounted panel reported again.
    /// </summary>
    private async Task<int> PanelReportsAsync() =>
        (await Page.ConsoleMessagesAsync()).Count(m => m.Text.Contains(PanelReportWarning, StringComparison.Ordinal));

    /// <summary>
    /// Every console message a page logs from the moment this is made, in
    /// arrival order — diagnostics for <c>halheinrich/backgammon#372</c>. A
    /// listener attached before the first navigation, unlike
    /// <see cref="IPage.ConsoleMessagesAsync"/>, keeps a record whose start
    /// is known and from which nothing is dropped.
    /// </summary>
    private sealed class ConsoleLog
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<string> _entries = [];
        private readonly object _gate = new();

        public ConsoleLog(IPage page) => page.Console += Record;

        private void Record(object? sender, IConsoleMessage message)
        {
            var received = _clock.ElapsedMilliseconds;
            lock (_gate)
            {
                _entries.Add(
                    $"#{_entries.Count + 1} +{received} ms [{message.Type}] {message.Text}"
                    + Environment.NewLine + $"    at {message.Location}");
            }
        }

        /// <summary>The messages recorded so far.</summary>
        public IReadOnlyList<string> Entries
        {
            get { lock (_gate) return [.. _entries]; }
        }
    }

    /// <summary>
    /// The page's state at the remount report's count check, read whether
    /// the check passed or failed (<c>halheinrich/backgammon#372</c>): the
    /// URL, whether the hosted filter panel is mounted, the notice and the
    /// folder line, both counts of the panel's warning, and the unhandled
    /// records. Each read is taken on its own and a failed read is reported
    /// in its place, so collecting this never throws over the check's own
    /// outcome.
    /// </summary>
    private async Task<IReadOnlyList<string>> CountCheckStateAsync(ConsoleLog console)
    {
        var state = new List<string>();
        async Task ReadAsync(string what, Func<Task<string>> read)
        {
            try { state.Add($"{what}: {await read()}"); }
            catch (Exception e) { state.Add($"{what}: (could not read — {e.GetType().Name}: {e.Message})"); }
        }

        await ReadAsync("URL", () => Task.FromResult(Page.Url));
        await ReadAsync("Apply Filter controls (the hosted panel mounted)", async () =>
            (await Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ApplyFilterButton }).CountAsync()).ToString());
        await ReadAsync("#storageUnavailableNotice elements", async () => (await StorageNotice.CountAsync()).ToString());
        await ReadAsync("Folder line", async () =>
        {
            var lines = await Page.Locator(".problem-folder-label").AllInnerTextsAsync();
            return lines.Count == 0 ? "(none)" : string.Join(" | ", lines);
        });
        await ReadAsync("Panel warnings in the listener's log", () =>
            Task.FromResult(console.Entries.Count(e => e.Contains(PanelReportWarning, StringComparison.Ordinal)).ToString()));
        await ReadAsync("Listener's log length", () => Task.FromResult(console.Entries.Count.ToString()));
        await ReadAsync("Panel warnings in ConsoleMessagesAsync (the check's source)", async () => (await PanelReportsAsync()).ToString());
        await ReadAsync("ConsoleMessagesAsync length", async () => (await Page.ConsoleMessagesAsync()).Count.ToString());
        await ReadAsync("window.__unhandled", async () =>
        {
            var unhandled = await UnhandledAsync();
            return unhandled.Length == 0
                ? "(none)"
                : unhandled.Length + " record(s)" + string.Concat(unhandled.Select((u, i) =>
                    Environment.NewLine + $"  [{i}] {u.Kind} {u.Name}: {u.Message}"
                    + (u.ReadFrom is null ? "" : Environment.NewLine + "      readFrom: " + u.ReadFrom.ReplaceLineEndings(Environment.NewLine + "        "))));
        });
        return state;
    }

    /// <summary>
    /// Print the remount report's evidence (<c>halheinrich/backgammon#372</c>)
    /// to the test output: the state at the count check, if it was reached,
    /// then every console message from the test's start.
    /// </summary>
    private void WriteRemountEvidence(ConsoleLog console, IReadOnlyList<string>? atCountCheck)
    {
        _output.WriteLine("[halheinrich/backgammon#372] state at the remount report's count check:");
        foreach (var line in atCountCheck ?? ["(the count check was not reached)"]) _output.WriteLine("  " + line);
        var entries = console.Entries;
        _output.WriteLine($"[halheinrich/backgammon#372] console messages from the test's start ({entries.Count}):");
        foreach (var entry in entries) _output.WriteLine("  " + entry);
    }

    /// <summary>The event the control's init-script listener answers with an unrelated unhandled failure.</summary>
    private const string UnrelatedFailureEvent = "bgquiz-test:unrelated-failure";

    /// <summary>
    /// Refuse both storage areas at the property, as a browser blocking site
    /// data does; record every unhandled error and rejection the page raises in
    /// <c>window.__unhandled</c>; and listen for the control's trigger, which
    /// answers with an unhandled rejection or throw that has nothing to do with
    /// storage. Nothing in the app is changed or consulted: the listener is
    /// this test's, and only a dispatch from the test fires it.
    /// </summary>
    private Task RefuseStorageAsync() =>
        Page.AddInitScriptAsync($$"""
            (() => {
                window.__unhandled = [];
                const record = (kind, reason, message) => window.__unhandled.push({
                    kind,
                    message: String(message ?? (reason && reason.message) ?? reason),
                    name: reason && reason.name ? String(reason.name) : null,
                    readFrom: reason && typeof reason.readFrom === 'string' ? reason.readFrom : null,
                });
                window.addEventListener('error', e => record('error', e.error, e.message));
                window.addEventListener('unhandledrejection', e => record('rejection', e.reason));

                window.addEventListener('{{UnrelatedFailureEvent}}', e => {
                    if (e.detail === 'rejection') Promise.reject(new Error('an unrelated unhandled rejection'));
                    else setTimeout(() => { throw new Error('an unrelated unhandled throw'); });
                });

                for (const area of ['localStorage', 'sessionStorage']) {
                    Object.defineProperty(window, area, {
                        configurable: true,
                        get() {
                            const refusal = new DOMException(
                                `Failed to read the '${area}' property from 'Window': Access is denied for this document.`,
                                'SecurityError');
                            refusal.readFrom = new Error().stack;
                            throw refusal;
                        },
                    });
                }
            })();
            """);

    /// <summary>One unhandled failure the capture recorded.</summary>
    private sealed record Unhandled(string Kind, string Message, string? Name, string? ReadFrom);

    private async Task<Unhandled[]> UnhandledAsync() =>
        JsonSerializer.Deserialize<Unhandled[]>(
            await Page.EvaluateAsync<string>("() => JSON.stringify(window.__unhandled)"),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    /// <summary>
    /// The function in Blazor's own <c>blazor.web.js</c> that reads
    /// <c>localStorage</c> unguarded in its WebAssembly start-up (whether the
    /// runtime's resources are cached), so a browser refusing storage sees that
    /// read's rejection go unhandled — the framework's, measured 2026-10-07 on
    /// the .NET 10 runtime this repo pins; the app boots past it.
    /// </summary>
    private const string FrameworksStartUpFunction = "startLoadingWebAssemblyIfNotStarted";

    /// <summary>
    /// Whether <paramref name="failure"/> is that read and nothing else: a
    /// rejection, of the stand-in's own <c>SecurityError</c>, whose read was
    /// made directly by <c>blazor.web.js</c> (the frame below the stand-in's
    /// getter) inside <see cref="FrameworksStartUpFunction"/>, itself in
    /// <c>blazor.web.js</c>.
    /// </summary>
    private static bool IsTheFrameworksStartUpRead(Unhandled failure)
    {
        if (failure is not { Kind: "rejection", Name: "SecurityError", ReadFrom: { } readFrom }) return false;
        var frames = readFrom.Split('\n').Select(f => f.Trim()).Where(f => f.StartsWith("at ", StringComparison.Ordinal)).ToArray();
        const string FrameworkScript = "/_framework/blazor.web.";
        return frames.Length >= 2
            && frames[1].Contains(FrameworkScript, StringComparison.Ordinal)
            && frames.Skip(1).Any(f =>
                f.Contains(FrameworksStartUpFunction + " (", StringComparison.Ordinal)
                && f.Contains(FrameworkScript, StringComparison.Ordinal));
    }

    /// <summary>
    /// Nothing of the app's went unhandled: no Blazor error banner, no .NET
    /// unhandled exception on the console, and no unhandled error or rejection
    /// of any kind but the framework's start-up read, at most once — see the
    /// type's remarks for what the capture sees.
    /// </summary>
    private async Task AssertNothingUnhandledButTheFrameworksStartUpReadAsync()
    {
        await Expect(Page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        Assert.DoesNotContain(await Page.ConsoleMessagesAsync(), m =>
            m.Type == "error" && m.Text.Contains("Unhandled exception", StringComparison.Ordinal));

        var unhandled = await UnhandledAsync();
        var admitted = unhandled.Where(IsTheFrameworksStartUpRead).ToArray();
        Assert.True(admitted.Length <= 1, $"the framework's start-up read was recorded {admitted.Length} times");
        var others = unhandled.Except(admitted).ToArray();
        Assert.True(others.Length == 0,
            "unhandled in the page: " + string.Join(" | ", others.Select(f => $"{f.Kind} {f.Name}: {f.Message}")));
    }

    [Fact]
    public async Task HomeSaysSoOnce_TheDismissalHoldsAcrossNavigation_AndAQuizStillRuns()
    {
        // Diagnostics for halheinrich/backgammon#372, printed whether or not
        // the test passes: the console from before the first navigation, and
        // the page's state at the remount report's count check.
        var console = new ConsoleLog(Page);
        IReadOnlyList<string>? atCountCheck = null;
        try
        {
            await RefuseStorageAsync();

            await BootHomeAsync();

            // The premise, observed: the page itself cannot reach storage.
            Assert.Equal("SecurityError", await Page.EvaluateAsync<string>(
                "() => { try { localStorage.length; return 'readable'; } catch (e) { return e.name; } }"));

            await Expect(StorageNotice).ToHaveCountAsync(1);
            await Expect(StorageNotice).ToContainTextAsync(
                "BgQuiz had trouble using your browser's storage. You can keep using it, but some choices may not "
                + "be remembered next time.");

            // Dismissed, and still dismissed after an enhanced navigation away and
            // back — the hosted panel remounting with a fresh pick reports the same
            // condition again on the way.
            await StorageNotice.GetByRole(AriaRole.Button).ClickAsync();
            await Expect(StorageNotice).ToHaveCountAsync(0);
            await PickFixtureAsync(CubeFixture);
            await Page.GetByRole(AriaRole.Link, new() { Name = ExpectedText.SettingsNavLink, Exact = true }).ClickAsync();
            await ExpectUrlAsync("/settings");
            // The URL alone does not show that Settings replaced Home
            // (halheinrich/backgammon#372): its heading is up and Home's
            // folder-pick control is gone before Home is clicked.
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Settings", Level = 1, Exact = true })).ToBeVisibleAsync();
            await Expect(PickFolderButton).ToHaveCountAsync(0);
            await Page.GetByRole(AriaRole.Link, new() { Name = ExpectedText.HomeNavLink, Exact = true }).ClickAsync();
            await Expect(PickFolderButton).ToBeVisibleAsync();
            try
            {
                await ExpectToPassAsync(async () => Assert.Equal(2, await PanelReportsAsync()));
            }
            finally
            {
                atCountCheck = await CountCheckStateAsync(console);
            }
            await Expect(StorageNotice).ToHaveCountAsync(0);

            // And the quiz runs on it: apply, start, answer.
            await ApplyFilterAsync();
            await StartQuizAsync();
            await Expect(CubeAnswers).ToHaveCountAsync(4);
            await AnswerCubeNoDoubleAsync();

            await AssertNothingUnhandledButTheFrameworksStartUpReadAsync();
        }
        finally
        {
            WriteRemountEvidence(console, atCountCheck);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("rejection")]
    [InlineData("throw")]
    public async Task TheUnhandledCheck_FailsOnAnUnrelatedFailure_AndPassesWithoutOne(string? injected)
    {
        // The check's control: the same refused-storage page, booted the same
        // way, with or without one unrelated unhandled failure.
        await RefuseStorageAsync();
        await BootHomeAsync();
        await Expect(StorageNotice).ToHaveCountAsync(1);

        if (injected is not null)
        {
            await Page.EvaluateAsync(
                "([name, kind]) => window.dispatchEvent(new CustomEvent(name, { detail: kind }))",
                new object[] { UnrelatedFailureEvent, injected });
            // The injected failure has reached the capture before the check runs.
            await Page.WaitForFunctionAsync(
                "() => window.__unhandled.some(f => f.message.includes('an unrelated unhandled'))");
        }

        var failure = await Record.ExceptionAsync(AssertNothingUnhandledButTheFrameworksStartUpReadAsync);

        if (injected is null)
        {
            Assert.Null(failure);
        }
        else
        {
            Assert.NotNull(failure);
            Assert.Contains($"an unrelated unhandled {injected}", failure.Message);
        }
    }
}
