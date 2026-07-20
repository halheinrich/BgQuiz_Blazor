using BgGame_Lib;
using BgQuiz_Blazor.Client.Components.Pages;
using Bunit;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="MixPanel"/> — the stats-weighted mix builder. Pins the
/// <c>FilterPanel</c>-mirroring commit model (Apply/Reset/dirty + the one
/// deliberate divergence, the adopting restore), the single-key localStorage
/// round-trip through the lib's <c>ToJson</c>/<c>TryFromJson</c>, the
/// semantic row order (reorder survives Apply), per-kind parameter defaults,
/// the percent-display/fraction-store rule for the wrong-rate row, and the
/// validation states that disable Apply.
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
    private readonly List<QuizMix> _restored = [];
    private int _dirtyCount;

    private IRenderedComponent<MixPanel> RenderPanel() =>
        Render<MixPanel>(parameters => parameters
            .Add(p => p.OnMixApplied, (QuizMix m) => _applied.Add(m))
            .Add(p => p.OnMixRestored, (QuizMix m) => _restored.Add(m))
            .Add(p => p.OnMixDirty, () => _dirtyCount++));

    private static Task ClickAsync(IRenderedComponent<MixPanel> cut, string selector) =>
        cut.Find(selector).ClickAsync(new());

    /// <summary>Click a row's ↑ / ↓ / ✕ button by its title, addressing rows by index.</summary>
    private static Task ClickRowButtonAsync(
        IRenderedComponent<MixPanel> cut, int rowIndex, string title) =>
        cut.FindAll(".mix-row")[rowIndex].QuerySelector($"button[title='{title}']")!
            .ClickAsync(new());

    // -----------------------------------------------------------------------
    //  Restore (first-render localStorage hydration)
    // -----------------------------------------------------------------------

    [Fact]
    public void Restore_NothingPersisted_BlankBuilder_NoRestoredEvent()
    {
        var cut = RenderPanel();

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.Empty(_restored);
        Assert.Empty(_applied);
        Assert.Equal(0, _dirtyCount);
    }

    [Fact]
    public void Restore_CorruptJson_BlankBuilder_NoEventNoCrash()
    {
        JSInterop.Setup<string?>("localStorage.getItem", MixPanel.MixKey)
            .SetResult("}{ not valid json");

        var cut = RenderPanel();

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.Empty(_restored);
    }

    [Fact]
    public void Restore_PersistedMix_HydratesRowsInWireOrder_AndRaisesRestored()
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

        // The restore adopts: the parent's holder receives the parsed mix.
        var restored = Assert.Single(_restored);
        Assert.Equal(mix.Entries, restored.Entries); // QuizMixEntry is value-equal
        Assert.Equal(25, restored.QuizLength);
        Assert.False(restored.RandomOrder);
        Assert.Empty(_applied); // restore is not an Apply gesture
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
        await ClickAsync(cut, "#mixAddRow"); // NeverSeen, 100
        await ClickAsync(cut, "#mixAddRow"); // NeverSeen, 1 — make it distinct + fix sum
        cut.FindAll(".mix-row")[1].QuerySelector("select")!.Change("GotWrong");
        cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!.Input("60");
        cut.FindAll(".mix-row")[1].QuerySelector(".mix-percent")!.Input("40");

        await ClickRowButtonAsync(cut, 1, "Move up");
        await ClickAsync(cut, "#mixApply");

        // GotWrong moved to the contested-overlap-winning first slot and the
        // committed entry order says so.
        var applied = Assert.Single(_applied);
        Assert.Equal(QuizCategory.GotWrong, applied.Entries[0].Category);
        Assert.Equal(QuizCategory.NeverSeen, applied.Entries[1].Category);
    }

    [Fact]
    public async Task RemoveRow_DropsExactlyThatRow()
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");
        await ClickAsync(cut, "#mixAddRow");
        cut.FindAll(".mix-row")[1].QuerySelector("select")!.Change("GotWrong");

        await ClickRowButtonAsync(cut, 0, "Remove");

        var row = Assert.Single(cut.FindAll(".mix-row"));
        Assert.Equal("GotWrong", row.QuerySelector("option[selected]")!.GetAttribute("value"));
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
        await ClickAsync(cut, "#mixAddRow");
        await ClickAsync(cut, "#mixAddRow");
        cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!.Input("50");
        cut.FindAll(".mix-row")[1].QuerySelector(".mix-percent")!.Input("50");

        // Both rows are NeverSeen — same kind, no parameter: a duplicate.
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
    public async Task BlankBuilder_ApplyEnabled_CommitsPassthrough()
    {
        var cut = RenderPanel();

        Assert.False(cut.Find("#mixApply").HasAttribute("disabled"));
        await ClickAsync(cut, "#mixApply");

        Assert.True(Assert.Single(_applied).IsPassthrough);
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
