using System.Collections.Immutable;
using BgDataTypes_Lib;
using BgFolderAccess_Razor;
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
/// </summary>
public class QuizAppTests : BunitContext
{
    private readonly FakeFolderAccess _folders = new();
    private readonly List<BgDecisionData> _problems = [];
    private Exception? _loadFailure;
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
        Services.AddScoped<ProblemLoad>(_ => LoadAsync);
        Services.AddScoped(sp => new QuizApp(
            sp.GetRequiredService<IFolderAccess>(), sp.GetRequiredService<FilterSetup>(),
            sp.GetRequiredService<QuizSettings>(), sp.GetRequiredService<ProblemLoad>(),
            NullLogger<QuizApp>.Instance, TimeProvider.System));
    }

    private Task<LoadedProblems> LoadAsync(ImmutableArray<PickedFile> files, Action<LoadProgress>? progress, CancellationToken ct)
    {
        _loads++;
        progress?.Invoke(new LoadProgress(0, files.Length));
        if (_loadFailure is { } failure) return Task.FromException<LoadedProblems>(failure);
        return Task.FromResult(new LoadedProblems([.. _problems], TestFixtures.WalkedReport(), forcedPlaysSetAside: 0));
    }

    private QuizApp App => Services.GetRequiredService<QuizApp>();
    private FilterSetup Setup => Services.GetRequiredService<FilterSetup>();

    private static Play Best() => TestFixtures.OpeningBest();
    private static Play Alt() => TestFixtures.OpeningAlternative();

    private static CheckerPlayDecision Problem(int n) =>
        TestFixtures.TwoChoiceDecision(Best(), Alt(), play2Loss: 0.05, away: n);

    private static FolderPickOutcome Folder(string name = "Corpus") =>
        TestFixtures.Picked(name, FolderWriteCapability.Enabled, [TestFixtures.ReadableXg()]);

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

    // ── Folder ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task APick_LandsTheSetup_ReportsItsSourceToTheFilterOwner_AndCountsTheEmptySelection()
    {
        var app = await PickedAsync(Problem(1), Problem(2));
        Assert.Equal(QuizPhase.Idle, app.Phase);
        Assert.NotNull(app.Setup);
        Assert.Equal("Corpus", app.Setup!.FolderName);
        Assert.True(Setup.Current.IsInEffectFor(app.Setup.FilterSource));
        Assert.NotNull(app.FilterInEffect);
        Assert.NotNull(app.CurrentSelection);
        Assert.Equal(2, app.CurrentSelection!.AnswerTypes.Total);
        Assert.Equal(1, _loads);
    }

    [Fact]
    public async Task ACancelledPick_KeepsTheSetupInHand()
    {
        var app = await PickedAsync(Problem(1));
        var setup = app.Setup;
        _folders.NextPickOutcome = FolderPickOutcome.CancelledOutcome;
        Assert.Equal(FolderOutcomeKind.Cancelled, (await app.PickFolderAsync(default)).Kind);
        Assert.Same(setup, app.Setup);
        Assert.Equal(1, _loads);
    }

    [Fact]
    public async Task AFolderWithNoFiles_OrOneThatFailsToArrive_LeavesNoSetup_AndReleasesTheFolder()
    {
        var app = await PickedAsync(Problem(1));
        _folders.NextPickOutcome = TestFixtures.Picked("Empty", FolderWriteCapability.Enabled, []);
        Assert.Equal(FolderOutcomeKind.NoFiles, (await app.PickFolderAsync(default)).Kind);
        Assert.Null(app.Setup);
        Assert.Null(Setup.Current.Source);
        Assert.Equal(1, _folders.ClearPickedCallCount);

        _folders.NextPickOutcome = Folder();
        _folders.PickException = new IOException("the drive went away");
        var failed = await app.PickFolderAsync(default);
        Assert.Equal(FolderOutcomeKind.Failed, failed.Kind);
        Assert.IsType<IOException>(failed.Failure);
        Assert.Null(app.Setup);
        Assert.Equal(QuizPhase.Idle, app.Phase);
    }

    [Fact]
    public async Task AParseFailure_LeavesNoSetup_AndTheRunInProgressGoesOn()
    {
        var app = await StartedAsync(await PickedAsync(Problem(1), Problem(2)));
        var run = app.Run;
        _loadFailure = new InvalidDataException("not an XG file after all");
        _folders.NextPickOutcome = Folder("Other");

        var outcome = await app.PickFolderAsync(default);

        Assert.Equal(FolderOutcomeKind.Failed, outcome.Kind);
        Assert.Null(app.Setup);
        Assert.Same(run, app.Run);
        Assert.Null(app.Finished);
    }

    [Fact]
    public async Task ANewSampleLanding_EndsTheRunInProgress_WithItsSummaryKept()
    {
        var app = await StartedAsync(await PickedAsync(Problem(1), Problem(2)));
        await app.SubmitPlayAsync(Best());
        _folders.NextPickOutcome = Folder("Other");

        Assert.Equal(FolderOutcomeKind.Landed, (await app.PickFolderAsync(default)).Kind);

        Assert.Null(app.Run);
        Assert.NotNull(app.Finished);
        Assert.True(app.Finished!.EndedByNewSample);
        Assert.Equal(1, app.Finished.Result.Score.PlayDecisions.Correct);
        Assert.Equal("Other", app.Setup!.FolderName);
    }

    [Fact]
    public async Task Clear_LetsTheFolderGo_AndKeepsTheRun()
    {
        var app = await StartedAsync(await PickedAsync(Problem(1)));
        var run = app.Run;
        Assert.True(await app.ClearFolderAsync());
        Assert.Null(app.Setup);
        Assert.Null(app.FilterInEffect);
        Assert.Same(run, app.Run);
        Assert.Equal(1, _folders.ClearPickedCallCount);
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

    // ── The run ─────────────────────────────────────────────────────────────────

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
