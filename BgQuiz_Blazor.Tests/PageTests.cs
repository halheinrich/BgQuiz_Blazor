using BgDataTypes_Lib;
using BgDiag_Razor.Components;
using BgGame_Lib;
using BgQuiz_Blazor.Quiz;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.Components;

// `BgQuiz_Blazor.Quiz` is a namespace; `BgQuiz_Blazor.Components.Pages.Quiz`
// is the page type — the using-import above shadows the type. Aliases keep
// the test calls (Render<QuizPage>()) unambiguous without renaming the page.
using HomePage = BgQuiz_Blazor.Components.Pages.Home;
using QuizPage = BgQuiz_Blazor.Components.Pages.Quiz;
using DonePage = BgQuiz_Blazor.Components.Pages.Done;

namespace BgQuiz_Blazor.Tests;

public class PageTests : BunitContext
{
    private static Play BestPlay() => TestFixtures.MakePlay((8, 5), (8, 5));
    private static Play AltPlay() => TestFixtures.MakePlay((13, 11), (11, 8));

    private QuizController WithController(params BgDecisionData[] items)
    {
        var fake = new FakeProblemSetSource(items);
        var controller = new QuizController(_ => fake);
        Services.AddSingleton(controller);
        return controller;
    }

    /// <summary>A directory guaranteed to exist — the test's own bin folder.</summary>
    private static string ExistingDirectory => AppContext.BaseDirectory;

    /// <summary>
    /// Register a <see cref="ProblemSetSelection"/> with the given directory so
    /// the rendered <c>Home</c> page resolves it. Returned for assertions.
    /// </summary>
    private ProblemSetSelection WithSelection(string directory)
    {
        var selection = new ProblemSetSelection { Directory = directory };
        Services.AddSingleton(selection);
        return selection;
    }

    // -----------------------------------------------------------------------
    //  Home.razor
    // -----------------------------------------------------------------------

    [Fact]
    public void Home_BeforeFilterApply_StartButtonDisabled()
    {
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithSelection(ExistingDirectory);
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();

        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.True(startBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_FilterPanelEmitsConfig_EnablesStartButton()
    {
        // Regression test for the FilterPanel binding bug: prior to the fix,
        // Home subscribed to a non-existent `OnFiltersChanged` event with a
        // `DecisionFilterSet` payload. The correct binding is
        // `OnFilterConfigChanged` with a `FilterConfig` payload. Without it,
        // the user's Apply-click never reached the Home handler — the Start
        // button stayed disabled forever.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithSelection(ExistingDirectory);
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();
        var fp = cut.FindComponent<FilterPanel>();

        await cut.InvokeAsync(() =>
            fp.Instance.OnFilterConfigChanged.InvokeAsync(new FilterConfig()));

        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.False(startBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_StartClick_HandsUserFilterConfigToControllerPipeline()
    {
        // End-to-end check that the Apply → Start flow actually narrows the
        // decision stream by the user's selections. Captures the
        // DecisionFilterSet the controller hands to its source factory and
        // asserts the user's PlayerFilter (Players=["Alice"]) survives the
        // FilterConfig.Build() materialization.
        DecisionFilterSet? capturedPipeline = null;
        var fake = new FakeProblemSetSource([TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay())]);
        var controller = new QuizController(set => { capturedPipeline = set; return fake; });
        Services.AddSingleton(controller);
        WithSelection(ExistingDirectory);
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();
        var fp = cut.FindComponent<FilterPanel>();
        await cut.InvokeAsync(() =>
            fp.Instance.OnFilterConfigChanged.InvokeAsync(
                new FilterConfig { Players = ["Alice"] }));

        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        await startBtn.ClickAsync(new());

        Assert.NotNull(capturedPipeline);
        var aliceData = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), onRoll: "Alice");
        var bobData = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), onRoll: "Bob");
        Assert.True(capturedPipeline.Matches(aliceData));
        Assert.False(capturedPipeline.Matches(bobData));
    }

    [Fact]
    public async Task Home_DirectoryInputChange_ThreadsToSelection()
    {
        // The picker → ProblemSetSelection half of the source-selection wire;
        // ServerDiskProblemSetSourceFactoryTests pins the other half
        // (selection → source).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        var selection = WithSelection(string.Empty);
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();
        var input = cut.Find("input#problemSetDir");
        await input.ChangeAsync(new ChangeEventArgs { Value = ExistingDirectory });

        Assert.Equal(ExistingDirectory, selection.Directory);
    }

    [Fact]
    public void Home_LocalStorageValue_OverridesSeededDirectory()
    {
        // localStorage rehydration: a persisted choice survives a reload and
        // overrides the appsettings-seeded default.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        var selection = WithSelection("seeded-default");
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<string?>("localStorage.getItem", "bgquiz_problemsetdirectory")
            .SetResult(ExistingDirectory);

        Render<HomePage>();

        Assert.Equal(ExistingDirectory, selection.Directory);
    }

    [Fact]
    public async Task Home_EmptyDirectory_StartButtonDisabled()
    {
        // Filters applied but no directory chosen → Start stays disabled.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithSelection(string.Empty);
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();
        var fp = cut.FindComponent<FilterPanel>();
        await cut.InvokeAsync(() =>
            fp.Instance.OnFilterConfigChanged.InvokeAsync(new FilterConfig()));

        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.True(startBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_NonexistentDirectory_StartButtonDisabled()
    {
        // Filters applied and a directory chosen, but it does not exist on the
        // server → Start stays disabled.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        var phantom = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        WithSelection(phantom);
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();
        var fp = cut.FindComponent<FilterPanel>();
        await cut.InvokeAsync(() =>
            fp.Instance.OnFilterConfigChanged.InvokeAsync(new FilterConfig()));

        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.True(startBtn.HasAttribute("disabled"));
    }

    // -----------------------------------------------------------------------
    //  Quiz.razor
    // -----------------------------------------------------------------------

    [Fact]
    public void Quiz_NoQuizStarted_RedirectsHome()
    {
        WithController();
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        Render<QuizPage>();

        Assert.EndsWith("/", nav.Uri);
    }

    [Fact]
    public async Task Quiz_AlreadyFinished_RedirectsToDone()
    {
        var c = WithController(); // empty source → exhausts immediately
        await c.StartAsync(new FilterConfig());
        Assert.True(c.IsFinished);
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        Render<QuizPage>();

        Assert.EndsWith("/done", nav.Uri);
    }

    [Fact]
    public async Task Quiz_Active_RendersScorePanelAndButtons()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());

        var cut = Render<QuizPage>();

        Assert.Contains("Submitted", cut.Markup);
        Assert.Contains("Skipped", cut.Markup);
        Assert.Contains("Submit", cut.Markup);
        Assert.Contains("Skip", cut.Markup);
        Assert.Contains("Restart", cut.Markup);
    }

    [Fact]
    public async Task Quiz_SubmitButton_DisabledBeforePlayCompleted()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());

        var cut = Render<QuizPage>();

        var submit = cut.Find("button.btn-primary");
        Assert.True(submit.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Quiz_SkipClick_AdvancesController()
    {
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = WithController(d1, d2);
        await c.StartAsync(new FilterConfig());

        var cut = Render<QuizPage>();
        var skipButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Skip");
        await skipButton.ClickAsync(new());

        Assert.Equal(1, c.SkippedCount);
        Assert.Same(d2, c.Current);
    }

    [Fact]
    public async Task Quiz_FinishedAfterContinue_RedirectsToDone()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        // Submit enters review (no redirect yet); Continue drives past the
        // source's tail. The page is subscribed to StateChanged and should
        // redirect to /done once IsFinished flips on Continue.
        await cut.InvokeAsync(() => c.SubmitPlay(BestPlay()));
        Assert.False(c.IsFinished);
        await c.ContinueAsync();

        Assert.True(c.IsFinished);
        Assert.EndsWith("/done", nav.Uri);
    }

    [Fact]
    public async Task Quiz_AfterSubmit_ShowsSolutionView_ContinueReturnsToEntry()
    {
        // The review branch: after Submit the page shows the solution view —
        // Continue is offered and the Submit / Skip action row is gone. Continue
        // advances to the next problem and the entry row returns. Driven through
        // the wire (cube entry callback → Submit click → Continue click).
        var c = WithController(
            TestFixtures.CubeDecision(),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        var cubeEntry = cut.FindComponent<BackgammonCubeEntry>();
        await cut.InvokeAsync(() =>
            cubeEntry.Instance.OnCubeDecisionCompleted.InvokeAsync(
                new CubeDecisionPair(CubeAction.Double, CubeAction.Take)));
        var submit = cut.FindAll("button").First(b => b.TextContent.Trim() == "Submit");
        await submit.ClickAsync(new());

        // Review view: Continue present, Submit / Skip gone.
        var reviewButtons = cut.FindAll("button").Select(b => b.TextContent.Trim()).ToList();
        Assert.Contains("Continue", reviewButtons);
        Assert.DoesNotContain("Submit", reviewButtons);
        Assert.DoesNotContain("Skip", reviewButtons);

        var continueBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Continue");
        await continueBtn.ClickAsync(new());

        // Back to the answering view for the next problem.
        var entryButtons = cut.FindAll("button").Select(b => b.TextContent.Trim()).ToList();
        Assert.Contains("Submit", entryButtons);
        Assert.Contains("Skip", entryButtons);
        Assert.DoesNotContain("Continue", entryButtons);
    }

    [Fact]
    public async Task Quiz_CubeDecision_RendersCubeEntryAndCubeActionRow()
    {
        var c = WithController(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig());

        var cut = Render<QuizPage>();

        // Cube entry's two button groups render their labels.
        Assert.Contains("No Double", cut.Markup);
        Assert.Contains("Take", cut.Markup);
        Assert.Contains("Submit", cut.Markup);
        Assert.Contains("Skip", cut.Markup);
        // Cube decisions have no partial-move state, so no Undo row.
        Assert.DoesNotContain("Undo", cut.Markup);
    }

    [Fact]
    public async Task Quiz_CubeSubmit_DisabledBeforeCubeCompleted()
    {
        var c = WithController(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig());

        var cut = Render<QuizPage>();

        var submit = cut.Find("button.btn-primary");
        Assert.True(submit.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Quiz_CubeComplete_ThenSubmit_ScoresIntoCubeSegments()
    {
        // The parent → child → handler wire for cube: the cube entry fires
        // OnCubeDecisionCompleted, the page latches it and enables Submit, and
        // the Submit click routes to SubmitCubeAction, scoring both halves
        // into the Double / Take segments.
        var c = WithController(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        var cubeEntry = cut.FindComponent<BackgammonCubeEntry>();
        await cut.InvokeAsync(() =>
            cubeEntry.Instance.OnCubeDecisionCompleted.InvokeAsync(
                new CubeDecisionPair(CubeAction.Double, CubeAction.Take)));

        var submit = cut.FindAll("button").First(b => b.TextContent.Trim() == "Submit");
        await submit.ClickAsync(new());

        Assert.Single(c.CubeHistory);
        Assert.Equal(1, c.Score.DoubleDecisions.Submitted);
        Assert.Equal(1, c.Score.DoubleDecisions.Correct);
        Assert.Equal(1, c.Score.TakeDecisions.Submitted);
        Assert.Equal(1, c.Score.TakeDecisions.Correct);
    }

    // -----------------------------------------------------------------------
    //  Done.razor
    // -----------------------------------------------------------------------

    [Fact]
    public void Done_NoQuizStarted_RedirectsHome()
    {
        WithController();
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        Render<DonePage>();

        Assert.EndsWith("/", nav.Uri);
    }

    [Fact]
    public async Task Done_RendersFinalScoreAndBothButtons()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync(); // exhausts → IsFinished

        var cut = Render<DonePage>();

        Assert.Contains("Quiz complete", cut.Markup);
        Assert.Contains("Final", cut.Markup);
        Assert.Contains("Restart with same filters", cut.Markup);
        Assert.Contains("Start over", cut.Markup);
    }

    [Fact]
    public async Task Done_RestartClick_NavigatesToQuiz()
    {
        var c = WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();
        Assert.True(c.IsFinished);

        var cut = Render<DonePage>();
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        var restart = cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("Restart"));
        await restart.ClickAsync(new());

        Assert.EndsWith("/quiz", nav.Uri);
        Assert.False(c.IsFinished);
        Assert.Equal(QuizScore.Empty, c.Score);
    }

    [Fact]
    public async Task Done_StartOverClick_NavigatesHome()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();

        var cut = Render<DonePage>();
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        var startOver = cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("Start over"));
        await startOver.ClickAsync(new());

        Assert.EndsWith("/", nav.Uri);
    }

    [Fact]
    public async Task Done_MixedRun_RendersFourWayBreakdownAndProblemCount()
    {
        // One cube position + one checker play. The cube folds as +1 Double and
        // +1 Take, so Total.Submitted is 3 decisions — but only 2 problems were
        // shown. Pins both the four-way breakdown rows and the corrected count
        // (which must not double-count the cube position).
        var c = WithController(
            TestFixtures.CubeDecision(),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        c.SubmitCubeAction(new CubeDecisionPair(CubeAction.Double, CubeAction.Take));
        await c.ContinueAsync();
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();
        Assert.True(c.IsFinished);

        var cut = Render<DonePage>();

        // Four-way breakdown rows.
        Assert.Contains("Play", cut.Markup);
        Assert.Contains("Double", cut.Markup);
        Assert.Contains("Take", cut.Markup);
        Assert.Contains("Total", cut.Markup);

        // Total.Submitted counts 3 decisions, but problems-shown is 2.
        Assert.Equal(3, c.Score.Total.Submitted);
        Assert.Contains("Total problems shown", cut.Markup);
        Assert.Contains("<strong>2</strong>", cut.Markup);
    }
}
