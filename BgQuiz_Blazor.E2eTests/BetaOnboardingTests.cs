using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The beta wave's two published-artifact surfaces, neither of which any other
/// layer can see.
///
/// <para>
/// <c>robots.txt</c> is served by the <b>host</b> over HTTP — it is a static file
/// in the host's wwwroot, not a Blazor route — so bUnit is structurally blind to
/// it and only a real request against the publish output proves it is there and
/// reachable. It is also the exact thing a wrong-wwwroot mistake would break
/// silently: the client project has a wwwroot too, and a file placed there would
/// still build, still publish, and still 404.
/// </para>
///
/// <para>
/// The feedback link is checked here because the version in its subject is
/// resolved from the <i>built</i> assembly's informational version. bUnit pins
/// that the pages read one app-level value; only a real boot of the published
/// artifact shows what that value actually is, including the
/// <c>+g&lt;shortsha&gt;</c> suffix a non-shipping build carries — which is the
/// whole reason the version is in the subject at all. This suite deliberately
/// keeps its own literals (address, subject shape, escaping) rather than
/// importing an app constant: the e2e project references no app assembly by
/// design, and the literal's independence is what gives the assertion power.
/// </para>
/// </summary>
public sealed class BetaOnboardingTests : E2eTestBase
{
    /// <summary>The beta mailbox, restated here as the consumer-side pin.</summary>
    private const string FeedbackAddress = "bgquiz.beta@gmail.com";

    public BetaOnboardingTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    [Fact]
    public async Task RobotsTxt_IsServed_AndDisallowsEveryCrawler()
    {
        var response = await Page.GotoAsync(BaseUrl + "/robots.txt");

        // A missing file would not 404 visibly here — the host's
        // UseStatusCodePagesWithReExecute would serve the styled NotFound page
        // with a 404 — so the status and the body both have to be asserted.
        Assert.NotNull(response);
        Assert.Equal(200, response.Status);

        var body = await response.TextAsync();
        Assert.Contains("User-agent: *", body, StringComparison.Ordinal);
        Assert.Contains("Disallow: /", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomeAndHelp_OfferOneFeedbackMailto_SubjectCarryingTheRunningBuild()
    {
        await BootHomeAsync();

        // The version the running build actually reports, read off the footer the
        // user reads — not a literal, which would pin this suite to one release.
        var footer = (await Page.Locator("#appVersion").InnerTextAsync()).Trim();
        Assert.StartsWith("v", footer, StringComparison.Ordinal);
        var version = footer[1..];
        Assert.Matches(@"^\d+\.\d+\.\d+", version);

        // Built from independent literals: the address, the subject wording, and
        // the escaping rule. Escaping is load-bearing — a non-shipping build's
        // version carries a '+' ("1.0.10+gabc1234"), which a raw query would let
        // a mail client decode as a space.
        var expected =
            $"mailto:{FeedbackAddress}?subject={Uri.EscapeDataString($"BgQuiz feedback ({version})")}";

        var homeLink = Page.Locator("a[href^='mailto:']");
        await Expect(homeLink).ToHaveCountAsync(1);
        await Expect(homeLink).ToHaveAttributeAsync("href", expected);

        // The same link on Help, resolved from the same app-level value — the
        // reason the version stopped living on the Home page class.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Help" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Send feedback" }))
            .ToBeVisibleAsync();

        var helpLink = Page.Locator("a[href^='mailto:']");
        await Expect(helpLink).ToHaveCountAsync(1);
        await Expect(helpLink).ToHaveAttributeAsync("href", expected);
    }
}
