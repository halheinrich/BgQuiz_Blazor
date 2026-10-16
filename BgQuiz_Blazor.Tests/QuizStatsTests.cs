using System.Text.Json;
using BgDataTypes_Lib;
using BgFolderAccess_Razor;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="QuizStats"/> and <see cref="StatsProbe"/> over a fake folder: the Start-time
/// bind (load, seed, set aside, fold), the per-answer fold and write-back with the pre-write
/// guard, the degrade guarantees — an unreadable file is never written, a failed write stops
/// writing without faulting — and the pick-time probe's facts and forecasts.
/// </summary>
public class QuizStatsTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Deterministic clock — the recording must never read ambient time.</summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedNow;
    }

    private static readonly FixedTimeProvider Clock = new();

    private static FakeFolderContext Folder(FolderWriteCapability capability = FolderWriteCapability.Enabled) =>
        new("Corpus", capability);

    private static Task<QuizStats> BindAsync(FakeFolderContext folder) => QuizStats.BindAsync(folder, Clock);

    /// <summary>The content key of the <paramref name="problem"/>-th distinct fixture problem; distinct values are distinct lifetime records.</summary>
    private static ProblemKey PlayKey(int problem = 0) => ProblemKey.From(PlayProblem(problem));

    private static CheckerPlayDecision PlayProblem(int problem) =>
        TestFixtures.TwoChoiceDecision(
            TestFixtures.OpeningBest(), TestFixtures.OpeningAlternative(), away: problem);

    private static SubmittedPlay PlaySubmission(int problem = 0, bool correct = true) =>
        TestFixtures.Scored(
            PlayProblem(problem),
            correct ? TestFixtures.OpeningBest() : TestFixtures.OpeningAlternative(),
            PlayRanking.Equity);

    private static SubmittedCubeAnswer CubeSubmission(int problem = 0) =>
        SubmittedCubeAnswer.Score(CubeAnswer.DoubleTake, TestFixtures.CubeDecision(away: problem));

    private static AnswerOfRecord Play(int problem = 0, bool correct = true) => AnswerOfRecord.Of(PlaySubmission(problem, correct));

    private static AnswerOfRecord Cube(int problem = 0) => AnswerOfRecord.Of(CubeSubmission(problem));

    /// <summary>A stats document holding one decision, in the wire format the app writes.</summary>
    private static string StatsDocumentJson() =>
        JsonSerializer.Serialize(
            ProblemStatsDocument.Empty.Plus(PlaySubmission(), Clock),
            QuizStatsFile.DocumentTypeInfo);

    // -----------------------------------------------------------------------
    //  BindAsync — the Start-time bind
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(FolderWriteCapability.BrowserUnsupported)]
    [InlineData(FolderWriteCapability.PermissionDenied)]
    public async Task Bind_WithoutWriteCapability_IsDisabled_AndNeverReads(FolderWriteCapability capability)
    {
        // Capability is the pick-time verdict: a folder that cannot be written is not even read.
        var folder = Folder(capability);

        var stats = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.Disabled, stats.Report.Status);
        Assert.Null(stats.Document);
        Assert.Empty(folder.FileNames);
    }

    [Fact]
    public async Task Bind_NoStatsFile_ReadySeededEmpty()
    {
        // A fresh folder: Ready over an empty document; the first fold writes the file into being.
        var folder = Folder();

        var stats = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.Ready, stats.Report.Status);
        Assert.Equal(0, stats.Document!.Count);
        Assert.Empty(folder.Writes);
    }

    [Fact]
    public async Task Bind_ValidStatsFile_ReadyAndFoldsContinueExistingTallies()
    {
        var folder = Folder();
        folder.StatsJson = JsonSerializer.Serialize(
            ProblemStatsDocument.Empty.Plus(PlaySubmission(), Clock), QuizStatsFile.DocumentTypeInfo);
        var stats = await BindAsync(folder);
        Assert.Equal(QuizStatsStatus.Ready, stats.Report.Status);

        await stats.RecordAsync(Play());

        var written = JsonSerializer.Deserialize<ProblemStatsDocument>(folder.Writes.Single());
        Assert.NotNull(written);
        var record = Assert.Single(written.Problems).Value;
        Assert.Equal(2, record.Tally.Submitted);
        Assert.Equal(2, record.Tally.Correct);
    }

    [Fact]
    public async Task Bind_CorruptStatsFile_LoadFailedRecordsNothingWritesNothing()
    {
        // The file-untouched guarantee: an unparseable file is LoadFailed, and no fold ever writes over it.
        var folder = Folder();
        folder.StatsJson = "not json at all";

        var stats = await BindAsync(folder);
        await stats.RecordAsync(Play());
        await stats.RecordAsync(Cube());

        Assert.Equal(QuizStatsStatus.LoadFailed, stats.Report.Status);
        Assert.Null(stats.Document);
        Assert.Empty(folder.Writes);
        Assert.Equal("not json at all", folder.StatsJson);
    }

    [Fact]
    public async Task Bind_ForeignSchemaJson_LoadFailed()
    {
        var folder = Folder();
        folder.StatsJson = """{"someOtherApp":true}""";

        var stats = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.LoadFailed, stats.Report.Status);
    }

    [Fact]
    public async Task Bind_ReadThrowsJs_LoadFailed()
    {
        var folder = Folder();
        folder.ReadException = new JSException("read failed");

        var stats = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.LoadFailed, stats.Report.Status);
        Assert.Empty(folder.Writes);
    }

    // -----------------------------------------------------------------------
    //  Retirement — the clean break with deliberate recognition (SPEC-stats-identity.md §3)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Bind_RetiredV1File_SetsItAsideSeedsFreshAndRecords()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V1Json;

        var stats = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.Ready, stats.Report.Status);
        Assert.Equal(QuizStatsFile.RetiredNameFor(1), stats.Report.SetAsideFileName);
        Assert.Equal(RetiredStatsFixture.V1Json, folder.RetiredStatsJson(1)); // bytes, unparsed
        Assert.Equal(0, JsonSerializer.Deserialize<ProblemStatsDocument>(folder.StatsJson!)!.Count);

        await stats.RecordAsync(Play());

        var written = JsonSerializer.Deserialize<ProblemStatsDocument>(folder.Writes[^1]);
        Assert.NotNull(written);
        Assert.Equal(PlayKey(), Assert.Single(written.Problems).Key);
    }

    [Fact]
    public async Task Bind_RetiredV1File_SetsAsideBeforeReplacing()
    {
        // The write order is the data-safety guarantee: the copy aside lands before the
        // standard name is overwritten.
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V1Json;

        await BindAsync(folder);

        var writeNames = folder.FileNames.Skip(1).ToList();   // after the bind's read
        Assert.Equal([QuizStatsFile.RetiredNameFor(1), QuizStatsFile.FileName], writeNames);
    }

    [Fact]
    public async Task Bind_RetiredV1File_SetAsideFails_LoadFailedAndFileUntouched()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V1Json;
        folder.WriteException = new JSException("write refused");

        var stats = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.LoadFailed, stats.Report.Status);
        Assert.Null(stats.Report.SetAsideFileName);
        Assert.Empty(folder.Writes);
        Assert.Equal(RetiredStatsFixture.V1Json, folder.StatsJson);
    }

    [Fact]
    public async Task Bind_AfterARetirement_TheNextBindReportsNothingAndRetiresNothing()
    {
        // The retirement is a one-off: the second run binds against the fresh document it left.
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V1Json;
        var first = await BindAsync(folder);
        Assert.NotNull(first.Report.SetAsideFileName);
        folder.Writes.Clear();

        var second = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.Ready, second.Report.Status);
        Assert.Null(second.Report.SetAsideFileName);
        Assert.Empty(folder.Writes);
    }

    [Fact]
    public async Task Bind_RetiredV1File_FailedReplaceIsRetriedOnTheNextBind()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V1Json;
        folder.WriteException = new JSException("write refused");
        var failed = await BindAsync(folder);
        Assert.Equal(QuizStatsStatus.LoadFailed, failed.Report.Status);

        folder.WriteException = null;
        var retried = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.Ready, retried.Report.Status);
        Assert.Equal(QuizStatsFile.RetiredNameFor(1), retried.Report.SetAsideFileName);
        Assert.Equal(RetiredStatsFixture.V1Json, folder.RetiredStatsJson(1));
    }

    [Fact]
    public async Task Bind_RetiredV2File_SetsItAsideUnderTheV2Name()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V2Json;

        var stats = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.Ready, stats.Report.Status);
        Assert.Equal(RetiredStatsFixture.V2Json, folder.RetiredStatsJson(2));
        Assert.Null(folder.RetiredStatsJson(1));
        Assert.Equal(0, JsonSerializer.Deserialize<ProblemStatsDocument>(folder.StatsJson!)!.Count);
    }

    [Theory]
    [InlineData(RetiredStatsFixture.V1Json, 1)]
    [InlineData(RetiredStatsFixture.V2Json, 2)]
    public async Task Bind_RetiredFile_ReportsTheNameItActuallyWrote(string json, int version)
    {
        var folder = Folder();
        folder.StatsJson = json;

        var stats = await BindAsync(folder);

        Assert.Equal(QuizStatsFile.RetiredNameFor(version), stats.Report.SetAsideFileName);
        Assert.Contains(stats.Report.SetAsideFileName, folder.FileNames);
    }

    [Fact]
    public async Task Bind_V3File_IsTheCurrentFormat_ReadsUntouchedAndForecastsNothing()
    {
        // v3 is the current format again (SPEC-stats-identity.md §3, amended 2026-09-02): it
        // reads as current, nothing is set aside or written, and the probe counts its records.
        Assert.Equal(3, ProblemStatsDocument.CurrentSchemaVersion);
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V3Json;

        var probe = await StatsProbe.OfAsync(folder);
        Assert.True(probe.HasStats);
        Assert.Null(probe.SetAsideForecast);

        var stats = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.Ready, stats.Report.Status);
        Assert.Null(stats.Report.SetAsideFileName);
        Assert.Empty(folder.Writes);
        Assert.Equal(RetiredStatsFixture.V3Json, folder.StatsJson);
        Assert.Equal(2, stats.Document!.Count);
    }

    [Fact]
    public async Task Bind_SecondRetirement_LeavesTheFirstSetAsideAlone()
    {
        // A tester who skipped a release: a v1 already set aside, and a v2 under the standard
        // name. One fixed name would land the second copy on the first.
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V2Json;
        folder.SetRetiredStatsJson(1, RetiredStatsFixture.V1Json);

        await BindAsync(folder);

        Assert.Equal(RetiredStatsFixture.V1Json, folder.RetiredStatsJson(1));
        Assert.Equal(RetiredStatsFixture.V2Json, folder.RetiredStatsJson(2));
    }

    [Fact]
    public async Task Bind_ClaimsV1ButMalformed_LoadFailedNotRetired()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.ClaimsV1ButMalformedJson;

        var stats = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.LoadFailed, stats.Report.Status);
        Assert.Null(stats.Report.SetAsideFileName);
        Assert.Empty(folder.Writes);
    }

    [Fact]
    public async Task Bind_NewerSchemaFile_LoadFailedNotRetired()
    {
        // A document from a later BgQuiz is not retired: it is a file whose owner is a version still to come.
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.NewerSchemaJson;

        var stats = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.LoadFailed, stats.Report.Status);
        Assert.Null(stats.Report.SetAsideFileName);
        Assert.Empty(folder.Writes);
        Assert.Equal(RetiredStatsFixture.NewerSchemaJson, folder.StatsJson);
    }

    // -----------------------------------------------------------------------
    //  The fold — the interim v4 folds back into v3 (SPEC-stats-identity.md §3, amended 2026-09-02)
    // -----------------------------------------------------------------------

    /// <summary>
    /// The merged document as the fold must produce it from <see cref="RetiredStatsFixture.V4Json"/>
    /// over <see cref="RetiredStatsFixture.V3Json"/>: the shared play key summed with the later
    /// date kept, and each one-sided record passing through. Asserted on the written JSON.
    /// </summary>
    private static void AssertMergedDocument(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(ProblemStatsDocument.CurrentSchemaVersion, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        var problems = doc.RootElement.GetProperty("problems");
        Assert.Equal(3, problems.EnumerateObject().Count());

        var shared = problems.GetProperty(RetiredStatsFixture.SharedPlayKeyText);
        Assert.Equal(3, shared.GetProperty("tally").GetProperty("submitted").GetInt32());
        Assert.Equal(2, shared.GetProperty("tally").GetProperty("correct").GetInt32());
        Assert.Equal(0.05, shared.GetProperty("tally").GetProperty("totalEquityLoss").GetDouble(), 9);
        Assert.Equal("2026-09-01T18:30:00+00:00", shared.GetProperty("lastQuizzed").GetString());

        var v3Only = problems.GetProperty(RetiredStatsFixture.V3OnlyCubeKeyText);
        Assert.Equal(2, v3Only.GetProperty("tally").GetProperty("submitted").GetInt32());
        Assert.Equal(0.08, v3Only.GetProperty("tally").GetProperty("totalEquityLoss").GetDouble(), 9);

        var v4Only = problems.GetProperty(RetiredStatsFixture.V4OnlyCubeKeyText);
        Assert.Equal(4, v4Only.GetProperty("tally").GetProperty("submitted").GetInt32());
        Assert.Equal(2, v4Only.GetProperty("tally").GetProperty("correct").GetInt32());
        Assert.Contains("tally", v4Only.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Bind_V4FileWithV3Sibling_FoldsIntoTheSibling_RenamesTheV4Aside_AndReportsNoRestart()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V4Json;
        folder.SetRetiredStatsJson(3, RetiredStatsFixture.V3Json);

        var stats = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.Ready, stats.Report.Status);
        Assert.Null(stats.Report.SetAsideFileName);
        Assert.Equal(RetiredStatsFixture.V4Json, folder.MergedStatsJson(4));      // bytes, unparsed
        Assert.Equal("bgquiz-stats.v4.merged.json", QuizStatsFile.MergedNameFor(4));
        Assert.Null(folder.RetiredStatsJson(4));                                  // never a retired name
        Assert.Equal(RetiredStatsFixture.V3Json, folder.RetiredStatsJson(3));     // the base is left as it was
        AssertMergedDocument(folder.StatsJson!);
        Assert.Equal(3, stats.Document!.Count);
    }

    [Fact]
    public async Task Bind_V4FileWithV3Sibling_CopiesAsideBeforeReplacing()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V4Json;
        folder.SetRetiredStatsJson(3, RetiredStatsFixture.V3Json);

        await BindAsync(folder);

        int mergedWrite = folder.FileNames.LastIndexOf(QuizStatsFile.MergedNameFor(4));
        int replace = folder.FileNames.LastIndexOf(QuizStatsFile.FileName);
        Assert.True(mergedWrite >= 0 && replace > mergedWrite,
            $"expected the merged copy before the replace; log: {string.Join(", ", folder.FileNames)}");
        Assert.Equal(2, folder.Writes.Count);
    }

    [Fact]
    public async Task Bind_V4FileAlone_ConvertsToCurrent_AndRenamesTheV4Aside()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V4Json;

        var stats = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.Ready, stats.Report.Status);
        Assert.Null(stats.Report.SetAsideFileName);
        Assert.Equal(RetiredStatsFixture.V4Json, folder.MergedStatsJson(4));
        Assert.Null(folder.RetiredStatsJson(3));
        using var doc = JsonDocument.Parse(folder.StatsJson!);
        Assert.Equal(ProblemStatsDocument.CurrentSchemaVersion, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        var problems = doc.RootElement.GetProperty("problems");
        Assert.Equal(2, problems.EnumerateObject().Count());
        Assert.Equal(2, problems.GetProperty(RetiredStatsFixture.SharedPlayKeyText).GetProperty("tally").GetProperty("submitted").GetInt32());
        Assert.Equal(4, problems.GetProperty(RetiredStatsFixture.V4OnlyCubeKeyText).GetProperty("tally").GetProperty("submitted").GetInt32());
        Assert.DoesNotContain("cubePair", folder.StatsJson);
        Assert.DoesNotContain("checkerPlay", folder.StatsJson);
    }

    [Fact]
    public async Task Bind_EmptyV4FileWithV3Sibling_TheSiblingIsTheWholeResult()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V4EmptyJson;
        folder.SetRetiredStatsJson(3, RetiredStatsFixture.V3Json);

        var stats = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.Ready, stats.Report.Status);
        Assert.Equal(RetiredStatsFixture.V4EmptyJson, folder.MergedStatsJson(4));
        Assert.Equal(2, stats.Document!.Count);
        using var doc = JsonDocument.Parse(folder.StatsJson!);
        Assert.Equal(2, doc.RootElement.GetProperty("problems").EnumerateObject().Count());
    }

    [Fact]
    public async Task Bind_AfterTheFold_TheNextBindReadsACurrentFile_AndFoldsNothing()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V4Json;
        folder.SetRetiredStatsJson(3, RetiredStatsFixture.V3Json);
        await BindAsync(folder);
        int writesAfterFold = folder.Writes.Count;

        var second = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.Ready, second.Report.Status);
        Assert.Equal(writesAfterFold, folder.Writes.Count);
        Assert.Equal(3, second.Document!.Count);
        AssertMergedDocument(folder.StatsJson!);
    }

    [Fact]
    public async Task Bind_AfterTheFold_RecordsIntoTheMergedDocument()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V4Json;
        folder.SetRetiredStatsJson(3, RetiredStatsFixture.V3Json);
        var stats = await BindAsync(folder);

        await stats.RecordAsync(Cube());

        using var doc = JsonDocument.Parse(folder.Writes[^1]);
        Assert.Equal(4, doc.RootElement.GetProperty("problems").EnumerateObject().Count());
        Assert.Equal(3, doc.RootElement.GetProperty("problems").GetProperty(RetiredStatsFixture.SharedPlayKeyText)
            .GetProperty("tally").GetProperty("submitted").GetInt32());
    }

    [Fact]
    public async Task Bind_ClaimsV4ButMalformed_LoadFailedAndNothingWritten()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.ClaimsV4ButMalformedJson;
        folder.SetRetiredStatsJson(3, RetiredStatsFixture.V3Json);

        var stats = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.LoadFailed, stats.Report.Status);
        Assert.Null(stats.Report.SetAsideFileName);
        Assert.Empty(folder.Writes);
        Assert.Null(folder.MergedStatsJson(4));
        Assert.Equal(RetiredStatsFixture.ClaimsV4ButMalformedJson, folder.StatsJson);
    }

    [Fact]
    public async Task Bind_V4FileWithUnreadableV3Sibling_LoadFailedAndNothingWritten()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V4Json;
        folder.SetRetiredStatsJson(3, "not json at all");

        var stats = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.LoadFailed, stats.Report.Status);
        Assert.Empty(folder.Writes);
        Assert.Equal(RetiredStatsFixture.V4Json, folder.StatsJson);
        Assert.Null(folder.MergedStatsJson(4));
    }

    [Fact]
    public async Task Bind_V4Fold_CopyAsideFails_LoadFailedAndFileUntouched()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V4Json;
        folder.SetRetiredStatsJson(3, RetiredStatsFixture.V3Json);
        folder.WriteException = new JSException("disk full");

        var stats = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.LoadFailed, stats.Report.Status);
        Assert.Empty(folder.Writes);
        Assert.Equal(RetiredStatsFixture.V4Json, folder.StatsJson);
        Assert.Null(stats.Document);
    }

    [Fact]
    public async Task Bind_V4Fold_FailedReplaceIsRetriedOnTheNextBind()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V4Json;
        folder.SetRetiredStatsJson(3, RetiredStatsFixture.V3Json);
        folder.OnWrite = name =>
        {
            if (name == QuizStatsFile.FileName) throw new JSException("transient");
        };
        var failed = await BindAsync(folder);
        Assert.Equal(QuizStatsStatus.LoadFailed, failed.Report.Status);
        Assert.Equal(RetiredStatsFixture.V4Json, folder.StatsJson);        // still the v4
        Assert.Equal(RetiredStatsFixture.V4Json, folder.MergedStatsJson(4)); // the copy landed

        folder.OnWrite = null;
        var retried = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.Ready, retried.Report.Status);
        AssertMergedDocument(folder.StatsJson!);
        Assert.Equal(RetiredStatsFixture.V4Json, folder.MergedStatsJson(4));
    }

    // -----------------------------------------------------------------------
    //  RecordAsync — fold + write-back per answer of record
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Record_Play_WritesRoundTrippableIndentedDocument()
    {
        var folder = Folder();
        var stats = await BindAsync(folder);

        await stats.RecordAsync(Play());

        var payload = Assert.Single(folder.Writes);
        Assert.Contains('\n', payload); // WriteIndented, the one options-controlled aspect
        var doc = JsonSerializer.Deserialize<ProblemStatsDocument>(payload);
        Assert.NotNull(doc);
        var record = Assert.Single(doc.Problems).Value;
        Assert.Equal(1, record.Tally.Submitted);
        Assert.Equal(1, record.Tally.Correct);
        Assert.Equal(FixedNow, record.LastQuizzed); // the clock came from the TimeProvider seam
    }

    [Fact]
    public async Task Record_Cube_FoldsAsTwoDecisions()
    {
        // A cube answer is two lifetime decisions, its doubling half and its take half
        // (SPEC-scoring §3, 2026-10-01); both right here.
        var folder = Folder();
        var stats = await BindAsync(folder);

        await stats.RecordAsync(Cube());

        var doc = JsonSerializer.Deserialize<ProblemStatsDocument>(folder.Writes.Single());
        Assert.NotNull(doc);
        var record = Assert.Single(doc.Problems).Value;
        Assert.Equal(2, record.Tally.Submitted);
        Assert.Equal(2, record.Tally.Correct);
    }

    [Fact]
    public async Task Record_NoDouble_FoldsTwoHalves_ItsTakeHalfACorrectZero()
    {
        // No double's take half is a correct zero (Hal, 2026-10-01); at the Double / Take
        // position its doubling half is wrong, so two submitted, one correct.
        var folder = Folder();
        var stats = await BindAsync(folder);

        await stats.RecordAsync(AnswerOfRecord.Of(SubmittedCubeAnswer.Score(CubeAnswer.NoDouble, TestFixtures.CubeDecision())));

        var doc = JsonSerializer.Deserialize<ProblemStatsDocument>(folder.Writes.Single());
        Assert.NotNull(doc);
        var record = Assert.Single(doc.Problems).Value;
        Assert.Equal(2, record.Tally.Submitted);
        Assert.Equal(1, record.Tally.Correct);
    }

    [Fact]
    public async Task Record_EachFold_WritesOnce()
    {
        var folder = Folder();
        var stats = await BindAsync(folder);

        await stats.RecordAsync(Play(1));
        await stats.RecordAsync(Play(2));

        Assert.Equal(2, folder.Writes.Count);
        var last = JsonSerializer.Deserialize<ProblemStatsDocument>(folder.Writes[^1]);
        Assert.NotNull(last);
        Assert.Equal(2, last.Count);
    }

    [Fact]
    public async Task Record_FoldsOntoTheFileAsItStandsNow_NotTheBindSnapshot()
    {
        // The pre-write guard (SPEC-stats-identity.md §5): another recorder's record, written
        // after this one bound, survives this fold.
        var folder = Folder();
        var stats = await BindAsync(folder);

        folder.StatsJson = JsonSerializer.Serialize(
            ProblemStatsDocument.Empty.Plus(PlaySubmission(1), Clock), QuizStatsFile.DocumentTypeInfo);

        await stats.RecordAsync(Play(2));

        var written = JsonSerializer.Deserialize<ProblemStatsDocument>(folder.Writes.Single());
        Assert.NotNull(written);
        Assert.Equal(2, written.Count);
        Assert.Contains(PlayKey(1), written.Problems.Keys);
        Assert.Contains(PlayKey(2), written.Problems.Keys);
    }

    [Theory]
    [InlineData("unreadable")]
    [InlineData("corrupt")]
    [InlineData("missing")]
    public async Task Record_ReadTroubleAtFoldTime_FoldsInMemoryAndKeepsRecording(string trouble)
    {
        // Every read trouble degrades to the in-memory fold, and none changes the condition.
        var folder = Folder();
        var stats = await BindAsync(folder);
        await stats.RecordAsync(Play(1));

        switch (trouble)
        {
            case "unreadable": folder.ReadException = new JSException("read failed"); break;
            case "corrupt": folder.StatsJson = "not json at all"; break;
            case "missing": folder.StatsJson = null; break;
        }

        await stats.RecordAsync(Play(2));

        Assert.Equal(QuizStatsStatus.Ready, stats.Report.Status);
        var written = JsonSerializer.Deserialize<ProblemStatsDocument>(folder.Writes[^1]);
        Assert.NotNull(written);
        Assert.Equal(2, written.Count);
        Assert.Contains(PlayKey(1), written.Problems.Keys);
    }

    [Fact]
    public async Task Record_WriteThrows_WriteFailedOnceThenStopsWritingWithoutThrowing()
    {
        var folder = Folder();
        folder.WriteException = new JSException("disk gone");
        var stats = await BindAsync(folder);

        await stats.RecordAsync(Play(1)); // fold ok, write fails
        await stats.RecordAsync(Play(2)); // degraded: no further attempt

        Assert.Equal(QuizStatsStatus.WriteFailed, stats.Report.Status);
        Assert.NotNull(stats.Document);   // the folds stand in memory
        Assert.Empty(folder.Writes);
    }

    [Fact]
    public async Task Bind_AfterLoadFailed_ANewBindReadsAfresh()
    {
        var folder = Folder();
        folder.StatsJson = "corrupt";
        var failed = await BindAsync(folder);
        Assert.Equal(QuizStatsStatus.LoadFailed, failed.Report.Status);

        folder.StatsJson = null; // the file replaced before the next Start
        var fresh = await BindAsync(folder);

        Assert.Equal(QuizStatsStatus.Ready, fresh.Report.Status);
    }

    [Fact]
    public async Task Bind_ReadsTheFileNotThePreviousRecordingsDocument()
    {
        var folder = Folder();
        var first = await BindAsync(folder);
        await first.RecordAsync(Play(1));

        folder.StatsJson = null;
        var second = await BindAsync(folder);
        await second.RecordAsync(Play(2));

        var last = JsonSerializer.Deserialize<ProblemStatsDocument>(folder.Writes[^1]);
        Assert.NotNull(last);
        Assert.Equal(PlayKey(2), Assert.Single(last.Problems).Key);
    }

    [Fact]
    public async Task Bind_FreshFolderWithNoStats_StillBindsAndRecords()
    {
        // The bind gates on capability, never on the folder holding stats: a brand-new
        // folder's first run is what creates the record a mix later composes from.
        var folder = Folder();

        var probe = await StatsProbe.OfAsync(folder);
        var stats = await BindAsync(folder);

        Assert.False(probe.HasStats);
        Assert.Equal(QuizStatsStatus.Ready, stats.Report.Status);
        await stats.RecordAsync(Play());
        Assert.Single(folder.Writes);
    }

    // -----------------------------------------------------------------------
    //  StatsProbe — the pick-time facts: HasStats (the mix's visibility) and the forecasts
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Probe_EnabledFolderWithStatsContent_HasStats()
    {
        var folder = Folder();
        folder.StatsJson = StatsDocumentJson();

        var probe = await StatsProbe.OfAsync(folder);

        Assert.True(probe.HasStats);
        Assert.Null(probe.SetAsideForecast);
        Assert.False(probe.Unreadable);
        Assert.False(probe.Unwritable);
    }

    [Theory]
    [InlineData(null)]                                         // no file at all
    [InlineData("not json at all")]                            // unreadable
    [InlineData(RetiredStatsFixture.NewerSchemaJson)]          // written by a later BgQuiz
    [InlineData(RetiredStatsFixture.ClaimsV1ButMalformedJson)] // corrupt, not retired
    [InlineData(RetiredStatsFixture.V1Json)]                   // retired…
    [InlineData(RetiredStatsFixture.V2Json)]                   // …either of them
    [InlineData(RetiredStatsFixture.V4EmptyJson)]              // foldable, nothing in it
    [InlineData(RetiredStatsFixture.ClaimsV4ButMalformedJson)] // foldable signal, body rejected
    public async Task Probe_MissingOrUnusable_AllReadNoStats(string? statsJson)
    {
        // The ruling in one place: a document that cannot be weighted by is no document.
        var folder = Folder();
        folder.StatsJson = statsJson;

        var probe = await StatsProbe.OfAsync(folder);

        Assert.False(probe.HasStats);
        Assert.Empty(folder.Writes);
    }

    [Fact]
    public async Task Probe_EmptyCurrentVersionDocument_HasNoStats()
    {
        var folder = Folder();
        folder.StatsJson = JsonSerializer.Serialize(ProblemStatsDocument.Empty, QuizStatsFile.DocumentTypeInfo);

        var probe = await StatsProbe.OfAsync(folder);

        Assert.False(probe.HasStats);
    }

    [Fact]
    public async Task Probe_V4File_ReadsAsStatsToWeightBy_AndForecastsNoRestart()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V4Json;

        var probe = await StatsProbe.OfAsync(folder);

        Assert.True(probe.HasStats);
        Assert.Null(probe.SetAsideForecast);
        Assert.False(probe.Unreadable);
        Assert.Empty(folder.Writes);
    }

    [Theory]
    [InlineData(FolderWriteCapability.BrowserUnsupported)]
    [InlineData(FolderWriteCapability.PermissionDenied)]
    public async Task Probe_WithoutWriteCapability_ReadsAndProbesNothing(FolderWriteCapability capability)
    {
        var folder = Folder(capability);
        folder.StatsJson = "not json at all";
        folder.Writability = FileWritability.NotWritable;

        var probe = await StatsProbe.OfAsync(folder);

        Assert.Equal(StatsProbe.None, probe);
        Assert.Empty(folder.FileNames);
        Assert.Empty(folder.WritabilityProbeNames);
    }

    [Fact]
    public async Task Probe_ReadsAndNeverWrites()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V1Json;

        var probe = await StatsProbe.OfAsync(folder);

        Assert.NotNull(probe.SetAsideForecast);
        Assert.Empty(folder.Writes);
        Assert.Equal(RetiredStatsFixture.V1Json, folder.StatsJson);
    }

    [Theory]
    [InlineData(RetiredStatsFixture.V1Json, 1)]
    [InlineData(RetiredStatsFixture.V2Json, 2)]
    public async Task Probe_RetiredFile_ForecastsTheNameThatVersionWillBeSetAsideUnder(string statsJson, int schemaVersion)
    {
        // The forecast follows the version the file declared, through the derivation the act
        // calls, so the two tenses of one event cannot name two files.
        var folder = Folder();
        folder.StatsJson = statsJson;

        var probe = await StatsProbe.OfAsync(folder);

        Assert.Equal(QuizStatsFile.RetiredNameFor(schemaVersion), probe.SetAsideForecast);
        Assert.False(probe.HasStats);
        Assert.False(probe.Unreadable);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData(RetiredStatsFixture.ClaimsV1ButMalformedJson)]
    [InlineData(RetiredStatsFixture.NewerSchemaJson)]
    [InlineData(RetiredStatsFixture.ClaimsV4ButMalformedJson)]
    public async Task Probe_AFileTheBindWillRefuse_ForecastsUnreadable_AndNoSetAside(string statsJson)
    {
        var folder = Folder();
        folder.StatsJson = statsJson;

        var probe = await StatsProbe.OfAsync(folder);

        Assert.True(probe.Unreadable);
        Assert.False(probe.HasStats);
        Assert.Null(probe.SetAsideForecast);
    }

    [Fact]
    public async Task Probe_TheBrowserFailsTheRead_ForecastsUnreadable_AndStillProbesWritability()
    {
        var folder = Folder();
        folder.ReadException = new JSException("read failed");
        folder.Writability = FileWritability.NotWritable;

        var probe = await StatsProbe.OfAsync(folder);

        Assert.True(probe.Unreadable);
        Assert.True(probe.Unwritable);
        Assert.False(probe.HasStats);
    }

    [Theory]
    [InlineData(FileWritability.NotWritable, true)]
    [InlineData(FileWritability.Writable, false)]
    [InlineData(FileWritability.Absent, false)] // gone between the read and the probe
    public async Task Probe_Unwritable_FollowsTheProducersAnswer(FileWritability answer, bool expected)
    {
        var folder = Folder();
        folder.StatsJson = StatsDocumentJson();
        folder.Writability = answer;

        var probe = await StatsProbe.OfAsync(folder);

        Assert.Equal(expected, probe.Unwritable);
        Assert.Equal([QuizStatsFile.FileName], folder.WritabilityProbeNames);
        Assert.True(probe.HasStats); // the mix answer is the read's alone
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData(RetiredStatsFixture.V1Json)]
    public async Task Probe_AsksWritabilityOfUnreadableAndRetiredFilesToo(string statsJson)
    {
        var folder = Folder();
        folder.StatsJson = statsJson;
        folder.Writability = FileWritability.NotWritable;

        var probe = await StatsProbe.OfAsync(folder);

        Assert.True(probe.Unwritable);
    }

    [Fact]
    public async Task Probe_NoFile_AsksNoWritability()
    {
        var folder = Folder();
        folder.Writability = FileWritability.NotWritable;

        var probe = await StatsProbe.OfAsync(folder);

        Assert.Empty(folder.WritabilityProbeNames);
        Assert.False(probe.Unwritable);
    }

    [Fact]
    public async Task Probe_AWritabilityProbeTheBrowserFails_ForecastsNothing()
    {
        var folder = Folder();
        folder.StatsJson = StatsDocumentJson();
        folder.WritabilityProbeException = new JSException("probe failed");

        var probe = await StatsProbe.OfAsync(folder);

        Assert.False(probe.Unwritable);
        Assert.False(probe.Unreadable);
        Assert.True(probe.HasStats);
    }

    [Fact]
    public async Task Probe_AfterTheBindPerformedTheRetirement_HasNothingToForecast()
    {
        // Forecast, then report, in the one order they can happen: the next probe of the
        // same folder finds the current document the bind seeded.
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V1Json;
        var before = await StatsProbe.OfAsync(folder);
        Assert.Equal(QuizStatsFile.RetiredNameFor(1), before.SetAsideForecast);

        var stats = await BindAsync(folder);
        Assert.Equal(QuizStatsFile.RetiredNameFor(1), stats.Report.SetAsideFileName);

        var after = await StatsProbe.OfAsync(folder);

        Assert.Null(after.SetAsideForecast);
    }

    // -----------------------------------------------------------------------
    //  DocumentTypeInfo — the one serializer contract (halheinrich/backgammon#129)
    // -----------------------------------------------------------------------

    [Fact]
    public void DocumentTypeInfo_WritesByteIdenticalToTheReflectionPath()
    {
        // The mechanism changes, the bytes do not: the source-generated contract writes
        // exactly what the reflection path wrote, on a document with both submission kinds.
        var doc = ProblemStatsDocument.Empty
            .Plus(PlaySubmission(1), Clock)
            .Plus(CubeSubmission(2), Clock)
            .Plus(PlaySubmission(1, correct: false), Clock);

        var reflection = JsonSerializer.Serialize(
            doc, new JsonSerializerOptions { WriteIndented = true });
        var sourceGenerated = JsonSerializer.Serialize(doc, QuizStatsFile.DocumentTypeInfo);

        Assert.Equal(reflection, sourceGenerated);
    }

    // -----------------------------------------------------------------------
    //  As the recording goes — its knowledge of the file supersedes the pick's
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FileHasStats_FollowsTheWrites_FromTheSeedToTheFirstRecord()
    {
        var folder = Folder();
        var stats = await BindAsync(folder);
        Assert.False(stats.FileHasStats);   // seeded in memory; no file yet

        await stats.RecordAsync(Play());

        Assert.True(stats.FileHasStats);
    }

    [Fact]
    public async Task FileHasStats_AfterARetirement_IsTheSeededEmptyFile()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V1Json;

        var stats = await BindAsync(folder);

        Assert.False(stats.FileHasStats);
    }

    [Fact]
    public async Task FileHasStats_AfterAFold_IsTheMergedFile()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V4Json;

        var stats = await BindAsync(folder);

        Assert.True(stats.FileHasStats);
    }

    [Fact]
    public async Task FileHasStats_AFailedWrite_LeavesTheFileAsItWas()
    {
        var folder = Folder();
        folder.WriteException = new JSException("disk gone");
        var stats = await BindAsync(folder);

        await stats.RecordAsync(Play());

        Assert.Equal(QuizStatsStatus.WriteFailed, stats.Report.Status);
        Assert.False(stats.FileHasStats);   // the fold stands in memory only
    }

    [Theory]
    [InlineData(FolderWriteCapability.PermissionDenied, null)]
    [InlineData(FolderWriteCapability.Enabled, "not json at all")]
    public async Task FileHasStats_ARecordingThatNeverResolvedTheFile_SaysNothing(FolderWriteCapability capability, string? statsJson)
    {
        var folder = Folder(capability);
        folder.StatsJson = statsJson;

        var stats = await BindAsync(folder);

        Assert.Null(stats.Document);
        Assert.Null(stats.FileHasStats);
    }

    [Fact]
    public async Task AfterRecording_ARecordingThatResolvedTheFile_ClearsTheForecasts_AndSaysWhatItHoldsNow()
    {
        var folder = Folder();
        folder.StatsJson = RetiredStatsFixture.V1Json;
        folder.Writability = FileWritability.NotWritable;
        var atThePick = await StatsProbe.OfAsync(folder);
        Assert.NotNull(atThePick.SetAsideForecast);
        var stats = await BindAsync(folder);   // the set-aside happens here

        var seeded = atThePick.AfterRecording(stats);

        Assert.False(seeded.HasStats);
        Assert.Null(seeded.SetAsideForecast);
        Assert.False(seeded.Unreadable);
        Assert.True(seeded.Unwritable);   // the pick's probe stands

        await stats.RecordAsync(Play());

        Assert.True(atThePick.AfterRecording(stats).HasStats);
    }

    [Fact]
    public async Task AfterRecording_ARecordingThatNeverResolvedTheFile_LeavesThePicksReadingStanding()
    {
        var folder = Folder();
        folder.StatsJson = "not json at all";
        var atThePick = await StatsProbe.OfAsync(folder);
        var stats = await BindAsync(folder);
        Assert.Equal(QuizStatsStatus.LoadFailed, stats.Report.Status);

        Assert.Same(atThePick, atThePick.AfterRecording(stats));
        Assert.True(atThePick.Unreadable);
    }
}
