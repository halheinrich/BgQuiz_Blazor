using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The busy affordance the folder scan raises (umbrella #48): between the
/// browser's prompts and the pick summary the app enumerates and buffers the
/// folder, which on a real corpus takes long enough that silence reads as a
/// hung page.
///
/// <para>
/// This scenario belongs at the e2e layer specifically, not only in bUnit.
/// bUnit can prove the state was <i>set</i>; it cannot prove the browser ever
/// <b>painted</b> it — and on WebAssembly's single thread a busy state raised
/// immediately before uninterrupted work never does, which is the exact failure
/// this affordance exists to avoid and one a component assertion reports as a
/// pass. Reading the real DOM mid-scan, with the fake's enumeration held open,
/// is what makes the claim falsifiable.
/// </para>
/// </summary>
public sealed class PickBusyAffordanceTests : FsAccessFakeTestBase
{
    public PickBusyAffordanceTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    private static Regex BusyClass => new(@"\bapp-busy\b");

    [Fact]
    public async Task FolderScan_PaintsTheBusyAffordance_AndClearsWhenTheSummaryLands()
    {
        await BootHomeAsync();

        var container = Page.Locator("div.container").First;
        await Expect(container).Not.ToHaveClassAsync(BusyClass);

        await HoldScanAsync();
        await PickFolderButton.ClickAsync();

        // Mid-scan: the progress cursor is on the page and the whole setup
        // surface — the pick button included — is disabled. Both derive from
        // one predicate, so a regression in either shows up here.
        await Expect(container).ToHaveClassAsync(BusyClass);
        await Expect(PickFolderButton).ToBeDisabledAsync();

        await ReleaseScanAsync();

        // …and it is lowered exactly when the summary lands, not before.
        await Expect(Page.GetByText("1 problem file")).ToBeVisibleAsync();
        await Expect(container).Not.ToHaveClassAsync(BusyClass);
        await Expect(PickFolderButton).ToBeEnabledAsync();
    }
}
