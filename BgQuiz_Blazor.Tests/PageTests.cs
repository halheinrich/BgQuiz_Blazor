using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BackgammonDiagram_Lib;
using BackgammonDiagram_Lib.Rendering;
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
using StatsPage = BgQuiz_Blazor.Client.Components.Pages.Stats;
using HelpPage = BgQuiz_Blazor.Client.Components.Pages.Help;
using ScorePanelComponent = BgQuiz_Blazor.Client.Components.Pages.ScorePanel;

namespace BgQuiz_Blazor.Tests;

public class PageTests : BunitContext
{
    public PageTests()
    {
        // Home and Done inject the sessionStorage-backed QuizLiveMarker. It needs
        // only the framework IJSRuntime — which bUnit registers in Services — so
        // one fixture-wide registration serves every page render. The marker's
        // JS calls are handled per-test through JSInterop (Loose mode, or an
        // explicit Setup where a test drives a specific stored value).
        Services.AddScoped<QuizLiveMarker>();
    }

    /// <summary>The sessionStorage key <see cref="QuizLiveMarker"/> reads/writes.</summary>
    private const string QuizLiveKey = "bgquiz.quizLive";

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

    /// <summary>
    /// Register a <see cref="ShuffleOption"/> for the rendered <c>Home</c> page
    /// (Home injects it). Every Home render needs one — the checkbox binds to it
    /// unconditionally — so every Home test calls this alongside
    /// <see cref="WithAppliedFilter"/> / <see cref="WithPickedFile"/>. Returns the
    /// holder so tests can assert the toggle after a checkbox interaction.
    /// </summary>
    private ShuffleOption WithShuffleOption(bool enabled = false)
    {
        var holder = new ShuffleOption();
        if (enabled) holder.Set(true);
        Services.AddSingleton(holder);
        return holder;
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
        WithShuffleOption();
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
        WithShuffleOption();
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
        WithShuffleOption();
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
        WithShuffleOption();
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
        WithShuffleOption();
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
        WithShuffleOption();
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
        WithShuffleOption();
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
        WithShuffleOption();
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

    [Fact]
    public async Task Home_StartClick_EmptyFilterResult_ShowsBannerAndStaysHome()
    {
        // The empty-result guard: a filter set matching zero decisions makes
        // StartAsync exhaust immediately (IsFinished true straight away). Without
        // the post-Start check the page navigates to /quiz and the user bounces to
        // a 0/0 /done with no hint why. With it, the page stays on / and shows the
        // no-match banner. Empty source == zero filter matches at the controller's
        // seam.
        var controller = WithController(); // empty source → finishes on Start
        WithPickedFile();
        WithAppliedFilter();
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        var cut = Render<HomePage>();
        var fp = cut.FindComponent<FilterPanel>();
        await cut.InvokeAsync(() =>
            fp.Instance.OnFilterConfigChanged.InvokeAsync(new FilterConfig()));

        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        await startBtn.ClickAsync(new());

        Assert.True(controller.IsFinished);           // controller did start and exhaust
        Assert.EndsWith("/", nav.Uri);                // stayed on Home, no /quiz nav
        Assert.Contains("No quiz problems matched these filters", cut.Markup);
        // A neutral status message, not the assertive error banner.
        Assert.Contains("role=\"status\"", cut.Markup);
        Assert.DoesNotContain("Could not start quiz", cut.Markup);
    }

    [Fact]
    public async Task Home_StartClick_AllMatchesAutoSkippedPasses_ShowsSameBanner()
    {
        // The second, indistinguishable cause of an immediately-finished
        // controller: every admitted decision is an auto-skipped pass position, so
        // the user is shown nothing even though the filter "matched". The page
        // can't tell this apart from zero matches, and the wording must not claim
        // to — same neutral banner, same stay-home behavior. Pins the "both causes"
        // wording decision.
        var controller = WithController(TestFixtures.PassDecision());
        WithPickedFile();
        WithAppliedFilter();
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        var cut = Render<HomePage>();
        var fp = cut.FindComponent<FilterPanel>();
        await cut.InvokeAsync(() =>
            fp.Instance.OnFilterConfigChanged.InvokeAsync(new FilterConfig()));

        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        await startBtn.ClickAsync(new());

        Assert.True(controller.IsFinished);
        Assert.EndsWith("/", nav.Uri);
        Assert.Contains("No quiz problems matched these filters", cut.Markup);
    }

    [Fact]
    public async Task Home_StartClick_NonEmptyResult_NavigatesToQuizWithoutBanner()
    {
        // Over-trigger guard for the empty-result check: a source with a showable
        // decision leaves the controller unfinished after Start, so the page must
        // navigate to /quiz and raise no no-match banner.
        var controller = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFile();
        WithAppliedFilter();
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        var cut = Render<HomePage>();
        var fp = cut.FindComponent<FilterPanel>();
        await cut.InvokeAsync(() =>
            fp.Instance.OnFilterConfigChanged.InvokeAsync(new FilterConfig()));

        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        await startBtn.ClickAsync(new());

        Assert.False(controller.IsFinished);
        Assert.EndsWith("/quiz", nav.Uri);
        Assert.DoesNotContain("No quiz problems matched these filters", cut.Markup);
    }

    [Fact]
    public void Home_ShuffleCheckbox_TogglesHolder()
    {
        // UI wire: the checkbox's @onchange must reach the ShuffleOption holder —
        // no intermediate transient field to desync on navigate-back, matching
        // AppliedFilter / PickedProblemSet's holder-first pattern.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        Services.AddSingleton(new PickedProblemSet());
        WithAppliedFilter();
        var shuffle = WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();
        var checkbox = cut.Find("#shuffleOrder");
        Assert.False(checkbox.HasAttribute("checked"));

        checkbox.Change(true);
        Assert.True(shuffle.Enabled);

        checkbox.Change(false);
        Assert.False(shuffle.Enabled);
    }

    [Fact]
    public void Home_BootWithLiveMarker_NoActiveQuiz_ShowsResetNotice()
    {
        // A2: a full reload rebooted the runtime out from under a live quiz. On
        // the fresh boot the controller has no quiz (HasStarted false) but the
        // sessionStorage marker survived — so Home surfaces the polite reset
        // notice, then clears the marker so it shows once. Without the boot check
        // this notice never renders (the fails-without-the-fix guard).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay())); // not started
        Services.AddSingleton(new PickedProblemSet());
        WithAppliedFilter();
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<string?>("sessionStorage.getItem", QuizLiveKey).SetResult("1");

        var cut = Render<HomePage>();

        Assert.Contains("previous quiz was reset by the page reload", cut.Markup);
        Assert.Contains("role=\"status\"", cut.Markup); // polite outcome, not an alert
        JSInterop.VerifyInvoke("sessionStorage.removeItem"); // cleared when shown
    }

    [Fact]
    public void Home_BootWithoutMarker_ShowsNoResetNotice()
    {
        // A2 over-trigger guard: an ordinary cold boot (no marker) must not
        // announce a reset. getItem returns null → no notice.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        Services.AddSingleton(new PickedProblemSet());
        WithAppliedFilter();
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<string?>("sessionStorage.getItem", QuizLiveKey).SetResult(null);

        var cut = Render<HomePage>();

        Assert.DoesNotContain("previous quiz was reset", cut.Markup);
    }

    [Fact]
    public async Task Home_MarkerPresentButQuizLive_ShowsNoResetNotice()
    {
        // A2's HasStarted guard — the multi-tab-safe part on the *controller*
        // side. In-app navigation back to Home mid-quiz keeps the same per-tab
        // controller (quiz still live) and leaves the marker set. That is not a
        // reload, so no notice fires; the marker is also left in place for a real
        // later reload (VerifyNotInvoke on removeItem).
        var controller = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await controller.StartAsync(new FilterConfig()); // HasStarted true
        Services.AddSingleton(new PickedProblemSet());
        WithAppliedFilter();
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<string?>("sessionStorage.getItem", QuizLiveKey).SetResult("1");

        var cut = Render<HomePage>();

        Assert.DoesNotContain("previous quiz was reset", cut.Markup);
        JSInterop.VerifyNotInvoke("sessionStorage.removeItem"); // marker left in place
    }

    [Fact]
    public async Task Home_StartClick_MarksQuizLive()
    {
        // A2 lifecycle: a successful Start (non-empty result → navigates to /quiz)
        // records the live-quiz marker, so a mid-quiz reload can be acknowledged
        // on the next boot.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFile();
        WithAppliedFilter();
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();
        var fp = cut.FindComponent<FilterPanel>();
        await cut.InvokeAsync(() =>
            fp.Instance.OnFilterConfigChanged.InvokeAsync(new FilterConfig()));

        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        await startBtn.ClickAsync(new());

        JSInterop.VerifyInvoke("sessionStorage.setItem");
    }

    [Fact]
    public async Task Home_StartClick_EmptyResult_DoesNotMarkQuizLive()
    {
        // A2 over-trigger guard: the empty-result path stays on Home with no live
        // quiz, so it must not set the marker — otherwise the next boot would
        // falsely announce a reset for a quiz that never ran.
        WithController(); // empty source → finishes on Start
        WithPickedFile();
        WithAppliedFilter();
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();
        var fp = cut.FindComponent<FilterPanel>();
        await cut.InvokeAsync(() =>
            fp.Instance.OnFilterConfigChanged.InvokeAsync(new FilterConfig()));

        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        await startBtn.ClickAsync(new());

        JSInterop.VerifyNotInvoke("sessionStorage.setItem");
    }

    [Fact]
    public void Home_ClearPickedFiles_RemovesSummaryAndDisablesStart()
    {
        // A4: the Clear affordance beside the picked-file summary drops the set —
        // the holder-derived summary disappears and the file half of the gate
        // re-disables Start by construction. Start from a fully-armed state
        // (file picked + filters applied) so the disable is attributable to the
        // clear, not to the filter half.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFile("clear-me.xg");
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();

        // Armed: summary shown, Start enabled.
        Assert.Contains("clear-me.xg", cut.Markup);
        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.False(startBtn.HasAttribute("disabled"));

        // Clear → summary gone, Start disabled.
        var clear = cut.FindAll("button").First(b => b.TextContent.Trim() == "Clear");
        clear.Click();

        Assert.DoesNotContain("clear-me.xg", cut.Markup);
        startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.True(startBtn.HasAttribute("disabled"));
    }

    /// <summary>
    /// Builds a <see cref="QuizController"/> whose source factory mirrors the
    /// real wiring in the client's <c>Program.cs</c>: it reads <paramref
    /// name="shuffle"/> at invocation time and, when enabled, wraps the fake
    /// source in a seeded (deterministic) <c>ShuffledProblemSetSource</c>.
    /// </summary>
    private QuizController WithShufflableController(ShuffleOption shuffle, params BgDecisionData[] items)
    {
        var fake = new FakeProblemSetSource(items);
        var controller = new QuizController(_ =>
            shuffle.Enabled ? new ShuffledProblemSetSource(fake, seed: 42) : fake);
        Services.AddSingleton(controller);
        return controller;
    }

    /// <summary>Drives <paramref name="controller"/> through its whole run via Skip, collecting each shown decision's Id in presentation order.</summary>
    private static async Task<List<DecisionId>> CollectPresentedOrderAsync(QuizController controller)
    {
        var ids = new List<DecisionId>();
        while (controller.Current is { } current)
        {
            ids.Add(current.Id);
            await controller.SkipCurrentAsync();
        }
        return ids;
    }

    private static BgDecisionData[] OrderedDecisions(int count) =>
        Enumerable.Range(0, count)
            .Select(i => TestFixtures.TwoChoiceDecision(
                BestPlay(), AltPlay(), id: new XgpDecisionId($"test{i}.xgp")))
            .ToArray();

    [Fact]
    public async Task Home_StartClick_ShuffleUnchecked_PreservesFileOrder()
    {
        // Baseline: with the toggle left unchecked (its default), Start hands the
        // controller the plain fake source and the quiz presents decisions in
        // exactly file (insertion) order — this stays green as the "unchanged
        // behavior" anchor for the checked case below.
        var items = OrderedDecisions(6);
        var shuffle = WithShuffleOption();
        var controller = WithShufflableController(shuffle, items);
        WithPickedFile();
        WithAppliedFilter();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();
        var fp = cut.FindComponent<FilterPanel>();
        await cut.InvokeAsync(() =>
            fp.Instance.OnFilterConfigChanged.InvokeAsync(new FilterConfig()));

        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        await startBtn.ClickAsync(new());

        var order = await CollectPresentedOrderAsync(controller);
        Assert.Equal(items.Select(d => d.Id), order);
    }

    [Fact]
    public async Task Home_StartClick_ShuffleChecked_YieldsNonFileOrder()
    {
        // Checking the box before Start must flow through to the constructed
        // source: the controller's presentation order differs from file order
        // (seeded, so deterministic) while still presenting the exact same set
        // of decisions.
        var items = OrderedDecisions(6);
        var shuffle = WithShuffleOption();
        var controller = WithShufflableController(shuffle, items);
        WithPickedFile();
        WithAppliedFilter();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();
        var fp = cut.FindComponent<FilterPanel>();
        await cut.InvokeAsync(() =>
            fp.Instance.OnFilterConfigChanged.InvokeAsync(new FilterConfig()));

        var checkbox = cut.Find("#shuffleOrder");
        checkbox.Change(true);

        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        await startBtn.ClickAsync(new());

        var order = await CollectPresentedOrderAsync(controller);
        Assert.Equal(items.Select(d => d.Id).ToHashSet(), order.ToHashSet());
        Assert.NotEqual(items.Select(d => d.Id), order);
    }

    /// <summary>The client assembly's informational version — the single source
    /// the Home page reads for its <c>v{version}</c> footer.</summary>
    private static string AssemblyInformationalVersion() =>
        typeof(HomePage).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

    [Fact]
    public void Home_RendersAppVersion_SourcedFromAssembly()
    {
        // F: the landing page shows a small v{version}, sourced at runtime from
        // the client assembly's informational version (csproj <Version>), not a
        // hardcoded literal — asserting against the assembly keeps this robust
        // across version bumps.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        Services.AddSingleton(new PickedProblemSet());
        WithAppliedFilter();
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();

        var version = AssemblyInformationalVersion();
        Assert.False(string.IsNullOrWhiteSpace(version)); // non-empty
        Assert.Matches(@"^\d+\.\d+", version);            // expected shape: leading SemVer
        Assert.Contains($"v{version}", cut.Markup);
    }

    [Fact]
    public async Task Quiz_DoesNotRenderAppVersion()
    {
        // F placement: the version string is a Home-only footer — the quiz view
        // must not carry it.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());

        var cut = Render<QuizPage>();

        Assert.DoesNotContain($"v{AssemblyInformationalVersion()}", cut.Markup);
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
    }

    [Fact]
    public void ScorePanel_SubmittedScore_RendersTotalAccuracyAsPercent()
    {
        // Total = 3 correct of 4 submitted → 75%. Pins the percentage the panel
        // renders so the Accuracy-sourced PercentCorrect stays behaviour-neutral:
        // the ×100 display of the library's [0, 1] Accuracy, not a re-derivation.
        var score = new QuizScore(new ScoreSegment(4, 3, 0.8), ScoreSegment.Empty, ScoreSegment.Empty);

        var cut = Render<ScorePanelComponent>(p => p.Add(c => c.Score, score));

        Assert.Contains("(75%)", cut.Markup);
    }

    [Fact]
    public void ScorePanel_EmptyScore_OmitsPercent()
    {
        // Submitted == 0: the panel shows no "(…%)" at all. Accuracy is 0 on an
        // empty segment, so the guard that survives is the render-side @if, not a
        // divide-by-zero defence inside PercentCorrect.
        var cut = Render<ScorePanelComponent>(p => p.Add(c => c.Score, QuizScore.Empty));

        Assert.DoesNotContain("%", cut.Markup);
    }

    [Fact]
    public async Task Quiz_AnsweringState_RestartButtonAbsent()
    {
        // Restart was removed from the answering-state row; only Home/Done's
        // own Restart affordances (unrelated to this page) remain in the app.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());

        var cut = Render<QuizPage>();

        Assert.DoesNotContain("Restart", cut.Markup);
    }

    [Fact]
    public async Task Quiz_ReviewState_RestartButtonAbsent()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        await cut.InvokeAsync(() => c.SubmitPlay(BestPlay()));
        Assert.NotNull(c.Review);

        Assert.DoesNotContain("Restart", cut.Markup);
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

        await AnswerCubeAsync(cut, new CubeDecisionPair(CubeAction.Double, CubeAction.Take));
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
    public async Task Quiz_CubeAnswering_BoardHostsDiagramOnly_RadiosInActionRow()
    {
        // The cube-answering composition after the board-only migration: the board
        // region hosts a plain read-only BackgammonDiagram (no entry component), and
        // the cube answer is entered by BackgammonCubeActions living *inside* the
        // action row beside Submit / Skip — not on the board.
        var c = WithController(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig());

        var cut = Render<QuizPage>();

        // Board region: a bare diagram, and no play-entry wrapper.
        Assert.NotEmpty(cut.FindAll(".board-container .bg-diagram"));
        Assert.Empty(cut.FindAll(".board-container .bg-play-entry"));

        // The radios render in the action row, not the board region. Pin via the
        // stable structural hook — role="radiogroup" scoped to the row — not the
        // producer's caption text (a cosmetic rename there is BgDiag_Razor's
        // concern, covered by its own component tests).
        Assert.NotNull(cut.FindComponent<BackgammonCubeActions>());
        var actionRow = cut.Find("div.d-flex.flex-wrap.gap-2");
        Assert.NotEmpty(actionRow.QuerySelectorAll("[role=\"radiogroup\"]"));
        Assert.Empty(cut.FindAll(".board-container [role=\"radiogroup\"]"));

        // The consumer-owned cube action row: Submit / Skip, and no Undo — a cube
        // answer has no partial-move state.
        Assert.Contains("Submit", cut.Markup);
        Assert.Contains("Skip", cut.Markup);
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
        // The parent → child → handler wire for cube: BackgammonCubeActions fires
        // ValueChanged, @bind-Value latches it into _completedCube and enables
        // Submit, and the Submit click routes to SubmitCubeAction, scoring both
        // halves into the Double / Take segments.
        var c = WithController(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        await AnswerCubeAsync(cut, new CubeDecisionPair(CubeAction.Double, CubeAction.Take));

        var submit = cut.FindAll("button").First(b => b.TextContent.Trim() == "Submit");
        await submit.ClickAsync(new());

        Assert.Single(c.CubeHistory);
        Assert.Equal(1, c.Score.DoubleDecisions.Submitted);
        Assert.Equal(1, c.Score.DoubleDecisions.Correct);
        Assert.Equal(1, c.Score.TakeDecisions.Submitted);
        Assert.Equal(1, c.Score.TakeDecisions.Correct);
    }

    [Fact]
    public async Task Quiz_ProblemWithXgid_RendersXgidTextAndCopyButton()
    {
        // The decision carries an XGID, so the entry (problem) view overlays it
        // as selectable text plus a copy button in the board's upper-right.
        const string xgid = "XGID=-b----E-C---eE---c-e----B-:0:0:1:00:0:0:0:0:10";
        var c = WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), xgid: xgid));
        await c.StartAsync(new FilterConfig());

        var cut = Render<QuizPage>();

        Assert.Contains(xgid, cut.Markup);
        Assert.Contains("board-xgid", cut.Markup);
        var copy = cut.FindAll("button").First(b => b.TextContent.Trim() == "Copy");
        Assert.NotNull(copy);
    }

    [Fact]
    public async Task Quiz_ProblemWithoutXgid_HidesXgidLabel()
    {
        // Empty XGID (the fixture default) renders no badge at all.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());

        var cut = Render<QuizPage>();

        Assert.DoesNotContain("board-xgid", cut.Markup);
    }

    [Fact]
    public async Task Quiz_SolutionViewWithXgid_RendersXgidText()
    {
        // Coverage check for the second phase: after Submit the page flips to the
        // solution-review view, which must still surface the same XGID.
        const string xgid = "XGID=-b----E-C---eE---c-e----B-:1:1:1:00:5:3:0:7:10";
        var c = WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), xgid: xgid));
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        await cut.InvokeAsync(() => c.SubmitPlay(BestPlay()));
        Assert.NotNull(c.Review); // in the review (solution) state

        Assert.Contains(xgid, cut.Markup);
        Assert.Contains("board-xgid", cut.Markup);
    }

    [Fact]
    public async Task Quiz_SolutionView_AnswerDiffersFromRecorded_MarksBothStarAndDagger()
    {
        // G semantics: * marks the .xg-recorded played move (candidate 0 here),
        // † marks the quiz answer when it differs. The user answers the alt play
        // (candidate 1), so BuildSolutionRequest leaves UserPlayIndex at the
        // recorded 0 and sets SecondaryPlayIndex to the answered 1 — the solution
        // SVG draws both marks and the legend names both.
        var c = WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), recordedPlayIndex: 0));
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        await cut.InvokeAsync(() => c.SubmitPlay(AltPlay())); // answer = candidate 1

        var diagram = cut.FindComponent<BackgammonDiagram>();
        Assert.Equal(0, diagram.Instance.Request!.Decision.UserPlayIndex);   // * = recorded
        Assert.Equal(1, diagram.Instance.Request!.SecondaryPlayIndex);       // † = answer

        // SVG shows both marks (diagram markup excludes the page-level legend).
        Assert.Contains("*", diagram.Markup);
        Assert.Contains("†", diagram.Markup);

        // Legend explains both markers.
        Assert.Contains("* played", cut.Markup);
        Assert.Contains("† your answer", cut.Markup);
    }

    [Fact]
    public async Task Quiz_SolutionView_AnswerEqualsRecorded_MarksOnlyStar()
    {
        // The user played the recorded move (both candidate 0): SecondaryPlayIndex
        // coincides with UserPlayIndex, so the producer collapses † into the
        // single * — the SVG shows no † and the legend omits the answer half.
        var c = WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), recordedPlayIndex: 0));
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        await cut.InvokeAsync(() => c.SubmitPlay(BestPlay())); // answer = candidate 0 = recorded

        var diagram = cut.FindComponent<BackgammonDiagram>();
        Assert.Equal(0, diagram.Instance.Request!.Decision.UserPlayIndex);
        Assert.Equal(0, diagram.Instance.Request!.SecondaryPlayIndex);

        Assert.Contains("*", diagram.Markup);
        Assert.DoesNotContain("†", diagram.Markup);

        Assert.Contains("* played", cut.Markup);
        Assert.DoesNotContain("† your answer", cut.Markup);
    }

    [Fact]
    public async Task Quiz_SolutionView_OffListAnswer_MarksOnlyStar()
    {
        // An off-list answer isn't in the candidate list (review index -1), so
        // SecondaryPlayIndex is -1 and no † is drawn — only the recorded * shows.
        var c = WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), recordedPlayIndex: 0));
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        // A play matching neither candidate → off-list.
        await cut.InvokeAsync(() => c.SubmitPlay(TestFixtures.MakePlay((24, 23), (23, 21))));
        var review = Assert.IsType<ProblemReview.Play>(c.Review);
        Assert.True(review.OffList);

        var diagram = cut.FindComponent<BackgammonDiagram>();
        Assert.Equal(0, diagram.Instance.Request!.Decision.UserPlayIndex);
        Assert.Equal(-1, diagram.Instance.Request!.SecondaryPlayIndex);

        Assert.Contains("*", diagram.Markup);
        Assert.DoesNotContain("†", diagram.Markup);

        Assert.Contains("* played", cut.Markup);
        Assert.DoesNotContain("† your answer", cut.Markup);
    }

    [Fact]
    public async Task Quiz_CompletePlay_DiceClick_SubmitsThroughBoundCallback()
    {
        // The checker-play analog of Quiz_CubeComplete_ThenSubmit: the parent →
        // child → handler wire for a dice-click submit. Driving the inner
        // BackgammonPlayEntry to completion (1/off) and clicking the dice hit-rect
        // fires OnSubmitRequested, which Quiz.razor binds to its Submit handler —
        // routing HandleDiceClick → OnSubmitRequested → Submit and scoring exactly
        // as the Submit button would. Without that binding the dice click is a
        // silent no-op: Review stays null and the page never leaves the answering
        // view, so this test fails.
        var decision = TestFixtures.BearOffOneDecision();
        var c = WithController(decision);
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        // Same request the page builds for the entry; drives the hit-rect indices.
        var request = DiagramRequest.FromDecisionData(decision, DiagramMode.Problem);

        // Answering state — not yet in review.
        Assert.Null(c.Review);

        // One-click completion: clicking the 1-pt advances its lone checker, whose
        // only move bears off (ToPt 0); with no checker left the play completes in
        // a single click — no separate tray step.
        await ClickRectAsync(cut, RectIndexForPoint(request, 1));

        // The completing move re-rendered the board (the borne-off checker is
        // gone), so the dice hit-rect must be re-queried against the new render —
        // a stale pre-move index would land on a now-handler-less rect and throw
        // MissingEventHandlerException. ClickDiceAsync re-finds the rects, then the
        // complete-play dice click signals submit intent → bound Submit runs.
        await ClickDiceAsync(cut);

        // Controller scored and entered review — the dice click submitted the
        // matched best play, exactly as a Submit-button click would.
        Assert.NotNull(c.Review);
        Assert.Single(c.History);
        Assert.True(c.History[0].IsCorrect);
        Assert.Equal(1, c.Score.Total.Submitted);

        // The page flipped to the solution view: Continue present, Submit gone.
        var buttons = cut.FindAll("button").Select(b => b.TextContent.Trim()).ToList();
        Assert.Contains("Continue", buttons);
        Assert.DoesNotContain("Submit", buttons);
    }

    [Fact]
    public async Task Quiz_ReviewState_DiceClick_AdvancesLikeContinue()
    {
        // The review branch's read-only BackgammonDiagram binds OnDiceClicked to
        // the same ContinueAsync handler as the Continue button — clicking the
        // dice hit-region during review must advance to the next problem exactly
        // as Continue does. Without that binding the click is a silent no-op:
        // Review stays set and Current stays on the answered problem, so this
        // test fails.
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = WithController(d1, d2);
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        await cut.InvokeAsync(() => c.SubmitPlay(BestPlay()));
        Assert.NotNull(c.Review); // in the review (solution) state
        Assert.Same(d1, c.Current);

        // The review view renders only the read-only diagram (no entry
        // component), so the last transparent hit-rect is unambiguously its dice.
        await ClickDiceAsync(cut);

        Assert.Null(c.Review);
        Assert.Same(d2, c.Current);
    }

    [Fact]
    public async Task Quiz_RedoClick_ReturnsToAnsweringState_SameProblem()
    {
        // Wire test for the Redo button itself: clicking it during review must
        // reverse the just-submitted cube answer and fall back to the answering
        // view on the exact same problem.
        var c = WithController(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();
        var current = c.Current;

        await AnswerCubeAsync(cut, new CubeDecisionPair(CubeAction.Double, CubeAction.Take));
        var submit = cut.FindAll("button").First(b => b.TextContent.Trim() == "Submit");
        await submit.ClickAsync(new());
        Assert.NotNull(c.Review);

        var redo = cut.FindAll("button").First(b => b.TextContent.Trim() == "Redo");
        await redo.ClickAsync(new());

        Assert.Null(c.Review);
        Assert.Same(current, c.Current);
        Assert.Empty(c.CubeHistory);

        var buttons = cut.FindAll("button").Select(b => b.TextContent.Trim()).ToList();
        Assert.Contains("Submit", buttons);
        Assert.DoesNotContain("Continue", buttons);
        Assert.DoesNotContain("Redo", buttons);
    }

    [Fact]
    public async Task Quiz_Redo_CubeActions_ClearsSelection_AndSecondAnswerScoresCleanly()
    {
        // Redo's answer-freshness for the cube kind. BackgammonCubeActions is
        // strictly controlled off _completedCube — it holds no selection state of
        // its own — and HandleStateChanged nulls _completedCube on the Redo
        // transition, so the radios render unselected on the way back regardless
        // of remounting. This pins that: after Redo no radio is checked, and a
        // second (different) answer scores cleanly as the only CubeHistory entry.
        var c = WithController(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        await AnswerCubeAsync(cut, new CubeDecisionPair(CubeAction.Double, CubeAction.Take));
        Assert.NotEmpty(cut.FindAll("input[checked]")); // first answer selected a radio
        var submit = cut.FindAll("button").First(b => b.TextContent.Trim() == "Submit");
        await submit.ClickAsync(new());
        Assert.NotNull(c.Review);

        var redo = cut.FindAll("button").First(b => b.TextContent.Trim() == "Redo");
        await redo.ClickAsync(new());
        Assert.Null(c.Review);

        // No radio left checked — a carried-over selection would still show the
        // first answer's pill.
        Assert.Empty(cut.FindAll("input[checked]"));

        // Re-answer differently and confirm clean scoring: exactly one
        // CubeHistory entry, reflecting only the second answer.
        await AnswerCubeAsync(cut, new CubeDecisionPair(CubeAction.NoDouble, CubeAction.Pass));
        var submit2 = cut.FindAll("button").First(b => b.TextContent.Trim() == "Submit");
        await submit2.ClickAsync(new());

        Assert.Single(c.CubeHistory);
        var sub = c.CubeHistory[0];
        Assert.False(sub.DoublerCorrect);
        Assert.False(sub.TakerCorrect);
        Assert.Equal(1, c.Score.DoubleDecisions.Submitted);
        Assert.Equal(0, c.Score.DoubleDecisions.Correct);
        Assert.Equal(1, c.Score.TakeDecisions.Submitted);
        Assert.Equal(0, c.Score.TakeDecisions.Correct);
    }

    [Fact]
    public async Task Quiz_CubeActions_SelectEnablesSubmit_ThenSkipClearsForNextProblem()
    {
        // Submit-enable round-trip + clear-on-Skip. Selecting a cube action latches
        // _completedCube and enables Submit; Skipping to the next cube problem must
        // null it via HandleStateChanged, so the next problem starts with Submit
        // disabled and no radio checked (the previous answer never carries over).
        var c = WithController(TestFixtures.CubeDecision(), TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        // Disabled until an answer is selected.
        Assert.True(cut.Find("button.btn-primary").HasAttribute("disabled"));

        await AnswerCubeAsync(cut, new CubeDecisionPair(CubeAction.Double, CubeAction.Take));
        Assert.False(cut.Find("button.btn-primary").HasAttribute("disabled"));
        Assert.NotEmpty(cut.FindAll("input[checked]"));

        // Skip advances to the next cube problem — the answer must not carry over.
        var skip = cut.FindAll("button").First(b => b.TextContent.Trim() == "Skip");
        await skip.ClickAsync(new());

        Assert.True(cut.Find("button.btn-primary").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("input[checked]"));
    }

    [Fact]
    public async Task Quiz_CubeActions_ClearsForNextProblemOnContinue()
    {
        // Clear-on-Continue: answer + Submit (→ review) + Continue advances to the
        // next cube problem, which must start with a cleared answer (no radio
        // checked, Submit disabled) — HandleStateChanged nulls _completedCube on
        // both the submit and the continue transitions.
        var c = WithController(TestFixtures.CubeDecision(), TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        await AnswerCubeAsync(cut, new CubeDecisionPair(CubeAction.Double, CubeAction.Take));
        var submit = cut.FindAll("button").First(b => b.TextContent.Trim() == "Submit");
        await submit.ClickAsync(new());
        Assert.NotNull(c.Review);

        var continueBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Continue");
        await continueBtn.ClickAsync(new());

        Assert.Null(c.Review);
        Assert.Empty(cut.FindAll("input[checked]"));
        Assert.True(cut.Find("button.btn-primary").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Quiz_Redo_PlayEntry_RemountsFreshComponent()
    {
        // The play-entry analog: BackgammonPlayEntry only resets its internal
        // MoveEntryState when Mop/Dice differ from the last request it saw, and
        // Redo returns to the SAME Mop/Dice — but Submit already unmounted the
        // entry when the page swapped to the review branch, so that
        // reset-suppression path is never reached. A distinct component
        // instance post-Redo pins the guarantee that the branch swap alone
        // produces a genuinely fresh entry.
        var decision = TestFixtures.BearOffOneDecision();
        var c = WithController(decision);
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        var request = DiagramRequest.FromDecisionData(decision, DiagramMode.Problem);
        var firstEntry = cut.FindComponent<BackgammonPlayEntry>().Instance;

        await ClickRectAsync(cut, RectIndexForPoint(request, 1)); // completes the play
        var submit = cut.FindAll("button").First(b => b.TextContent.Trim() == "Submit");
        await submit.ClickAsync(new());
        Assert.NotNull(c.Review);

        var redo = cut.FindAll("button").First(b => b.TextContent.Trim() == "Redo");
        await redo.ClickAsync(new());

        Assert.Null(c.Review);
        var secondEntry = cut.FindComponent<BackgammonPlayEntry>().Instance;
        Assert.NotSame(firstEntry, secondEntry);
    }

    [Fact]
    public async Task Quiz_ShowStatsButton_PresentInAnsweringAndReviewStates()
    {
        // The "Show stats" affordance must be reachable regardless of
        // Controller.Review — it's present in both action rows.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        Assert.Contains("Show stats", cut.Markup);

        await cut.InvokeAsync(() => c.SubmitPlay(BestPlay()));
        Assert.NotNull(c.Review);

        Assert.Contains("Show stats", cut.Markup);
    }

    [Fact]
    public async Task Quiz_AnsweringState_ShowStatsButton_OccupiesTrailingMsAutoSlot()
    {
        // Show stats now sits where Restart used to — the row's trailing
        // ms-auto slot — rather than the standalone block above the branch.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        var rowButtons = cut.FindAll("div.d-flex.flex-wrap.gap-2 button").ToList();
        var showStats = Assert.Single(rowButtons, b => b.TextContent.Trim() == "Show stats");
        Assert.True(showStats.ClassList.Contains("ms-auto"));
        Assert.Same(showStats, rowButtons[^1]); // last button in the row
    }

    [Fact]
    public async Task Quiz_ReviewState_ShowStatsButton_OccupiesTrailingMsAutoSlot()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();
        await cut.InvokeAsync(() => c.SubmitPlay(BestPlay()));
        Assert.NotNull(c.Review);

        var rowButtons = cut.FindAll("div.d-flex.flex-wrap.gap-2 button").ToList();
        var showStats = Assert.Single(rowButtons, b => b.TextContent.Trim() == "Show stats");
        Assert.True(showStats.ClassList.Contains("ms-auto"));
        Assert.Same(showStats, rowButtons[^1]);
    }

    [Fact]
    public async Task QuizToStatsToQuiz_FromReviewState_PreservesCurrentAndReview()
    {
        // Round trip through /stats must not disturb the in-progress problem:
        // Stats is a read-only consumer of the same live QuizController — no
        // Submit / Continue / Skip call — so Current and Review (captured here in
        // the review state, the more telling case since it's non-null) survive
        // the whole /quiz -> /stats -> /quiz trip unchanged.
        var d1 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var d2 = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = WithController(d1, d2);
        await c.StartAsync(new FilterConfig());
        var quizCut = Render<QuizPage>();
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        await quizCut.InvokeAsync(() => c.SubmitPlay(BestPlay()));
        var currentBeforeStats = c.Current;
        var reviewBeforeStats = c.Review;
        Assert.NotNull(reviewBeforeStats);

        var showStats = quizCut.FindAll("button").First(b => b.TextContent.Trim() == "Show stats");
        await showStats.ClickAsync(new());
        Assert.EndsWith("/stats", nav.Uri);

        var statsCut = Render<StatsPage>();
        Assert.Same(currentBeforeStats, c.Current);
        Assert.Equal(reviewBeforeStats, c.Review);

        var backButton = statsCut.FindAll("button").First(b => b.TextContent.Trim() == "Back to quiz");
        await backButton.ClickAsync(new());
        Assert.EndsWith("/quiz", nav.Uri);

        // Re-rendering Quiz confirms the controller itself was never touched —
        // still the same problem, still in review.
        Render<QuizPage>();
        Assert.Same(currentBeforeStats, c.Current);
        Assert.Equal(reviewBeforeStats, c.Review);
    }

    // -----------------------------------------------------------------------
    //  Hit-rect click helpers (Quiz answering state renders only the entry's
    //  board, so the page's transparent overlay rects are the entry's). Order
    //  mirrors BackgammonDiagram's overlay emission: Points in iteration order,
    //  then bar, optional cube, optional tray, dice last. Rects are re-found per
    //  click so post-render handler IDs stay fresh — a stale index against a
    //  re-rendered board throws MissingEventHandlerException.
    // -----------------------------------------------------------------------

    private static int RectIndexForPoint(DiagramRequest req, int point)
    {
        var regions = DiagramRenderer.GetHitRegions(req, new DiagramOptions());
        int i = 0;
        foreach (var kvp in regions.Points)
        {
            if (kvp.Key == point) return i;
            i++;
        }
        throw new ArgumentException($"Point {point} not present in regions.");
    }

    private static Task ClickRectAsync(IRenderedComponent<QuizPage> cut, int rectIndex)
    {
        var rects = cut.FindAll("rect[fill='transparent'][pointer-events='all']");
        return rects[rectIndex].ClickAsync(new());
    }

    private static Task ClickDiceAsync(IRenderedComponent<QuizPage> cut)
    {
        // The dice rect is emitted last (after points, bar, cube, tray) and a
        // play always has a dice region, so the final transparent rect is it.
        var rects = cut.FindAll("rect[fill='transparent'][pointer-events='all']");
        return rects[^1].ClickAsync(new());
    }

    /// <summary>
    /// Answers the rendered cube-answering page by invoking
    /// <see cref="BackgammonCubeActions"/>'s <c>ValueChanged</c> with the given
    /// pair — the parent-side half of the <c>@bind-Value</c> wire the page relies
    /// on. Driving by the stable <see cref="CubeDecisionPair"/> data contract
    /// (not the producer's radio-caption text) keeps the consumer test insulated
    /// from cosmetic label renames; a mis-named / dropped binding leaves
    /// <c>_completedCube</c> unset, so Submit stays disabled and the caller fails.
    /// </summary>
    private static Task AnswerCubeAsync(IRenderedComponent<QuizPage> cut, CubeDecisionPair answer) =>
        cut.InvokeAsync(() =>
            cut.FindComponent<BackgammonCubeActions>().Instance.ValueChanged.InvokeAsync(answer));

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
        JSInterop.Mode = JSRuntimeMode.Loose; // Done clears the live-quiz marker on init

        var cut = Render<DonePage>();

        Assert.Contains("Quiz complete", cut.Markup);
        Assert.Contains("Final", cut.Markup);
        Assert.Contains("Restart with same filters", cut.Markup);

        // A3: the navigation button describes navigation ("Back to setup") and
        // must not promise a reset it doesn't perform — the holders persist, so
        // there is no "new filters" (the label that used to lie).
        Assert.Contains("Back to setup", cut.Markup);
        Assert.DoesNotContain("Start over", cut.Markup);
        Assert.DoesNotContain("new filters", cut.Markup);
    }

    [Fact]
    public async Task Done_ReachingDone_ClearsLiveQuizMarker()
    {
        // A2 lifecycle: reaching Done is honest completion, so it clears the
        // live-quiz marker — a subsequent boot must not misread a finished quiz
        // as one a reload interrupted.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync(); // exhausts → IsFinished
        JSInterop.Mode = JSRuntimeMode.Loose;

        Render<DonePage>();

        JSInterop.VerifyInvoke("sessionStorage.removeItem");
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
        JSInterop.Mode = JSRuntimeMode.Loose; // Done clears the live-quiz marker on init

        var cut = Render<DonePage>();
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        var restart = cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("Restart"));
        await restart.ClickAsync(new());

        Assert.EndsWith("/quiz", nav.Uri);
        Assert.False(c.IsFinished);
        Assert.Equal(QuizScore.Empty, c.Score);
    }

    [Fact]
    public async Task Done_RestartClick_ReMarksQuizLive()
    {
        // A2 lifecycle, restart path: reaching Done cleared the live-quiz marker;
        // Restart makes a quiz live again, so it must re-set it — otherwise a
        // reload during the restarted quiz falls back to the old silent reset with
        // no notice (the one-click-wide hole this closes). The sibling half —
        // reaching Done clears the marker, whatever route arrived there — is pinned
        // by Done_ReachingDone_ClearsLiveQuizMarker and holds equally for the
        // restart-then-finish loop, since any Done render clears on init.
        var c = WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();
        Assert.True(c.IsFinished);
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<DonePage>();
        var restart = cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("Restart"));
        await restart.ClickAsync(new());

        JSInterop.VerifyInvoke("sessionStorage.setItem"); // re-marked live on Restart
    }

    [Fact]
    public async Task Done_BackToSetupClick_NavigatesHome()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();
        JSInterop.Mode = JSRuntimeMode.Loose; // Done clears the live-quiz marker on init

        var cut = Render<DonePage>();
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        var backToSetup = cut.FindAll("button").First(b => b.TextContent.Trim() == "Back to setup");
        await backToSetup.ClickAsync(new());

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
        JSInterop.Mode = JSRuntimeMode.Loose; // Done clears the live-quiz marker on init

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

    // -----------------------------------------------------------------------
    //  Stats.razor
    // -----------------------------------------------------------------------

    [Fact]
    public void Stats_NoQuizStarted_RedirectsHome()
    {
        WithController();
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        Render<StatsPage>();

        Assert.EndsWith("/", nav.Uri);
    }

    [Fact]
    public async Task Stats_QuizFinished_RedirectsToDone()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync(); // exhausts -> finished
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        Render<StatsPage>();

        Assert.EndsWith("/done", nav.Uri);
    }

    [Fact]
    public async Task Stats_MidQuiz_RendersLiveScoreAndBreakdownWithoutRedirecting()
    {
        // Mid-quiz: one problem answered (correct), one still pending — the quiz
        // is started but not finished, so Stats must render in place rather than
        // bouncing anywhere.
        var c = WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        c.SubmitPlay(BestPlay());
        Assert.False(c.IsFinished);
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        var baseUri = nav.Uri;

        var cut = Render<StatsPage>();

        // Honest mid-quiz headings — not Done's "Final" / hardcoded literal.
        Assert.Contains("Progress so far", cut.Markup);
        Assert.Contains("Detailed evaluation so far", cut.Markup);

        // Live score from the same in-progress controller.
        Assert.Equal(1, c.Score.Total.Submitted);
        Assert.Equal(1, c.Score.Total.Correct);
        Assert.Contains("Play", cut.Markup);
        Assert.Contains("Double", cut.Markup);
        Assert.Contains("Take", cut.Markup);
        Assert.Contains("Total", cut.Markup);

        // No redirect fired — OnInitialized's guards did not trigger.
        Assert.Equal(baseUri, nav.Uri);
    }

    [Fact]
    public async Task Stats_BackToQuizClick_NavigatesToQuiz()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        var cut = Render<StatsPage>();
        var backButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Back to quiz");
        await backButton.ClickAsync(new());

        Assert.EndsWith("/quiz", nav.Uri);
    }

    // -----------------------------------------------------------------------
    //  Help.razor
    // -----------------------------------------------------------------------

    [Fact]
    public void Help_RendersTheFlowSectionsAndTheSemanticsSection()
    {
        // The page exists to teach the flow, the click vocabulary of a checker play,
        // *and* the semantics a user cannot discover by clicking around; pin its
        // section skeleton so a future edit can't quietly drop part of it. The
        // headings alone are pinned, never the prose beneath them.
        WithController();

        var cut = Render<HelpPage>();

        var headings = cut.FindAll("h2").Select(h => h.TextContent.Trim()).ToList();
        Assert.Equal(
            [
                "Pick your files",
                "Choose filters",
                "Answer the position",
                "Making a checker play",
                "Scoring",
                "Review the solution",
                "Stats and finishing",
                "Things worth knowing",
            ],
            headings);
    }

    [Fact]
    public void Help_StatesFileCaps_SourcedFromTheConstantsHomeEnforces()
    {
        // SSOT: Home enforces the pick against PickedFileLimits and Help documents
        // the same constants, with the megabyte figure *derived* from the byte cap
        // rather than restated. Asserting against the constants (not the literals
        // "50" / "500") is what makes this fail if page prose and enforced rule
        // ever drift — which is the whole reason the caps were hoisted off Home.
        WithController();

        var cut = Render<HelpPage>();

        Assert.Contains($"{PickedFileLimits.MaxFileCount} files", cut.Markup);
        Assert.Contains($"{PickedFileLimits.MaxFileMegabytes} MB", cut.Markup);
    }

    [Fact]
    public void Help_NoQuizInProgress_RendersWithoutRedirecting_AndOffersNoBackButton()
    {
        // Unlike Stats, Help is reachable from any state — including a cold visit
        // or a bookmark — so it must never bounce. With no quiz to return to, the
        // Back affordance is simply absent.
        WithController();
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        var baseUri = nav.Uri;

        var cut = Render<HelpPage>();

        Assert.Equal(baseUri, nav.Uri); // no redirect fired
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Back to quiz");
    }

    [Fact]
    public async Task Help_QuizFinished_OffersNoBackButton()
    {
        // The finished quiz has no answering state to return to — the same half of
        // the predicate Stats redirects to /done on.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync(); // exhausts → finished
        Assert.True(c.IsFinished);

        var cut = Render<HelpPage>();

        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Back to quiz");
    }

    [Fact]
    public async Task Help_MidQuiz_BackToQuizClick_NavigatesToQuiz()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        Assert.True(c.HasStarted && !c.IsFinished);
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        var cut = Render<HelpPage>();
        var back = cut.FindAll("button").First(b => b.TextContent.Trim() == "Back to quiz");
        await back.ClickAsync(new());

        Assert.EndsWith("/quiz", nav.Uri);
    }

    // -----------------------------------------------------------------------
    //  Quiz.razor layout: board-on-top + XGID badge in the producer overlay
    //
    //  These pin the structural contract the width-driven bottom-row layout
    //  depends on; the sizing itself (aspect-ratio, letterboxing, badge tracking)
    //  is pure CSS that bUnit's AngleSharp DOM can't evaluate — verified live in
    //  the browser instead.
    // -----------------------------------------------------------------------

    private const string SampleXgid = "XGID=-b----E-C---eE---c-e----B-:0:0:1:42:0:0:0:1:10";

    [Fact]
    public async Task Quiz_PlayState_XgidBadge_RendersInProducerOverlay_NotBoardContainerSibling()
    {
        // The badge is passed via BackgammonPlayEntry's Overlay slot, so it lands
        // inside the producer's .bg-diagram-overlay (which tracks the board box),
        // not as a direct child of .board-container (which no longer matches the
        // board under letterboxing — the whole reason for the overlay move).
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), xgid: SampleXgid));
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        var badge = cut.Find(".board-xgid");
        Assert.Contains("bg-diagram-overlay", badge.ParentElement!.ClassList);
        Assert.NotEmpty(cut.FindAll(".bg-play-entry .bg-diagram-overlay .board-xgid"));
        Assert.Empty(cut.FindAll(".board-container > .board-xgid"));
    }

    [Fact]
    public async Task Quiz_CubeState_XgidBadge_RendersInProducerOverlay()
    {
        // Cube answering now renders a bare BackgammonDiagram (board-only), so the
        // badge lands in the producer's .bg-diagram-overlay exactly as in review —
        // there is no .bg-cube-entry wrapper any more.
        var c = WithController(TestFixtures.CubeDecision(xgid: SampleXgid));
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        var badge = cut.Find(".board-xgid");
        Assert.Contains("bg-diagram-overlay", badge.ParentElement!.ClassList);
        Assert.NotEmpty(cut.FindAll(".board-container .bg-diagram .bg-diagram-overlay .board-xgid"));
        Assert.Empty(cut.FindAll(".board-container > .board-xgid"));
    }

    [Fact]
    public async Task Quiz_ReviewState_XgidBadge_RendersInProducerOverlay()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), xgid: SampleXgid));
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();
        await cut.InvokeAsync(() => c.SubmitPlay(BestPlay()));
        Assert.NotNull(c.Review);

        var badge = cut.Find(".board-xgid");
        Assert.Contains("bg-diagram-overlay", badge.ParentElement!.ClassList);
        Assert.Empty(cut.FindAll(".board-container > .board-xgid"));
    }

    [Fact]
    public async Task Quiz_BoardContainer_RendersBeforeChrome()
    {
        // Board-on-top: .board-container precedes .board-chrome in source order,
        // which the width-driven layout relies on (board first, chrome below).
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        var markup = cut.Markup;
        var boardIdx = markup.IndexOf("board-container", StringComparison.Ordinal);
        var chromeIdx = markup.IndexOf("board-chrome", StringComparison.Ordinal);
        Assert.True(boardIdx >= 0, "board-container present");
        Assert.True(chromeIdx >= 0, "board-chrome present");
        Assert.True(boardIdx < chromeIdx, "the board must render before the chrome (board-on-top)");
    }

    // -----------------------------------------------------------------------
    //  Status strip: state-invariant chrome between the score panel and the
    //  action row. The strip is ALWAYS rendered — empty legend + neutral prompt
    //  while answering, legend + verdict at review — so chrome height (a fixed
    //  CSS constant) and therefore board size is equal across states. bUnit
    //  can't measure the CSS heights; these pin the structural half: the strip
    //  and both its lines exist in every state, with the right content.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Quiz_PlayAnswering_StatusStrip_ShowsNeutralPrompt()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        var strip = cut.Find(".status-strip");
        Assert.Equal(string.Empty, strip.QuerySelector(".status-legend")!.TextContent.Trim());
        var verdict = strip.QuerySelector(".status-verdict")!;
        Assert.Contains("alert-secondary", verdict.ClassList);
        Assert.Contains("build your play", verdict.TextContent);
    }

    [Fact]
    public async Task Quiz_CubeAnswering_StatusStrip_ShowsNeutralPrompt()
    {
        var c = WithController(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        var strip = cut.Find(".status-strip");
        Assert.Equal(string.Empty, strip.QuerySelector(".status-legend")!.TextContent.Trim());
        var verdict = strip.QuerySelector(".status-verdict")!;
        Assert.Contains("alert-secondary", verdict.ClassList);
        Assert.Contains("cube action", verdict.TextContent);
    }

    [Fact]
    public async Task Quiz_Review_StatusStrip_CarriesLegendAndVerdict()
    {
        // Recorded play present (index 0) and the user answers the alt play, so
        // the legend names both markers and the verdict is the not-best line.
        var c = WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), recordedPlayIndex: 0));
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        await cut.InvokeAsync(() => c.SubmitPlay(AltPlay()));
        Assert.NotNull(c.Review);

        var strip = cut.Find(".status-strip");
        var legend = strip.QuerySelector(".status-legend")!.TextContent;
        Assert.Contains("* played", legend);
        Assert.Contains("† your answer", legend);

        var verdict = strip.QuerySelector(".status-verdict")!;
        Assert.Contains("alert-danger", verdict.ClassList);
        Assert.Contains("Not best", verdict.TextContent);
        Assert.DoesNotContain("Submit.", verdict.TextContent); // prompt gone
    }

    [Fact]
    public async Task Quiz_StatusStrip_SitsBetweenScorePanelAndActionRow()
    {
        // The settled design places the fixed-height strip between the score
        // panel and the button row in both states; pin the answering order (the
        // review branch shares the same strip instance above the branch).
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig());
        var cut = Render<QuizPage>();

        var markup = cut.Markup;
        var scoreIdx = markup.IndexOf("score-panel", StringComparison.Ordinal);
        var stripIdx = markup.IndexOf("status-strip", StringComparison.Ordinal);
        var rowIdx = markup.IndexOf("d-flex flex-wrap gap-2", StringComparison.Ordinal);
        Assert.True(scoreIdx >= 0 && stripIdx >= 0 && rowIdx >= 0, "all three chrome pieces present");
        Assert.True(scoreIdx < stripIdx, "strip renders after the score panel");
        Assert.True(stripIdx < rowIdx, "strip renders before the action row");
    }

    [Fact]
    public void AppCss_DeclaresNoBoardAspectRatioLiteral()
    {
        // SSOT: the board's ratio is single-sourced to the producer's self-sizing
        // .bg-diagram (BgDiag_Razor emits aspect-ratio inline from its viewBox).
        // BgQuiz must re-encode no ratio — no `aspect-ratio` declaration, and none
        // of the historical literals (16/9, 429.8/446). Comments (which reference
        // the ratio in prose) are stripped first so only real CSS is checked.
        var css = File.ReadAllText(AppCssPath());
        var noComments = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

        Assert.DoesNotContain("aspect-ratio", noComments);
        Assert.DoesNotContain("429.8", noComments);
        Assert.DoesNotContain("446", noComments);
        Assert.DoesNotContain("16 / 9", noComments);
        Assert.DoesNotContain("16/9", noComments);
    }

    [Fact]
    public void AppCss_RetiredBoundedHeightGlue_StaysGone()
    {
        // Migration pin for the bounded-height contract adoption (BgDiag_Razor's
        // bg-board-slot + .bg-diagram contain-fit default). The pre-contract
        // consumer glue must never come back:
        //   - display:contents on .bg-play-entry now *breaks* the contract (it
        //     dissolves the producer's flex column that gives the slot its
        //     definite post-flex height) — producer pitfall;
        //   - consumer-side max-height on .bg-diagram (and the cube
        //     max-height:none override) duplicated what is now the producer's
        //     inline contain-fit default;
        //   - the :has() cube fold-management opt-out existed only because no
        //     consumer CSS could contain-fit a board beside the radios — moving
        //     the radios out of the board region (into the action row) removed
        //     that need entirely.
        // Comments are stripped so only real declarations are checked.
        var css = File.ReadAllText(AppCssPath());
        var noComments = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

        Assert.DoesNotContain("display: contents", noComments);
        Assert.DoesNotContain("display:contents", noComments);
        Assert.DoesNotContain("max-height", noComments);
        Assert.DoesNotContain(":has(", noComments);
    }

    /// <summary>
    /// Absolute path to the server project's <c>wwwroot/app.css</c>, resolved from
    /// this test file's own compile-time location so it doesn't depend on the test
    /// runner's working directory or on the CSS being copied to output.
    /// </summary>
    private static string AppCssPath([CallerFilePath] string thisFile = "")
    {
        var testDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testDir, "..", "BgQuiz_Blazor", "wwwroot", "app.css"));
    }
}
