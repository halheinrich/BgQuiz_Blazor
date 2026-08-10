using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The known-empty pool: filters the page has just reported matching nothing
/// must darken Start with their own hint — not leave it live to dead-end in
/// the no-match outcome (the live dogfooding find folded into the #83
/// rebuild). The click-through banner this suite originally gated (the fourth
/// of the four invisible-to-tests production defects) survives as the
/// backstop for a Start racing the count, pinned at the bUnit layer where the
/// race can be staged; in a real browser the primary path now never reaches
/// it, because the button is genuinely dark.
/// </summary>
public sealed class EmptyFilterBannerTests : E2eTestBase
{
    public EmptyFilterBannerTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    [Fact]
    public async Task RaceFilterAgainstContactPosition_DarkensStart_UntilRelaxedAndReapplied()
    {
        await BootHomeAsync();

        // Pick first: waiting for the picked-file summary also guarantees the
        // filter panel's first-render localStorage restore has settled, so the
        // Race click below cannot be overwritten by a late hydrate.
        await PickFixtureAsync(CubeFixture);

        // Contact type sits behind the panel's "more filters" disclosure, so
        // open it the way the user must. The cube fixture is a contact
        // position, so the Race contact-type filter admits nothing.
        await ExpandMoreFiltersAsync();
        await Page.GetByLabel("Race", new() { Exact = true }).CheckAsync();
        await ApplyFilterAsync();

        // The page states the empty pool, and Start is dark with the reason —
        // the exact pinned sentence, adjacent to its sibling gate hints.
        await Expect(Page.GetByText("0 decisions match your filters")).ToBeVisibleAsync();
        await Expect(StartButton).ToBeDisabledAsync();
        await Expect(Page.GetByText(
                "The filters match no problems — adjust and re-apply them to enable Start."))
            .ToBeVisibleAsync();

        // Adjust and re-apply — exactly what the hint says — and the page
        // recovers: a non-empty count, the hint gone, Start live.
        await Page.GetByLabel("Race", new() { Exact = true }).UncheckAsync();
        await ApplyFilterAsync();

        await Expect(Page.GetByText("1 decision matches your filters")).ToBeVisibleAsync();
        await Expect(Page.GetByText("The filters match no problems")).ToHaveCountAsync(0);
        await Expect(StartButton).ToBeEnabledAsync();
    }
}
