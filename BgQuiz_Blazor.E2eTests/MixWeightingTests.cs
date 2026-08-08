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
    public async Task NoStatsHistory_OffersNoMix_AndTheQuizStillRuns()
    {
        // The gating smoke for issue halheinrich/backgammon#87. A folder with no
        // stats history can't mean a weighted mix, so the section is not offered
        // at all — no panel, no disabled controls, no explanation — and the quiz
        // runs perfectly well without it. This is the state EVERY first-time
        // user of a new folder is in, so it is the path that must not break.
        await BootHomeAsync();
        await PickFakeFolderAsync();

        // Positive precondition first: the setup surface really did disclose, so
        // the absences below are the mix's specifically and not a pick that
        // silently failed.
        await Expect(Page.Locator("#shuffleOrder")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Weighted mix")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#mixApply")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#mixClear")).ToHaveCountAsync(0);

        await ApplyFilterAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();
        await Expect(Page.GetByText("Total problems shown: 1")).ToBeVisibleAsync();

        // Unweighted, and recording — that write is what gives this folder the
        // stats history a mix would later compose from.
        Assert.Single(await CapturedWritesAsync());
    }

    [Fact]
    public async Task WeightedStart_OverASeededHistory_ComposesAndRunsToDone()
    {
        // The weighted pipeline (mix UI → holder → controller → composing
        // decorator over the real stats bind) end to end, now necessarily over a
        // folder that HAS a history — under #87 there is no other kind of folder
        // a mix can be built on. The seeding quiz leaves the one fixture
        // decision seen, so the category has to be one that still reaches it:
        // "Everything else" draws exactly what the rows above it didn't claim,
        // which here is everything.
        await BootHomeAsync();
        await SeedStatsHistoryAsync();
        await ApplyFilterAsync();

        await AddDefaultMixRowAsync();
        await Page.GetByLabel("Category").SelectOptionAsync("EverythingElse");
        await ApplyMixAsync();

        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();
        await Expect(Page.GetByText("Total problems shown: 1")).ToBeVisibleAsync();

        // Two write-backs now: the seeding quiz's fold and this weighted run's.
        Assert.Equal(2, (await CapturedWritesAsync()).Length);
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
        await SeedStatsHistoryAsync(); // #87: no stats history, no mix panel to edit
        await ApplyFilterAsync();
        await AddDefaultMixRowAsync(); // an uncommitted edit — Start gates

        await Expect(StartButton).ToBeDisabledAsync();
        await Expect(Page.GetByText("Apply or clear the mix above to enable Start"))
            .ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Help" }).ClickAsync();
        await ExpectUrlAsync("/help");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Home" }).ClickAsync();
        await ExpectUrlAsync("/");

        // The edit is still on screen and still gating; the filter half also
        // survived (Scoped holder), so the hint is the mix's specifically.
        await Expect(Page.Locator(".mix-row")).ToHaveCountAsync(1);
        await Expect(StartButton).ToBeDisabledAsync();
        await Expect(Page.GetByText("Apply or clear the mix above to enable Start"))
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
/// The weighted-start refusal ruling at the one path issue
/// <c>halheinrich/backgammon#87</c> leaves reachable. The mix is offered only
/// where the shared predicate holds — write capability <i>and</i> a readable
/// stats record — so a committed mix can no longer meet absent stats by the
/// folder simply having none, and it cannot meet an already-corrupt file either
/// (the pick-time probe would have hidden the panel, leaving nothing to commit).
/// What remains, and what the refusal is kept as a backstop for, is a stats file
/// that stops being readable <i>between</i> the pick and the Start: the pick
/// looked capable and the bind then wasn't. Start is refused with the actionable
/// notice and the one-click override runs the quiz unweighted — never a silent
/// unweighted substitution.
/// </summary>
public sealed class MixRefusalTests : FsAccessFakeTestBase
{
    public MixRefusalTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    [Fact]
    public async Task StatsBecomeUnreadableAfterTheMixIsCommitted_Refuses_OverrideRunsUnweighted()
    {
        // Seed a real history so the mix is offered and can be committed at all.
        await BootHomeAsync();
        await SeedStatsHistoryAsync();
        await ApplyFilterAsync();
        await AddDefaultMixRowAsync();
        await Page.GetByLabel("Category").SelectOptionAsync("EverythingElse");
        await ApplyMixAsync();

        // Now the file turns unreadable underneath the committed mix — the user
        // edited it, or another tool rewrote it, between setup and Start. The
        // pick-time probe is long past and cannot know.
        await Page.EvaluateAsync("() => { window.__statsFake.statsJson = 'not json at all'; }");

        // Nothing warns in advance — there is nothing to warn from — so the
        // refusal is discovered at Start, which is exactly what the backstop is
        // for.
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
