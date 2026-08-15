using System.Text.Json;
using BgDataTypes_Lib;
using BgGame_Lib;
using BgFolderAccess_Razor;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="QuizStatsStore"/> lifecycle: the Start-time bind (promote +
/// load), per-fold write-back, and the degrade guarantees — a load failure
/// never writes over the user's file, a write failure stops writing without
/// faulting the quiz, and a re-bind resets the failure states.
/// </summary>
public class QuizStatsStoreTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Deterministic clock — the store must never read ambient time.</summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedNow;
    }

    private static PickedProblemFolder EnabledFolder()
    {
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", [new PickedFile("a.xgp", [1, 2, 3])], FolderWriteCapability.Enabled, []);
        return folder;
    }

    private static QuizStatsStore MakeStore(
        FakeFolderAccess fake, PickedProblemFolder? folder = null) =>
        new(fake, new FixedTimeProvider(), folder ?? EnabledFolder());

    /// <summary>
    /// The content key of the <paramref name="problem"/>-th distinct fixture
    /// problem, derived through the producer's one factory. Distinct values are
    /// distinct <i>problems</i> and therefore distinct lifetime records — the
    /// only thing that separates records now that identity is content, not
    /// provenance (the file a decision came from is no longer part of it).
    /// </summary>
    private static ProblemKey PlayKey(int problem = 0) =>
        TestFixtures.KeyOf(TestFixtures.TwoChoiceDecision(
            TestFixtures.MakePlay((8, 5)), TestFixtures.MakePlay((13, 10)), away: problem));

    /// <summary>The cube analog of <see cref="PlayKey"/>; a cube key never collides with a play key (no dice field).</summary>
    private static ProblemKey CubeKey(int problem = 0) =>
        TestFixtures.KeyOf(TestFixtures.CubeDecision(away: problem));

    private static SubmittedPlay PlaySubmission(int problem = 0, bool correct = true) =>
        new(PlayKey(problem), TestFixtures.MakePlay((8, 5)), 0,
            correct ? 0.0 : 0.05, correct);

    private static SubmittedCubeAction CubeSubmission(int problem = 0) =>
        new(CubeKey(problem), new CubeDecisionPair(CubeAction.Double, CubeAction.Take),
            0.0, 0.0, DoublerCorrect: true, TakerCorrect: true);

    // -----------------------------------------------------------------------
    //  BeginQuizAsync — the Start-time bind
    // -----------------------------------------------------------------------

    [Fact]
    public void InitialStatus_IsDisabled()
    {
        var store = MakeStore(new FakeFolderAccess());
        Assert.Equal(QuizStatsStatus.Disabled, store.Status);
    }

    [Fact]
    public async Task BeginQuiz_CapabilityBrowserUnsupported_DisabledWithoutPromote()
    {
        // Capability is the pick-time verdict; a non-Enabled pick must not even
        // touch the JS slots — the bind short-circuits to Disabled.
        var fake = new FakeFolderAccess();
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", [new PickedFile("a.xg", [1])], FolderWriteCapability.BrowserUnsupported, []);
        var store = MakeStore(fake, folder);

        await store.BeginQuizAsync();

        Assert.Equal(QuizStatsStatus.Disabled, store.Status);
        Assert.Equal(0, fake.PromoteCallCount);
    }

    [Fact]
    public async Task BeginQuiz_CapabilityPermissionDenied_Disabled()
    {
        var fake = new FakeFolderAccess();
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", [new PickedFile("a.xg", [1])], FolderWriteCapability.PermissionDenied, []);
        var store = MakeStore(fake, folder);

        await store.BeginQuizAsync();

        Assert.Equal(QuizStatsStatus.Disabled, store.Status);
    }

    [Fact]
    public async Task BeginQuiz_NothingPicked_Disabled()
    {
        // A Start with a cleared/never-populated holder (defensive; the Start
        // gate normally prevents it): capability defaults to non-Enabled.
        var store = MakeStore(new FakeFolderAccess(), new PickedProblemFolder());

        await store.BeginQuizAsync();

        Assert.Equal(QuizStatsStatus.Disabled, store.Status);
    }

    [Fact]
    public async Task BeginQuiz_PromoteFindsNoHandle_Disabled()
    {
        // Enabled capability but the JS picked slot holds no FS-Access handle
        // (e.g. cleared between pick and Start) — the handle-level half of the
        // check degrades to Disabled rather than faulting.
        var fake = new FakeFolderAccess { PromoteResult = false };
        var store = MakeStore(fake);

        await store.BeginQuizAsync();

        Assert.Equal(QuizStatsStatus.Disabled, store.Status);
        Assert.Equal(1, fake.PromoteCallCount);
    }

    [Fact]
    public async Task BeginQuiz_NoStatsFile_ReadySeededEmpty()
    {
        // null read = fresh corpus: Ready over an Empty document — the first
        // fold writes the file into being.
        var fake = new FakeFolderAccess { StatsJson = null };
        var store = MakeStore(fake);

        await store.BeginQuizAsync();

        Assert.Equal(QuizStatsStatus.Ready, store.Status);
        Assert.Empty(fake.Writes); // binding never writes — only folds do
    }

    [Fact]
    public async Task BeginQuiz_ValidStatsFile_ReadyAndFoldsContinueExistingTallies()
    {
        // An existing document loads and later folds accumulate onto its
        // records: one prior submission for this decision on disk, one folded
        // now → the written tally shows two.
        var clock = new FixedTimeProvider();
        var existing = ProblemStatsDocument.Empty.Plus(PlaySubmission(), clock);
        var fake = new FakeFolderAccess
        {
            StatsJson = JsonSerializer.Serialize(existing, QuizStatsFile.SerializerOptions),
        };
        var store = MakeStore(fake);
        await store.BeginQuizAsync();
        Assert.Equal(QuizStatsStatus.Ready, store.Status);

        await store.RecordAsync(PlaySubmission());

        var written = JsonSerializer.Deserialize<ProblemStatsDocument>(fake.Writes.Single());
        Assert.NotNull(written);
        var record = Assert.Single(written.Problems).Value;
        Assert.Equal(2, record.Tally.Submitted);
        Assert.Equal(2, record.Tally.Correct);
    }

    [Fact]
    public async Task BeginQuiz_CorruptStatsFile_LoadFailedRecordsNothingWritesNothing()
    {
        // The file-untouched guarantee: an unparseable file flips LoadFailed,
        // and no code path may ever write over it this quiz — folds are no-ops.
        var fake = new FakeFolderAccess { StatsJson = "not json at all" };
        var store = MakeStore(fake);

        await store.BeginQuizAsync();
        await store.RecordAsync(PlaySubmission());
        await store.RecordAsync(CubeSubmission());

        Assert.Equal(QuizStatsStatus.LoadFailed, store.Status);
        Assert.Empty(fake.Writes);
    }

    [Fact]
    public async Task BeginQuiz_ForeignSchemaJson_LoadFailed()
    {
        // Structurally valid JSON that isn't a stats document (foreign or
        // newer-schema file) — the converter's fail-loud read is the detector.
        var fake = new FakeFolderAccess { StatsJson = """{"someOtherApp":true}""" };
        var store = MakeStore(fake);

        await store.BeginQuizAsync();

        Assert.Equal(QuizStatsStatus.LoadFailed, store.Status);
    }

    [Fact]
    public async Task BeginQuiz_ReadThrowsJs_LoadFailed()
    {
        var fake = new FakeFolderAccess { ReadException = new JSException("read failed") };
        var store = MakeStore(fake);

        await store.BeginQuizAsync();

        Assert.Equal(QuizStatsStatus.LoadFailed, store.Status);
        Assert.Empty(fake.Writes);
    }

    // -----------------------------------------------------------------------
    //  RecordAsync — fold + write-back per submission
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Record_BeforeAnyBind_NoOp()
    {
        var fake = new FakeFolderAccess();
        var store = MakeStore(fake);

        await store.RecordAsync(PlaySubmission());

        Assert.Empty(fake.Writes);
    }

    [Fact]
    public async Task Record_Play_WritesRoundTrippableIndentedDocument()
    {
        var fake = new FakeFolderAccess();
        var store = MakeStore(fake);
        await store.BeginQuizAsync();

        await store.RecordAsync(PlaySubmission());

        var payload = Assert.Single(fake.Writes);
        Assert.Contains('\n', payload); // WriteIndented pin — the one options-controlled aspect
        var doc = JsonSerializer.Deserialize<ProblemStatsDocument>(payload);
        Assert.NotNull(doc);
        var record = Assert.Single(doc.Problems).Value;
        Assert.Equal(1, record.Tally.Submitted);
        Assert.Equal(1, record.Tally.Correct);
        Assert.Equal(FixedNow, record.LastQuizzed); // clock came from the TimeProvider seam
    }

    [Fact]
    public async Task Record_Cube_FoldsAsTwoDecisions()
    {
        // Producer contract: a cube position is TWO lifetime decisions — one per
        // half, matching QuizScore's two-half fold. Both halves right here, so
        // the written tally shows two submissions and two correct.
        var fake = new FakeFolderAccess();
        var store = MakeStore(fake);
        await store.BeginQuizAsync();

        await store.RecordAsync(CubeSubmission());

        var doc = JsonSerializer.Deserialize<ProblemStatsDocument>(fake.Writes.Single());
        Assert.NotNull(doc);
        var record = Assert.Single(doc.Problems).Value;
        Assert.Equal(2, record.Tally.Submitted);
        Assert.Equal(2, record.Tally.Correct);
    }

    [Fact]
    public async Task Record_EachFold_WritesOnce()
    {
        // Write-back timing is per-fold (user-settled): two folds, two writes,
        // the second superseding the first — crash-safety over batching.
        var fake = new FakeFolderAccess();
        var store = MakeStore(fake);
        await store.BeginQuizAsync();

        await store.RecordAsync(PlaySubmission(1));
        await store.RecordAsync(PlaySubmission(2));

        Assert.Equal(2, fake.Writes.Count);
        var last = JsonSerializer.Deserialize<ProblemStatsDocument>(fake.Writes[^1]);
        Assert.NotNull(last);
        Assert.Equal(2, last.Count);
    }

    [Fact]
    public async Task Record_WriteThrows_WriteFailedOnceThenStopsWritingWithoutThrowing()
    {
        var fake = new FakeFolderAccess { WriteException = new JSException("disk gone") };
        var store = MakeStore(fake);
        await store.BeginQuizAsync();
        var statusChanges = 0;
        store.StatusChanged += () => statusChanges++;

        await store.RecordAsync(PlaySubmission(1)); // fold ok, write fails
        await store.RecordAsync(PlaySubmission(2)); // degraded: no further attempt

        Assert.Equal(QuizStatsStatus.WriteFailed, store.Status);
        Assert.Equal(1, statusChanges); // Ready → WriteFailed exactly once, no per-answer spam
        Assert.Empty(fake.Writes);
    }

    // -----------------------------------------------------------------------
    //  Re-bind + isolation (the two-slot ruling)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BeginQuiz_AfterLoadFailed_RebindsFreshContext()
    {
        // Failure states scope to the active context: fixing (or replacing) the
        // file and starting a new quiz re-binds cleanly.
        var fake = new FakeFolderAccess { StatsJson = "corrupt" };
        var store = MakeStore(fake);
        await store.BeginQuizAsync();
        Assert.Equal(QuizStatsStatus.LoadFailed, store.Status);

        fake.StatsJson = null; // file replaced/removed before the next Start
        await store.BeginQuizAsync();

        Assert.Equal(QuizStatsStatus.Ready, store.Status);
    }

    [Fact]
    public async Task BeginQuiz_AfterWriteFailed_RebindsAndWritesAgain()
    {
        var fake = new FakeFolderAccess { WriteException = new JSException("boom") };
        var store = MakeStore(fake);
        await store.BeginQuizAsync();
        await store.RecordAsync(PlaySubmission());
        Assert.Equal(QuizStatsStatus.WriteFailed, store.Status);

        fake.WriteException = null;
        await store.BeginQuizAsync();
        await store.RecordAsync(PlaySubmission());

        Assert.Equal(QuizStatsStatus.Ready, store.Status);
        Assert.Single(fake.Writes);
    }

    [Fact]
    public async Task Record_HolderClearedAfterBind_StillReadyAndStillWrites()
    {
        // The mid-quiz-Clear ruling: the stats context bound at Start persists
        // regardless of what happens to the picked slot afterward. Clearing the
        // holder (Home's Clear) must not stop the running quiz's recording.
        var fake = new FakeFolderAccess();
        var folder = EnabledFolder();
        var store = MakeStore(fake, folder);
        await store.BeginQuizAsync();

        folder.Clear();
        await store.RecordAsync(PlaySubmission());

        Assert.Equal(QuizStatsStatus.Ready, store.Status);
        Assert.Single(fake.Writes);
    }

    [Fact]
    public async Task BeginQuiz_ReplacesPreviousDocument()
    {
        // A re-bind starts from the file's (or Empty's) state, not the previous
        // quiz's in-memory document: after re-bind over a fresh corpus, the
        // first write contains only the new fold.
        var fake = new FakeFolderAccess();
        var store = MakeStore(fake);
        await store.BeginQuizAsync();
        await store.RecordAsync(PlaySubmission(1));

        // Empty the folder's file before the re-bind: the point is what the
        // second context loads *from the file*, so the first run's write must
        // not be what it finds — that would pin the round-trip instead.
        fake.StatsJson = null;
        await store.BeginQuizAsync();
        await store.RecordAsync(PlaySubmission(2));

        var last = JsonSerializer.Deserialize<ProblemStatsDocument>(fake.Writes[^1]);
        Assert.NotNull(last);
        var record = Assert.Single(last.Problems);
        Assert.Equal(PlayKey(2), record.Key);
    }

    // -----------------------------------------------------------------------
    //  CanWeightMix — the shared "can a mix mean anything here" predicate (#87)
    // -----------------------------------------------------------------------

    /// <summary>
    /// A stats document holding one decision, in the real wire format the app
    /// writes — never a hand-built blob, so a format change reaches this fixture.
    /// </summary>
    private static string StatsDocumentJson() =>
        JsonSerializer.Serialize(
            ProblemStatsDocument.Empty.Plus(PlaySubmission(), new FixedTimeProvider()),
            QuizStatsFile.SerializerOptions);

    [Fact]
    public void CanWeightMix_BeforeAnyProbe_IsFalse()
    {
        // The probe's generation stamp starts at -1 precisely so an un-probed
        // store cannot accidentally agree with the generation a freshly-Set
        // holder sits on. Without that, "never asked" would read as "asked and
        // found nothing" — the same answer today, and the wrong one the moment
        // the stamp's initial value or the holder's counter changes.
        var store = MakeStore(new FakeFolderAccess { PickedStatsJson = StatsDocumentJson() });

        Assert.False(store.CanWeightMix);
    }

    [Fact]
    public async Task CanWeightMix_EnabledPickWithStatsContent_IsTrue()
    {
        var fake = new FakeFolderAccess { PickedStatsJson = StatsDocumentJson() };
        var store = MakeStore(fake);

        await store.RefreshPickedStatsAsync();

        Assert.True(store.CanWeightMix);
    }

    [Theory]
    [InlineData(null)]                                            // no file at all
    [InlineData("""{"schemaVersion":1,"decisions":[]}""")]         // present, but empty
    [InlineData("not json at all")]                                // unreadable
    [InlineData("""{"schemaVersion":99,"decisions":[]}""")]        // foreign / newer schema
    public async Task CanWeightMix_MissingEmptyOrUnreadable_AllReadFalse(string? pickedStatsJson)
    {
        // #87's ruling in one place: an empty stats document is treated exactly
        // as no stats document — and so is one that cannot be read. Three
        // situations, one answer, no rungs to tell apart.
        var fake = new FakeFolderAccess { PickedStatsJson = pickedStatsJson };
        var store = MakeStore(fake);

        await store.RefreshPickedStatsAsync();

        Assert.False(store.CanWeightMix);
    }

    [Theory]
    [InlineData(FolderWriteCapability.BrowserUnsupported)]
    [InlineData(FolderWriteCapability.PermissionDenied)]
    public async Task CanWeightMix_WithoutWriteCapability_IsFalse_AndSkipsTheRead(
        FolderWriteCapability capability)
    {
        // The other half of the predicate. The read is skipped rather than
        // performed-and-ignored: on these rungs there is no handle to read a
        // document through, and the answer is settled before any interop.
        var fake = new FakeFolderAccess { PickedStatsJson = StatsDocumentJson() };
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", [new PickedFile("a.xg", [1])], capability, []);
        var store = MakeStore(fake, folder);

        await store.RefreshPickedStatsAsync();

        Assert.False(store.CanWeightMix);
    }

    [Fact]
    public async Task CanWeightMix_ExpiresWhenTheFolderChanges_WithNoResetCall()
    {
        // The derived-stamp guarantee: nothing clears the probe, and nothing
        // has to. A new pick bumps the holder's generation and the probe stops
        // matching, so a verdict about the *previous* folder can never be read
        // as one about this one.
        var folder = EnabledFolder();
        var store = MakeStore(new FakeFolderAccess { PickedStatsJson = StatsDocumentJson() }, folder);
        await store.RefreshPickedStatsAsync();
        Assert.True(store.CanWeightMix);

        folder.Set("Other", [new PickedFile("b.xg", [1])], FolderWriteCapability.Enabled, []);

        Assert.False(store.CanWeightMix);
    }

    [Fact]
    public async Task RefreshPickedStats_ReadsThePickedSlot_AndNeverPromotes()
    {
        // The probe is a setup-time read on the folder being configured. It
        // must not promote — promotion belongs to the Start-time bind, and a
        // probe running earlier must not move it — and must not touch the
        // active context a running quiz records through.
        var fake = new FakeFolderAccess { PickedStatsJson = StatsDocumentJson() };
        var store = MakeStore(fake);

        await store.RefreshPickedStatsAsync();

        Assert.Equal(0, fake.PromoteCallCount);
        Assert.Empty(fake.ActiveFileNames);   // the active slot was never read or written
        Assert.Empty(fake.Writes);
        Assert.Equal(QuizStatsStatus.Disabled, store.Status); // no status side effect
    }

    [Fact]
    public async Task RefreshPickedStats_DoesNotDisturbABoundQuizContext()
    {
        // The two states share a class and nothing else: probing mid-quiz must
        // leave the live document and status exactly where the bind left them.
        var fake = new FakeFolderAccess();
        var store = MakeStore(fake);
        await store.BeginQuizAsync();
        await store.RecordAsync(PlaySubmission());
        var boundDoc = store.CurrentDocument;

        fake.PickedStatsJson = StatsDocumentJson();
        await store.RefreshPickedStatsAsync();

        Assert.Same(boundDoc, store.CurrentDocument);
        Assert.Equal(QuizStatsStatus.Ready, store.Status);
    }

    [Fact]
    public async Task BeginQuiz_FreshFolderWithNoStats_StillBindsAndRecords()
    {
        // The redefinition's load-bearing non-regression: the bind gates on
        // capability, never on CanWeightMix. A brand-new folder has nothing to
        // weight by and must still seed, record, and write — that first quiz is
        // what creates the record a mix later composes from.
        var fake = new FakeFolderAccess(); // no stats file anywhere
        var store = MakeStore(fake);

        await store.BeginQuizAsync();
        Assert.False(store.CanWeightMix);          // no mix offered…
        Assert.Equal(QuizStatsStatus.Ready, store.Status); // …but stats are live

        await store.RecordAsync(PlaySubmission());

        Assert.Single(fake.Writes);
    }
}
