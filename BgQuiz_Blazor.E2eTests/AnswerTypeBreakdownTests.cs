using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The pre-Start answer-type breakdown (umbrella issue halheinrich/backgammon#35): beside the match
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
/// The four cube rows are named by <c>CubeLabels.Label(CubeClaimPair)</c> in
/// <c>BackgammonDiagram_Lib</c> since halheinrich/backgammon#185, so the
/// literals below are <b>consumer pins by ruling and must not be
/// re-sourced</b>: re-reading them from the label home would turn every one
/// into <c>Label(pair) == Label(pair)</c> and let a re-wording at the producer
/// reach this app's users unseen. See <see cref="E2eTestBase"/>'s copy
/// inventory.
/// </para>
///
/// <para>
/// The pool is two real committed fixtures — one checker play, one cube decision
/// whose best pair is (NoDouble, Take) — so three of the five answer types are
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
        await Expect(Page.GetByText(ExpectedText.AnswerTypeHeading)).ToHaveCountAsync(0);

        await ApplyFilterAsync();

        // The count line's own semantics are unchanged — still decisions, still
        // filter-only — and the breakdown sits with it.
        await Expect(body).ToContainTextAsync(ExpectedText.DecisionsMatchYourFilters(2));
        await Expect(body).ToContainTextAsync(ExpectedText.AnswerTypeHeading);

        // The two answer types this folder holds…
        await Expect(body).ToContainTextAsync(ExpectedText.AnswerTypeCount(ExpectedText.CheckerPlaysType, 1));
        await Expect(body).ToContainTextAsync(ExpectedText.AnswerTypeCount(ExpectedText.NoDoublePill, 1));

        // …and the three it holds none of, on screen and reading zero. Absent
        // rows would leave a collection of nothing but takes looking complete.
        // Too good is one row — its pass side, which reads as the claim alone
        // — since SPEC-scoring §3's 2026-09-02 amendment retired the take side
        // as a verdict (halheinrich/backgammon#187); the absence of that row is
        // pinned below, in the joined form the label home would give it.
        await Expect(body).ToContainTextAsync("Double / Take: 0");
        await Expect(body).ToContainTextAsync("Double / Pass: 0");
        await Expect(body).ToContainTextAsync(ExpectedText.AnswerTypeCount(ExpectedText.TooGoodPill, 0));
        await Expect(body).Not.ToContainTextAsync("Too good / Take");
    }

    [Fact]
    public async Task ATooGoodToDoubleTakePositionCountsUnderNoDoubleTake_ByRuling()
    {
        // The position XG labels "Too good to double/Take" on a real file: under
        // the halheinrich/backgammon#86 claim vocabulary it counted under a row
        // of its own; SPEC-scoring §3's 2026-09-02 amendment
        // (halheinrich/backgammon#187) rules it a (NoDouble, Take) — Too Good
        // requires the pass, and the opponent takes — so it lands in the
        // No double row, and no too-good row counts it.
        await BootHomeAsync();
        await PickFixturesAsync(CheckerFixture, TooGoodTakeFixture);
        await ApplyFilterAsync();

        var body = Page.Locator("body");
        await Expect(body).ToContainTextAsync(ExpectedText.DecisionsMatchYourFilters(2));
        await Expect(body).ToContainTextAsync(ExpectedText.AnswerTypeCount(ExpectedText.NoDoublePill, 1));
        await Expect(body).ToContainTextAsync(ExpectedText.AnswerTypeCount(ExpectedText.TooGoodPill, 0));
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
        await Expect(status).ToContainTextAsync(ExpectedText.AnswerTypeHeading);
        await Expect(status).ToContainTextAsync(ExpectedText.AnswerTypeCount(ExpectedText.CheckerPlaysType, 1));
    }
}
