using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Bunit;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="MixDraft"/> — the app-scoped mix edit state and the
/// <b>derived</b> start-gate rule that replaced the stored dirty flag. Pins
/// the derivation matrix (<see cref="MixDraft.Matches"/>: blank-vs-passthrough
/// clean with no special case, any divergence — content, order, toggle,
/// length, or unbuildability — dirty, and agreement clean <i>however</i> it
/// was reached), the once-per-setup hydration lifecycle
/// (<see cref="MixDraft.EnsureHydratedAsync"/> idempotent;
/// <see cref="MixDraft.Discard"/> forgets it, <see cref="MixDraft.Clear"/>
/// does not; a read still in flight when the setup ends lands nothing), and
/// the committed-only persistence round-trip. The builder policies the draft
/// inherited from the panel (rebalance, next-unused-kind, validation wording)
/// stay pinned where they are user-visible, in <see cref="MixPanelTests"/>.
/// Extends <see cref="BunitContext"/> only for the JSInterop double behind
/// the draft's localStorage reads/writes.
/// </summary>
public class MixDraftTests : BunitContext
{
    public MixDraftTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose; // getItem → null unless a test sets a value
    }

    private MixDraft NewDraft() => new(JSInterop.JSRuntime);

    /// <summary>A one-row never-seen mix, deterministic order — the smallest committable draft content.</summary>
    private static QuizMix NeverSeenMix() =>
        new([new QuizMixEntry(QuizCategory.NeverSeen, 100)], quizLength: null, randomOrder: true);

    /// <summary>Stage a draft holding exactly <see cref="NeverSeenMix"/> (one Add — NeverSeen seeds at 100%).</summary>
    private static MixDraft DraftWithNeverSeenRow(MixDraft draft)
    {
        draft.AddRow();
        return draft;
    }

    // -----------------------------------------------------------------------
    //  The derived gate rule (Matches)
    // -----------------------------------------------------------------------

    [Fact]
    public void FreshBlankDraft_MatchesPassthrough()
    {
        // The fresh-load default on both sides: a blank draft builds Empty and
        // a fresh holder is Empty, so a user who never touches the mix is
        // never gated — no special case, just the equality.
        var draft = NewDraft();

        Assert.Equal(QuizMix.Empty, draft.Build());
        Assert.True(draft.Matches(QuizMix.Empty));
    }

    [Fact]
    public void EditedDraft_DoesNotMatchPassthrough()
    {
        var draft = DraftWithNeverSeenRow(NewDraft());

        Assert.False(draft.Matches(QuizMix.Empty));
    }

    [Fact]
    public void DraftAgreeingWithCommitted_Matches_ByContentNotReference()
    {
        // The leg-1 primitive doing the work: the committed mix is a different
        // instance than anything the draft will build, so only content
        // equality can ever report agreement.
        var draft = DraftWithNeverSeenRow(NewDraft());

        Assert.True(draft.Matches(NeverSeenMix()));
    }

    [Fact]
    public void IdenticalReEdit_DerivesCleanAgain()
    {
        // The deferred displayed==committed variant, free under derivation: an
        // edit away from the committed content gates, and the edit BACK to the
        // exact committed content un-gates — no Apply, no reconcile, nothing
        // stored to clear (the stored flag stayed dirty here until re-Apply).
        var draft = NewDraft();
        draft.AddRow(); // NeverSeen, 100
        var committed = NeverSeenMix();
        Assert.True(draft.Matches(committed));

        draft.SetPercentText(0, "90");
        Assert.False(draft.Matches(committed)); // diverged (and unbuildable at 90)

        draft.SetPercentText(0, "100");
        Assert.True(draft.Matches(committed)); // agreement restored by the edit itself
    }

    [Fact]
    public void Reorder_DerivesDirty_AndReorderBackClean()
    {
        // Order is semantic — the producer draws contested overlap toward the
        // earlier row — so the same rows reordered are a DIFFERENT mix and must
        // gate, even though no text changed.
        var draft = NewDraft();
        draft.AddRow(); // NeverSeen, 50 after the second Add
        draft.AddRow(); // GotWrong
        var committed = draft.Build();
        Assert.NotNull(committed);
        Assert.True(draft.Matches(committed!));

        draft.MoveRow(1, -1);
        Assert.False(draft.Matches(committed!));

        draft.MoveRow(0, +1);
        Assert.True(draft.Matches(committed!));
    }

    [Fact]
    public void RandomOrderAndLength_ParticipateInTheRule()
    {
        // The full member surface gates, not just the rows: toggling Random
        // order or editing the length is a real divergence from the committed
        // mix (both flow into the built QuizMix and its equality).
        var draft = DraftWithNeverSeenRow(NewDraft());
        var committed = NeverSeenMix();
        Assert.True(draft.Matches(committed));

        draft.SetRandomOrder(false);
        Assert.False(draft.Matches(committed));
        draft.SetRandomOrder(true);
        Assert.True(draft.Matches(committed));

        draft.SetLengthText("10");
        Assert.False(draft.Matches(committed));
        draft.SetLengthText(string.Empty);
        Assert.True(draft.Matches(committed));
    }

    [Fact]
    public void UnbuildableDraft_IsDirtyByDefinition()
    {
        // An unbuildable draft can never agree with any commitment — including
        // the passthrough default — so it gates without needing a judgment
        // about "what it would have meant".
        var draft = DraftWithNeverSeenRow(NewDraft());
        draft.SetPercentText(0, string.Empty);

        Assert.Null(draft.Build());
        Assert.False(draft.Matches(QuizMix.Empty));
        Assert.False(draft.Matches(NeverSeenMix()));
    }

    [Fact]
    public void Matches_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => NewDraft().Matches(null!));
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
        Assert.True(draft.Matches(stored));

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
        Assert.True(draft.Matches(QuizMix.Empty)); // blank hydration never gates
    }

    [Fact]
    public async Task Discard_BlanksTheDraft_AndForgetsHydration()
    {
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult(NeverSeenMix().ToJson());
        var draft = NewDraft();
        await draft.EnsureHydratedAsync();
        draft.SetRandomOrder(false);
        draft.SetLengthText("7");

        draft.Discard();

        // The setup's edits are gone and the blank-builder defaults are back…
        Assert.Empty(draft.Rows);
        Assert.True(draft.RandomOrder);
        Assert.Equal(string.Empty, draft.LengthText);
        Assert.True(draft.Matches(QuizMix.Empty));

        // …and hydration is forgotten, so the next setup's panel mount re-reads
        // the stored mix and re-offers it afresh.
        await draft.EnsureHydratedAsync();
        Assert.Equal(2, JSInterop.Invocations["localStorage.getItem"].Count);
        Assert.Single(draft.Rows);
    }

    [Fact]
    public async Task Clear_BlanksTheDraft_ButStaysHydrated()
    {
        // Clear is the blank the user asked for INSIDE a setup (Reset /
        // last-row removal): the draft goes blank but the setup keeps its
        // hydration — no re-read re-offers the stored mix behind the user's
        // back after they explicitly blanked the builder.
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult(NeverSeenMix().ToJson());
        var draft = NewDraft();
        await draft.EnsureHydratedAsync();
        Assert.Single(draft.Rows);

        draft.Clear();

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
        Assert.True(draft.Matches(QuizMix.Empty));
    }

    // -----------------------------------------------------------------------
    //  Persistence (committed-only) and change notification
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Persist_WritesTheOneBlob_RoundTrippable()
    {
        var draft = NewDraft();
        var mix = NeverSeenMix();

        await draft.PersistAsync(mix);

        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == MixDraft.StorageKey)
            .Arguments[1] as string;
        Assert.NotNull(stored);
        Assert.Equal(mix, QuizMix.FromJson(stored!));
    }

    [Fact]
    public async Task Persist_Null_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => NewDraft().PersistAsync(null!));
    }

    [Fact]
    public async Task EveryMutation_RaisesChanged()
    {
        // The state-container contract Home's gate rendering depends on: any
        // write path a component can take must notify, or the Start button
        // renders against a stale derivation.
        var draft = NewDraft();
        var raised = 0;
        draft.Changed += () => raised++;

        draft.AddRow();
        draft.SetKind(0, QuizCategoryKind.WrongRateOver);
        draft.SetParamText(0, "40");
        draft.SetPercentText(0, "100");
        draft.SetRandomOrder(false);
        draft.SetLengthText("5");
        draft.AddRow();
        draft.MoveRow(1, -1);
        draft.RemoveRow(0);
        draft.Clear();
        draft.Discard();
        Assert.Equal(11, raised);

        await draft.EnsureHydratedAsync(); // hydration settles → notify too
        Assert.Equal(12, raised);
    }
}
