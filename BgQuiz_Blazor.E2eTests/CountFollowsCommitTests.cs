using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The match count follows a commit — Apply, Clear, or the restoration settling the empty
/// selection — and never typing (halheinrich/backgammon#421, the ratified plan: "the count
/// updates on Apply, not as you type"). The one edit that changes what is in effect without a
/// commit is the one that empties an applied filter: the empty selection is then in effect
/// (Start stays available), nothing is counted, and Home says so until Clear or Apply.
/// </summary>
public sealed class CountFollowsCommitTests : E2eTestBase
{
    public CountFollowsCommitTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright, output) { }

    private ILocator ErrorMin => Page.Locator("#errorMin");
    private ILocator ClearFilters => Page.Locator("#clearFilters");
    private ILocator CountFollowsCommit => Page.Locator("#countFollowsCommit");
    private ILocator ACountLine => Page.GetByText("your filters");

    [Fact]
    public async Task EmptyingAnAppliedFilter_CountsNothing_UntilClear()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ExpectFilterInEffectAsync();
        await Expect(Page.GetByText(ExpectedText.DecisionsMatchYourFilters(1))).ToBeVisibleAsync();

        // A filter applied: counted (whatever it admits of the one decision).
        await ErrorMin.PressSequentiallyAsync("0.5");
        await ErrorMin.PressAsync("Tab");
        await ApplyFilterAsync();
        await Expect(ACountLine).ToBeVisibleAsync();
        await Expect(CountFollowsCommit).ToHaveCountAsync(0);

        // The criterion erased: the empty selection is in effect with no commit, so no count
        // runs; Start stays available, and Home says what the count waits for.
        await ErrorMin.PressAsync("Control+a");
        await ErrorMin.PressAsync("Backspace");
        await ErrorMin.PressAsync("Tab");
        await Expect(CountFollowsCommit).ToBeVisibleAsync();
        await Expect(ACountLine).ToHaveCountAsync(0);
        await Expect(StartButton).ToBeEnabledAsync();

        // Clear commits the empty selection: counted.
        await ClearFilters.ClickAsync();
        await Expect(Page.GetByText(ExpectedText.DecisionsMatchYourFilters(1))).ToBeVisibleAsync();
        await Expect(CountFollowsCommit).ToHaveCountAsync(0);
    }
}
