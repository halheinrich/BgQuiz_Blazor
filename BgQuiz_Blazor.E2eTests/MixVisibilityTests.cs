using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// <b>Visible means in effect</b> (<c>SPEC-filtering.md</c> §5, ruled
/// 2026-09-07; <c>halheinrich/backgammon#181</c>), in a real browser: the mix
/// panel is on screen exactly when the Settings choice is on <i>and</i> the
/// picked folder holds a stats record, and being on screen is what puts its
/// rows in effect.
///
/// <para>
/// <b>This file replaced <c>ApplyMixGatingTests</c>, whose subject was
/// deleted.</b> That suite pinned the "Mix applies" checkbox's Fork A gate —
/// disabled until a filter was applied, re-gated by a later filter edit, with a
/// host sentence explaining why, and the asymmetry that kept a checked box
/// operable. The ruling deletes the checkbox and rule 2's activation gate
/// together, so those scenarios could not be re-keyed; what replaces them is
/// the conjunction that decides visibility, pinned from <b>both</b> sides
/// because either half alone would be a passing test over a broken derivation.
/// </para>
///
/// <para>
/// The bUnit layer pins the same conjunction over a fake corpus. What only a
/// real browser shows is that the setting survives the trip between two pages
/// and a genuine localStorage round trip, and that the panel's appearance and
/// the weighted run agree once it has.
/// </para>
/// </summary>
public sealed class MixVisibilityTests : FsAccessFakeTestBase
{
    public MixVisibilityTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    /// <summary>
    /// <b>Arm one: the setting off hides a mix the folder could otherwise
    /// carry.</b> A folder with a real stats record — the half that used to be
    /// the whole predicate — offers no panel while the setting is off, and the
    /// quiz runs unweighted. Without this, a derivation that ignored the
    /// setting would pass every other scenario in the suite.
    /// </summary>
    [Fact]
    public async Task SettingOff_FolderWithStats_OffersNoMix_AndTheQuizStillRuns()
    {
        await BootHomeAsync();

        // Seed a stats history WITHOUT the setting: run a quiz, feed the app's
        // own captured write back as the folder's file, and re-pick. This is
        // SeedStatsHistoryAsync's body minus its Settings visit, spelled out
        // because the difference from it is precisely what is under test.
        await PickFakeFolderAsync();
        await ApplyFilterAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();
        await StageFirstWriteAsTheFoldersStatsFileAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.BackToSetupButton }).ClickAsync();
        await ExpectUrlAsync("/");
        await PickFolderButton.ClickAsync();

        // The setup surface really did disclose, so the absence below is the
        // mix's specifically and not a pick that failed.
        await Expect(Page.Locator("#shuffleOrder")).ToBeVisibleAsync();
        await Expect(MixPanel).ToHaveCountAsync(0);
        await Expect(Page.Locator(".mix-row")).ToHaveCountAsync(0);
        // And no residue of the control the ruling deleted.
        await Expect(Page.Locator("#mixApplies")).ToHaveCountAsync(0);

        // The quiz runs, unweighted, exactly as for a folder with no stats.
        await ApplyFilterAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();
        await Expect(Page.GetByText(ExpectedText.TotalProblemsShown(1))).ToBeVisibleAsync();
    }

    /// <summary>
    /// <b>Arm two: the setting on cannot conjure a mix where there is nothing
    /// to weight by.</b> A freshly picked folder with no stats record offers no
    /// panel however the setting is set — the emergent behaviour
    /// <c>halheinrich/backgammon#87</c> accepted, unchanged by the ruling — and
    /// the setting is verifiably still on while the panel is absent, so this
    /// cannot pass by the setting having quietly failed to stick.
    /// </summary>
    [Fact]
    public async Task SettingOn_FolderWithoutStats_StillOffersNoMix()
    {
        await BootHomeAsync();
        await TurnOnTheWeightedMixSettingAsync();
        await PickFakeFolderAsync();

        await Expect(Page.Locator("#shuffleOrder")).ToBeVisibleAsync();
        await Expect(MixPanel).ToHaveCountAsync(0);

        // The setting really is on — the page just has nothing to weight by.
        await Page.GetByRole(AriaRole.Link, new() { Name = ExpectedText.SettingsNavLink }).ClickAsync();
        await ExpectUrlAsync("/settings");
        await Expect(Page.Locator("#settingsWeightQuizzes")).ToBeCheckedAsync();
    }

    /// <summary>
    /// <b>Both halves true: the panel appears, and appearing is the
    /// activation.</b> No second gesture stands between the panel and a
    /// weighted run — the composed rows are what Start draws from, with no
    /// checkbox to tick, which is the ruling stated as behaviour.
    /// </summary>
    [Fact]
    public async Task SettingOnAndStatsPresent_OffersTheMix_AndComposingItIsEnough()
    {
        await BootHomeAsync();
        await SeedStatsHistoryAsync(); // turns the setting on, then seeds a record
        await ApplyFilterAsync();

        await Expect(MixPanel).ToBeVisibleAsync();
        await Expect(Page.Locator("#mixApplies")).ToHaveCountAsync(0); // nothing to arm

        // Compose "Everything else" at 100% so the mix can actually draw the
        // one seeded problem, and start — with no activation gesture between.
        await AddDefaultMixRowAsync();
        await Page.GetByLabel(ExpectedText.MixCategoryLabel).SelectOptionAsync("EverythingElse");
        await Expect(StartButton).ToBeEnabledAsync();

        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();
        await Expect(Page.GetByText(ExpectedText.TotalProblemsShown(1))).ToBeVisibleAsync();
    }

    /// <summary>
    /// <b>The filter no longer sequences the mix.</b> Rule 2's activation gate
    /// is deleted, so the panel is composable with no filter in effect and stays
    /// in effect across a filter edit; Start's own filter gate is the only
    /// sequencing left, and the host sentence that explained the old one is
    /// gone from the page.
    /// </summary>
    [Fact]
    public async Task MixComposesWithNoFilterApplied_AndSurvivesADirtyFilter()
    {
        await BootHomeAsync();
        await SeedStatsHistoryAsync(); // ends with no filter in effect for this pick

        // No filter applied, and the panel is here and composable anyway.
        await Expect(MixPanel).ToBeVisibleAsync();
        await Expect(Page.GetByText("the mix draws its problems from the filtered pool"))
            .ToHaveCountAsync(0);
        await AddDefaultMixRowAsync();
        await Expect(StartButton).ToBeDisabledAsync(); // the FILTER's gate, not the mix's

        await ApplyFilterAsync();
        await Expect(StartButton).ToBeEnabledAsync();

        // A filter edit takes Start away and leaves the mix alone — the old
        // model revoked the check gesture at exactly this moment.
        await Page.Locator("#errorMin").FillAsync("0.05");
        await Expect(StartButton).ToBeDisabledAsync();
        await Expect(MixPanel).ToBeVisibleAsync();
        await Expect(Page.Locator(".mix-row")).ToHaveCountAsync(1);

        // Undo the edit and the applied filter is back in effect — one gesture,
        // no wedge, and the mix never moved.
        await Page.Locator("#errorMin").FillAsync("");
        await Expect(StartButton).ToBeEnabledAsync();
        await Expect(Page.Locator(".mix-row")).ToHaveCountAsync(1);
    }

    /// <summary>
    /// <b>Clear mix stays a way out and never becomes the off-switch.</b> It is
    /// ungated in every state, it empties the rows, and the panel is still on
    /// screen afterwards — blank is the blank mix in effect, the passthrough.
    /// </summary>
    [Fact]
    public async Task ClearMix_EmptiesTheRows_AndLeavesTheMixOn()
    {
        await BootHomeAsync();
        await SeedStatsHistoryAsync();
        await AddDefaultMixRowAsync();

        await Expect(Page.Locator("#mixClear")).ToBeEnabledAsync();
        await Page.Locator("#mixClear").ClickAsync();

        await Expect(Page.Locator(".mix-row")).ToHaveCountAsync(0);
        await Expect(MixPanel).ToBeVisibleAsync(); // still on, still applying
    }
}
