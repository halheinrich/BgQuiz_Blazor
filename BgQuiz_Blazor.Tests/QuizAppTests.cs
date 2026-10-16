using System.Collections.Immutable;
using System.Text.Json;
using BgDataTypes_Lib;
using BgFolderAccess_Razor;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;
using XgFilter_Razor.Components;
using XgFilter_Razor.TestSupport;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The coordinator's table: which command does what in which phase, over a fake folder seam
/// and a fake parse. The filter owner is the real one, settled by rendering the surface once.
/// The folder context's lifetime, the stats recording and the mix ride on the same table.
/// </summary>
public class QuizAppTests : BunitContext
{
    private readonly FakeFolderAccess _folders = new();
    private readonly List<BgDecisionData> _problems = [];
    private Exception? _loadFailure;
    private Action? _onLoad;
    private int _loads;
    private readonly BrowserStoragePlan _plan;

    public QuizAppTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _plan = BrowserStoragePlan.On(JSInterop);
        _plan.ExpectRead(BrowserStorageArea.Local, QuizSettings.StorageKey, BrowserStorageReadAnswer.Absent);
        Services.AddSingleton<IFolderAccess>(_folders);
        Services.AddSingleton(TimeProvider.System);
        Services.AddBrowserStorage();
        Services.AddScoped<BrowserStorageCondition>();
        Services.AddScoped<FilterStorageRefusalSink>();
        Services.AddFilterSurface<FilterStorageRefusalSink>();
        Services.AddScoped<QuizSettings>();
        Services.AddScoped(sp => new MixDraft(
            sp.GetRequiredService<BrowserStorage>(), NullLogger<MixDraft>.Instance, sp.GetRequiredService<BrowserStorageCondition>()));
        Services.AddScoped<ProblemLoad>(_ => LoadAsync);
        Services.AddScoped(sp => new QuizApp(
            sp.GetRequiredService<IFolderAccess>(), sp.GetRequiredService<FilterSetup>(),
            sp.GetRequiredService<QuizSettings>(), sp.GetRequiredService<MixDraft>(), sp.GetRequiredService<ProblemLoad>(),
            NullLogger<QuizApp>.Instance, TimeProvider.System));
    }

    private Task<LoadedProblems> LoadAsync(ImmutableArray<PickedFile> files, Action<LoadProgress>? progress, CancellationToken ct)
    {
        _loads++;
        progress?.Invoke(new LoadProgress(0, files.Length));
        _onLoad?.Invoke();
        if (_loadFailure is { } failure) return Task.FromException<LoadedProblems>(failure);
        return Task.FromResult(new LoadedProblems([.. _problems], TestFixtures.WalkedReport(), forcedPlaysSetAside: 0));
    }

    private QuizApp App => Services.GetRequiredService<QuizApp>();
    private FilterSetup Setup => Services.GetRequiredService<FilterSetup>();
    private QuizSettings Settings => Services.GetRequiredService<QuizSettings>();
    private MixDraft Draft => Services.GetRequiredService<MixDraft>();

    private static Play Best() => TestFixtures.OpeningBest();
    private static Play Alt() => TestFixtures.OpeningAlternative();

    private static CheckerPlayDecision Problem(int n) =>
        TestFixtures.TwoChoiceDecision(Best(), Alt(), play2Loss: 0.05, away: n);

    /// <summary>A problem with an id of its own: an active mix contributes each decision id once.</summary>
    private static CheckerPlayDecision DistinctProblem(int n) =>
        TestFixtures.TwoChoiceDecision(
            Best(), Alt(), play2Loss: 0.05, away: n, location: TestFixtures.SourceLocation.OnePosition($"p{n}.xgp"));

    private static FolderPickOutcome Folder(string name = "Corpus", FolderWriteCapability capability = FolderWriteCapability.Enabled) =>
        TestFixtures.Picked(name, capability, [TestFixtures.ReadableXg()]);

    /// <summary>The folder the next pick lands, to stage its stats file and read what the app did with it.</summary>
    private FakeFolderContext NextFolder => TestFixtures.FolderOf(_folders.NextPickOutcome);

    /// <summary>A stats document with one record, in the wire format the app writes: the folder "has stats".</summary>
    private static string StatsWithContent() =>
        JsonSerializer.Serialize(
            ProblemStatsDocument.Empty.Plus(
                TestFixtures.Scored(Problem(9), Best(), PlayRanking.Equity), TimeProvider.System),
            QuizStatsFile.DocumentTypeInfo);

    /// <summary>Mount the filter surface once, so the owner's restoration settles and the empty selection is in effect for whatever source is reported.</summary>
    private void SettleTheFilterOwner()
    {
        _plan.ExpectFilterPanelMount();
        _plan.ExpectFilterRestore(FilterRestoration.NothingStored);
        Render<FilterSurface>();
    }

    private async Task<QuizApp> PickedAsync(params BgDecisionData[] problems)
    {
        SettleTheFilterOwner();
        _problems.AddRange(problems);
        _folders.NextPickOutcome = Folder();
        var app = App;
        var outcome = await app.PickFolderAsync(default);
        Assert.Equal(FolderOutcomeKind.Landed, outcome.Kind);
        return app;
    }

    private static async Task<QuizApp> StartedAsync(QuizApp app)
    {
        Assert.Equal(StartOutcome.Started, await app.StartAsync());
        Assert.NotNull(app.Run);
        return app;
    }

    /// <summary>Turn the setting on and build a one-row "Everything else" mix, declaring each store's write.</summary>
    private async Task TurnTheMixOnAsync()
    {
        await Settings.EnsureHydratedAsync();
        _plan.ExpectWrite(BrowserStorageArea.Local, QuizSettings.StorageKey, SettingsPayload.Of(weightQuizzesByStats: true), BrowserStorageWriteAnswer.Succeeded);
        await Settings.SetWeightQuizzesByStatsAsync(true);
        _plan.ExpectWrite(BrowserStorageArea.Local, MixDraft.StorageKey,
            new QuizMix([new QuizMixEntry(QuizCategory.NeverSeen, 100)]).ToJson(), BrowserStorageWriteAnswer.Succeeded);
        _plan.ExpectWrite(BrowserStorageArea.Local, MixDraft.StorageKey,
            new QuizMix([new QuizMixEntry(QuizCategory.EverythingElse, 100)]).ToJson(), BrowserStorageWriteAnswer.Succeeded);
        await Draft.AddRowAsync();
        await Draft.SetKindAsync(0, QuizCategoryKind.EverythingElse);
    }

    // ── Folder ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task APick_LandsTheSetup_ReportsItsSourceToTheFilterOwner_AndCountsTheEmptySelection()
    {
        var app = await PickedAsync(Problem(1), Problem(2));
        Assert.Equal(QuizPhase.Idle, app.Phase);
        Assert.NotNull(app.Setup);
        Assert.Equal("Corpus", app.Setup!.FolderName);
        Assert.Same(NextFolder, app.Setup.Folder);
        Assert.True(Setup.Current.IsInEffectFor(app.Setup.FilterSource));
        Assert.NotNull(app.FilterInEffect);
        Assert.NotNull(app.CurrentSelection);
        Assert.Equal(2, app.CurrentSelection!.AnswerTypes.Total);
        Assert.Equal(1, _loads);
    }

    [Fact]
    public async Task APick_ProbesTheFoldersStatsFile_WithoutWritingIt()
    {
        SettleTheFilterOwner();
        _problems.Add(Problem(1));
        _folders.NextPickOutcome = Folder();
        NextFolder.StatsJson = StatsWithContent();
        var app = App;

        Assert.Equal(FolderOutcomeKind.Landed, (await app.PickFolderAsync(default)).Kind);

        Assert.True(app.Setup!.Stats.HasStats);
        Assert.Empty(NextFolder.Writes);
    }

    [Fact]
    public async Task ACancelledPick_KeepsTheSetupInHand()
    {
        var app = await PickedAsync(Problem(1));
        var setup = app.Setup;
        _folders.NextPickOutcome = FolderPickOutcome.CancelledOutcome;
        Assert.Equal(FolderOutcomeKind.Cancelled, (await app.PickFolderAsync(default)).Kind);
        Assert.Same(setup, app.Setup);
        Assert.False(setup!.Folder.As().IsReleased);
        Assert.Equal(1, _loads);
    }

    [Fact]
    public async Task AFolderWithNoFiles_OrOneThatFailsToArrive_LeavesNoSetup_AndReleasesTheFolders()
    {
        var app = await PickedAsync(Problem(1));
        var first = NextFolder;
        _folders.NextPickOutcome = TestFixtures.Picked("Empty", FolderWriteCapability.Enabled, []);
        var empty = NextFolder;
        Assert.Equal(FolderOutcomeKind.NoFiles, (await app.PickFolderAsync(default)).Kind);
        Assert.Null(app.Setup);
        Assert.Null(Setup.Current.Source);
        Assert.True(first.IsReleased);   // the setup in hand went with the landing
        Assert.True(empty.IsReleased);   // and nothing holds the empty folder

        _folders.NextPickOutcome = Folder();
        _folders.PickException = new IOException("the drive went away");
        var failed = await app.PickFolderAsync(default);
        Assert.Equal(FolderOutcomeKind.Failed, failed.Kind);
        Assert.IsType<IOException>(failed.Failure);
        Assert.Null(app.Setup);
        Assert.Equal(QuizPhase.Idle, app.Phase);
    }

    [Fact]
    public async Task APickThatFailsAfterItsPrompts_LeavesNoSetup_AndTheSeamReleasedItsFolder()
    {
        var app = await PickedAsync(Problem(1));
        var first = NextFolder;
        _folders.NextPickOutcome = Folder("Other");
        var other = NextFolder;
        _folders.OnScanning = () => throw new IOException("a file could not be read");

        var outcome = await app.PickFolderAsync(default);

        Assert.Equal(FolderOutcomeKind.Failed, outcome.Kind);
        Assert.Null(app.Setup);
        Assert.True(first.IsReleased);
        Assert.True(other.IsReleased);
    }

    [Fact]
    public async Task AParseFailureAfterLanding_LeavesNoSetup_ReleasesTheFolder_AndTheRunTheLandingFinished_StaysFinished()
    {
        var app = await StartedAsync(await PickedAsync(Problem(1), Problem(2)));
        var first = NextFolder;
        _loadFailure = new InvalidDataException("not an XG file after all");
        _folders.NextPickOutcome = Folder("Other");
        var other = NextFolder;

        var outcome = await app.PickFolderAsync(default);

        // The sample landed, so the run ended there (SPEC-quiz-history.md §9); the failure to read
        // it leaves no folder and revives nothing, and neither folder is held any more.
        Assert.Equal(FolderOutcomeKind.Failed, outcome.Kind);
        Assert.Null(app.Setup);
        Assert.Null(app.Run);
        Assert.True(app.Finished!.EndedByNewSample);
        Assert.True(first.IsReleased);
        Assert.True(other.IsReleased);
    }

    [Fact]
    public void ANewSampleLanding_ReleasesTheOldCollectionAndItsCount_BeforeTheNewFilesAreRead()
    {
        // Two parsed collections never coexist (§9): the old setup and its count are unreachable
        // by the time the parse begins. Measured inside the parse, on one thread.
        SingleThreadedRunner.Run(async () =>
        {
            var app = await StartedAsync(await PickedAsync(Problem(1), Problem(2)));
            var (oldSetup, oldSelection) = WeakReferencesTo(app);
            bool? setupAlive = null, selectionAlive = null;
            _onLoad = () =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                setupAlive = oldSetup.IsAlive;
                selectionAlive = oldSelection.IsAlive;
            };
            _folders.NextPickOutcome = Folder("Other");

            Assert.Equal(FolderOutcomeKind.Landed, (await app.PickFolderAsync(default)).Kind);

            Assert.False(setupAlive, "the old setup was still reachable while the new files were read");
            Assert.False(selectionAlive, "the old count was still reachable while the new files were read");
            Assert.True(app.Finished!.EndedByNewSample);
            Assert.Equal("Other", app.Setup!.FolderName);
        });
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (WeakReference Setup, WeakReference Selection) WeakReferencesTo(QuizApp app) =>
        (new WeakReference(app.Setup!), new WeakReference(app.CurrentSelection!));

    [Fact]
    public async Task ANewSampleLanding_EndsTheRunInProgress_WithItsSummaryKept_AndReleasesTheOldFolder()
    {
        var app = await StartedAsync(await PickedAsync(Problem(1), Problem(2)));
        var first = NextFolder;
        await app.SubmitPlayAsync(Best());
        _folders.NextPickOutcome = Folder("Other");

        Assert.Equal(FolderOutcomeKind.Landed, (await app.PickFolderAsync(default)).Kind);

        Assert.Null(app.Run);
        Assert.NotNull(app.Finished);
        Assert.True(app.Finished!.EndedByNewSample);
        Assert.Equal(1, app.Finished.Result.Score.PlayDecisions.Correct);
        Assert.Equal("Other", app.Setup!.FolderName);
        Assert.True(first.IsReleased);
        Assert.False(NextFolder.IsReleased);
    }

    [Fact]
    public async Task Clear_LetsTheFolderGo_AndKeepsTheRun_RecordingIntoItsFolder()
    {
        var app = await StartedAsync(await PickedAsync(Problem(1), Problem(2)));
        var folder = NextFolder;
        var run = app.Run;
        Assert.True(await app.ClearFolderAsync());
        Assert.Null(app.Setup);
        Assert.Null(app.FilterInEffect);
        Assert.Same(run, app.Run);
        Assert.False(folder.IsReleased);   // the live run still records into it

        await app.SubmitPlayAsync(Best());
        Assert.Single(folder.Writes);

        app.EndQuiz();
        Assert.True(folder.IsReleased);    // and nothing holds it once the run finishes
    }

    [Fact]
    public async Task Clear_WithNoRun_ReleasesTheFolder()
    {
        var app = await PickedAsync(Problem(1));
        var folder = NextFolder;
        Assert.True(await app.ClearFolderAsync());
        Assert.True(folder.IsReleased);
    }

    // ── Starting ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Start_WithoutASetup_IsRefused_AndWithNothingMatching_KeepsWhatWasInHand()
    {
        SettleTheFilterOwner();
        Assert.Equal(StartOutcome.NoSetup, await App.StartAsync());

        var app = await StartedAsync(await PickedAsync(Problem(1)));
        var run = app.Run;
        _problems.Clear();
        _folders.NextPickOutcome = Folder("Empty-ish");
        Assert.Equal(FolderOutcomeKind.Landed, (await app.PickFolderAsync(default)).Kind);   // ends the run: a new sample
        var finished = app.Finished;
        Assert.NotNull(finished);
        Assert.Equal(StartOutcome.NoProblems, await app.RestartAsync());
        Assert.Same(finished, app.Finished);
        Assert.Null(app.Run);
        Assert.NotSame(run, app.Run);
    }

    [Fact]
    public async Task Start_PresentsTheFirstProblem_AndTheRunKnowsItsCount()
    {
        var app = await StartedAsync(await PickedAsync(Problem(1), Problem(2), Problem(3)));
        Assert.Equal(3, app.ProblemCount);
        Assert.Equal(1, app.ProblemNumber);
        Assert.NotNull(app.Current);
        Assert.Equal("readable.xg", app.Name);   // a one-file pick is named by its file
        Assert.Null(app.Finished);
        Assert.Null(app.Composition);            // a blank mix composes nothing
    }

    [Fact]
    public async Task Start_OverSeveralFiles_NamesTheRunByTheFolder()
    {
        SettleTheFilterOwner();
        _problems.Add(Problem(1));
        _folders.NextPickOutcome = TestFixtures.Picked(
            "Corpus", FolderWriteCapability.Enabled, [TestFixtures.ReadableXg("a.xg"), TestFixtures.ReadableXg("b.xg")]);
        var app = App;
        Assert.Equal(FolderOutcomeKind.Landed, (await app.PickFolderAsync(default)).Kind);

        await StartedAsync(app);

        Assert.Equal("Corpus", app.Name);
    }

    [Fact]
    public async Task Start_BindsTheStatsRecording_OverTheSetupsFolder()
    {
        var app = await StartedAsync(await PickedAsync(Problem(1)));
        Assert.Equal(QuizStatsStatus.Ready, app.Stats!.Status);

        _folders.NextPickOutcome = Folder("Denied", FolderWriteCapability.PermissionDenied);
        Assert.Equal(FolderOutcomeKind.Landed, (await app.PickFolderAsync(default)).Kind);
        await StartedAsync(app);

        Assert.Equal(QuizStatsStatus.Disabled, app.Stats!.Status);
    }

    [Fact]
    public async Task Restart_ComposesAgainFromTheSetupInHand_UnderTheFinishedRunsFilter()
    {
        var app = await StartedAsync(await PickedAsync(Problem(1), Problem(2)));
        app.EndQuiz();
        Assert.NotNull(app.Finished);
        Assert.Equal(StartOutcome.Started, await app.RestartAsync());
        Assert.NotNull(app.Run);
        Assert.Null(app.Finished);
        Assert.Equal(2, app.ProblemCount);
    }

    [Fact]
    public async Task Restart_WithNothingFinished_IsRefused()
    {
        var app = await PickedAsync(Problem(1));
        Assert.Equal(StartOutcome.NothingToRestart, await app.RestartAsync());
    }

    // ── The run, and its recording ───────────────────────────────────────────────

    [Fact]
    public async Task TheRun_AnswersAndMovesOn_AndFinishesWhenTheSequenceRunsOut()
    {
        var app = await StartedAsync(await PickedAsync(Problem(1), Problem(2)));
        await app.SubmitPlayAsync(Alt());
        Assert.NotNull(app.Review);
        app.Next();
        Assert.Equal(2, app.ProblemNumber);
        Assert.True(app.CanGoBack);
        app.GoBack();
        Assert.Equal(1, app.ProblemNumber);
        app.GoToLast();
        Assert.Equal(2, app.ProblemNumber);
        app.Next();   // skips the unanswered frontier; the sequence is exhausted
        Assert.True(app.IsFinished);
        Assert.Equal(1, app.SkippedCount);
        Assert.Equal(1, app.Finished!.Result.Score.PlayDecisions.Submitted);
        Assert.False(app.Finished.EndedByNewSample);
    }

    [Fact]
    public async Task AnAnswerOfRecord_IsWrittenToTheFoldersStatsFile_BeforeTheNextCommandIsAccepted()
    {
        var app = await StartedAsync(await PickedAsync(Problem(1), Problem(2)));
        var folder = NextFolder;
        QuizPhase? phaseDuringTheWrite = null;
        folder.OnWrite = _ =>
        {
            phaseDuringTheWrite = app.Phase;
            app.Next();   // refused: the write is the one operation in flight
        };

        await app.SubmitPlayAsync(Best());

        Assert.Equal(QuizPhase.Recording, phaseDuringTheWrite);
        Assert.Equal(1, app.ProblemNumber);
        Assert.Equal(QuizPhase.Idle, app.Phase);
        var written = JsonSerializer.Deserialize<ProblemStatsDocument>(Assert.Single(folder.Writes));
        Assert.Equal(1, written!.Count);
        Assert.Equal(FakeFolderContext.StatsFileName, folder.FileNames[^1]);   // the file name is the app's
    }

    [Fact]
    public async Task APracticeAnswer_RecordsNothing()
    {
        var app = await StartedAsync(await PickedAsync(Problem(1), Problem(2)));
        var folder = NextFolder;
        await app.SubmitPlayAsync(Best());
        app.Next();
        app.GoBack();
        Assert.Single(folder.Writes);

        await app.SubmitPlayAsync(Alt());   // the problem is already answered: practice

        Assert.Single(folder.Writes);
    }

    [Fact]
    public async Task AWriteThatFails_KeepsTheQuizGoing_AndTheReportSaysSo()
    {
        var app = await StartedAsync(await PickedAsync(Problem(1), Problem(2)));
        NextFolder.WriteException = new Microsoft.JSInterop.JSException("disk gone");

        await app.SubmitPlayAsync(Best());

        Assert.Equal(QuizStatsStatus.WriteFailed, app.Stats!.Status);
        Assert.NotNull(app.Review);
        Assert.Equal(QuizPhase.Idle, app.Phase);
    }

    [Fact]
    public async Task TheStatsReport_OutlivesTheRun_OnTheFinishedQuiz()
    {
        SettleTheFilterOwner();
        _problems.Add(Problem(1));
        _folders.NextPickOutcome = Folder();
        NextFolder.StatsJson = "not json at all";
        var app = App;
        Assert.Equal(FolderOutcomeKind.Landed, (await app.PickFolderAsync(default)).Kind);
        await StartedAsync(app);
        var report = app.Stats;
        Assert.Equal(QuizStatsStatus.LoadFailed, report!.Status);

        app.EndQuiz();

        Assert.Same(report, app.Stats);
        Assert.Same(report, app.Finished!.Stats);
    }

    [Fact]
    public async Task AFailedReplacement_KeepsThePriorRun_AndItsRecording()
    {
        // The halheinrich/backgammon#404 case: a Start that is refused leaves the run in hand recording where it
        // was, into its own folder, with its own report. Here the folder's stats file turns
        // unreadable under a live weighted run, so the next weighted Start is refused.
        SettleTheFilterOwner();
        _problems.AddRange([DistinctProblem(1), DistinctProblem(2)]);
        _folders.NextPickOutcome = Folder();
        var folder = NextFolder;
        folder.StatsJson = StatsWithContent();
        var app = App;
        Assert.Equal(FolderOutcomeKind.Landed, (await app.PickFolderAsync(default)).Kind);
        await TurnTheMixOnAsync();
        await StartedAsync(app);
        var run = app.Run;
        var report = app.Stats;
        folder.StatsJson = "not json at all";

        Assert.Equal(StartOutcome.MixRequiresStats, await app.StartAsync());

        Assert.Same(run, app.Run);
        Assert.Same(report, app.Stats);
        Assert.False(folder.IsReleased);
        await app.SubmitPlayAsync(Best());
        Assert.Single(folder.Writes);   // still recording, into the same folder: the guard folds in memory and writes
    }

    [Fact]
    public async Task EndQuiz_FinishesTheRun_AndRunCommandsThenDoNothing()
    {
        var app = await StartedAsync(await PickedAsync(Problem(1)));
        app.EndQuiz();
        Assert.True(app.IsFinished);
        app.Next();
        app.GoBack();
        await app.SubmitPlayAsync(Best());
        Assert.True(app.IsFinished);
        Assert.Null(app.Run);
    }

    // ── The mix ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheMix_IsInEffect_OnlyWithTheSettingOn_AndStatsInTheFolder()
    {
        SettleTheFilterOwner();
        _problems.Add(Problem(1));
        _folders.NextPickOutcome = Folder();
        NextFolder.StatsJson = StatsWithContent();
        var app = App;
        Assert.Equal(FolderOutcomeKind.Landed, (await app.PickFolderAsync(default)).Kind);
        Assert.False(app.MixInEffect);   // the setting is off

        await TurnTheMixOnAsync();
        Assert.True(app.MixInEffect);

        _folders.NextPickOutcome = Folder("Fresh");   // no stats
        Assert.Equal(FolderOutcomeKind.Landed, (await app.PickFolderAsync(default)).Kind);
        Assert.False(app.MixInEffect);
    }

    [Fact]
    public async Task Start_ComposesWithTheMixInEffect_AndTheRunSaysWhatItDrew()
    {
        SettleTheFilterOwner();
        _problems.AddRange([DistinctProblem(1), DistinctProblem(2)]);
        _folders.NextPickOutcome = Folder();
        NextFolder.StatsJson = StatsWithContent();
        var app = App;
        Assert.Equal(FolderOutcomeKind.Landed, (await app.PickFolderAsync(default)).Kind);
        await TurnTheMixOnAsync();

        await StartedAsync(app);

        Assert.NotNull(app.Composition);
        Assert.Equal(2, app.Composition!.DrawnCount);
        Assert.False(app.CompositionNoticeDismissed);
        Assert.False(app.Finished?.Mix.IsPassthrough ?? false);
        app.DismissCompositionNotice();
        Assert.True(app.CompositionNoticeDismissed);
    }

    [Fact]
    public async Task Start_WithTheMixOnScreenInvalid_IsRefused()
    {
        SettleTheFilterOwner();
        _problems.Add(Problem(1));
        _folders.NextPickOutcome = Folder();
        NextFolder.StatsJson = StatsWithContent();
        var app = App;
        Assert.Equal(FolderOutcomeKind.Landed, (await app.PickFolderAsync(default)).Kind);
        await TurnTheMixOnAsync();
        await Draft.SetPercentTextAsync(0, "");   // invalid: writes nothing

        Assert.Null(app.MixForStart);
        Assert.Equal(StartOutcome.MixInvalid, await app.StartAsync());
        Assert.Null(app.Run);
    }

    [Fact]
    public async Task Start_WithoutMix_RunsThisQuizUnweighted()
    {
        SettleTheFilterOwner();
        _problems.Add(Problem(1));
        _folders.NextPickOutcome = Folder();
        NextFolder.StatsJson = StatsWithContent();
        var app = App;
        Assert.Equal(FolderOutcomeKind.Landed, (await app.PickFolderAsync(default)).Kind);
        await TurnTheMixOnAsync();

        Assert.Equal(StartOutcome.Started, await app.StartAsync(withoutMix: true));

        Assert.Null(app.Composition);
        Assert.True(app.MixInEffect);   // the mix stays what the user built
    }

    [Fact]
    public async Task Restart_ReplaysTheRunsMix_OnlyWhileTheMixIsInEffect()
    {
        SettleTheFilterOwner();
        _problems.AddRange([DistinctProblem(1), DistinctProblem(2)]);
        _folders.NextPickOutcome = Folder();
        NextFolder.StatsJson = StatsWithContent();
        var app = App;
        Assert.Equal(FolderOutcomeKind.Landed, (await app.PickFolderAsync(default)).Kind);
        await TurnTheMixOnAsync();
        await StartedAsync(app);
        app.EndQuiz();
        Assert.False(app.Finished!.Mix.IsPassthrough);

        Assert.Equal(StartOutcome.Started, await app.RestartAsync());
        Assert.NotNull(app.Composition);   // replayed
        app.EndQuiz();

        _plan.ExpectWrite(BrowserStorageArea.Local, QuizSettings.StorageKey, SettingsPayload.Of(weightQuizzesByStats: false), BrowserStorageWriteAnswer.Succeeded);
        await Settings.SetWeightQuizzesByStatsAsync(false);
        Assert.Equal(StartOutcome.Started, await app.RestartAsync());
        Assert.Null(app.Composition);      // the mix is gone: unweighted
    }

    // ── The one operation in flight ─────────────────────────────────────────────

    [Fact]
    public async Task WhileAPickIsInFlight_EveryOtherCommandIsRefused_AndChangedFiresForEachPhase()
    {
        SettleTheFilterOwner();
        _problems.Add(Problem(1));
        var app = App;
        var phases = new List<QuizPhase>();
        app.Changed += () => phases.Add(app.Phase);
        var prompts = new TaskCompletionSource();
        _folders.OnPrompts = () => prompts.Task;
        _folders.NextPickOutcome = Folder();

        var pick = app.PickFolderAsync(default);
        Assert.Equal(QuizPhase.Picking, app.Phase);
        Assert.Equal(FolderOutcomeKind.Refused, (await app.PickFolderAsync(default)).Kind);
        Assert.Equal(StartOutcome.Busy, await app.StartAsync());
        Assert.False(await app.ClearFolderAsync());

        prompts.SetResult();
        Assert.Equal(FolderOutcomeKind.Landed, (await pick).Kind);
        Assert.Equal(QuizPhase.Idle, app.Phase);
        Assert.Contains(QuizPhase.Picking, phases);
        Assert.Contains(QuizPhase.Loading, phases);
        Assert.Equal(QuizPhase.Idle, phases[^1]);
    }

    [Fact]
    public async Task TheSelection_DescribesTheSetupTheFilterAndTheRanking_AndIsRecomputedOnlyWhenOneMoves()
    {
        var app = await PickedAsync(Problem(1), Problem(2));
        var first = app.CurrentSelection;
        Assert.NotNull(first);
        await app.EnsureSelectionAsync();
        Assert.Same(first, app.CurrentSelection);

        _folders.NextPickOutcome = Folder("Other");
        Assert.Equal(FolderOutcomeKind.Landed, (await app.PickFolderAsync(default)).Kind);
        var second = app.CurrentSelection;
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Same(app.Setup, second!.Setup);
        Assert.True(second.Describes(app.Setup!, app.FilterInEffect!, Services.GetRequiredService<QuizSettings>().Ranking));
        Assert.False(second.Describes(first!.Setup, app.FilterInEffect!, Services.GetRequiredService<QuizSettings>().Ranking));
    }
}

internal static class FolderContextTestExtensions
{
    /// <summary>The fake behind a context the app holds.</summary>
    public static FakeFolderContext As(this IFolderContext folder) => (FakeFolderContext)folder;
}
