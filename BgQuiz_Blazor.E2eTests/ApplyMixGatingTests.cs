using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// <b>Mix activation is sequenced behind Apply Filter</b> (umbrella #45, the
/// spec's Fork A ruled strict), in a real browser — now on the "Mix applies"
/// checkbox, the sole activation control (#83). The suite's other flows always
/// happen to apply the filter first, so they pass with or without the gate —
/// these scenarios are the ones that fail without it.
/// </summary>
public sealed class ApplyMixGatingTests : FsAccessFakeTestBase
{
    public ApplyMixGatingTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    private ILocator MixApplies => Page.Locator("#mixApplies");

    [Fact]
    public async Task MixActivation_IsGatedUntilApplyFilter_AndRevokedByALaterFilterEdit()
    {
        await BootHomeAsync();
        // #87: the mix panel is offered only for a folder with a stats history,
        // and this helper leaves exactly the state the gate is about — folder
        // held, stats readable, no filter applied for the current pick.
        await SeedStatsHistoryAsync();

        // A complete, valid one-row mix: from here the only thing that can
        // disable the checkbox is the host's filter gate. The hint must say
        // *why*, not merely refuse — the bare rule is what read as arbitrary.
        await AddDefaultMixRowAsync();
        await Expect(MixApplies).ToBeDisabledAsync();
        await Expect(Page.GetByText("the mix draws its problems from the filtered pool"))
            .ToBeVisibleAsync();

        await ApplyFilterAsync();
        await Expect(MixApplies).ToBeEnabledAsync();

        // A later filter edit revokes the (unchecked) check gesture and Start
        // together — Fork A strict: activation reads the filter in effect
        // *now*, the same fact Start reads, so there is no browser state in
        // which the two disagree.
        await Page.GetByPlaceholder("Min").First.FillAsync("0.05");
        await Expect(StartButton).ToBeDisabledAsync();
        await Expect(MixApplies).ToBeDisabledAsync();
        await Expect(Page.GetByText("the mix draws its problems from the filtered pool"))
            .ToBeVisibleAsync();

        // Undo the edit: the panel reports clean, the applied filter is back
        // in effect, and the gate reopens — one gesture, no wedge. (The
        // re-apply recovery path is pinned at the bUnit layer, where the
        // corpus is fake and the pool cannot empty underneath the assertion.)
        await Page.GetByPlaceholder("Min").First.FillAsync("");
        await Expect(MixApplies).ToBeEnabledAsync();
        await Expect(Page.GetByText("the mix draws its problems from the filtered pool"))
            .ToHaveCountAsync(0);

        // Checking is the activation, and Start stays live throughout — an
        // un-activated mix never gated it, and the now-active valid mix
        // doesn't either.
        await ActivateMixAsync();
        await Expect(StartButton).ToBeEnabledAsync();
    }

    [Fact]
    public async Task GatedActivation_LeavesClearMixLive_AndTheCheckedBoxOperable()
    {
        // Two ways out that must never be sequenced away: Clear mix (rows) is
        // ungated in every state, and a box checked while the filter was in
        // effect stays operable through a later filter edit — unchecking is
        // consent withdrawn, and only the user moves the bit.
        await BootHomeAsync();
        await SeedStatsHistoryAsync();
        await AddDefaultMixRowAsync();

        // Gated (no filter in effect) — Clear stays live.
        await Expect(MixApplies).ToBeDisabledAsync();
        await Expect(Page.Locator("#mixClear")).ToBeEnabledAsync();

        // Activate properly (the row from above is still on screen), then
        // dirty the filter: the CHECKED box remains operable (the disable is
        // asymmetric — it gates checking only).
        await ApplyFilterAsync();
        await ActivateMixAsync();
        await Page.GetByPlaceholder("Min").First.FillAsync("0.05");
        await Expect(StartButton).ToBeDisabledAsync(); // the filter's own gate
        await Expect(MixApplies).ToBeEnabledAsync();   // but uncheck is still live

        await MixApplies.UncheckAsync();
        await Expect(MixApplies).Not.ToBeCheckedAsync();

        // And Clear mix still works here too — rows removed, box untouched.
        await Page.Locator("#mixClear").ClickAsync();
        await Expect(Page.Locator(".mix-row")).ToHaveCountAsync(0);
    }
}
