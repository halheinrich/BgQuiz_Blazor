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
    public async Task MixEdit_SurvivesInAppNavigation_GatedUntilApplied()
    {
        // The derived-dirtiness architecture's headline surface, in a real
        // browser: the mix draft is app-scoped, so an uncommitted edit survives
        // in-app navigation (client-side routing — the WASM runtime and its
        // Scoped services live on; a full reload is the separate, reset story).
        // This supersedes finding (AK)'s letter — the old remount came back
        // BLANK and had to un-gate a stale flag; now the edit itself comes
        // back, still gating Start, with Apply as the visible way out. Gated,
        // never wedged.
        await BootHomeAsync();
        await PickFakeFolderAsync();
        await ApplyFilterAsync();
        await AddDefaultMixRowAsync(); // an uncommitted edit — Start gates

        await Expect(StartButton).ToBeDisabledAsync();
        await Expect(Page.GetByText("Apply or reset the mix above to enable Start"))
            .ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Help" }).ClickAsync();
        await ExpectUrlAsync("/help");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Home" }).ClickAsync();
        await ExpectUrlAsync("/");

        // The edit is still on screen and still gating; the filter half also
        // survived (Scoped holder), so the hint is the mix's specifically.
        await Expect(Page.Locator(".mix-row")).ToHaveCountAsync(1);
        await Expect(StartButton).ToBeDisabledAsync();
        await Expect(Page.GetByText("Apply or reset the mix above to enable Start"))
            .ToBeVisibleAsync();
        await Expect(Page.Locator("#mixApply")).ToBeEnabledAsync(); // the way out

        await ApplyMixAsync();
        await Expect(StartButton).ToBeEnabledAsync();
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
/// The weighted-start refusal ruling at its post-X reachable path. Task X
/// offers the mix <i>only</i> for an Enabled (File System Access) pick — a
/// stats-less pick hides the panel entirely — so a committed mix can meet
/// absent stats in exactly one way: an Enabled pick whose existing
/// <c>bgquiz-stats.json</c> is unreadable. The capability peek passes (write was
/// granted), but the bind reads no document, so Start is refused with the
/// actionable notice and the one-click override runs the quiz unweighted —
/// never a silent unweighted substitution. Rides the FS-Access fake with a
/// corrupt stats file injected per scenario (the old fallback-pick refusal is
/// no longer reachable through the UI).
/// </summary>
public sealed class MixRefusalTests : FsAccessFakeTestBase
{
    public MixRefusalTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    [Fact]
    public async Task EnabledPickUnreadableStats_WithCommittedMix_Refuses_OverrideRunsUnweighted()
    {
        // An existing stats file the converter must reject: the capability peek
        // still passes (write granted), but the bind reads no document, so a
        // committed mix is refused at the stage-2 check.
        await Page.AddInitScriptAsync("window.__statsFake.statsJson = 'not json at all';");

        await BootHomeAsync();
        await PickFakeFolderAsync();
        await ApplyFilterAsync();
        await AddDefaultMixRowAsync();
        await ApplyMixAsync();

        // The pick CAN provide stats (Enabled), so there is no early advisory —
        // the unreadable file is discovered only at Start.
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
