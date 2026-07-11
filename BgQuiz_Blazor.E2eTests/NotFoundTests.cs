using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Server-side unmatched paths must serve the styled NotFound page with a 404
/// status. Without the host's status-code re-execute, an unknown URL never
/// reaches Blazor and returns a zero-byte body — a completely blank page that
/// reads as "the site is down" (another of the four invisible-to-tests
/// production defects). Both halves matter: the status for machine consumers,
/// the body for humans.
/// </summary>
public sealed class NotFoundTests : E2eTestBase
{
    public NotFoundTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    [Fact]
    public async Task UnknownUrl_Returns404WithStyledNotFoundPage()
    {
        var response = await Page.GotoAsync(BaseUrl + "/zzz");

        Assert.NotNull(response);
        Assert.Equal(404, response.Status);

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Not Found" }))
            .ToBeVisibleAsync();
        await Expect(Page.GetByText("Sorry, the content you are looking for does not exist."))
            .ToBeVisibleAsync();
    }
}
