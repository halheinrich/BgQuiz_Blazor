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
        await Expect(Page.Locator("#mixApplies")).ToHaveCountAsync(0);
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
        // The weighted pipeline (mix UI → draft build → controller → composing
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
        await ActivateMixAsync();

        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();
        await Expect(Page.GetByText("Total problems shown: 1")).ToBeVisibleAsync();

        // Two write-backs now: the seeding quiz's fold and this weighted run's.
        Assert.Equal(2, (await CapturedWritesAsync()).Length);
    }

    [Fact]
    public async Task MixRows_SurviveInAppNavigation_InertUntilActivated()
    {
        // The screen-is-the-mix architecture's headline surface, in a real
        // browser: the mix draft is app-scoped, so an edit survives in-app
        // navigation (client-side routing — the WASM runtime and its Scoped
        // services live on; a full reload is the separate story below). Under
        // the spec's §5 the un-activated rows never gate Start — they are
        // simply not in effect — so the page is live before, during, and after
        // the round trip, and one check activates exactly what survived.
        await BootHomeAsync();
        await SeedStatsHistoryAsync(); // #87: no stats history, no mix panel to edit
        await ApplyFilterAsync();
        await AddDefaultMixRowAsync(); // rows on screen, box unchecked

        await Expect(StartButton).ToBeEnabledAsync(); // never gated by inert rows

        await Page.GetByRole(AriaRole.Link, new() { Name = "Help" }).ClickAsync();
        await ExpectUrlAsync("/help");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Home" }).ClickAsync();
        await ExpectUrlAsync("/");

        // The rows are still on screen, still inert; the filter half also
        // survived (Scoped holder), so activation is one check away.
        await Expect(Page.Locator(".mix-row")).ToHaveCountAsync(1);
        await Expect(Page.Locator("#mixApplies")).Not.ToBeCheckedAsync();
        await Expect(StartButton).ToBeEnabledAsync();

        await ActivateMixAsync();
        await Expect(StartButton).ToBeEnabledAsync();
    }

    [Fact]
    public async Task MixRows_SurviveAFullReload_TheCheckboxDoesNot()
    {
        // §4's law at the reload boundary, end to end over real localStorage:
        // the rows are choice and persist (the write-through saved them on the
        // edit itself — no commit gesture exists); the checkbox is consent and
        // dies with the app scope. After reload + re-pick the SAME mix is on
        // screen, unchecked and inert, and re-checking weights the next run.
        await BootHomeAsync();
        await SeedStatsHistoryAsync();
        await ApplyFilterAsync();
        await AddDefaultMixRowAsync();
        await Page.GetByLabel("Category").SelectOptionAsync("EverythingElse");
        await ActivateMixAsync();
        await Expect(StartButton).ToBeEnabledAsync();

        // Carry the seeded stats record across the reload by hand: the reload
        // re-runs the context init script, which resets the fake's state (a
        // real folder's bgquiz-stats.json would survive; the fake's must be
        // re-staged).
        //
        // Both reads here are single ones and race nothing: the value being
        // moved is the one this test staged itself, through
        // StageFirstWriteAsTheFoldersStatsFileAsync, and the app is not writing
        // to the slot at either moment.
        var statsJson = await Page.EvaluateAsync<string?>("() => window.__statsFake.statsJson");
        await Page.ReloadAsync();
        await Expect(PickFolderButton).ToBeVisibleAsync(); // WASM re-booted
        await Page.EvaluateAsync("s => { window.__statsFake.statsJson = s; }", statsJson);

        // A reload is the arrival at a fresh setup: pick and re-apply.
        await PickFakeFolderAsync();
        await ApplyFilterAsync();

        // The mix came back from localStorage — same row, same category —
        // visible but inert: the consent bit did not survive.
        await Expect(Page.Locator(".mix-row")).ToHaveCountAsync(1);
        await Expect(Page.GetByLabel("Category")).ToHaveValueAsync("EverythingElse");
        await Expect(Page.Locator("#mixApplies")).Not.ToBeCheckedAsync();
        await Expect(StartButton).ToBeEnabledAsync();

        // Re-checking weights the restored mix and the quiz runs to Done.
        await ActivateMixAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();
        await Expect(Page.GetByText("Total problems shown: 1")).ToBeVisibleAsync();
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

        await StageFirstWriteAsTheFoldersStatsFileAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Back to setup" }).ClickAsync();
        await ExpectUrlAsync("/");
        await AddDefaultMixRowAsync();
        await ActivateMixAsync();

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
        await ActivateMixAsync();

        // Now the file turns unreadable underneath the active mix — the user
        // edited it, or another tool rewrote it, between setup and Start. The
        // pick-time probe is long past and cannot know.
        // Pure setup, not a read: the corrupt content is put in place before the
        // bind that will choke on it, and nothing is being observed here.
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
