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

    /// <summary>
    /// Refuse both storage areas at the property, as a browser blocking site
    /// data does, and record every refusal nothing caught — with where the read
    /// was made — in <c>window.__unhandledStorageRefusals</c>.
    /// </summary>
    private Task RefuseStorageAsync() =>
        Page.AddInitScriptAsync("""
            (() => {
                window.__unhandledStorageRefusals = [];
                const record = reason => {
                    if (reason && reason.readFrom) window.__unhandledStorageRefusals.push(reason.readFrom);
                };
                window.addEventListener('error', e => record(e.error));
                window.addEventListener('unhandledrejection', e => record(e.reason));
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

    /// <summary>
    /// The one storage read this app does not make and cannot guard: Blazor's
    /// own <c>blazor.web.js</c> reads <c>localStorage</c> unguarded in its
    /// WebAssembly start-up (whether the runtime's resources are cached), so a
    /// browser refusing storage sees that read's rejection go unhandled. It is
    /// the framework's, measured 2026-10-07 on the .NET 10 runtime this repo
    /// pins; the app boots past it. Any other unhandled refusal is this app's,
    /// and fails the scenario.
    /// </summary>
    private const string FrameworksOwnStartUpRead = "startLoadingWebAssemblyIfNotStarted";

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

        // Nothing of the app's went unhandled: no Blazor error banner, no .NET
        // unhandled exception on the console, and no refused read left
        // uncaught but the framework's own.
        await Expect(Page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        Assert.DoesNotContain(await Page.ConsoleMessagesAsync(), m =>
            m.Type == "error" && m.Text.Contains("Unhandled exception", StringComparison.Ordinal));
        Assert.All(
            await Page.EvaluateAsync<string[]>("() => window.__unhandledStorageRefusals"),
            readFrom => Assert.Contains(FrameworksOwnStartUpRead, readFrom));
    }
}
