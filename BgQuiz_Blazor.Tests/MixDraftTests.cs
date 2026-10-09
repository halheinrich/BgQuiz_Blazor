using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

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
/// user-visible, in <see cref="MixPanelTests"/>. Storage is planned with
/// <see cref="BrowserStoragePlan"/> where it is a test's subject (see
/// <see cref="Plan"/>), so the real <see cref="BrowserStorage"/> runs and no
/// interop call is spelled here; elsewhere it is incidental (Loose).
/// </summary>
public class MixDraftTests : BunitContext
{
    public MixDraftTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose; // incidental storage: nothing stored, every write lands
    }

    /// <summary>The app's one storage fact, which a refusal here is reported to (halheinrich/backgammon#360).</summary>
    private readonly BrowserStorageCondition _storage = new();

    private readonly RecordingLogger<MixDraft> _log = new();

    private MixDraft NewDraft() => new(new BrowserStorage(JSInterop.JSRuntime), _log, _storage);

    private BrowserStoragePlan? _plan;

    /// <summary>
    /// The storage plan, put on the runtime by the first test step that states
    /// a storage call: from then on every call is answered as declared, an
    /// undeclared one — a write nobody expected, a second read — fails where it
    /// is made, and the test verifies the plan.
    /// </summary>
    private BrowserStoragePlan Plan => _plan ??= BrowserStoragePlan.On(JSInterop);

    /// <summary>The mix entry as the browser holds it, or no entry for <see langword="null"/>.</summary>
    private void StageStored(string? json) =>
        Plan.ExpectRead(
            BrowserStorageArea.Local, MixDraft.StorageKey,
            json is null ? BrowserStorageReadAnswer.Absent : BrowserStorageReadAnswer.Stored(json));

    /// <summary>
    /// One write of <paramref name="mix"/>, in the lib's own wire format — the
    /// one format, owned by the lib, that stored mixes have always had.
    /// </summary>
    private BrowserStorageExpectation ExpectPersisted(QuizMix mix, BrowserStorageWriteAnswer? answer = null) =>
        Plan.ExpectWrite(
            BrowserStorageArea.Local, MixDraft.StorageKey, mix.ToJson(), answer ?? BrowserStorageWriteAnswer.Succeeded);

    /// <summary>A refusal is said twice: one warning carrying the browser's exception, and the report.</summary>
    private void AssertRefusalSaid()
    {
        var entry = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<JSException>(entry.Exception);
        Assert.NotNull(_storage.Occurrence);
    }

    /// <summary>A one-row never-seen mix, deterministic content — what one Add builds (NeverSeen seeds at 100%).</summary>
    private static QuizMix NeverSeenMix() =>
        new([new QuizMixEntry(QuizCategory.NeverSeen, 100)], quizLength: null, randomOrder: true);

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
        ExpectPersisted(NeverSeenMix());
        var draft = NewDraft();

        await draft.AddRowAsync();

        Plan.Verify();
    }

    [Fact]
    public async Task InvalidMutation_SkipsTheWrite_StorageKeepsLastValidState()
    {
        // RULED (design point A): a mutation that leaves the draft invalid
        // writes nothing, so a reload restores the last well-formed screen
        // state — never a torn half-edit.
        ExpectPersisted(NeverSeenMix());
        var draft = NewDraft();
        await draft.AddRowAsync();
        Plan.Verify();

        await draft.SetPercentTextAsync(0, string.Empty); // invalid: no percent

        Plan.Verify(); // nothing new was written: any write would have been undeclared

        // The edit that restores validity writes through again.
        ExpectPersisted(NeverSeenMix());
        await draft.SetPercentTextAsync(0, "100");
        Plan.Verify();
    }

    [Fact]
    public async Task Clear_PersistsTheBlankMix()
    {
        // Clear's one honest job: deliberately removing the rows, storage
        // following the screen. The blank draft builds Empty (see the Build
        // pin above), so the write-through persists the blank mix rather than
        // skipping.
        Plan.RequireOrder(ExpectPersisted(NeverSeenMix()), ExpectPersisted(QuizMix.Empty));
        var draft = NewDraft();
        await draft.AddRowAsync();

        await draft.ClearAsync();

        Assert.Empty(draft.Rows);
        Plan.Verify();
    }

    [Fact]
    public async Task RemovingTheLastRow_PersistsTheBlankMix()
    {
        // The last-row removal is an edit like any other now — no panel
        // auto-commit path. It lands blank, blank builds Empty, Empty writes
        // through.
        Plan.RequireOrder(ExpectPersisted(NeverSeenMix()), ExpectPersisted(QuizMix.Empty));
        var draft = NewDraft();
        await draft.AddRowAsync();

        await draft.RemoveRowAsync(0);

        Assert.Empty(draft.Rows);
        Plan.Verify();
    }

    [Fact]
    public async Task Discard_PersistsNothing_TheStoredMixSurvivesTheSetupEnd()
    {
        // The Clear/Discard asymmetry is §4's line drawn through the draft:
        // what the user deliberately removed is a choice and is written down,
        // while a setup ENDING is not a decision about the rows at all. So
        // ending a setup blanks the DRAFT and must leave the STORED mix for the
        // next setup's hydration to re-offer. A Discard that wrote blank
        // through would delete the user's mix on every pick.
        ExpectPersisted(NeverSeenMix());
        var draft = NewDraft();
        await draft.AddRowAsync();

        draft.Discard();

        Assert.Empty(draft.Rows);
        Plan.Verify(); // the Add's write and nothing after it
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
        StageStored(stored.ToJson());
        var draft = NewDraft();

        await draft.EnsureHydratedAsync();

        // Projected in wire order, buffers rendered for editing.
        Assert.Equal(2, draft.Rows.Length);
        Assert.Equal(QuizCategoryKind.GotWrong, draft.Rows[0].Kind);
        Assert.Equal(QuizCategoryKind.SeenFewerThan, draft.Rows[1].Kind);
        Assert.Equal("3", draft.Rows[1].ParamText);
        Assert.Equal("25", draft.LengthText);
        Assert.False(draft.RandomOrder);
        // Round-trip identity: what hydration shows is exactly what was stored.
        Assert.Equal(stored, draft.Build());

        // Idempotent per setup: a re-mounting panel triggers no second read and
        // cannot overwrite edits the surviving draft holds. One read declared.
        await draft.EnsureHydratedAsync();
        Plan.Verify();
    }

    [Theory]
    [InlineData(null)]                 // missing
    [InlineData("}{ not valid json")]  // corrupt
    public async Task EnsureHydrated_CorruptOrMissing_LeavesBlankDraftDefaults(string? stored)
    {
        // Tolerant restore, and only a SUCCESSFUL parse projects: TryFromJson's
        // Empty fallback must not overwrite the blank draft's own defaults.
        StageStored(stored);
        var draft = NewDraft();

        await draft.EnsureHydratedAsync();

        Assert.Empty(draft.Rows);
        Assert.True(draft.RandomOrder);
        Assert.Equal(QuizMix.Empty, draft.Build()); // blank hydration is inert
        Plan.Verify();
    }

    [Fact]
    public async Task Hydration_FillsTheDraftOnly_NeverWritesStorage()
    {
        // Hydration is a read: restoring the stored mix must not echo it back
        // as a write (screen-follows-storage is about EDITS; a boot that wrote
        // storage would churn the blob for no gesture at all). No write is
        // declared, so one would fail where it was made.
        StageStored(NeverSeenMix().ToJson());
        var draft = NewDraft();

        await draft.EnsureHydratedAsync();

        Plan.Verify();
    }

    [Fact]
    public async Task Discard_BlanksTheDraft_AndForgetsHydration()
    {
        StageStored(NeverSeenMix().ToJson());
        ExpectPersisted(new QuizMix([new QuizMixEntry(QuizCategory.NeverSeen, 100)], quizLength: null, randomOrder: false));
        ExpectPersisted(new QuizMix([new QuizMixEntry(QuizCategory.NeverSeen, 100)], quizLength: 7, randomOrder: false));
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
        // the stored mix and re-offers it afresh — a second read, declared.
        StageStored(NeverSeenMix().ToJson());
        await draft.EnsureHydratedAsync();
        Assert.Single(draft.Rows);
        Plan.Verify();
    }

    [Fact]
    public async Task Clear_BlanksTheDraft_ButStaysHydrated()
    {
        // Clear is the blank the user asked for INSIDE a setup: the draft goes
        // blank (and the blank persists — see the write-through pin) but the
        // setup keeps its hydration — no re-read re-offers the stored mix
        // behind the user's back after they explicitly blanked the builder.
        StageStored(NeverSeenMix().ToJson());
        ExpectPersisted(QuizMix.Empty);
        var draft = NewDraft();
        await draft.EnsureHydratedAsync();
        Assert.Single(draft.Rows);

        await draft.ClearAsync();

        Assert.Empty(draft.Rows);
        await draft.EnsureHydratedAsync(); // no second read: one declared
        Assert.Empty(draft.Rows);
        Plan.Verify();
    }

    [Fact]
    public async Task Discard_WhileHydrationInFlight_LandsNothing()
    {
        // The stale-async guard: the setup can end (pick gesture) while the
        // hydration's storage read is still pending. The late result must not
        // land rows on the discarded draft — the next setup re-reads.
        var read = Plan.ExpectHeldRead(BrowserStorageArea.Local, MixDraft.StorageKey);
        var draft = NewDraft();
        var inFlight = draft.EnsureHydratedAsync();
        Assert.True(read.IsReached);

        draft.Discard();
        read.Release(BrowserStorageReadAnswer.Stored(NeverSeenMix().ToJson()));
        await inFlight.WaitAsync(DefaultWaitTimeout); // the hydration's continuation has run

        Assert.Empty(draft.Rows);
        Assert.Equal(QuizMix.Empty, draft.Build());
        Plan.Verify();
    }

    // -----------------------------------------------------------------------
    //  Storage the browser refuses (halheinrich/backgammon#360)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hydration_ARefusedRead_LeavesTheDraftBlank_IsSaid_AndThrowsNothing()
    {
        // The read MixPanel's init awaits, on the way to Home's first render:
        // refused, it hydrates nothing — the blank draft, as for a missing key.
        Plan.ExpectRead(BrowserStorageArea.Local, MixDraft.StorageKey, BrowserStorageReadAnswer.Refused);
        var draft = NewDraft();

        await draft.EnsureHydratedAsync();

        Assert.Empty(draft.Rows);
        Assert.Equal(QuizMix.Empty, draft.Build());
        AssertRefusalSaid();
        Plan.Verify();
    }

    [Fact]
    public async Task Hydration_ARefusedRead_StaysOncePerSetup()
    {
        // The cached task holds for the refused read too: a re-mount does not
        // ask the browser again within the setup, and Discard still forgets it
        // — the refusal latched nothing, so the next setup asks again.
        Plan.ExpectRead(BrowserStorageArea.Local, MixDraft.StorageKey, BrowserStorageReadAnswer.Refused);
        var draft = NewDraft();

        await draft.EnsureHydratedAsync();
        await draft.EnsureHydratedAsync();
        Plan.Verify(); // one read

        Plan.ExpectRead(BrowserStorageArea.Local, MixDraft.StorageKey, BrowserStorageReadAnswer.Refused);
        draft.Discard();
        await draft.EnsureHydratedAsync();
        Plan.Verify(); // and a second, after the setup ended
    }

    [Fact]
    public async Task AWriteTheBrowserRefuses_KeepsTheDraft_IsSaid_AndThrowsNothing()
    {
        // Silent before halheinrich/backgammon#360; still the screen's truth,
        // and now said.
        ExpectPersisted(NeverSeenMix(), BrowserStorageWriteAnswer.Refused);
        var draft = NewDraft();

        await draft.AddRowAsync();

        Assert.Equal(NeverSeenMix(), draft.Build());
        AssertRefusalSaid();
        Plan.Verify();
    }

    [Fact]
    public async Task ARefusedWrite_DisablesNoLaterOne()
    {
        // No latch (halheinrich/backgammon#374): the next valid edit is
        // written all the same, and lands.
        ExpectPersisted(NeverSeenMix(), BrowserStorageWriteAnswer.Refused);
        ExpectPersisted(new QuizMix([new QuizMixEntry(QuizCategory.NeverSeen, 100)], quizLength: null, randomOrder: false));
        var draft = NewDraft();

        await draft.AddRowAsync();
        await draft.SetRandomOrderAsync(false);

        AssertRefusalSaid();
        Plan.Verify();
    }

    [Fact]
    public async Task Hydration_AfterAnotherStoresRefusal_StillReadsAndRestores()
    {
        // The fact reports and never gates: another store's refused write does
        // not establish that this read will fail, so it is made and kept.
        _storage.ReportRefused();
        StageStored(NeverSeenMix().ToJson());
        var draft = NewDraft();

        await draft.EnsureHydratedAsync();

        Assert.Equal(NeverSeenMix(), draft.Build());
        Assert.Empty(_log.Entries);
        Plan.Verify();
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
