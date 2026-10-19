using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using BgDataTypes_Lib;
using BgFolderAccess_Razor;
using BgGame_Lib;
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
using StatsPage = BgQuiz_Blazor.Client.Components.Pages.Stats;
using SettingsPage = BgQuiz_Blazor.Client.Components.Pages.Settings;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The pages over the coordinator: Home sets up and starts, Quiz presents and answers, Stats
/// shows the progress, Done summarises and restarts, Settings holds the one mix control. The
/// folder seam and the parse are faked; everything else is the app's own wiring.
/// </summary>
public class PageTests : BunitContext
{
    private readonly FakeFolderAccess _folders = new();
    private readonly FakeQuizLiveMarker _marker = new();
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
        Services.AddScoped<QuizSettings>();
        Services.AddScoped(sp => new MixDraft(
            sp.GetRequiredService<BrowserStorage>(), NullLogger<MixDraft>.Instance, sp.GetRequiredService<BrowserStorageCondition>()));
        Services.AddScoped<NotesPlacementStore>();
        Services.AddScoped<ProblemLoad>(_ => LoadAsync);
        Services.AddSingleton<IQuizLiveMarker>(_marker);
        Services.AddScoped(sp => new QuizApp(
            sp.GetRequiredService<IFolderAccess>(), sp.GetRequiredService<FilterSetup>(),
            sp.GetRequiredService<QuizSettings>(), sp.GetRequiredService<MixDraft>(), sp.GetRequiredService<ProblemLoad>(),
            sp.GetRequiredService<IQuizLiveMarker>(), NullLogger<QuizApp>.Instance, TimeProvider.System));
    }

    private Task<LoadedProblems> LoadAsync(ImmutableArray<PickedFile> files, Action<LoadProgress>? progress, CancellationToken ct)
    {
        if (_loadFailure is { } failure) return Task.FromException<LoadedProblems>(failure);
        return Task.FromResult(new LoadedProblems([.. _problems], TestFixtures.WalkedReport(), forcedPlaysSetAside: 0));
    }

    private QuizApp App => Services.GetRequiredService<QuizApp>();
    private QuizSettings Settings => Services.GetRequiredService<QuizSettings>();
    private BunitNavigationManager Nav => Services.GetRequiredService<BunitNavigationManager>();

    private static Play Best() => TestFixtures.OpeningBest();
    private static Play Alt() => TestFixtures.OpeningAlternative();

    private static CheckerPlayDecision Problem(int n) =>
        TestFixtures.TwoChoiceDecision(Best(), Alt(), play2Loss: 0.05, away: n);

    /// <summary>A problem with an id of its own: an active mix contributes each decision id once.</summary>
    private static CheckerPlayDecision DistinctProblem(int n) =>
        TestFixtures.TwoChoiceDecision(
            Best(), Alt(), play2Loss: 0.05, away: n, location: TestFixtures.SourceLocation.OnePosition($"p{n}.xgp"));

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

    /// <summary>The folder the next pick lands.</summary>
    private FakeFolderContext NextFolder => TestFixtures.FolderOf(_folders.NextPickOutcome);

    /// <summary>A stats document with one record, so the folder "has stats".</summary>
    private static string StatsWithContent() =>
        JsonSerializer.Serialize(
            ProblemStatsDocument.Empty.Plus(
                TestFixtures.Scored(Problem(9), Best(), PlayRanking.Equity), TimeProvider.System),
            QuizStatsFile.DocumentTypeInfo);

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

    /// <summary>Turn the mix setting on, declaring its write; the folder's stats decide the rest.</summary>
    private async Task TurnTheMixSettingOnAsync()
    {
        await Settings.EnsureHydratedAsync();
        _plan.ExpectWrite(BrowserStorageArea.Local, QuizSettings.StorageKey, SettingsPayload.Of(weightQuizzesByStats: true), BrowserStorageWriteAnswer.Succeeded);
        await Settings.SetWeightQuizzesByStatsAsync(true);
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
    public async Task Home_APick_ShowsTheFolder_TheFilters_TheCount_TheStatsLine_AndEnablesStart()
    {
        var cut = await PickedHomeAsync(Problem(1), Problem(2));
        cut.WaitForAssertion(() =>
        {
            var text = Text(cut);
            Assert.Contains("'Corpus' — 1 problem file", text);
            Assert.Contains("2 decisions match your filters", text);
            Assert.Contains("There are no repeated positions", text);
            Assert.Contains("By answer type", text);
            Assert.Contains("stats will be saved to bgquiz-stats.json", text);
            Assert.Single(cut.FindComponents<FilterSurface>());
            Assert.Empty(cut.FindAll("#mixAddRow"));   // the setting is off
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
    public async Task Home_APickThatFailsMidScan_LowersBusy_ShowsTheError_AndNamesTheFolderKept()
    {
        // The pick failed after the browser granted 'Other', before a sample landed: 'Corpus' is
        // still in hand and the error says so (halheinrich/backgammon#424).
        var cut = await PickedHomeAsync(Problem(1));
        _folders.NextPickOutcome = TestFixtures.Picked("Other", FolderWriteCapability.Enabled, [TestFixtures.ReadableXg()]);
        _folders.OnScanning = () => throw new IOException("the drive went away");
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        cut.WaitForAssertion(() =>
        {
            var text = Text(cut);
            Assert.Contains("Could not read the folder:", text);
            Assert.Contains("the drive went away", text);
            Assert.Contains("Current folder is still 'Corpus'", text);
            Assert.Contains("'Corpus' — 1 problem file", text);
            Assert.False(App.IsBusy);
            Assert.DoesNotContain("app-busy", cut.Find("div.container").ClassName);
        });
        Assert.Equal("Corpus", App.Setup!.FolderName);
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
        Assert.DoesNotContain("Current folder is still", Text(cut));   // there was none to keep
        Assert.Null(App.Setup);
    }

    [Fact]
    public async Task Home_AnEmptyPick_WithAFolderInHand_NamesTheFolderKept_AndItsNoticesStepAsideMeanwhile()
    {
        // Hal, 2026-10-17: beside the empty-pick notice, the kept folder's own notices read as
        // the empty folder's. They step aside while that notice stands and return when it goes;
        // nothing is dismissed for the user.
        var (cut, _) = await PickedTruncatedHomeAsync(Problem(1));
        Assert.Single(cut.FindAll("#truncationNotice"));
        Assert.Single(cut.FindAll("#statsCapabilityNotice"));
        _folders.NextPickOutcome = TestFixtures.Picked("Empty", FolderWriteCapability.Enabled, []);

        await cut.Find("#pickProblemFolder").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Contains("Current folder is still 'Big'", Text(cut)));
        Assert.Equal("Big", App.Setup!.FolderName);
        Assert.Empty(cut.FindAll("#truncationNotice"));
        Assert.Empty(cut.FindAll("#statsCapabilityNotice"));

        await cut.Find("#emptyFolderNotice .btn-close").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#emptyFolderNotice")));
        Assert.Single(cut.FindAll("#truncationNotice"));
        Assert.Single(cut.FindAll("#statsCapabilityNotice"));
    }

    [Fact]
    public async Task Home_AFailedPick_WithAFolderInHand_NamesTheFolderKept_AndItsNoticesStepAsideMeanwhile()
    {
        // The failed-pick error stands beside the kept folder as the empty-pick notice does, and
        // the same misreading follows: the folder's own notices step aside while it stands and
        // return when it is dismissed, nothing dismissed for the user (halheinrich/backgammon#424).
        var (cut, _) = await PickedTruncatedHomeAsync(Problem(1));
        Assert.Single(cut.FindAll("#truncationNotice"));
        Assert.Single(cut.FindAll("#statsCapabilityNotice"));
        _folders.NextPickOutcome = TestFixtures.Picked("Other", FolderWriteCapability.Enabled, [TestFixtures.ReadableXg()]);
        _folders.OnScanning = () => throw new IOException("the drive went away");

        await cut.Find("#pickProblemFolder").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Contains("Current folder is still 'Big'", Text(cut)));
        Assert.Contains("the drive went away", Text(cut));
        Assert.Equal("Big", App.Setup!.FolderName);
        Assert.Empty(cut.FindAll("#truncationNotice"));
        Assert.Empty(cut.FindAll("#statsCapabilityNotice"));

        await cut.Find("#pickErrorNotice .btn-close").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#pickErrorNotice")));
        Assert.Single(cut.FindAll("#truncationNotice"));
        Assert.Single(cut.FindAll("#statsCapabilityNotice"));
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
    public async Task Home_SaysWhichStatsRungTheFolderLandedOn()
    {
        // No write grant: the quiz runs, the record does not accumulate, and no promise is made.
        _problems.Add(Problem(1));
        _folders.NextPickOutcome = TestFixtures.Picked("Corpus", FolderWriteCapability.PermissionDenied, [TestFixtures.ReadableXg()]);
        ExpectHomeOverAFolder();
        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("which problems give you difficulty", Text(cut));
            Assert.DoesNotContain("stats will be saved", Text(cut));
        });
    }

    [Fact]
    public async Task Home_ForecastsAnUnreadableStatsFile_InsteadOfPromisingToSave()
    {
        _problems.Add(Problem(1));
        _folders.NextPickOutcome = TestFixtures.Picked("Corpus", FolderWriteCapability.Enabled, [TestFixtures.ReadableXg()]);
        NextFolder.StatsJson = "not json at all";
        ExpectHomeOverAFolder();
        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("#statsUnreadableForecastNotice"));
            Assert.Contains("can't be read, so the quiz will run but record nothing", Text(cut));
            Assert.DoesNotContain("stats will be saved", Text(cut));
        });
    }

    [Fact]
    public async Task Home_ForecastsARetiredStatsFile_ByTheNameItWillBeSetAsideUnder()
    {
        _problems.Add(Problem(1));
        _folders.NextPickOutcome = TestFixtures.Picked("Corpus", FolderWriteCapability.Enabled, [TestFixtures.ReadableXg()]);
        NextFolder.StatsJson = RetiredStatsFixture.V1Json;
        ExpectHomeOverAFolder();
        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("will be set aside as bgquiz-stats.v1.json", Text(cut));
            Assert.Contains("stats will be saved", Text(cut));   // the promise still holds: the file is readable
        });
    }

    [Fact]
    public async Task Home_OffersTheMix_OnlyWithTheSettingOn_AndStatsInTheFolder()
    {
        _problems.Add(Problem(1));
        _folders.NextPickOutcome = TestFixtures.Picked("Corpus", FolderWriteCapability.Enabled, [TestFixtures.ReadableXg()]);
        NextFolder.StatsJson = StatsWithContent();
        ExpectHomeOverAFolder();
        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.NotNull(App.CurrentSelection));
        Assert.True(App.Setup!.Stats.HasStats);
        Assert.Empty(cut.FindAll("#mixAddRow"));   // the setting is off

        _plan.ExpectRead(BrowserStorageArea.Local, MixDraft.StorageKey, BrowserStorageReadAnswer.Absent);
        await TurnTheMixSettingOnAsync();
        cut.Render();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("#mixAddRow")));
        Assert.Contains("Weighted mix", Text(cut));
    }

    [Fact]
    public async Task Home_WithTheSettingOn_ButNoStatsInTheFolder_OffersNoMix()
    {
        await TurnTheMixSettingOnAsync();
        var cut = await PickedHomeAsync(Problem(1));

        Assert.Empty(cut.FindAll("#mixAddRow"));
        Assert.False(App.MixInEffect);
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

    // ── Resample, and the reload notice ─────────────────────────────────────────

    private async Task<(IRenderedComponent<HomePage> Home, FakeFolderContext Folder)> PickedTruncatedHomeAsync(params BgDecisionData[] problems)
    {
        _problems.AddRange(problems);
        _folders.NextPickOutcome = TestFixtures.Picked(
            "Big", FolderWriteCapability.Enabled, [TestFixtures.ReadableXg()], (".xg", 7));
        var folder = NextFolder;
        ExpectHomeOverAFolder();
        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Contains("7 more were not read", Text(cut)));
        return (cut, folder);
    }

    private static FolderSample ANewDraw() => TestFixtures.Sample([TestFixtures.ReadableXg("again.xg")], (".xg", 2));

    [Fact]
    public async Task Home_OffersResample_WhileAnotherDrawCouldDiffer_AndItReplacesTheSample()
    {
        var (cut, folder) = await PickedTruncatedHomeAsync(Problem(1));
        folder.NextResample = ANewDraw();

        await cut.Find("#resampleProblemFolder").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Contains("2 more were not read", Text(cut)));
        Assert.Same(folder, App.Setup!.Folder);
        Assert.Same(folder.NextResample, App.Setup.Sample);
    }

    [Fact]
    public async Task Home_OffersNoResample_ForAFolderHeldWhole()
    {
        var cut = await PickedHomeAsync(Problem(1));
        Assert.Empty(cut.FindAll("#resampleProblemFolder"));
    }

    [Fact]
    public async Task Home_AnEmptyResample_SaysSo_AndKeepsTheFilesInUse()
    {
        var (cut, folder) = await PickedTruncatedHomeAsync(Problem(1));
        var setup = App.Setup;
        folder.NextResample = TestFixtures.Sample([]);

        await cut.Find("#resampleProblemFolder").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Contains("Resample found no .xg / .xgp files", Text(cut)));
        Assert.Contains("7 more were not read", Text(cut));
        Assert.Same(setup, App.Setup);
    }

    [Fact]
    public async Task Home_AFailedResample_ShowsTheError_AndTheFolderInHandKeepsItsNotices()
    {
        // A failed draw is about the folder in hand, not another, so the error names no folder
        // kept and the folder's own notices stand, as beside an empty draw: those two are a
        // failed pick's (halheinrich/backgammon#424).
        var (cut, folder) = await PickedTruncatedHomeAsync(Problem(1));
        var setup = App.Setup;
        folder.ResampleException = new IOException("the drive went away");

        await cut.Find("#resampleProblemFolder").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Contains("the drive went away", Text(cut)));
        Assert.Contains("Could not read the folder:", Text(cut));
        Assert.DoesNotContain("Current folder is still", Text(cut));
        Assert.Single(cut.FindAll("#truncationNotice"));
        Assert.Single(cut.FindAll("#statsCapabilityNotice"));
        Assert.Same(setup, App.Setup);
    }

    [Fact]
    public async Task Done_OffersRestartWithANewSample_WhileResampleIsOffered_AndItRunsTheQuizOverTheNewDraw()
    {
        var (home, folder) = await PickedTruncatedHomeAsync(Problem(1));
        home.WaitForAssertion(() => Assert.NotNull(App.CurrentSelection));
        Assert.Equal(StartOutcome.Started, await App.StartAsync());
        App.EndQuiz();
        folder.NextResample = ANewDraw();

        var cut = Render<DonePage>();
        await cut.Find("#restartWithNewSample").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.EndsWith("/quiz", Nav.Uri));
        Assert.NotNull(App.Run);
        Assert.Same(folder.NextResample, App.Setup!.Sample);
    }

    [Fact]
    public async Task Done_OffersNoRestartWithANewSample_ForAFolderHeldWhole()
    {
        var app = await StartedAppAsync(Problem(1));
        app.EndQuiz();
        var cut = Render<DonePage>();
        Assert.Empty(cut.FindAll("#restartWithNewSample"));
    }

    [Fact]
    public async Task Done_WhenTheNewSampleLandsButNothingCanBePresented_SaysTheSampleIsHeld()
    {
        var (home, folder) = await PickedTruncatedHomeAsync(Problem(1));
        home.WaitForAssertion(() => Assert.NotNull(App.CurrentSelection));
        Assert.Equal(StartOutcome.Started, await App.StartAsync());
        App.EndQuiz();
        folder.NextResample = ANewDraw();
        _problems.Clear();   // the new draw parses to nothing to present

        var cut = Render<DonePage>();
        await cut.Find("#restartWithNewSample").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Contains("The new sample is held", Text(cut)));
        Assert.Contains("Quiz complete", Text(cut));
        Assert.Same(folder.NextResample, App.Setup!.Sample);
        Assert.Null(App.Run);
    }

    [Fact]
    public async Task Done_WhenTheNewSampleLandsButTheMixNeedsStats_SaysTheSampleIsHeld_AndOffersRestartWithoutMix()
    {
        // The refusal over a new sample says the sample is held (halheinrich/backgammon#417;
        // ChatGPT's review 6229447651). The backstop case: the landing's probe finds stats, the
        // restart's bind then cannot read the file.
        _problems.Add(Problem(1));
        _folders.NextPickOutcome = TestFixtures.Picked(
            "Big", FolderWriteCapability.Enabled, [TestFixtures.ReadableXg()], (".xg", 7));
        var folder = NextFolder;
        folder.StatsJson = StatsWithContent();
        ExpectHomeOverAFolder();
        var home = Render<HomePage>();
        await home.Find("#pickProblemFolder").ClickAsync(new());
        home.WaitForAssertion(() => Assert.NotNull(App.CurrentSelection));
        await TurnTheMixSettingOnAsync();
        var draft = Services.GetRequiredService<MixDraft>();
        // The first edit re-renders Home with the mix in effect, so its mix panel mounts and
        // hydrates the draft before the edit writes through: the read comes first, then the write.
        _plan.ExpectRead(BrowserStorageArea.Local, MixDraft.StorageKey, BrowserStorageReadAnswer.Absent);
        _plan.ExpectWrite(BrowserStorageArea.Local, MixDraft.StorageKey,
            new QuizMix([new QuizMixEntry(QuizCategory.NeverSeen, 100)]).ToJson(), BrowserStorageWriteAnswer.Succeeded);
        await draft.AddRowAsync();
        Assert.Equal(StartOutcome.Started, await App.StartAsync());
        App.EndQuiz();
        folder.NextResample = ANewDraw();
        App.Changed += () =>
        {
            if (App.Setup is { } landed && ReferenceEquals(landed.Sample, folder.NextResample))
                folder.StatsJson = "not json at all";
        };

        var cut = Render<DonePage>();
        await cut.Find("#restartWithNewSample").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Contains("The new sample is held", Text(cut)));
        Assert.NotNull(cut.Find("#restartWithoutMix"));
        Assert.Contains("Quiz complete", Text(cut));
        Assert.Same(folder.NextResample, App.Setup!.Sample);
    }

    [Fact]
    public void Home_AfterAReloadMidQuiz_SaysTheQuizWasReset_OnceOnly()
    {
        _marker.IsLive = true;   // the mark a reload left behind; no run at all is in hand

        var cut = Render<HomePage>();

        cut.WaitForAssertion(() => Assert.Contains("reset by the page reload", Text(cut)));
        Assert.False(_marker.IsLive);
        var again = Render<HomePage>();
        Assert.DoesNotContain("reset by the page reload", Text(again));
    }

    [Fact]
    public async Task Home_MidQuiz_ReadsTheLiveMarkAsNavigation_NotAReload()
    {
        await StartedAppAsync(Problem(1));
        Assert.True(_marker.IsLive);
        ExpectAnotherHomeOverAFolder();

        var cut = Render<HomePage>();

        cut.WaitForAssertion(() => Assert.Contains("Back to quiz", Text(cut)));
        Assert.DoesNotContain("reset by the page reload", Text(cut));
        Assert.True(_marker.IsLive);
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
    public async Task Quiz_ShowStats_ReachesTheStatsPage_WhichShowsTheProgress()
    {
        var app = await StartedAppAsync(Problem(1), Problem(2));
        await app.SubmitPlayAsync(Best());
        var cut = Render<QuizPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".board-container")));

        await cut.Find("button[aria-label='Show stats']").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.EndsWith("/stats", Nav.Uri));
        var stats = Render<StatsPage>();
        var text = Text(stats);
        Assert.Contains("Quiz progress", text);
        Assert.Contains("Submitted: 1", text);
        Assert.Contains("Back to quiz", text);
        Assert.NotNull(app.Run);   // read-only: the run is as it was
    }

    [Fact]
    public void Stats_WithNoRun_BouncesHome()
    {
        Render<StatsPage>();
        Assert.EndsWith("/", Nav.Uri);
    }

    [Fact]
    public async Task Quiz_ReportsAnUnreadableStatsFile_AndDoneRepeatsIt()
    {
        _problems.Add(Problem(1));
        _folders.NextPickOutcome = TestFixtures.Picked("Corpus", FolderWriteCapability.Enabled, [TestFixtures.ReadableXg()]);
        NextFolder.StatsJson = "not json at all";
        ExpectHomeOverAFolder();
        var home = Render<HomePage>();
        await home.Find("#pickProblemFolder").ClickAsync(new());
        home.WaitForAssertion(() => Assert.NotNull(App.CurrentSelection));
        Assert.Equal(StartOutcome.Started, await App.StartAsync());

        var quiz = Render<QuizPage>();
        quiz.WaitForAssertion(() => Assert.Contains("couldn't be read, so this quiz won't record stats", Text(quiz)));

        App.EndQuiz();
        var done = Render<DonePage>();
        Assert.Contains("couldn't be read, so this quiz didn't record stats", Text(done));
        Assert.DoesNotContain("Nothing here needs saving", Text(done));
        Assert.Empty(NextFolder.Writes);
    }

    [Fact]
    public async Task Quiz_SaysWhatTheMixDrew_UntilTheFirstAnswer()
    {
        _problems.AddRange([DistinctProblem(1), DistinctProblem(2)]);
        _folders.NextPickOutcome = TestFixtures.Picked("Corpus", FolderWriteCapability.Enabled, [TestFixtures.ReadableXg()]);
        NextFolder.StatsJson = StatsWithContent();
        ExpectHomeOverAFolder();
        var home = Render<HomePage>();
        await home.Find("#pickProblemFolder").ClickAsync(new());
        home.WaitForAssertion(() => Assert.NotNull(App.CurrentSelection));
        await TurnTheMixSettingOnAsync();
        var draft = Services.GetRequiredService<MixDraft>();
        _plan.ExpectWrite(BrowserStorageArea.Local, MixDraft.StorageKey,
            new QuizMix([new QuizMixEntry(QuizCategory.NeverSeen, 100)]).ToJson(), BrowserStorageWriteAnswer.Succeeded);
        await draft.AddRowAsync();
        Assert.Equal(StartOutcome.Started, await App.StartAsync());

        var quiz = Render<QuizPage>();
        quiz.WaitForAssertion(() => Assert.Contains("Your quiz has 2 problems: 2 Never seen.", Text(quiz)));

        await App.SubmitPlayAsync(Best());
        quiz.WaitForAssertion(() => Assert.DoesNotContain("Your quiz has", Text(quiz)));
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
        Assert.Contains("Nothing here needs saving", Text(cut));
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
    public async Task Done_WithTheMixGoneSinceTheRun_SaysRestartRunsUnweighted()
    {
        _problems.Add(Problem(1));
        _folders.NextPickOutcome = TestFixtures.Picked("Corpus", FolderWriteCapability.Enabled, [TestFixtures.ReadableXg()]);
        NextFolder.StatsJson = StatsWithContent();
        ExpectHomeOverAFolder();
        var home = Render<HomePage>();
        await home.Find("#pickProblemFolder").ClickAsync(new());
        home.WaitForAssertion(() => Assert.NotNull(App.CurrentSelection));
        await TurnTheMixSettingOnAsync();
        var draft = Services.GetRequiredService<MixDraft>();
        _plan.ExpectRead(BrowserStorageArea.Local, MixDraft.StorageKey, BrowserStorageReadAnswer.Absent);   // the panel mounts on the first edit
        _plan.ExpectWrite(BrowserStorageArea.Local, MixDraft.StorageKey,
            new QuizMix([new QuizMixEntry(QuizCategory.NeverSeen, 100)]).ToJson(), BrowserStorageWriteAnswer.Succeeded);
        await draft.AddRowAsync();
        Assert.Equal(StartOutcome.Started, await App.StartAsync());
        App.EndQuiz();
        _plan.ExpectWrite(BrowserStorageArea.Local, QuizSettings.StorageKey, SettingsPayload.Of(weightQuizzesByStats: false), BrowserStorageWriteAnswer.Succeeded);
        await Settings.SetWeightQuizzesByStatsAsync(false);

        var cut = Render<DonePage>();

        Assert.Contains("Restart runs unweighted", Text(cut));
    }

    [Fact]
    public void Done_WithNoRun_BouncesHome()
    {
        Render<DonePage>();
        Assert.EndsWith("/", Nav.Uri);
    }

    // ── Settings ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Settings_OffersTheMixToggle_WhichRecordsTheChoice()
    {
        var cut = Render<SettingsPage>();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#settingsWeightQuizzes")));
        Assert.Contains("Draw quizzes from your weighted mix", Text(cut));
        Assert.False(cut.Find("#settingsWeightQuizzes").HasAttribute("checked"));

        _plan.ExpectWrite(BrowserStorageArea.Local, QuizSettings.StorageKey, SettingsPayload.Of(weightQuizzesByStats: true), BrowserStorageWriteAnswer.Succeeded);
        await cut.Find("#settingsWeightQuizzes").ChangeAsync(new() { Value = true });

        Assert.True(Settings.WeightQuizzesByStats);
    }
}
