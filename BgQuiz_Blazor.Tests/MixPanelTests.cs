using System.Globalization;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Components.Pages;
using BgQuiz_Blazor.Client.Quiz;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="MixPanel"/>, a view over the app-scoped <see cref="MixDraft"/> and nothing else:
/// the absence of any activation control (being mounted is the activation), the hydration
/// the panel triggers, the write-through as panel gestures drive it, the semantic row order,
/// per-kind parameter defaults, the rebalance and next-unused-kind seeding, the percent
/// display of the wrong-rate row, and the validation display. Whether the panel is on screen
/// at all is the host's decision, pinned in <c>PageTests</c>.
/// </summary>
public class MixPanelTests : BunitContext
{
    private readonly MixDraft _draft;

    public MixPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _draft = new MixDraft(new ReportingBrowserStorage(
            new BrowserStorage(JSInterop.JSRuntime), new BrowserStorageCondition(), NullLogger<ReportingBrowserStorage>.Instance));
        Services.AddSingleton(_draft);
    }

    private IRenderedComponent<MixPanel> RenderPanel() => Render<MixPanel>();

    private static Task ClickAsync(IRenderedComponent<MixPanel> cut, string selector) =>
        cut.Find(selector).ClickAsync(new());

    private static Task ClickRowButtonAsync(
        IRenderedComponent<MixPanel> cut, int rowIndex, string title) =>
        cut.FindAll(".mix-row")[rowIndex].QuerySelector($"button[title='{title}']")!
            .ClickAsync(new());

    private static string[] Percents(IRenderedComponent<MixPanel> cut) =>
        [.. cut.FindAll(".mix-row")
            .Select(r => r.QuerySelector(".mix-percent")!.GetAttribute("value")!)];

    private static string[] Kinds(IRenderedComponent<MixPanel> cut) =>
        [.. cut.FindAll(".mix-row")
            .Select(r => r.QuerySelector("option[selected]")!.GetAttribute("value")!)];

    private BrowserStoragePlan? _plan;

    private BrowserStoragePlan Plan => _plan ??= BrowserStoragePlan.On(JSInterop);

    private void StageStored(string? json) =>
        Plan.ExpectRead(
            BrowserStorageArea.Local, MixDraft.StorageKey,
            json is null ? BrowserStorageReadAnswer.Absent : BrowserStorageReadAnswer.Stored(json));

    private BrowserStorageExpectation ExpectPersisted(QuizMix mix) =>
        Plan.ExpectWrite(BrowserStorageArea.Local, MixDraft.StorageKey, mix.ToJson(), BrowserStorageWriteAnswer.Succeeded);

    private static QuizMix NeverSeenMix(int? quizLength = null) =>
        new([new QuizMixEntry(QuizCategory.NeverSeen, 100)], quizLength);

    /// <summary>
    /// The panel offers no way to turn the mix on or off (SPEC-filtering.md §5): the old
    /// "Mix applies" control's id is named here, the one place that still may, so a suite
    /// that stopped mentioning it could not pass with the control quietly back.
    /// </summary>
    [Fact]
    public void ThePanelHasNoActivationControl()
    {
        var cut = RenderPanel();

        Assert.Empty(cut.FindAll("#mixApplies"));
        Assert.Empty(cut.FindAll("#mixActivateDisabledReason"));
        Assert.Empty(cut.FindAll("input[type=checkbox]"));
        Assert.Empty(cut.FindAll(".form-switch input"));
    }

    [Fact]
    public async Task ClearMix_BlanksAndPersistsBlank_LeavingTheMixInEffect()
    {
        StageStored(null);
        Plan.RequireOrder(ExpectPersisted(NeverSeenMix()), ExpectPersisted(QuizMix.Empty));
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");

        await ClickAsync(cut, "#mixClear");

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.Empty(cut.FindAll("#mixApplies"));
        Plan.Verify();
    }

    [Fact]
    public async Task ClearMix_LabelAndId_ArePinned()
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");

        Assert.Equal("Clear mix", cut.Find("#mixClear").TextContent.Trim());
        Assert.Empty(cut.FindAll("#mixReset"));
    }

    [Fact]
    public void Hydrate_NothingPersisted_BlankBuilder_NothingWritten()
    {
        StageStored(null);

        var cut = RenderPanel();

        Assert.Empty(cut.FindAll(".mix-row"));
        Plan.Verify();
    }

    [Fact]
    public void Hydrate_CorruptJson_BlankBuilder()
    {
        StageStored("}{ not valid json");

        var cut = RenderPanel();

        Assert.Empty(cut.FindAll(".mix-row"));
        Plan.Verify();
    }

    /// <summary>The panel has no order control: the run's Order presents (SPEC-filtering.md §5, §7).</summary>
    [Fact]
    public void ThePanelHasNoOrderControl()
    {
        var cut = RenderPanel();

        Assert.Empty(cut.FindAll("#mixRandomOrder"));
        Assert.DoesNotContain("Random order", cut.Markup);
    }

    [Fact]
    public void Hydrate_PersistedMix_ShowsRowsInWireOrder()
    {
        var mix = new QuizMix(
            [
                new QuizMixEntry(QuizCategory.GotWrong, 60),
                new QuizMixEntry(QuizCategory.SeenFewerThan(3), 40),
            ],
            quizLength: 25);
        StageStored(mix.ToJson());

        var cut = RenderPanel();

        var rows = cut.FindAll(".mix-row");
        Assert.Equal(2, rows.Count);
        Assert.Equal("GotWrong",
            rows[0].QuerySelector("option[selected]")!.GetAttribute("value"));
        Assert.Equal("SeenFewerThan",
            rows[1].QuerySelector("option[selected]")!.GetAttribute("value"));
        Assert.Equal("3", rows[1].QuerySelector(".mix-param")!.GetAttribute("value"));
        Assert.Equal("60", rows[0].QuerySelector(".mix-percent")!.GetAttribute("value"));
        Assert.Equal("40", rows[1].QuerySelector(".mix-percent")!.GetAttribute("value"));
        Assert.Equal("25", cut.Find("#mixQuizLength").GetAttribute("value"));
        Assert.Equal(mix, _draft.Build());
        Assert.Empty(cut.FindAll("#mixApplies"));
        Plan.Verify();
    }

    [Fact]
    public void Hydrate_PersistedPassthroughMix_ShowsBlank()
    {
        StageStored(QuizMix.Empty.ToJson());

        var cut = RenderPanel();

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.Equal(QuizMix.Empty, _draft.Build());
        Plan.Verify();
    }

    [Fact]
    public async Task Remount_KeepsTheSurvivingDraftEdits()
    {
        // The draft is app-scoped and hydration is once per visit, so a re-mounted panel
        // (navigate away and back, or the next pick) re-renders the surviving edits.
        StageStored(null);
        ExpectPersisted(NeverSeenMix());
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");
        await DisposeComponentsAsync();

        var back = RenderPanel();

        var row = Assert.Single(back.FindAll(".mix-row"));
        Assert.Equal("NeverSeen", row.QuerySelector("option[selected]")!.GetAttribute("value"));
        Plan.Verify(); // no re-read
    }

    [Fact]
    public async Task ValidEdits_PersistTheBuiltMix_PerGesture()
    {
        StageStored(null);
        var cut = RenderPanel();

        ExpectPersisted(NeverSeenMix());
        await ClickAsync(cut, "#mixAddRow");
        Plan.Verify();

        ExpectPersisted(NeverSeenMix(quizLength: 10));
        await cut.Find("#mixQuizLength").InputAsync(new() { Value = "10" });
        Plan.Verify();
    }

    [Fact]
    public async Task InvalidEdit_SkipsTheWrite_StorageKeepsTheLastValidState()
    {
        StageStored(null);
        ExpectPersisted(NeverSeenMix());
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");

        await cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!
            .InputAsync(new() { Value = "" });

        Plan.Verify();
    }

    [Fact]
    public async Task RemoveLastRow_IsAPlainEdit_PersistingTheBlankMix()
    {
        StageStored(null);
        Plan.RequireOrder(ExpectPersisted(NeverSeenMix()), ExpectPersisted(QuizMix.Empty));
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");
        Assert.Single(cut.FindAll(".mix-row"));

        await ClickRowButtonAsync(cut, 0, "Remove");

        Assert.Empty(cut.FindAll(".mix-row"));
        Plan.Verify();
    }

    [Fact]
    public async Task WrongRate_DisplaysPercent_StoresFraction()
    {
        StageStored(null);
        ExpectPersisted(NeverSeenMix());
        ExpectPersisted(new QuizMix([new QuizMixEntry(QuizCategory.WrongRateOver(0.25), 100)], null));
        ExpectPersisted(new QuizMix([new QuizMixEntry(QuizCategory.WrongRateOver(0.40), 100)], null));
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");
        await cut.FindAll(".mix-row")[0].QuerySelector("select")!.ChangeAsync(new() { Value = "WrongRateOver" });
        await cut.FindAll(".mix-row")[0].QuerySelector(".mix-param")!.InputAsync(new() { Value = "40" });

        Assert.Equal(QuizCategory.WrongRateOver(0.40),
            Assert.Single(_draft.Build()!.Entries).Category);
        Plan.Verify();
    }

    [Theory]
    [InlineData("5")]
    [InlineData("1")]
    [InlineData("99")]
    [InlineData("avgEquityLossOver")]
    [InlineData("AVGEQUITYLOSSOVER")]
    [InlineData("NotAKind")]
    [InlineData("")]
    public async Task KindSelection_IgnoresAnythingThePickerNeverOffered(string token)
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");
        var before = Kinds(cut);

        await cut.FindAll(".mix-row")[0].QuerySelector("select")!
            .ChangeAsync(new() { Value = token });

        Assert.Equal(before, Kinds(cut));
    }

    [Fact]
    public async Task KindSelection_AcceptsEveryOfferedKind_ByTheTokenTheOptionRenders()
    {
        foreach (var kind in MixDraft.CategoryKinds)
        {
            var cut = RenderPanel();
            await ClickAsync(cut, "#mixAddRow");

            await cut.FindAll(".mix-row")[0].QuerySelector("select")!
                .ChangeAsync(new() { Value = kind.ToString() });

            Assert.Equal(kind.ToString(), Kinds(cut)[0]);
        }
    }

    [Theory]
    [InlineData("SeenFewerThan", "3")]
    [InlineData("NotSeenInDays", "30")]
    [InlineData("AvgEquityLossOver", "0.05")]
    [InlineData("WrongRateOver", "25")]
    public async Task KindSelection_SeedsItsDefaultParameter(string kind, string expected)
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");

        await cut.FindAll(".mix-row")[0].QuerySelector("select")!.ChangeAsync(new() { Value = kind });

        Assert.Equal(expected,
            cut.FindAll(".mix-row")[0].QuerySelector(".mix-param")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Reorder_MoveUp_CarriesPercentsWithTheirRows()
    {
        StageStored(null);
        ExpectPersisted(NeverSeenMix());
        ExpectPersisted(new QuizMix(
            [new QuizMixEntry(QuizCategory.NeverSeen, 50), new QuizMixEntry(QuizCategory.GotWrong, 50)]));
        var beforeTheMove = ExpectPersisted(new QuizMix(
            [new QuizMixEntry(QuizCategory.NeverSeen, 60), new QuizMixEntry(QuizCategory.GotWrong, 40)]));
        var theMove = ExpectPersisted(new QuizMix(
            [new QuizMixEntry(QuizCategory.GotWrong, 40), new QuizMixEntry(QuizCategory.NeverSeen, 60)]));
        Plan.RequireOrder(beforeTheMove, theMove);
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow"); // NeverSeen
        await ClickAsync(cut, "#mixAddRow"); // GotWrong
        await cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!.InputAsync(new() { Value = "60" });
        await cut.FindAll(".mix-row")[1].QuerySelector(".mix-percent")!.InputAsync(new() { Value = "40" });

        await ClickRowButtonAsync(cut, 1, "Move up");

        var built = _draft.Build();
        Assert.NotNull(built);
        Assert.Equal(QuizCategory.GotWrong, built!.Entries[0].Category);
        Assert.Equal(QuizCategory.NeverSeen, built.Entries[1].Category);
        Assert.Equal(40, built.Entries[0].Percent);
        Assert.Equal(60, built.Entries[1].Percent);
        Plan.Verify();
    }

    [Fact]
    public async Task RemoveRow_DropsExactlyThatRow()
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow"); // NeverSeen
        await ClickAsync(cut, "#mixAddRow"); // GotWrong

        await ClickRowButtonAsync(cut, 0, "Remove");

        var row = Assert.Single(cut.FindAll(".mix-row"));
        Assert.Equal("GotWrong", row.QuerySelector("option[selected]")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Add_RebalancesEveryRow_ToAnEven100Split_OverHandEditedPercents()
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");
        await ClickAsync(cut, "#mixAddRow");
        await cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!.InputAsync(new() { Value = "90" });
        await cut.FindAll(".mix-row")[1].QuerySelector(".mix-percent")!.InputAsync(new() { Value = "10" });

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
        Assert.Equal(100, expected.Sum(p => int.Parse(p, CultureInfo.InvariantCulture)));
        Assert.Contains("Total: 100%", cut.Markup);
        Assert.DoesNotContain("must reach 100", cut.Markup);
    }

    [Fact]
    public async Task SuccessiveAdds_WalkTheKindList_InPickerOrder()
    {
        var cut = RenderPanel();
        for (var i = 0; i < 7; i++) await ClickAsync(cut, "#mixAddRow");

        Assert.Equal(
            [
                "NeverSeen", "GotWrong", "SeenFewerThan", "NotSeenInDays",
                "AvgEquityLossOver", "WrongRateOver", "EverythingElse",
            ],
            Kinds(cut));
        Assert.DoesNotContain("Duplicate category", cut.Markup);
    }

    [Fact]
    public async Task Add_SeedsTheNewKindsDefaultParameter_SoTheRowIsNeverBornInvalid()
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow"); // NeverSeen
        await ClickAsync(cut, "#mixAddRow"); // GotWrong
        await ClickAsync(cut, "#mixAddRow"); // SeenFewerThan — takes a parameter

        Assert.Equal("3",
            cut.FindAll(".mix-row")[2].QuerySelector(".mix-param")!.GetAttribute("value"));
        Assert.NotNull(_draft.Build());
        Assert.Null(_draft.ValidationError);
    }

    [Fact]
    public async Task Add_WhenEveryKindIsUsed_FallsBackToTheFirstKind()
    {
        var cut = RenderPanel();
        for (var i = 0; i < 8; i++) await ClickAsync(cut, "#mixAddRow"); // one past the list

        var kinds = Kinds(cut);
        Assert.Equal(8, kinds.Length);
        Assert.Equal("NeverSeen", kinds[^1]);
        Assert.Contains("Duplicate category", cut.Markup);
        Assert.Equal(["13", "13", "13", "13", "12", "12", "12", "12"], Percents(cut));
    }

    [Fact]
    public async Task Remove_RebalancesTheSurvivingRows_ToAnEven100Split()
    {
        var cut = RenderPanel();
        for (var i = 0; i < 3; i++) await ClickAsync(cut, "#mixAddRow");

        await ClickRowButtonAsync(cut, 1, "Remove");

        Assert.Equal(["50", "50"], Percents(cut));
        Assert.Equal(["NeverSeen", "SeenFewerThan"], Kinds(cut));
        Assert.DoesNotContain("must reach 100", cut.Markup);
    }

    [Fact]
    public async Task SumNot100_ReportsTheError_AndTheDraftDoesNotBuild()
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");
        await cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!.InputAsync(new() { Value = "85" });

        Assert.Contains("must reach 100", cut.Markup);
        Assert.Null(_draft.Build());

        await cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!.InputAsync(new() { Value = "100" });
        Assert.NotNull(_draft.Build());
    }

    [Fact]
    public async Task DuplicateCategory_ReportsTheError()
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow"); // NeverSeen, 100
        await ClickAsync(cut, "#mixAddRow"); // GotWrong, both rebalanced to 50

        await cut.FindAll(".mix-row")[1].QuerySelector("select")!.ChangeAsync(new() { Value = "NeverSeen" });

        Assert.Equal(["NeverSeen", "NeverSeen"], Kinds(cut));
        Assert.Contains("Duplicate category", cut.Markup);
        Assert.Null(_draft.Build());
    }

    [Fact]
    public async Task BadParameter_ReportsTheError()
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");
        await cut.FindAll(".mix-row")[0].QuerySelector("select")!.ChangeAsync(new() { Value = "SeenFewerThan" });
        await cut.FindAll(".mix-row")[0].QuerySelector(".mix-param")!.InputAsync(new() { Value = "0" });

        Assert.Contains("at least 1", cut.Markup);
        Assert.Null(_draft.Build());
    }

    [Fact]
    public void BlankBuilder_AddCategoryIsUsable_AndDoesNotLookDisabled()
    {
        // At zero rows the neighbouring controls are genuinely disabled, so Add category —
        // the only way out of the zero-row state — must be enabled and look it.
        var cut = RenderPanel();

        var addRow = cut.Find("#mixAddRow");
        Assert.False(addRow.HasAttribute("disabled"));
        Assert.Contains("btn-outline-primary", addRow.ClassName);
        Assert.True(cut.Find("#mixQuizLength").HasAttribute("disabled"));
    }
}
