using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The known-empty pool: filters the page has just reported matching nothing
/// must darken Start with their own hint — not leave it live to dead-end in
/// the no-match outcome (the live dogfooding find folded into the halheinrich/backgammon#83
/// rebuild). Since halheinrich/backgammon#262 the zero count itself is the
/// non-dismissible warning box <c>#noMatchNotice</c> — the only thing on the
/// page saying why Start is dark — pinned here in a real browser and at the
/// bUnit layer (<c>PageTests.Home_ZeroMatchCount_IsANonDismissibleWarningBox</c>).
/// The click-through banner this suite originally gated (the fourth of the
/// four invisible-to-tests production defects) now says only what the page
/// knows after a Start that found nothing; a zero count never reaches it.
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

        // Contact type is one of the panel's collapsed rows, so open that row
        // the way the user must. The cube fixture is a contact position, so
        // the Race contact-type filter admits nothing.
        await ExpandFacetRowAsync("ContactTypes");
        await Page.GetByLabel("Race", new() { Exact = true }).CheckAsync();
        await ApplyFilterAsync();

        // The page states the empty pool, and Start is dark with the reason —
        // the exact pinned sentence, adjacent to its sibling gate hints.
        await Expect(Page.GetByText("0 decisions match your filters")).ToBeVisibleAsync();
        await Expect(StartButton).ToBeDisabledAsync();
        await Expect(Page.GetByText(
                "No problems match the filters — adjust and re-apply them to enable Start."))
            .ToBeVisibleAsync();

        // Adjust and re-apply — exactly what the hint says — and the page
        // recovers: a non-empty count, the hint gone, Start live.
        await Page.GetByLabel("Race", new() { Exact = true }).UncheckAsync();
        await ApplyFilterAsync();

        await Expect(Page.GetByText(ExpectedText.DecisionsMatchYourFilters(1))).ToBeVisibleAsync();
        await Expect(Page.GetByText("No problems match the filters")).ToHaveCountAsync(0);
        await Expect(StartButton).ToBeEnabledAsync();
    }

    [Fact]
    public async Task PlayerNobodyHas_ShowsTheZeroCountWarningBox_AndADisabledStart()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);

        // A player name no file carries: the filter admits nothing.
        await ExpandFacetRowAsync("Players");
        await Page.Locator("input[aria-describedby='facetHint_Players']").FillAsync("Nobody Anyone Knows");
        await ApplyFilterAsync();

        var box = Page.Locator("#noMatchNotice");
        await Expect(box).ToBeVisibleAsync();
        await Expect(box).ToContainTextAsync(ExpectedText.DecisionsMatchYourFilters(0));
        await Expect(box).ToHaveClassAsync(new Regex(@"\balert-warning\b"));
        await Expect(box.GetByRole(AriaRole.Button)).ToHaveCountAsync(0); // nothing closes it
        await Expect(StartButton).ToBeDisabledAsync();
    }
}
