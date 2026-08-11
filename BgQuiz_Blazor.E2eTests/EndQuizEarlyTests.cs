using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Ending a run before the source is exhausted (issue #57) — the exit a quiz
/// that has served its purpose needs, and the one the app had no control for:
/// until now the only ways out of a live run were answering every problem or
/// abandoning the tab.
///
/// <para>
/// The transition and its scoring live in <c>QuizController</c> and are pinned
/// there. What only a real run can show is the whole gesture landing: that the
/// button is on the page mid-quiz, that one click carries the user to the
/// summary with no confirmation in the way, and that what arrives there is an
/// ordinary finished quiz — the same Done page, saying the same things, over the
/// score of what was actually answered.
/// </para>
/// </summary>
public sealed class EndQuizEarlyTests : E2eTestBase
{
    public EndQuizEarlyTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    private ILocator EndQuizButton => Page.GetByRole(AriaRole.Button, new() { Name = "End quiz" });

    /// <summary>
    /// Quitting on an unanswered problem: the answered work stands, the problem
    /// being looked at is abandoned and counts as a skip, and the run ends.
    /// </summary>
    [Fact]
    public async Task EndingOnAnUnansweredProblemFinishesTheRunWithTheScoreSoFar()
    {
        // Three problems, so the run is unmistakably cut short: one answered,
        // one abandoned, one never reached.
        await BootHomeAsync();
        await PickCubeProblemsAsync(3);
        await ApplyFilterAsync();
        await StartQuizAsync();

        await AnswerCubeNoDoubleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Expect(Page.GetByText("Problem 2")).ToBeVisibleAsync();

        // One click, no confirmation, straight to the summary.
        await EndQuizButton.ClickAsync();
        await ExpectUrlAsync("/done");

        var body = Page.Locator("body");

        // Received as the completed quiz it is — ruled deliberately: no
        // ended-early wording, no second kind of ending.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Quiz complete" }))
            .ToBeVisibleAsync();

        // The partial score: the cube answered scores as two decisions (the
        // double and the take), and the abandoned problem counts among the
        // problems shown, exactly as a skipped one does.
        await Expect(body).ToContainTextAsync("Submitted: 2");
        await Expect(body).ToContainTextAsync("Skipped: 1");
        await Expect(body).ToContainTextAsync("Total problems shown: 2");
    }

    /// <summary>
    /// Quitting while reading a solution: a forward exit, not an abandonment.
    /// The answer was submitted and scored before the click, so it must survive
    /// into the summary rather than being rolled back with the run.
    /// </summary>
    [Fact]
    public async Task EndingWhileReadingASolutionKeepsThatAnswer()
    {
        // Two problems, so ending here is genuinely early: a one-problem pool
        // would make this scenario indistinguishable from finishing the quiz.
        await BootHomeAsync();
        await PickCubeProblemsAsync(2);
        await ApplyFilterAsync();
        await StartQuizAsync();

        await AnswerCubeNoDoubleAsync(); // lands in review, Continue showing

        await EndQuizButton.ClickAsync();
        await ExpectUrlAsync("/done");

        var body = Page.Locator("body");

        // The reviewed answer counted, and nothing was recorded as skipped on
        // top of it — the problem was answered, not abandoned.
        await Expect(body).ToContainTextAsync("Submitted: 2");
        await Expect(body).ToContainTextAsync("Skipped: 0");
        await Expect(body).ToContainTextAsync("Total problems shown: 1");
    }
}
