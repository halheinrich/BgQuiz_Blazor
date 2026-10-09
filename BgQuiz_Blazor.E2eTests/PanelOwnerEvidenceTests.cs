using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Where the navigation panel's owner is absent — navFold.js has not assigned
/// <c>window.bgquizNavFold</c> — the action row's geometry fails with the
/// page's evidence, never with a bare <c>undefined</c> (halheinrich/backgammon#8,
/// leg 4c: a full-suite run on 2026-10-03 failed in
/// <c>RowFitTests.WiderText_RaisesTheMeasuredBudget_AndThePanelFoldsAtTheSameViewport</c>
/// on exactly that undefined, with nothing to say why; that run's cause is
/// still unidentified, and this evidence is what will name it if it recurs).
/// </summary>
/// <remarks>
/// The owner is withheld each way a page can lack it while Blazor still starts
/// (<see cref="PanelOwnerAbsence"/>). These pin the evidence only; what the
/// row and the settings do without their owner is
/// <see cref="MissingPanelOwnerTests"/>'.
/// </remarks>
public sealed class PanelOwnerEvidenceTests : E2eTestBase
{
    public PanelOwnerEvidenceTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    [Theory]
    [InlineData(PanelOwnerAbsence.RequestFailed, "request status 0, 0 bytes decoded", "console errors from navFold.js: Failed to load resource: net::ERR_FAILED", "page errors: none")]
    [InlineData(PanelOwnerAbsence.EmptyBody, "request status 200, 0 bytes decoded", "console errors from navFold.js: none", "page errors: none")]
    [InlineData(PanelOwnerAbsence.ScriptThrew, "request status 200, ", "console errors from navFold.js: none", "page errors: 1, the last: Error: " + PanelOwner.Thrown)]
    public async Task WhereNavFoldJsLeftNoOwner_TheRowsGeometryFailsWithThePagesEvidence(
        PanelOwnerAbsence absence, string request, string consoleErrors, string pageErrors)
    {
        await PanelOwner.WithholdAsync(Page, absence);
        await Page.SetViewportSizeAsync(1000, 800);
        await BootHomeAsync();
        await PickSynthesizedFileAsync(SyntheticXgMatch.StagedFileName, SyntheticXgMatch.Bytes());
        await ExpectFilterInEffectAsync();
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
