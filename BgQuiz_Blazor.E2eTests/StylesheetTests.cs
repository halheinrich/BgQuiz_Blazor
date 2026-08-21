using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Bootstrap is actually applied to the served app (issue
/// <c>halheinrich/backgammon#126</c>). One scenario, on a cold <c>/</c>, and it
/// exists because the whole suite was blind to the app shipping unstyled.
///
/// <para>
/// <b>What went wrong without it.</b> <c>App.razor</c> links one file under
/// <c>wwwroot/lib</c>, nothing in the repo restores that folder, and
/// <c>.gitignore</c> excluded it — so the stylesheet existed on one developer
/// machine and nowhere else. Umbrella CI (run 32534082312) built and served an
/// unstyled app for months: <c>.btn</c> at the user-agent's 21px, the quiz's
/// trailing cluster laid out as a block on its own line, no container gutters.
/// Every test stayed green, because every test asked about behaviour — which
/// survives a missing stylesheet — and the one geometric assertion in the suite
/// (the locator's, <c>halheinrich/backgammon#115</c>) was read as a layout bug
/// rather than as the messenger it was.
/// </para>
///
/// <para>
/// <b>Why these two assertions.</b> The first is a real layout value on a real
/// element: at a viewport of 1280 Bootstrap's <c>.container</c> resolves to its
/// <c>xl</c> width of 1140px, where an unstyled <c>div</c> computes
/// <c>none</c> — the two cannot be confused, and it is the same class of fact
/// the CI failure was made of. It is deliberately not the button's height: the
/// symptom CI showed, but a value the user agent also has an opinion about, so
/// a wrong number there reads as ambiguous. The second identifies the
/// stylesheet as <i>Bootstrap</i> rather than merely as "something that sets
/// 1140px", by reading a custom property only Bootstrap declares. Neither can
/// pass with the file missing, which is the whole point; both were checked by
/// renaming the tracked file and watching this fail.
/// </para>
/// </summary>
public sealed class StylesheetTests : E2eTestBase
{
    public StylesheetTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    /// <summary>
    /// Wide enough to land in Bootstrap's <c>xl</c> tier (≥1200px), so the
    /// container width below is a single known number rather than a function of
    /// whatever the runner's default viewport happens to be.
    /// </summary>
    private const int DesktopWidth = 1280;

    private const int DesktopHeight = 800;

    /// <summary>Bootstrap 5's <c>.container</c> max-width in the <c>xl</c> tier.</summary>
    private const string ContainerWidthAtXl = "1140px";

    [Fact]
    public async Task BootstrapIsLoadedAndApplied_OnAColdHomePage()
    {
        await Page.SetViewportSizeAsync(DesktopWidth, DesktopHeight);
        await BootHomeAsync();

        // Applied to an element the page actually renders — Home's own wrapper.
        var container = Page.Locator("div.container").First;
        await Expect(container).ToBeVisibleAsync();

        var maxWidth = await container.EvaluateAsync<string>(
            "e => getComputedStyle(e).maxWidth");

        Assert.Equal(ContainerWidthAtXl, maxWidth);

        // …and it is Bootstrap's stylesheet doing it. --bs-* custom properties
        // are declared by Bootstrap's :root block and by nothing else this app
        // loads, so a non-empty value here is the file's fingerprint.
        var bootstrapVariable = await Page.EvaluateAsync<string>(
            "() => getComputedStyle(document.documentElement)"
            + ".getPropertyValue('--bs-primary').trim()");

        Assert.False(
            string.IsNullOrEmpty(bootstrapVariable),
            "--bs-primary is empty: Bootstrap's stylesheet did not load. It is vendored at "
            + "BgQuiz_Blazor/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css and tracked by a "
            + "negation in .gitignore — check that the file is present and still re-included.");
    }
}
