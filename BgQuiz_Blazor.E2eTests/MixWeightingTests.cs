using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The stats-weighted mix, end to end over the FS-Access fake: a weighted
/// start composing from a real lifetime-stats read, and the composed-to-zero
/// outcome — seeded by feeding the app's <i>own</i> captured stats write back
/// as the pre-existing file, so the scenario never hand-crafts the wire format
/// (and stays agnostic to the decision-id encoding).
/// </summary>
public sealed class MixWeightingTests : FsAccessFakeTestBase
{
    public MixWeightingTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    [Fact]
    public async Task WeightedStart_NeverSeenMix_ComposesAndRunsToDone()
    {
        // No existing stats file: the fixture's decision is never-seen, so a
        // 100% never-seen mix composes exactly it and the quiz runs to Done —
        // the weighted pipeline (mix UI → holder → controller → composing
        // decorator over the real stats bind) exercised end to end.
        await BootHomeAsync();
        await PickFakeFolderAsync();
        await ApplyFilterAsync();
        await AddDefaultMixRowAsync();
        await ApplyMixAsync();

        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();
        await Expect(Page.GetByText("Total problems shown: 1")).ToBeVisibleAsync();

        // The weighted run still records: one fold, one write-back.
        Assert.Single(await CapturedWritesAsync());
    }

    [Fact]
    public async Task WeightedStart_EverythingAlreadySeen_ComposesToZero_MixNoticeStaysHome()
    {
        // Quiz 1 (blank mix) folds the one decision into the stats file; its
        // captured write becomes the pre-existing file for the next bind. A
        // 100% never-seen mix then has an empty pool — the start stays on
        // Home behind the mix-aware zero notice (the composed-to-zero sibling
        // of the filtered-to-zero banner), not a 0/0 bounce.
        await BootHomeAsync();
        await PickFakeFolderAsync();
        await ApplyFilterAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();

        await Page.EvaluateAsync(
            "() => { window.__statsFake.statsJson = window.__statsFake.writes[0]; }");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Back to setup" }).ClickAsync();
        await ExpectUrlAsync("/");
        await AddDefaultMixRowAsync();
        await ApplyMixAsync();

        await Expect(StartButton).ToBeEnabledAsync();
        await StartButton.ClickAsync();

        await Expect(Page.GetByText("Your mix drew no problems")).ToBeVisibleAsync();
        await ExpectUrlAsync("/"); // stayed on Home — no 0/0 /quiz → /done bounce
    }
}

/// <summary>
/// The no-stats refusal ruling, end to end on the fallback rung: a
/// <c>webkitdirectory</c> pick can never provide lifetime stats, so a
/// committed mix draws the early advisory, Start is refused with the
/// actionable notice, and the one-click override runs the quiz unweighted —
/// never a silent unweighted substitution. No FS-Access fake here: like the
/// migrated flow scenarios, this drives the app's real fallback pick path.
/// </summary>
public sealed class MixRefusalTests : E2eTestBase
{
    public MixRefusalTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    [Fact]
    public async Task FallbackPick_WithCommittedMix_AdvisesThenRefuses_OverrideRunsUnweighted()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture); // read-only by construction — no stats rung
        await ApplyFilterAsync();
        await AddDefaultMixRowAsync();
        await ApplyMixAsync();

        // Signal early: the advisory renders before any Start attempt.
        await Expect(Page.GetByText("Start will offer to run without the mix")).ToBeVisibleAsync();

        // Gate late: Start is refused with the actionable notice; no navigation.
        await Expect(StartButton).ToBeEnabledAsync();
        await StartButton.ClickAsync();
        await Expect(Page.GetByText("weighted mix can't be applied")).ToBeVisibleAsync();
        await ExpectUrlAsync("/");

        // The one-click per-run escape runs this quiz unweighted, to Done.
        await Page.Locator("#startWithoutMix").ClickAsync();
        await ExpectUrlAsync("/quiz");
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();
        await Expect(Page.GetByText("Total problems shown: 1")).ToBeVisibleAsync();
    }
}
