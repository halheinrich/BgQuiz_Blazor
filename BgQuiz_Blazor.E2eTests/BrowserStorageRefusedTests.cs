using System.Text.Json;
using Microsoft.Playwright;
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
    public BrowserStorageRefusedTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    private ILocator StorageNotice => Page.Locator("#storageUnavailableNotice");

    /// <summary>
    /// How many times the hosted filter panel has reported the refusal to
    /// Home, read off the warning Home logs for each report (the panel logs
    /// nothing itself) — the evidence that a remounted panel reported again.
    /// </summary>
    private async Task<int> PanelReportsAsync() =>
        (await Page.ConsoleMessagesAsync()).Count(m =>
            m.Text.Contains("The filter panel reports that the browser refused its storage", StringComparison.Ordinal));

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
        await Page.GetByRole(AriaRole.Link, new() { Name = ExpectedText.HomeNavLink, Exact = true }).ClickAsync();
        await Expect(PickFolderButton).ToBeVisibleAsync();
        await ExpectToPassAsync(async () => Assert.Equal(2, await PanelReportsAsync()));
        await Expect(StorageNotice).ToHaveCountAsync(0);

        // And the quiz runs on it: apply, start, answer.
        await ApplyFilterAsync();
        await StartQuizAsync();
        await Expect(CubeAnswers).ToHaveCountAsync(4);
        await AnswerCubeNoDoubleAsync();

        await AssertNothingUnhandledButTheFrameworksStartUpReadAsync();
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
