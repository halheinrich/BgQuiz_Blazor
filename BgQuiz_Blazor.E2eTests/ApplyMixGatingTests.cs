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
    public async Task ApplyMix_IsGatedUntilApplyFilter_AndIsRevokedByALaterFilterEdit()
    {
        await BootHomeAsync();
        // #87: the mix panel is offered only for a folder with a stats history,
        // and this helper leaves exactly the state the gate is about — folder
        // held, stats readable, no filter applied for the current pick.
        await SeedStatsHistoryAsync();

        // A complete, valid one-row mix: from here the only thing that can
        // disable Apply Mix is the host's filter gate. The hint must say *why*,
        // not merely refuse — the bare rule is what read as arbitrary.
        await AddDefaultMixRowAsync();
        await Expect(MixApply).ToBeDisabledAsync();
        await Expect(Page.GetByText("the mix draws its problems from the filtered pool"))
            .ToBeVisibleAsync();

        await ApplyFilterAsync();
        await Expect(MixApply).ToBeEnabledAsync();

        // A later filter edit revokes both gates together — the spec's Fork A,
        // ruled strict: activation reads the filter in effect *now*, the same
        // fact Start reads, so there is no browser state in which the two
        // disagree. (This inverts the earlier ruling that the gate asked
        // "has this corpus been filtered?" and survived edits; that question no
        // longer exists in the model.) Re-applying is the one-gesture recovery,
        // and pinning it here is what keeps the accepted mid-composition
        // friction from being an actual dead end in a real browser.
        await Page.GetByPlaceholder("Min").First.FillAsync("0.05");
        await Expect(StartButton).ToBeDisabledAsync();
        await Expect(MixApply).ToBeDisabledAsync();
        await Expect(Page.GetByText("the mix draws its problems from the filtered pool"))
            .ToBeVisibleAsync();

        await ApplyFilterAsync();
        await Expect(MixApply).ToBeEnabledAsync();
        await Expect(Page.GetByText("the mix draws its problems from the filtered pool"))
            .ToHaveCountAsync(0);

        // Start is still dark here, and for the *mix's* reason — the row added
        // above was never committed. Commit it and the page finally arms: the
        // whole friction Fork A accepts is one re-Apply away from recovery, and
        // nothing about it strands the run.
        await ApplyMixAsync();
        await Expect(StartButton).ToBeEnabledAsync();
    }

    [Fact]
    public async Task GatedApplyMix_LeavesClearMixLive_SoADirtyDraftCanAlwaysBeCleared()
    {
        // Wedge-proofing: a dirty draft gates Start, so if both ways out were
        // sequenced behind the filter a user could reach a state with no
        // visible way forward. Clear mix is ungated in every state.
        await BootHomeAsync();
        // #87: the mix panel is offered only for a folder with a stats history,
        // and this helper leaves exactly the state the gate is about — folder
        // held, stats readable, no filter applied for the current pick.
        await SeedStatsHistoryAsync();
        await AddDefaultMixRowAsync();

        await Expect(MixApply).ToBeDisabledAsync();
        await Expect(StartButton).ToBeDisabledAsync();
        await Expect(Page.Locator("#mixClear")).ToBeEnabledAsync();

        await Page.Locator("#mixClear").ClickAsync();

        await Expect(Page.GetByText("Apply or clear the mix above to enable Start"))
            .ToHaveCountAsync(0);
    }
}
