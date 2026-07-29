using System.Globalization;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Components.Pages;
using Bunit;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="MixPanel"/> — the stats-weighted mix builder. Pins the
/// <c>FilterPanel</c>-mirroring commit model (Apply/Reset/dirty, and the
/// reconcile-not-adopt hydration — the panel hydrates and raises OnMixHydrated
/// whether or not anything was stored, the parent decides whether to gate), the
/// single-key localStorage
/// round-trip through the lib's <c>ToJson</c>/<c>TryFromJson</c>, the
/// semantic row order (reorder survives Apply), per-kind parameter defaults,
/// the row-count-owns-the-percents rebalance and next-unused-kind seeding
/// (findings AH/AI), the percent-display/fraction-store rule for the wrong-rate
/// row, and the validation states that disable Apply.
/// </summary>
public class MixPanelTests : BunitContext
{
    public MixPanelTests()
    {
        // Loose mode — OnAfterRenderAsync issues a localStorage.getItem; the
        // mock returns default (null) = "no persisted mix" unless a test sets
        // up an explicit value.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private readonly List<QuizMix> _applied = [];
    private readonly List<QuizMix> _hydrated = [];
    private int _dirtyCount;

    private IRenderedComponent<MixPanel> RenderPanel() =>
        Render<MixPanel>(parameters => parameters
            .Add(p => p.OnMixApplied, (QuizMix m) => _applied.Add(m))
            .Add(p => p.OnMixHydrated, (QuizMix m) => _hydrated.Add(m))
            .Add(p => p.OnMixDirty, () => _dirtyCount++));

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
    //  Hydration (first-render localStorage restore)
    // -----------------------------------------------------------------------

    [Fact]
    public void Hydrate_NothingPersisted_BlankBuilder_RaisesHydratedWithBlankMix()
    {
        // The signal is TOTAL (finding AK): "nothing was stored" is a statement
        // the parent needs — it is what proves a dirty flag surviving from a
        // previous, now-unmounted instance of this panel stale. Silence here left
        // that flag standing and wedged Start.
        var cut = RenderPanel();

        Assert.Empty(cut.FindAll(".mix-row"));
        var hydrated = Assert.Single(_hydrated);
        Assert.True(hydrated.IsPassthrough); // exactly what the blank panel shows
        Assert.Empty(_applied);              // still no commit…
        Assert.Equal(0, _dirtyCount);        // …and no dirty of its own
    }

    [Fact]
    public void Hydrate_CorruptJson_BlankBuilder_RaisesHydratedWithBlankMix()
    {
        // Corrupt is the absent case's twin: the builder is blank, so the panel
        // says so rather than staying silent. The stored blob is left untouched
        // (never-silently-clear), which is why this can't be folded into a write.
        JSInterop.Setup<string?>("localStorage.getItem", MixPanel.MixKey)
            .SetResult("}{ not valid json");

        var cut = RenderPanel();

        Assert.Empty(cut.FindAll(".mix-row"));
        var hydrated = Assert.Single(_hydrated);
        Assert.True(hydrated.IsPassthrough);
        Assert.Empty(_applied);
        Assert.Equal(0, _dirtyCount);
    }

    [Fact]
    public void Hydrate_NothingPersisted_LeavesRandomOrderDefaultOn()
    {
        // The blank-hydration arm must not project TryFromJson's Empty fallback
        // onto the builder: Empty carries RandomOrder false, while the blank
        // builder's default — and the checkbox a first-time user sees — is on.
        // Only a *successful* parse hydrates; the raise is what became total.
        var cut = RenderPanel();

        Assert.True(cut.Find("#mixRandomOrder").HasAttribute("checked"));
    }

    [Fact]
    public void Hydrate_PersistedMix_HydratesRowsInWireOrder_AndRaisesHydrated()
    {
        var mix = new QuizMix(
            [
                new QuizMixEntry(QuizCategory.GotWrong, 60),
                new QuizMixEntry(QuizCategory.SeenFewerThan(3), 40),
            ],
            quizLength: 25, randomOrder: false);
        JSInterop.Setup<string?>("localStorage.getItem", MixPanel.MixKey)
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

        // The panel hands the parsed mix to the parent for reconciliation — it
        // neither commits (no Apply) nor dirties itself (the gate decision is the
        // parent's, against its holder).
        var hydrated = Assert.Single(_hydrated);
        Assert.Equal(mix.Entries, hydrated.Entries); // QuizMixEntry is value-equal
        Assert.Equal(25, hydrated.QuizLength);
        Assert.False(hydrated.RandomOrder);
        Assert.Empty(_applied);
        Assert.Equal(0, _dirtyCount);
    }

    [Fact]
    public void Hydrate_PersistedPassthroughMix_HydratesBlank_RaisesHydrated()
    {
        // A persisted passthrough (e.g. after a prior Reset) round-trips to zero
        // rows. The panel raises OnMixHydrated so the parent can reconcile — the
        // same blank statement the nothing-stored case makes, reached the other
        // way; the panel itself neither dirties nor commits.
        JSInterop.Setup<string?>("localStorage.getItem", MixPanel.MixKey)
            .SetResult(QuizMix.Empty.ToJson());

        var cut = RenderPanel();

        Assert.Empty(cut.FindAll(".mix-row"));
        var hydrated = Assert.Single(_hydrated);
        Assert.True(hydrated.IsPassthrough);
        Assert.Equal(0, _dirtyCount);
        Assert.Empty(_applied);
    }

    // -----------------------------------------------------------------------
    //  Apply / Reset / persistence
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
            .Last(i => (string?)i.Arguments[0] == MixPanel.MixKey)
            .Arguments[1] as string;
        Assert.NotNull(stored);
        Assert.Equal(applied.Entries, QuizMix.FromJson(stored!).Entries);
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
    public async Task Reset_CommitsBlankMix_AndPersistsIt()
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");
        await ClickAsync(cut, "#mixApply");

        await ClickAsync(cut, "#mixReset");

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.Equal(2, _applied.Count);
        Assert.True(_applied[^1].IsPassthrough); // an explicit apply of Empty
        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == MixPanel.MixKey)
            .Arguments[1] as string;
        Assert.True(QuizMix.FromJson(stored!).IsPassthrough);
    }

    [Fact]
    public async Task RemoveLastRow_AutoCommitsBlankMix_AndPersistsIt()
    {
        // The reported bug's root: a mix edited back to zero rows would stay
        // dirty at the parent while Apply is disabled (zero rows), leaving Start
        // wedged with only the non-obvious Reset as an escape. Removing the last
        // row must instead auto-commit the blank mix through the Apply channel —
        // the same effect as Reset — so the parent un-dirties and Start un-gates.
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow"); // one row, dirty
        Assert.Single(cut.FindAll(".mix-row"));

        await ClickRowButtonAsync(cut, 0, "Remove"); // removes the last row

        Assert.Empty(cut.FindAll(".mix-row"));
        // OnMixApplied fired with a passthrough mix (not OnMixDirty) — the
        // channel that un-dirties the parent's AppliedMix holder.
        var applied = Assert.Single(_applied);
        Assert.True(applied.IsPassthrough);

        // localStorage matches Reset: the blank mix is persisted, so a later
        // reload restores the blank builder rather than the removed mix.
        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == MixPanel.MixKey)
            .Arguments[1] as string;
        Assert.True(QuizMix.FromJson(stored!).IsPassthrough);
    }

    [Fact]
    public async Task RemoveNonLastRow_StaysDirty_DoesNotCommit()
    {
        // Over-trigger guard: removing a row while others remain is an ordinary
        // edit — it raises dirty, not an Apply of Empty (the mix still has
        // uncommitted rows the user must Apply).
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow"); // NeverSeen
        await ClickAsync(cut, "#mixAddRow"); // GotWrong — the next unused kind (AI)
        _dirtyCount = 0; // ignore the setup edits; count only the removal

        await ClickRowButtonAsync(cut, 0, "Remove");

        Assert.Single(cut.FindAll(".mix-row"));
        // One dirty for the whole gesture, even though the removal also
        // rebalanced the survivor's percent.
        Assert.Equal(1, _dirtyCount);
        Assert.Empty(_applied);       // nothing committed — one row still pending
    }

    [Fact]
    public async Task PersistedMix_RoundTripsAcrossRemount()
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");
        var select = cut.FindAll(".mix-row")[0].QuerySelector("select")!;
        select.Change("WrongRateOver");
        await ClickAsync(cut, "#mixApply");

        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == MixPanel.MixKey)
            .Arguments[1] as string;
        JSInterop.Setup<string?>("localStorage.getItem", MixPanel.MixKey).SetResult(stored);

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
        // (AI) can now seat a *parameterized* kind on a fresh row, so Add must
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

    [Fact]
    public async Task Add_RaisesDirtyOnce_HoweverManyRowsItRebalanced()
    {
        // Dirty is per gesture, not per row touched — the parent's Start gate
        // counts commits, and a rebalance that fanned out N dirty events would
        // still be one uncommitted edit.
        var cut = RenderPanel();

        await ClickAsync(cut, "#mixAddRow");
        await ClickAsync(cut, "#mixAddRow");
        await ClickAsync(cut, "#mixAddRow");

        Assert.Equal(3, _dirtyCount);
        Assert.Empty(_applied); // and the rebalance is an edit, never a commit
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
    public void BlankBuilder_ApplyDisabled_ResetIsTheBlankPath()
    {
        var cut = RenderPanel();

        // Apply is gated to require at least one row, so a blank builder cannot
        // commit QuizMix.Empty through Apply — that duplicated Reset, which stays
        // the one sanctioned way to clear a stored mix (Reset_CommitsBlankMix).
        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.True(cut.Find("#mixApply").HasAttribute("disabled"));
        Assert.Empty(_applied);
    }

    // -----------------------------------------------------------------------
    //  Dirty signaling
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EveryEdit_RaisesDirty_ApplyDoesNot()
    {
        var cut = RenderPanel();

        await ClickAsync(cut, "#mixAddRow");                                   // 1
        cut.FindAll(".mix-row")[0].QuerySelector("select")!.Change("GotWrong"); // 2
        cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!.Input("100"); // 3
        cut.Find("#mixRandomOrder").Change(false);                              // 4
        cut.Find("#mixQuizLength").Input("5");                                  // 5
        Assert.Equal(5, _dirtyCount);

        await ClickAsync(cut, "#mixApply");
        Assert.Equal(5, _dirtyCount); // Apply commits; it is not an edit
    }
}
