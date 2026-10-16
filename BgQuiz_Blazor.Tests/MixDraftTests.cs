using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="MixDraft"/>: the last-valid write-through (every mutation that validates
/// persists the built mix, blank included; an invalid one skips the write), the ruled blank
/// ⇒ <see cref="QuizMix.Empty"/> line of <see cref="MixDraft.Build"/>, the once-per-visit
/// hydration, and storage the browser refuses. Storage is planned with
/// <see cref="BrowserStoragePlan"/> where it is a test's subject, so the real
/// <see cref="BrowserStorage"/> runs; elsewhere it is incidental (Loose).
/// </summary>
public class MixDraftTests : BunitContext
{
    public MixDraftTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private readonly BrowserStorageCondition _storage = new();

    private readonly RecordingLogger<MixDraft> _log = new();

    private MixDraft NewDraft() => new(new BrowserStorage(JSInterop.JSRuntime), _log, _storage);

    private BrowserStoragePlan? _plan;

    private BrowserStoragePlan Plan => _plan ??= BrowserStoragePlan.On(JSInterop);

    private void StageStored(string? json) =>
        Plan.ExpectRead(
            BrowserStorageArea.Local, MixDraft.StorageKey,
            json is null ? BrowserStorageReadAnswer.Absent : BrowserStorageReadAnswer.Stored(json));

    private BrowserStorageExpectation ExpectPersisted(QuizMix mix, BrowserStorageWriteAnswer? answer = null) =>
        Plan.ExpectWrite(
            BrowserStorageArea.Local, MixDraft.StorageKey, mix.ToJson(), answer ?? BrowserStorageWriteAnswer.Succeeded);

    private void AssertRefusalSaid()
    {
        var entry = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<JSException>(entry.Exception);
        Assert.NotNull(_storage.Occurrence);
    }

    /// <summary>A one-row never-seen mix: what one Add builds.</summary>
    private static QuizMix NeverSeenMix(int? quizLength = null) =>
        new([new QuizMixEntry(QuizCategory.NeverSeen, 100)], quizLength);

    [Fact]
    public void BlankDraft_Builds_Empty_NeverNull()
    {
        // Ruled: blank is the passthrough in effect, never the gated null.
        var draft = NewDraft();

        Assert.NotNull(draft.Build());
        Assert.Equal(QuizMix.Empty, draft.Build());
        Assert.True(draft.Build()!.IsPassthrough);
    }

    [Fact]
    public async Task InvalidDraft_Builds_Null()
    {
        var draft = NewDraft();
        await draft.AddRowAsync();
        await draft.SetPercentTextAsync(0, string.Empty);

        Assert.Null(draft.Build());
        Assert.NotNull(draft.ValidationError);
    }

    [Fact]
    public async Task Build_FlushesRowsInOrder_WithLength()
    {
        var draft = NewDraft();
        await draft.AddRowAsync(); // NeverSeen, 50 after the second Add
        await draft.AddRowAsync(); // GotWrong
        await draft.SetLengthTextAsync("10");

        var built = draft.Build();

        Assert.NotNull(built);
        Assert.Equal(
            new QuizMix(
                [new QuizMixEntry(QuizCategory.NeverSeen, 50), new QuizMixEntry(QuizCategory.GotWrong, 50)],
                quizLength: 10),
            built);
    }

    [Fact]
    public async Task ValidMutation_PersistsTheBuiltMix()
    {
        ExpectPersisted(NeverSeenMix());
        var draft = NewDraft();

        await draft.AddRowAsync();

        Plan.Verify();
    }

    [Fact]
    public async Task InvalidMutation_SkipsTheWrite_StorageKeepsLastValidState()
    {
        ExpectPersisted(NeverSeenMix());
        var draft = NewDraft();
        await draft.AddRowAsync();
        Plan.Verify();

        await draft.SetPercentTextAsync(0, string.Empty); // invalid: no percent

        Plan.Verify(); // nothing new was written

        ExpectPersisted(NeverSeenMix());
        await draft.SetPercentTextAsync(0, "100");
        Plan.Verify();
    }

    [Fact]
    public async Task Clear_PersistsTheBlankMix()
    {
        Plan.RequireOrder(ExpectPersisted(NeverSeenMix()), ExpectPersisted(QuizMix.Empty));
        var draft = NewDraft();
        await draft.AddRowAsync();

        await draft.ClearAsync();

        Assert.Empty(draft.Rows);
        Assert.Equal(string.Empty, draft.LengthText);
        Plan.Verify();
    }

    [Fact]
    public async Task RemovingTheLastRow_PersistsTheBlankMix()
    {
        Plan.RequireOrder(ExpectPersisted(NeverSeenMix()), ExpectPersisted(QuizMix.Empty));
        var draft = NewDraft();
        await draft.AddRowAsync();

        await draft.RemoveRowAsync(0);

        Assert.Empty(draft.Rows);
        Plan.Verify();
    }

    /// <summary>
    /// A mix stored before the order toggle was retired — schema 1, its toggle on — still
    /// hydrates with no migration: its rows and length come back, the toggle is dropped on
    /// read by the producer, and the next edit writes the current schema.
    /// </summary>
    [Fact]
    public async Task EnsureHydrated_ASchemaOneMixWithItsToggleOn_RestoresItsRows_AndTheNextWriteIsCurrent()
    {
        StageStored("""{"schemaVersion":1,"randomOrder":true,"quizLength":12,"entries":[{"category":{"kind":"NeverSeen"},"percent":100}]}""");
        ExpectPersisted(NeverSeenMix(quizLength: 13));
        var draft = NewDraft();

        await draft.EnsureHydratedAsync();

        Assert.Equal(NeverSeenMix(quizLength: 12), draft.Build());
        await draft.SetLengthTextAsync("13");
        Plan.Verify();
    }

    [Fact]
    public async Task EnsureHydrated_LoadsStoredMix_Once()
    {
        var stored = new QuizMix(
            [new QuizMixEntry(QuizCategory.GotWrong, 60), new QuizMixEntry(QuizCategory.SeenFewerThan(3), 40)],
            quizLength: 25);
        StageStored(stored.ToJson());
        var draft = NewDraft();

        await draft.EnsureHydratedAsync();

        Assert.Equal(2, draft.Rows.Length);
        Assert.Equal(QuizCategoryKind.GotWrong, draft.Rows[0].Kind);
        Assert.Equal(QuizCategoryKind.SeenFewerThan, draft.Rows[1].Kind);
        Assert.Equal("3", draft.Rows[1].ParamText);
        Assert.Equal("25", draft.LengthText);
        Assert.Equal(stored, draft.Build());

        // Idempotent: a re-mounting panel triggers no second read. One read declared.
        await draft.EnsureHydratedAsync();
        Plan.Verify();
    }

    [Theory]
    [InlineData(null)]                 // missing
    [InlineData("}{ not valid json")]  // corrupt
    public async Task EnsureHydrated_CorruptOrMissing_LeavesBlankDraftDefaults(string? stored)
    {
        StageStored(stored);
        var draft = NewDraft();

        await draft.EnsureHydratedAsync();

        Assert.Empty(draft.Rows);
        Assert.Equal(QuizMix.Empty, draft.Build());
        Plan.Verify();
    }

    [Fact]
    public async Task Hydration_FillsTheDraftOnly_NeverWritesStorage()
    {
        StageStored(NeverSeenMix().ToJson());
        var draft = NewDraft();

        await draft.EnsureHydratedAsync();

        Plan.Verify(); // no write declared, so one would have failed
    }

    [Fact]
    public async Task Clear_BlanksTheDraft_AndStaysHydrated()
    {
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
    public async Task Hydration_ARefusedRead_LeavesTheDraftBlank_IsSaid_AndThrowsNothing()
    {
        Plan.ExpectRead(BrowserStorageArea.Local, MixDraft.StorageKey, BrowserStorageReadAnswer.Refused);
        var draft = NewDraft();

        await draft.EnsureHydratedAsync();

        Assert.Empty(draft.Rows);
        Assert.Equal(QuizMix.Empty, draft.Build());
        AssertRefusalSaid();
        Plan.Verify();
    }

    [Fact]
    public async Task Hydration_ARefusedRead_IsStillOnce()
    {
        Plan.ExpectRead(BrowserStorageArea.Local, MixDraft.StorageKey, BrowserStorageReadAnswer.Refused);
        var draft = NewDraft();

        await draft.EnsureHydratedAsync();
        await draft.EnsureHydratedAsync();

        Plan.Verify(); // one read
    }

    [Fact]
    public async Task AWriteTheBrowserRefuses_KeepsTheDraft_IsSaid_AndThrowsNothing()
    {
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
        ExpectPersisted(NeverSeenMix(), BrowserStorageWriteAnswer.Refused);
        ExpectPersisted(new QuizMix([new QuizMixEntry(QuizCategory.NeverSeen, 100)], quizLength: 7));
        var draft = NewDraft();

        await draft.AddRowAsync();
        await draft.SetLengthTextAsync("7");

        AssertRefusalSaid();
        Plan.Verify();
    }

    [Fact]
    public async Task Hydration_AfterAnotherStoresRefusal_StillReadsAndRestores()
    {
        _storage.ReportRefused();
        StageStored(NeverSeenMix().ToJson());
        var draft = NewDraft();

        await draft.EnsureHydratedAsync();

        Assert.Equal(NeverSeenMix(), draft.Build());
        Assert.Empty(_log.Entries);
        Plan.Verify();
    }

    [Fact]
    public async Task EveryMutation_RaisesChanged()
    {
        var draft = NewDraft();
        var raised = 0;
        draft.Changed += () => raised++;

        await draft.AddRowAsync();
        await draft.SetKindAsync(0, QuizCategoryKind.WrongRateOver);
        await draft.SetParamTextAsync(0, "40");
        await draft.SetPercentTextAsync(0, "100");
        await draft.SetLengthTextAsync("5");
        await draft.AddRowAsync();
        await draft.MoveRowAsync(1, -1);
        await draft.RemoveRowAsync(0);
        await draft.ClearAsync();
        Assert.Equal(9, raised);

        await draft.EnsureHydratedAsync(); // hydration settles → notify too
        Assert.Equal(10, raised);
    }
}
