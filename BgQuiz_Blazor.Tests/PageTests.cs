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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
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
using MixPanelComponent = BgQuiz_Blazor.Client.Components.Pages.MixPanel;

namespace BgQuiz_Blazor.Tests;

public class PageTests : BunitContext
{
    /// <summary>
    /// The scriptable folder-access double every page render resolves as
    /// <see cref="IFolderAccess"/>. Tests drive picks by setting
    /// <see cref="FakeFolderAccess.NextPickOutcome"/> (and friends) before
    /// clicking the pick button — no JS module is involved in page tests.
    /// </summary>
    private readonly FakeFolderAccess _folderAccess = new();

    public PageTests()
    {
        // Home and Done inject the sessionStorage-backed QuizLiveMarker. It needs
        // only the framework IJSRuntime — which bUnit registers in Services — so
        // one fixture-wide registration serves every page render. The marker's
        // JS calls are handled per-test through JSInterop (Loose mode, or an
        // explicit Setup where a test drives a specific stored value).
        Services.AddScoped<QuizLiveMarker>();

        // Home injects IFolderAccess; Quiz and Done inject QuizStatsStore, whose
        // ctor pulls IFolderAccess + TimeProvider + PickedProblemFolder. Register
        // fixture-wide defaults for all of them — per-test helpers re-register
        // (last registration wins) when a test needs a scripted instance.
        Services.AddSingleton<IFolderAccess>(_folderAccess);
        Services.AddSingleton(TimeProvider.System);
        Services.AddScoped<PickedProblemFolder>();
        Services.AddScoped<QuizStatsStore>();

        // Home injects AppliedMix (and hosts MixPanel, whose restore path runs
        // under each test's JSInterop mode). The fixture-wide default is the
        // blank-mix holder; WithAppliedMix re-registers when a test needs a
        // committed mix in place.
        Services.AddScoped<AppliedMix>();
    }

    /// <summary>The sessionStorage key <see cref="QuizLiveMarker"/> reads/writes.</summary>
    private const string QuizLiveKey = "bgquiz.quizLive";

    private static Play BestPlay() => TestFixtures.MakePlay((8, 5), (8, 5));
    private static Play AltPlay() => TestFixtures.MakePlay((13, 11), (11, 8));

    /// <summary>
    /// A one-file <see cref="FolderPickOutcome"/> for scripting
    /// <see cref="FakeFolderAccess.NextPickOutcome"/> — the standard "the user
    /// picked a folder" payload for pick-flow tests.
    /// </summary>
    private static FolderPickOutcome OneFileOutcome(
        string folderName = "Corpus", string fileName = "match.xg",
        StatsSaveCapability capability = StatsSaveCapability.Enabled) =>
        new(Cancelled: false, folderName, [new PickedFile(fileName, [1, 2, 3])], capability);

    private QuizController WithController(params BgDecisionData[] items)
    {
        var fake = new FakeProblemSetSource(items);
        var controller = new QuizController((_, _) => fake, new FakeDecisionStatsSink(), TimeProvider.System);
        Services.AddSingleton(controller);
        return controller;
    }

    /// <summary>
    /// Register a <see cref="PickedProblemFolder"/> already holding one file so
    /// the rendered <c>Home</c> page's folder gate is satisfied — lets tests
    /// exercise the filter gate / Start click in isolation. The bytes are
    /// irrelevant: the quiz runs against the test's fake source, not the picked
    /// file. The default capability is the no-stats fallback so ordinary flow
    /// tests don't also render the stats-enabled notice.
    /// </summary>
    private PickedProblemFolder WithPickedFolder(
        string folderName = "Corpus", string fileName = "sample.xg",
        StatsSaveCapability capability = StatsSaveCapability.BrowserUnsupported)
    {
        var folder = new PickedProblemFolder();
        folder.Set(folderName, [new PickedFile(fileName, [1, 2, 3])], capability);
        Services.AddSingleton(folder);
        return folder;
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
    /// <see cref="WithAppliedFilter"/> / <see cref="WithPickedFolder"/>. Returns the
    /// holder so tests can assert the toggle after a checkbox interaction.
    /// </summary>
    private ShuffleOption WithShuffleOption(bool enabled = false)
    {
        var holder = new ShuffleOption();
        if (enabled) holder.Set(true);
        Services.AddSingleton(holder);
        return holder;
    }

    /// <summary>
    /// Register an <see cref="AppliedMix"/> for the rendered <c>Home</c> page,
    /// optionally pre-committed with <paramref name="mix"/> — simulating
    /// navigate-back (or a panel restore) with a mix the user applied earlier.
    /// Returns the holder so tests can assert commit/dirty transitions.
    /// </summary>
    private AppliedMix WithAppliedMix(QuizMix? mix = null)
    {
        var holder = new AppliedMix();
        if (mix is not null) holder.Apply(mix);
        Services.AddSingleton(holder);
        return holder;
    }

    /// <summary>A minimal weighted mix: 100% never-seen, deterministic order.</summary>
    private static QuizMix NeverSeenMix(int? quizLength = null) =>
        new([new QuizMixEntry(QuizCategory.NeverSeen, 100)], quizLength, randomOrder: false);

    /// <summary>A 50/50 never-seen / got-wrong mix, deterministic order.</summary>
    private static QuizMix SplitMix(int? quizLength = null) =>
        new([new QuizMixEntry(QuizCategory.NeverSeen, 50), new QuizMixEntry(QuizCategory.GotWrong, 50)],
            quizLength, randomOrder: false);

    /// <summary>
    /// Like <see cref="WithController"/> but over a scriptable stats sink, so
    /// weighted-start page tests can script stats availability
    /// (<c>CanBindStats</c> / <c>CurrentDocument</c>) before driving the UI.
    /// </summary>
    private QuizController WithWeighableController(
        out FakeDecisionStatsSink sink, params BgDecisionData[] items)
    {
        var fake = new FakeProblemSetSource(items);
        sink = new FakeDecisionStatsSink();
        var controller = new QuizController((_, _) => fake, sink, TimeProvider.System);
        Services.AddSingleton(controller);
        return controller;
    }

    // -----------------------------------------------------------------------
    //  Home.razor
    // -----------------------------------------------------------------------

    [Fact]
    public void Home_BeforeFilterApply_StartButtonDisabled()
    {
        // No file picked yet, so Start is disabled regardless of filters.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        // (empty PickedProblemFolder comes from the fixture default)
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
        // PickedProblemFolder, which survives in-app navigation, but Home is
        // re-instantiated on return. The summary must derive from the holder,
        // not a transient component field — the old field reset to null on
        // re-instantiation, blanking the summary while the file gate stayed
        // satisfied (summary blank + Start enabled = the reported desync).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder("resume"); // holder already populated, as after navigate-back
        WithAppliedFilter();
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();

        // Summary renders straight from the persisted holder, no pick handler run.
        Assert.Contains("resume", cut.Markup);
        Assert.Contains("1 problem file", cut.Markup);

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
        WithPickedFolder();
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
        var controller = new QuizController(
            (set, _) => { capturedPipeline = set; return fake; },
            new FakeDecisionStatsSink(), TimeProvider.System);
        Services.AddSingleton(controller);
        WithPickedFolder(); // satisfy the folder gate so Start is clickable
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
    public async Task Home_FolderPick_BuildsPickedFilesWithExtensionBearingNames()
    {
        // The pick button routes through IFolderAccess into the holder,
        // preserving each file's name *with* its extension — required by the
        // stream iterator's DecisionId stamping. This pins the picker → holder
        // half of the source wire; WasmUploadedProblemSetSourceTests pins the
        // other half (holder → source → controller).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _folderAccess.NextPickOutcome = OneFileOutcome("Corpus", "match.xg");

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        var folder = Services.GetRequiredService<PickedProblemFolder>();
        Assert.True(folder.HasFiles);
        var file = Assert.Single(folder.Files);
        Assert.Equal("match.xg", file.FileName);
        Assert.Equal([1, 2, 3], file.Bytes);
        Assert.Equal("Corpus", folder.FolderName);
    }

    [Fact]
    public async Task Home_FolderPickedAndFiltersApplied_EnablesStart()
    {
        // Both gates: a folder picked *and* filters applied — the migrated
        // pick → start wire test.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _folderAccess.NextPickOutcome = OneFileOutcome();

        var cut = Render<HomePage>();

        // Filters applied but no folder yet → still disabled.
        var fp = cut.FindComponent<FilterPanel>();
        await cut.InvokeAsync(() =>
            fp.Instance.OnFilterConfigChanged.InvokeAsync(new FilterConfig()));
        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.True(startBtn.HasAttribute("disabled"));

        // Pick a folder → both gates satisfied → enabled.
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.False(startBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_FolderPick_StatsEnabled_ShowsSaveNotice()
    {
        // Capability rung 1: FS-Access pick with write granted → the polite
        // stats-enabled notice names the stats file (from the constant).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: StatsSaveCapability.Enabled);

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.Contains(QuizStatsFile.FileName, cut.Markup);
        Assert.Contains("stats will be saved", cut.Markup);
        Assert.Contains("role=\"status\"", cut.Markup); // outcome, not an alert
    }

    [Fact]
    public async Task Home_FolderPick_BrowserUnsupported_ShowsNoStatsNotice()
    {
        // Capability rung 2: fallback mechanism → quiz-without-stats notice.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _folderAccess.NextPickOutcome =
            OneFileOutcome(capability: StatsSaveCapability.BrowserUnsupported);

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.Contains("can't save quiz stats", cut.Markup);
        Assert.DoesNotContain("stats will be saved", cut.Markup);
    }

    [Fact]
    public async Task Home_FolderPick_PermissionDenied_ShowsDeniedNotice()
    {
        // Capability rung 3: FS-Access pick but write declined → denied
        // variant; the quiz still runs (holder populated, gate satisfiable).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _folderAccess.NextPickOutcome =
            OneFileOutcome(capability: StatsSaveCapability.PermissionDenied);

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.Contains("declined write access", cut.Markup);
        var folder = Services.GetRequiredService<PickedProblemFolder>();
        Assert.True(folder.HasFiles);
    }

    [Fact]
    public async Task Home_FolderPick_Cancelled_ChangesNothingShowsNothing()
    {
        // A dismissed picker is an expected outcome: no holder change, no
        // notice — the user simply changed their mind.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _folderAccess.NextPickOutcome = FolderPickOutcome.CancelledOutcome;

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        var folder = Services.GetRequiredService<PickedProblemFolder>();
        Assert.False(folder.HasFiles);
        Assert.DoesNotContain("alert-warning", cut.Markup);
        Assert.DoesNotContain("alert-danger", cut.Markup);
    }

    [Fact]
    public async Task Home_FolderPick_EmptyFolder_ShowsEmptyNoticeKeepsGateDisabled()
    {
        // A completed pick with zero top-level problem files: polite outcome
        // notice, holder stays clear, Start stays disabled.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter(new FilterConfig()); // filter half satisfied
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _folderAccess.NextPickOutcome = new FolderPickOutcome(
            Cancelled: false, "Empty", [], StatsSaveCapability.Enabled);

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.Contains("No .xg / .xgp files found", cut.Markup);
        var folder = Services.GetRequiredService<PickedProblemFolder>();
        Assert.False(folder.HasFiles);
        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.True(startBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_FolderPick_Throws_ShowsPickErrorBanner()
    {
        // Unexpected browser failure (or a folder past the caps): the failure
        // idiom — assertive alert — and a cleared holder.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _folderAccess.PickException = new InvalidOperationException("boom from the browser");

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.Contains("Could not read the folder", cut.Markup);
        Assert.Contains("boom from the browser", cut.Markup);
        Assert.Contains("role=\"alert\"", cut.Markup);
        var folder = Services.GetRequiredService<PickedProblemFolder>();
        Assert.False(folder.HasFiles);
    }

    [Fact]
    public async Task Home_PickClick_WithoutFsAccess_TriggersFallbackPicker()
    {
        // The mechanism fork: no showDirectoryPicker → the same button opens
        // the hidden webkitdirectory input's picker instead (the pick itself
        // then arrives via the input's change event).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _folderAccess.SupportsDirectoryPicker = false;

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.Equal(1, _folderAccess.TriggerFallbackCallCount);
        var folder = Services.GetRequiredService<PickedProblemFolder>();
        Assert.False(folder.HasFiles); // nothing picked yet — change event pending
    }

    [Fact]
    public async Task Home_FallbackInputChange_CollectsFilesIntoHolder()
    {
        // The fallback landing: the hidden input's change event collects the
        // FileList through IFolderAccess; capability is forced to the no-stats
        // fallback by the interop layer (the fake mirrors that contract).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _folderAccess.NextCollectOutcome = new FolderPickOutcome(
            Cancelled: false, "FallbackDir",
            [new PickedFile("fb.xgp", [9, 9])], StatsSaveCapability.BrowserUnsupported);

        var cut = Render<HomePage>();
        await cut.Find("#problemFolderFallback").ChangeAsync(new ChangeEventArgs());

        var folder = Services.GetRequiredService<PickedProblemFolder>();
        Assert.True(folder.HasFiles);
        Assert.Equal("fb.xgp", Assert.Single(folder.Files).FileName);
        Assert.Equal(StatsSaveCapability.BrowserUnsupported, folder.Capability);
        Assert.Contains("can't save quiz stats", cut.Markup);
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
        WithPickedFolder("resume");
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
        WithPickedFolder();
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
        WithPickedFolder();
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
        WithPickedFolder();
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
        WithPickedFolder();
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
        // AppliedFilter / PickedProblemFolder's holder-first pattern.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        // (empty PickedProblemFolder comes from the fixture default)
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
        // (empty PickedProblemFolder comes from the fixture default)
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
        // (empty PickedProblemFolder comes from the fixture default)
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
        await controller.StartAsync(new FilterConfig(), QuizMix.Empty); // HasStarted true
        // (empty PickedProblemFolder comes from the fixture default)
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
        WithPickedFolder();
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
        WithPickedFolder();
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
    public async Task Home_ClearPickedFolder_RemovesSummaryDisablesStartClearsPickedSlotOnly()
    {
        // A4, folder edition: the Clear affordance beside the summary drops the
        // pick — the holder-derived summary disappears and the folder half of
        // the gate re-disables Start by construction. Start from a fully-armed
        // state (folder picked + filters applied) so the disable is
        // attributable to the clear, not to the filter half. Clearing reaches
        // only the JS picked slot (ClearPickedAsync) — a running quiz's active
        // stats context is bound at Start and must keep recording.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder("clear-me");
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();

        // Armed: summary shown, Start enabled.
        Assert.Contains("clear-me", cut.Markup);
        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.False(startBtn.HasAttribute("disabled"));

        // Clear → summary gone, Start disabled, picked slot cleared.
        var clear = cut.FindAll("button").First(b => b.TextContent.Trim() == "Clear");
        await clear.ClickAsync(new());

        Assert.DoesNotContain("clear-me", cut.Markup);
        startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.True(startBtn.HasAttribute("disabled"));
        Assert.Equal(1, _folderAccess.ClearPickedCallCount);
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
        var controller = new QuizController(
            (_, _) => shuffle.Enabled ? new ShuffledProblemSetSource(fake, seed: 42) : fake,
            new FakeDecisionStatsSink(), TimeProvider.System);
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
        WithPickedFolder();
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
        WithPickedFolder();
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
        // (empty PickedProblemFolder comes from the fixture default)
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        Assert.True(c.IsFinished);
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        Render<QuizPage>();

        Assert.EndsWith("/done", nav.Uri);
    }

    [Fact]
    public async Task Quiz_Active_RendersScorePanelAndButtons()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        var cut = Render<QuizPage>();

        Assert.Contains("Submitted", cut.Markup);
        Assert.Contains("Skipped", cut.Markup);
        Assert.Contains("Submit", cut.Markup);
        Assert.Contains("Skip", cut.Markup);
    }

    /// <summary>
    /// The problem-position indicator's visible text, whitespace-normalized
    /// (the markup spreads "Problem <strong>N</strong> of <strong>M</strong>"
    /// across source lines).
    /// </summary>
    private static string ProblemPositionText(IRenderedComponent<QuizPage> cut) =>
        Regex.Replace(cut.Find(".problem-position").TextContent, @"\s+", " ").Trim();

    [Fact]
    public async Task Quiz_Counter_RendersPositionOfTotal_AndAdvances()
    {
        var c = WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp")),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp")));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        var cut = Render<QuizPage>();
        Assert.Equal("Problem 1 of 2", ProblemPositionText(cut));

        await cut.InvokeAsync(() => c.SubmitPlay(BestPlay()));
        await cut.InvokeAsync(c.ContinueAsync);

        cut.WaitForAssertion(() => Assert.Equal("Problem 2 of 2", ProblemPositionText(cut)));
    }

    [Fact]
    public async Task Quiz_Counter_UnknownTotal_RendersPositionOnly()
    {
        // A source that declares no Count (streaming) must not fabricate a
        // total — the indicator degrades to the bare position.
        var fake = new FakeProblemSetSource(
            [TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay())], countKnown: false);
        var c = new QuizController((_, _) => fake, new FakeDecisionStatsSink(), TimeProvider.System);
        Services.AddSingleton(c);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        var cut = Render<QuizPage>();

        Assert.Equal("Problem 1", ProblemPositionText(cut));
    }

    [Fact]
    public async Task Quiz_Counter_WeightedQuiz_TotalIsCompositionDrawnCount()
    {
        // Weighted, the total comes from the composition's drawn count (1),
        // not the inner source's Count (2).
        var seen = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"));
        var unseen = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"));
        var c = WithWeighableController(out var sink, seen, unseen);
        sink.CanBindStats = true;
        sink.CurrentDocument = DecisionStatsDocument.Empty.Plus(
            new SubmittedPlay(seen.Id, BestPlay(), 0, 0.0, IsCorrect: true),
            TimeProvider.System);
        await c.StartAsync(new FilterConfig(), NeverSeenMix());
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<QuizPage>();

        Assert.Equal("Problem 1 of 1", ProblemPositionText(cut));
    }

    [Fact]
    public void ScorePanel_WithoutProblemNumber_OmitsPositionIndicator()
    {
        // Stats and Done render the shared panel without the counter params —
        // the indicator is opt-in per surface.
        var cut = Render<ScorePanelComponent>(ps => ps.Add(p => p.Score, QuizScore.Empty));

        Assert.Empty(cut.FindAll(".problem-position"));
    }

    // -----------------------------------------------------------------------
    //  Active-context stats notices (Quiz + Done)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Register a real <see cref="QuizStatsStore"/> driven into
    /// <paramref name="status"/> through its own lifecycle (no test-only state
    /// setter exists — the store's transitions are the contract), overriding
    /// the fixture's default store registration.
    /// </summary>
    private async Task<QuizStatsStore> WithStatsStoreInStatusAsync(QuizStatsStatus status)
    {
        var access = new FakeFolderAccess();
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", [new PickedFile("a.xgp", [1])], StatsSaveCapability.Enabled);
        var store = new QuizStatsStore(access, TimeProvider.System, folder);

        switch (status)
        {
            case QuizStatsStatus.LoadFailed:
                access.StatsJson = "corrupt";
                await store.BeginQuizAsync();
                break;
            case QuizStatsStatus.WriteFailed:
                access.WriteException = new JSException("write refused");
                await store.BeginQuizAsync();
                await store.RecordAsync(new SubmittedPlay(
                    new XgpDecisionId("x.xgp"), TestFixtures.MakePlay((8, 5)), 0, 0.0, true));
                break;
        }

        Assert.Equal(status, store.Status); // helper sanity: the drive worked
        Services.AddSingleton(store);
        return store;
    }

    [Fact]
    public async Task Quiz_StatsLoadFailed_ShowsPoliteUntouchedFileNotice()
    {
        // The quiz-runs-without-stats degrade: an unreadable stats file is an
        // outcome (role="status"), states the file was not changed, and the
        // quiz renders normally beneath it.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        await WithStatsStoreInStatusAsync(QuizStatsStatus.LoadFailed);

        var cut = Render<QuizPage>();

        Assert.Contains(QuizStatsFile.FileName, cut.Markup);
        Assert.Contains("couldn't be read", cut.Markup);
        Assert.Contains("has not been changed", cut.Markup);
        Assert.Contains("role=\"status\"", cut.Markup);
        Assert.Contains("Submit", cut.Markup); // quiz still fully functional
    }

    [Fact]
    public async Task Quiz_StatsWriteFailed_ShowsAssertiveAlert()
    {
        // A mid-quiz write failure is a failure (role="alert") but must not
        // block the quiz — the answering UI still renders.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        await WithStatsStoreInStatusAsync(QuizStatsStatus.WriteFailed);

        var cut = Render<QuizPage>();

        Assert.Contains("could not be saved", cut.Markup);
        Assert.Contains("role=\"alert\"", cut.Markup);
        Assert.Contains("Submit", cut.Markup);
    }

    [Fact]
    public async Task Quiz_StatsReady_ShowsNoStatsNotice()
    {
        // Over-trigger guard: a healthy (or Disabled) stats context renders no
        // stats notice at all.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        var cut = Render<QuizPage>();

        Assert.DoesNotContain("couldn't be read", cut.Markup);
        Assert.DoesNotContain("could not be saved", cut.Markup);
    }

    [Fact]
    public async Task Done_StatsWriteFailed_ShowsAlert()
    {
        // A failure on the FINAL Continue lands the user on Done without ever
        // seeing the in-quiz alert — Done must state it too.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync(); // exhausts → IsFinished
        await WithStatsStoreInStatusAsync(QuizStatsStatus.WriteFailed);
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<DonePage>();

        Assert.Contains("could not be saved", cut.Markup);
        Assert.Contains("role=\"alert\"", cut.Markup);
    }

    [Fact]
    public async Task Done_StatsLoadFailed_ShowsPoliteNotice()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();
        await WithStatsStoreInStatusAsync(QuizStatsStatus.LoadFailed);
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<DonePage>();

        Assert.Contains("couldn't be read", cut.Markup);
        Assert.Contains("role=\"status\"", cut.Markup);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        var cut = Render<QuizPage>();

        Assert.DoesNotContain("Restart", cut.Markup);
    }

    [Fact]
    public async Task Quiz_ReviewState_RestartButtonAbsent()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var cut = Render<QuizPage>();

        await cut.InvokeAsync(() => c.SubmitPlay(BestPlay()));
        Assert.NotNull(c.Review);

        Assert.DoesNotContain("Restart", cut.Markup);
    }

    [Fact]
    public async Task Quiz_SubmitButton_DisabledBeforePlayCompleted()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
                "Pick your folder",
                "Choose filters",
                "Answer the position",
                "Making a checker play",
                "Scoring",
                "Review the solution",
                "Stats and finishing",
                "Lifetime stats",
                "Things worth knowing",
            ],
            headings);
    }

    [Fact]
    public void Help_StatesFileCaps_SourcedFromTheConstantsThePickEnforces()
    {
        // SSOT: the folder pick enforces PickedFileLimits (in JsFolderAccess) and
        // Help documents the same constants, with the megabyte figure *derived*
        // from the byte cap rather than restated. Asserting against the constants
        // (not the literals "50" / "500") is what makes this fail if page prose
        // and enforced rule ever drift — which is the whole reason the caps were
        // hoisted off the enforcing type.
        WithController();

        var cut = Render<HelpPage>();

        Assert.Contains($"{PickedFileLimits.MaxFileCount} problem files", cut.Markup);
        Assert.Contains($"{PickedFileLimits.MaxFileMegabytes} MB", cut.Markup);
    }

    [Fact]
    public void Help_NamesTheStatsFile_FromTheConstantTheStoreWrites()
    {
        // Same page/rule discipline for the stats file: Help names it from
        // QuizStatsFile.FileName — the constant the store actually writes — so
        // documented name and written name cannot drift.
        WithController();

        var cut = Render<HelpPage>();

        Assert.Contains(QuizStatsFile.FileName, cut.Markup);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
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

    // -----------------------------------------------------------------------
    //  Stats-weighted mix: Home wiring, gate, refusal, notices
    // -----------------------------------------------------------------------

    /// <summary>The Start Quiz button on a rendered Home page.</summary>
    private static AngleSharp.Dom.IElement StartButton(IRenderedComponent<HomePage> cut) =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");

    [Fact]
    public async Task Home_MixAppliedInPanel_StartComposesWeightedQuiz()
    {
        // The full UI → QuizMix → start-composition wire: the mix panel's
        // Apply lands in the holder, Start hands it to the controller, and the
        // started quiz composes through the real MixedProblemSetSource
        // (LastComposition non-null is the composed-layer signature).
        var c = WithWeighableController(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        sink.CanBindStats = true;
        sink.CurrentDocument = DecisionStatsDocument.Empty;
        WithPickedFolder(capability: StatsSaveCapability.Enabled);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        WithAppliedMix();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();
        var panel = cut.FindComponent<MixPanelComponent>();
        await cut.InvokeAsync(() => panel.Instance.OnMixApplied.InvokeAsync(NeverSeenMix()));
        await StartButton(cut).ClickAsync(new());

        Assert.True(c.HasStarted);
        Assert.NotNull(c.LastComposition);
        Assert.Equal(1, c.LastComposition!.DrawnCount);
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        Assert.EndsWith("/quiz", nav.Uri);
    }

    [Fact]
    public async Task Home_DirtyMix_DisablesStart_UntilApplied()
    {
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder();
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        WithAppliedMix();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();
        var panel = cut.FindComponent<MixPanelComponent>();

        await cut.InvokeAsync(() => panel.Instance.OnMixDirty.InvokeAsync());
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains("Apply or reset the mix", cut.Markup);

        await cut.InvokeAsync(() => panel.Instance.OnMixApplied.InvokeAsync(QuizMix.Empty));
        Assert.False(StartButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_MixRestoredEvent_AdoptsIntoHolder_NoGating()
    {
        // The panel's restore path must adopt: holder and rendered rows agree
        // without a re-Apply, and the gate stays open (a restored mix is a
        // committed one, not an edit).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: StatsSaveCapability.Enabled);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        var holder = WithAppliedMix();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();
        var panel = cut.FindComponent<MixPanelComponent>();
        var mix = NeverSeenMix();
        await cut.InvokeAsync(() => panel.Instance.OnMixRestored.InvokeAsync(mix));

        Assert.Same(mix, holder.Current);
        Assert.False(holder.IsDirty);
        Assert.False(StartButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_WeightedStartWithoutStats_RefusedWithNotice_OverrideRunsPassthrough()
    {
        // The ratified no-stats ruling end-to-end: Start with a committed mix
        // and no stats capability refuses (no navigation, no quiz), renders
        // the actionable notice, and its one-click override starts THIS quiz
        // unweighted while the holder's mix survives for next time.
        var c = WithWeighableController(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        sink.CanBindStats = false;
        WithPickedFolder(); // fallback pick — BrowserUnsupported
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        var holder = WithAppliedMix(NeverSeenMix());
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();
        await StartButton(cut).ClickAsync(new());

        Assert.False(c.HasStarted);
        Assert.Contains("weighted mix can't be applied", cut.Markup);
        Assert.Contains("can't save stats in your browser", cut.Markup); // capability-derived reason
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        Assert.DoesNotContain("/quiz", nav.Uri);

        await cut.Find("#startWithoutMix").ClickAsync(new());

        Assert.True(c.HasStarted);
        Assert.Null(c.LastComposition);          // passthrough run
        Assert.False(holder.Current.IsPassthrough); // stored mix untouched
        Assert.EndsWith("/quiz", nav.Uri);
    }

    [Fact]
    public void Home_EarlyAdvisory_RendersOnlyForStatslessPickWithActiveMix()
    {
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(); // BrowserUnsupported — can't provide stats
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        WithAppliedMix(NeverSeenMix());
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();

        Assert.Contains("Start will offer to run without the mix", cut.Markup);
    }

    [Fact]
    public void Home_EarlyAdvisory_AbsentWithBlankMix()
    {
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder();
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        WithAppliedMix(); // blank
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();

        Assert.DoesNotContain("Start will offer to run without the mix", cut.Markup);
    }

    [Fact]
    public async Task Home_MixComposesToZero_MixAwareNotice_StaysHome()
    {
        // Parallel to the filtered-to-zero banner: a weighted start that drew
        // nothing stays on Home with wording that names the mix, not the
        // filters. One decision, already seen — a 100% never-seen mix draws 0.
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = WithWeighableController(out var sink, d);
        sink.CanBindStats = true;
        sink.CurrentDocument = DecisionStatsDocument.Empty.Plus(
            new SubmittedPlay(d.Id, BestPlay(), 0, 0.0, IsCorrect: true), TimeProvider.System);
        WithPickedFolder(capability: StatsSaveCapability.Enabled);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        WithAppliedMix(NeverSeenMix());
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();
        await StartButton(cut).ClickAsync(new());

        Assert.Contains("Your mix drew no problems", cut.Markup);
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        Assert.DoesNotContain("/quiz", nav.Uri);
    }

    [Fact]
    public async Task Home_ShuffleCheckbox_DisabledUnderActiveMix_ValueNeverRewritten()
    {
        // Disabled must not mean rewritten: the checkbox greys out while the
        // committed mix owns order, but ShuffleOption keeps the user's value,
        // so clearing the mix (apply blank) restores the prior preference.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder();
        WithAppliedFilter(new FilterConfig());
        var shuffle = WithShuffleOption(enabled: true);
        WithAppliedMix(NeverSeenMix());
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();

        Assert.True(cut.Find("#shuffleOrder").HasAttribute("disabled"));
        Assert.Contains("order comes from the mix", cut.Markup);
        Assert.True(shuffle.Enabled);

        var panel = cut.FindComponent<MixPanelComponent>();
        await cut.InvokeAsync(() => panel.Instance.OnMixApplied.InvokeAsync(QuizMix.Empty));

        Assert.False(cut.Find("#shuffleOrder").HasAttribute("disabled"));
        Assert.True(shuffle.Enabled); // untouched throughout
    }

    // -----------------------------------------------------------------------
    //  Stats-weighted mix: Quiz shortfall notice, Done refused Restart
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Quiz_MixUnderTarget_CompositionLeads_KeepsRequestedVsDrawn()
    {
        // A quiz length beyond reachable supply: 100% never-seen over one
        // unseen decision with QuizLength 5 targets 5, draws 1. The notice
        // leads with the quiz actually underway (Finding (M): the effective
        // composition must appear before any apportionment internals), then
        // keeps the asked-for-X-drew-Y explanation naming the dried-up
        // category.
        var c = WithWeighableController(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        sink.CanBindStats = true;
        sink.CurrentDocument = DecisionStatsDocument.Empty;
        await c.StartAsync(new FilterConfig(), NeverSeenMix(quizLength: 5));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<QuizPage>();

        var alert = cut.Find("div.alert-warning[role=alert]");
        Assert.Contains("Your quiz has 1 problem: 1 Never seen.", alert.TextContent);
        Assert.Contains("asked for 5 problems but only", cut.Markup);
        Assert.Contains("drew 1 of 5 requested", cut.Markup);
    }

    [Fact]
    public async Task Quiz_MixMetTarget_EntryShort_CompositionLeadsInternalsDemoted()
    {
        // Finding (M)'s exact shape at miniature scale: the target is met
        // (2 of 2) while got-wrong's empty pool redistributed its share to
        // never-seen. The notice must lead with the effective quiz and demote
        // the apportionment internals to a share explanation — no
        // "asked for … could be drawn" line, no bare requested-vs-drawn.
        var c = WithWeighableController(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp")),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp")),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("c.xgp")));
        sink.CanBindStats = true;
        sink.CurrentDocument = DecisionStatsDocument.Empty;
        await c.StartAsync(new FilterConfig(), SplitMix(quizLength: 2));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<QuizPage>();

        var alert = cut.Find("div.alert-warning[role=alert]");
        Assert.Contains("Your quiz has 2 problems: 2 Never seen + 0 Ever got wrong.", alert.TextContent);
        Assert.Contains("couldn't fill their share", alert.TextContent);
        Assert.Contains("Ever got wrong: filled 0 of its 50% share (1 requested)", alert.TextContent);
        Assert.DoesNotContain("asked for", cut.Markup);
        Assert.DoesNotContain("drew", cut.Markup);
    }

    [Fact]
    public async Task Quiz_CaplessMix_CompositionOnlyStatus_NoRequestedFraming()
    {
        // Capless, the percentages bind to no length: got-wrong's zero pool
        // means largest-remainder apportionment handed its union share to
        // never-seen — composition noise, not a shortfall (the old rendering
        // showed a misleading "drew 0 of 1 requested" alert here). The notice
        // reduces to a polite composition-only status line.
        var c = WithWeighableController(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp")),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp")),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("c.xgp")));
        sink.CanBindStats = true;
        sink.CurrentDocument = DecisionStatsDocument.Empty;
        await c.StartAsync(new FilterConfig(), SplitMix()); // no length
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<QuizPage>();

        var status = cut.Find("div.alert-info[role=status]");
        Assert.Contains("Your quiz has 3 problems: 3 Never seen + 0 Ever got wrong.", status.TextContent);
        Assert.DoesNotContain("requested", cut.Markup);
        Assert.DoesNotContain("ran short", cut.Markup);
        Assert.Empty(cut.FindAll("div.alert-warning"));
    }

    [Fact]
    public async Task Quiz_LengthBoundMixExactFill_NoNotice()
    {
        // Target met with every entry filling its own share: the quiz matches
        // the ask exactly, so no mix notice of any kind renders.
        var c = WithWeighableController(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        sink.CanBindStats = true;
        sink.CurrentDocument = DecisionStatsDocument.Empty;
        await c.StartAsync(new FilterConfig(), NeverSeenMix(quizLength: 1));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<QuizPage>();

        Assert.DoesNotContain("Your quiz has", cut.Markup);
        Assert.DoesNotContain("ran short", cut.Markup);
        Assert.DoesNotContain("requested", cut.Markup);
    }

    [Fact]
    public async Task Done_WeightedRestartWithoutStats_RefusedKeepsSummary_OverrideRestartsPassthrough()
    {
        // Restart re-attempts the stored mix; stats fell away in between. The
        // refusal must leave the summary standing (touches-no-state) and the
        // override must restart unweighted.
        var c = WithWeighableController(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        sink.CanBindStats = true;
        sink.CurrentDocument = DecisionStatsDocument.Empty;
        await c.StartAsync(new FilterConfig(), NeverSeenMix());
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync(); // exhausts the one-problem source → finished
        Assert.True(c.IsFinished);

        sink.CanBindStats = false; // e.g. the pick was cleared between quizzes
        WithPickedFolder(); // Done reads capability for the refusal reason
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<DonePage>();
        await cut.FindAll("button").First(b => b.TextContent.Contains("Restart with same filters"))
            .ClickAsync(new());

        Assert.Contains("weighted mix can't be applied", cut.Markup);
        Assert.True(c.IsFinished);                     // summary state survived the refusal
        Assert.Equal(1, c.Score.Total.Submitted);

        sink.CurrentDocument = null; // override ignores the mix, so stats stay unused
        await cut.Find("#restartWithoutMix").ClickAsync(new());

        Assert.True(c.HasStarted);
        Assert.False(c.IsFinished);                    // a fresh (passthrough) run began
        Assert.Null(c.LastComposition);
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        Assert.EndsWith("/quiz", nav.Uri);
    }

    // -----------------------------------------------------------------------
    //  Busy affordances: cursor + disabled controls while the controller
    //  runs a gated transition
    // -----------------------------------------------------------------------

    /// <summary>
    /// Like <see cref="WithController"/> but over a
    /// <see cref="GatedProblemSetSource"/>, so a page test can freeze the
    /// controller mid-transition (the busy window) and assert the rendered
    /// busy affordances before releasing it.
    /// </summary>
    private QuizController WithGatedController(
        out GatedProblemSetSource source, out FakeDecisionStatsSink sink,
        params BgDecisionData[] items)
    {
        var gated = new GatedProblemSetSource(items);
        source = gated;
        sink = new FakeDecisionStatsSink();
        var controller = new QuizController((_, _) => gated, sink, TimeProvider.System);
        Services.AddSingleton(controller);
        return controller;
    }

    [Fact]
    public async Task Home_StartPending_DisablesSetupFieldsetAndShowsBusyCursor()
    {
        // While Start's transition is in flight the whole setup surface —
        // the pick controls, both panels' Apply buttons (via the enclosing
        // fieldset), and Start itself — must read disabled, and the page
        // must carry the app-busy progress-cursor class. The controller's
        // gate yield is what lets this state render before the churn.
        WithGatedController(out var source, out _,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder();
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HomePage>();

        // Idle: the boundary exists but is not disabled, and no busy cursor.
        Assert.False(cut.Find("fieldset").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("div.app-busy"));

        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        var click = startBtn.ClickAsync(new()); // suspends at the gated first advance

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("fieldset").HasAttribute("disabled"));
            Assert.NotNull(cut.Find("div.app-busy"));
            Assert.True(cut.FindAll("button")
                .First(b => b.TextContent.Trim() == "Start Quiz").HasAttribute("disabled"));
        });

        source.ReleaseNext();
        await click; // completes: navigation to /quiz
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        Assert.EndsWith("/quiz", nav.Uri);
    }

    [Fact]
    public async Task Quiz_ContinuePending_DisablesTransitionButtonsAndShowsBusyCursor()
    {
        // Freeze the Continue inside the awaited stats fold (Review still
        // set, so the review branch keeps rendering deterministically) and
        // assert Continue/Redo disable and the busy cursor shows; Show stats
        // stays enabled (navigation only).
        var controller = WithGatedController(out var source, out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        source.ReleaseNext();
        await controller.StartAsync(new FilterConfig(), QuizMix.Empty);
        controller.SubmitPlay(BestPlay());
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<QuizPage>();
        Assert.Empty(cut.FindAll("div.app-busy"));
        var continueBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Continue");
        Assert.False(continueBtn.HasAttribute("disabled"));

        var foldGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sink.RecordGate = foldGate.Task;

        var click = continueBtn.ClickAsync(new()); // suspended inside the fold

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("div.app-busy"));
            Assert.True(cut.FindAll("button")
                .First(b => b.TextContent.Trim() == "Continue").HasAttribute("disabled"));
            Assert.True(cut.FindAll("button")
                .First(b => b.TextContent.Trim() == "Redo").HasAttribute("disabled"));
            Assert.False(cut.FindAll("button")
                .First(b => b.TextContent.Trim() == "Show stats").HasAttribute("disabled"));
        });

        foldGate.SetResult();
        source.ReleaseNext();
        await click;

        // Transition done: busy affordances clear, the next problem is up.
        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("div.app-busy"));
            Assert.NotNull(controller.Current);
        });
    }

    [Fact]
    public async Task Done_RestartPending_DisablesRestartAndShowsBusyCursor()
    {
        var controller = WithGatedController(out var source, out _,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        source.ReleaseNext();
        await controller.StartAsync(new FilterConfig(), QuizMix.Empty);
        WithPickedFolder();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<DonePage>();
        var restartBtn = cut.FindAll("button")
            .First(b => b.TextContent.Contains("Restart with same filters"));
        Assert.False(restartBtn.HasAttribute("disabled"));

        var click = restartBtn.ClickAsync(new()); // suspends at the gated first advance

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("div.app-busy"));
            Assert.True(cut.FindAll("button")
                .First(b => b.TextContent.Contains("Restart with same filters"))
                .HasAttribute("disabled"));
        });

        source.ReleaseNext();
        await click;
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        Assert.EndsWith("/quiz", nav.Uri);
    }
}
