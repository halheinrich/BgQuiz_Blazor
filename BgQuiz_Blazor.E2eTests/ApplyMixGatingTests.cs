using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// <b>Apply Mix is sequenced behind Apply Filter</b> (umbrella #45), in a real
/// browser. The suite's other flows always happened to apply the filter first,
/// so they pass with or without the gate — this scenario is the one that fails
/// without it.
/// </summary>
public sealed class ApplyMixGatingTests : FsAccessFakeTestBase
{
    public ApplyMixGatingTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    private ILocator MixApply => Page.Locator("#mixApply");

    [Fact]
    public async Task ApplyMix_IsGatedUntilApplyFilter_AndSurvivesALaterFilterEdit()
    {
        await BootHomeAsync();
        await PickFakeFolderAsync();

        // A complete, valid one-row mix: from here the only thing that can
        // disable Apply Mix is the host's filter gate. The hint must say *why*,
        // not merely refuse — the bare rule is what read as arbitrary.
        await AddDefaultMixRowAsync();
        await Expect(MixApply).ToBeDisabledAsync();
        await Expect(Page.GetByText("the mix draws its problems from the filtered pool"))
            .ToBeVisibleAsync();

        await ApplyFilterAsync();
        await Expect(MixApply).ToBeEnabledAsync();

        // A later filter edit re-gates Start but must NOT revoke the mix gate:
        // the corpus has been filtered, and yanking Apply Mix away
        // mid-composition for an unrelated edit is the coupling the issue's
        // caution ruled out.
        await Page.GetByPlaceholder("Min").First.FillAsync("0.05");
        await Expect(StartButton).ToBeDisabledAsync();
        await Expect(MixApply).ToBeEnabledAsync();
    }

    [Fact]
    public async Task GatedApplyMix_LeavesResetLive_SoADirtyDraftCanAlwaysBeCleared()
    {
        // Wedge-proofing: a dirty draft gates Start, so if both ways out were
        // sequenced behind the filter a user could reach a state with no
        // visible way forward. Reset is ungated in every state.
        await BootHomeAsync();
        await PickFakeFolderAsync();
        await AddDefaultMixRowAsync();

        await Expect(MixApply).ToBeDisabledAsync();
        await Expect(StartButton).ToBeDisabledAsync();
        await Expect(Page.Locator("#mixReset")).ToBeEnabledAsync();

        await Page.Locator("#mixReset").ClickAsync();

        await Expect(Page.GetByText("Apply or reset the mix above to enable Start"))
            .ToHaveCountAsync(0);
    }
}
