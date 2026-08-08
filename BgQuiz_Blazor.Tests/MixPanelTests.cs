using System.Globalization;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Components.Pages;
using BgQuiz_Blazor.Client.Quiz;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="MixPanel"/> — the stats-weighted mix builder, a view
/// over the app-scoped <see cref="MixDraft"/>. Pins the commit model (Apply /
/// Clear mix / last-row removal commit and persist; mere edits never do — there
/// is no dirty event to raise, the gate derives from the draft elsewhere),
/// the draft hydration surfacing through the panel (hydrate-don't-commit; the
/// re-offer after a <see cref="MixDraft.Discard"/>), the single-key
/// localStorage round-trip through the lib's <c>ToJson</c>/<c>TryFromJson</c>,
/// the semantic row order (reorder survives Apply), per-kind parameter
/// defaults, the row-count-owns-the-percents rebalance and next-unused-kind
/// seeding (findings AH/AI), the percent-display/fraction-store rule for the
/// wrong-rate row, and the validation states that disable Apply. The derived
/// dirtiness rule itself is pinned in <see cref="MixDraftTests"/> (service
/// level) and <c>PageTests</c> (the Home wire).
/// </summary>
public class MixPanelTests : BunitContext
{
    private readonly MixDraft _draft;

    public MixPanelTests()
    {
        // Loose mode — the draft's hydration issues a localStorage.getItem; the
        // mock returns default (null) = "no persisted mix" unless a test sets
        // up an explicit value.
        JSInterop.Mode = JSRuntimeMode.Loose;

        // The panel injects the app-scoped draft; registering one instance up
        // front lets tests assert draft state and drive its setup lifecycle
        // (Discard) the way Home does.
        _draft = new MixDraft(JSInterop.JSRuntime);
        Services.AddSingleton(_draft);
    }

    private readonly List<QuizMix> _applied = [];

    private IRenderedComponent<MixPanel> RenderPanel() =>
        Render<MixPanel>(parameters => parameters
            .Add(p => p.OnMixApplied, (QuizMix m) => _applied.Add(m)));

    private static Task ClickAsync(IRenderedComponent<MixPanel> cut, string selector) =>
        cut.Find(selector).ClickAsync(new());

    /// <summary>Click a row's ↑ / ↓ / ✕ button by its title, addressing rows by index.</summary>
    private static Task ClickRowButtonAsync(
        IRenderedComponent<MixPanel> cut, int rowIndex, string title) =>
        cut.FindAll(".mix-row")[rowIndex].QuerySelector($"button[title='{title}']")!
            .ClickAsync(new());

    /// <summary>Every row's percent field, in row order — the (AH) observable.</summary>
    private static string[] Percents(IRenderedComponent<MixPanel> cut) =>
        [.. cut.FindAll(".mix-row")
            .Select(r => r.QuerySelector(".mix-percent")!.GetAttribute("value")!)];

    /// <summary>Every row's selected kind, in row order — the (AI) observable.</summary>
    private static string[] Kinds(IRenderedComponent<MixPanel> cut) =>
        [.. cut.FindAll(".mix-row")
            .Select(r => r.QuerySelector("option[selected]")!.GetAttribute("value")!)];

    // -----------------------------------------------------------------------
    //  Hydration (the draft's once-per-setup localStorage restore, panel-triggered)
    // -----------------------------------------------------------------------

    [Fact]
    public void Hydrate_NothingPersisted_BlankBuilder_NoCommit()
    {
        var cut = RenderPanel();

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.Empty(_applied);                     // hydration never commits…
        Assert.True(_draft.Matches(QuizMix.Empty)); // …and a blank draft derives clean
    }

    [Fact]
    public void Hydrate_CorruptJson_BlankBuilder_NoCommit()
    {
        // Corrupt is the absent case's twin: the builder is blank. The stored
        // blob is left untouched (never-silently-clear), which is why this
        // can't be folded into a write.
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult("}{ not valid json");

        var cut = RenderPanel();

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.Empty(_applied);
        Assert.True(_draft.Matches(QuizMix.Empty));
    }

    [Fact]
    public void Hydrate_NothingPersisted_LeavesRandomOrderDefaultOn()
    {
        // The blank hydration must not project TryFromJson's Empty fallback
        // onto the draft: only a *successful* parse projects, so the blank
        // builder keeps its own defaults — the checkbox a first-time user
        // sees is on.
        var cut = RenderPanel();

        Assert.True(cut.Find("#mixRandomOrder").HasAttribute("checked"));
    }

    [Fact]
    public void Hydrate_PersistedMix_ShowsRowsInWireOrder_WithoutCommitting()
    {
        var mix = new QuizMix(
            [
                new QuizMixEntry(QuizCategory.GotWrong, 60),
                new QuizMixEntry(QuizCategory.SeenFewerThan(3), 40),
            ],
            quizLength: 25, randomOrder: false);
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult(mix.ToJson());

        var cut = RenderPanel();

        var rows = cut.FindAll(".mix-row");
        Assert.Equal(2, rows.Count);
        // Order is contractual — GotWrong first, exactly as persisted.
        Assert.Equal("GotWrong",
            rows[0].QuerySelector("option[selected]")!.GetAttribute("value"));
        Assert.Equal("SeenFewerThan",
            rows[1].QuerySelector("option[selected]")!.GetAttribute("value"));
        Assert.Equal("3", rows[1].QuerySelector(".mix-param")!.GetAttribute("value"));
        Assert.Equal("60", rows[0].QuerySelector(".mix-percent")!.GetAttribute("value"));
        Assert.Equal("40", rows[1].QuerySelector(".mix-percent")!.GetAttribute("value"));
        Assert.Equal("25", cut.Find("#mixQuizLength").GetAttribute("value"));
        Assert.False(cut.Find("#mixRandomOrder").HasAttribute("checked"));

        // Hydration fills the draft for editing but commits nothing — on a
        // fresh load the holder stays passthrough, so the derived gate holds
        // Start until the user Applies or clears what the panel now shows.
        Assert.Empty(_applied);
        Assert.True(_draft.Matches(mix)); // shows exactly what was stored…
        Assert.False(_draft.Matches(QuizMix.Empty)); // …which diverges from a fresh holder
    }

    [Fact]
    public void Hydrate_PersistedPassthroughMix_ShowsBlank_NoCommit()
    {
        // A persisted passthrough (e.g. after a prior Clear mix) round-trips to
        // zero rows — the same blank, clean state the nothing-stored case
        // reaches the other way.
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult(QuizMix.Empty.ToJson());

        var cut = RenderPanel();

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.Empty(_applied);
        Assert.True(_draft.Matches(QuizMix.Empty));
    }

    [Fact]
    public async Task Remount_WithinASetup_KeepsTheSurvivingDraftEdits()
    {
        // The ratified navigate-away semantics at the panel's level: the draft
        // is app-scoped and hydration is once per setup, so a re-mounted panel
        // (navigate away and back) re-renders the surviving edits — it does
        // NOT re-run the restore over them. Superseded behavior: the old
        // component-owned rows died with the instance (finding AK's wedge).
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow"); // an uncommitted edit
        await DisposeComponentsAsync();      // navigate away: the panel unmounts

        var back = RenderPanel();

        var row = Assert.Single(back.FindAll(".mix-row"));
        Assert.Equal("NeverSeen", row.QuerySelector("option[selected]")!.GetAttribute("value"));
        Assert.Empty(_applied); // still uncommitted — surviving is not committing
        Assert.Single(JSInterop.Invocations["localStorage.getItem"]); // no re-read
    }

    // -----------------------------------------------------------------------
    //  Apply / Clear mix / persistence
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Apply_CommitsMix_PersistsBlobAndRaisesApplied()
    {
        var cut = RenderPanel();

        await ClickAsync(cut, "#mixAddRow"); // NeverSeen, auto-percent 100
        await ClickAsync(cut, "#mixApply");

        var applied = Assert.Single(_applied);
        var entry = Assert.Single(applied.Entries);
        Assert.Equal(QuizCategory.NeverSeen, entry.Category);
        Assert.Equal(100, entry.Percent);
        Assert.Null(applied.QuizLength);
        Assert.True(applied.RandomOrder);

        // One blob under the one key, round-trippable by the lib.
        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == MixDraft.StorageKey)
            .Arguments[1] as string;
        Assert.NotNull(stored);
        Assert.Equal(applied, QuizMix.FromJson(stored!)); // full content equality (leg 1)

        // The committed-agrees-with-shown postcondition the derived gate reads.
        Assert.True(_draft.Matches(applied));
    }

    [Fact]
    public async Task Apply_LengthAndRandom_FlowIntoCommittedMix()
    {
        var cut = RenderPanel();

        await ClickAsync(cut, "#mixAddRow");
        cut.Find("#mixQuizLength").Input("10");
        cut.Find("#mixRandomOrder").Change(false);
        await ClickAsync(cut, "#mixApply");

        var applied = Assert.Single(_applied);
        Assert.Equal(10, applied.QuizLength);
        Assert.False(applied.RandomOrder);
    }

    [Fact]
    public async Task ClearMix_CommitsBlankMix_AndPersistsIt()
    {
        // Issue #87's rename, behavior end included: the gesture is now called
        // what it does. The label and the id are pinned here together because
        // they are the two handles the rest of the app addresses it by — the
        // page's hint text names the label, and every test locator names the id
        // — and the old spellings must be gone, not merely joined.
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");
        await ClickAsync(cut, "#mixApply");

        Assert.Equal("Clear mix", cut.Find("#mixClear").TextContent.Trim());
        Assert.Empty(cut.FindAll("#mixReset"));

        await ClickAsync(cut, "#mixClear");

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.Equal(2, _applied.Count);
        Assert.True(_applied[^1].IsPassthrough); // an explicit apply of Empty
        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == MixDraft.StorageKey)
            .Arguments[1] as string;
        Assert.True(QuizMix.FromJson(stored!).IsPassthrough);
    }

    [Fact]
    public async Task RemoveLastRow_AutoCommitsBlankMix_AndPersistsIt()
    {
        // The pre-beta wedge's root: a mix edited back to zero rows left an
        // uncommitted divergence while Apply is disabled (zero rows), gating
        // Start with only the non-obvious Clear mix as an escape. Removing the
        // last row must instead auto-commit the blank mix through the Apply
        // channel — the same effect as Clear mix — so committed and shown agree
        // and Start un-gates.
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow"); // one row, uncommitted
        Assert.Single(cut.FindAll(".mix-row"));

        await ClickRowButtonAsync(cut, 0, "Remove"); // removes the last row

        Assert.Empty(cut.FindAll(".mix-row"));
        // OnMixApplied fired with a passthrough mix — the commit that keeps the
        // derived gate clean.
        var applied = Assert.Single(_applied);
        Assert.True(applied.IsPassthrough);

        // localStorage matches Clear mix: the blank mix is persisted, so a later
        // reload restores the blank builder rather than the removed mix.
        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == MixDraft.StorageKey)
            .Arguments[1] as string;
        Assert.True(QuizMix.FromJson(stored!).IsPassthrough);
    }

    [Fact]
    public async Task RemoveNonLastRow_IsAnEdit_DoesNotCommit()
    {
        // Over-trigger guard: removing a row while others remain is an
        // ordinary edit — never an Apply of Empty (the mix still has
        // uncommitted rows the user must Apply).
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow"); // NeverSeen
        await ClickAsync(cut, "#mixAddRow"); // GotWrong — the next unused kind (AI)

        await ClickRowButtonAsync(cut, 0, "Remove");

        Assert.Single(cut.FindAll(".mix-row"));
        Assert.Empty(_applied);                      // nothing committed — one row still pending
        Assert.False(_draft.Matches(QuizMix.Empty)); // and the survivor derives dirty until Apply
    }

    [Fact]
    public async Task PersistedMix_RoundTripsIntoTheNextSetup()
    {
        // Commit in one setup, Discard (the pick / Clear path), and the next
        // panel mount re-offers exactly the committed content from storage —
        // the localStorage round-trip driven the way Home actually drives it.
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");
        var select = cut.FindAll(".mix-row")[0].QuerySelector("select")!;
        select.Change("WrongRateOver");
        await ClickAsync(cut, "#mixApply");

        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == MixDraft.StorageKey)
            .Arguments[1] as string;
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey).SetResult(stored);
        await DisposeComponentsAsync();
        _draft.Discard(); // the setup ended; hydration is forgotten

        var remounted = RenderPanel();

        var row = Assert.Single(remounted.FindAll(".mix-row"));
        Assert.Equal("WrongRateOver",
            row.QuerySelector("option[selected]")!.GetAttribute("value"));
        Assert.Equal("25", row.QuerySelector(".mix-param")!.GetAttribute("value"));
    }

    // -----------------------------------------------------------------------
    //  Kind selection, parameter defaults, the percent/fraction display rule
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("SeenFewerThan", "3")]
    [InlineData("NotSeenInDays", "30")]
    [InlineData("AvgEquityLossOver", "0.05")]
    [InlineData("WrongRateOver", "25")]
    public async Task KindSelection_SeedsItsDefaultParameter(string kind, string expected)
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");

        cut.FindAll(".mix-row")[0].QuerySelector("select")!.Change(kind);

        Assert.Equal(expected,
            cut.FindAll(".mix-row")[0].QuerySelector(".mix-param")!.GetAttribute("value"));
    }

    [Fact]
    public async Task WrongRate_DisplaysPercent_StoresFraction()
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");
        cut.FindAll(".mix-row")[0].QuerySelector("select")!.Change("WrongRateOver");
        cut.FindAll(".mix-row")[0].QuerySelector(".mix-param")!.Input("40");
        await ClickAsync(cut, "#mixApply");

        // The UI said 40 (percent); the committed category carries the
        // producer's fraction — thresholds are fractions, rendering is a
        // display concern.
        var applied = Assert.Single(_applied);
        Assert.Equal(QuizCategory.WrongRateOver(0.40), Assert.Single(applied.Entries).Category);
    }

    // -----------------------------------------------------------------------
    //  Row order (semantic) and the reorder affordance
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Reorder_MoveUp_SurvivesThroughApply()
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow"); // NeverSeen
        await ClickAsync(cut, "#mixAddRow"); // GotWrong — the next unused kind (AI)
        // Hand-edited away from the even 50/50 the Add produced: the reorder must
        // carry the percents with their rows, not re-derive them.
        cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!.Input("60");
        cut.FindAll(".mix-row")[1].QuerySelector(".mix-percent")!.Input("40");

        await ClickRowButtonAsync(cut, 1, "Move up");
        await ClickAsync(cut, "#mixApply");

        // GotWrong moved to the contested-overlap-winning first slot and the
        // committed entry order says so.
        var applied = Assert.Single(_applied);
        Assert.Equal(QuizCategory.GotWrong, applied.Entries[0].Category);
        Assert.Equal(QuizCategory.NeverSeen, applied.Entries[1].Category);
        // Each percent travelled with its row: reordering is not a row-count
        // change, so it does not rebalance (that would silently retune the mix).
        Assert.Equal(40, applied.Entries[0].Percent);
        Assert.Equal(60, applied.Entries[1].Percent);
    }

    [Fact]
    public async Task RemoveRow_DropsExactlyThatRow()
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow"); // NeverSeen
        await ClickAsync(cut, "#mixAddRow"); // GotWrong — the next unused kind (AI)

        await ClickRowButtonAsync(cut, 0, "Remove");

        var row = Assert.Single(cut.FindAll(".mix-row"));
        Assert.Equal("GotWrong", row.QuerySelector("option[selected]")!.GetAttribute("value"));
    }

    // -----------------------------------------------------------------------
    //  The row count owns the percents and the new row's kind (findings AH/AI)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Add_RebalancesEveryRow_ToAnEven100Split_OverHandEditedPercents()
    {
        // Finding (AH). Add used to leave the existing rows alone, so the user
        // hand-balanced back to the 100 the panel demands. Overwriting deliberate
        // edits is the intent, not a side effect: the gesture restructures the
        // mix, and the panel already knows the arithmetic.
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");
        await ClickAsync(cut, "#mixAddRow");
        cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!.Input("90");
        cut.FindAll(".mix-row")[1].QuerySelector(".mix-percent")!.Input("10");

        await ClickAsync(cut, "#mixAddRow");

        Assert.Equal(["34", "33", "33"], Percents(cut));
    }

    [Theory]
    [InlineData(1, new[] { "100" })]
    [InlineData(2, new[] { "50", "50" })]
    [InlineData(3, new[] { "34", "33", "33" })]
    [InlineData(6, new[] { "17", "17", "17", "17", "16", "16" })]
    [InlineData(7, new[] { "15", "15", "14", "14", "14", "14", "14" })]
    public async Task Add_FloorsTheShare_AndHandsTheRemainderOutFromTheTop(
        int rowCount, string[] expected)
    {
        var cut = RenderPanel();
        for (var i = 0; i < rowCount; i++) await ClickAsync(cut, "#mixAddRow");

        Assert.Equal(expected, Percents(cut));
        // The point of pinning the exact policy: the total lands on 100 by
        // construction, so the "must reach 100%" error can never be the
        // immediate consequence of an Add.
        Assert.Equal(100, expected.Sum(p => int.Parse(p, CultureInfo.InvariantCulture)));
        Assert.Contains("Total: 100%", cut.Markup);
        Assert.DoesNotContain("must reach 100", cut.Markup);
    }

    [Fact]
    public async Task SuccessiveAdds_WalkTheKindList_InPickerOrder()
    {
        // Finding (AI). Every Add used to land on NeverSeen, so building a mix
        // meant re-picking each row's kind by hand — and the second row was born
        // a duplicate of the first.
        var cut = RenderPanel();
        for (var i = 0; i < 7; i++) await ClickAsync(cut, "#mixAddRow");

        Assert.Equal(
            [
                "NeverSeen", "GotWrong", "SeenFewerThan", "NotSeenInDays",
                "AvgEquityLossOver", "WrongRateOver", "EverythingElse",
            ],
            Kinds(cut));
        // Walking the list to its end is the whole point: EverythingElse, the
        // residual, is offered last.
        Assert.DoesNotContain("Duplicate category", cut.Markup);
    }

    [Fact]
    public async Task Add_SeedsTheNewKindsDefaultParameter_SoTheRowIsNeverBornInvalid()
    {
        // (AI) can seat a *parameterized* kind on a fresh row, so Add must
        // seed that kind's default exactly as picking it by hand does.
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow"); // NeverSeen
        await ClickAsync(cut, "#mixAddRow"); // GotWrong
        await ClickAsync(cut, "#mixAddRow"); // SeenFewerThan — takes a parameter

        Assert.Equal("3",
            cut.FindAll(".mix-row")[2].QuerySelector(".mix-param")!.GetAttribute("value"));
        Assert.False(cut.Find("#mixApply").HasAttribute("disabled")); // valid as built
    }

    [Fact]
    public async Task Add_WhenEveryKindIsUsed_FallsBackToTheFirstKind()
    {
        var cut = RenderPanel();
        for (var i = 0; i < 8; i++) await ClickAsync(cut, "#mixAddRow"); // one past the list

        var kinds = Kinds(cut);
        Assert.Equal(8, kinds.Length);
        Assert.Equal("NeverSeen", kinds[^1]);
        // The fallback does not pretend the row is usable — beyond the list every
        // choice is a duplicate, and the existing validation says so.
        Assert.Contains("Duplicate category", cut.Markup);
        // The rebalance still lands on exactly 100, duplicate or not.
        Assert.Equal(["13", "13", "13", "13", "12", "12", "12", "12"], Percents(cut));
    }

    [Fact]
    public async Task Remove_RebalancesTheSurvivingRows_ToAnEven100Split()
    {
        // Settled symmetric with Add: a removal must not strand the panel showing
        // "must reach 100%" for percent the user never chose to give away.
        var cut = RenderPanel();
        for (var i = 0; i < 3; i++) await ClickAsync(cut, "#mixAddRow");

        await ClickRowButtonAsync(cut, 1, "Remove");

        Assert.Equal(["50", "50"], Percents(cut));
        Assert.Equal(["NeverSeen", "SeenFewerThan"], Kinds(cut)); // exactly that row went
        Assert.DoesNotContain("must reach 100", cut.Markup);
    }

    // -----------------------------------------------------------------------
    //  Validation gates Apply
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Apply_Disabled_WhileSumIsNot100()
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");
        cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!.Input("85");

        Assert.True(cut.Find("#mixApply").HasAttribute("disabled"));
        Assert.Contains("must reach 100", cut.Markup);
        Assert.Empty(_applied);

        cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!.Input("100");
        Assert.False(cut.Find("#mixApply").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Apply_Disabled_OnDuplicateCategory()
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow"); // NeverSeen, 100
        await ClickAsync(cut, "#mixAddRow"); // GotWrong, and both rebalanced to 50

        // Add no longer produces a duplicate on its own (AI), so the collision is
        // now what it should be: a deliberate hand pick. The panel does not fight
        // it — the row shows the chosen kind and validation reports the clash.
        cut.FindAll(".mix-row")[1].QuerySelector("select")!.Change("NeverSeen");

        Assert.Equal(["NeverSeen", "NeverSeen"], Kinds(cut));
        Assert.True(cut.Find("#mixApply").HasAttribute("disabled"));
        Assert.Contains("Duplicate category", cut.Markup);
    }

    [Fact]
    public async Task Apply_Disabled_OnBadParameter()
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");
        cut.FindAll(".mix-row")[0].QuerySelector("select")!.Change("SeenFewerThan");
        cut.FindAll(".mix-row")[0].QuerySelector(".mix-param")!.Input("0");

        Assert.True(cut.Find("#mixApply").HasAttribute("disabled"));
        Assert.Contains("at least 1", cut.Markup);
    }

    [Fact]
    public void BlankBuilder_AddCategoryIsUsable_AndDoesNotLookDisabled()
    {
        // Finding (AF). At zero rows the panel's other three controls are
        // *genuinely* disabled, so Add category — which is never disabled, and
        // is the only way out of the zero-row state — sat in a cluster of
        // switched-off controls wearing the same secondary grey and read as one
        // of them. Both halves are pinned together, because the defect was the
        // gap between them: the button is enabled (state) and carries the
        // actionable outline-primary styling (appearance). Disabling it, or
        // restyling it back into the muted grey, fails here.
        var cut = RenderPanel();

        var addRow = cut.Find("#mixAddRow");
        Assert.False(addRow.HasAttribute("disabled"));
        Assert.Contains("btn-outline-primary", addRow.ClassName);

        // The context that made the misread reasonable — asserted, not assumed.
        Assert.True(cut.Find("#mixApply").HasAttribute("disabled"));
        Assert.True(cut.Find("#mixRandomOrder").HasAttribute("disabled"));
        Assert.True(cut.Find("#mixQuizLength").HasAttribute("disabled"));
    }

    [Fact]
    public void BlankBuilder_ApplyDisabled_ClearMixIsTheBlankPath()
    {
        var cut = RenderPanel();

        // Apply is gated to require at least one row, so a blank builder cannot
        // commit QuizMix.Empty through Apply — that duplicated Clear mix, which
        // stays the one sanctioned way to clear a stored mix
        // (ClearMix_CommitsBlankMix_AndPersistsIt).
        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.True(cut.Find("#mixApply").HasAttribute("disabled"));
        Assert.Empty(_applied);
    }

    // -----------------------------------------------------------------------
    //  Edits never commit (the commit channel is Apply/Clear mix/last-row only)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EveryEdit_LeavesTheCommitChannelSilent()
    {
        // There is no per-edit event any more — edits mutate the draft, and the
        // gate derives from it elsewhere — so the one thing to hold here is
        // that no edit ever slips out as a commit.
        var cut = RenderPanel();

        await ClickAsync(cut, "#mixAddRow");
        cut.FindAll(".mix-row")[0].QuerySelector("select")!.Change("GotWrong");
        cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!.Input("100");
        cut.Find("#mixRandomOrder").Change(false);
        cut.Find("#mixQuizLength").Input("5");
        Assert.Empty(_applied);

        await ClickAsync(cut, "#mixApply");
        Assert.Single(_applied); // Apply is the commit — exactly once
    }
}
