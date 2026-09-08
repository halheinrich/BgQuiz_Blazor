using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Components.Pages;
using BgQuiz_Blazor.Client.Quiz;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="MixPanel"/> — the stats-weighted mix builder, a view
/// over the app-scoped <see cref="MixDraft"/> and nothing else. Pins the
/// <b>absence</b> of any activation control (<c>SPEC-filtering.md</c> §5,
/// "Visible means in effect", ruled 2026-09-07: being mounted is what puts
/// these rows in effect, so a control saying so would be a second copy of one
/// fact), the draft hydration surfacing through the panel (the re-offer after a
/// <see cref="MixDraft.Discard"/>), the write-through persistence as driven by
/// panel gestures (edits persist while valid; Clear persists blank; the
/// localStorage round-trip through the lib's <c>ToJson</c>/<c>TryFromJson</c>),
/// the semantic row order, per-kind parameter defaults, the
/// row-count-owns-the-percents rebalance and next-unused-kind seeding
/// (findings AH/AI), the percent-display/fraction-store rule for the
/// wrong-rate row, and the validation display.
///
/// <para>
/// Two things that used to live here now live where their subjects moved. The
/// effect derivation (visible ∧ build) is Home's and is pinned in
/// <c>PageTests</c>, together with the <c>halheinrich/backgammon#154</c> border
/// treatment — which followed the one control onto the Settings page. The
/// write-through's full matrix is pinned in <see cref="MixDraftTests"/>.
/// </para>
/// </summary>
public class MixPanelTests : BunitContext
{
    private readonly MixDraft _draft;

    public MixPanelTests()
    {
        // Loose mode — the draft's hydration issues a localStorage.getItem; the
        // mock returns default (null) = "no persisted mix" unless a test sets
        // up an explicit value. The write-through's setItem lands in the same
        // mock for assertion.
        JSInterop.Mode = JSRuntimeMode.Loose;

        // The panel injects the app-scoped draft and nothing else; one
        // instance registered up front lets tests assert its state and drive
        // the setup lifecycle (Discard) the way Home does. No consent bit and
        // no visibility service: since SPEC-filtering.md §5's "Visible means in
        // effect" ruling the panel is a pure view over the draft, and whether
        // it is on screen at all is the host's decision — pinned in PageTests,
        // where the host is.
        _draft = new MixDraft(JSInterop.JSRuntime);
        Services.AddSingleton(_draft);
    }

    /// <summary>
    /// The panel under test. Parameterless, and that is a pin in itself: the
    /// component takes no parameters at all since activation left the model.
    /// </summary>
    private IRenderedComponent<MixPanel> RenderPanel() => Render<MixPanel>();

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

    /// <summary>The last blob written under the mix key, parsed back through the lib; null when none was.</summary>
    private QuizMix? LastPersistedMix() =>
        JSInterop.Invocations
            .LastOrDefault(i => i.Identifier == "localStorage.setItem"
                             && (string?)i.Arguments[0] == MixDraft.StorageKey)
            is { Arguments: [_, string blob] }
            ? QuizMix.FromJson(blob)
            : null;

    // -----------------------------------------------------------------------
    //  No activation control — the absence is the ruling
    // -----------------------------------------------------------------------

    /// <summary>
    /// <b>The panel offers no way to turn the mix on or off</b>
    /// (<c>SPEC-filtering.md</c> §5, "Visible means in effect", ruled
    /// 2026-09-07). Being on screen is what puts these rows in effect, so a
    /// control saying so again would be the second copy of one fact that the
    /// ruling deletes.
    ///
    /// <para>
    /// <b>Pinned as an absence, and pinned by <i>id</i>, deliberately.</b> The
    /// six tests this replaced asserted the "Mix applies" switch's gate, its
    /// asymmetry, its programmatic backstop and its zero-row behaviour; all six
    /// went with their subject, and a suite that simply stopped mentioning
    /// <c>#mixApplies</c> would be indistinguishable from one where the control
    /// quietly came back. So the old id is named here — the one place in the
    /// suite that still may — together with the hint line that went with it and
    /// the general shape (no checkbox in this panel is an activation control:
    /// the two that remain are the mix's own Random-order toggle and nothing
    /// else). The Settings page carries the one control now, pinned in
    /// <c>PageTests</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void ThePanelHasNoActivationControl()
    {
        var cut = RenderPanel();

        Assert.Empty(cut.FindAll("#mixApplies"));
        Assert.Empty(cut.FindAll("#mixActivateDisabledReason"));

        // The only checkbox left is the mix's own Random-order setting, so an
        // activation control could not hide among unnamed siblings either.
        // That one is itself a form switch and always was — which is why the
        // switch is pinned by the id it belongs to rather than by class alone.
        Assert.Equal(
            ["mixRandomOrder"],
            cut.FindAll("input[type=checkbox]").Select(i => i.Id!).ToArray());
        Assert.Equal(
            ["mixRandomOrder"],
            cut.FindAll(".form-switch input").Select(i => i.Id!).ToArray());
    }

    [Fact]
    public async Task ClearMix_BlanksAndPersistsBlank_LeavingTheMixInEffect()
    {
        // Clear's one honest job is removing the rows (storage follows). It is
        // not an off-switch and never was: blank is the blank mix IN EFFECT —
        // the passthrough, ruled — so the panel is still here afterwards and
        // still applying. Turning the mix off is the Settings page's, which is
        // also what takes this panel off screen.
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");

        await ClickAsync(cut, "#mixClear");

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.True(LastPersistedMix()!.IsPassthrough);
        Assert.Empty(cut.FindAll("#mixApplies")); // no off-switch appeared either
    }

    [Fact]
    public async Task ClearMix_LabelAndId_ArePinned()
    {
        // The label and the id are the two handles the rest of the app
        // addresses the gesture by — Home's refusal copy names the panel's
        // controls, every test locator names the id — and the old #mixReset
        // spelling must stay gone.
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");

        Assert.Equal("Clear mix", cut.Find("#mixClear").TextContent.Trim());
        Assert.Empty(cut.FindAll("#mixReset"));
    }

    // -----------------------------------------------------------------------
    //  Hydration (the draft's once-per-setup localStorage restore, panel-triggered)
    // -----------------------------------------------------------------------

    [Fact]
    public void Hydrate_NothingPersisted_BlankBuilder_NothingWritten()
    {
        var cut = RenderPanel();

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.Null(LastPersistedMix()); // hydration is a read, never an echo write
    }

    [Fact]
    public void Hydrate_CorruptJson_BlankBuilder()
    {
        // Corrupt is the absent case's twin: the builder is blank. The stored
        // blob is left untouched (never-silently-clear) — no write happens
        // until the user's next valid edit replaces it.
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult("}{ not valid json");

        var cut = RenderPanel();

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.Null(LastPersistedMix());
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
    public void Hydrate_PersistedMix_ShowsRowsInWireOrder()
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
        Assert.Equal(mix, _draft.Build()); // shows exactly what was stored…

        // …and what is shown is what is in effect. Rule 3's explicit activation
        // went with the 2026-09-07 ruling: there is no longer a state where a
        // restored mix is visible but inert, so hydration has nothing left to
        // withhold. The host mounting this panel IS the activation, and the
        // absence of any control to check is pinned above.
        Assert.Empty(cut.FindAll("#mixApplies"));
    }

    [Fact]
    public void Hydrate_PersistedPassthroughMix_ShowsBlank()
    {
        // A persisted passthrough (e.g. after a prior Clear mix) round-trips to
        // zero rows — the same blank state the nothing-stored case reaches the
        // other way.
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult(QuizMix.Empty.ToJson());

        var cut = RenderPanel();

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.Equal(QuizMix.Empty, _draft.Build());
    }

    [Fact]
    public async Task Remount_WithinASetup_KeepsTheSurvivingDraftEdits()
    {
        // The ratified navigate-away semantics at the panel's level: the draft
        // is app-scoped and hydration is once per setup, so a re-mounted panel
        // (navigate away and back) re-renders the surviving edits — it does
        // NOT re-run the restore over them.
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow"); // an edit (persisted by write-through)
        await DisposeComponentsAsync();      // navigate away: the panel unmounts

        var back = RenderPanel();

        var row = Assert.Single(back.FindAll(".mix-row"));
        Assert.Equal("NeverSeen", row.QuerySelector("option[selected]")!.GetAttribute("value"));
        Assert.Single(JSInterop.Invocations["localStorage.getItem"]); // no re-read
    }

    [Fact]
    public async Task PersistedMix_RoundTripsIntoTheNextSetup()
    {
        // Edit in one setup (the write-through persists it), Discard (the
        // pick / Clear path), and the next panel mount re-offers exactly the
        // persisted content from storage — the localStorage round-trip driven
        // the way Home actually drives it.
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");
        await cut.FindAll(".mix-row")[0].QuerySelector("select")!.ChangeAsync(new() { Value = "WrongRateOver" });

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
    //  Write-through persistence, as driven by panel gestures
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidEdits_PersistTheBuiltMix_PerGesture()
    {
        // Persistence follows the screen — no commit gesture exists. Each
        // valid edit leaves the current screen state as the stored blob, in
        // the unchanged lib wire format.
        var cut = RenderPanel();

        await ClickAsync(cut, "#mixAddRow"); // NeverSeen, auto-percent 100
        Assert.Equal(
            new QuizMix([new QuizMixEntry(QuizCategory.NeverSeen, 100)], null, randomOrder: true),
            LastPersistedMix());

        await cut.Find("#mixQuizLength").InputAsync(new() { Value = "10" });
        await cut.Find("#mixRandomOrder").ChangeAsync(new() { Value = false });
        Assert.Equal(
            new QuizMix([new QuizMixEntry(QuizCategory.NeverSeen, 100)], 10, randomOrder: false),
            LastPersistedMix());
    }

    [Fact]
    public async Task InvalidEdit_SkipsTheWrite_StorageKeepsTheLastValidState()
    {
        // The ruled half-edit story at the panel seam: blanking the percent
        // mid-retype writes nothing, so what a reload would restore is the
        // last well-formed mix, never the torn edit.
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");
        var writesAfterAdd = JSInterop.Invocations["localStorage.setItem"].Count;

        await cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!
            .InputAsync(new() { Value = "" });

        Assert.Equal(writesAfterAdd, JSInterop.Invocations["localStorage.setItem"].Count);
        Assert.Equal(100, Assert.Single(LastPersistedMix()!.Entries).Percent);
    }

    [Fact]
    public async Task RemoveLastRow_IsAPlainEdit_PersistingTheBlankMix()
    {
        // No auto-commit machinery any more: removing the last row lands the
        // blank draft, blank builds Empty, and the ordinary write-through
        // persists it — so a later reload restores the blank builder rather
        // than the removed mix. Nothing gates Start on the way (the old
        // pre-beta wedge is unrepresentable: there is no commitment to
        // diverge from).
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow"); // one row
        Assert.Single(cut.FindAll(".mix-row"));

        await ClickRowButtonAsync(cut, 0, "Remove");

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.True(LastPersistedMix()!.IsPassthrough);
    }

    [Fact]
    public async Task WrongRate_DisplaysPercent_StoresFraction()
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow");
        await cut.FindAll(".mix-row")[0].QuerySelector("select")!.ChangeAsync(new() { Value = "WrongRateOver" });
        await cut.FindAll(".mix-row")[0].QuerySelector(".mix-param")!.InputAsync(new() { Value = "40" });

        // The UI said 40 (percent); the built and persisted category carries
        // the producer's fraction — thresholds are fractions, rendering is a
        // display concern.
        Assert.Equal(QuizCategory.WrongRateOver(0.40),
            Assert.Single(LastPersistedMix()!.Entries).Category);
        Assert.Equal(QuizCategory.WrongRateOver(0.40),
            Assert.Single(_draft.Build()!.Entries).Category);
    }

    // -----------------------------------------------------------------------
    //  Kind selection and parameter defaults
    // -----------------------------------------------------------------------

    /// <summary>
    /// The category select's token vocabulary: its reader is the inverse of
    /// its writer, which renders <c>value="@kind"</c> over
    /// <see cref="MixDraft.CategoryKinds"/>. Only a name the picker actually
    /// offered moves the row (halheinrich/backgammon#164).
    /// </summary>
    [Theory]
    [InlineData("5")]                  // ordinal — selected AvgEquityLossOver before #164
    [InlineData("1")]                  // ordinal — the row's own current kind, as a number
    [InlineData("99")]                 // ordinal outside the declared range
    [InlineData("avgEquityLossOver")]  // case variant
    [InlineData("AVGEQUITYLOSSOVER")]  // case variant
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

    /// <summary>
    /// The other half: every kind the picker offers is still selectable by the
    /// exact token it renders, so the strictness above costs no legitimate
    /// gesture as kinds are added to the list.
    /// </summary>
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

    // -----------------------------------------------------------------------
    //  Row order (semantic) and the reorder affordance
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Reorder_MoveUp_CarriesPercentsWithTheirRows()
    {
        var cut = RenderPanel();
        await ClickAsync(cut, "#mixAddRow"); // NeverSeen
        await ClickAsync(cut, "#mixAddRow"); // GotWrong — the next unused kind (AI)
        // Hand-edited away from the even 50/50 the Add produced: the reorder must
        // carry the percents with their rows, not re-derive them.
        await cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!.InputAsync(new() { Value = "60" });
        await cut.FindAll(".mix-row")[1].QuerySelector(".mix-percent")!.InputAsync(new() { Value = "40" });

        await ClickRowButtonAsync(cut, 1, "Move up");

        // GotWrong moved to the contested-overlap-winning first slot; the built
        // (and therefore effective and persisted) entry order says so.
        var built = _draft.Build();
        Assert.NotNull(built);
        Assert.Equal(QuizCategory.GotWrong, built!.Entries[0].Category);
        Assert.Equal(QuizCategory.NeverSeen, built.Entries[1].Category);
        // Each percent travelled with its row: reordering is not a row-count
        // change, so it does not rebalance (that would silently retune the mix).
        Assert.Equal(40, built.Entries[0].Percent);
        Assert.Equal(60, built.Entries[1].Percent);
        Assert.Equal(built, LastPersistedMix()); // the reorder wrote through
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
        Assert.NotNull(_draft.Build()); // valid as built
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
    //  Validation display (the gate it feeds is Home's — pinned in PageTests)
    // -----------------------------------------------------------------------

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
        await ClickAsync(cut, "#mixAddRow"); // GotWrong, and both rebalanced to 50

        // Add no longer produces a duplicate on its own (AI), so the collision is
        // now what it should be: a deliberate hand pick. The panel does not fight
        // it — the row shows the chosen kind and validation reports the clash.
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
        // Finding (AF). At zero rows the panel's neighbouring controls are
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
        Assert.True(cut.Find("#mixRandomOrder").HasAttribute("disabled"));
        Assert.True(cut.Find("#mixQuizLength").HasAttribute("disabled"));
    }
}
