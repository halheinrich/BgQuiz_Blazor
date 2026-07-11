using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The empty-result outcome: a valid Start whose filters admit nothing must
/// stay on Home with a polite status banner — not bounce silently through
/// <c>/quiz</c> to a 0/0 <c>/done</c>. This automates the live repro from the
/// beta-readiness assessment (the fourth of the four invisible-to-tests
/// production defects this suite exists to gate).
/// </summary>
public sealed class EmptyFilterBannerTests : E2eTestBase
{
    public EmptyFilterBannerTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    [Fact]
    public async Task RaceFilterAgainstContactPosition_ShowsBannerAndStaysHome()
    {
        await BootHomeAsync();

        // Pick first: waiting for the picked-file summary also guarantees the
        // filter panel's first-render localStorage restore has settled, so the
        // Race click below cannot be overwritten by a late hydrate.
        await PickFixtureAsync(CubeFixture);

        // The cube fixture is a contact position, so the Race contact-type
        // filter admits nothing.
        await Page.GetByLabel("Race", new() { Exact = true }).CheckAsync();
        await ApplyFilterAsync();

        await Expect(StartButton).ToBeEnabledAsync();
        await StartButton.ClickAsync();

        // Outcome, not failure: a polite role="status" banner, and no navigation.
        var banner = Page.GetByRole(AriaRole.Status)
            .Filter(new() { HasText = "No quiz problems matched these filters" });
        await Expect(banner).ToBeVisibleAsync();
        await ExpectUrlAsync("/");
    }
}
