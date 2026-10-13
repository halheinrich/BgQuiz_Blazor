using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// A Start made from <c>Home</c> while a quiz is still running, and the
/// Restart of the quiz it started (<c>SPEC-quiz-history.md</c> §8, "Starting a
/// run: prepare, then commit"; halheinrich/backgammon#325 item 1).
///
/// <para>
/// §8's faults are pinned in bUnit and against the controller, where a fault
/// can be injected; the shipped app has no fault seam, so this journey drives
/// the path every user takes — the attempt that commits. Over a quiz that is
/// still active, the new quiz must become the current one, with its first
/// problem on screen, and the stored filters a Restart replays must be the new
/// quiz's, not the old one's. Worth a browser pass because the pieces meet only
/// in the real app: Home's filter setup, the controller's switch, Done's
/// Restart, and the in-app navigation that keeps them all standing.
/// </para>
///
/// <para>
/// <b>The two quizzes are told apart by kind.</b> One folder holds a cube
/// decision and a checker play; the first quiz filters to cube decisions and
/// the second to checker plays, so the answering prompt on screen — the
/// verdict band's, read in the normal view — names which quiz is running.
/// </para>
/// </summary>
public sealed class StartMidQuizTests : E2eTestBase
{
    public StartMidQuizTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright, output) { }

    /// <summary>The answering prompt for a cube decision (<c>Quiz.razor.cs</c>, <c>StatusText</c>).</summary>
    private const string CubePrompt = "Pick the cube decision, then Submit.";

    /// <summary>The answering prompt for a checker play (<c>Quiz.razor.cs</c>, <c>StatusText</c>).</summary>
    private const string CheckerPrompt = "Click the board to build your play, then Submit.";

    private ILocator HomeNavLink => Page.GetByRole(AriaRole.Link, new() { Name = ExpectedText.HomeNavLink });

    private ILocator BackToQuizButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.BackToQuizButton });

    private ILocator EndQuizButton => Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.EndQuizButton });

    private ILocator RestartButton => Page.GetByRole(AriaRole.Button, new() { Name = "Restart with same filters" });

    /// <summary>The decision-type filter's option labelled <paramref name="label"/>, as the filter panel spells it.</summary>
    private ILocator DecisionType(string label) => Page.GetByRole(AriaRole.Radio, new() { Name = label, Exact = true });

    [Fact]
    public async Task AStartMidQuiz_RunsTheNewQuiz_AndDonesRestartReplaysTheNewQuizsFilters()
    {
        // The normal view, so the verdict band's prompt is on screen while
        // answering (maximize, the default, hides it).
        await BootHomeAsync();
        await DisableMaximizeAsync();
        await HomeNavLink.First.ClickAsync();
        await ExpectPageRenderedAsync(AppRoute.Home);
        await PickFixturesAsync(CubeFixture, CheckerFixture);

        // The first quiz: cube decisions only.
        await ExpandFacetRowAsync("DecisionType");
        await DecisionType("Cube decisions only").CheckAsync();
        await ApplyFilterAsync();
        await StartQuizAsync();
        await ExpectProblemNumberAsync(1);
        await Expect(VerdictBand).ToHaveTextAsync(CubePrompt);

        // Home mid-quiz, the first quiz still active and offered back.
        await HomeNavLink.ClickAsync();
        await ExpectPageRenderedAsync(AppRoute.Home);
        await Expect(BackToQuizButton).ToBeVisibleAsync();

        // The second quiz: checker plays only. The decision-type row is still
        // open — the panel restores its open rows on every mount — so it is
        // waited for, never toggled again.
        await WaitForFilterRestorationAsync();
        await Expect(Page.Locator("#facetToggle_DecisionType")).ToHaveAttributeAsync("aria-expanded", "true");
        await DecisionType("Checker plays only").CheckAsync();
        await ApplyFilterAsync();
        await StartQuizAsync();

        // The new quiz is current, its first problem on screen.
        await ExpectProblemNumberAsync(1);
        await Expect(VerdictBand).ToHaveTextAsync(CheckerPrompt);

        await EndQuizButton.ClickAsync();
        await ExpectPageRenderedAsync(AppRoute.Done);

        // Restart replays the second quiz's filters: a checker play again,
        // where the first quiz's would have brought the cube decision.
        await RestartButton.ClickAsync();
        await ExpectPageRenderedAsync(AppRoute.Quiz);
        await ExpectProblemNumberAsync(1);
        await Expect(VerdictBand).ToHaveTextAsync(CheckerPrompt);
    }
}
