using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The reload-reset honesty notice: a full reload reboots the WASM runtime and
/// silently discards a live quiz, and the one thing that survives — the
/// <c>sessionStorage</c> quiz-live marker — lets the next boot say so, once.
/// These scenarios pin the notice for both ways a quiz becomes live: a fresh
/// Start, and Done's Restart (the one-click-wide gap a follow-up commit closed;
/// pinned here forever).
/// </summary>
public sealed class ReloadNoticeTests : E2eTestBase
{
    public ReloadNoticeTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    [Fact]
    public async Task ReloadMidQuiz_ShowsOneShotResetNotice()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CheckerFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();

        // A full reload mid-quiz: the fresh boot finds no quiz for /quiz, lands
        // on Home, and Home announces the reset.
        await Page.ReloadAsync();
        await Expect(ReloadNotice).ToBeVisibleAsync();
        await ExpectUrlAsync("/");

        // One-shot: showing the notice cleared the marker, so the next reload
        // boots clean. The re-pick round-trip is an ordering guard for the
        // negative assertion — by the time the picked-folder summary has rendered,
        // the boot lifecycle that would have shown the notice has completed.
        await Page.ReloadAsync();
        await Expect(PickFolderButton).ToBeVisibleAsync();
        await PickFixtureAsync(CheckerFixture);
        await Expect(ReloadNotice).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task RestartFromDone_ThenReload_ShowsResetNotice()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();

        // Restart makes a quiz live again — reaching Done cleared the marker,
        // and Restart must re-set it, or a reload during the restarted quiz
        // falls back to the old silent reset.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Restart with same filters" }).ClickAsync();
        await ExpectUrlAsync("/quiz");

        await Page.ReloadAsync();
        await Expect(ReloadNotice).ToBeVisibleAsync();
    }
}
