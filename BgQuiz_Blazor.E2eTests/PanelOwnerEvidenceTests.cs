using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Where the navigation panel's owner is absent — navFold.js has not assigned
/// <c>window.bgquizNavFold</c> — the action row's geometry fails with the
/// page's evidence, never with a bare <c>undefined</c> (halheinrich/backgammon#8,
/// leg 4c: a full-suite run on 2026-10-03 failed in
/// <c>RowFitTests.WiderText_RaisesTheMeasuredBudget_AndThePanelFoldsAtTheSameViewport</c>
/// on exactly that undefined, with nothing to say why).
/// </summary>
/// <remarks>
/// The owner is made absent each way a page can lack it while Blazor still
/// starts: the script's request failing, its body arriving empty (the shape
/// MapStaticAssets serves for an asset missing on disk), and the script
/// throwing before its assignment. Holding the script back cannot: Blazor
/// starts at <c>DOMContentLoaded</c>, which the parser-blocking script holds
/// until it has run or failed. These pin the evidence only, not what the row
/// does without its owner.
/// </remarks>
public sealed class PanelOwnerEvidenceTests : E2eTestBase
{
    public PanelOwnerEvidenceTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    /// <summary>How navFold.js fails to provide the owner.</summary>
    public enum Absence
    {
        /// <summary>The request fails at the network.</summary>
        RequestFailed,

        /// <summary>A 200 with an empty body.</summary>
        EmptyBody,

        /// <summary>The script loads and throws before its assignment.</summary>
        ScriptThrew,
    }

    private const string Thrown = "navFold.js threw before its assignment";

    private static Task Serve(IRoute route, Absence absence) => absence switch
    {
        Absence.RequestFailed => route.AbortAsync("failed"),
        Absence.EmptyBody => route.FulfillAsync(new() { Status = 200, Body = "" }),
        _ => route.FulfillAsync(new() { Status = 200, ContentType = "text/javascript", Body = $"throw new Error('{Thrown}');" }),
    };

    [Theory]
    [InlineData(Absence.RequestFailed, "request status 0, 0 bytes decoded", "console errors from navFold.js: Failed to load resource: net::ERR_FAILED", "page errors: none")]
    [InlineData(Absence.EmptyBody, "request status 200, 0 bytes decoded", "console errors from navFold.js: none", "page errors: none")]
    [InlineData(Absence.ScriptThrew, "request status 200, ", "console errors from navFold.js: none", "page errors: 1, the last: Error: " + Thrown)]
    public async Task WhereNavFoldJsLeftNoOwner_TheRowsGeometryFailsWithThePagesEvidence(
        Absence absence, string request, string consoleErrors, string pageErrors)
    {
        await Page.RouteAsync("**/js/navFold*.js", route => Serve(route, absence));
        await Page.SetViewportSizeAsync(1000, 800);
        await BootHomeAsync();
        await PickSynthesizedFileAsync(SyntheticXgMatch.StagedFileName, SyntheticXgMatch.Bytes());
        await ApplyFilterAsync();
        await StartQuizAsync();
        await Expect(Page.Locator(".action-row .bg-cube-actions label")).ToHaveCountAsync(4);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => ActionRowGeometry.FitAsync(Page));

        var evidence = failure.Message;
        Assert.Contains("navFold.js has not assigned window.bgquizNavFold", evidence);
        Assert.Contains($"page: {Page.Url}, readyState complete", evidence);
        Assert.Contains("navFold.js: script ", evidence);
        Assert.Contains(request, evidence);
        Assert.Contains(consoleErrors, evidence);
        Assert.Contains(pageErrors, evidence);
        Assert.Contains("Blazor: global present, WebAssembly runtime started, error UI hidden", evidence);
        Assert.Contains("action row: present", evidence);
    }
}
