using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Wire tests for the host's 404 handling — exercised through the real ASP.NET Core
/// pipeline, not through a rendered component.
/// <para>
/// This layer is the point: <c>Routes.razor</c>'s <c>NotFoundPage</c> only covers
/// client-side navigation, after the WASM runtime has booted. A server-side request
/// for an unmatched path never reaches Blazor at all — <c>MapRazorComponents</c>
/// registers endpoints only for known routes — so without
/// <c>UseStatusCodePagesWithReExecute</c> the response is a bare 404 with an empty
/// body: a blank page that reads as "the site is down". No bUnit component render
/// can observe that, because middleware and endpoint routing are outside its world.
/// </para>
/// <para>
/// The entry point is <see cref="BgQuiz_Blazor.Components.App"/> rather than the
/// conventional <c>Program</c>: both the host and the <c>.Client</c> emit a
/// global-namespace <c>Program</c> from top-level statements, and the client already
/// grants <c>InternalsVisibleTo</c> to this project — so the bare name is ambiguous
/// here. <see cref="WebApplicationFactory{TEntryPoint}"/> only uses the type to
/// locate its assembly, and <c>App</c> is unambiguously the host's.
/// </para>
/// </summary>
public sealed class NotFoundPipelineTests : IClassFixture<WebApplicationFactory<BgQuiz_Blazor.Components.App>>
{
    private readonly WebApplicationFactory<BgQuiz_Blazor.Components.App> _factory;

    public NotFoundPipelineTests(WebApplicationFactory<BgQuiz_Blazor.Components.App> factory) =>
        _factory = factory;

    /// <summary>
    /// The defect this suite exists for: a mistyped URL or a stale link must render the
    /// NotFound page, not a zero-byte body.
    /// </summary>
    [Fact]
    public async Task UnmatchedPath_Returns404_WithTheNotFoundPageBody()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/zzz-no-such-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(body);
        Assert.Contains("Not Found", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The re-execute must not swallow the status code. A 200 here would tell crawlers
    /// and link checkers that every mistyped URL is a real page.
    /// </summary>
    [Fact]
    public async Task UnmatchedPath_PreservesTheNotFoundStatus_AndServesHtml()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/quiz/deeper/still-missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// A known route must be unaffected — <c>/not-found</c> is itself a mapped route, so
    /// requesting it directly is a 200, not a 404. This pins that the re-execute target
    /// stays reachable on its own terms and that the middleware doesn't blanket the app.
    /// </summary>
    [Fact]
    public async Task MappedRoute_IsUntouchedByTheReExecute()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/not-found");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Blast-radius guard. <c>UseStatusCodePagesWithReExecute</c> intercepts every
    /// bodyless 4xx/5xx, so a missing <c>_framework</c> asset is answered with the
    /// NotFound page's HTML body rather than an empty one. That is correct, not merely
    /// tolerated: on a 4xx the body is an error document, not a representation of the
    /// requested resource, and <c>text/html</c> truthfully describes the body actually
    /// sent. The body is inert — nothing executes a document returned to a
    /// <c>fetch</c> for a script.
    /// <para>
    /// So what this asserts is the contract every asset consumer — Blazor's boot loader
    /// included — actually keys on: the status stays 404. Pinning it here makes any
    /// future narrowing of the re-execute (see INSTRUCTIONS.md Pitfalls: server-side
    /// JSON endpoints are the trigger) a deliberate, tested change rather than silent
    /// drift.
    /// </para>
    /// </summary>
    [Fact]
    public async Task MissingFrameworkAsset_Still404s()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/_framework/no-such-asset.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
