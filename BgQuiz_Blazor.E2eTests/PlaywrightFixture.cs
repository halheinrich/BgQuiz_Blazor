using Microsoft.Playwright;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Once-per-run owner of the Playwright driver and a single headless Chromium
/// instance (Chromium only in v1). Tests take isolated
/// <see cref="IBrowserContext"/>s from <see cref="Browser"/> — a fresh context
/// per test gives clean <c>sessionStorage</c> / <c>localStorage</c>, which the
/// app uses for the quiz-live marker and the filter panel's persisted config.
///
/// <para>
/// <b>Fail loud, never skip.</b> Playwright's browser binaries are a one-time
/// per-machine install (see INSTRUCTIONS.md). When they are missing, launching
/// throws — and this fixture rethrows with the install command rather than
/// letting any test skip. A skipped smoke that reads as green is the defect
/// class this suite exists to kill.
/// </para>
/// </summary>
public sealed class PlaywrightFixture : IAsyncLifetime
{
    /// <summary>
    /// Generous ceiling for every locator action and <c>Expect</c> assertion:
    /// the first navigation in each fresh context downloads and boots the
    /// ~19.5 MB WASM payload before any page content exists. Playwright
    /// auto-waits, so passing steps never pay this in full.
    /// </summary>
    public const float DefaultTimeoutMs = 30_000f;

    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Assertions.SetDefaultExpectTimeout(DefaultTimeoutMs);

        _playwright = await Playwright.CreateAsync();
        try
        {
            Browser = await _playwright.Chromium.LaunchAsync();
        }
        catch (PlaywrightException ex)
        {
            throw new InvalidOperationException(
                "Chromium could not be launched — most likely the Playwright browsers " +
                "have not been installed on this machine yet. One-time setup, from the " +
                "repo root after building this project:\n" +
                @"  pwsh BgQuiz_Blazor.E2eTests\bin\Debug\net10.0\playwright.ps1 install chromium" + "\n" +
                "(See INSTRUCTIONS.md. This suite fails rather than skips on a missing " +
                "browser — a silently-skipped smoke gate reads as green.)", ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        _playwright?.Dispose();
    }
}
