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

    /// <summary>
    /// The filter panel's equity-error lower bound — the field these scenarios
    /// dirty the filter with (<c>halheinrich/backgammon#157</c>).
    ///
    /// <para>
    /// By <b>id</b>, which the panel carries for exactly this. Both of its range
    /// facets — equity error and move number — label their boxes "Min" and
    /// "Max", so a placeholder match names a pair of fields and not a field;
    /// this was <c>GetByPlaceholder("Min").First</c>, which resolved by whichever
    /// section renders first rather than by which facet was meant. It happened to
    /// land here, and would have moved to the move-number box the day the panel
    /// reordered its sections or the collapsed half opened — silently, since
    /// these tests only ever assert on the gate the edit trips, never on the
    /// value that tripped it.
    /// </para>
    ///
    /// <para>
    /// Which facet is meant is not arbitrary: the value filled below is
    /// <c>0.05</c>, an equity in a field whose step is 0.001. The move-number
    /// box is <c>step="1" min="1"</c>, where the same string is not a bound at
    /// all.
    /// </para>
    /// </summary>
    private ILocator ErrorMinField => Page.Locator("#errorMin");

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
        await ErrorMinField.FillAsync("0.05");
        await Expect(StartButton).ToBeDisabledAsync();
        await Expect(MixApplies).ToBeDisabledAsync();
        await Expect(Page.GetByText("the mix draws its problems from the filtered pool"))
            .ToBeVisibleAsync();

        // Undo the edit: the panel reports clean, the applied filter is back
        // in effect, and the gate reopens — one gesture, no wedge. (The
        // re-apply recovery path is pinned at the bUnit layer, where the
        // corpus is fake and the pool cannot empty underneath the assertion.)
        await ErrorMinField.FillAsync("");
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
        await ErrorMinField.FillAsync("0.05");
        await Expect(StartButton).ToBeDisabledAsync(); // the filter's own gate
        await Expect(MixApplies).ToBeEnabledAsync();   // but uncheck is still live

        await MixApplies.UncheckAsync();
        await Expect(MixApplies).Not.ToBeCheckedAsync();

        // And Clear mix still works here too — rows removed, box untouched.
        await Page.Locator("#mixClear").ClickAsync();
        await Expect(Page.Locator(".mix-row")).ToHaveCountAsync(0);
    }
}
