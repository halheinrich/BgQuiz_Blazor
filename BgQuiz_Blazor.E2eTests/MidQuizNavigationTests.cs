using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Leaving a running quiz for <c>Home</c> and coming back (issue #58).
///
/// <para>
/// The round trip itself always worked — the controller and every holder are
/// app-scoped, so an in-app navigation costs nothing — but <c>Home</c> was the
/// last page reachable mid-quiz with no way back on it, after <c>Help</c> and
/// <c>Settings</c> grew theirs. This drives the affordance a user actually has.
/// </para>
///
/// <para>
/// Worth a browser pass rather than bUnit alone, because two of the three claims
/// exist only in a real runtime: that a nav-menu click is an <i>enhanced</i>
/// navigation which leaves the WASM runtime (and with it the quiz) standing, and
/// that <c>Home</c>'s boot-time reload notice correctly reads a live controller
/// as "navigated back", not "reloaded". A bUnit render sees neither.
/// </para>
/// </summary>
public sealed class MidQuizNavigationTests : E2eTestBase
{
    public MidQuizNavigationTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    /// <summary>Home's way back into a running quiz — absent when none is.</summary>
    private ILocator BackToQuizButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = "Back to quiz" });

    private ILocator HomeNavLink => Page.GetByRole(AriaRole.Link, new() { Name = "Home" });

    [Fact]
    public async Task HomeOffersTheWayBackMidQuiz_AndTheRunSurvivesTheRoundTrip()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();
        await Expect(Page.GetByText("Problem 1")).ToBeVisibleAsync();

        // Out to Home mid-quiz, the way a user gets there: the nav menu.
        await HomeNavLink.ClickAsync();
        await ExpectUrlAsync("/");
        await Expect(PickFolderButton).ToBeVisibleAsync();

        // The quiz is still live, so this is a navigation and not a reset —
        // Home's reload notice must stay silent (its HasStarted guard is the
        // discriminator, and this is the case that exercises it).
        await Expect(ReloadNotice).ToHaveCountAsync(0);

        await BackToQuizButton.ClickAsync();
        await ExpectUrlAsync("/quiz");

        // Back on the same problem, with the run intact: nothing was answered,
        // skipped or restarted by the trip.
        await Expect(Page.GetByText("Problem 1")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Radio, new() { Name = "No double" })).ToBeVisibleAsync();
    }

    /// <summary>
    /// The other half of the predicate, on the page a first-time visitor lands
    /// on: with no quiz running there is nowhere to go back to, so the button is
    /// absent — Home does not redirect, and offers no dead affordance either.
    /// </summary>
    [Fact]
    public async Task HomeOffersNoWayBackWhenNoQuizIsRunning()
    {
        await BootHomeAsync();

        await Expect(BackToQuizButton).ToHaveCountAsync(0);
    }
}
