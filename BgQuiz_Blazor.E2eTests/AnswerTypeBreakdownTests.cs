using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The pre-Start answer-type breakdown (umbrella issue #35): beside the match
/// count, Home says what the matched pool is <i>made of</i> — the curation-bias
/// check a beta tester asked for after suspecting his collection was mostly
/// takes.
///
/// <para>
/// The wording is pinned here as independent literals in the published app, per
/// the copy-pin split: the wiring — that the count and the buckets are two
/// renderings of one <c>AnswerTypeDistribution</c> — is asserted in bUnit, where
/// a test can reach the types. A unit test comparing the page against the same
/// label constants the page reads would agree with any wording at all, so the
/// labels a user actually reads are pinned only here.
/// </para>
///
/// <para>
/// The pool is two real committed fixtures — one checker play, one cube decision
/// whose best pair is No Double / Take — so four of the six answer types are
/// genuinely absent. That is the scenario the feature exists for: the zeros are
/// the finding, and a breakdown that quietly listed only what it found would
/// report a lopsided collection as a balanced one.
/// </para>
/// </summary>
public sealed class AnswerTypeBreakdownTests : E2eTestBase
{
    public AnswerTypeBreakdownTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    [Fact]
    public async Task AppliedFiltersReportThePoolsAnswerTypesIncludingTheEmptyOnes()
    {
        await BootHomeAsync();
        await PickFixturesAsync(CheckerFixture, CubeFixture);

        // Nothing is claimed about a pool before the user has applied a filter to
        // define one: the breakdown arrives with the count, not before it.
        var body = Page.Locator("body");
        await Expect(Page.GetByText("By answer type")).ToHaveCountAsync(0);

        await ApplyFilterAsync();

        // The count line's own semantics are unchanged — still decisions, still
        // filter-only — and the breakdown sits with it.
        await Expect(body).ToContainTextAsync("2 decisions match your filters");
        await Expect(body).ToContainTextAsync("By answer type");

        // The two answer types this folder holds…
        await Expect(body).ToContainTextAsync("Checker plays: 1");
        await Expect(body).ToContainTextAsync("No double / take: 1");

        // …and the four it holds none of, on screen and reading zero. Absent
        // rows would leave a collection of nothing but takes looking complete.
        // Too good is two rows since the claim vocabulary
        // (halheinrich/backgammon#86): its take side was uncountable before.
        await Expect(body).ToContainTextAsync("Double / take: 0");
        await Expect(body).ToContainTextAsync("Double / pass: 0");
        await Expect(body).ToContainTextAsync("Too good / pass: 0");
        await Expect(body).ToContainTextAsync("Too good / take: 0");
    }

    [Fact]
    public async Task ATooGoodTakePositionCountsUnderItsOwnRowNotUnderNoDoubleTake()
    {
        // The landing the split exists for, on a real file: before the claim
        // vocabulary the too-good-and-take fixture read as No double / take
        // (the identical board action), and a collection of such positions
        // looked like a collection of takes. Now it is its own row, and the No
        // double / take row reads zero.
        await BootHomeAsync();
        await PickFixturesAsync(CheckerFixture, TooGoodTakeFixture);
        await ApplyFilterAsync();

        var body = Page.Locator("body");
        await Expect(body).ToContainTextAsync("2 decisions match your filters");
        await Expect(body).ToContainTextAsync("Too good / take: 1");
        await Expect(body).ToContainTextAsync("No double / take: 0");
    }

    /// <summary>
    /// The breakdown is announced with the count, not merely printed near it: a
    /// screen-reader user who is told "2 decisions match" and nothing else has
    /// the number without the fact it was added to give. Scoped to the polite
    /// region Home already uses for the count and its mix caveat.
    /// </summary>
    [Fact]
    public async Task TheBreakdownRidesInsideTheCountsPoliteStatusRegion()
    {
        await BootHomeAsync();
        await PickFixturesAsync(CheckerFixture, CubeFixture);
        await ApplyFilterAsync();

        var status = Page.Locator("[role=status]")
                         .Filter(new() { HasText = "decisions match your filters" });

        await Expect(status).ToHaveCountAsync(1);
        await Expect(status).ToContainTextAsync("By answer type");
        await Expect(status).ToContainTextAsync("Checker plays: 1");
    }
}
