using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Quiz navigation in a real browser (<c>SPEC-quiz-history.md</c>, the
/// navigation leg of halheinrich/backgammon#8): ⏮ ◀ ▶ ⏭ on the published app,
/// from committed fixtures. Each walk keeps to a pool of cube positions, so it
/// does not depend on which one the source serves first — every problem is
/// answerable by the same gesture. Normal view throughout, so the score panel —
/// the skip count and the problem number — stays on screen while answering.
/// </summary>
public sealed class NavigationTests : E2eTestBase
{
    public NavigationTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright, output) { }

    private async Task StartTwoCubeProblemsInNormalViewAsync()
    {
        await BootHomeAsync();
        await DisableMaximizeAsync();
        await BootHomeAsync();
        await PickCubeProblemsAsync(2);
        await ExpectFilterInEffectAsync();
        await StartQuizAsync();
        await ExpectProblemNumberAsync(1);
    }

    [Fact]
    public async Task DeferGoBackAndAnswerLive_TheSkipCountDrops_AndTheAnswerCounts()
    {
        // §3: "A deferred problem answered live becomes answered" — counted once,
        // in the score, and the session skip count drops.
        await StartTwoCubeProblemsInNormalViewAsync();
        await Expect(Page.GetByText(ExpectedText.Skipped(0))).ToBeVisibleAsync();

        await NavButton(ExpectedText.SkipButton).ClickAsync();

        await ExpectProblemNumberAsync(2);
        await Expect(Page.GetByText(ExpectedText.Skipped(1))).ToBeVisibleAsync();

        await NavButton(ExpectedText.BackButton).ClickAsync();

        await ExpectProblemNumberAsync(1);
        await Expect(NavButton(ExpectedText.NextButton)).ToBeEnabledAsync();   // behind the frontier: Next
        await Expect(NavButton(ExpectedText.SkipButton)).ToHaveCountAsync(0);

        await AnswerCubeNoDoubleAsync();

        await Expect(VerdictBand).Not.ToContainTextAsync("Practice");
        await Expect(Page.GetByText(ExpectedText.Skipped(0))).ToBeVisibleAsync();
        await Expect(Page.GetByText(ExpectedText.Submitted(1))).ToBeVisibleAsync();
    }

    [Fact]
    public async Task BackOnTheFirstProblemsSolution_ReopensItsQuestion_AndTheReanswerIsPractice()
    {
        // §2, amended 2026-10-25 (halheinrich/backgammon#365): on a solution
        // view ◀ lands on that same problem's decision, so even the first
        // problem's answer can be checked again; the re-answer is practice and
        // the answer of record stands.
        await StartTwoCubeProblemsInNormalViewAsync();
        await AnswerCubeNoDoubleAsync();
        await Expect(VerdictBand).Not.ToContainTextAsync("Practice");
        var scorePanel = Page.Locator(".score-panel");
        await Expect(scorePanel).ToContainTextAsync(ExpectedText.Submitted(1));
        var recorded = await scorePanel.InnerTextAsync();
        await Expect(NavButton(ExpectedText.BackButton)).ToBeEnabledAsync();
        await Expect(NavButton(ExpectedText.GoToFirstButton)).ToBeEnabledAsync();

        await NavButton(ExpectedText.BackButton).ClickAsync();

        await ExpectProblemNumberAsync(1);
        await Expect(SubmitButton).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ContinueButton })).ToHaveCountAsync(0);
        await Expect(NavButton(ExpectedText.BackButton)).ToBeDisabledAsync();   // answering the first problem

        await AnswerCubeAsync(ExpectedText.DoublePassPill);

        await Expect(VerdictBand).ToHaveTextAsync(new Regex("^" + Regex.Escape(ExpectedText.PracticePrefix)));
        await Expect(scorePanel).ToHaveTextAsync(recorded);
    }

    [Fact]
    public async Task GoToLast_LandsOnTheFrontier_AndIsUnavailableThere()
    {
        // §2: ⏭ "goes to the frontier and is unavailable there". From the first
        // problem, behind a frontier the user has moved on to.
        await StartTwoCubeProblemsInNormalViewAsync();
        await NavButton(ExpectedText.SkipButton).ClickAsync();
        await ExpectProblemNumberAsync(2);
        await NavButton(ExpectedText.GoToFirstButton).ClickAsync();
        await ExpectProblemNumberAsync(1);
        await Expect(NavButton(ExpectedText.GoToFirstButton)).ToBeDisabledAsync();
        await Expect(NavButton(ExpectedText.BackButton)).ToBeDisabledAsync();

        await NavButton(ExpectedText.GoToLastButton).ClickAsync();

        await ExpectProblemNumberAsync(2);
        await Expect(NavButton(ExpectedText.GoToLastButton)).ToBeDisabledAsync();
        await Expect(NavButton(ExpectedText.SkipButton)).ToBeEnabledAsync();      // the unresolved frontier
        await Expect(Page.GetByText(ExpectedText.Skipped(1))).ToBeVisibleAsync(); // nothing moved the count
    }
}
