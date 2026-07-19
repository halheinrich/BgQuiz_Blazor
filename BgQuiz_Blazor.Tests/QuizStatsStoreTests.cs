using System.Text.Json;
using BgDataTypes_Lib;
using BgGame_Lib;
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
        folder.Set("Corpus", [new PickedFile("a.xgp", [1, 2, 3])], StatsSaveCapability.Enabled);
        return folder;
    }

    private static QuizStatsStore MakeStore(
        FakeFolderAccess fake, PickedProblemFolder? folder = null) =>
        new(fake, new FixedTimeProvider(), folder ?? EnabledFolder());

    private static SubmittedPlay PlaySubmission(string file = "stats.xgp", bool correct = true) =>
        new(new XgpDecisionId(file), TestFixtures.MakePlay((8, 5)), 0,
            correct ? 0.0 : 0.05, correct);

    private static SubmittedCubeAction CubeSubmission(string file = "cube.xgp") =>
        new(new XgpDecisionId(file), new CubeDecisionPair(CubeAction.Double, CubeAction.Take),
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
        folder.Set("Corpus", [new PickedFile("a.xg", [1])], StatsSaveCapability.BrowserUnsupported);
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
        folder.Set("Corpus", [new PickedFile("a.xg", [1])], StatsSaveCapability.PermissionDenied);
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
        var existing = DecisionStatsDocument.Empty.Plus(PlaySubmission(), clock);
        var fake = new FakeFolderAccess
        {
            StatsJson = JsonSerializer.Serialize(existing, QuizStatsFile.SerializerOptions),
        };
        var store = MakeStore(fake);
        await store.BeginQuizAsync();
        Assert.Equal(QuizStatsStatus.Ready, store.Status);

        await store.RecordAsync(PlaySubmission());

        var written = JsonSerializer.Deserialize<DecisionStatsDocument>(fake.Writes.Single());
        Assert.NotNull(written);
        var record = Assert.Single(written.Decisions).Value;
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
        var doc = JsonSerializer.Deserialize<DecisionStatsDocument>(payload);
        Assert.NotNull(doc);
        var record = Assert.Single(doc.Decisions).Value;
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

        var doc = JsonSerializer.Deserialize<DecisionStatsDocument>(fake.Writes.Single());
        Assert.NotNull(doc);
        var record = Assert.Single(doc.Decisions).Value;
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

        await store.RecordAsync(PlaySubmission("a.xgp"));
        await store.RecordAsync(PlaySubmission("b.xgp"));

        Assert.Equal(2, fake.Writes.Count);
        var last = JsonSerializer.Deserialize<DecisionStatsDocument>(fake.Writes[^1]);
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

        await store.RecordAsync(PlaySubmission("a.xgp")); // fold ok, write fails
        await store.RecordAsync(PlaySubmission("b.xgp")); // degraded: no further attempt

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
        await store.RecordAsync(PlaySubmission("old.xgp"));

        await store.BeginQuizAsync(); // fake still reads null — nothing persisted it
        await store.RecordAsync(PlaySubmission("new.xgp"));

        var last = JsonSerializer.Deserialize<DecisionStatsDocument>(fake.Writes[^1]);
        Assert.NotNull(last);
        var record = Assert.Single(last.Decisions);
        Assert.Equal(new XgpDecisionId("new.xgp"), record.Key);
    }
}
