using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BackgammonDiagram_Lib;
using BackgammonDiagram_Lib.Rendering;
using BgDataTypes_Lib;
using BgDiag_Razor.Components;
using BgGame_Lib;
using BgQuiz_Blazor.Client;
using BgQuiz_Blazor.Client.Quiz;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using XgFilter_Lib.Enums;
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
using SettingsPage = BgQuiz_Blazor.Client.Components.Pages.Settings;
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
        // Loose JSInterop, for the whole fixture and stated in exactly this one
        // place. Every page render reaches localStorage on the way in —
        // QuizSettings hydrates from Home, Quiz and Settings alike — so strict
        // mode would make an unrelated page test fail on a storage read it has
        // no opinion about. A test that cares what is stored still says so with
        // its own Setup, which takes precedence; no test in this fixture asserts
        // on an *unhandled* call.
        JSInterop.Mode = JSRuntimeMode.Loose;

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

        // Home also injects SavedFiltersStore (deps: IFolderAccess +
        // PickedProblemFolder, both above). Default construction is fine — the
        // store is Disabled until a pick loads it, so ordinary flow tests that
        // don't drive a saved-filters gesture render no panel.
        Services.AddScoped<SavedFiltersStore>();

        // Home injects both halves of the mix state: AppliedMix (the committed
        // holder — fixture default is blank; WithAppliedMix re-registers when a
        // test needs a committed mix in place) and MixDraft (the app-scoped
        // edit state MixPanel views; its hydration runs under each test's
        // JSInterop mode, resolving the bUnit IJSRuntime from the container).
        // The start gate derives from the pair — draft builds and
        // content-equals the commitment — so tests arm it through the panel UI
        // or via WithAppliedMix's staged localStorage, never a stored flag.
        Services.AddScoped<AppliedMix>();
        Services.AddScoped<MixDraft>();

        // Home, Quiz and Settings all inject QuizSettings. Scoped, as in
        // Program.cs, so one hydration serves every render in a test and the
        // Settings page's writes are visible to a Quiz page rendered after it —
        // which is the app-scoped behavior the side settings depend on.
        Services.AddScoped<QuizSettings>();

        // Quiz injects MixNoticeDismissal (its composition notice checks it before
        // rendering). Scoped, as in Program.cs, so a test that re-renders the page
        // sees the same dismissal the first instance recorded — the navigate-back
        // case the holder exists for.
        Services.AddScoped<MixNoticeDismissal>();
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
    /// <param name="pickGeneration">
    /// The pick the config is stamped as applied for — what Home's Apply Mix
    /// gate compares against the live <see cref="PickedProblemFolder.PickGeneration"/>.
    /// The default matches the generation <see cref="WithPickedFolder"/> leaves
    /// behind (one <c>Set</c> ⇒ 1), so the common "already set up" fixture is
    /// coherent; a test probing the gate passes a mismatching value deliberately.
    /// </param>
    private void WithAppliedFilter(FilterConfig? applied = null, int pickGeneration = 1)
    {
        var holder = new AppliedFilter();
        if (applied is not null) holder.Set(applied, pickGeneration);
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
    /// navigate-back with a mix the user applied earlier. Returns the holder so
    /// tests can assert commit transitions.
    /// <para>
    /// A non-null <paramref name="mix"/> also stages the localStorage blob to
    /// match, because that is the app's invariant: every commit persists
    /// (committed-only persistence via <see cref="MixDraft.PersistAsync"/>), so
    /// a committed mix always has its content in storage. The rendered panel's
    /// hydration then fills the draft with the same content and the derived
    /// gate reads clean — pre-committing <i>without</i> the blob would fabricate
    /// a state no user can reach (committed mix, divergent draft → Start
    /// gated). Tests probing divergence stage their own blob afterwards.
    /// </para>
    /// </summary>
    private AppliedMix WithAppliedMix(QuizMix? mix = null)
    {
        var holder = new AppliedMix();
        if (mix is not null)
        {
            holder.Apply(mix);
            JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
                .SetResult(mix.ToJson());
        }
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
    public async Task Home_ProgressiveDisclosure_HidesSetupUntilFolderPicked()
    {
        // Task S: before a folder with problem files is picked there is nothing
        // to filter, weight, or start, so the FilterPanel, MixPanel, the shuffle
        // toggle, and Start are not rendered at all — only the folder-pick
        // controls show. A completed pick reveals the whole setup surface. This
        // also makes the old "Start disabled before a folder is picked" true by
        // construction: the button doesn't exist yet.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome();

        var cut = Render<HomePage>();

        // Pre-pick: the pick button is present; everything downstream is hidden.
        Assert.NotNull(cut.Find("#pickProblemFolder"));
        Assert.Empty(cut.FindComponents<FilterPanel>());
        Assert.Empty(cut.FindComponents<MixPanelComponent>());
        Assert.Empty(cut.FindAll("#shuffleOrder"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Start Quiz");

        // Pick a folder with files → the setup surface appears.
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.Single(cut.FindComponents<FilterPanel>());
        Assert.Single(cut.FindComponents<MixPanelComponent>());
        Assert.NotEmpty(cut.FindAll("#shuffleOrder"));
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Start Quiz");
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
        // pick → start wire test. Progressive disclosure means the FilterPanel
        // only exists after the pick, so the order is pick-then-apply.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome();

        var cut = Render<HomePage>();

        // Pick a folder → the setup surface (with FilterPanel and Start)
        // appears, but Start stays disabled until filters are applied.
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.True(startBtn.HasAttribute("disabled"));

        // Apply filters → both gates satisfied → enabled.
        var fp = cut.FindComponent<FilterPanel>();
        await cut.InvokeAsync(() =>
            fp.Instance.OnFilterConfigChanged.InvokeAsync(new FilterConfig()));

        startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.False(startBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_ApplyFilters_ShowsMatchCount()
    {
        // Task U: applying filters shows how many decisions matched, sourced
        // from the controller's SummarizeMatchesAsync over the source's items (the
        // fake yields its whole list; production filters). Two items → "2".
        WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder();
        WithAppliedFilter();
        WithShuffleOption();
        WithAppliedMix();

        var cut = Render<HomePage>();
        var fp = cut.FindComponent<FilterPanel>();
        await cut.InvokeAsync(() =>
            fp.Instance.OnFilterConfigChanged.InvokeAsync(new FilterConfig()));

        Assert.Contains("<strong>2</strong>", cut.Markup);
        Assert.Contains("decisions match your filters", cut.Markup);
    }

    [Fact]
    public async Task Home_ApplyFilters_SingleMatch_UsesSingularWording()
    {
        // Pluralization pin: exactly one match reads "decision matches", not
        // "decisions match".
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder();
        WithAppliedFilter();
        WithShuffleOption();
        WithAppliedMix();

        var cut = Render<HomePage>();
        var fp = cut.FindComponent<FilterPanel>();
        await cut.InvokeAsync(() =>
            fp.Instance.OnFilterConfigChanged.InvokeAsync(new FilterConfig()));

        Assert.Contains("<strong>1</strong>", cut.Markup);
        Assert.Contains("decision matches your filters", cut.Markup);
        Assert.DoesNotContain("decisions match your filters", cut.Markup);
    }

    [Fact]
    public async Task Home_ApplyFilters_CommittedMix_CountCarriesThePoolCaveat()
    {
        // The count is filter-only (SummarizeMatchesAsync composes with QuizMix.Empty),
        // so with a mix committed the number is the pool the quiz is *drawn from* —
        // the quiz itself can be far smaller. The caveat says so beside the number,
        // inside the same role="status" region, and the count stays pool-only (both
        // decisions here matched, so it still reads 2).
        WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: StatsSaveCapability.Enabled);
        WithAppliedFilter();
        WithShuffleOption();
        WithAppliedMix(NeverSeenMix()); // committed, non-passthrough

        var cut = Render<HomePage>();
        await ApplyFiltersAsync(cut);

        var count = cut.FindAll("div[role=status]")
                       .First(d => d.TextContent.Contains("decisions match your filters"));
        Assert.Contains("2", count.TextContent);
        Assert.Contains("draws the quiz from these matches", count.TextContent);
        Assert.Contains("can be much smaller", count.TextContent);
    }

    [Fact]
    public async Task Home_ApplyFilters_NoMix_CountCarriesNoCaveat()
    {
        // Passthrough (the default): the quiz presents what the filters matched, so
        // there is nothing to qualify — the caveat must not appear.
        WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: StatsSaveCapability.Enabled);
        WithAppliedFilter();
        WithShuffleOption();
        WithAppliedMix(); // blank

        var cut = Render<HomePage>();
        await ApplyFiltersAsync(cut);

        Assert.Contains("decisions match your filters", cut.Markup);
        Assert.DoesNotContain("draws the quiz from these matches", cut.Markup);
    }

    [Fact]
    public async Task Home_ApplyFilters_BreaksThePoolDownByAnswerType()
    {
        // Issue #35: the count line is joined by the answer-type breakdown, so a
        // user can see what their collection is made of before starting. The
        // pool here is deliberately lopsided — two checker plays and one
        // double/take — because the interesting reading is the categories that
        // are *missing*, and every bucket must be on screen for that to be
        // legible. Bucket labels are AnswerTypeDisplay's, read from the class so
        // this test pins the wiring, not the copy (the e2e suite pins the copy).
        WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.CubeDecision(noDoubleEquity: 0.5, doubleTakeEquity: 0.7));
        WithPickedFolder();
        WithAppliedFilter();
        WithShuffleOption();
        WithAppliedMix();

        var cut = Render<HomePage>();
        await ApplyFiltersAsync(cut);

        var region = MatchSummaryRegion(cut);
        Assert.Contains("By answer type", region.TextContent);

        // The two populated buckets, and — the point of the feature — the three
        // empty ones, present and reading zero rather than quietly dropped.
        var expected = AnswerTypeDisplay.Buckets(new AnswerTypeDistribution(
            CheckerPlays: 2, NoDoubleTake: 0, TooGood: 0, DoubleTake: 1, DoublePass: 0));
        Assert.Equal(
            expected.Select(b => $"{b.Label}: {b.Count}"),
            region.QuerySelectorAll("li").Select(li => Normalize(li.TextContent)));
    }

    [Fact]
    public async Task Home_ApplyFilters_CountAndBreakdownComeFromOneDistribution()
    {
        // The wiring guarantee behind replacing the int-returning count: the
        // number the user reads and the buckets under it are two renderings of
        // one AnswerTypeDistribution from one enumeration, so they cannot
        // disagree. Asserted the way a user could check it — the buckets sum to
        // the count — which fails the moment a second computation creeps in.
        WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.CubeDecision(noDoubleEquity: 0.8, doubleTakeEquity: 0.7),
            TestFixtures.CubeDecision(noDoubleEquity: 1.2, doubleTakeEquity: 1.5),
            TestFixtures.CubeDecision(noDoubleEquity: 0.5, doubleTakeEquity: 1.5));
        WithPickedFolder();
        WithAppliedFilter();
        WithShuffleOption();
        WithAppliedMix();

        var cut = Render<HomePage>();
        await ApplyFiltersAsync(cut);

        var region = MatchSummaryRegion(cut);
        var renderedCount = int.Parse(region.QuerySelector("strong")!.TextContent);
        var buckets = region.QuerySelectorAll("li")
                            .Select(li => int.Parse(li.QuerySelector("strong")!.TextContent))
                            .ToList();

        Assert.Equal(4, renderedCount);
        Assert.Equal(5, buckets.Count);
        Assert.Equal(renderedCount, buckets.Sum());
        Assert.Contains("decisions match your filters", region.TextContent);
    }

    [Fact]
    public async Task Home_ApplyFilters_NoMatches_ShowsTheCountWithoutABreakdown()
    {
        // The one case the breakdown is suppressed: an empty pool has no
        // make-up to describe, and five zeros under "0 decisions match" would be
        // noise exactly where the page should be quiet. Distinct from a zero
        // *bucket* inside a real pool, which always renders (test above).
        WithController();
        WithPickedFolder();
        WithAppliedFilter();
        WithShuffleOption();
        WithAppliedMix();

        var cut = Render<HomePage>();
        await ApplyFiltersAsync(cut);

        var region = MatchSummaryRegion(cut);
        Assert.Contains("0 decisions match your filters", Normalize(region.TextContent));
        Assert.DoesNotContain("By answer type", region.TextContent);
    }

    [Fact]
    public async Task Home_FiltersDirty_ClearsMatchCount()
    {
        // Editing any filter control invalidates the shown count — it described
        // the now-abandoned config — so the notice disappears until re-Apply.
        // The breakdown is part of that same summary and goes with it: a stale
        // make-up of an abandoned pool is exactly as wrong as a stale number.
        WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder();
        WithAppliedFilter();
        WithShuffleOption();
        WithAppliedMix();

        var cut = Render<HomePage>();
        var fp = cut.FindComponent<FilterPanel>();
        await cut.InvokeAsync(() =>
            fp.Instance.OnFilterConfigChanged.InvokeAsync(new FilterConfig()));
        Assert.Contains("decisions match your filters", cut.Markup);
        Assert.Contains("By answer type", cut.Markup);

        await cut.InvokeAsync(() => fp.Instance.OnFilterDirty.InvokeAsync());

        Assert.DoesNotContain("decisions match your filters", cut.Markup);
        Assert.DoesNotContain("By answer type", cut.Markup);
    }

    /// <summary>
    /// Home's applied-filter summary region — the one polite <c>role="status"</c>
    /// block carrying the match count, its mix caveat, and the answer-type
    /// breakdown. Scoping assertions to it is what proves the breakdown is
    /// announced <i>with</i> the count rather than merely present on the page.
    /// </summary>
    private static AngleSharp.Dom.IElement MatchSummaryRegion(IRenderedComponent<HomePage> cut) =>
        cut.FindAll("div[role=status]")
           .First(d => d.TextContent.Contains("match your filters")
                    || d.TextContent.Contains("matches your filters"));

    /// <summary>
    /// Collapse the whitespace Razor leaves between an element's text and its
    /// nested markup, so a rendered list item can be compared to the string a
    /// reader sees.
    /// </summary>
    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public async Task Home_FolderPick_StatsEnabled_ShowsSaveNotice()
    {
        // Capability rung 1: FS-Access pick with write granted → the polite
        // stats-enabled notice names the stats file (from the constant).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: StatsSaveCapability.Enabled);

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.Contains(QuizStatsFile.FileName, cut.Markup);
        Assert.Contains("stats will be saved", cut.Markup);
        Assert.Contains("role=\"status\"", cut.Markup); // outcome, not an alert
        // A completed pick that held a folder is neither of the no-folder
        // outcomes, and write access was granted — neither notice belongs here.
        Assert.DoesNotContain("No folder is picked", cut.Markup);
        // The consequence clause is absent on two independent counts now: write
        // access was granted (so the PermissionDenied notice is off), and a
        // folder is held (so the pick guidance that renders the same clause has
        // hidden — finding (AB)). Either alone would satisfy this; the pins for
        // the second live in the guidance tests below.
        Assert.DoesNotContain(FolderPickDisplay.WriteAccessConsequence, cut.Markup);
    }

    [Fact]
    public async Task Home_FolderPick_BrowserUnsupported_ShowsNoStatsNotice()
    {
        // Capability rung 2: fallback mechanism → quiz-without-stats notice.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
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
        _folderAccess.NextPickOutcome =
            OneFileOutcome(capability: StatsSaveCapability.PermissionDenied);

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        // Scoped to the notice element, not cut.Markup: the pre-pick guidance
        // renders the very same consequence constant (that is the point of the
        // SSOT), so a whole-markup Contains could be satisfied by the wrong
        // surface. Pairing with the premise makes the sentence discriminating.
        // (Under (AB) the guidance is in fact hidden here — a folder is held —
        // but the scoping is what makes this assert say what it means.)
        var notice = cut.Find(".alert.alert-warning");
        Assert.Contains(FolderPickDisplay.WriteAccessNotGranted, notice.TextContent);
        // Finding (AA): the notice says what that costs — not "stats won't be
        // saved". And it never claims the user declined: this rung is also
        // reached by a readwrite request that auto-denied with no prompt shown.
        Assert.Contains(FolderPickDisplay.WriteAccessConsequence, notice.TextContent);
        Assert.DoesNotContain("declined", notice.TextContent);
        var folder = Services.GetRequiredService<PickedProblemFolder>();
        Assert.True(folder.HasFiles);
    }

    [Fact]
    public async Task Home_FolderPick_Cancelled_ShowsNeutralNotice()
    {
        // Finding (AA), reversing the earlier silence: a pick that ended holding
        // no folder now says so. Cancellation covers both a dismissed picker and
        // a declined view-files permission, so the notice must be neutral —
        // polite role="status", never the assertive error banner.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = FolderPickOutcome.CancelledOutcome;

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        var folder = Services.GetRequiredService<PickedProblemFolder>();
        Assert.False(folder.HasFiles); // the holder is still untouched
        Assert.Contains("No folder is picked", cut.Markup);
        Assert.Contains("role=\"status\"", cut.Markup);
        Assert.DoesNotContain("alert-danger", cut.Markup);
    }

    [Fact]
    public async Task Home_FolderPick_Cancelled_NoticeClearsOnNextPick()
    {
        // The notice is per-attempt: ClearPickNotices runs at pick *start*, so a
        // following successful pick leaves no stale "no folder is picked" line
        // beside the folder it just picked.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = FolderPickOutcome.CancelledOutcome;

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        Assert.Contains("No folder is picked", cut.Markup);

        _folderAccess.NextPickOutcome = OneFileOutcome();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.DoesNotContain("No folder is picked", cut.Markup);
        Assert.True(Services.GetRequiredService<PickedProblemFolder>().HasFiles);
    }

    [Fact]
    public async Task Home_PickGesture_ResetsTheSetupBeforeThePickerOpens()
    {
        // The deliverable's actual claim: the reset fires at the *click*, not on
        // a successful outcome. Observed from inside the fake's PickFolderAsync —
        // the instant the real implementation raises the OS picker and the
        // browser's permission prompts — so what this asserts is precisely what
        // the user would be looking at behind those prompts. Before the move, all
        // three reads were the previous setup's: picker and prompts played out
        // over a fully-populated screen.
        WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        var mix = WithAppliedMix();
        _folderAccess.NextPickOutcome = OneFileOutcome("First", "first.xg");

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        await ApplyFiltersAsync(cut);
        await ApplyMixThroughPanelAsync(cut);

        var folder = Services.GetRequiredService<PickedProblemFolder>();
        var filter = Services.GetRequiredService<AppliedFilter>();
        Assert.True(folder.HasFiles);          // fully armed…
        Assert.True(filter.IsApplied);
        Assert.False(mix.Current.IsPassthrough);

        // …then a second pick gesture, sampled at the picker.
        bool? heldAtPicker = null, appliedAtPicker = null, mixedAtPicker = null;
        _folderAccess.OnPickCalled = () =>
        {
            heldAtPicker = folder.HasFiles;
            appliedAtPicker = filter.IsApplied;
            mixedAtPicker = !mix.Current.IsPassthrough;
        };
        _folderAccess.NextPickOutcome = OneFileOutcome("Second", "second.xg");
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.False(heldAtPicker);            // the picker opened over the initial screen
        Assert.False(appliedAtPicker);
        Assert.False(mixedAtPicker);
    }

    [Fact]
    public async Task Home_CancelledRePick_EndsTheHeldSetupAndLosesTheFolder()
    {
        // The settled semantic, and the flip from the old behavior: choosing a
        // folder ends the current setup at the click, so a *cancelled* re-pick
        // lands on the initial screen — guidance up, cancelled-pick notice — with
        // the previously held folder deliberately gone. No snapshot, no restore.
        // Before the move a cancelled re-pick left the whole previous setup
        // standing, which is what made the reset look like it had never run.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        var mix = WithAppliedMix();
        _folderAccess.FiltersJson = SavedFiltersJson();
        _folderAccess.NextPickOutcome = OneFileOutcome("Held", "held.xg");

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        await ApplyFiltersAsync(cut);
        await ApplyMixThroughPanelAsync(cut);
        Assert.Contains("Held", cut.Markup);
        Assert.Contains("Race", cut.Markup); // the folder's saved filter

        _folderAccess.NextPickOutcome = FolderPickOutcome.CancelledOutcome;
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        // The held folder is gone, along with everything scoped to it…
        var folder = Services.GetRequiredService<PickedProblemFolder>();
        Assert.False(folder.HasFiles);
        Assert.DoesNotContain("Held", cut.Markup);
        Assert.DoesNotContain("Race", cut.Markup);
        Assert.Equal(0, Services.GetRequiredService<SavedFiltersStore>().Filters.Count);
        Assert.False(Services.GetRequiredService<AppliedFilter>().IsApplied);
        Assert.True(mix.Current.IsPassthrough);
        // Both mix halves ended with the setup: the discarded draft is blank,
        // so it agrees with the reset holder — no derived gate survives.
        Assert.True(Services.GetRequiredService<MixDraft>().Matches(mix.Current));
        // The picked slot too. Derivation: EndCurrentSetupAsync clears it once
        // per gesture that ends a setup, and this test makes two pick gestures
        // (the held pick, then the cancelled one) — so 2, not 1.
        Assert.Equal(2, _folderAccess.ClearPickedCallCount);

        // …and the screen is the initial one, with the cancellation accounted for.
        Assert.Contains("Your browser will ask about the selected folder", cut.Markup);
        Assert.Contains("No folder is picked", cut.Markup);
    }

    [Fact]
    public async Task Home_FallbackPick_Dismissed_ShowsCancelledNotice()
    {
        // The fallback mechanism fires no change event on a dismissal, so without
        // the input's cancel event the gesture would end on the screen the click
        // just emptied with nothing said. Pins the binding (bUnit can only do
        // that — whether a given browser fires `cancel` is the browser's half).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.SupportsDirectoryPicker = false;
        _folderAccess.NextCollectOutcome = new FolderPickOutcome(
            Cancelled: false, "FallbackDir",
            [new PickedFile("fb.xgp", [9, 9])], StatsSaveCapability.BrowserUnsupported);

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        await cut.Find("#problemFolderFallback").ChangeAsync(new ChangeEventArgs());
        Assert.Contains("FallbackDir", cut.Markup);

        // Re-pick, then dismiss the native picker: the input reports it here.
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        Assert.DoesNotContain("FallbackDir", cut.Markup); // the click already ended the setup
        await cut.Find("#problemFolderFallback").TriggerEventAsync("oncancel", EventArgs.Empty);

        Assert.Contains("No folder is picked", cut.Markup);
        Assert.DoesNotContain("alert-danger", cut.Markup); // an outcome, never an error
    }

    [Fact]
    public async Task Home_FolderPick_EmptyFolder_ShowsEmptyNoticeKeepsSetupHidden()
    {
        // A completed pick with zero top-level problem files: polite outcome
        // notice, holder stays clear. With no files held, progressive disclosure
        // keeps the whole setup surface (incl. Start) hidden — the gate is moot.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter(new FilterConfig()); // filter half satisfied
        WithShuffleOption();
        _folderAccess.NextPickOutcome = new FolderPickOutcome(
            Cancelled: false, "Empty", [], StatsSaveCapability.Enabled);

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.Contains("No .xg / .xgp files found", cut.Markup);
        // The two no-folder outcomes stay distinct: this pick completed and held
        // a folder, so the cancelled-pick notice must not also fire.
        Assert.DoesNotContain("No folder is picked", cut.Markup);
        var folder = Services.GetRequiredService<PickedProblemFolder>();
        Assert.False(folder.HasFiles);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Start Quiz");
    }

    [Fact]
    public async Task Home_FolderPick_Throws_ShowsPickErrorBanner()
    {
        // Unexpected browser failure (or a folder past the caps): the failure
        // idiom — assertive alert — and a cleared holder.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
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
        _folderAccess.SupportsDirectoryPicker = false;

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.Equal(1, _folderAccess.TriggerFallbackCallCount);
        var folder = Services.GetRequiredService<PickedProblemFolder>();
        Assert.False(folder.HasFiles); // nothing picked yet — change event pending
    }

    [Fact]
    public void Home_InitialRender_ShowsTwoStepPermissionGuidance()
    {
        // Task V + findings (AA)/(AB): on an FS-Access-capable browser Home shows
        // in-page guidance for BOTH of that mechanism's (easily-missed)
        // permission prompts, saying what declining each costs — step 1 required,
        // step 2 optional with the shared write-access consequence. (AB) moved it
        // to *initial render*: it is only useful read before the gesture that
        // raises the prompts, so no click is needed to observe it. The probe is
        // awaited in OnInitializedAsync, hence WaitForAssertion — the note lands
        // on the render pass after it resolves.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();

        var cut = Render<HomePage>();

        cut.WaitForAssertion(() =>
        {
            // The lead-in promises no count of prompts: the readwrite request
            // auto-denies on some Chromium versions, and then only one prompt
            // ever appears.
            Assert.Contains("Your browser will ask about the selected folder", cut.Markup);
            Assert.DoesNotContain("ask you twice", cut.Markup);

            var steps = cut.FindAll("ol li");
            Assert.Equal(2, steps.Count);
            // Step 1: load-bearing — nothing works without it.
            Assert.Contains("view the selected folder's files", steps[0].TextContent);
            Assert.Contains("no folder is picked at all", steps[0].TextContent);
            // Step 2: optional, and what it costs — from the shared constant, so
            // this and the PermissionDenied notice cannot drift apart. Scoped to
            // the step element precisely because that notice renders the same
            // clause; a whole-markup Contains would not discriminate.
            Assert.Contains("save files into the folder", steps[1].TextContent);
            Assert.Contains(FolderPickDisplay.WriteAccessConsequence, steps[1].TextContent);
        });
        // No gesture was needed to surface it.
        Assert.Equal(0, _folderAccess.TriggerFallbackCallCount);
    }

    [Fact]
    public async Task Home_PickHoldsFolder_HidesPermissionGuidance()
    {
        // The other end of (AB)'s visibility window: once a folder is held the
        // guidance has done its job, and leaving it beside the populated summary
        // would be stale noise. Clearing the pick brings it back — the window is
        // "no folder held", not "not yet picked once".
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome();

        var cut = Render<HomePage>();
        cut.WaitForAssertion(() =>
            Assert.Contains("Your browser will ask about the selected folder", cut.Markup));

        await cut.Find("#pickProblemFolder").ClickAsync(new());
        Assert.True(Services.GetRequiredService<PickedProblemFolder>().HasFiles);
        Assert.DoesNotContain("Your browser will ask about the selected folder", cut.Markup);

        await cut.FindAll("button").First(b => b.TextContent.Trim() == "Clear").ClickAsync(new());
        Assert.Contains("Your browser will ask about the selected folder", cut.Markup);
    }

    [Fact]
    public async Task Home_CancelledPick_KeepsPermissionGuidance()
    {
        // A cancelled pick leaves no folder held — including when the cause was a
        // declined view-files permission — so the guidance is still the next
        // thing the user needs and must NOT hide. This is the case that makes
        // the gate "no folder held" rather than "the pick has returned".
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = FolderPickOutcome.CancelledOutcome;

        var cut = Render<HomePage>();
        cut.WaitForAssertion(() =>
            Assert.Contains("Your browser will ask about the selected folder", cut.Markup));

        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.False(Services.GetRequiredService<PickedProblemFolder>().HasFiles);
        Assert.Contains("Your browser will ask about the selected folder", cut.Markup);
        Assert.Contains("No folder is picked", cut.Markup); // …alongside the cancelled notice
    }

    [Fact]
    public async Task Home_NoFsAccessBrowser_ShowsNoPermissionGuidance()
    {
        // Over-trigger guard, and the real fallback-mechanism pin: a browser
        // without showDirectoryPicker raises no permission prompt at all, so
        // neither step of the guidance ever shows — not on load, and not around
        // the pick. (AB) made the gate browser *capability*, so this — not the
        // e2e fallback scenario, which runs in an FS-Access-capable Chromium —
        // is what holds the note to FS-Access.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.SupportsDirectoryPicker = false;

        var cut = Render<HomePage>();
        Assert.DoesNotContain("Your browser will ask about the selected folder", cut.Markup);

        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.DoesNotContain("Your browser will ask about the selected folder", cut.Markup);
        Assert.DoesNotContain(FolderPickDisplay.WriteAccessConsequence, cut.Markup);
        Assert.Equal(1, _folderAccess.TriggerFallbackCallCount);
    }

    [Fact]
    public void Home_NoFsAccessBrowser_StillStatesTheSupportedBrowsers()
    {
        // The beta wave's device statement, and the case that decides its gate.
        // A visitor whose browser has no directory picker — a phone, where the
        // webkitdirectory fallback is weak-to-absent — meets a pick button that
        // may raise nothing at all, with no code path that ever runs to explain
        // it. So unlike the two-step permission guidance directly beneath it,
        // this line is NOT behind the FS-Access probe: the reader it exists for
        // is exactly the one that probe excludes. Pinned together here so the
        // two gates can't be conflated by a later edit.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.SupportsDirectoryPicker = false;

        var cut = Render<HomePage>();

        Assert.Contains(FolderPickDisplay.SupportedBrowsers, cut.Markup);
        // …while its capability-gated neighbour stays absent on this browser.
        Assert.DoesNotContain("Your browser will ask about the selected folder", cut.Markup);
    }

    [Fact]
    public async Task Home_PickHoldsFolder_HidesTheSupportedBrowsersStatement()
    {
        // The other end of the window: once a folder is held the pick demonstrably
        // worked on this browser, so the caution is moot and would be stale noise
        // beside a populated summary. Clearing brings it back — the gate is "no
        // folder held", matching its neighbour's window (this pick is an FS-Access
        // one, so both lines are on screen beforehand).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome();

        var cut = Render<HomePage>();
        Assert.Contains(FolderPickDisplay.SupportedBrowsers, cut.Markup);

        await cut.Find("#pickProblemFolder").ClickAsync(new());
        Assert.True(Services.GetRequiredService<PickedProblemFolder>().HasFiles);
        Assert.DoesNotContain(FolderPickDisplay.SupportedBrowsers, cut.Markup);

        await cut.FindAll("button").First(b => b.TextContent.Trim() == "Clear").ClickAsync(new());
        Assert.Contains(FolderPickDisplay.SupportedBrowsers, cut.Markup);
    }

    [Fact]
    public void Home_OffersTheFeedbackMailto_BesideTheVersionFooter()
    {
        // The beta feedback affordance, footer-side. It sits beside the version
        // because the version is what makes a report actionable — and both halves
        // read from AppInfo, so the subject line cannot name a different build
        // than the footer the tester is looking at. Pinned to AppInfo rather than
        // to a literal address for the same reason Help's copy is: one link, two
        // surfaces, no way to drift.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();

        var cut = Render<HomePage>();

        var link = cut.Find("a[href^='mailto:']");
        Assert.Equal(AppInfo.FeedbackMailto, link.GetAttribute("href"));
        Assert.Contains($"v{AppInfo.Version}", cut.Find("#appVersion").TextContent);
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

    // -----------------------------------------------------------------------
    //  Home.razor — saved filters (Arc B): the parent → SavedFiltersPanel →
    //  handler wiring, driven through real picks so the SavedFiltersStore loads
    //  from the FakeFolderAccess exactly as it would from the JS picked slot.
    // -----------------------------------------------------------------------

    /// <summary>A one-entry saved-filters document JSON, "Race" carrying a distinguishing player.</summary>
    private static string SavedFiltersJson(string name = "Race", string player = "Magriel") =>
        NamedFilterCollection.Empty.With(name, new FilterConfig { Players = [player] }).ToJson();

    private static AngleSharp.Dom.IElement FindSavedFilterRowButton(
        IRenderedComponent<HomePage> cut, string name, string buttonText)
    {
        var row = cut.FindAll("li.list-group-item")
            .Single(li => li.QuerySelector("span")?.TextContent == name);
        return row.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == buttonText);
    }

    private static Task ClickSavedFilterButtonByTextAsync(
        IRenderedComponent<HomePage> cut, string buttonText) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == buttonText).ClickAsync(new());

    [Fact]
    public async Task Home_LoadSavedFilter_StagesConfigIntoPanel_AndRegatesStart()
    {
        // The load chain the arc turns on: clicking a saved filter's Load stages
        // its config into the FilterPanel as a bulk edit (→ OnFilterDirty), which
        // clears AppliedFilter and re-gates Start until the user re-Applies.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: StatsSaveCapability.Enabled);
        _folderAccess.FiltersJson = SavedFiltersJson();

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        await ApplyFiltersAsync(cut); // post-pick, since the pick resets the applied state

        // Both gates met (folder picked + filters applied): Start is armed.
        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.False(startBtn.HasAttribute("disabled"));

        // Players lives behind the panel's disclosure, so open it to read the
        // staged value back off the real control.
        await ExpandMoreFiltersAsync(cut);
        await FindSavedFilterRowButton(cut, "Race", "Load").ClickAsync(new());

        // The config staged into the panel — its players input now shows the value.
        Assert.Equal("Magriel",
            cut.Find("input[placeholder='e.g. Hal, Magriel']").GetAttribute("value"));
        // …and the load's dirty signal cleared the applied filter, re-gating Start.
        startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.True(startBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_SavedFiltersPanel_RendersAboveFilterPanel()
    {
        // Task T: the Saved Filters panel renders above the FilterPanel, so
        // loading a saved config (which stages into the panel below) reads
        // top-down. Needs an FS-Access pick (Enabled) for the panel to show.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: StatsSaveCapability.Enabled);
        _folderAccess.FiltersJson = SavedFiltersJson();

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        // Address the two panels by their own unique, always-rendered controls:
        // the saved-filters save-name box and the filter panel's more-filters
        // disclosure toggle. (Not the position-pattern box — it sits inside the
        // disclosure and is absent from the DOM while collapsed.)
        var markup = cut.Markup;
        var savedFiltersIndex = markup.IndexOf("id=\"saveFilterName\"", StringComparison.Ordinal);
        var filterPanelIndex = markup.IndexOf("id=\"moreFiltersToggle\"", StringComparison.Ordinal);
        Assert.True(savedFiltersIndex >= 0, "SavedFiltersPanel should render for an FS-Access pick");
        Assert.True(filterPanelIndex >= 0, "FilterPanel should render post-pick");
        Assert.True(savedFiltersIndex < filterPanelIndex,
            "SavedFiltersPanel must render above the FilterPanel");
    }

    [Fact]
    public async Task Home_SaveAsNewFilter_PersistsAndListsIt()
    {
        // Save-as: TryGetEditedConfig snapshots the panel, the store's With +
        // persist writes once, and the new instance flows back so the pick list
        // shows the name.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: StatsSaveCapability.Enabled);
        _folderAccess.FiltersJson = null; // fresh folder

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        cut.Find("#saveFilterName").Input("MyFilter");
        await ClickSavedFilterButtonByTextAsync(cut, "Save");

        Assert.Single(_folderAccess.FiltersWrites);
        Assert.Contains("MyFilter", cut.Markup);
    }

    [Fact]
    public async Task Home_SaveAsInvalidPositionPattern_ShowsNoticeAndDoesNotWrite()
    {
        // The one state Apply refuses — an unparseable position pattern — is the
        // one TryGetEditedConfig refuses. The host surfaces the refusal (the
        // panel already cleared its typed name) and nothing is written.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: StatsSaveCapability.Enabled);
        _folderAccess.FiltersJson = null;

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        // Position pattern sits behind the panel's disclosure — open it to type.
        await ExpandMoreFiltersAsync(cut);
        cut.Find("#positionPattern").Input("[6,2"); // unparseable
        cut.Find("#saveFilterName").Input("Bad");
        await ClickSavedFilterButtonByTextAsync(cut, "Save");

        Assert.Contains("position pattern is invalid", cut.Markup);
        Assert.Empty(_folderAccess.FiltersWrites);
    }

    [Fact]
    public async Task Home_CorruptFiltersFile_ShowsNoticeHidesPanel_FileUntouched()
    {
        // A corrupt bgquiz-filters.json degrades to LoadFailed: the panel is
        // replaced by the notice (naming the file), and the file is never
        // overwritten — the zero-writes preservation guarantee.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: StatsSaveCapability.Enabled);
        _folderAccess.FiltersJson = "{ not valid json";

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.Contains(QuizFiltersFile.FileName, cut.Markup);
        Assert.Contains("couldn't be read", cut.Markup);
        Assert.Empty(cut.FindAll("#saveFilterName")); // panel not rendered
        Assert.Empty(_folderAccess.FiltersWrites);
    }

    [Fact]
    public async Task Home_FiltersPermissionDenied_LoadOnly_SaveDisabledLoadEnabled()
    {
        // Capability mapping: PermissionDenied → load-only. The panel renders
        // (the pick's implicit read grant loaded the collection), Save is
        // disabled with the filters-specific reason, and Load stays enabled.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: StatsSaveCapability.PermissionDenied);
        _folderAccess.FiltersJson = SavedFiltersJson();

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        // The reason spells out that Delete is disabled too, not just Save — the
        // panel offers both persistence gestures and PermissionDenied bars both.
        Assert.Contains("saved filters can be loaded but not changed or deleted", cut.Markup);
        Assert.True(cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save").HasAttribute("disabled"));
        Assert.False(FindSavedFilterRowButton(cut, "Race", "Load").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_FiltersBrowserUnsupported_NoSavedFiltersPanel()
    {
        // A fallback pick can't see the file: no saved-filters panel at all, and
        // the store never even reads (the JSON below is set but ignored).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextCollectOutcome = new FolderPickOutcome(
            Cancelled: false, "FallbackDir",
            [new PickedFile("fb.xgp", [9, 9])], StatsSaveCapability.BrowserUnsupported);
        _folderAccess.FiltersJson = SavedFiltersJson();

        var cut = Render<HomePage>();
        await cut.Find("#problemFolderFallback").ChangeAsync(new ChangeEventArgs());

        Assert.DoesNotContain("Saved Filters", cut.Markup);
        Assert.Empty(cut.FindAll("#saveFilterName"));
        Assert.Empty(_folderAccess.FiltersWrites);
    }

    [Fact]
    public async Task Home_FiltersPermissionDenied_NoSavedFilters_HidesPanel()
    {
        // Task Y: under a read-only (PermissionDenied) pick saving is disabled,
        // so a saved-filters section with nothing to load is pure clutter — hide
        // it. A fresh folder (no bgquiz-filters.json) reads as Ready over an empty
        // collection, so Count is 0 and the whole section is suppressed (panel and
        // its load-only reason both).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: StatsSaveCapability.PermissionDenied);
        _folderAccess.FiltersJson = null; // fresh folder → Ready, zero saved filters

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.Empty(cut.FindAll("#saveFilterName")); // no panel
        Assert.DoesNotContain("can be loaded but not changed or deleted", cut.Markup);
    }

    [Fact]
    public async Task Home_FiltersPermissionDenied_WithSavedFilters_ShowsLoadOnlyPanel()
    {
        // Task Y over-trigger guard: read-only with at least one saved filter
        // still shows the panel (load-only) — there is something to load, so it is
        // not clutter.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: StatsSaveCapability.PermissionDenied);
        _folderAccess.FiltersJson = SavedFiltersJson(); // one saved filter

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.NotEmpty(cut.FindAll("#saveFilterName")); // panel present, load-only
        Assert.Contains("can be loaded but not changed or deleted", cut.Markup);
    }

    [Fact]
    public async Task Home_FiltersEnabled_NoSavedFilters_ShowsPanel()
    {
        // Task Y boundary: an Enabled pick with zero saved filters still shows the
        // panel — you can save into it, so an empty collection isn't clutter the
        // way it is under read-only.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: StatsSaveCapability.Enabled);
        _folderAccess.FiltersJson = null; // fresh folder, zero saved filters

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.NotEmpty(cut.FindAll("#saveFilterName")); // savable, so shown even when empty
    }

    [Fact]
    public async Task Home_FiltersPermissionDenied_LoadFailed_HidesPanelButShowsUntouchedNotice()
    {
        // Task Y split: the panel-offering rule (hide when empty) and the
        // degrade-reporting rule (report a read failure) are separate concerns.
        // Under a read-only pick whose read is genuinely withheld, the store
        // degrades to LoadFailed with an empty collection — so the panel stays
        // hidden (nothing to offer), but the "couldn't be read, left untouched"
        // data-protection notice must still show. LoadFailed always implies an
        // empty collection, so gating it on the panel's empty-hiding predicate
        // would swallow it every time it fires.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: StatsSaveCapability.PermissionDenied);
        _folderAccess.FiltersReadException = new JSException("read withheld"); // → LoadFailed

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.Empty(cut.FindAll("#saveFilterName")); // panel hidden (empty, can't save)
        Assert.Contains(QuizFiltersFile.FileName, cut.Markup);
        Assert.Contains("couldn't be read", cut.Markup); // the data-protection notice survives
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
    public async Task Home_DepthFacetEdit_RegatesStart_AndAppliesTheModeIntent()
    {
        // The start gate over the redesigned depth facet (XgFilter_Lib cbca4b3 /
        // XgFilter_Razor f227f25), driven through the panel's real controls: the
        // facet is now three per-mode toggles, each with its own level list, and
        // nothing here re-derives that — the arc pinned is edit → dirty → Start
        // re-gated → Apply → the raw intent lands in AppliedFilter.
        //
        // Worth its own case because BgQuiz names no depth member anywhere: the
        // compiler could not have caught the facet's rewrite, and no existing test
        // touched a depth control. Asserting IncludeRollouts off the *applied*
        // config (rather than the panel's checkbox) is what proves the toggle
        // reaches the config the quiz is built from, across the panel's emit.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome();

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        // Apply through the panel's own button, not ApplyFiltersAsync: that helper
        // invokes OnFilterConfigChanged with a *fresh* FilterConfig, which arms the
        // gate but discards whatever the panel's controls hold — it could never
        // carry a depth selection, and this case is about exactly that payload.
        await ClickApplyFilterAsync(cut);

        Assert.False(StartButton(cut).HasAttribute("disabled"));

        // Analysis depth sits behind the panel's disclosure, like every facet but
        // the error range. The mode toggle ids are the panel's own
        // md_<AnalysisMode> convention — a level group renders only once its mode
        // is checked, which is itself part of what a bare toggle click asserts.
        await ExpandMoreFiltersAsync(cut);
        await cut.Find($"#md_{AnalysisMode.Rollout}").ChangeAsync(new ChangeEventArgs { Value = true });

        // The edit is a dirty signal like any other: Start re-gates until Apply.
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.False(Services.GetRequiredService<AppliedFilter>().IsApplied);

        await ClickApplyFilterAsync(cut);

        var applied = Services.GetRequiredService<AppliedFilter>().Config;
        Assert.NotNull(applied);
        Assert.True(applied!.IncludeRollouts);
        // Untouched toggles stay off, and an untoggled mode's level list stays
        // empty — the facet is a union over the enabled toggles only.
        Assert.False(applied.IncludeEvaluations);
        Assert.False(applied.IncludeBookRollouts);
        Assert.Empty(applied.RolloutLevels); // no level checked = any level
        Assert.False(StartButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_RePick_ResetsAppliedFilterAndPanelBuffersToDefaults()
    {
        // A pick ends the current setup — the filter half of the rule the mix half
        // already followed. Type a player and Apply against one folder (Start
        // armed, count shown), then re-pick another: the applied state clears (so
        // Start re-gates behind its Apply hint), the panel's edit buffers go back
        // to defaults, and the stale count line is gone. Without the reset the old
        // filter stays applied and Start is live against a corpus that filter was
        // never weighed against. Driven through the FS-Access mechanism, but the
        // reset sits in the gesture (PickFolderAsync) — one click, one reset,
        // whichever mechanism then serves it.
        WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome("First", "first.xg");

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        // Set a filter through the panel's own controls and commit it with its own
        // Apply button — the real gesture, not a synthesized callback. Players
        // lives behind the panel's disclosure, so open it first — and again after
        // the re-pick, because a pick renders at its empty-folder state (the busy
        // affordance paints there, and before that the picked-slot interop
        // yielded), which unmounts the panel behind the disclosure gate and
        // re-mounts it collapsed. That re-mount is the documented production
        // behavior, not an artifact: what this test pins is that the buffers come
        // back at defaults however the panel got there.
        await ExpandMoreFiltersAsync(cut);
        cut.Find("input[placeholder='e.g. Hal, Magriel']").Input("Magriel");
        await ClickApplyFilterAsync(cut);

        Assert.Equal("Magriel",
            cut.Find("input[placeholder='e.g. Hal, Magriel']").GetAttribute("value"));
        Assert.True(Services.GetRequiredService<AppliedFilter>().IsApplied);
        Assert.False(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains("decisions match your filters", cut.Markup);

        // Re-pick a different folder.
        _folderAccess.NextPickOutcome = OneFileOutcome("Second", "second.xg");
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        // Panel buffers back to defaults…
        await ExpandMoreFiltersAsync(cut); // re-mounted collapsed by the pick's render
        Assert.Equal(string.Empty,
            cut.Find("input[placeholder='e.g. Hal, Magriel']").GetAttribute("value"));
        // …applied state dropped, so Start re-gates behind the Apply hint…
        Assert.False(Services.GetRequiredService<AppliedFilter>().IsApplied);
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains("Apply the filters above to enable Start", cut.Markup);
        // …and the count that described the old corpus is gone.
        Assert.DoesNotContain("decisions match your filters", cut.Markup);
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
        WithPickedFolder(); // progressive disclosure: the checkbox shows only post-pick
        WithAppliedFilter();
        var shuffle = WithShuffleOption();

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

        var cut = Render<HomePage>();

        // Armed: summary shown, Start enabled.
        Assert.Contains("clear-me", cut.Markup);
        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.False(startBtn.HasAttribute("disabled"));

        // Clear → summary gone, setup surface (incl. Start) hidden by
        // progressive disclosure (HasFiles false), picked slot cleared.
        var clear = cut.FindAll("button").First(b => b.TextContent.Trim() == "Clear");
        await clear.ClickAsync(new());

        Assert.DoesNotContain("clear-me", cut.Markup);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Start Quiz");
        // Derivation: WithPickedFolder seeds the holder directly rather than
        // picking through the button, so no pick gesture ran and the Clear click
        // is the only thing that reached the picked slot — 1. (A test that picks
        // through the button adds one per gesture; see
        // Home_CancelledRePick_EndsTheHeldSetupAndLosesTheFolder.)
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

        var cut = Render<HomePage>();

        var version = AssemblyInformationalVersion();
        Assert.False(string.IsNullOrWhiteSpace(version)); // non-empty
        Assert.Matches(@"^\d+\.\d+", version);            // expected shape: leading SemVer
        Assert.Contains($"v{version}", cut.Markup);
    }

    [Fact]
    public void AppVersion_LeadingSemVerIsTheReleaseNumber_AnyGitShaSuffixIsWellFormed()
    {
        // The informational version may carry a "+g<shortsha>" build-metadata
        // suffix naming the commit a build came from (StampGitShaSuffix in
        // BgQuiz_Blazor.Client.csproj — on by default, off for the shipping
        // publish), so this must hold for a stamped and a clean build alike:
        //
        //   1. Whatever follows, the release number the user reads is the
        //      csproj <Version> and nothing else — a suffix appends, it never
        //      displaces or corrupts. AssemblyVersion is the cross-check: it
        //      flows from the same <Version> (padded to a 4th field) but takes
        //      no build metadata, so the two agreeing pins <Version> as the
        //      single source without hardcoding a literal here.
        //   2. When a suffix *is* present it is the short-sha form, not some
        //      other trailing text that happened to land in the footer.
        var informational = AssemblyInformationalVersion();
        var assemblyVersion = typeof(HomePage).Assembly.GetName().Version!;
        var release = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";

        var plus = informational.IndexOf('+');
        Assert.Equal(release, plus < 0 ? informational : informational[..plus]);
        if (plus >= 0)
        {
            Assert.Matches(@"^\+g[0-9a-f]{7}$", informational[plus..]);
        }
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

        var cut = Render<DonePage>();

        Assert.Contains("couldn't be read", cut.Markup);
        Assert.Contains("role=\"status\"", cut.Markup);
    }

    [Fact]
    public async Task Done_SaysNothingNeedsSaving()
    {
        // Issue #51's echo. There is no "finish and quit" button and there should
        // not be one — closing the tab is the exit for an app with no account and
        // nothing pending — so the reassurance is words, and words only exist if
        // something asserts they are there.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();

        var cut = Render<DonePage>();

        Assert.Contains("you can close the tab whenever you like", cut.Markup);
    }

    [Fact]
    public async Task Done_StatsWriteFailed_WithholdsTheNothingNeedsSavingLine()
    {
        // The gate, and the reason for it: "nothing needs saving" directly under
        // "your stats could not be saved" reads as a contradiction, and in that
        // state the alert is the honest word. The reassurance is true of the app
        // in general and useless to a user whose write just failed, so it stands
        // down rather than softening the alert.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();
        await WithStatsStoreInStatusAsync(QuizStatsStatus.WriteFailed);

        var cut = Render<DonePage>();

        Assert.DoesNotContain("you can close the tab whenever you like", cut.Markup);
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
    public async Task Quiz_UndoButtons_UsableFromTheFirstRenderOfAnEntry()
    {
        // Both Undo buttons were gated on `_playEntry is null` — an @ref Blazor
        // assigns only AFTER the render that creates the entry, so the answering
        // branch's first render always read null and disabled them. Nothing
        // re-renders this page during click-by-click assembly (the entry raises
        // no callback until the play completes), so they stayed disabled for the
        // whole entry and enabled only at completion — when Undo is no longer
        // the thing you want. This asserts the state at the first render, which
        // is the start of assembly and the exact moment that failed.
        var decision = TestFixtures.BearOffOneDecision();
        var c = WithController(decision);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var cut = Render<QuizPage>();

        Assert.False(UndoButton(cut, "Undo last").HasAttribute("disabled"));
        Assert.False(UndoButton(cut, "Undo all").HasAttribute("disabled"));

        // And usable on an entry with nothing entered: the producer documents
        // both as no-ops there, which is what makes always-enabled honest rather
        // than a promise the click discovers is empty. The play is still
        // un-assembled afterwards, so Submit stays gated and no answer was scored.
        await UndoButton(cut, "Undo last").ClickAsync(new());
        await UndoButton(cut, "Undo all").ClickAsync(new());

        Assert.Null(c.Review);
        Assert.True(cut.FindAll("button")
                       .First(b => b.TextContent.Trim() == "Submit").HasAttribute("disabled"));
        Assert.False(UndoButton(cut, "Undo last").HasAttribute("disabled"));
    }

    /// <summary>The named Undo button in the quiz page's answering action row.</summary>
    private static AngleSharp.Dom.IElement UndoButton(
        IRenderedComponent<QuizPage> cut, string label) =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == label);

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

    /// <summary>
    /// Answers the rendered play-answering page the way the user does: latch the
    /// finished play through <see cref="BackgammonPlayEntry"/>'s
    /// <c>OnPlayCompleted</c> (what the board's clicks raise), then click the
    /// page's own Submit. Tests that merely need a review state on screen call
    /// <c>Controller.SubmitPlay</c> directly and skip the page entirely; a test
    /// about the page's <c>Submit</c> handler has to go through the button.
    /// </summary>
    private static async Task SubmitPlayThroughPageAsync(IRenderedComponent<QuizPage> cut, Play play)
    {
        await cut.InvokeAsync(() =>
            cut.FindComponent<BackgammonPlayEntry>().Instance.OnPlayCompleted.InvokeAsync(play));
        await cut.FindAll("button").First(b => b.TextContent.Trim() == "Submit").ClickAsync(new());
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync(); // exhausts → IsFinished

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
        // The page exists to teach the prerequisites, the flow, the click
        // vocabulary of a checker play, *and* the semantics a user cannot discover
        // by clicking around; pin its section skeleton so a future edit can't
        // quietly drop part of it. The headings alone are pinned, never the prose
        // beneath them. Order is part of the pin: the two setup features live where
        // the user meets them on Home (filters, then saved filters, then the mix),
        // and the prerequisites lead because everything after them assumes them.
        WithController();

        var cut = Render<HelpPage>();

        var headings = cut.FindAll("h2").Select(h => h.TextContent.Trim()).ToList();
        Assert.Equal(
            [
                "Before you start",
                "Your data stays yours",
                "Pick your folder",
                "Choose filters",
                "Save filters you use often",
                "Weight the quiz by your lifetime stats",
                "Answer the position",
                "Making a checker play",
                "Scoring",
                "Review the solution",
                "Stats and finishing",
                "Lifetime stats",
                "Things worth knowing",
                "Send feedback",
            ],
            headings);
    }

    [Fact]
    public void Help_BeforeYouStart_StatesTheBrowserRuleFromTheSharedConstant()
    {
        // The beta wave's prerequisites lead. The browser sentence is the one
        // clause Help renders *verbatim* from FolderPickDisplay rather than
        // restating in its own voice (the class doc records why): Home says the
        // same thing beside the pick button, and a reader who checks Help before
        // trying, then hits the dead entry point anyway, must not find two
        // differently-worded rules. Asserting the constant is what makes a
        // future edit to one surface fail here rather than drift.
        WithController();

        var cut = Render<HelpPage>();

        Assert.Contains(FolderPickDisplay.SupportedBrowsers, cut.Markup);
    }

    [Fact]
    public void Help_BeforeYouStart_NamesBothFilesBgQuizWritesIntoTheFolder()
    {
        // Same page/rule discipline the caps and the stats filename already use,
        // extended to the second file the app writes: the prerequisites lead tells
        // a tester exactly what will appear in their folder, sourced from the two
        // constants the two stores actually write — so neither can drift.
        WithController();

        var cut = Render<HelpPage>();

        Assert.Contains(QuizStatsFile.FileName, cut.Markup);
        Assert.Contains(QuizFiltersFile.FileName, cut.Markup);
    }

    [Fact]
    public void Help_DataSection_NamesItsOwnBrowserStorageFromTheOwningConstants()
    {
        // The wiring half of the copy-pin split (the phrasing is pinned as
        // independent literals in the e2e suite). Every entry the app keeps in
        // the browser is rendered from the constant that actually writes it —
        // MixDraft owns xg_quizMix in both directions, QuizSettings owns the
        // settings entry, QuizLiveMarker owns its sessionStorage mark — so a key
        // rename that left the prose behind fails here rather than shipping a
        // name the reader can't find in devtools. That is the reason
        // QuizLiveMarker's key was widened from private to internal at all;
        // asserting it is what keeps the widening earning its keep.
        WithController();

        var cut = Render<HelpPage>();

        var section = SectionText(
            cut.FindAll("h2").Single(h => h.TextContent.Trim() == "Your data stays yours"));

        Assert.Contains(MixDraft.StorageKey, section);
        Assert.Contains(QuizSettings.StorageKey, section);
        Assert.Contains(QuizLiveMarker.StorageKey, section);
    }

    [Fact]
    public void Help_DataSection_PointsAtTheFilterPanelsStorageInsteadOfDescribingIt()
    {
        // The producer-owns-its-own-prose split, pinned. XgFilter_Razor documents
        // what FilterPanel persists inside FilterHelp, rendered from the panel's
        // own key constants; this page links into that anchor and names no key of
        // the panel's. The positive half is the link. The negative half asserts
        // the section's <code> elements are *exactly* the app's own two keys —
        // stated that way rather than as "does not contain xg_filter_config",
        // because the panel's key names are internal to another repo, so a
        // literal here could rot into a negative assertion that passes for the
        // wrong reason. An inlined copy of the panel's list would add <code>
        // elements and fail, whatever those keys end up being called.
        WithController();

        var cut = Render<HelpPage>();

        var heading = cut.FindAll("h2").Single(h => h.TextContent.Trim() == "Your data stays yours");

        var links = new List<AngleSharp.Dom.IElement>();
        var codes = new List<string>();
        for (var node = heading.NextElementSibling;
             node is not null && !string.Equals(node.TagName, "H2", StringComparison.OrdinalIgnoreCase);
             node = node.NextElementSibling)
        {
            links.AddRange(node.QuerySelectorAll("a"));
            codes.AddRange(node.QuerySelectorAll("code").Select(c => c.TextContent.Trim()));
        }

        // EndsWith, not equality: the href is the page's own address plus the
        // fragment, because a bare "#fragment" resolves against <base href="/">
        // and would land the reader on Home. That the *link* works is an e2e
        // question; what this pins is that the pointer exists and aims at the
        // producer's anchor.
        Assert.Contains(links, a => a.GetAttribute("href")!.EndsWith("#fh-what-is-remembered"));
        Assert.Equal([MixDraft.StorageKey, QuizSettings.StorageKey, QuizLiveMarker.StorageKey], codes);
    }

    [Fact]
    public void Help_DataSection_LeavesTheTwoWrittenFilesToThePrerequisitesList()
    {
        // The other half of "compose, don't consolidate": the files BgQuiz writes
        // into the user's folder are already named from QuizStatsFile /
        // QuizFiltersFile in Before you start, so the data section points back at
        // them rather than restating them. Pinned because the natural instinct
        // when writing a section called "where everything is stored" is to list
        // all of it in one place — which would put the two filenames on the page
        // twice and make the next rename a two-site edit.
        WithController();

        var cut = Render<HelpPage>();

        var section = SectionText(
            cut.FindAll("h2").Single(h => h.TextContent.Trim() == "Your data stays yours"));

        Assert.DoesNotContain(QuizStatsFile.FileName, section);
        Assert.DoesNotContain(QuizFiltersFile.FileName, section);
    }

    [Fact]
    public void Help_DocumentsSavedFiltersAndTheWeightedMix()
    {
        // Both features shipped user-facing and undocumented. The headings are
        // pinned above; this pins that each section carries the load-bearing fact
        // a user cannot discover by clicking — saved filters live per-folder in a
        // named file, and the mix needs the lifetime stats it composes from
        // (which is why it is offered only for a stats-capable pick).
        WithController();

        var cut = Render<HelpPage>();

        var headings = cut.FindAll("h2").ToList();
        var savedFilters = headings.Single(h => h.TextContent.Trim() == "Save filters you use often");
        var mix = headings.Single(
            h => h.TextContent.Trim() == "Weight the quiz by your lifetime stats");

        Assert.Contains(QuizFiltersFile.FileName, SectionText(savedFilters));
        Assert.Contains("lifetime stats", SectionText(mix), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Help_ChooseFilters_DocumentsTheMatchCountAndItsMixCaveat()
    {
        // The count line shipped undocumented. Two readings of it are wrong and
        // both are pinned here: it counts matching *decisions* (pass positions
        // included, then skipped at quiz time), and it describes the filters only —
        // so with a mix applied the quiz is drawn from that pool and can be much
        // smaller than the number shown. Section-scoped, since "mix" appears in
        // several sections legitimately.
        WithController();

        var cut = Render<HelpPage>();

        var section = SectionText(
            cut.FindAll("h2").Single(h => h.TextContent.Trim() == "Choose filters"));

        // Substrings deliberately kept inside a single source line: the rendered
        // text carries the razor file's own line breaks and indentation.
        Assert.Contains("says how many decisions", section);
        Assert.Contains("not problems you will be shown", section);
        Assert.Contains("can be much smaller than the number shown", section);
    }

    [Fact]
    public void Help_ChooseFilters_DocumentsTheAnswerTypeBreakdownAndItsZeros()
    {
        // The count line now carries a breakdown, so the paragraph that explains
        // the line has to explain that too — and specifically the reading a user
        // cannot get from the control itself: the list is exhaustive, so a bucket
        // at zero is a fact about their collection, not a missing row.
        //
        // What is pinned is the *explanation*, never the five bucket names: those
        // are AnswerTypeDisplay's copy, rendered on Home, and reciting them here
        // would be the second encoding this page's prose deliberately avoids.
        WithController();

        var cut = Render<HelpPage>();

        var section = SectionText(
            cut.FindAll("h2").Single(h => h.TextContent.Trim() == "Choose filters"));

        Assert.Contains("breaks the pool down by the kind of", section);
        Assert.Contains("Every kind is listed every time", section);
        Assert.Contains("holds none of that kind", section);

        // The four cube-verdict labels specifically: "checker plays" is ordinary
        // English this paragraph is entitled to use, but the cube verdicts have
        // exact spellings only AnswerTypeDisplay gets to choose, so their
        // appearance here would be the recitation. (Skip(1) drops the checker
        // bucket — the order is pinned in AnswerTypeDisplayTests.)
        foreach (var bucket in AnswerTypeDisplay.Buckets(AnswerTypeDistribution.Empty).Skip(1))
            Assert.DoesNotContain(bucket.Label, section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Help_ChooseFilters_EmbedsTheProducerOwnedFacetReference()
    {
        // Facet documentation has one owner, and it is not this app: XgFilter_Razor
        // renders FilterHelp beside the panel that implements the facets, and Help
        // embeds it rather than restating what each filter admits. Pinning the
        // embedded *component* (not prose) is what makes a future edit that deletes
        // it — or replaces it with a hand-written copy — fail here; the copy would
        // otherwise pass every existing test while drifting from the lib on the next
        // facet redesign, the way the depth facet's rewrite into per-mode clauses
        // would have, had BgQuiz ever described that facet.
        WithController();

        var cut = Render<HelpPage>();

        Assert.NotNull(cut.FindComponent<FilterHelp>());

        // ...and it lands inside Choose filters, where the surrounding framing puts
        // it, rather than floating somewhere the reader meets before the panel.
        var section = SectionText(
            cut.FindAll("h2").Single(h => h.TextContent.Trim() == "Choose filters"));
        Assert.Contains(FilterFacet.AnalysisDepth.ToLabel(), section);
        Assert.Contains(FilterFacet.ErrorRange.ToLabel(), section);
    }

    [Fact]
    public void Help_ChooseFilters_KeepsItsFramingAndWritesNoFacetProseOfItsOwn()
    {
        // The other half of the ruling: what stays app-level is what FilterHelp
        // cannot know — where the panel sits in the start flow, and that filters
        // must be applied before Start. Pinned so a later "the producer documents
        // filters now" sweep can't take the framing with it.
        //
        // The negative half names the one gloss the embed retired: Help used to
        // define the error-range facet in its own voice ("how costly the recorded
        // mistake was"). Asserting its absence is narrow on purpose — a general
        // "no facet prose" assertion isn't expressible — but it pins the specific
        // second encoding this leg removed against being pasted back.
        WithController();

        var cut = Render<HelpPage>();

        var section = SectionText(
            cut.FindAll("h2").Single(h => h.TextContent.Trim() == "Choose filters"));

        Assert.Contains("Show more filters", section);
        Assert.Contains("before Start becomes available", section);
        Assert.DoesNotContain("how costly the recorded mistake was", section);
    }

    /// <summary>
    /// The rendered text of one Help section: everything from the given heading
    /// up to the next <c>h2</c>. Lets a section-scoped assertion say what it
    /// means on a page where the same term legitimately appears in several
    /// sections (a whole-markup <c>Contains</c> would not discriminate).
    /// </summary>
    private static string SectionText(AngleSharp.Dom.IElement heading)
    {
        var text = new System.Text.StringBuilder();
        for (var node = heading.NextElementSibling;
             node is not null && !string.Equals(node.TagName, "H2", StringComparison.OrdinalIgnoreCase);
             node = node.NextElementSibling)
        {
            text.Append(node.TextContent);
        }
        return text.ToString();
    }

    [Fact]
    public void Help_OffersTheFeedbackMailto_CarryingTheRunningVersion()
    {
        // The beta feedback affordance. Asserted against AppInfo — the hoisted
        // app-level SSOT both pages now read — rather than a literal address, so
        // the two surfaces cannot render different links; the version half is
        // asserted separately below (it is what makes a report actionable, and a
        // hardcoded subject would drift at every deploy).
        WithController();

        var cut = Render<HelpPage>();

        var link = cut.Find("a[href^='mailto:']");
        Assert.Equal(AppInfo.FeedbackMailto, link.GetAttribute("href"));
    }

    [Fact]
    public void FeedbackMailto_AddressesTheBetaMailbox_AndEscapesTheVersionIntoTheSubject()
    {
        // The link's contract, independent of either page. The version must ride
        // the subject *escaped*: a non-shipping build's "+g<shortsha>" suffix
        // contains a '+', which a mail client decoding the query as form data
        // reads as a space — the commit being reported against would arrive
        // mangled. Asserting the escaped form is what pins that.
        Assert.StartsWith($"mailto:{AppInfo.FeedbackAddress}?subject=", AppInfo.FeedbackMailto);
        Assert.Contains(Uri.EscapeDataString($"BgQuiz feedback ({AppInfo.Version})"),
            AppInfo.FeedbackMailto);
        Assert.DoesNotContain(" ", AppInfo.FeedbackMailto);
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
    public async Task Quiz_Review_CubeVerdict_LabelsHalvesByUsersSubmittedActions()
    {
        // The verdict line names each half for the action the user actually
        // submitted (not a generic half-name), matching the solution diagram's
        // banner wording. Against the default cube fixture (best is Double/Take),
        // a Too-Good answer — (NoDouble, Pass) — is incorrect on both halves, so
        // the doubler half reads "No Double" and the taker half reads "Pass".
        var c = WithController(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var cut = Render<QuizPage>();

        await cut.InvokeAsync(() => c.SubmitCubeAction(CubeDecisionPair.TooGood));
        Assert.NotNull(c.Review);

        var verdict = cut.Find(".status-strip").QuerySelector(".status-verdict")!;
        Assert.Contains("alert-danger", verdict.ClassList);
        Assert.Contains("No Double: incorrect", verdict.TextContent);
        Assert.Contains("Pass: incorrect", verdict.TextContent);
        // The taker half is now labeled by the submitted action ("Pass"), never
        // the old generic "Take" half-name.
        Assert.DoesNotContain("Take:", verdict.TextContent);
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

    /// <summary>
    /// Satisfies the filter half of the start gate through the hosted
    /// <see cref="FilterPanel"/>'s own Apply callback. Every pick resets the
    /// applied state (see
    /// <see cref="Home_RePick_ResetsAppliedFilterAndPanelBuffersToDefaults"/>), so
    /// a test that picks and then wants Start armed has to apply <i>after</i> its
    /// last pick — pre-arming the holder is not enough.
    /// <para>
    /// It emits a <b>fresh, empty</b> <see cref="FilterConfig"/>, not whatever the
    /// panel's controls currently hold: it satisfies the gate and says nothing
    /// about the payload. A test that sets a facet and then asserts what reached
    /// <see cref="AppliedFilter"/> must go through
    /// <see cref="ClickApplyFilterAsync"/> instead — this helper would silently
    /// discard the selection and the assertion would fail for the wrong reason.
    /// </para>
    /// </summary>
    private static Task ApplyFiltersAsync(IRenderedComponent<HomePage> cut) =>
        cut.InvokeAsync(() => cut.FindComponent<FilterPanel>()
                                 .Instance.OnFilterConfigChanged.InvokeAsync(new FilterConfig()));

    /// <summary>
    /// Apply through the <see cref="FilterPanel"/>'s own <i>Apply Filter</i>
    /// button — the real gesture, so the config that reaches
    /// <see cref="AppliedFilter"/> is the one the panel built from its controls.
    /// The route to use whenever a test cares <i>what</i> was applied rather than
    /// merely that the gate opened (contrast <see cref="ApplyFiltersAsync"/>).
    /// </summary>
    private static Task ClickApplyFilterAsync(IRenderedComponent<HomePage> cut) =>
        cut.FindAll("button")
           .First(b => b.TextContent.Trim() == "Apply Filter")
           .ClickAsync(new());

    /// <summary>
    /// Commit a minimal one-row mix (NeverSeen, 100%) through the real panel —
    /// Add category, then Apply Mix. The UI route matters under the derived
    /// gate: invoking <c>OnMixApplied</c> directly would commit to the holder
    /// while leaving the draft blank, fabricating a committed-but-divergent
    /// state no user can reach (every real commit flows draft → build →
    /// holder, so committed and shown agree at the moment of commit).
    /// <para>
    /// <b>Precondition:</b> a filter must have been applied for the <i>current</i>
    /// pick — Apply Mix is gated on it (§ <c>Home.MixApplyEnabled</c>), and a
    /// click on a disabled button is a silent no-op. A fixture that pre-arms
    /// <see cref="WithAppliedFilter"/> and then picks through the UI must
    /// re-apply after the pick, exactly as a user would.
    /// </para>
    /// </summary>
    private static async Task ApplyMixThroughPanelAsync(IRenderedComponent<HomePage> cut)
    {
        await cut.Find("#mixAddRow").ClickAsync(new());
        await cut.Find("#mixApply").ClickAsync(new());
    }

    /// <summary>
    /// Opens the <see cref="FilterPanel"/>'s "more filters" disclosure through
    /// the panel's own toggle button. The panel keeps the error-range section
    /// first and always visible; its other eight sections (player names,
    /// decision type, match scores, move number range, contact type, analysis
    /// depth, dice rolls, position pattern) render <i>only</i> while expanded —
    /// they are absent from the DOM when collapsed, not merely styled away — so
    /// any test driving one of those controls has to expand first. Error-range
    /// edits, Apply, and Clear filters need no expansion.
    /// <para>
    /// Toggling is navigation, not an edit: the panel raises no
    /// <c>OnFilterDirty</c> for it, so calling this never disturbs a test's
    /// applied/dirty expectations.
    /// </para>
    /// </summary>
    private static Task ExpandMoreFiltersAsync(IRenderedComponent<HomePage> cut) =>
        cut.Find("#moreFiltersToggle").ClickAsync(new());

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

        var cut = Render<HomePage>();
        await ApplyMixThroughPanelAsync(cut);
        await StartButton(cut).ClickAsync(new());

        Assert.True(c.HasStarted);
        Assert.NotNull(c.LastComposition);
        Assert.Equal(1, c.LastComposition!.DrawnCount);
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        Assert.EndsWith("/quiz", nav.Uri);
    }

    [Fact]
    public async Task Home_MixEdit_DisablesStart_UntilApplied()
    {
        // The derived gate's basic arc through the real UI: an edit makes the
        // draft diverge from the (blank) commitment, so Start gates with the
        // hint; Apply commits what the panel shows, agreement returns, Start
        // re-enables. No dirty event exists — Home re-renders off the draft's
        // Changed notification and re-derives the comparison.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: StatsSaveCapability.Enabled); // mix panel is Enabled-gated (Task X)
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        WithAppliedMix();

        var cut = Render<HomePage>();
        Assert.False(StartButton(cut).HasAttribute("disabled")); // blank draft, blank holder: clean

        await cut.Find("#mixAddRow").ClickAsync(new());
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains("Apply or reset the mix", cut.Markup);

        await cut.Find("#mixApply").ClickAsync(new());
        Assert.False(StartButton(cut).HasAttribute("disabled"));
        Assert.DoesNotContain("Apply or reset the mix", cut.Markup);
    }

    // -----------------------------------------------------------------------
    //  Apply Mix is sequenced behind Apply Filter (issue #45)
    // -----------------------------------------------------------------------

    /// <summary>The mix panel's Apply Mix button on a rendered Home page.</summary>
    private static bool MixApplyDisabled(IRenderedComponent<HomePage> cut) =>
        cut.Find("#mixApply").HasAttribute("disabled");

    /// <summary>
    /// Arrange an Enabled pick made <i>through the UI</i> — the only route that
    /// bumps <see cref="PickedProblemFolder.PickGeneration"/> the way a real
    /// pick does, which is what the Apply Mix gate reads. A pre-armed
    /// <see cref="WithPickedFolder"/> fixture cannot exercise the gate's
    /// expiry, because nothing ever expires.
    /// </summary>
    private async Task<IRenderedComponent<HomePage>> RenderWithUiPickAsync()
    {
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        WithAppliedMix();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: StatsSaveCapability.Enabled);

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        return cut;
    }

    [Fact]
    public async Task Home_FreshPick_ApplyMixGatedUntilAFilterIsApplied()
    {
        // Issue #45, the headline: the mix draws from the filtered pool, so
        // composing one before any filter has been applied is premature. The
        // gate is UX sequencing — the pipeline never required the order — so it
        // must also *say* why, not merely refuse.
        var cut = await RenderWithUiPickAsync();

        // The hint is up from the moment the panel appears — before any row
        // exists — so the ordering is learned before the composing starts.
        Assert.Contains("the mix draws its problems from the", cut.Markup);

        // A complete, valid one-row mix: from here the host gate is the only
        // thing still disabling Apply (the panel's own two conditions — at
        // least one row, and a draft that validates — are both satisfied).
        await cut.Find("#mixAddRow").ClickAsync(new());
        Assert.Null(Services.GetRequiredService<MixDraft>().ValidationError);
        Assert.True(MixApplyDisabled(cut));

        await ApplyFiltersAsync(cut);

        Assert.False(MixApplyDisabled(cut));
        Assert.DoesNotContain("the mix draws its problems from the", cut.Markup);
    }

    [Fact]
    public async Task Home_GatedApplyMix_LeavesResetEnabled_SoNoDraftCanWedge()
    {
        // The wedge-proofing the issue demanded. A dirty draft gates Start; if
        // the *only* two ways to un-dirty it were both sequenced behind the
        // filter, a user could reach a state with no visible way out. Reset is
        // deliberately ungated in every state, so there always is one.
        var cut = await RenderWithUiPickAsync();
        await cut.Find("#mixAddRow").ClickAsync(new()); // draft now diverges

        Assert.True(MixApplyDisabled(cut));
        Assert.False(cut.Find("#mixReset").HasAttribute("disabled"));

        await cut.Find("#mixReset").ClickAsync(new());

        // Back to agreement with the committed passthrough mix — no Apply Mix
        // needed, so the gate could never have trapped anyone.
        Assert.DoesNotContain("Apply or reset the mix", cut.Markup);
    }

    [Fact]
    public async Task Home_ApplyMix_NotRevokedByASubsequentlyDirtyFilter()
    {
        // The settled semantics: the gate asks "has this corpus been filtered?",
        // which a half-typed filter edit does not un-answer. The *config* is
        // edit-coupled (Start re-gates, below); the pick stamp is not. Getting
        // this wrong would yank Apply Mix away mid-composition for an unrelated
        // edit — the (AK)-flavoured failure the issue warned about.
        var cut = await RenderWithUiPickAsync();
        await cut.Find("#mixAddRow").ClickAsync(new()); // a valid, committable draft
        await ApplyFiltersAsync(cut);
        Assert.False(MixApplyDisabled(cut));

        await cut.InvokeAsync(() =>
            cut.FindComponent<FilterPanel>().Instance.OnFilterDirty.InvokeAsync());

        Assert.False(Services.GetRequiredService<AppliedFilter>().IsApplied); // Start re-gated…
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.False(MixApplyDisabled(cut));                                  // …Apply Mix not
    }

    [Fact]
    public async Task Home_NewPick_ReGatesApplyMix()
    {
        // The other half of the rule: a new corpus has not been filtered, so the
        // gate closes again. No reset code does this — the folder's generation
        // bumps and the stamp simply stops matching.
        var cut = await RenderWithUiPickAsync();
        await cut.Find("#mixAddRow").ClickAsync(new());
        await ApplyFiltersAsync(cut);
        Assert.False(MixApplyDisabled(cut));

        _folderAccess.NextPickOutcome =
            OneFileOutcome("Second", "second.xg", StatsSaveCapability.Enabled);
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        // The pick also discards the draft, so re-add a row: what is being
        // pinned is that a *committable* draft still cannot be applied, not
        // that an empty one can't.
        await cut.Find("#mixAddRow").ClickAsync(new());
        Assert.True(MixApplyDisabled(cut));
        Assert.Contains("the mix draws its problems from the", cut.Markup);
    }

    [Fact]
    public async Task Home_GatedApplyMix_IgnoresAProgrammaticClick()
    {
        // The disabled attribute is the affordance, not the contract: the
        // panel's handler early-returns too, so a dispatch that ignores
        // `disabled` still cannot commit past the gate.
        var cut = await RenderWithUiPickAsync();
        var holder = Services.GetRequiredService<AppliedMix>();
        await cut.Find("#mixAddRow").ClickAsync(new());

        await cut.InvokeAsync(() => cut.FindComponent<MixPanelComponent>().Instance
            .OnMixApplied.InvokeAsync(QuizMix.Empty)); // the host callback still works…
        await cut.Find("#mixApply").ClickAsync(new());  // …but the gated gesture does not

        Assert.True(holder.Current.IsPassthrough); // no non-blank mix ever committed
    }

    [Fact]
    public async Task Home_MixEditedBackToCommittedContent_DerivesCleanWithoutReApply()
    {
        // The deferred displayed==committed variant, now free: dirtiness is a
        // comparison, not a latch. Editing a committed mix gates Start; editing
        // it back to the exact committed content un-gates with NO Apply click —
        // there is no stored flag left dangling to clear. (Under the stored
        // flag this state stayed wedged-dirty until a re-Apply.)
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: StatsSaveCapability.Enabled);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        WithAppliedMix();

        var cut = Render<HomePage>();
        await ApplyMixThroughPanelAsync(cut); // committed: NeverSeen at 100%
        Assert.False(StartButton(cut).HasAttribute("disabled"));

        // Divergence gates…
        cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!.Input("90");
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains("Apply or reset the mix", cut.Markup);

        // …and identical content un-gates, however it came about.
        cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!.Input("100");
        Assert.False(StartButton(cut).HasAttribute("disabled"));
        Assert.DoesNotContain("Apply or reset the mix", cut.Markup);
    }

    [Fact]
    public async Task Home_MixReordered_GatesStart_OrderIsSemantic()
    {
        // Reorder alone — no text changed — is a real divergence: entry order
        // decides contested-overlap draws (producer contract), so the same
        // rows reordered are a different mix and Start must gate until the
        // user commits (or reorders back).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: StatsSaveCapability.Enabled);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        WithAppliedMix();

        var cut = Render<HomePage>();
        await cut.Find("#mixAddRow").ClickAsync(new()); // NeverSeen
        await cut.Find("#mixAddRow").ClickAsync(new()); // GotWrong (AI)
        await cut.Find("#mixApply").ClickAsync(new());
        Assert.False(StartButton(cut).HasAttribute("disabled"));

        await cut.FindAll(".mix-row")[1].QuerySelector("button[title='Move up']")!.ClickAsync(new());
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains("Apply or reset the mix", cut.Markup);

        await cut.FindAll(".mix-row")[0].QuerySelector("button[title='Move down']")!.ClickAsync(new());
        Assert.False(StartButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_MixEmptiedToZeroRows_AutoCommits_UnGatesStart()
    {
        // The pre-beta wedge's shape, end to end through the real MixPanel: a
        // mix built in the panel then emptied back to zero rows must not leave
        // Start gated with Apply disabled (zero rows) and only Reset as an
        // escape. Removing the last row auto-commits the blank mix, so holder
        // and draft agree at Empty and Start re-enables — no Reset click
        // needed. (Under derivation the gate would also clear if the holder
        // were still Empty, but the auto-commit is what keeps localStorage and
        // a previously committed mix consistent with the blank the user chose.)
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: StatsSaveCapability.Enabled); // mix panel is Enabled-gated (Task X)
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        WithAppliedMix();

        var cut = Render<HomePage>();

        // Build a one-row mix in the real panel → dirty → Start gated.
        await cut.Find("#mixAddRow").ClickAsync(new());
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains("Apply or reset the mix", cut.Markup);

        // Remove that row → back to zero rows → auto-commit blank → Start un-gates.
        await cut.FindAll(".mix-row")[0].QuerySelector("button[title='Remove']")!.ClickAsync(new());

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.False(StartButton(cut).HasAttribute("disabled"));
        Assert.DoesNotContain("Apply or reset the mix", cut.Markup);
    }

    [Fact]
    public async Task Home_MixRestore_FreshLoad_ShowsStoredMixGated_UntilApplied()
    {
        // W's fresh-load surface, now derived: the holder is at its passthrough
        // default (a cold boot / reload), so the persisted non-blank mix
        // hydrates into the draft for convenience but is NOT committed — the
        // draft diverges from the blank commitment, so Start gates and can't
        // run passthrough while a mix is displayed. No reconcile code fires:
        // the gate IS the comparison. Driven through the real hydration wire
        // (localStorage → MixDraft → panel), not a synthetic event.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: StatsSaveCapability.Enabled);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        var holder = WithAppliedMix(); // blank holder — hydration must not commit into it
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult(NeverSeenMix().ToJson());

        var cut = Render<HomePage>();

        // The panel shows the stored rows…
        Assert.NotEmpty(cut.FindAll(".mix-row"));
        // …but nothing was committed, so shown ≠ applied and Start is gated.
        Assert.True(holder.Current.IsPassthrough);
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains("Apply or reset the mix", cut.Markup);

        // Applying the restored mix through the panel commits it and un-gates.
        await cut.Find("#mixApply").ClickAsync(new());

        Assert.Equal(NeverSeenMix(), holder.Current); // committed exactly what was shown
        Assert.False(StartButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_MixRestore_NonEmpty_ResetClearsAndUngates()
    {
        // The other exit from the restored-gated state: Reset commits the blank
        // mix, so the rows clear, holder and draft agree at Empty, and Start
        // un-gates without ever running the restored mix.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: StatsSaveCapability.Enabled);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        var holder = WithAppliedMix();
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult(NeverSeenMix().ToJson());

        var cut = Render<HomePage>();
        Assert.True(StartButton(cut).HasAttribute("disabled"));

        await cut.Find("#mixReset").ClickAsync(new());

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.True(holder.Current.IsPassthrough);
        Assert.False(StartButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void Home_MixRestore_Passthrough_NoGate()
    {
        // A persisted passthrough (e.g. after a prior Reset) hydrates to zero
        // rows — the blank draft builds Empty and matches the fresh holder, so
        // Start is free. This is the "user with no persisted mix sees no gate"
        // invariant, falling out of the one rule rather than a special case.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: StatsSaveCapability.Enabled);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        WithAppliedMix();
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult(QuizMix.Empty.ToJson());

        var cut = Render<HomePage>();

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.False(StartButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void Home_MixRestore_NavigateBack_CommittedMix_NoReGate()
    {
        // Navigate-back with a committed mix: the Scoped holder survives, the
        // Scoped draft survives showing the same content (here freshly hydrated
        // from the matching blob a real Apply always leaves — WithAppliedMix
        // stages it), so shown == applied and Start stays enabled. No re-Apply
        // forced, and no reconcile arm deciding whom to believe: the equality
        // is the whole judgment. The fresh-load test above is the
        // fails-without contrast (blank holder, same blob → gated).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: StatsSaveCapability.Enabled);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        var holder = WithAppliedMix(NeverSeenMix()); // committed earlier this session

        var cut = Render<HomePage>();

        // Panel re-shows the rows, the holder stays committed, Start enabled.
        Assert.NotEmpty(cut.FindAll(".mix-row"));
        Assert.False(holder.Current.IsPassthrough);
        Assert.False(StartButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_MixEditedThenNavigatedAway_DraftSurvives_GatedButNeverWedged()
    {
        // Finding (AK)'s scenario under the ratified successor semantics. The
        // letter of (AK) — "navigate-away un-gates Start" — is SUPERSEDED: the
        // draft is now app-scoped, so the edit no longer dies with the panel.
        // Its spirit holds and is what this pins: after navigate-away/back the
        // user is never wedged. The edit is still on screen, Start is gated
        // exactly because an uncommitted mix is displayed, and Apply (enabled —
        // the rows are there) or Reset resolves it. The (AK) wedge — Start
        // gated over a BLANK panel with Apply disabled and the edit existing
        // nowhere — is unrepresentable under derivation: a blank draft builds
        // Empty and cannot disagree with a passthrough holder.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: StatsSaveCapability.Enabled); // mix panel is Enabled-gated
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        var holder = WithAppliedMix();
        // No stored mix, so hydration finds nothing: nothing was ever applied.

        var cut = Render<HomePage>();

        // Add a category and stop there — an uncommitted edit, so Start gates.
        await cut.Find("#mixAddRow").ClickAsync(new());
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains("Apply or reset the mix", cut.Markup);

        // Navigate away and back: Home and its MixPanel unmount, but the draft
        // — like the holders — is Scoped (Singleton here) and survives, as on a
        // real in-app navigation to Help and back.
        await DisposeComponentsAsync();
        var back = Render<HomePage>();

        // The edit is still on screen, still gating — and Apply is the visible,
        // enabled way out, so gated never means wedged.
        var row = Assert.Single(back.FindAll(".mix-row"));
        Assert.Equal("NeverSeen", row.QuerySelector("option[selected]")!.GetAttribute("value"));
        Assert.True(StartButton(back).HasAttribute("disabled"));
        Assert.Contains("Apply or reset the mix", back.Markup);
        Assert.False(back.Find("#mixApply").HasAttribute("disabled"));

        await back.Find("#mixApply").ClickAsync(new());

        Assert.False(holder.Current.IsPassthrough); // the surviving edit, committed
        Assert.False(StartButton(back).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_UnbuildableDraft_GatesStart_ResetUngates()
    {
        // Matrix arm: an unbuildable draft is dirty by definition — it cannot
        // agree with any commitment, so Start gates while the panel's own
        // validation says why Apply is disabled. Reset remains the in-panel
        // escape (the blank commit), exactly as it was for the old wedge state
        // this replaces — gated always comes with a visible way out.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: StatsSaveCapability.Enabled);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        var holder = WithAppliedMix();

        var cut = Render<HomePage>();
        await cut.Find("#mixAddRow").ClickAsync(new());
        cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!.Input("85"); // sum ≠ 100

        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.True(cut.Find("#mixApply").HasAttribute("disabled")); // invalid — no commit via Apply

        await cut.Find("#mixReset").ClickAsync(new());

        Assert.True(holder.Current.IsPassthrough);
        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.False(StartButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_WeightedStart_EnabledButUnreadableStats_Refused_OverrideRunsPassthrough()
    {
        // The refusal ruling at its post-X reachable path. Under X the mix is
        // offered ONLY for an Enabled pick, so a committed mix can meet absent
        // stats in exactly one way: an Enabled pick whose stats file is
        // unreadable (the capability peek passes — stage 1 — but the bind yields
        // no document — stage 2). Start refuses with the actionable notice, no
        // navigation; the one-click override starts THIS quiz unweighted while
        // the stored mix survives. (The stage-1 no-capability refusal is now
        // UI-unreachable — no committed mix can coexist with a no-stats pick, see
        // Home_MixCommittedThenRepickNoStats — and stays covered at the
        // controller layer.)
        var c = WithWeighableController(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        sink.CanBindStats = true;    // capability peek passes (stage 1)
        sink.CurrentDocument = null; // ...but the bind yields no document (stage 2: unreadable file)
        WithPickedFolder(capability: StatsSaveCapability.Enabled);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        var holder = WithAppliedMix(NeverSeenMix());

        var cut = Render<HomePage>();
        await StartButton(cut).ClickAsync(new());

        Assert.False(c.HasStarted);
        Assert.Contains("weighted mix can't be applied", cut.Markup);
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        Assert.DoesNotContain("/quiz", nav.Uri);

        await cut.Find("#startWithoutMix").ClickAsync(new());

        Assert.True(c.HasStarted);
        Assert.Null(c.LastComposition);          // passthrough run
        Assert.False(holder.Current.IsPassthrough); // stored mix untouched
        Assert.EndsWith("/quiz", nav.Uri);
    }

    [Fact]
    public async Task Home_NoStatsPick_MixPanelHidden_StartRunsPassthrough()
    {
        // Task X: a no-stats pick can't provide the lifetime stats the mix
        // composes from, so the mix panel isn't offered at all. With no way to
        // build a mix (and every pick resetting any committed one), the mix plays
        // no part in Start — it runs plain: no panel, no mix gate, passthrough.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        WithAppliedMix();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: StatsSaveCapability.BrowserUnsupported);
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        await ApplyFiltersAsync(cut); // the pick reset the applied filter

        Assert.Empty(cut.FindComponents<MixPanelComponent>()); // no mix panel
        var startBtn = StartButton(cut);
        Assert.False(startBtn.HasAttribute("disabled")); // enabled, not mix-gated
        Assert.DoesNotContain("Apply or reset the mix", cut.Markup);

        await startBtn.ClickAsync(new());

        Assert.True(c.HasStarted);
        Assert.Null(c.LastComposition); // passthrough — no composition
        Assert.EndsWith("/quiz", nav.Uri);
    }

    [Fact]
    public async Task Home_MixCommittedThenRepickNoStats_ClearsMix_NoRefusal()
    {
        // Task X unreachability proof (why the early "your mix can't be provided"
        // advisory was removable): commit a mix under an Enabled pick, then
        // re-pick a no-stats folder. Every pick resets the committed mix, and the
        // no-stats pick hides the panel — so a stats-less pick can never coexist
        // with a committed non-blank mix, the exact state that advisory reported.
        // Start then runs plain, with no refusal.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        var holder = WithAppliedMix();
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        var cut = Render<HomePage>();

        // Enabled pick → panel shows; commit a mix through the real UI. The
        // filter Apply is not optional here: a pick expires the applied-filter
        // stamp, and Apply Mix is gated on a filter having been applied for the
        // current pick (§ MixApplyEnabled), so the pre-armed holder above no
        // longer satisfies it.
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: StatsSaveCapability.Enabled);
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        await ApplyFiltersAsync(cut);
        await ApplyMixThroughPanelAsync(cut);
        Assert.False(holder.Current.IsPassthrough); // committed

        // Re-pick a no-stats folder → the pick resets the committed mix and
        // discards the draft; the panel hides, so nothing re-hydrates and the
        // blank draft agrees with the reset holder — no gate, no divergence.
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: StatsSaveCapability.BrowserUnsupported);
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.True(holder.Current.IsPassthrough); // mix cleared by the pick
        Assert.True(Services.GetRequiredService<MixDraft>().Matches(holder.Current));
        Assert.Empty(cut.FindComponents<MixPanelComponent>()); // panel hidden

        // Start runs plain — no refusal, no composition. (The re-pick reset the
        // applied filter too, so the gate needs a fresh Apply first.)
        await ApplyFiltersAsync(cut);
        await StartButton(cut).ClickAsync(new());
        Assert.True(c.HasStarted);
        Assert.Null(c.LastComposition);
        Assert.DoesNotContain("weighted mix can't be applied", cut.Markup);
        Assert.EndsWith("/quiz", nav.Uri);
    }

    [Fact]
    public async Task Home_MixSurfaceAcrossPickRepickClear_NeverDiverges()
    {
        // The setup lifecycle across all three transitions, under discard-then-
        // re-hydrate. With a persisted mix in localStorage:
        //  • pick (Enabled): panel mounts, the draft hydrates the stored mix —
        //    shown ≠ committed (holder blank), so Start gates.
        //  • Apply: committed, un-gated.
        //  • re-pick (Enabled): the pick resets the committed mix AND discards
        //    the draft; the keyed panel re-mounts and re-hydrates, re-offering
        //    the persisted mix against the reset holder — gated again, and the
        //    panel never shows rows while the gate reads them as committed (the
        //    divergence a surviving-draft re-pick would leave).
        //  • Clear: the whole mix surface vanishes; nothing to Start.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        var holder = WithAppliedMix();
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult(NeverSeenMix().ToJson()); // persisted from a prior session
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: StatsSaveCapability.Enabled);

        var cut = Render<HomePage>();

        // Pick: panel mounts, hydration re-offers the persisted mix — gated.
        // (Each pick also resets the applied filter, so re-arm that half after
        // every pick to keep Start reading the mix half alone.)
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        await ApplyFiltersAsync(cut);
        Assert.NotEmpty(cut.FindAll(".mix-row"));
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains("Apply or reset the mix", cut.Markup);

        // Apply: committed, un-gated.
        await cut.Find("#mixApply").ClickAsync(new());
        Assert.False(holder.Current.IsPassthrough);
        Assert.False(StartButton(cut).HasAttribute("disabled"));

        // Re-pick (Enabled): reset + discard + keyed re-mount → re-hydrated,
        // re-offered, re-gated.
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        await ApplyFiltersAsync(cut);
        Assert.NotEmpty(cut.FindAll(".mix-row"));  // panel re-shows the persisted mix
        Assert.True(holder.Current.IsPassthrough);  // committed mix reset by the pick
        Assert.True(StartButton(cut).HasAttribute("disabled")); // Start re-gated
        Assert.Contains("Apply or reset the mix", cut.Markup);  // by the mix half specifically

        // Clear: the mix surface (and Start) vanish entirely, and — because
        // both mix halves are pick-coupled — holder and draft agree at
        // passthrough, completing the "no pick → passthrough" invariant.
        await cut.FindAll("button").First(b => b.TextContent.Trim() == "Clear").ClickAsync(new());
        Assert.Empty(cut.FindComponents<MixPanelComponent>());
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Start Quiz");
        Assert.True(holder.Current.IsPassthrough);
        Assert.True(Services.GetRequiredService<MixDraft>().Matches(holder.Current));
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
        WithPickedFolder(capability: StatsSaveCapability.Enabled); // mix panel is Enabled-gated (Task X)
        WithAppliedFilter(new FilterConfig());
        var shuffle = WithShuffleOption(enabled: true);
        WithAppliedMix(NeverSeenMix());

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

        var cut = Render<QuizPage>();

        var status = cut.Find("div.alert-info[role=status]");
        Assert.Contains("Your quiz has 3 problems: 3 Never seen + 0 Ever got wrong.", status.TextContent);
        Assert.DoesNotContain("requested", cut.Markup);
        Assert.DoesNotContain("ran short", cut.Markup);
        Assert.Empty(cut.FindAll("div.alert-warning"));
    }

    [Fact]
    public async Task Quiz_MixNotice_ShortfallVariant_RetiresOnFirstSubmittedPlay()
    {
        // The notice says how this quiz was built — read before answering, stale
        // chrome above the board for every problem after. The first submitted
        // answer retires it. Driven here on the assertive shortfall variant with a
        // checker play.
        var c = WithWeighableController(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        sink.CanBindStats = true;
        sink.CurrentDocument = DecisionStatsDocument.Empty;
        await c.StartAsync(new FilterConfig(), NeverSeenMix(quizLength: 5));

        var cut = Render<QuizPage>();
        Assert.Contains("Your quiz has 1 problem", cut.Markup);

        await SubmitPlayThroughPageAsync(cut, BestPlay());
        Assert.NotNull(c.Review); // the submit landed

        Assert.DoesNotContain("Your quiz has", cut.Markup);
        Assert.DoesNotContain("asked for 5 problems", cut.Markup);
        // Telemetry is untouched — the notice was dismissed, not deleted. The
        // problem counter reads the same composition.
        Assert.NotNull(c.LastComposition);
        Assert.Equal(1, c.ProblemCount);
    }

    [Fact]
    public async Task Quiz_MixNotice_CaplessVariant_RetiresOnFirstSubmittedCubeAnswer()
    {
        // The cube half of the same rule, on the polite capless variant: a cube
        // answer is a submitted answer too, so it retires the notice identically.
        var c = WithWeighableController(out var sink, TestFixtures.CubeDecision());
        sink.CanBindStats = true;
        sink.CurrentDocument = DecisionStatsDocument.Empty;
        await c.StartAsync(new FilterConfig(), NeverSeenMix()); // capless

        var cut = Render<QuizPage>();
        Assert.NotNull(cut.Find("div.alert-info[role=status]"));

        await AnswerCubeAsync(cut, new CubeDecisionPair(CubeAction.Double, CubeAction.Take));
        await cut.FindAll("button").First(b => b.TextContent.Trim() == "Submit").ClickAsync(new());

        Assert.DoesNotContain("Your quiz has", cut.Markup);
        Assert.Empty(cut.FindAll("div.alert-info[role=status]"));
    }

    [Fact]
    public async Task Quiz_MixNotice_DismissedThenShowStatsRoundTrip_StaysRetired()
    {
        // Why the dismissal is a scoped holder and not a page field: "Show stats"
        // is a mainline mid-quiz gesture and returning re-instantiates this page,
        // which would resurrect a notice the user had already dismissed. A fresh
        // render against the same composition must stay quiet.
        var c = WithWeighableController(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        sink.CanBindStats = true;
        sink.CurrentDocument = DecisionStatsDocument.Empty;
        await c.StartAsync(new FilterConfig(), NeverSeenMix(quizLength: 5));

        var cut = Render<QuizPage>();
        await SubmitPlayThroughPageAsync(cut, BestPlay());
        Assert.DoesNotContain("Your quiz has", cut.Markup);

        // Navigate away and back — a second Quiz instance over the same run.
        Assert.DoesNotContain("Your quiz has", Render<QuizPage>().Markup);
    }

    [Fact]
    public async Task Quiz_MixNotice_SkipIsNotADismissal()
    {
        // Skip moves past a problem without answering it, so the composition is
        // still the thing the user hasn't engaged with — the settled rule is
        // "first submitted answer", and Skip isn't one.
        var c = WithWeighableController(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp")),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp")));
        sink.CanBindStats = true;
        sink.CurrentDocument = DecisionStatsDocument.Empty;
        await c.StartAsync(new FilterConfig(), NeverSeenMix(quizLength: 5));

        var cut = Render<QuizPage>();
        Assert.Contains("Your quiz has", cut.Markup);

        await cut.FindAll("button").First(b => b.TextContent.Trim() == "Skip").ClickAsync(new());

        Assert.Contains("Your quiz has", cut.Markup);
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
    public async Task Home_PickScanning_DisablesSetupFieldsetAndShowsBusyCursor()
    {
        // Issue #48: after the browser's prompts, the app scans and buffers the
        // folder with nothing on screen to say so. The affordance must be up —
        // and *rendered*, not merely set — for that whole stretch. OnScanning
        // observes from inside it, which is the only place the claim is
        // falsifiable: asserting after the pick would pass even if the state had
        // never painted.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome();

        var cut = Render<HomePage>();
        Assert.False(cut.Find("fieldset").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("div.app-busy"));

        bool? fieldsetDisabledMidScan = null, busyCursorMidScan = null;
        _folderAccess.OnScanning = () =>
        {
            fieldsetDisabledMidScan = cut.Find("fieldset").HasAttribute("disabled");
            busyCursorMidScan = cut.FindAll("div.app-busy").Count > 0;
        };

        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.True(fieldsetDisabledMidScan);
        Assert.True(busyCursorMidScan);

        // …and lowered again once the summary is on screen.
        Assert.False(cut.Find("fieldset").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("div.app-busy"));
        Assert.Contains("1 problem file", cut.Markup);
    }

    [Fact]
    public async Task Home_PickCancelled_NeverRaisesTheBusyAffordance()
    {
        // A dismissed picker (or a declined view-files grant) does no work, so
        // there is nothing to be busy for — and the cancelled notice must land
        // on a live page, not a disabled one.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = FolderPickOutcome.CancelledOutcome;

        var cut = Render<HomePage>();
        var scanned = false;
        _folderAccess.OnScanning = () => scanned = true;

        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.False(scanned); // the fake only scans past a non-cancelled outcome
        Assert.False(cut.Find("fieldset").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("div.app-busy"));
        Assert.Contains("No folder is picked", cut.Markup);
    }

    [Fact]
    public async Task Home_PickFailsMidScan_LowersBusyAndShowsTheError()
    {
        // A scan that throws after the prompts succeeded — an over-cap folder is
        // the real instance — must not strand the page disabled with a progress
        // cursor and no way back. The busy state is lowered in a finally, so the
        // error banner lands on a usable page.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome();
        _folderAccess.OnScanning = () => throw new InvalidOperationException("too many files");

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.False(cut.Find("fieldset").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("div.app-busy"));
        Assert.Contains("too many files", cut.Markup);
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

    // -----------------------------------------------------------------------
    //  Settings.razor, and the board side it drives
    // -----------------------------------------------------------------------

    /// <summary>
    /// The settings instance the rendered pages resolve — the same app-scoped
    /// object <c>Program.cs</c> registers, so a test can drive a setting and then
    /// render a page against it exactly as the Settings page would have.
    /// </summary>
    private QuizSettings Settings() => Services.GetRequiredService<QuizSettings>();

    /// <summary>The <c>HomeBoardOnRight</c> the currently rendered board is asking the producer for.</summary>
    private static bool RenderedBoardSide(IRenderedComponent<QuizPage> cut) =>
        cut.FindAll(".board-container").Count > 0
            && cut.FindComponents<BackgammonPlayEntry>().Count > 0
            ? cut.FindComponent<BackgammonPlayEntry>().Instance.Request!.HomeBoardOnRight
            : cut.FindComponent<BackgammonDiagram>().Instance.Request!.HomeBoardOnRight;

    [Fact]
    public void Settings_RendersEveryControl_ReflectingTheStoredValues()
    {
        // The page is a view over the service and nothing else: what it shows is
        // what hydration put there, not a page-local default.
        WithController();
        JSInterop.Setup<string?>("localStorage.getItem", QuizSettings.StorageKey).SetResult(
            """{"homeBoardOnRight":false,"randomizeSidePerProblem":true,"keepNavigationPanelFolded":true}""");

        var cut = Render<SettingsPage>();

        Assert.False(cut.Find("#settingsSideRight").HasAttribute("checked"));
        Assert.True(cut.Find("#settingsSideLeft").HasAttribute("checked"));
        Assert.True(cut.Find("#settingsRandomizeSide").HasAttribute("checked"));
        Assert.True(cut.Find("#settingsKeepNavFolded").HasAttribute("checked"));
    }

    [Fact]
    public void Settings_HasNoApplyGesture_BecauseEveryChangeIsImmediate()
    {
        // Pinned as a design constraint, not a coincidence: an Apply button is
        // the front end of the draft/commit lifetime split that produced finding
        // (AK)'s wedge, and this page must never grow one.
        //
        // Stated as "no buttons at all", which stays exact: the one button the
        // page can render is Back to quiz, and with no quiz started it is absent
        // by its own predicate. So an Apply added later still fails here,
        // whatever it is called.
        WithController();

        var cut = Render<SettingsPage>();

        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public async Task Settings_ChangingAControl_AppliesAndPersistsOnTheSpot()
    {
        WithController();
        var cut = Render<SettingsPage>();

        await cut.Find("#settingsSideLeft").ChangeAsync(new() { Value = true });
        Assert.False(Settings().HomeBoardOnRight);

        await cut.Find("#settingsRandomizeSide").ChangeAsync(new() { Value = true });
        Assert.True(Settings().RandomizeSidePerProblem);

        await cut.Find("#settingsKeepNavFolded").ChangeAsync(new() { Value = true });
        Assert.True(Settings().KeepNavigationPanelFolded);

        // …and each landed in the one storage entry, with no further gesture.
        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == QuizSettings.StorageKey).Arguments[1] as string;
        Assert.Equal(
            """{"homeBoardOnRight":false,"randomizeSidePerProblem":true,"keepNavigationPanelFolded":true}""",
            stored);
    }

    [Fact]
    public async Task Settings_TurningTheFoldOn_LeavesThePanelTheUserIsOnAlone()
    {
        // Finding #50, from the control rather than the service: ticking the box
        // records the choice and nothing else moves. The panel folds on the next
        // navigation, off the value already in storage — which is why deferring
        // needed no code at all, only the removal of a call.
        //
        // This test previously asserted the opposite literal (the applier called
        // with true), because the shipped reading of "immediate apply" was
        // symmetric. The rule it protected — a setting must never look inert —
        // is now carried by the fine print beside the control, pinned below.
        WithController();
        var cut = Render<SettingsPage>();

        await cut.Find("#settingsKeepNavFolded").ChangeAsync(new() { Value = true });

        Assert.True(Settings().KeepNavigationPanelFolded);
        Assert.DoesNotContain("bgquizNavFold.apply", JSInterop.Invocations.Identifiers);
    }

    [Fact]
    public async Task Settings_TurningTheFoldOff_ReachesTheApplierImmediately()
    {
        // The direction that cannot wait, and the reason the seam exists at all:
        // with the panel folded, the navigations that would otherwise apply the
        // new value are behind the folded panel's own links. The page cannot do
        // this itself and neither can the service — the control is an
        // uncontrolled checkbox in the statically rendered layout.
        WithController();
        JSInterop.Setup<string?>("localStorage.getItem", QuizSettings.StorageKey).SetResult(
            """{"keepNavigationPanelFolded":true}""");
        var cut = Render<SettingsPage>();

        await cut.Find("#settingsKeepNavFolded").ChangeAsync(new() { Value = false });

        Assert.False(Settings().KeepNavigationPanelFolded);
        var apply = Assert.Single(JSInterop.Invocations["bgquizNavFold.apply"]);
        Assert.Equal(false, apply.Arguments[0]);
    }

    [Fact]
    public void Settings_FoldControl_SaysWhenItTakesHold()
    {
        // The words are load-bearing here in a way they are not for the other
        // two settings: a user who ticks the box and sees nothing happen has no
        // way to tell "deferred" from "broken". So the deferral is stated beside
        // the control, and pinned — dropping the sentence would leave the page
        // silently inert-looking, which is the failure #50 reported.
        WithController();
        var cut = Render<SettingsPage>();

        // Whitespace-collapsed before matching: razor renders the source's own
        // line breaks into the text, so a literal that happens to straddle one
        // fails for a reason that has nothing to do with the copy.
        var text = Regex.Replace(
            cut.Find("#settingsKeepNavFolded").Closest("fieldset")!
                .QuerySelector(".form-text")!.TextContent,
            @"\s+", " ");

        Assert.Contains("this only decides how a page starts out", text);
        Assert.Contains("takes hold as you move on from here", text);
    }

    [Fact]
    public async Task Settings_MidQuiz_OffersBackToQuiz_AndItNavigates()
    {
        // The way back the dogfood pass found missing (issue #30): the round trip
        // always worked — both the controller and the settings are app-scoped —
        // but nothing on the page pointed at it, so a user who changed the board
        // side mid-quiz was left with the browser's Back button and a guess.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        Assert.True(c.HasStarted && !c.IsFinished);
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        var cut = Render<SettingsPage>();
        var back = cut.FindAll("button").First(b => b.TextContent.Trim() == "Back to quiz");
        await back.ClickAsync(new());

        Assert.EndsWith("/quiz", nav.Uri);
    }

    [Fact]
    public void Settings_NoQuizInProgress_RendersWithoutRedirecting_AndOffersNoBackButton()
    {
        // Settings sits where Help sits, not where Stats sits: reachable from any
        // state — a cold deep link is the very visit the hydration gate exists
        // for — so it must never bounce, and with no quiz there is nowhere to go
        // back to.
        WithController();
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        var baseUri = nav.Uri;

        var cut = Render<SettingsPage>();

        Assert.Equal(baseUri, nav.Uri); // no redirect fired
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Back to quiz");
    }

    [Fact]
    public async Task Settings_QuizFinished_OffersNoBackButton()
    {
        // The other half of the predicate, and the half a HasStarted-only test
        // would miss: a finished quiz has no answering state to return to, which
        // is exactly why Stats redirects to /done on it.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync(); // exhausts → finished
        Assert.True(c.IsFinished);

        var cut = Render<SettingsPage>();

        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Back to quiz");
    }

    [Fact]
    public async Task Quiz_BoardSide_PlayAnsweringBranch_FollowsTheSetting()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        Assert.True(RenderedBoardSide(Render<QuizPage>()));   // default: home board right

        await Settings().SetHomeBoardOnRightAsync(false);
        Assert.False(RenderedBoardSide(Render<QuizPage>()));
    }

    [Fact]
    public async Task Quiz_BoardSide_CubeAnsweringBranch_FollowsTheSetting()
    {
        // The second of the three render branches. A cube problem's board region
        // is a bare BackgammonDiagram, built by a different method than the play
        // branch's — which is exactly how a setting ends up honored in some views
        // and not others.
        var c = WithController(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        Assert.True(RenderedBoardSide(Render<QuizPage>()));

        await Settings().SetHomeBoardOnRightAsync(false);
        Assert.False(RenderedBoardSide(Render<QuizPage>()));
    }

    [Fact]
    public async Task Quiz_BoardSide_SolutionBranch_FollowsTheSetting()
    {
        // The third branch, and the one built through Builder.From rather than
        // FromDecisionData.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        await Settings().SetHomeBoardOnRightAsync(false);

        var cut = Render<QuizPage>();
        await cut.InvokeAsync(() => c.SubmitPlay(BestPlay()));

        Assert.NotNull(c.Review);
        Assert.False(cut.FindComponent<BackgammonDiagram>().Instance.Request!.HomeBoardOnRight);
    }

    [Fact]
    public async Task Quiz_BoardSide_RandomizeOn_IsTheControllersRollForThisProblem()
    {
        // The composition rule, observed end to end: with randomization on the
        // board takes the controller's per-problem roll and the fixed choice
        // stops mattering. Asserted against the roll rather than against a
        // literal side — the roll is unseeded on purpose.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        await Settings().SetHomeBoardOnRightAsync(true);
        await Settings().SetRandomizeSidePerProblemAsync(true);

        Assert.Equal(c.RandomHomeBoardOnRight, RenderedBoardSide(Render<QuizPage>()));

        // The fixed choice is genuinely out of the picture while randomizing.
        await Settings().SetHomeBoardOnRightAsync(false);
        Assert.Equal(c.RandomHomeBoardOnRight, RenderedBoardSide(Render<QuizPage>()));
    }

    [Fact]
    public async Task Quiz_BoardSide_HoldsStillAcrossSubmitAndRedo()
    {
        // The constraint that makes randomization usable: one problem, one side.
        // Submitting must not flip the board the user is still looking at, and
        // Redo — which returns to the answering state on the SAME problem — must
        // not either. Both would read as the board moving under the user.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        await Settings().SetRandomizeSidePerProblemAsync(true);

        var cut = Render<QuizPage>();
        var answering = RenderedBoardSide(cut);

        await cut.InvokeAsync(() => c.SubmitPlay(BestPlay()));
        Assert.Equal(answering, RenderedBoardSide(cut));   // the solution review

        await cut.InvokeAsync(() => c.RedoAsync());
        Assert.Null(c.Review);
        Assert.Equal(answering, RenderedBoardSide(cut));   // back to answering
    }
}
