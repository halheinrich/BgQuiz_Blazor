using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The pre-Start match count read as a <i>deduplicated</i> count (umbrella
/// issue <c>halheinrich/backgammon#104</c>). The count has always been one item
/// per distinct position — the source stack dedupes beneath every quiz mode
/// (issue #84) — but the screen said nothing about it, so a user reading "N
/// problem file(s)" and a smaller match count on the same page subtracted the
/// two and reported a bug.
///
/// <para>
/// <b>Why this scenario is owed a browser.</b> The collapse is a property of
/// the real parse: content-identical files carry distinct, file-relative
/// decision ids, so nothing short of parsing two copies produces the state
/// under test. Staging one committed fixture under several names is the
/// smallest thing that does — and it puts the two disagreeing numbers on one
/// screen exactly as the report did.
/// </para>
///
/// <para>
/// The sentences are pinned here as independent literals, per this suite's
/// copy-pin split: the wiring (that the magnitude is the stack's own telemetry
/// and not a subtraction the page performs) is asserted in bUnit and against a
/// real composition in <c>PositionDedupeTests</c>, where a test can reach the
/// types. Both grammatical forms are pinned, because both are sentences a user
/// reads.
/// </para>
/// </summary>
public sealed class DeduplicatedCountTests : E2eTestBase
{
    public DeduplicatedCountTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    [Fact]
    public async Task DuplicatedFilesCollapse_AndTheCountAccountsForWhatItLeftOut()
    {
        await BootHomeAsync();
        // Three copies of one position: the count will read 1, three files are
        // on screen, and two records were dropped.
        await PickDuplicatedFixtureAsync(CheckerFixture, copies: 3);
        await ApplyFilterAsync();

        var body = Page.Locator("body");

        // The gap the report was about, both numbers visible at once.
        await Expect(body).ToContainTextAsync("3 problem files");
        await Expect(body).ToContainTextAsync("1 decision matches your filters");

        // The standing rule, then the magnitude — 1 + 2 accounts for all three.
        await Expect(body).ToContainTextAsync("Repeated positions are counted once.");
        await Expect(body).ToContainTextAsync("That left out 2 more matching decisions.");
    }

    [Fact]
    public async Task ASingleCollapsedCopy_ReadsInTheSingular()
    {
        await BootHomeAsync();
        await PickDuplicatedFixtureAsync(CheckerFixture, copies: 2);
        await ApplyFilterAsync();

        var body = Page.Locator("body");

        await Expect(body).ToContainTextAsync("1 decision matches your filters");
        await Expect(body).ToContainTextAsync("That left out 1 more matching decision.");
    }
}
