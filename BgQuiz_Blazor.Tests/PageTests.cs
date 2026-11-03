using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using BackgammonDiagram_Lib;
using BgDataTypes_Lib;
using BgDiag_Razor.Components;
using BgFolderAccess_Razor;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Components;
using BgQuiz_Blazor.Client.Quiz;
using BgQuiz_Blazor.E2eTests;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using Bunit.TestDoubles;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;
using XgFilter_Razor.Components;
using XgFilter_Razor.TestSupport;
using HomePage = BgQuiz_Blazor.Client.Components.Pages.Home;
using QuizPage = BgQuiz_Blazor.Client.Components.Pages.Quiz;
using DonePage = BgQuiz_Blazor.Client.Components.Pages.Done;
using StatsPage = BgQuiz_Blazor.Client.Components.Pages.Stats;
using SettingsPage = BgQuiz_Blazor.Client.Components.Pages.Settings;
using HelpPage = BgQuiz_Blazor.Client.Components.Pages.Help;

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

    /// <summary>
    /// Whether the pick's files are parsed for real, through <see cref="ProblemLoader"/>, so what
    /// Home reads off the load — its problems and its walk's report — comes from a real walk of
    /// the bytes picked. Otherwise every pick parses to <see cref="_problems"/> over an empty walk.
    /// </summary>
    private bool _parseThePick;
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
        _plan.ExpectRead(BrowserStorageArea.Local, NotesPreferencesStore.StorageKey, BrowserStorageReadAnswer.Absent);

        Services.AddSingleton<IFolderAccess>(_folders);
        Services.AddSingleton(TimeProvider.System);
        Services.AddFilterSurface();
        Services.AddScoped<QuizSettings>();
        Services.AddScoped<MixDraft>();
        Services.AddScoped<NotesPreferencesStore>();
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
        if (_parseThePick) return ProblemLoader.LoadAsync(files, NullLoggerFactory.Instance, TimeProvider.System, progress, ct);
        return Task.FromResult(new LoadedProblems([.. _problems], TestFixtures.WalkedReport()));
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

    // ── Home: what the walk left out (halheinrich/backgammon#514) ───────────────
    //
    // Over a real walk of the picked bytes: the load is unfiltered, so an empty load says the
    // files hold no quiz decision and a zero beside decisions is the filter's; every file
    // rejected keeps its own box; and what was rejected or skipped beside decisions is listed in
    // the record, whose headline tells a file not read from a game or decision skipped.

    /// <summary>Home over a pick of <paramref name="files"/>, parsed for real, once its count is in.</summary>
    private async Task<IRenderedComponent<HomePage>> HomeOverAWalkAsync(params PickedFile[] files)
    {
        _plan.ExpectFilterRestore(FilterRestoration.NothingStored);
        var cut = await HomeOverAWalkLandedAsync(files);
        cut.WaitForAssertion(() => Assert.NotNull(App.CurrentSelection));
        return cut;
    }

    /// <summary>Home over a pick of <paramref name="files"/>, parsed for real, once the pick has landed — for a filter not yet applied, which has no count.</summary>
    private async Task<IRenderedComponent<HomePage>> HomeOverAWalkLandedAsync(params PickedFile[] files)
    {
        _parseThePick = true;
        _folders.NextPickOutcome = TestFixtures.Picked("Corpus", FolderWriteCapability.Enabled, files);
        _plan.ExpectFilterPanelMount();
        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.True(App.Setup is not null && !App.IsBusy));
        return cut;
    }

    private static string Collapsed(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    private static AngleSharp.Dom.IElement StartButton(IRenderedComponent<HomePage> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == ExpectedText.StartQuizButton);

    /// <summary>The non-dismissible, politely announced warning <paramref name="id"/> names — the shape every box here has.</summary>
    private static NoticeBox Box(IRenderedComponent<HomePage> cut, string id) =>
        NoticeBox.ById(cut, id).ShouldBe(NoticeKind.Warning, NoticeAnnouncement.Polite, dismissible: false, "id", "class");

    /// <summary>The list items of <paramref name="box"/>, each whitespace-collapsed.</summary>
    private static string[] Items(NoticeBox box) => [.. box.Content.QuerySelectorAll("li").Select(li => Collapsed(li.TextContent))];

    private static PickedFile RefusedGame2Of3(string name = "refused.xg") =>
        TestFixtures.PickedXg(XgFileEdits.WithRefusedGames(TestFixtures.OpeningMatch(games: 3), 2), name);

    [Fact]
    public async Task Home_AnEmptyLoadWithOmissions_SaysNoQuizDecisions_ListsThem_AndStartIsDark()
    {
        var cut = await HomeOverAWalkAsync(TestFixtures.PickedXg(
            XgFileEdits.WithRefusedGames(TestFixtures.OpeningMatch(games: 2), 1, 2), "refused.xg"));

        var box = Box(cut, "noQuizDecisionsNotice");
        Assert.StartsWith(ExpectedText.NoQuizDecisionsAvailable, Collapsed(box.Content.TextContent));
        var omissions = App.Setup!.Loaded.Report.Omissions;
        Assert.Equal(omissions.Select(omission => $"refused.xg — {Collapsed(omission.ToString())}"), Items(box));
        var text = Text(cut);
        Assert.DoesNotContain("decisions match", text);
        Assert.DoesNotContain("Counting matching", text);
        Assert.Empty(cut.FindAll("#noMatchNotice"));
        Assert.Empty(cut.FindAll("#rejectedFilesNotice"));   // listed once, in the gate reason
        Assert.Empty(cut.FindAll("#allRejectedNotice"));
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains(ExpectedText.NoQuizDecisionsHint, text);
    }

    [Fact]
    public async Task Home_OnlyForcedOrIllegalPlays_SaysNoQuizDecisions_WithNoList_AndStartIsDark()
    {
        // Neither is a problem (Hal's rulings): the box is the neutral gate reason alone, with no
        // record, and not "0 decisions match your filters", which blamed a filter.
        var cut = await HomeOverAWalkAsync(
            TestFixtures.PickedXg(TestFixtures.Match(game => game.WithForcedPlay().WithDance()), "forced.xg"),
            TestFixtures.PickedXg(TestFixtures.Match(game => game.IllegalPlay(XgPlayer.Player1, new DiceRoll(6, 5))), "illegal.xg"));

        var box = Box(cut, "noQuizDecisionsNotice");
        Assert.Equal(ExpectedText.NoQuizDecisionsAvailable, Collapsed(box.Content.TextContent));
        Assert.Empty(box.Content.QuerySelectorAll("ul"));
        Assert.Empty(cut.FindAll("#rejectedFilesNotice"));
        Assert.Empty(cut.FindAll("#noMatchNotice"));
        Assert.DoesNotContain("decisions match", Text(cut));
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains(ExpectedText.NoQuizDecisionsHint, Text(cut));
    }

    [Fact]
    public async Task Home_AnEmptyLoad_IsDark_WhileItsCountIsPending()
    {
        // The gate reads the load, not the count: a ranking change leaves the count unknown and
        // nothing in flight, which a gate reading only the count would let through.
        var cut = await HomeOverAWalkAsync(TestFixtures.PickedXg(TestFixtures.Match(game => game.WithForcedPlay()), "forced.xg"));
        await Settings.EnsureHydratedAsync();
        _plan.ExpectWrite(BrowserStorageArea.Local, QuizSettings.StorageKey,
            SettingsPayload.Of(sortAnalysisByDepthFirst: true), BrowserStorageWriteAnswer.Succeeded);
        await Settings.SetSortAnalysisByDepthFirstAsync(true);
        cut.Render();

        Assert.Null(App.CurrentSelection);   // the premise: the count is pending
        Assert.False(App.IsBusy);            // and nothing holds Start dark but the gate
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains(ExpectedText.NoQuizDecisionsHint, Text(cut));
    }

    [Fact]
    public async Task Home_UsableDecisionsBesideOmissions_ShowTheRecord_AndStartIsAvailable()
    {
        var cut = await HomeOverAWalkAsync(RefusedGame2Of3());

        Assert.Contains(ExpectedText.DecisionsMatchYourFilters(4), Text(cut));
        var record = Box(cut, "rejectedFilesNotice");
        Assert.Equal(ExpectedText.OneGameSkipped, Collapsed(record.Content.Children[0].TextContent));
        Assert.Single(Items(record));
        Assert.Empty(cut.FindAll("#noQuizDecisionsNotice"));
        Assert.False(StartButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_AFilteredZeroBesideOmissions_SaysTheFilterMatchesNothing_BesideTheRecord()
    {
        // The load is unfiltered and holds decisions, so the zero is the filter's: the filter's
        // box stands, with the record beside it, and Start is dark for the filter.
        var noOne = new FilterConfig { Players = ["Nobody"] };
        _plan.ExpectFilterRestore(noOne);
        var cut = await HomeOverAWalkLandedAsync(RefusedGame2Of3());
        _plan.ExpectFilterCommit(noOne, BrowserStorageWriteAnswer.Succeeded);
        await cut.FindAll("button").Single(b => b.TextContent.Trim() == ExpectedText.ApplyFilterButton).ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Equal(0, App.CurrentSelection?.AnswerTypes.Total));
        Assert.Contains(ExpectedText.DecisionsMatchYourFilters(0), Collapsed(Box(cut, "noMatchNotice").Content.TextContent));
        Assert.Equal(ExpectedText.OneGameSkipped, Collapsed(Box(cut, "rejectedFilesNotice").Content.Children[0].TextContent));
        Assert.Empty(cut.FindAll("#noQuizDecisionsNotice"));
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains("No problems match the filters", Text(cut));
    }

    [Fact]
    public async Task Home_EveryFileRejected_ShowsTheAllRejectedBox_AsBefore()
    {
        var cut = await HomeOverAWalkAsync(TestFixtures.DamagedXg("a.xg"), TestFixtures.DamagedXg("b.xg"));

        var box = Box(cut, "allRejectedNotice");
        Assert.StartsWith(
            "None of the selected problem files could be read, so there are no decisions to count:",
            Collapsed(box.Content.TextContent));
        Assert.Equal(["a.xg", "b.xg"], Items(box).Select(item => item.Split(' ')[0]));
        Assert.Empty(cut.FindAll("#noQuizDecisionsNotice"));
        Assert.Empty(cut.FindAll("#rejectedFilesNotice"));
        Assert.Empty(cut.FindAll("#noMatchNotice"));
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains("No selected problem file could be read — pick a folder with readable problem files to enable Start.", Text(cut));
    }

    /// <summary>The picks the headline cases walk, by name: rejected files, omitted games, omitted decisions.</summary>
    private static PickedFile[] HeadlineCase(string which)
    {
        var match = TestFixtures.OpeningMatch(games: 4);
        PickedFile Edited(Func<ConvertXgToJson_Lib.Models.XgFile, ConvertXgToJson_Lib.Models.XgFile> edit) =>
            TestFixtures.PickedXg(edit(match), "match.xg");
        return which switch
        {
            "1 file" => [TestFixtures.PickedXg(match, "match.xg"), TestFixtures.DamagedXg()],
            "1 game" => [Edited(m => XgFileEdits.WithRefusedGames(m, 2))],
            "2 games" => [Edited(m => XgFileEdits.WithRefusedGames(m, 2, 3))],
            "1 decision" => [Edited(m => XgFileEdits.WithCorruptCandidate(m, 1, 1))],
            "2 decisions" => [Edited(m => XgFileEdits.WithCorruptCandidate(XgFileEdits.WithCorruptCandidate(m, 1, 1), 2, 1))],
            "1 game, 1 decision" => [Edited(m => XgFileEdits.WithCorruptCandidate(XgFileEdits.WithRefusedGames(m, 3), 1, 1))],
            "2 games, 2 decisions" => [Edited(m =>
                XgFileEdits.WithCorruptCandidate(XgFileEdits.WithCorruptCandidate(XgFileEdits.WithRefusedGames(m, 3, 4), 1, 1), 2, 1))],
            "1 file, 1 game" => [Edited(m => XgFileEdits.WithRefusedGames(m, 2)), TestFixtures.DamagedXg()],
            "2 files, 2 games, 1 decision" => [
                Edited(m => XgFileEdits.WithCorruptCandidate(XgFileEdits.WithRefusedGames(m, 3, 4), 1, 1)),
                TestFixtures.DamagedXg("damaged-1.xg"), TestFixtures.DamagedXg("damaged-2.xg")],
            _ => throw new ArgumentOutOfRangeException(nameof(which), which, null),
        };
    }

    [Theory]
    [InlineData("1 file", "1 of 2 files could not be read, so this selection is incomplete:", "1")]
    [InlineData("1 game", "1 game was skipped, so this selection is incomplete:", "1")]
    [InlineData("2 games", "2 games were skipped, so this selection is incomplete:", "2")]
    [InlineData("1 decision", "1 decision was skipped, so this selection is incomplete:", "1")]
    [InlineData("2 decisions", "2 decisions were skipped, so this selection is incomplete:", "2")]
    [InlineData("1 game, 1 decision", "1 game and 1 decision were skipped, so this selection is incomplete:", "1 1")]
    [InlineData("2 games, 2 decisions", "2 games and 2 decisions were skipped, so this selection is incomplete:", "2 2")]
    [InlineData("1 file, 1 game", "1 of 2 files could not be read; 1 game was skipped. This selection is incomplete:", "1 1")]
    [InlineData("2 files, 2 games, 1 decision",
        "2 of 3 files could not be read; 2 games and 1 decision were skipped. This selection is incomplete:", "2 2 1")]
    public async Task Home_TheRecordsHeadline_CountsFilesNotRead_ApartFromGamesAndDecisionsSkipped(
        string which, string headline, string strongNumbers)
    {
        var cut = await HomeOverAWalkAsync(HeadlineCase(which));

        var record = Box(cut, "rejectedFilesNotice");
        var head = record.Content.Children[0];
        Assert.Equal(headline, Collapsed(head.TextContent));
        Assert.Equal(strongNumbers, string.Join(' ', head.QuerySelectorAll("strong").Select(strong => strong.TextContent.Trim())));
        // Rejected files first, then the omissions in the walk's order, one item each.
        var report = App.Setup!.Loaded.Report;
        Assert.Equal(
            [.. report.Rejected.Select(rejection => rejection.SourceName), .. report.Omissions.Select(omission => omission.SourceFile)],
            Items(record).Select(item => item.Split(' ')[0]));
        Assert.False(StartButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_AnOmissionsReason_IsOneListItem_Whole_WhateverLineBreaksItHolds()
    {
        // A refused standing's reason is the rule's message whole: the sentence, the parameter
        // .NET appends and, for an out-of-range value, the value on a line of its own — the
        // platform's line break, a CRLF on Windows (XgOmission.Reason). It is shown unparsed and
        // unshortened, in one item.
        var cut = await HomeOverAWalkAsync(RefusedGame2Of3("match.xg"));
        var omission = Assert.Single(App.Setup!.Loaded.Report.Omissions);
        Assert.Contains(Environment.NewLine, omission.Reason);   // the premise

        var item = Assert.Single(Box(cut, "rejectedFilesNotice").Content.QuerySelectorAll("li"));
        Assert.Equal("match.xg", item.QuerySelector("code")!.TextContent);
        // bUnit reads the markup back through an HTML parser, which turns every CRLF into an LF.
        Assert.Equal($"match.xg — {omission}".ReplaceLineEndings("\n"), item.TextContent);
    }

    [Fact]
    public async Task Home_TheForcedLine_IsTheWalksCount_DancesIncluded()
    {
        // The count has one owner, the walk's report, and a dance is forced (Hal's ruling).
        var cut = await HomeOverAWalkAsync(TestFixtures.PickedXg(
            TestFixtures.Match(game => game.WithOpeningPlays(), game => game.WithForcedPlay().WithDance()), "match.xg"));

        Assert.Contains(ExpectedText.DecisionsMatchYourFilters(2), Text(cut));
        Assert.Contains(ExpectedText.ForcedPlaysSetAside(2), Text(cut));
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

    /// <summary>The solution board's request: the one diagram a review draws.</summary>
    private static DiagramRequest SolutionRequest(IRenderedComponent<QuizPage> cut) =>
        cut.FindComponents<BackgammonDiagram>().Single().Instance.Request!;

    [Fact]
    public async Task Quiz_AShownPlay_DoesNotSurviveATransition_EvenWithNoClickOnThePage()
    {
        // Showing a play (SPEC-quiz-view.md §1, 2026-10-25). The transitions are driven
        // on the coordinator, not by buttons: in the browser a button's click also
        // reaches the page's restore handler, which would hide a transition that left
        // the shown play in place.
        var app = await StartedAppAsync(Problem(1), Problem(2));
        await app.SubmitPlayAsync(Alt());
        var cut = Render<QuizPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("rect[data-candidate]")));
        Assert.Null(SolutionRequest(cut).ShownPlayIndex);

        await cut.Find("rect[data-candidate='0']").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Equal(0, SolutionRequest(cut).ShownPlayIndex));

        // Back from the first problem's solution reopens its question; a practice
        // answer opens a new review of the same decision, where index 0 would be
        // a valid shown play again.
        app.GoBack();
        cut.WaitForAssertion(() => Assert.Null(app.Review));
        Assert.Equal(1, app.ProblemNumber);
        await app.SubmitPlayAsync(Best());
        cut.WaitForAssertion(() => Assert.Contains(QuizPage.PracticePrefix, Text(cut)));

        Assert.Null(SolutionRequest(cut).ShownPlayIndex);
    }

    [Fact]
    public async Task Quiz_APlayShownDuringTheStatsSave_SurvivesTheSavesEnd()
    {
        // A submission publishes its review, then awaits the stats write; the write's
        // start and end each notify the page while the same review stays on screen
        // (halheinrich/backgammon#440, leg 4's follow-up).
        var app = await StartedAppAsync(Problem(1), Problem(2));
        var folder = NextFolder;   // the folder the pick landed
        var cut = Render<QuizPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".board-container")));
        var write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        folder.OnWriting = () => write.Task;

        var submitting = app.SubmitPlayAsync(Alt());
        cut.WaitForAssertion(() =>
        {
            Assert.True(app.IsBusy);
            Assert.NotEmpty(cut.FindAll("rect[data-candidate]"));
        });
        var review = app.Review;
        await cut.Find("rect[data-candidate='0']").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Equal(0, SolutionRequest(cut).ShownPlayIndex));

        write.SetResult();
        await submitting;

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".board-page.app-busy")));
        Assert.False(app.IsBusy);
        Assert.Single(folder.Writes);
        Assert.Same(review, app.Review);
        Assert.Equal(0, SolutionRequest(cut).ShownPlayIndex);
    }

    [Fact]
    public async Task Quiz_TheRestoreHandler_IsOnThePageOnlyWhileAPlayIsShown()
    {
        var app = await StartedAppAsync(Problem(1), Problem(2));
        await app.SubmitPlayAsync(Alt());
        var cut = Render<QuizPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("rect[data-candidate]")));

        // Nothing shown: no handler, so a click on the page re-renders nothing.
        Assert.False(cut.Find(".board-page").HasAttribute("blazor:onclick"));

        await cut.Find("rect[data-candidate='0']").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Equal(0, SolutionRequest(cut).ShownPlayIndex));
        Assert.True(cut.Find(".board-page").HasAttribute("blazor:onclick"));

        // A click elsewhere on the page bubbles to the root and restores the played position.
        await cut.Find(".status-strip").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Null(SolutionRequest(cut).ShownPlayIndex));
        Assert.False(cut.Find(".board-page").HasAttribute("blazor:onclick"));
        Assert.NotNull(app.Review);
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

    [Fact]
    public async Task Settings_OffersAnonymizePlayers_LastInTheBoard_WithItsRuledLine_AndRecordsTheChoice()
    {
        var cut = Render<SettingsPage>();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#settingsAnonymizePlayers")));
        var box = cut.Find("#settingsAnonymizePlayers");
        Assert.Equal("checkbox", box.GetAttribute("type"));
        Assert.False(box.HasAttribute("checked"));
        Assert.Equal("Anonymize players", cut.Find("label[for=settingsAnonymizePlayers]").TextContent.Trim());

        // The last control in "The board", and its help line is the fieldset's last line.
        var board = cut.FindAll("fieldset").First(f => f.QuerySelector("legend")?.TextContent.Trim() == "The board");
        Assert.Equal("settingsAnonymizePlayers", board.QuerySelectorAll("input").Last().Id);
        // Ruled copy (halheinrich/backgammon#448, comment 6487470153), word for word.
        Assert.Equal(
            "The board's rails show \"On Roll\" and \"Opponent\" in place of the players' names, on the problem and on its solution. The Player names filter still matches the real names.",
            Regex.Replace(board.Children.Last().TextContent, @"\s+", " ").Trim());

        _plan.ExpectWrite(BrowserStorageArea.Local, QuizSettings.StorageKey, SettingsPayload.Of(anonymizePlayers: true), BrowserStorageWriteAnswer.Succeeded);
        await box.ChangeAsync(new() { Value = true });

        // The write is declared exactly, so any other bytes fail at the click; that the
        // setter writes at all is QuizSettingsTests' pin.
        Assert.True(Settings.AnonymizePlayers);
    }

    // ── Help ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Help_ThingsWorthKnowing_SaysHowToReviewCubeDecisionsBlind()
    {
        var cut = Render<HelpPage>();

        // Ruled copy (Hal approved it in chat, 2026-10-28), word for word.
        var bullet = cut.FindAll("li").Single(li => li.TextContent.Contains("Reviewing your own cube decisions blind", StringComparison.Ordinal));
        Assert.Equal(
            "Reviewing your own cube decisions blind. With Anonymize players turned on in Settings, the board shows \"On Roll\" and \"Opponent\" in place of the players' names, so it doesn't tell you whether you doubled or took.",
            Regex.Replace(bullet.TextContent, @"\s+", " ").Trim());
        Assert.Equal("Reviewing your own cube decisions blind.", bullet.QuerySelector("strong")!.TextContent);
        Assert.Equal(["Anonymize players", "Settings"], bullet.QuerySelectorAll("em").Select(e => e.TextContent));
        // Among the other things worth knowing, in that section's list.
        Assert.Contains(bullet.ParentElement!.Children, li => li.TextContent.Contains("Under depth-first ranking", StringComparison.Ordinal));
    }

    // ── Anonymize players on the quiz's boards ──────────────────────────────────

    /// <summary>Turn Anonymize players on, declaring its write.</summary>
    private async Task TurnAnonymizeOnAsync()
    {
        await Settings.EnsureHydratedAsync();
        _plan.ExpectWrite(BrowserStorageArea.Local, QuizSettings.StorageKey, SettingsPayload.Of(anonymizePlayers: true), BrowserStorageWriteAnswer.Succeeded);
        await Settings.SetAnonymizePlayersAsync(true);
    }

    /// <summary>A diagram's rails as drawn: each text that carries a side's score in a 3-point match.</summary>
    private static string[] Rails(string markup) =>
        [.. Regex.Matches(markup, @">([^<>]*needs 3)</text>").Select(m => m.Groups[1].Value)];

    /// <summary>What the rails read with the setting off and on: the fixtures' Alice on roll against Bob.</summary>
    private static string[] ExpectedRails(bool anonymize) =>
        anonymize ? ["On Roll needs 3", "Opponent needs 3"] : ["Alice needs 3", "Bob needs 3"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Quiz_ACubeDecision_TheProblemAndTheSolutionRails_FollowAnonymize(bool anonymize)
    {
        var app = await StartedAppAsync(TestFixtures.CubeDecision(away: 3));
        if (anonymize) await TurnAnonymizeOnAsync();
        var cut = Render<QuizPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".board-container")));

        var problem = cut.FindComponents<BackgammonDiagram>().Single();
        Assert.Equal(anonymize ? QuizSettings.AnonymousRailNames : null, problem.Instance.Request!.RailNames);
        Assert.Equal(ExpectedRails(anonymize), Rails(problem.Markup).Order());

        await app.SubmitCubeAnswerAsync(CubeAnswer.NoDouble);
        cut.WaitForAssertion(() => Assert.Equal(DiagramMode.Solution, SolutionRequest(cut).Mode));
        var solution = cut.FindComponents<BackgammonDiagram>().Single();
        Assert.Equal(ExpectedRails(anonymize), Rails(solution.Markup).Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Quiz_ACheckerPlay_ThePlayEntrysWorkingBoardAndTheSolutionRails_FollowAnonymize(bool anonymize)
    {
        var app = await StartedAppAsync(Problem(3));
        if (anonymize) await TurnAnonymizeOnAsync();
        var cut = Render<QuizPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindComponents<BackgammonPlayEntry>()));

        // The entry draws its own working board (WithWorkingBoard of the page's request).
        var working = cut.FindComponent<BackgammonPlayEntry>().FindComponents<BackgammonDiagram>().Single();
        Assert.Equal(anonymize ? QuizSettings.AnonymousRailNames : null, working.Instance.Request!.RailNames);
        Assert.Equal(ExpectedRails(anonymize), Rails(working.Markup).Order());

        await app.SubmitPlayAsync(Best());
        cut.WaitForAssertion(() => Assert.Equal(DiagramMode.Solution, SolutionRequest(cut).Mode));
        var solution = cut.FindComponents<BackgammonDiagram>().Single();
        Assert.Equal(ExpectedRails(anonymize), Rails(solution.Markup).Order());
    }
}
