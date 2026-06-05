using BgDataTypes_Lib;
using BgDiag_Razor.Components;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.Components;

// `BgQuiz_Blazor.Client.Quiz` is a namespace; `BgQuiz_Blazor.Client.Components.Pages.Quiz`
// is the page type — the using-import above shadows the type. Aliases keep
// the test calls (Render<QuizPage>()) unambiguous without renaming the page.
using HomePage = BgQuiz_Blazor.Client.Components.Pages.Home;
using QuizPage = BgQuiz_Blazor.Client.Components.Pages.Quiz;
using DonePage = BgQuiz_Blazor.Client.Components.Pages.Done;

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

    /// <summary>
    /// Register a <see cref="PickedProblemSet"/> already holding one file so the
    /// rendered <c>Home</c> page's file gate is satisfied — lets tests exercise
    /// the filter gate / Start click in isolation. The bytes are irrelevant: the
    /// quiz runs against the test's fake source, not the picked file.
    /// </summary>
    private void WithPickedFile(string name = "sample.xg")
    {
        var problemSet = new PickedProblemSet();
        problemSet.Set([new PickedFile(name, [1, 2, 3])]);
        Services.AddSingleton(problemSet);
    }

    /// <summary>
    /// Register an <see cref="AppliedFilter"/> for the rendered <c>Home</c> page
    /// (Home injects it). With <paramref name="applied"/> non-null the filter half
    /// of the gate is already satisfied — simulating navigate-back with a config
    /// the user applied earlier this session; otherwise it starts un-applied.
    /// </summary>
    private void WithAppliedFilter(FilterConfig? applied = null)
    {
        var holder = new AppliedFilter();
        if (applied is not null) holder.Set(applied);
        Services.AddSingleton(holder);
    }

    // -----------------------------------------------------------------------
    //  Home.razor
    // -----------------------------------------------------------------------

    [Fact]
    public void Home_BeforeFilterApply_StartButtonDisabled()
    {
        // No file picked yet, so Start is disabled regardless of filters.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        Services.AddSingleton(new PickedProblemSet());
        WithAppliedFilter();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();

        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.True(startBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_PrePopulatedHolder_RendersSummaryAndEnablesStart()
    {
        // Navigate-back regression: the picked set lives in the per-app
        // PickedProblemSet, which survives in-app navigation, but Home is
        // re-instantiated on return. The summary must derive from the holder,
        // not a transient component field — the old field reset to null on
        // re-instantiation, blanking the summary while the file gate stayed
        // satisfied (summary blank + Start enabled = the reported desync).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFile("resume.xg"); // holder already populated, as after navigate-back
        WithAppliedFilter();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();

        // Summary renders straight from the persisted holder, no pick handler run.
        Assert.Contains("resume.xg", cut.Markup);

        // With both gates met (file already held + filters applied) Start enables.
        var fp = cut.FindComponent<FilterPanel>();
        await cut.InvokeAsync(() =>
            fp.Instance.OnFilterConfigChanged.InvokeAsync(new FilterConfig()));

        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.False(startBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_FilterPanelEmitsConfig_EnablesStartButton()
    {
        // FilterPanel binding contract: Home subscribes to OnFilterConfigChanged
        // (FilterConfig payload). With a file already picked, applying filters
        // satisfies the second gate and flips Start to enabled.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFile();
        WithAppliedFilter();
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
        WithPickedFile(); // satisfy the file gate so Start is clickable
        WithAppliedFilter();
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
    public void Home_FilePick_BuildsPickedFileWithExtensionBearingName()
    {
        // The InputFile handler reads each browser file into a PickedFile that
        // preserves the original name *with* its extension — required by the
        // stream iterator's DecisionId stamping. This pins the picker → holder
        // half of the source wire; WasmUploadedProblemSetSourceTests pins the
        // other half (holder → source → controller).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        var problemSet = new PickedProblemSet();
        Services.AddSingleton(problemSet);
        WithAppliedFilter();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();
        var input = cut.FindComponent<InputFile>();
        input.UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "match.xg"));

        cut.WaitForAssertion(() => Assert.True(problemSet.HasFiles));
        var file = Assert.Single(problemSet.Files);
        Assert.Equal("match.xg", file.FileName);
        Assert.Equal([1, 2, 3], file.Bytes);
    }

    [Fact]
    public async Task Home_FilePickedAndFiltersApplied_EnablesStart()
    {
        // Both gates: a file picked *and* filters applied.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        Services.AddSingleton(new PickedProblemSet());
        WithAppliedFilter();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();

        // Filters applied but no file yet → still disabled.
        var fp = cut.FindComponent<FilterPanel>();
        await cut.InvokeAsync(() =>
            fp.Instance.OnFilterConfigChanged.InvokeAsync(new FilterConfig()));
        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.True(startBtn.HasAttribute("disabled"));

        // Pick a file → both gates satisfied → enabled.
        var input = cut.FindComponent<InputFile>();
        input.UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "match.xg"));

        cut.WaitForAssertion(() =>
        {
            var btn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
            Assert.False(btn.HasAttribute("disabled"));
        });
    }

    [Fact]
    public void Home_PreAppliedFilterHolder_EnablesStartWithoutReApply()
    {
        // Navigate-back regression (filter half): the applied filter lives in the
        // per-app AppliedFilter holder, which survives in-app navigation, but Home
        // is re-instantiated on return. The gate must re-derive from the holder,
        // not a transient component field — the old field reset to false, forcing
        // a needless re-click of Apply even though the values persisted. With both
        // holders pre-populated (file picked + filter applied earlier this
        // session) Start is enabled on first render, no FilterPanel callback run.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFile("resume.xg");
        WithAppliedFilter(new FilterConfig()); // applied earlier, as after navigate-back
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();

        // FilterPanel re-renders and silently restores its values from
        // localStorage (raising no callback), so the applied holder is untouched
        // and Start is enabled without re-applying.
        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.False(startBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_FiltersDirty_ClearsAppliedState_DisablesStart()
    {
        // Gate semantics guard: "applied" means the user deliberately applied, not
        // merely that a config exists. Editing any filter control fires the
        // panel's dirty signal, which must clear the applied holder so a
        // half-edited set re-disables Start — even with a file still picked.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFile();
        WithAppliedFilter(new FilterConfig()); // start from an applied, enabled state
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();

        // Both gates met → enabled.
        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.False(startBtn.HasAttribute("disabled"));

        // User edits a filter → dirty → applied state cleared → disabled again.
        var fp = cut.FindComponent<FilterPanel>();
        await cut.InvokeAsync(() => fp.Instance.OnFilterDirty.InvokeAsync());

        startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
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
