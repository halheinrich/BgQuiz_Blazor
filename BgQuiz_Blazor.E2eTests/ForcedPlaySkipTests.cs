using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// A forced checker play never reaches the user (umbrella issue
/// <c>halheinrich/backgammon#140</c>, beta feedback). A position whose roll
/// admits exactly one legal play poses no question, so the controller passes
/// over it silently — the same treatment a pass position has always had.
///
/// <para>
/// <b>Why this scenario is owed a browser.</b> The skip is derived from the
/// record's own board and dice through real move generation, on a record the
/// real parser produced; nothing short of picking a real file puts a genuinely
/// forced position into a real quiz stream. The pairing is what makes the
/// assertion sharp: two matching decisions go in, exactly one comes out, and
/// the one that comes out is the one that asks something.
/// </para>
///
/// <para>
/// Deliberately independent of enumeration order — the source's file order is
/// not a contract this suite may lean on. Whichever slot the forced position
/// occupies, the user answers one problem and the run ends at one problem
/// shown, so the scenario reads the same either way.
/// </para>
/// </summary>
public sealed class ForcedPlaySkipTests : E2eTestBase
{
    public ForcedPlaySkipTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    [Fact]
    public async Task ForcedPlayIsPassedOver_AndTheQuizLandsOnTheRealDecision()
    {
        await BootHomeAsync();
        await PickFixturesAsync(ForcedFixture, CheckerFixture);
        await ApplyFilterAsync();

        var body = Page.Locator("body");

        // Both decisions match: the skip is a presentation-time rule, so the
        // pre-Start count still counts the forced position (the stream-slot
        // convention). Pinned here because it is the half a reader is most
        // likely to assume the other way.
        await Expect(body).ToContainTextAsync("2 problem files");
        await Expect(body).ToContainTextAsync("2 decisions match your filters");

        await StartQuizAsync();

        // The quiz opened on a decision that can be answered — the checker
        // fixture's 6-5, whose best play is 24/13 — not on the forced position.
        // Keyed on completing that play, so it cannot pass against some other
        // board: the entry only accepts these clicks on this position.
        await Expect(Page.Locator(".board-container .bg-play-entry")).ToBeVisibleAsync();
        await ClickBoardPointAsync(24);
        await ClickBoardPointAsync(18);
        await Expect(SubmitButton).ToBeEnabledAsync();
        await SubmitButton.ClickAsync();
        await Expect(VerdictBand).ToContainTextAsync("Correct — you found the best play.");

        // And there is nothing after it: continuing consumes the forced slot
        // without ever showing it, so the run ends having shown one problem of
        // the two that matched.
        await ContinueToDoneAsync();
        await Expect(Page.GetByText("Total problems shown: 1")).ToBeVisibleAsync();
    }
}
