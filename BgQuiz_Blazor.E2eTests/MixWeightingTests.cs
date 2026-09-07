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

    /// <summary>
    /// The category select is as wide as its own options and no wider, by the
    /// app's one content-sizing mechanism — the same ruler pair
    /// <c>/settings</c>' depth-ceiling dropdown wears
    /// (<c>halheinrich/backgammon#174</c>; <c>SettingsTests</c>
    /// <c>DepthCeilingDropdown_IsSizedFromItsOptions_NotFromThePage</c> is the
    /// other adopter's copy of this measurement).
    ///
    /// <para>
    /// <b>The overlap assertion is the load-bearing one.</b> The ruler's box is
    /// 15% narrower than the select painted inside it, and this row is a flex
    /// line whose gap is measured from the ruler — so before the room was
    /// reserved the percent field sat 19.89px under the select's right end. The
    /// reservation is a length (<c>app.css</c>'s <c>.mix-row &gt; .mix-kind</c>),
    /// because no percentage can be written against a width only the ruler
    /// knows, and a length is a number that can rot: a category label 15% longer
    /// than "Avg equity loss over…" grows the overflow past it. This assertion
    /// is where that goes red. It is a strict inequality on painted boxes, not a
    /// pin on the length, so re-tuning the length is the fix and re-tuning the
    /// test is not.
    /// </para>
    ///
    /// <para>
    /// The width claims mirror the Settings pin's, and for its reasons:
    /// inequalities rather than the 115% itself, which is the stylesheet's to
    /// state and is pinned there as a literal — a geometric equality would turn
    /// every sub-pixel of font rounding into a red suite. Box-guarded, since a
    /// rect read before layout settles measures nothing and "narrower than the
    /// row" is a green for the wrong reason.
    /// </para>
    /// </summary>
    [Fact]
    public async Task CategorySelect_IsSizedFromItsOptions_AndClearsThePercentField()
    {
        await Page.SetViewportSizeAsync(1280, 900);
        await BootHomeAsync();
        await SeedStatsHistoryAsync();
        await ApplyFilterAsync();
        await AddDefaultMixRowAsync();

        // Every option is present before anything is measured: the width comes
        // off the widest of them, so a half-rendered list is a narrower control
        // for a reason this test is not about.
        var select = Page.GetByLabel("Category");
        await Expect(select).ToContainTextAsync("Never seen");
        await Expect(select).ToContainTextAsync("Avg equity loss over…");

        await ExpectToPassAsync(async () =>
        {
            var row = await LaidOutBoxAsync(Page.Locator(".mix-row"), "the mix row");
            var ruler = await LaidOutBoxAsync(
                Page.Locator(".mix-row > .option-sized-field"), "the category select's ruler");
            var box = await LaidOutBoxAsync(select, "the category select");
            var percent = await LaidOutBoxAsync(
                Page.Locator(".mix-percent"), "the percent field");

            Assert.True(
                ruler.Width < row.Width / 2,
                $"the ruler measured the row rather than the options "
                + $"(ruler {ruler.Width}, row {row.Width}).");
            Assert.True(
                box.Width < row.Width / 2,
                $"the category select is sized from the row rather than from its "
                + $"options (select {box.Width}, row {row.Width}).");
            Assert.True(
                box.Width > ruler.Width,
                $"the category select got no breathing room past the width its "
                + $"options need (select {box.Width}, ruler {ruler.Width}) — this "
                + "is what separates the mechanism from a plain w-auto.");

            // …and the room the overflow needs is really reserved: the painted
            // control ends before the next control begins.
            Assert.True(
                box.X + box.Width < percent.X,
                $"the category select overlaps the percent field by "
                + $"{box.X + box.Width - percent.X}px — the ruler's 15% overflow "
                + "has outgrown the room reserved for it in app.css "
                + "(.mix-row > .mix-kind).");
        });
    }
}
