using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Bunit;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="MixDraft"/> — the app-scoped mix edit state under the
/// checkbox-activation model (<c>SPEC-filtering.md</c> §5): there is no
/// committed mix and no commit gesture, so what these pin is the
/// <b>last-valid write-through</b> (every mutation that validates persists the
/// built <see cref="QuizMix"/> — blank included; an invalid mutation skips the
/// write, so storage always holds the last well-formed screen state), the
/// ruled <b>blank ⇒ <see cref="QuizMix.Empty"/>, never null</b> line of
/// <see cref="MixDraft.Build"/> that checked-but-inert and Clear-persists-blank
/// both load-bear on, and the once-per-setup hydration lifecycle
/// (<see cref="MixDraft.EnsureHydratedAsync"/> idempotent;
/// <see cref="MixDraft.Discard"/> forgets it and — deliberately — persists
/// nothing, so the stored mix survives a setup end;
/// <see cref="MixDraft.ClearAsync"/> keeps hydration and persists blank). The
/// builder policies the draft inherited from the panel (rebalance,
/// next-unused-kind, validation wording) stay pinned where they are
/// user-visible, in <see cref="MixPanelTests"/>. Extends
/// <see cref="BunitContext"/> only for the JSInterop double behind the draft's
/// localStorage reads/writes.
/// </summary>
public class MixDraftTests : BunitContext
{
    public MixDraftTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose; // getItem → null unless a test sets a value
    }

    private MixDraft NewDraft() => new(JSInterop.JSRuntime);

    /// <summary>A one-row never-seen mix, deterministic content — what one Add builds (NeverSeen seeds at 100%).</summary>
    private static QuizMix NeverSeenMix() =>
        new([new QuizMixEntry(QuizCategory.NeverSeen, 100)], quizLength: null, randomOrder: true);

    /// <summary>Every persisted blob so far, oldest first, parsed back through the lib.</summary>
    private QuizMix[] PersistedMixes() =>
        [.. JSInterop.Invocations
            .Where(i => i.Identifier == "localStorage.setItem"
                     && (string?)i.Arguments[0] == MixDraft.StorageKey)
            .Select(i => QuizMix.FromJson((string)i.Arguments[1]!))];

    // -----------------------------------------------------------------------
    //  Build — the effect derivation's substrate
    // -----------------------------------------------------------------------

    [Fact]
    public void BlankDraft_Builds_Empty_NeverNull()
    {
        // RULED, and load-bearing twice: checked + blank must read as the
        // in-effect passthrough (Home's EffectiveMix must see Empty, not the
        // gated null), and Clear-persists-blank needs the write-through to see
        // blank as a persistable mix. Null is reserved for genuinely invalid
        // states.
        var draft = NewDraft();

        Assert.NotNull(draft.Build());
        Assert.Equal(QuizMix.Empty, draft.Build());
        Assert.True(draft.Build()!.IsPassthrough);
    }

    [Fact]
    public async Task InvalidDraft_Builds_Null()
    {
        // The other half of the null contract: a validation error — here a
        // blanked percent — is exactly what null means.
        var draft = NewDraft();
        await draft.AddRowAsync();
        await draft.SetPercentTextAsync(0, string.Empty);

        Assert.Null(draft.Build());
        Assert.NotNull(draft.ValidationError);
    }

    [Fact]
    public async Task Build_FlushesRowsInOrder_WithToggleAndLength()
    {
        var draft = NewDraft();
        await draft.AddRowAsync(); // NeverSeen, 50 after the second Add
        await draft.AddRowAsync(); // GotWrong
        await draft.SetRandomOrderAsync(false);
        await draft.SetLengthTextAsync("10");

        var built = draft.Build();

        Assert.NotNull(built);
        Assert.Equal(
            new QuizMix(
                [new QuizMixEntry(QuizCategory.NeverSeen, 50), new QuizMixEntry(QuizCategory.GotWrong, 50)],
                quizLength: 10, randomOrder: false),
            built);
    }

    // -----------------------------------------------------------------------
    //  Last-valid write-through persistence
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidMutation_PersistsTheBuiltMix()
    {
        // Persistence follows the screen: no commit gesture exists, so the
        // mutation itself is what writes. The blob is the built mix in the
        // unchanged lib wire format (same key, no migration).
        var draft = NewDraft();

        await draft.AddRowAsync();

        Assert.Equal(NeverSeenMix(), PersistedMixes().Last());
    }

    [Fact]
    public async Task InvalidMutation_SkipsTheWrite_StorageKeepsLastValidState()
    {
        // RULED (design point A): a mutation that leaves the draft invalid
        // writes nothing, so a reload restores the last well-formed screen
        // state — never a torn half-edit.
        var draft = NewDraft();
        await draft.AddRowAsync();
        var writesAfterAdd = PersistedMixes().Length;

        await draft.SetPercentTextAsync(0, string.Empty); // invalid: no percent

        Assert.Equal(writesAfterAdd, PersistedMixes().Length);
        Assert.Equal(NeverSeenMix(), PersistedMixes().Last()); // the Add's blob still stands

        // The edit that restores validity writes through again.
        await draft.SetPercentTextAsync(0, "100");
        Assert.Equal(writesAfterAdd + 1, PersistedMixes().Length);
        Assert.Equal(NeverSeenMix(), PersistedMixes().Last());
    }

    [Fact]
    public async Task Clear_PersistsTheBlankMix()
    {
        // Clear's one honest job: deliberately removing the rows, storage
        // following the screen. The blank draft builds Empty (see the Build
        // pin above), so the write-through persists the blank mix rather than
        // skipping.
        var draft = NewDraft();
        await draft.AddRowAsync();

        await draft.ClearAsync();

        Assert.Empty(draft.Rows);
        Assert.True(PersistedMixes().Last().IsPassthrough);
    }

    [Fact]
    public async Task RemovingTheLastRow_PersistsTheBlankMix()
    {
        // The last-row removal is an edit like any other now — no panel
        // auto-commit path. It lands blank, blank builds Empty, Empty writes
        // through.
        var draft = NewDraft();
        await draft.AddRowAsync();

        await draft.RemoveRowAsync(0);

        Assert.Empty(draft.Rows);
        Assert.True(PersistedMixes().Last().IsPassthrough);
    }

    [Fact]
    public async Task Discard_PersistsNothing_TheStoredMixSurvivesTheSetupEnd()
    {
        // The Clear/Discard asymmetry is §4's choice-vs-consent line: ending a
        // setup blanks the DRAFT but must leave the STORED mix for the next
        // setup's hydration to re-offer. A Discard that wrote blank through
        // would delete the user's mix on every pick.
        var draft = NewDraft();
        await draft.AddRowAsync();
        var writesBeforeDiscard = PersistedMixes().Length;

        draft.Discard();

        Assert.Empty(draft.Rows);
        Assert.Equal(writesBeforeDiscard, PersistedMixes().Length);
        Assert.Equal(NeverSeenMix(), PersistedMixes().Last());
    }

    // -----------------------------------------------------------------------
    //  Hydration lifecycle (once per setup)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EnsureHydrated_LoadsStoredMix_Once()
    {
        var stored = new QuizMix(
            [new QuizMixEntry(QuizCategory.GotWrong, 60), new QuizMixEntry(QuizCategory.SeenFewerThan(3), 40)],
            quizLength: 25, randomOrder: false);
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult(stored.ToJson());
        var draft = NewDraft();

        await draft.EnsureHydratedAsync();

        // Projected in wire order, buffers rendered for editing.
        Assert.Equal(2, draft.Rows.Count);
        Assert.Equal(QuizCategoryKind.GotWrong, draft.Rows[0].Kind);
        Assert.Equal(QuizCategoryKind.SeenFewerThan, draft.Rows[1].Kind);
        Assert.Equal("3", draft.Rows[1].ParamText);
        Assert.Equal("25", draft.LengthText);
        Assert.False(draft.RandomOrder);
        // Round-trip identity: what hydration shows is exactly what was stored.
        Assert.Equal(stored, draft.Build());

        // Idempotent per setup: a re-mounting panel triggers no second read and
        // cannot overwrite edits the surviving draft holds.
        await draft.EnsureHydratedAsync();
        Assert.Single(JSInterop.Invocations["localStorage.getItem"]);
    }

    [Fact]
    public async Task EnsureHydrated_CorruptOrMissing_LeavesBlankDraftDefaults()
    {
        // Tolerant restore, and only a SUCCESSFUL parse projects: TryFromJson's
        // Empty fallback must not overwrite the blank draft's own defaults.
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult("}{ not valid json");
        var draft = NewDraft();

        await draft.EnsureHydratedAsync();

        Assert.Empty(draft.Rows);
        Assert.True(draft.RandomOrder);
        Assert.Equal(QuizMix.Empty, draft.Build()); // blank hydration is inert
    }

    [Fact]
    public async Task Hydration_FillsTheDraftOnly_NeverWritesStorage()
    {
        // Hydration is a read: restoring the stored mix must not echo it back
        // as a write (screen-follows-storage is about EDITS; a boot that wrote
        // storage would churn the blob for no gesture at all).
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult(NeverSeenMix().ToJson());
        var draft = NewDraft();

        await draft.EnsureHydratedAsync();

        Assert.Empty(PersistedMixes());
    }

    [Fact]
    public async Task Discard_BlanksTheDraft_AndForgetsHydration()
    {
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult(NeverSeenMix().ToJson());
        var draft = NewDraft();
        await draft.EnsureHydratedAsync();
        await draft.SetRandomOrderAsync(false);
        await draft.SetLengthTextAsync("7");

        draft.Discard();

        // The setup's edits are gone and the blank-builder defaults are back…
        Assert.Empty(draft.Rows);
        Assert.True(draft.RandomOrder);
        Assert.Equal(string.Empty, draft.LengthText);
        Assert.Equal(QuizMix.Empty, draft.Build());

        // …and hydration is forgotten, so the next setup's panel mount re-reads
        // the stored mix and re-offers it afresh.
        await draft.EnsureHydratedAsync();
        Assert.Equal(2, JSInterop.Invocations["localStorage.getItem"].Count);
        Assert.Single(draft.Rows);
    }

    [Fact]
    public async Task Clear_BlanksTheDraft_ButStaysHydrated()
    {
        // Clear is the blank the user asked for INSIDE a setup: the draft goes
        // blank (and the blank persists — see the write-through pin) but the
        // setup keeps its hydration — no re-read re-offers the stored mix
        // behind the user's back after they explicitly blanked the builder.
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult(NeverSeenMix().ToJson());
        var draft = NewDraft();
        await draft.EnsureHydratedAsync();
        Assert.Single(draft.Rows);

        await draft.ClearAsync();

        Assert.Empty(draft.Rows);
        await draft.EnsureHydratedAsync();
        Assert.Single(JSInterop.Invocations["localStorage.getItem"]); // no second read
        Assert.Empty(draft.Rows);
    }

    [Fact]
    public async Task Discard_WhileHydrationInFlight_LandsNothing()
    {
        // The stale-async guard: the setup can end (pick gesture) while the
        // hydration's localStorage read is still awaited. The late result must
        // not land rows on the discarded draft — the next setup re-reads.
        var planned = JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey);
        var draft = NewDraft();
        var inFlight = draft.EnsureHydratedAsync();

        draft.Discard();
        planned.SetResult(NeverSeenMix().ToJson());
        await inFlight;

        Assert.Empty(draft.Rows);
        Assert.Equal(QuizMix.Empty, draft.Build());
    }

    // -----------------------------------------------------------------------
    //  Change notification
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EveryMutation_RaisesChanged()
    {
        // The state-container contract Home's gate rendering depends on: any
        // write path a component can take must notify, or the Start button
        // renders against a stale derivation.
        var draft = NewDraft();
        var raised = 0;
        draft.Changed += () => raised++;

        await draft.AddRowAsync();
        await draft.SetKindAsync(0, QuizCategoryKind.WrongRateOver);
        await draft.SetParamTextAsync(0, "40");
        await draft.SetPercentTextAsync(0, "100");
        await draft.SetRandomOrderAsync(false);
        await draft.SetLengthTextAsync("5");
        await draft.AddRowAsync();
        await draft.MoveRowAsync(1, -1);
        await draft.RemoveRowAsync(0);
        await draft.ClearAsync();
        draft.Discard();
        Assert.Equal(11, raised);

        await draft.EnsureHydratedAsync(); // hydration settles → notify too
        Assert.Equal(12, raised);
    }
}
