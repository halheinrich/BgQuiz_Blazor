using System.Collections.Immutable;
using System.Text.RegularExpressions;
using BgDataTypes_Lib;
using BgFolderAccess_Razor;
using BgQuiz_Blazor.Client.Components;
using BgQuiz_Blazor.Client.Quiz;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Razor;
using XgFilter_Razor.Components;
using XgFilter_Razor.TestSupport;
using HomePage = BgQuiz_Blazor.Client.Components.Pages.Home;
using QuizPage = BgQuiz_Blazor.Client.Components.Pages.Quiz;
using DonePage = BgQuiz_Blazor.Client.Components.Pages.Done;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The pages over the coordinator: Home sets up and starts, Quiz presents and answers, Done
/// summarises and restarts. The folder seam and the parse are faked; everything else is the
/// app's own wiring.
/// </summary>
public class PageTests : BunitContext
{
    private readonly FakeFolderAccess _folders = new();
    private readonly List<BgDecisionData> _problems = [];
    private Exception? _loadFailure;
    private readonly BrowserStoragePlan _plan;

    public PageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule(QuizPage.KeysModulePath).Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule(QuizPage.RowFitModulePath).Mode = JSRuntimeMode.Loose;
        var tailMenu = JSInterop.SetupModule(TailMenu.ModulePath);
        tailMenu.Mode = JSRuntimeMode.Loose;
        var decisionNotes = JSInterop.SetupModule(DecisionNotes.ModulePath);
        decisionNotes.Mode = JSRuntimeMode.Loose;
        _plan = BrowserStoragePlan.On(JSInterop);
        _plan.ExpectRead(BrowserStorageArea.Local, QuizSettings.StorageKey, BrowserStorageReadAnswer.Absent);
        _plan.ExpectRead(BrowserStorageArea.Local, NotesPlacementStore.StorageKey, BrowserStorageReadAnswer.Absent);

        Services.AddSingleton<IFolderAccess>(_folders);
        Services.AddSingleton(TimeProvider.System);
        Services.AddBrowserStorage();
        Services.AddScoped<BrowserStorageCondition>();
        Services.AddScoped<FilterStorageRefusalSink>();
        Services.AddFilterSurface<FilterStorageRefusalSink>();
        Services.AddScoped<PickedFolderDocumentStorage>();
        Services.AddScoped<QuizSettings>();
        Services.AddScoped<NotesPlacementStore>();
        Services.AddScoped<ProblemLoad>(_ => LoadAsync);
        Services.AddScoped(sp => new QuizApp(
            sp.GetRequiredService<IFolderAccess>(), sp.GetRequiredService<FilterSetup>(),
            sp.GetRequiredService<QuizSettings>(), sp.GetRequiredService<ProblemLoad>(),
            NullLogger<QuizApp>.Instance, TimeProvider.System));
    }

    private Task<LoadedProblems> LoadAsync(ImmutableArray<PickedFile> files, Action<LoadProgress>? progress, CancellationToken ct)
    {
        if (_loadFailure is { } failure) return Task.FromException<LoadedProblems>(failure);
        return Task.FromResult(new LoadedProblems([.. _problems], TestFixtures.WalkedReport(), forcedPlaysSetAside: 0));
    }

    private QuizApp App => Services.GetRequiredService<QuizApp>();
    private BunitNavigationManager Nav => Services.GetRequiredService<BunitNavigationManager>();

    private static Play Best() => TestFixtures.OpeningBest();
    private static Play Alt() => TestFixtures.OpeningAlternative();

    private static CheckerPlayDecision Problem(int n) =>
        TestFixtures.TwoChoiceDecision(Best(), Alt(), play2Loss: 0.05, away: n);

    /// <summary>The page's words, tags stripped and whitespace collapsed.</summary>
    private static string Text(IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> cut) =>
        Regex.Replace(Regex.Replace(cut.Markup, "<[^>]+>", " "), @"\s+", " ");

    private void ExpectHomeOverAFolder()
    {
        _plan.ExpectFilterPanelMount();
        _plan.ExpectFilterRestore(FilterRestoration.NothingStored);
    }

    /// <summary>A later Home over a folder mounts the panel again; the owner's restoration is already settled.</summary>
    private void ExpectAnotherHomeOverAFolder() => _plan.ExpectFilterPanelMount();

    private async Task<IRenderedComponent<HomePage>> PickedHomeAsync(params BgDecisionData[] problems)
    {
        _problems.AddRange(problems);
        _folders.NextPickOutcome = TestFixtures.Picked("Corpus", FolderWriteCapability.Enabled, [TestFixtures.ReadableXg()]);
        ExpectHomeOverAFolder();
        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.NotNull(App.CurrentSelection));
        return cut;
    }

    // ── Home ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Home_WithNoFolder_ShowsThePickAndHidesTheSetup()
    {
        var cut = Render<HomePage>();
        Assert.NotNull(cut.Find("#pickProblemFolder"));
        Assert.Empty(cut.FindComponents<FilterSurface>());
        var start = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Start Quiz");
        Assert.True(start.HasAttribute("disabled"));
        Assert.Contains("Pick a folder containing .xg / .xgp files to enable Start", Text(cut));
    }

    [Fact]
    public async Task Home_APick_ShowsTheFolder_TheFilters_TheCount_AndEnablesStart()
    {
        var cut = await PickedHomeAsync(Problem(1), Problem(2));
        cut.WaitForAssertion(() =>
        {
            var text = Text(cut);
            Assert.Contains("'Corpus' — 1 problem file", text);
            Assert.Contains("2 decisions match your filters", text);
            Assert.Contains("There are no repeated positions", text);
            Assert.Contains("By answer type", text);
            Assert.Single(cut.FindComponents<FilterSurface>());
            var start = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Start Quiz");
            Assert.False(start.HasAttribute("disabled"));
        });
    }

    [Fact]
    public async Task Home_ACancelledPick_SaysSo_AndKeepsTheFolderInHand()
    {
        var cut = await PickedHomeAsync(Problem(1));
        _folders.NextPickOutcome = FolderPickOutcome.CancelledOutcome;
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No folder was picked", Text(cut));
            Assert.Contains("'Corpus' — 1 problem file", Text(cut));
        });
    }

    [Fact]
    public async Task Home_APickThatFailsMidScan_LowersBusy_AndShowsTheError()
    {
        _folders.NextPickOutcome = TestFixtures.Picked("Corpus", FolderWriteCapability.Enabled, [TestFixtures.ReadableXg()]);
        _folders.PickException = new IOException("the drive went away");
        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Could not read the folder:", Text(cut));
            Assert.Contains("the drive went away", Text(cut));
            Assert.False(App.IsBusy);
            Assert.DoesNotContain("app-busy", cut.Find("div.container").ClassName);
        });
    }

    [Fact]
    public async Task Home_AFolderThatCannotBeReadAsProblems_ShowsTheError_AndTheRunTheLandingFinished_StaysFinished()
    {
        var app = await StartedAppAsync(Problem(1), Problem(2));
        _loadFailure = new InvalidDataException("not XG after all");
        _folders.NextPickOutcome = TestFixtures.Picked("Other", FolderWriteCapability.Enabled, [TestFixtures.ReadableXg()]);
        ExpectAnotherHomeOverAFolder();
        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Could not read the folder:", Text(cut));
            Assert.Contains("not XG after all", Text(cut));
            Assert.Contains("finished the quiz you had in progress", Text(cut));
        });
        Assert.Null(app.Setup);
        Assert.Null(app.Run);
        Assert.True(app.Finished!.EndedByNewSample);
    }

    [Fact]
    public async Task Home_AFolderWithNoProblemFiles_SaysSo()
    {
        _folders.NextPickOutcome = TestFixtures.Picked("Empty", FolderWriteCapability.Enabled, []);
        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Contains("No .xg / .xgp files found directly in that folder", Text(cut)));
        Assert.Null(App.Setup);
    }

    [Fact]
    public async Task Home_ATruncatedSample_IsReported_AndTheNoticeDismissesForThatSetup()
    {
        _problems.Add(Problem(1));
        _folders.NextPickOutcome = TestFixtures.Picked(
            "Big", FolderWriteCapability.Enabled, [TestFixtures.ReadableXg()], (".xg", 7));
        ExpectHomeOverAFolder();
        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Contains("7 more were not read", Text(cut)));
        Assert.Single(cut.FindAll("#truncationNotice"));
    }

    [Fact]
    public async Task Home_Start_NavigatesToTheQuiz_AndBackToQuizAppearsOnReturn()
    {
        var cut = await PickedHomeAsync(Problem(1), Problem(2));
        await cut.FindAll("button").Single(b => b.TextContent.Trim() == "Start Quiz").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.EndsWith("/quiz", Nav.Uri));
        Assert.NotNull(App.Run);

        ExpectAnotherHomeOverAFolder();
        var again = Render<HomePage>();
        Assert.Contains(again.FindAll("button"), b => b.TextContent.Trim() == "Back to quiz");
    }

    [Fact]
    public async Task Home_Clear_LetsTheFolderGo_AndDisablesStart()
    {
        var cut = await PickedHomeAsync(Problem(1));
        await cut.FindAll("button").Single(b => b.TextContent.Trim() == "Clear").ClickAsync(new());
        cut.WaitForAssertion(() =>
        {
            Assert.Null(App.Setup);
            Assert.Empty(cut.FindComponents<FilterSurface>());
            var start = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Start Quiz");
            Assert.True(start.HasAttribute("disabled"));
        });
    }

    // ── Quiz ────────────────────────────────────────────────────────────────────

    private async Task<QuizApp> StartedAppAsync(params BgDecisionData[] problems)
    {
        await PickedHomeAsync(problems);
        Assert.Equal(StartOutcome.Started, await App.StartAsync());
        return App;
    }

    [Fact]
    public void Quiz_WithNoRun_BouncesHome()
    {
        Render<QuizPage>();
        Assert.EndsWith("/", Nav.Uri);
    }

    [Fact]
    public async Task Quiz_PresentsTheProblem_ScoresTheAnswer_AndMovesOn()
    {
        // The board is maximized while answering (the default), so the score panel and
        // the status strip appear with the review.
        var app = await StartedAppAsync(Problem(1), Problem(2));
        var cut = Render<QuizPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".board-container")));
        Assert.Equal(1, app.ProblemNumber);

        await app.SubmitPlayAsync(Alt());
        cut.WaitForAssertion(() =>
        {
            var text = Text(cut);
            Assert.Contains("Not best", text);
            Assert.Contains("Problem 1 of 2", text);
        });
        await cut.FindAll(".action-row button").Single(b => b.TextContent.Trim() == "Continue").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Equal(2, app.ProblemNumber));
        await app.SubmitPlayAsync(Best());
        cut.WaitForAssertion(() =>
        {
            var text = Text(cut);
            Assert.Contains("Correct.", text);
            Assert.Contains("Problem 2 of 2", text);
        });
    }

    [Fact]
    public async Task Quiz_WhenTheRunFinishes_NavigatesToDone()
    {
        var app = await StartedAppAsync(Problem(1));
        var cut = Render<QuizPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".board-container")));
        await app.SubmitPlayAsync(Best());
        app.Next();
        cut.WaitForAssertion(() => Assert.EndsWith("/done", Nav.Uri));
        Assert.True(app.IsFinished);
    }

    // ── Done ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Done_ShowsTheSummary_AndRestartComposesANewRun()
    {
        var app = await StartedAppAsync(Problem(1), Problem(2));
        await app.SubmitPlayAsync(Best());
        app.EndQuiz();
        var cut = Render<DonePage>();
        Assert.Contains("Quiz complete", Text(cut));
        Assert.Contains("Total problems shown: 1", Text(cut));
        await cut.FindAll("button").Single(b => b.TextContent.Trim() == "Restart with same filters").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.EndsWith("/quiz", Nav.Uri));
        Assert.NotNull(app.Run);
        Assert.Equal(2, app.ProblemCount);
    }

    [Fact]
    public async Task Done_WithTheFolderCleared_DisablesRestart_AndSaysWhy()
    {
        var app = await StartedAppAsync(Problem(1));
        app.EndQuiz();
        Assert.True(await app.ClearFolderAsync());
        var cut = Render<DonePage>();
        var restart = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Restart with same filters");
        Assert.True(restart.HasAttribute("disabled"));
        Assert.Contains("Pick a folder on the setup page to restart", Text(cut));
    }

    [Fact]
    public void Done_WithNoRun_BouncesHome()
    {
        Render<DonePage>();
        Assert.EndsWith("/", Nav.Uri);
    }
}
