using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using BackgammonDiagram_Lib;
using BackgammonDiagram_Lib.Rendering;
using BgDataTypes_Lib;
using BgDiag_Razor.Components;
using BgGame_Lib;
using BgQuiz_Blazor.Client;
using BgFolderAccess_Razor;
using BgQuiz_Blazor.Client.Quiz;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;
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

        // Home injects the restored-filter notice's state and binds it to its
        // FilterSurface. Scoped, as in Program.cs — and in a bUnit fixture one
        // scope is one test, so a test's whole run is one "app boot": the first
        // panel mount that restores a stored selection arms the notice, and a
        // re-render or navigate-back within the same test re-arms rather than
        // announcing a second one. Registered fixture-wide because every Home
        // render needs it; the notice only ever shows where a test stages a
        // stored selection for the panel to restore.
        Services.AddScoped<FilterRestoreNotice>();

        // Home also injects the saved-filters storage adapter (over
        // IFolderAccess above) and hands it to its FilterSurface while the
        // pick's capability exposes a readable handle. The composite owns the
        // document lifecycle over it, so tests stage saved-filters content on
        // the fake's picked-slot properties (FiltersJson / LegacyFiltersJson)
        // and drive everything else through the rendered DOM.
        Services.AddScoped<PickedFolderFilterStorage>();

        // Home injects both halves of the mix state: MixConsent (the "Mix
        // applies" checkbox bit — fixture default is unchecked, so the mix is
        // simply not in effect; WithActiveMix stages a checked bit plus the
        // stored rows when a test needs a mix in effect) and MixDraft (the
        // app-scoped edit state MixPanel views; its hydration runs under each
        // test's JSInterop mode, resolving the bUnit IJSRuntime from the
        // container). The effective mix derives from the pair — the draft's
        // build when consented, passthrough otherwise — so tests arm it
        // through the panel UI or via the staged localStorage rows plus the
        // checkbox, never a stored copy.
        Services.AddScoped<MixConsent>();
        Services.AddScoped<MixDraft>();

        // Home, Quiz and Settings all inject QuizSettings. Scoped, as in
        // Program.cs, so one hydration serves every render in a test and the
        // Settings page's writes are visible to a Quiz page rendered after it —
        // which is the app-scoped behavior the side settings depend on.
        Services.AddScoped<QuizSettings>();

        // Quiz injects QuizNoticeDismissal (every notice checks it before
        // rendering). Scoped, as in Program.cs, so a test that re-renders the page
        // sees the same dismissal the first instance recorded — the navigate-back
        // case the holder exists for.
        Services.AddScoped<QuizNoticeDismissal>();
    }

    /// <summary>The sessionStorage key <see cref="QuizLiveMarker"/> reads/writes.</summary>
    private const string QuizLiveKey = "bgquiz.quizLive";

    private static Play BestPlay() => TestFixtures.MakePlay((8, 5), (8, 5));
    private static Play AltPlay() => TestFixtures.MakePlay((13, 11), (11, 8));

    /// <summary>
    /// A one-file <see cref="FolderPickOutcome"/> for scripting
    /// <see cref="FakeFolderAccess.NextPickOutcome"/> — the standard "the user
    /// picked a folder" payload for pick-flow tests. Nothing truncated unless a
    /// test says so: one file is nowhere near any cap.
    /// </summary>
    private static FolderPickOutcome OneFileOutcome(
        string folderName = "Corpus", string fileName = "match.xg",
        FolderWriteCapability capability = FolderWriteCapability.Enabled,
        params PickTruncation[] truncations) =>
        new(Cancelled: false, folderName, [new PickedFile(fileName, [1, 2, 3])], capability, truncations);

    private QuizController WithController(params BgDecisionData[] items)
    {
        var fake = new FakeProblemSetSource(items);
        var controller = new QuizController((_, _) => fake, new FakeProblemStatsSink(), TimeProvider.System);
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
    /// <param name="truncations">
    /// What the pick's count caps left unread — none by default. The injection
    /// point for the truncation-notice tests, which is also the state a
    /// navigated-back page re-derives from.
    /// </param>
    /// <param name="withStatsHistory">
    /// Whether the folder already holds a stats document with something in it —
    /// the second half of the mix predicate (issue
    /// <c>halheinrich/backgammon#87</c>), staged on the fake's picked slot where
    /// the probe reads it. Off by default and opted into explicitly at each call
    /// site rather than implied by <see cref="FolderWriteCapability.Enabled"/>:
    /// "this folder can save stats" and "this folder has some" are the two
    /// independent facts the predicate combines, and a fixture that quietly
    /// bundled them would hide exactly the distinction under test.
    /// </param>
    private PickedProblemFolder WithPickedFolder(
        string folderName = "Corpus", string fileName = "sample.xg",
        FolderWriteCapability capability = FolderWriteCapability.BrowserUnsupported,
        bool withStatsHistory = false,
        params PickTruncation[] truncations)
    {
        var folder = new PickedProblemFolder();
        folder.Set(folderName, [new PickedFile(fileName, [1, 2, 3])], capability, truncations);
        if (withStatsHistory) _folderAccess.PickedStatsJson = StatsDocumentJson(BestPlay());
        Services.AddSingleton(folder);
        return folder;
    }

    /// <summary>
    /// A real lifetime-stats document, serialized exactly as the app writes one
    /// (<see cref="QuizStatsFile.SerializerOptions"/> over the producer's
    /// bundled converter) — never a hand-written blob, so a wire-format change
    /// reaches these fixtures instead of passing them by. One folded submission,
    /// which is all the predicate asks about: <c>Count &gt; 0</c>.
    /// </summary>
    private static string StatsDocumentJson(Play play)
    {
        var decision = TestFixtures.TwoChoiceDecision(play, AltPlay());
        var doc = ProblemStatsDocument.Empty.Plus(
            new SubmittedPlay(TestFixtures.KeyOf(decision), play, 0, 0.0, IsCorrect: true),
            TimeProvider.System);
        return JsonSerializer.Serialize(doc, QuizStatsFile.SerializerOptions);
    }

    /// <summary>
    /// The empty-but-present stats document, in the same real wire format —
    /// #87's headline case: a file the user has, holding no decisions. The
    /// ruling is that this reads exactly as no file at all, so no test may
    /// treat it as a third state.
    /// </summary>
    private static string EmptyStatsDocumentJson() =>
        JsonSerializer.Serialize(ProblemStatsDocument.Empty, QuizStatsFile.SerializerOptions);

    /// <summary>
    /// Register an <see cref="AppliedFilter"/> (XgFilter_Razor's holder) for the
    /// rendered <c>Home</c> page. With <paramref name="applied"/> non-null the
    /// filter half of the gate is already satisfied — simulating navigate-back
    /// with a config the user applied earlier this session; otherwise it starts
    /// un-applied.
    /// </summary>
    /// <param name="pickGeneration">
    /// The pick the config is stamped as applied for — minted into the same
    /// <see cref="FilterSourceToken.FromGeneration"/> token Home's bindings use,
    /// so the mix-activation gate's comparison against the live
    /// <see cref="PickedProblemFolder.PickGeneration"/> reads it. The default
    /// matches the generation <see cref="WithPickedFolder"/> leaves behind (one
    /// <c>Set</c> ⇒ 1), so the common "already set up" fixture is coherent; a
    /// test probing the gate passes a mismatching value deliberately.
    /// </param>
    private void WithAppliedFilter(FilterConfig? applied = null, int pickGeneration = 1)
    {
        var holder = new AppliedFilter();
        if (applied is not null) holder.Set(applied, FilterSourceToken.FromGeneration(pickGeneration));
        Services.AddSingleton(holder);
    }

    /// <summary>
    /// The filter in effect for the folder held <i>right now</i>, or
    /// <see langword="null"/> when none is — the test-side mirror of Home's
    /// <c>FilterInEffect</c>, asking the holder the one question its surface
    /// answers. There is deliberately no way to ask "is anything applied at
    /// all": a config keyed to a superseded pick is not applied as far as any
    /// gate is concerned, and a test that could read it absolutely would be
    /// asserting something the page cannot see.
    /// </summary>
    private FilterConfig? FilterInEffect() =>
        Services.GetRequiredService<AppliedFilter>().ConfigFor(
            FilterSourceToken.FromGeneration(
                Services.GetRequiredService<PickedProblemFolder>().PickGeneration));

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
    /// Register a <see cref="MixConsent"/> for the rendered <c>Home</c> page,
    /// with <paramref name="mix"/> <b>in effect</b>: the checkbox bit checked
    /// and the localStorage rows staged to match, so the rendered panel's
    /// hydration fills the draft with that content and Home's effective mix
    /// derives to exactly <paramref name="mix"/> — simulating navigate-back
    /// with a mix the user activated earlier this session. Returns the consent
    /// bit so tests can assert its transitions.
    /// <para>
    /// Staging the blob beside the bit mirrors the app's invariant: the
    /// write-through persists every valid screen state, so a mix that could be
    /// activated always has its content in storage. Tests probing divergence
    /// (checked over something else on screen) edit the draft through the UI
    /// afterwards — under this model that <i>changes the effect</i> rather
    /// than gating it, which is exactly what those tests pin.
    /// </para>
    /// </summary>
    private MixConsent WithActiveMix(QuizMix mix)
    {
        var consent = new MixConsent();
        consent.Set(true);
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult(mix.ToJson());
        Services.AddSingleton(consent);
        return consent;
    }

    /// <summary>
    /// Register a <see cref="MixConsent"/> and return it — unchecked, the
    /// fixture default made explicit for tests that assert its transitions
    /// (the fixture's Scoped registration already serves renders that never
    /// touch it).
    /// </summary>
    private MixConsent WithMixConsent()
    {
        var consent = new MixConsent();
        Services.AddSingleton(consent);
        return consent;
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
    /// (<c>CanWeightMix</c> / <c>CurrentDocument</c>) before driving the UI.
    /// </summary>
    private QuizController WithWeighableController(
        out FakeProblemStatsSink sink, params BgDecisionData[] items)
    {
        var fake = new FakeProblemSetSource(items);
        sink = new FakeProblemStatsSink();
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
        // A folder with a stats record, so the mix panel is part of "the whole
        // setup surface" this test is about; the disclosure gate and the mix
        // predicate are independent, and only the former is under test here.
        _folderAccess.PickedStatsJson = StatsDocumentJson(BestPlay());

        var cut = Render<HomePage>();

        // Pre-pick: the pick button is present; everything downstream is hidden.
        Assert.NotNull(cut.Find("#pickProblemFolder"));
        Assert.Empty(cut.FindComponents<FilterSurface>());
        Assert.Empty(cut.FindComponents<MixPanelComponent>());
        Assert.Empty(cut.FindAll("#shuffleOrder"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Start Quiz");

        // Pick a folder with files → the setup surface appears.
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.Single(cut.FindComponents<FilterSurface>());
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

        // Summary renders straight from the persisted holder, no pick handler run,
        // under the markup-side caption that says what the folder IS (#96). The
        // caption is pinned here because it lives only in Home's markup: every
        // other pin on this line matches the holder's own Summary text, so all
        // of them would stay green with the caption gone.
        Assert.Contains("Problem folder:", cut.Markup);
        Assert.Contains("resume", cut.Markup);
        Assert.Contains("1 problem file", cut.Markup);

        // With both gates met (file already held + filters applied) Start enables.
        await ApplyFiltersAsync(cut);

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
        await ApplyFiltersAsync(cut);

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
            new FakeProblemStatsSink(), TimeProvider.System);
        Services.AddSingleton(controller);
        WithPickedFolder(); // satisfy the folder gate so Start is clickable
        WithAppliedFilter();
        WithShuffleOption();

        var cut = Render<HomePage>();

        // Type the player through the panel's own control (behind the
        // disclosure) and commit with its Apply — the real gesture, so the
        // config that reaches the pipeline is the one the panel built.
        await ExpandMoreFiltersAsync(cut);
        cut.Find("input[placeholder='e.g. Hal, Magriel']").Input("Alice");
        await ApplyFiltersAsync(cut);

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
        await ApplyFiltersAsync(cut);

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

        var cut = Render<HomePage>();
        await ApplyFiltersAsync(cut);

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

        var cut = Render<HomePage>();
        await ApplyFiltersAsync(cut);

        Assert.Contains("<strong>1</strong>", cut.Markup);
        Assert.Contains("decision matches your filters", cut.Markup);
        Assert.DoesNotContain("decisions match your filters", cut.Markup);
    }

    [Fact]
    public async Task Home_ApplyFilters_MixInEffect_CountCarriesThePoolCaveat()
    {
        // The count is filter-only (SummarizeMatchesAsync composes with QuizMix.Empty),
        // so with a mix in effect the number is the pool the quiz is *drawn from* —
        // the quiz itself can be far smaller. The caveat says so beside the number,
        // inside the same role="status" region, and the count stays pool-only (both
        // decisions here matched, so it still reads 2). The stats history is what
        // mounts the panel whose hydration fills the draft the effect derives from.
        WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: FolderWriteCapability.Enabled, withStatsHistory: true);
        WithAppliedFilter();
        WithShuffleOption();
        WithActiveMix(NeverSeenMix()); // checked, non-passthrough

        var cut = Render<HomePage>();
        await ApplyFiltersAsync(cut);

        var count = cut.FindAll("div[role=status]")
                       .First(d => d.TextContent.Contains("decisions match your filters"));
        Assert.Contains("2", count.TextContent);
        Assert.Contains("the quiz is drawn from these matches", count.TextContent);
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
        WithPickedFolder(capability: FolderWriteCapability.Enabled);
        WithAppliedFilter();
        WithShuffleOption();

        var cut = Render<HomePage>();
        await ApplyFiltersAsync(cut);

        Assert.Contains("decisions match your filters", cut.Markup);
        Assert.DoesNotContain("drawn from these matches", cut.Markup);
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

        var cut = Render<HomePage>();
        await ApplyFiltersAsync(cut);

        var region = MatchSummaryRegion(cut);
        Assert.Contains("0 decisions match your filters", Normalize(region.TextContent));
        Assert.DoesNotContain("By answer type", region.TextContent);
    }

    [Fact]
    public async Task Home_KnownEmptyPool_GatesStart_WithItsOwnHint()
    {
        // The zero-pool Start gate (found dogfooding, ruled): an applied filter
        // the page has just reported matching NOTHING must not leave Start
        // live to dead-end in the no-match outcome. Known-zero only — the gate
        // reads the resolved summary, so it takes no async dependency — and
        // the hint is its own sibling in the chain, stating this gate's reason.
        WithController(); // a corpus the filters match nothing in
        WithPickedFolder();
        WithAppliedFilter();
        WithShuffleOption();

        var cut = Render<HomePage>();
        await ApplyFiltersAsync(cut);

        Assert.Contains("0 decisions match your filters", Normalize(MatchSummaryRegion(cut).TextContent));
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains(cut.FindAll("small"), s => s.TextContent.Trim()
            == "No problems match the filters — adjust and re-apply them to enable Start.");
    }

    [Fact]
    public async Task Home_EmptyPoolGate_ReleasesWhenTheSummaryIsDropped()
    {
        // The known-zero-only half of the rule: editing the filter abandons the
        // summary (null again), so the pool gate releases and the ordinary
        // filter gate takes over with ITS hint — the zero-pool sentence must
        // not linger past the count it was about.
        WithController();
        WithPickedFolder();
        WithAppliedFilter();
        WithShuffleOption();

        var cut = Render<HomePage>();
        await ApplyFiltersAsync(cut);
        Assert.Contains("No problems match the filters", cut.Markup);

        await EditFilterControlAsync(cut);

        Assert.True(StartButton(cut).HasAttribute("disabled")); // now the filter's gate
        Assert.DoesNotContain("No problems match the filters", cut.Markup);
        Assert.Contains("Apply the filters above to enable Start", cut.Markup);
    }

    [Fact]
    public async Task Home_PanelReportsUncommittedEdits_ClearsMatchCount()
    {
        // Editing any filter control invalidates the shown count — it described
        // the now-abandoned config — so the notice disappears until the panel
        // reports clean again (a re-Apply, or the edit undone). The breakdown is
        // part of that same summary and goes with it: a stale make-up of an
        // abandoned pool is exactly as wrong as a stale number.
        WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder();
        WithAppliedFilter();
        WithShuffleOption();

        var cut = Render<HomePage>();
        await ApplyFiltersAsync(cut);
        Assert.Contains("decisions match your filters", cut.Markup);
        Assert.Contains("By answer type", cut.Markup);

        await EditFilterControlAsync(cut);

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
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.Enabled);

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
            OneFileOutcome(capability: FolderWriteCapability.BrowserUnsupported);

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
            OneFileOutcome(capability: FolderWriteCapability.PermissionDenied);

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
        var mixConsent = WithMixConsent();
        _folderAccess.NextPickOutcome = OneFileOutcome("First", "first.xg");
        _folderAccess.PickedStatsJson = StatsDocumentJson(BestPlay()); // so a mix can be built at all

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        await ApplyFiltersAsync(cut);
        await ActivateMixThroughPanelAsync(cut);

        var folder = Services.GetRequiredService<PickedProblemFolder>();
        Assert.True(folder.HasFiles);          // fully armed…
        Assert.NotNull(FilterInEffect());
        Assert.True(mixConsent.Applies);

        // …then a second pick gesture, sampled at the picker.
        bool? heldAtPicker = null, appliedAtPicker = null, mixedAtPicker = null;
        _folderAccess.OnPickCalled = () =>
        {
            heldAtPicker = folder.HasFiles;
            appliedAtPicker = FilterInEffect() is not null;
            mixedAtPicker = mixConsent.Applies;
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
        var mixConsent = WithMixConsent();
        _folderAccess.FiltersJson = SavedFiltersJson();
        _folderAccess.PickedStatsJson = StatsDocumentJson(BestPlay()); // so a mix can be built at all
        _folderAccess.NextPickOutcome = OneFileOutcome("Held", "held.xg");

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        await ApplyFiltersAsync(cut);
        await ActivateMixThroughPanelAsync(cut);
        Assert.Contains("Held", cut.Markup);
        Assert.Contains("Race", cut.Markup); // the folder's saved filter

        _folderAccess.NextPickOutcome = FolderPickOutcome.CancelledOutcome;
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        // The held folder is gone, along with everything scoped to it…
        var folder = Services.GetRequiredService<PickedProblemFolder>();
        Assert.False(folder.HasFiles);
        Assert.DoesNotContain("Held", cut.Markup);
        // The saved-filters context died with the composite the cleared folder
        // unmounted — no row, no panel, nothing to observe but its absence.
        Assert.DoesNotContain("Race", cut.Markup);
        Assert.Empty(cut.FindAll("#saveFilterName"));
        Assert.Null(FilterInEffect());
        // Both mix halves ended with the setup: consent revoked, draft
        // discarded to blank. (The STORED mix survives — §4's choice.)
        Assert.False(mixConsent.Applies);
        Assert.Empty(Services.GetRequiredService<MixDraft>().Rows);
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
            [new PickedFile("fb.xgp", [9, 9])], FolderWriteCapability.BrowserUnsupported,
            Truncations: []);

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
            Cancelled: false, "Empty", [], FolderWriteCapability.Enabled, Truncations: []);

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
        // Unexpected browser failure (or a file past the byte cap): the failure
        // idiom — assertive alert — and a cleared holder. A folder past the
        // *count* caps is not this: it truncates and reports (issue #59).
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

    /// <summary>
    /// Home's truncated-pick notice — scoped the way
    /// <see cref="MatchSummaryRegion"/> is, by role and a weak content marker,
    /// because the region carries no id and shares its alert styling with the
    /// stats-capability notice below it.
    /// </summary>
    private static AngleSharp.Dom.IElement TruncationNotice(IRenderedComponent<HomePage> cut) =>
        cut.FindAll("div[role=status]").First(d => d.TextContent.Contains("not read"));

    /// <summary>
    /// The truncation notice's lines as a reader sees them — whitespace
    /// collapsed, because the razor source's own line breaks ride into the
    /// rendered text.
    /// </summary>
    private static List<string> TruncationLines(IRenderedComponent<HomePage> cut) =>
        [.. TruncationNotice(cut).QuerySelectorAll("div").Select(d => Normalize(d.TextContent))];

    [Fact]
    public void Home_TruncatedPick_XgpOnly_ReportsThatKindFromTheConstants()
    {
        // Issue #59, the motivating case: a position library past the .xgp cap.
        // Blame-free and factual — what was used, and how many were not read —
        // with both figures from the constants the pick enforced, never literals.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        WithPickedFolder(truncations: [new PickTruncation(PickedFileLimits.XgpExtension, 340, PickedFileLimits.MaxXgpFileCount)]);

        var cut = Render<HomePage>();

        Assert.Equal(
            $"Using the first {PickedFileLimits.MaxXgpFileCount} .xgp files; 340 more were not read.",
            Assert.Single(TruncationLines(cut)));
        // An outcome, not a failure: the quiz runs on what was read.
        Assert.DoesNotContain("alert-danger", cut.Markup);
        Assert.Contains("role=\"status\"", cut.Markup);
    }

    [Fact]
    public void Home_TruncatedPick_XgOnly_ReportsThatKindAndAgreesWithOneLeftBehind()
    {
        // The other kind, and the singular: "1 more was not read" — a folder one
        // file past the cap is an ordinary case, not a rounding error.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        WithPickedFolder(truncations: [new PickTruncation(PickedFileLimits.XgExtension, 1, PickedFileLimits.MaxXgFileCount)]);

        var cut = Render<HomePage>();

        Assert.Equal(
            $"Using the first {PickedFileLimits.MaxXgFileCount} .xg files; 1 more was not read.",
            Assert.Single(TruncationLines(cut)));
    }

    [Fact]
    public void Home_TruncatedPick_BothKinds_ReportsBothLines()
    {
        // The caps are independent, so a big mixed folder can be past both — and
        // then both lines show, in the caps table's order.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        WithPickedFolder(truncations:
        [
            new PickTruncation(PickedFileLimits.XgExtension, 12, PickedFileLimits.MaxXgFileCount),
            new PickTruncation(PickedFileLimits.XgpExtension, 340, PickedFileLimits.MaxXgpFileCount),
        ]);

        var cut = Render<HomePage>();

        Assert.Equal(
            [
                $"Using the first {PickedFileLimits.MaxXgFileCount} .xg files; 12 more were not read.",
                $"Using the first {PickedFileLimits.MaxXgpFileCount} .xgp files; 340 more were not read.",
            ],
            TruncationLines(cut));
    }

    [Fact]
    public void Home_PickThatFit_ShowsNoTruncationNotice()
    {
        // The common case says nothing at all: a notice that fired on every pick
        // would train the reader to ignore the one that matters.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        WithPickedFolder();

        var cut = Render<HomePage>();

        Assert.DoesNotContain("were not read", cut.Markup);
        Assert.DoesNotContain("was not read", cut.Markup);
        Assert.DoesNotContain("Using the first", cut.Markup);
    }

    [Fact]
    public async Task Home_FolderPick_CarriesTheTruncationReportOntoTheHolder()
    {
        // The report travels pick → holder, not pick → page field: the notice is
        // about the folder being *held*, so it has to survive navigate-back the
        // way the capability notice does. Pinning the holder is what pins that.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(
            capability: FolderWriteCapability.Enabled,
            truncations: [new PickTruncation(PickedFileLimits.XgpExtension, 5, PickedFileLimits.MaxXgpFileCount)]);

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        var folder = Services.GetRequiredService<PickedProblemFolder>();
        var only = Assert.Single(folder.Truncations);
        Assert.Equal(PickedFileLimits.XgpExtension, only.Extension);
        Assert.Equal(5, only.OmittedCount);
        Assert.Contains("5 more were not read", Normalize(cut.Markup));
    }

    [Fact]
    public async Task Home_ClearAfterTruncatedPick_DropsTheNotice()
    {
        // Clear ends the setup, and the truncation is part of it: the report
        // describes a folder no longer held, so it must go with the folder.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(
            truncations: [new PickTruncation(PickedFileLimits.XgpExtension, 5, PickedFileLimits.MaxXgpFileCount)]);

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        Assert.Contains("Using the first", Normalize(cut.Markup));

        await cut.FindAll("button").First(b => b.TextContent.Trim() == "Clear").ClickAsync(new());

        Assert.DoesNotContain("Using the first", Normalize(cut.Markup));
        Assert.Empty(Services.GetRequiredService<PickedProblemFolder>().Truncations);
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
    public async Task Home_MidQuiz_OffersBackToQuiz_AndItNavigates()
    {
        // Issue #58: Home was the third page reachable mid-quiz — after Help and
        // Settings, which already carry this — and the last one with no way back.
        // Same predicate, same markup, same words as its two siblings.
        //
        // Rendered with NO folder picked on purpose: a mid-quiz Home usually
        // still holds the pick, but the affordance must not sit behind the
        // progressive-disclosure gate, which is where every other control on
        // this page lives. A user who Cleared the pick mid-quiz is exactly the
        // one who needs the way back.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        Assert.True(c.HasStarted && !c.IsFinished);
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        var cut = Render<HomePage>();
        var back = cut.FindAll("button").First(b => b.TextContent.Trim() == "Back to quiz");

        // Outside the busy fieldset: it navigates and drives no transition, so
        // it follows the Show-stats / Back-to-setup convention of staying live
        // while the page works. Inside, the native disabled would take the way
        // back away exactly while a long parse makes it most wanted.
        Assert.Null(back.Closest("fieldset"));

        await back.ClickAsync(new());
        Assert.EndsWith("/quiz", nav.Uri);
    }

    [Fact]
    public void Home_NoQuizInProgress_OffersNoBackButton()
    {
        // The ordinary cold visit: there is no quiz to go back to, so the
        // affordance is simply absent — Home never redirects either way.
        WithController();
        WithAppliedFilter();
        WithShuffleOption();

        var cut = Render<HomePage>();

        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Back to quiz");
    }

    [Fact]
    public async Task Home_QuizFinished_OffersNoBackButton()
    {
        // The other half of the predicate, and the half a HasStarted-only test
        // would miss: a finished quiz has no answering state to return to.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync(); // exhausts → finished
        Assert.True(c.IsFinished);

        var cut = Render<HomePage>();

        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Back to quiz");
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
            [new PickedFile("fb.xgp", [9, 9])], FolderWriteCapability.BrowserUnsupported,
            Truncations: []);

        var cut = Render<HomePage>();
        await cut.Find("#problemFolderFallback").ChangeAsync(new ChangeEventArgs());

        var folder = Services.GetRequiredService<PickedProblemFolder>();
        Assert.True(folder.HasFiles);
        Assert.Equal("fb.xgp", Assert.Single(folder.Files).FileName);
        Assert.Equal(FolderWriteCapability.BrowserUnsupported, folder.Capability);
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
        // its config into the FilterPanel as a bulk edit. The staged config is not
        // the committed one, so the panel's applied-state report carries null,
        // which clears AppliedFilter and re-gates Start until the user re-Applies.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.Enabled);
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
        // …and the load's null applied-state report cleared the applied filter,
        // re-gating Start.
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
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.Enabled);
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
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.Enabled);
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
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.Enabled);
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
    public async Task Home_ClearFilters_ClearsAStaleSaveRefusal()
    {
        // The save refusal is mooted by a commit, not only by an edit: the
        // applied-state report fires on every buffer-affecting gesture, so the
        // handler clears the notice for commits too. "Clear filters" is the
        // gesture that reaches this state — it commits without requiring a
        // parseable pattern, where Apply is disabled by exactly the condition
        // that produced the refusal, so a user holding an invalid pattern can
        // commit their way out but not Apply their way out.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.Enabled);
        _folderAccess.FiltersJson = null;

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        await ExpandMoreFiltersAsync(cut);
        cut.Find("#positionPattern").Input("[6,2"); // unparseable → save refused
        cut.Find("#saveFilterName").Input("Bad");
        await ClickSavedFilterButtonByTextAsync(cut, "Save");
        Assert.Contains("position pattern is invalid", cut.Markup);

        await cut.Find("#clearFilters").ClickAsync(new());

        Assert.DoesNotContain("position pattern is invalid", cut.Markup);
    }

    [Fact]
    public async Task Home_CorruptFiltersFile_ShowsNoticeHidesPanel_FileUntouched()
    {
        // A corrupt xg-filters.json degrades to LoadFailed: the panel is
        // replaced by the notice (naming the file), and the file is never
        // overwritten — the zero-writes preservation guarantee.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.Enabled);
        _folderAccess.FiltersJson = "{ not valid json";

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.Contains(SavedFiltersDocument.FileName, cut.Markup);
        Assert.Contains("couldn't be read", cut.Markup);
        Assert.Empty(cut.FindAll("#saveFilterName")); // panel not rendered
        Assert.Empty(_folderAccess.FiltersWrites);
    }

    [Fact]
    public async Task Home_LegacyFiltersFile_LoadsViaFallback_AndFirstSaveWritesCanonical()
    {
        // The ratified tester-migration behavior, host-side: a folder whose
        // filters were saved under the legacy name (and which has no canonical
        // file) keeps loading — the producer's store reads the canonical name
        // first and falls back to the legacy one only when it is absent. The
        // first save then writes the canonical name; the legacy file is never
        // deleted (the fake's legacy slot is untouched by writes, mirroring the
        // real module's name-parameterized I/O).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.Enabled);
        _folderAccess.FiltersJson = null;                    // no canonical file yet
        _folderAccess.LegacyFiltersJson = SavedFiltersJson(); // the tester's existing file

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        // The legacy document loaded: its filter is offered.
        Assert.NotNull(FindSavedFilterRowButton(cut, "Race", "Load"));

        cut.Find("#saveFilterName").Input("MyFilter");
        // The save-as button by its id — a row Save also labels itself "Save",
        // so text alone is ambiguous once a filter is listed.
        await cut.Find("#saveFilterButton").ClickAsync(new());

        // One write, to the canonical name, carrying both filters; the legacy
        // content is left exactly as it was.
        Assert.Equal([SavedFiltersDocument.FileName], _folderAccess.PickedWriteNames);
        Assert.True(NamedFilterCollection.TryFromJson(
            Assert.Single(_folderAccess.FiltersWrites), out var written));
        Assert.True(written.Contains("Race"));
        Assert.True(written.Contains("MyFilter"));
        Assert.Equal(SavedFiltersJson(), _folderAccess.LegacyFiltersJson);
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
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.PermissionDenied);
        _folderAccess.FiltersJson = SavedFiltersJson();

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        // The reason spells out that Delete is disabled too, not just Save — the
        // panel offers both persistence gestures and PermissionDenied bars both.
        // Two Save buttons render now (the row's overwrite-Save and save-as),
        // and the one capability ruling must disable them both.
        Assert.Contains("saved filters can be loaded but not changed or deleted", cut.Markup);
        var saveButtons = cut.FindAll("button").Where(b => b.TextContent.Trim() == "Save").ToList();
        Assert.NotEmpty(saveButtons);
        Assert.All(saveButtons, b => Assert.True(b.HasAttribute("disabled")));
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
            [new PickedFile("fb.xgp", [9, 9])], FolderWriteCapability.BrowserUnsupported,
            Truncations: []);
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
        // it. A fresh folder (no saved-filters file under either name) reads
        // as Ready over an empty
        // collection, so Count is 0 and the whole section is suppressed (panel and
        // its load-only reason both).
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.PermissionDenied);
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
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.PermissionDenied);
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
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.Enabled);
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
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.PermissionDenied);
        _folderAccess.FiltersReadException = new JSException("read withheld"); // → LoadFailed

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.Empty(cut.FindAll("#saveFilterName")); // panel hidden (empty, can't save)
        Assert.Contains(SavedFiltersDocument.FileName, cut.Markup);
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
    public async Task Home_PanelReportsUncommittedEdits_ClearsAppliedState_DisablesStart()
    {
        // Gate semantics guard: "applied" means the user deliberately applied, not
        // merely that a config exists. Editing any filter control makes the panel
        // report that its buffers equal no committed config, which must clear the
        // applied holder so a half-edited set re-disables Start — even with a file
        // still picked.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder();
        WithAppliedFilter(new FilterConfig()); // start from an applied, enabled state
        WithShuffleOption();

        var cut = Render<HomePage>();

        // Both gates met → enabled.
        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.False(startBtn.HasAttribute("disabled"));

        // User edits a filter → nothing committed matches → applied state
        // cleared → disabled again.
        await EditFilterControlAsync(cut);

        startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.True(startBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_CleanReportAfterUndoneEdit_ReAppliesTheCommittedConfig()
    {
        // The other direction of the same wire, and the reason the wiring
        // exists: a *clean* report re-applies. The panel raises it whenever its
        // buffers equal what it committed — including an edit undone back to
        // the applied values — and the composite must re-arm Start from it,
        // because the panel's own Apply is disabled in exactly that state
        // (nothing new to commit). Driven entirely through the always-visible
        // error-range control: commit a Min of 0.75, edit it away, undo it
        // back. (Home_UndoingAnEdit_ReArmsStartAndRestoresTheMatchCount pins
        // the same arc through a disclosure control plus the match count.)
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder();
        WithAppliedFilter();
        WithShuffleOption();

        var cut = Render<HomePage>();
        await EditFilterControlAsync(cut); // Min = 0.75, uncommitted: Start gated
        Assert.True(StartButton(cut).HasAttribute("disabled"));

        await ApplyFiltersAsync(cut); // commit it — Start arms
        Assert.False(StartButton(cut).HasAttribute("disabled"));

        await UndoFilterEditAsync(cut); // Min blank ≠ committed 0.75: re-gated
        Assert.True(StartButton(cut).HasAttribute("disabled"));

        await EditFilterControlAsync(cut); // back to the committed values

        Assert.False(StartButton(cut).HasAttribute("disabled"));
        // Re-set from the payload, not merely un-cleared: the config the quiz
        // would be built from is the one the panel reported clean.
        Assert.Equal(0.75, FilterInEffect()!.ErrorMin);
    }

    [Fact]
    public void Home_BindsTheFilterSurfaceCallbacks()
    {
        // The migration's own proof, and it has to be render-level: a binding
        // left on the composite's *previous* parameter name compiles clean and
        // throws only when it is first rendered with it. Asserting HasDelegate
        // goes one better than "it rendered" — it pins that Home supplies the
        // handlers rather than letting the attribute splat. FilterSurface is
        // consumer surface (unlike the .Internal panels), so locating the
        // component itself is inside the ruled boundary.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder();
        WithAppliedFilter();
        WithShuffleOption();

        var surface = Render<HomePage>().FindComponent<FilterSurface>().Instance;

        Assert.True(surface.OnAppliedStateChanged.HasDelegate);
        Assert.True(surface.OnFilterConfigChanged.HasDelegate);
    }

    /// <summary>
    /// A stored filter selection for the composite's first-render restore to
    /// find, answered <i>by exclusion</i>: the panel's <c>localStorage</c> key
    /// is a producer internal this host may not name (the producer keeps those
    /// constants <c>internal</c> precisely so no consumer depends on them), so
    /// this matches every <c>localStorage.getItem</c> for a key BgQuiz does not
    /// own. Everything left over on a Home render belongs to the composite, and
    /// the panel's other restore — its disclosure flag — reads this tolerantly
    /// (anything but <c>"true"</c> keeps the collapsed default).
    /// </summary>
    private void WithStoredFilterSelection(FilterConfig stored)
    {
        string[] hostKeys = [MixDraft.StorageKey, QuizSettings.StorageKey];
        JSInterop.Setup<string?>(
            "localStorage.getItem",
            invocation => invocation.Arguments is [string key] && !hostKeys.Contains(key))
            .SetResult(stored.ToJson());
    }

    [Fact]
    public async Task Home_RestoredFilterSelection_ShowsTheNotice_UntilAnEditSupersedesIt()
    {
        // This host's half of the spec's §4 legibility rule. The notice's
        // mechanics — when it arms, when it dies, that a remount re-arms it —
        // are the producer's to pin and are pinned there; what only this repo
        // can prove is that its own wiring is live: FilterRestoreNotice is
        // registered (at app scope, beside AppliedFilter) and bound to the
        // hosted FilterSurface, so the producer's decision actually reaches
        // this page. A missing registration throws at render and an unbound
        // parameter leaves the composite arming an instance nobody shows —
        // neither of which any other test here would catch.
        //
        // One bUnit fixture is one scope, so this test is one app boot: the
        // stored selection below is a previous session's, restored by the
        // panel's first render after the pick mounts it.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        WithStoredFilterSelection(new FilterConfig { ErrorMin = 0.5 });
        _folderAccess.NextPickOutcome = OneFileOutcome();

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        // The restore is interop-driven and lands after the pick's render, so
        // wait for it rather than sampling whatever paint the click returned on.
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("#filterRestoredNotice")));

        // The selection becomes the user's own — the notice's statement stops
        // holding, so it goes.
        await EditFilterControlAsync(cut);

        Assert.Empty(cut.FindAll("#filterRestoredNotice"));

        // And it stays gone across a navigate-back, which is the half that pins
        // the *lifetime* rather than the binding: a second Home instance mounts
        // a second panel that restores the same stored selection all over
        // again, and the only reason it does not announce it a second time is
        // that Home resolved the app-scoped instance the first edit spent.
        // Registered Transient this assertion fails while everything above
        // still passes — which is exactly the mistake worth a pin, since §4
        // says navigation changes nothing.
        var back = Render<HomePage>();

        // Wait on the restore itself before asserting the absence: the stored
        // 0.5 landing in the fresh panel's buffers (over the 0.75 the edit above
        // typed into the panel that just died) is the positive signal that the
        // arming path ran and declined. Asserting "no notice" without it would
        // pass on the paint before the interop even returned.
        back.WaitForAssertion(() =>
            Assert.Equal("0.5", back.Find("input[placeholder='Min']").GetAttribute("value")));
        Assert.Empty(back.FindAll("#filterRestoredNotice"));
    }

    [Fact]
    public async Task Home_UndoingAnEdit_ReArmsStartAndRestoresTheMatchCount()
    {
        // The scenario the migration exists for, driven through the panel's own
        // controls: apply → edit → Start re-gates → undo the edit → the panel is
        // clean again and says so, so Start re-arms with *no* re-Apply. It has to
        // be without one — the panel's Apply is disabled whenever the buffers
        // equal the last-committed config, so the old "re-click Apply to
        // recover" gesture no longer exists and an edit-then-undo would strand
        // the user behind two dead buttons.
        //
        // The match count is the other half of the same trap. The edit dropped it
        // (it described the abandoned config) and Apply can't bring it back, so
        // the re-affirm must recompute it, not merely leave it gone.
        WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome();

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        // Commit a distinctive config through the panel's own Apply button, so
        // what comes back on the undo is identifiably the committed one. Players
        // sits behind the panel's disclosure.
        await ExpandMoreFiltersAsync(cut);
        cut.Find("input[placeholder='e.g. Hal, Magriel']").Input("Magriel");
        await ApplyFiltersAsync(cut);
        Assert.False(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains("decisions match your filters", cut.Markup);

        // Edit away from it: the buffers now equal no committed config.
        await cut.Find("input[placeholder='e.g. Hal, Magriel']")
                 .InputAsync(new ChangeEventArgs { Value = "Magriel, Robertie" });
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.DoesNotContain("decisions match your filters", cut.Markup);

        // Undo the edit — nothing else.
        await cut.Find("input[placeholder='e.g. Hal, Magriel']")
                 .InputAsync(new ChangeEventArgs { Value = "Magriel" });

        // The report is fire-and-forget on the panel's side and the recount runs
        // through the busy affordance's yield, so wait for it rather than
        // sampling the markup the dispatch happened to return on.
        cut.WaitForAssertion(() =>
        {
            Assert.False(StartButton(cut).HasAttribute("disabled"));
            Assert.Contains("decisions match your filters", cut.Markup);
        });
        Assert.Equal(["Magriel"], FilterInEffect()!.Players);
    }

    [Fact]
    public async Task Home_Apply_CountsTheMatchesOnce()
    {
        // Idempotence at the seam where the two callbacks overlap: a commit
        // raises OnFilterConfigChanged *and* then the applied-state report
        // carrying the config it just committed. Both paths can summarize, so
        // without the already-current guard one Apply would parse the corpus
        // twice and flash the busy affordance twice. EnumerateCallCount is the
        // observable — SummarizeMatchesAsync enumerates the source once per call.
        var fake = new FakeProblemSetSource([TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay())]);
        Services.AddSingleton(
            new QuizController((_, _) => fake, new FakeProblemStatsSink(), TimeProvider.System));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome();

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        await ApplyFiltersAsync(cut);

        Assert.Contains("decision matches your filters", cut.Markup);
        Assert.Equal(1, fake.EnumerateCallCount);
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
        await ApplyFiltersAsync(cut);

        Assert.False(StartButton(cut).HasAttribute("disabled"));

        // Analysis depth sits behind the panel's disclosure, like every facet but
        // the error range. The mode toggle ids are the panel's own
        // md_<AnalysisMode> convention — a level group renders only once its mode
        // is checked, which is itself part of what a bare toggle click asserts.
        await ExpandMoreFiltersAsync(cut);
        await cut.Find($"#md_{AnalysisMode.Rollout}").ChangeAsync(new ChangeEventArgs { Value = true });

        // The edit is a dirty signal like any other: Start re-gates until Apply.
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Null(FilterInEffect());

        await ApplyFiltersAsync(cut);

        var applied = FilterInEffect();
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
        await ApplyFiltersAsync(cut);

        Assert.Equal("Magriel",
            cut.Find("input[placeholder='e.g. Hal, Magriel']").GetAttribute("value"));
        Assert.NotNull(FilterInEffect());
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
        Assert.Null(FilterInEffect());
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains("Apply the filters above to enable Start", cut.Markup);
        // …and the count that described the old corpus is gone.
        Assert.DoesNotContain("decisions match your filters", cut.Markup);
    }

    [Fact]
    public async Task Home_RePickAfterApplyingTheDefaults_StillResetsTheAppliedFilter()
    {
        // The same rule as above, on the path where the panel's applied-state
        // report actively contradicts it. The reset stages defaults into the
        // still-mounted panel, and a panel that committed the defaults reports
        // that staging as *clean* — true about the panel, and exactly wrong
        // here: the folder is already gone. Mirroring it would re-apply the
        // filter the reset had just cleared, and the pick would land with Start
        // armed against a corpus the filter was never weighed against.
        //
        // The case above escapes this by accident (its applied config carries a
        // player, so the staged defaults differ from it). Applying the panel as
        // it comes — the commonest gesture there is — does not.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome("First", "first.xg");

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        await ApplyFiltersAsync(cut); // commits the defaults, unedited
        Assert.NotNull(FilterInEffect());

        _folderAccess.NextPickOutcome = OneFileOutcome("Second", "second.xg");
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.Null(FilterInEffect());
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains("Apply the filters above to enable Start", cut.Markup);
        Assert.DoesNotContain("decision matches your filters", cut.Markup);
    }

    [Fact]
    public async Task Home_StartClick_EmptyFilterResult_ShowsBannerAndStaysHome()
    {
        // The empty-result BACKSTOP: a filter set matching zero decisions makes
        // StartAsync exhaust immediately (IsFinished true straight away), and
        // the page stays on / with the no-match banner rather than bouncing
        // through a 0/0 /quiz → /done. On the primary path the zero-pool gate
        // now darkens Start before this can happen (the known-zero count —
        // pinned in Home_KnownEmptyPool_GatesStart_WithItsOwnHint), so this
        // scenario drives the click programmatically past the disabled button,
        // exactly the race-with-the-count case the backstop is ruled to cover.
        var controller = WithController(); // empty source → finishes on Start
        WithPickedFolder();
        WithAppliedFilter();
        WithShuffleOption();
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        var cut = Render<HomePage>();
        await ApplyFiltersAsync(cut);

        var startBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Start Quiz");
        Assert.True(startBtn.HasAttribute("disabled")); // the gate holds; the click below is the backstop probe
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
        await ApplyFiltersAsync(cut);

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
        await ApplyFiltersAsync(cut);

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
        await ApplyFiltersAsync(cut);

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
        await ApplyFiltersAsync(cut);

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
            new FakeProblemStatsSink(), TimeProvider.System);
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
                BestPlay(), AltPlay(), id: new XgpDecisionId($"test{i}.xgp"), away: i + 1))
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
        await ApplyFiltersAsync(cut);

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
        await ApplyFiltersAsync(cut);

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

    /// <summary>The Quiz page's End-quiz control (issue #57), by its visible label.</summary>
    private static AngleSharp.Dom.IElement EndQuizButton(IRenderedComponent<QuizPage> cut) =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == "End quiz");

    [Fact]
    public async Task Quiz_AnsweringState_EndQuizFinishesTheRunAndLandsOnDone()
    {
        // The wiring the controller test cannot see: the button exists in the
        // answering row, its click reaches EndQuizAsync, and the page's existing
        // IsFinished redirect carries the user to Done — an early end arriving
        // there by exactly the same route a completed run does.
        var c = WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        var cut = Render<QuizPage>();
        await EndQuizButton(cut).ClickAsync(new());

        Assert.True(c.IsFinished);
        Assert.EndsWith("/done", nav.Uri);
    }

    [Fact]
    public async Task Quiz_ReviewState_AlsoOffersEndQuiz()
    {
        // Both action rows carry it. That is not cosmetic symmetry: reading a
        // solution is one of the two moments a user decides they have had
        // enough, and adding the button to both rows is also what keeps their
        // relative heights — and so the board's flex remainder — unchanged.
        var c = WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());
        Assert.NotNull(c.Review);

        var cut = Render<QuizPage>();

        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Continue");
        Assert.NotNull(EndQuizButton(cut));
    }

    [Fact]
    public async Task Quiz_EndQuiz_SitsAfterShowStatsInTheTrailingCluster()
    {
        // Placement is the entire mitigation for a control that acts on one
        // click with no confirmation: it sits at the far end of the row, past
        // Show stats, as far as the row allows from the button a user clicks
        // over and over. Pinned so a later tidy-up cannot quietly move it next
        // to Submit.
        var c = WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        var cut = Render<QuizPage>();
        var labels = cut.FindAll(".action-row button")
            .Select(b => b.TextContent.Trim()).ToList();

        Assert.Equal("End quiz", labels[^1]);   // the far end of the row...
        Assert.Equal("Submit", labels[0]);      // ...and the answer control is at the near end
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
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"), away: 1),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"), away: 2));
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
        var c = new QuizController((_, _) => fake, new FakeProblemStatsSink(), TimeProvider.System);
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
        var seen = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"), away: 1);
        var unseen = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"), away: 2);
        var c = WithWeighableController(out var sink, seen, unseen);
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty.Plus(
            new SubmittedPlay(TestFixtures.KeyOf(seen), BestPlay(), 0, 0.0, IsCorrect: true),
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
        folder.Set("Corpus", [new PickedFile("a.xgp", [1])], FolderWriteCapability.Enabled, []);
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
                    TestFixtures.KeyOf(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay())),
                    TestFixtures.MakePlay((8, 5)), 0, 0.0, true));
                break;
        }

        Assert.Equal(status, store.Status); // helper sanity: the drive worked
        Services.AddSingleton(store);
        return store;
    }

    /// <summary>
    /// Register a real <see cref="QuizStatsStore"/> that has just retired a v1
    /// stats file, driven through its own bind against one — the only way to
    /// reach the state, since the retirement is a consequence of what the folder
    /// held and not a setting.
    /// </summary>
    private async Task<QuizStatsStore> WithRetiredStatsStoreAsync()
    {
        var access = new FakeFolderAccess { StatsJson = RetiredStatsFixture.V1Json };
        var folder = new PickedProblemFolder();
        folder.Set("Corpus", [new PickedFile("a.xgp", [1])], FolderWriteCapability.Enabled, []);
        var store = new QuizStatsStore(access, TimeProvider.System, folder);

        await store.BeginQuizAsync();

        Assert.NotNull(store.StatsRetiredOccurrence); // helper sanity: the drive worked
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
        Assert.DoesNotContain("set aside", cut.Markup);
        Assert.DoesNotContain(QuizStatsFile.RetiredFileName, cut.Markup);
    }

    [Fact]
    public async Task Quiz_StatsRetired_ShowsPoliteRestartNotice()
    {
        // The retirement report: both file names from their constants (so the
        // prose cannot drift from what was written), the polite idiom because
        // this is an outcome and not a failure, and the quiz running normally
        // beneath it — the new file records from this quiz on.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        await WithRetiredStatsStoreAsync();

        var cut = Render<QuizPage>();

        Assert.Contains(QuizStatsFile.RetiredFileName, cut.Markup);
        Assert.Contains(QuizStatsFile.FileName, cut.Markup);
        Assert.Contains("set aside", cut.Markup);
        Assert.Contains("begin again", cut.Markup);
        Assert.Contains("role=\"status\"", cut.Markup);
        Assert.Contains("Submit", cut.Markup);           // quiz still fully functional
        Assert.DoesNotContain("couldn't be read", cut.Markup); // and not reported as a failure
    }

    [Fact]
    public async Task Quiz_StatsRetiredNotice_IsDismissible()
    {
        // Dismissible like every notice above the board, and on its own slot:
        // this one can be showing while a degrade notice is too, so dismissing
        // it must not depend on there being no other.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        await WithRetiredStatsStoreAsync();

        var cut = Render<QuizPage>();
        Assert.Contains("set aside", cut.Markup); // positive precondition

        await cut.Find(".quiz-notice").ClickAsync(new());

        Assert.DoesNotContain("set aside", cut.Markup);
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
    public async Task Done_StatsRetired_ShowsTheRestartNotice()
    {
        // Mirrored from Quiz: what happened to the stats context is exactly what
        // someone reading their results wants to know, and a restarted lifetime
        // record is the most consequential thing that can have happened to it.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();
        await WithRetiredStatsStoreAsync();

        var cut = Render<DonePage>();

        Assert.Contains(QuizStatsFile.RetiredFileName, cut.Markup);
        Assert.Contains("set aside", cut.Markup);
        Assert.Contains("role=\"status\"", cut.Markup);
        // A retirement is not a recording failure, so the page's "nothing needs
        // saving" line — gated on the two failure statuses — still stands.
        Assert.Contains("Nothing here needs saving", cut.Markup);
    }

    [Fact]
    public async Task Done_StatsNotRetired_ShowsNoRestartNotice()
    {
        // The absence half, keyed on the same wording the present half asserts.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync();

        var cut = Render<DonePage>();

        Assert.DoesNotContain("set aside", cut.Markup);
        Assert.DoesNotContain(QuizStatsFile.RetiredFileName, cut.Markup);
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
        var actionRow = cut.Find(".action-row");
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
        // The decision carries an XGID, so the answering view renders it as
        // selectable text plus a copy button in the bottom row.
        const string xgid = "XGID=-b----E-C---eE---c-e----B-:0:0:1:00:0:0:0:0:10";
        var c = WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), xgid: xgid));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        var cut = Render<QuizPage>();

        Assert.Contains("xgid-label", cut.Markup);

        // The full value is in the DOM twice over — as the element's text and as
        // its title — even though the CSS clips the visible part to the "XGID="
        // prefix. That is what keeps the clip visual only: a select-all, a
        // screen reader and the tooltip all still get the whole string.
        var text = cut.Find(".xgid-label-text");
        Assert.Equal(xgid, text.TextContent);
        Assert.Equal(xgid, text.GetAttribute("title"));

        // The copy control is icon-only, so its accessible name is real text in
        // a visually-hidden span rather than a caption — asserted as the name,
        // not as the glyph, which is a CSS background bUnit cannot see.
        var copy = cut.Find(".xgid-label-copy");
        Assert.Equal("Copy XGID to clipboard", copy.TextContent.Trim());
        Assert.Contains("visually-hidden", copy.QuerySelector("span")!.ClassList);
    }

    [Fact]
    public async Task Quiz_ProblemWithoutXgid_HidesXgidLabel()
    {
        // Empty XGID (the fixture default) renders no badge at all.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        var cut = Render<QuizPage>();

        Assert.DoesNotContain("xgid-label", cut.Markup);
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
        Assert.Contains("xgid-label", cut.Markup);
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
    public async Task Quiz_AnsweringState_ShowStatsButton_OpensTheTrailingCluster()
    {
        // Show stats sits where Restart used to — the row's trailing cluster —
        // rather than in a standalone block above the branch. It *opens* that
        // cluster rather than closing the row: End quiz trails it (issue #57).
        //
        // The ms-auto that pushes the cluster away from the answer controls is
        // the CLUSTER's, not this button's: the cluster now leads with the XGID
        // badge, which renders nothing for a decision without one, so an ms-auto
        // on its first child would be on a different element per problem.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var cut = Render<QuizPage>();

        var tail = cut.Find(".action-row-tail");
        Assert.Contains("ms-auto", tail.ClassList);

        var tailButtons = tail.QuerySelectorAll("button");
        Assert.Equal("Show stats", tailButtons[0].TextContent.Trim());
        Assert.Equal("End quiz", tailButtons[^1].TextContent.Trim());
    }

    [Fact]
    public async Task Quiz_ReviewState_ShowStatsButton_OpensTheTrailingCluster()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var cut = Render<QuizPage>();
        await cut.InvokeAsync(() => c.SubmitPlay(BestPlay()));
        Assert.NotNull(c.Review);

        var tail = cut.Find(".action-row-tail");
        Assert.Contains("ms-auto", tail.ClassList);

        var tailButtons = tail.QuerySelectorAll("button");
        Assert.Equal("Show stats", tailButtons[0].TextContent.Trim());
        Assert.Equal("End quiz", tailButtons[^1].TextContent.Trim());
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
        Assert.Contains(SavedFiltersDocument.FileName, cut.Markup);
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
        // SavedFiltersDocument in Before you start, so the data section points back at
        // them rather than restating them. Pinned because the natural instinct
        // when writing a section called "where everything is stored" is to list
        // all of it in one place — which would put the two filenames on the page
        // twice and make the next rename a two-site edit.
        WithController();

        var cut = Render<HelpPage>();

        var section = SectionText(
            cut.FindAll("h2").Single(h => h.TextContent.Trim() == "Your data stays yours"));

        Assert.DoesNotContain(QuizStatsFile.FileName, section);
        Assert.DoesNotContain(SavedFiltersDocument.FileName, section);
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

        Assert.Contains(SavedFiltersDocument.FileName, SectionText(savedFilters));
        // The legacy name is user-visible behavior too (umbrella ruling): an
        // existing folder's file under the earlier name is still read, and the
        // sentence saying so renders from the producer's constant — so a
        // rename or a dropped fallback fails here rather than shipping silent.
        Assert.Contains(SavedFiltersDocument.LegacyFileName, SectionText(savedFilters));
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
        // SSOT: the folder pick enforces PickedFileLimits (handed down to
        // folderAccess.js) and Help documents the same constants, with the
        // megabyte figure *derived* from the byte cap rather than restated.
        // Asserting against the constants (not the literals "50" / "500" /
        // "2000") is what makes this fail if page prose and enforced rule ever
        // drift — which is the whole reason the caps were hoisted off the
        // enforcing type. Per format since #59: a page that stated one number
        // for both would be wrong about one of them.
        WithController();

        var cut = Render<HelpPage>();

        // Asserted against the rendered *text*, not the markup: the extensions
        // sit in <code> elements, so the sentence a reader sees is not a
        // substring of the markup at all.
        var text = Normalize(cut.Find("div.container").TextContent);
        Assert.Contains($"{PickedFileLimits.MaxXgFileCount} .xg files", text);
        Assert.Contains($"{PickedFileLimits.MaxXgpFileCount} .xgp files", text);
        Assert.Contains($"{PickedFileLimits.MaxFileMegabytes} MB", text);
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
    //  Quiz.razor layout: board-on-top + the XGID's one home in the bottom row
    //
    //  These pin the structural contract the width-driven bottom-row layout
    //  depends on; the sizing itself (aspect-ratio, letterboxing) is pure CSS
    //  that bUnit's AngleSharp DOM can't evaluate — verified live in the browser
    //  instead.
    //
    //  The XGID set below is SPEC-quiz-view.md §4's one-home ruling (issue
    //  halheinrich/backgammon#98) from both ends, in every branch: the badge is
    //  in the action row's trailing cluster, and the XGID is NOT on the canvas.
    //  Both halves are load-bearing — a present-only assertion would stay green
    //  if the badge were rendered twice, and an absent-only assertion would stay
    //  green if it were not rendered at all.
    // -----------------------------------------------------------------------

    private const string SampleXgid = "XGID=-b----E-C---eE---c-e----B-:0:0:1:42:0:0:0:1:10";

    /// <summary>
    /// The badge's home, asserted as one place: exactly one badge on the page,
    /// inside the action row's trailing cluster, and no XGID anywhere in the
    /// board region — neither the component nor the value by any other route.
    /// </summary>
    /// <remarks>
    /// The absent half is asserted twice on purpose, at two levels. The
    /// component-level pin catches the badge being re-parented onto the board.
    /// The content-level pin catches the route a class selector is blind to:
    /// <c>DiagramOptions.ShowXgid</c> bakes the XGID into the producer's SVG as
    /// a plain, class-less <c>&lt;text&gt;</c> (default off; the raster
    /// exporters use it), so flipping it on in <c>Quiz.BoardOptions</c> would
    /// put the XGID back on the canvas with every class-level pin still green.
    /// Neither half can go vacuous: <c>Find</c> throws if the board region is
    /// missing, and the present half fails loudly if the badge's class is
    /// renamed.
    /// </remarks>
    private static void AssertXgidIsInTheBottomRowOnly(IRenderedComponent<QuizPage> cut)
    {
        var badge = Assert.Single(cut.FindAll(".xgid-label"));
        Assert.Contains("action-row-tail", badge.ParentElement!.ClassList);
        Assert.NotNull(badge.Closest(".board-chrome"));

        Assert.Empty(cut.FindAll(".board-container .xgid-label"));
        Assert.DoesNotContain(SampleXgid, cut.Find(".board-container").InnerHtml);
    }

    [Fact]
    public async Task Quiz_PlayAnswering_Xgid_RendersInTheBottomRow_NotOnTheBoard()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), xgid: SampleXgid));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var cut = Render<QuizPage>();

        AssertXgidIsInTheBottomRowOnly(cut);
        Assert.Contains(SampleXgid, cut.Find(".action-row-tail").TextContent);
    }

    [Fact]
    public async Task Quiz_CubeAnswering_Xgid_RendersInTheBottomRow_NotOnTheBoard()
    {
        var c = WithController(TestFixtures.CubeDecision(xgid: SampleXgid));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var cut = Render<QuizPage>();

        AssertXgidIsInTheBottomRowOnly(cut);
    }

    [Fact]
    public async Task Quiz_Review_Xgid_RendersInTheBottomRow_NotOnTheBoard()
    {
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), xgid: SampleXgid));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var cut = Render<QuizPage>();
        await cut.InvokeAsync(() => c.SubmitPlay(BestPlay()));
        Assert.NotNull(c.Review);

        AssertXgidIsInTheBottomRowOnly(cut);
    }

    [Fact]
    public async Task Quiz_ActionRow_IsOneRow_SharedByBothStates()
    {
        // The single row is what makes the badge's one home reachable at all:
        // an in-row site plus a per-state row would be two sites. It is also
        // what keeps the two states' row heights equal by construction, which
        // the board's flex remainder depends on — the claim the old two-row
        // arrangement had to make by hand ("add the button to BOTH rows").
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), xgid: SampleXgid));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var cut = Render<QuizPage>();

        Assert.Single(cut.FindAll(".board-chrome > .action-row"));
        Assert.Single(cut.FindAll(".action-row > .action-row-tail"));

        await cut.InvokeAsync(() => c.SubmitPlay(BestPlay()));
        Assert.NotNull(c.Review);

        Assert.Single(cut.FindAll(".board-chrome > .action-row"));
        Assert.Single(cut.FindAll(".action-row > .action-row-tail"));
    }

    [Fact]
    public async Task Quiz_NoXgid_TrailingClusterKeepsItsMsAuto()
    {
        // XgidLabel renders nothing for an empty XGID, so the ms-auto that pushes
        // the trailing cluster away from the answer controls cannot live on the
        // badge — this is the state that would silently left-align the cluster if
        // it did.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var cut = Render<QuizPage>();

        Assert.Empty(cut.FindAll(".xgid-label"));
        Assert.Contains("ms-auto", cut.Find(".action-row-tail").ClassList);
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
    public async Task Quiz_Chrome_OrdersStatusStripThenActionRowThenScorePanel()
    {
        // SPEC-quiz-view.md §5: the ongoing-stats strip moved to the BOTTOM of
        // the page, below the action row. Both halves of this pin were re-keyed
        // by that move — it used to assert score-panel FIRST (scoreIdx < stripIdx
        // < rowIdx), and an assertion that merely dropped the score panel would
        // have gone vacuously green while the panel sat anywhere at all.
        //
        // Pinned on the answering state; the review branch shares this strip and
        // this score panel, which sit outside the per-state action-row branch.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var cut = Render<QuizPage>();

        var markup = cut.Markup;
        var stripIdx = markup.IndexOf("status-strip", StringComparison.Ordinal);
        var rowIdx = markup.IndexOf("action-row", StringComparison.Ordinal);
        var scoreIdx = markup.IndexOf("score-panel", StringComparison.Ordinal);
        Assert.True(stripIdx >= 0 && rowIdx >= 0 && scoreIdx >= 0, "all three chrome pieces present");
        Assert.True(stripIdx < rowIdx, "the status strip renders before the action row");
        Assert.True(rowIdx < scoreIdx, "the score panel renders after the action row — page bottom");
    }

    [Fact]
    public async Task Quiz_ScorePanelMove_LeavesTheChromeBlockIntact()
    {
        // The move's whole no-interaction claim: the score panel changed
        // position, not membership. It is still inside .board-chrome (the
        // measured flex:0 0 auto region), so the block's total height — and
        // therefore the board's flex remainder — is what it was. A "move" that
        // hoisted the panel out of the chrome block would silently resize the
        // board in Normal view, which §5 says this rider must not do.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var cut = Render<QuizPage>();

        var panel = cut.Find(".score-panel");
        Assert.NotNull(panel.Closest(".board-chrome"));
        Assert.Empty(cut.FindAll(".board-container .score-panel"));
    }

    // -----------------------------------------------------------------------
    //  The maximize-board mode (issue #41 / SPEC-quiz-view.md §4). The mode is
    //  a pure derivation over (the setting, answering|review), so these pin it
    //  from both ends: which chrome renders, and which canvas the producer is
    //  asked for. bUnit cannot measure the resulting board, which is the e2e
    //  suite's job; what it CAN pin is that the two legs move together, since
    //  §2's measurement says either one alone delivers nothing on desktop.
    // -----------------------------------------------------------------------

    /// <summary>
    /// The canvas preset the currently rendered board is asking the producer for
    /// — the mirror of <see cref="RenderedBoardSide"/>, and read off the same
    /// outermost component so it reflects what <c>Quiz</c> passed rather than
    /// what a producer default supplied.
    /// </summary>
    private static AspectPreset RenderedCanvas(IRenderedComponent<QuizPage> cut) =>
        cut.FindComponents<BackgammonPlayEntry>().Count > 0
            ? cut.FindComponent<BackgammonPlayEntry>().Instance.Options.Aspect
            : cut.FindComponent<BackgammonDiagram>().Instance.Options.Aspect;

    /// <summary>Turn the maximize setting on, as the Settings page would.</summary>
    private Task MaximizeAsync() => Settings().SetMaximizeBoardWhileAnsweringAsync(true);

    [Fact]
    public async Task Quiz_Maximized_PlayAnswering_SuppressesChrome_AndCropsTheCanvas()
    {
        // Both legs of §4's maximized-answering composition, asserted together
        // because either alone is a feature that measures as doing nothing:
        // suppressing chrome frees height the width-bound 16:9 canvas cannot use,
        // and cropping the canvas without freeing height wastes the crop.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        await MaximizeAsync();

        var cut = Render<QuizPage>();

        Assert.Empty(cut.FindAll(".score-panel"));
        Assert.Empty(cut.FindAll(".status-strip"));
        Assert.Equal(AspectPreset.BoardOnly, RenderedCanvas(cut));

        // The board and the action row are what remain — the mode suppresses
        // chrome, it does not suppress the page.
        Assert.NotEmpty(cut.FindAll(".board-container .bg-play-entry"));
        Assert.NotEmpty(cut.FindAll(".action-row"));
    }

    [Fact]
    public async Task Quiz_Maximized_CubeAnswering_KeepsEveryAnswerInstrument()
    {
        // The filing's play/cube fork, dissolved by ruling: the action row keeps
        // every instrument — cube radios included — so a cube answer stays
        // makeable without leaving the maximized view. A mode that suppressed the
        // radios with the rest of the chrome would strand the user on exactly the
        // decisions the cube fixtures cover.
        var c = WithController(TestFixtures.CubeDecision());
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        await MaximizeAsync();

        var cut = Render<QuizPage>();

        Assert.Empty(cut.FindAll(".score-panel"));
        Assert.Empty(cut.FindAll(".status-strip"));
        Assert.Equal(AspectPreset.BoardOnly, RenderedCanvas(cut));

        var actionRow = cut.Find(".action-row");
        Assert.NotEmpty(actionRow.QuerySelectorAll("[role=\"radiogroup\"]"));
        Assert.Contains("Submit", actionRow.TextContent);
        Assert.Contains("Skip", actionRow.TextContent);
    }

    [Fact]
    public async Task Quiz_Maximized_Review_NormalizesChrome_AndNeverAsksForBoardOnly()
    {
        // The normalize trigger is exactly the answering → review transition, and
        // this is the pin that keeps the producer from throwing: BoardOnly is
        // rejected outright for a Solution request (ArgumentException from
        // RenderSvg and GetHitRegions alike), and the review branch is the one
        // that builds Solution requests. The guard is BoardOptions' derivation,
        // not a check — so this asserts the derivation, in the state that would
        // fault if it were ever loosened.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        await MaximizeAsync();

        var cut = Render<QuizPage>();
        Assert.Equal(AspectPreset.BoardOnly, RenderedCanvas(cut)); // maximized first

        await cut.InvokeAsync(() => c.SubmitPlay(AltPlay()));
        Assert.NotNull(c.Review);

        Assert.NotEqual(AspectPreset.BoardOnly, RenderedCanvas(cut));
        Assert.Equal(DiagramMode.Solution, cut.FindComponent<BackgammonDiagram>().Instance.Request!.Mode);
        Assert.NotEmpty(cut.FindAll(".score-panel"));
        Assert.NotEmpty(cut.FindAll(".status-strip"));
    }

    [Fact]
    public async Task Quiz_Maximized_Xgid_KeepsItsBottomRowHome_AnsweringAndReview()
    {
        // The XGID has ONE home in BOTH view modes (SPEC-quiz-view.md §4, issue
        // halheinrich/backgammon#98), and this is the composition the ruling came
        // out of: maximized answering, where the title strip the badge used to
        // obscure no longer exists at all. The row survives the chrome
        // suppression, so the badge rides with it — and then still sits in the
        // same place one Submit later, which is the "never teleports between
        // modes" half.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), xgid: SampleXgid));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        await MaximizeAsync();

        var cut = Render<QuizPage>();

        Assert.Empty(cut.FindAll(".status-strip"));   // maximized, as staged
        Assert.Equal(AspectPreset.BoardOnly, RenderedCanvas(cut));
        AssertXgidIsInTheBottomRowOnly(cut);

        await cut.InvokeAsync(() => c.SubmitPlay(BestPlay()));
        Assert.NotNull(c.Review);

        Assert.NotEmpty(cut.FindAll(".status-strip"));  // normalized
        AssertXgidIsInTheBottomRowOnly(cut);
    }

    [Fact]
    public async Task Quiz_Maximized_RedoAndContinue_ReMaximize_WithNoSpecialCase()
    {
        // "One rule, no special cases" (§4): the composition is derived from the
        // answering/review fact every render, so Redo — which returns to
        // answering on the SAME problem — re-maximizes without any transition
        // knowing the mode exists. A stored "currently maximized" bit is what
        // would make this a special case to remember; there isn't one.
        var c = WithController(
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        await MaximizeAsync();

        var cut = Render<QuizPage>();

        await cut.InvokeAsync(() => c.SubmitPlay(BestPlay()));
        Assert.NotEmpty(cut.FindAll(".status-strip"));      // normalized at review

        await cut.InvokeAsync(() => c.RedoAsync());
        Assert.Null(c.Review);
        Assert.Empty(cut.FindAll(".status-strip"));         // re-maximized
        Assert.Equal(AspectPreset.BoardOnly, RenderedCanvas(cut));

        // And the next problem's answering state, reached by Continue, is
        // maximized too — the mode is not a per-problem thing.
        await cut.InvokeAsync(() => c.SubmitPlay(BestPlay()));
        await cut.InvokeAsync(() => c.ContinueAsync());
        Assert.Null(c.Review);
        Assert.Empty(cut.FindAll(".status-strip"));
        Assert.Equal(AspectPreset.BoardOnly, RenderedCanvas(cut));
    }

    [Fact]
    public async Task Quiz_SettingOff_AnsweringReproducesTodaysComposition()
    {
        // The contract that lets the mode ship dark, and the other half of every
        // assertion above: with the setting off — the default — the answering
        // state keeps all its chrome and the producer's own canvas. Written as
        // its own test rather than left implicit in the older pins, so a
        // suppression that leaked out of the mode fails somewhere that names why.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        var cut = Render<QuizPage>();

        Assert.False(Settings().MaximizeBoardWhileAnswering);
        Assert.NotEmpty(cut.FindAll(".score-panel"));
        Assert.NotEmpty(cut.FindAll(".status-strip"));
        Assert.NotEqual(AspectPreset.BoardOnly, RenderedCanvas(cut));
    }

    [Fact]
    public async Task Quiz_Maximized_NoticesAreNeverSuppressedByTheMode()
    {
        // §4's notices ruling, in the composition that would be tempted to hide
        // them: the mix notice retires on the first answer, so suppressing it
        // while answering means it is never seen at all — and the stats notices
        // report degraded recording, which must be seen. Dismissibility is the
        // answer to the space they cost, not suppression.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        await WithStatsStoreInStatusAsync(QuizStatsStatus.WriteFailed);
        await MaximizeAsync();

        var cut = Render<QuizPage>();

        Assert.Empty(cut.FindAll(".status-strip"));   // maximized, as staged
        Assert.NotEmpty(cut.FindAll(".alert-danger")); // and the notice stands
        Assert.Contains("stats won't be recorded", cut.Markup);
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

    [Fact]
    public void AppCss_RetiredBadgeContainerQueryAnchor_StaysGone()
    {
        // Migration pin for the XGID's move off the canvas (SPEC-quiz-view.md §4,
        // issue halheinrich/backgammon#98). `.board-container .bg-diagram` was
        // made a `container-type: inline-size` query container for exactly one
        // consumer: the overlaid badge, sized in cqw so it tracked the *rendered*
        // board width under letterboxing. With the badge in the bottom row there
        // is no cqw anchor to be — and containment is real layout, not a comment,
        // so it was removed rather than left as decoration (measured first: the
        // board box is identical with it on and off). Both halves are pinned,
        // since re-adding either one alone is how it would creep back.
        var css = File.ReadAllText(AppCssPath());
        var noComments = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

        Assert.DoesNotContain("container-type", noComments);
        Assert.DoesNotContain("cqw", noComments);
    }

    [Fact]
    public void AppCss_XgidLabelText_StaysCapped()
    {
        // The visible-XGID cap is a board-size contract, not styling (issue
        // halheinrich/backgammon#98, SPEC-quiz-view.md §2). Uncapped, the badge
        // measured ~401px and wrapped the action row at widths where the board is
        // height-bound — and since a cube row is wider than a checker row, the
        // wrap width depended on the PROBLEM KIND, i.e. per-problem board jitter
        // inside Normal view. The three declarations are one mechanism (clip,
        // ellipsis, no wrapping): drop any one and the cap stops capping, so all
        // three are pinned, together with the measured 2.5rem value — 40px, which
        // is "XGID=" (32.7px in this font) plus the ellipsis (6.5px), so the
        // visible text is exactly the value's self-labeling prefix and the whole
        // badge is 74.4px. At that width 1440x900 and 1366x800 show no
        // per-problem-kind divergence at all. bUnit cannot measure CSS; what it
        // can do is stop the contract being edited away without a fresh
        // measurement.
        var css = File.ReadAllText(AppCssPath());
        var rule = Regex.Match(css, @"\.xgid-label-text\s*\{[^}]*\}", RegexOptions.Singleline);

        Assert.True(rule.Success, ".xgid-label-text rule present");
        Assert.Contains("max-width: 2.5rem", rule.Value);
        Assert.Contains("text-overflow: ellipsis", rule.Value);
        Assert.Contains("white-space: nowrap", rule.Value);
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
    /// Commit the panel's current selection through its own <i>Apply Filter</i>
    /// button — the real gesture, since the panels are producer-internal now
    /// and wire tests drive <c>FilterSurface</c>'s rendered DOM (the ruled
    /// no-carve-out ban on <c>FindComponent</c> over the panels). On a fresh
    /// mount under this fixture's loose JS interop the buffers are the
    /// defaults, so this commits the empty config the retired synthetic helper
    /// used to emit; a test that stages control edits first commits those.
    /// Every pick re-mounts the panel with nothing committed, so a test that
    /// picks and then wants Start armed applies <i>after</i> its last pick.
    /// <para>
    /// The panel's own gate refuses a commit while the selection equals the
    /// last-committed config, so a second call within one panel mount must be
    /// preceded by a real control edit — the click dispatches, and the panel
    /// deliberately no-ops it.
    /// </para>
    /// </summary>
    private static Task ApplyFiltersAsync(IRenderedComponent<HomePage> cut) =>
        cut.FindAll("button")
           .First(b => b.TextContent.Trim() == "Apply Filter")
           .ClickAsync(new());

    /// <summary>
    /// Make a real edit on the always-visible error-range Min input (to
    /// <c>0.75</c>, a value no fixture commits), moving the buffers off any
    /// committed config — the panel reports "uncommitted edits" (<c>null</c>)
    /// through the composite, which clears the applied holder and re-gates
    /// Start. The DOM-gesture successor of the retired synthetic
    /// applied-state raise. Undo with <see cref="UndoFilterEditAsync"/>.
    /// </summary>
    private static Task EditFilterControlAsync(IRenderedComponent<HomePage> cut) =>
        cut.Find("input[placeholder='Min']")
           .InputAsync(new ChangeEventArgs { Value = "0.75" });

    /// <summary>
    /// Undo <see cref="EditFilterControlAsync"/>'s edit (Min back to blank).
    /// With the buffers back at the committed values the panel reports clean —
    /// re-applying the committed config through the composite — or, when
    /// nothing is committed this mount, reports <c>null</c> again.
    /// </summary>
    private static Task UndoFilterEditAsync(IRenderedComponent<HomePage> cut) =>
        cut.Find("input[placeholder='Min']")
           .InputAsync(new ChangeEventArgs { Value = "" });

    /// <summary>
    /// Put a minimal one-row mix (NeverSeen, 100%) in effect through the real
    /// panel — Add category, then check <b>"Mix applies"</b>. The UI route
    /// matters: setting <c>MixConsent</c> directly would skip the check
    /// gesture's gate and backstop, which are part of what these tests pin.
    /// <para>
    /// <b>Precondition:</b> a filter must be in effect for the <i>current</i>
    /// pick — the check gesture is gated on it
    /// (§ <c>Home.MixActivationEnabled</c>, the spec's Fork A), and a change
    /// dispatched at the gated box is dropped by the handler's backstop. A
    /// fixture that pre-arms <see cref="WithAppliedFilter"/> and then picks
    /// through the UI must re-apply after the pick, exactly as a user would.
    /// </para>
    /// </summary>
    private static async Task ActivateMixThroughPanelAsync(IRenderedComponent<HomePage> cut)
    {
        await cut.Find("#mixAddRow").ClickAsync(new());
        await cut.Find("#mixApplies").ChangeAsync(new ChangeEventArgs { Value = true });
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
    /// Toggling is navigation, not an edit: the panel raises no applied-state
    /// report for it, so calling this never disturbs a test's applied/dirty
    /// expectations.
    /// </para>
    /// </summary>
    private static Task ExpandMoreFiltersAsync(IRenderedComponent<HomePage> cut) =>
        cut.Find("#moreFiltersToggle").ClickAsync(new());

    [Fact]
    public async Task Home_MixActivatedInPanel_StartComposesWeightedQuiz()
    {
        // The full UI → QuizMix → start-composition wire: checking "Mix
        // applies" puts the on-screen mix in effect, Start hands the draft's
        // build to the controller, and the started quiz composes through the
        // real MixedProblemSetSource (LastComposition non-null is the
        // composed-layer signature).
        var c = WithWeighableController(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty;
        WithPickedFolder(capability: FolderWriteCapability.Enabled, withStatsHistory: true);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();

        var cut = Render<HomePage>();
        await ActivateMixThroughPanelAsync(cut);
        await StartButton(cut).ClickAsync(new());

        Assert.True(c.HasStarted);
        Assert.NotNull(c.LastComposition);
        Assert.Equal(1, c.LastComposition!.DrawnCount);
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        Assert.EndsWith("/quiz", nav.Uri);
    }

    [Fact]
    public async Task Home_UnactivatedMixEdit_NeverGatesStart_StartRunsPassthrough()
    {
        // The spec's §5 headline, and the resolution of issue #83 by
        // construction: an un-activated mix is simply not in effect. Editing
        // rows without checking "Mix applies" leaves Start live, and the run
        // it starts is passthrough — no gate, no hint, no wedge to escape.
        var c = WithWeighableController(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty;
        WithPickedFolder(capability: FolderWriteCapability.Enabled, withStatsHistory: true);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();

        var cut = Render<HomePage>();
        Assert.False(StartButton(cut).HasAttribute("disabled"));

        await cut.Find("#mixAddRow").ClickAsync(new()); // rows on screen, box unchecked

        Assert.False(StartButton(cut).HasAttribute("disabled"));
        await StartButton(cut).ClickAsync(new());

        Assert.True(c.HasStarted);
        Assert.Null(c.LastComposition); // passthrough — the rows played no part
    }

    // -----------------------------------------------------------------------
    //  Mix activation is sequenced behind Apply Filter (issue #45 / Fork A)
    // -----------------------------------------------------------------------

    /// <summary>The "Mix applies" checkbox's disabled state on a rendered Home page.</summary>
    private static bool MixActivationDisabled(IRenderedComponent<HomePage> cut) =>
        cut.Find("#mixApplies").HasAttribute("disabled");

    /// <summary>
    /// Arrange an Enabled pick made <i>through the UI</i> — the only route that
    /// bumps <see cref="PickedProblemFolder.PickGeneration"/> the way a real
    /// pick does, which is what the mix-activation gate reads. A pre-armed
    /// <see cref="WithPickedFolder"/> fixture cannot exercise the gate's
    /// expiry, because nothing ever expires.
    /// </summary>
    private async Task<IRenderedComponent<HomePage>> RenderWithUiPickAsync()
    {
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.Enabled);
        // Both halves of the mix predicate, since these scenarios are about the
        // Apply-Mix gate and need the panel on screen to exercise it.
        _folderAccess.PickedStatsJson = StatsDocumentJson(BestPlay());

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        return cut;
    }

    [Fact]
    public async Task Home_FreshPick_MixActivationGatedUntilAFilterIsApplied()
    {
        // Issue #45, the headline: the mix draws from the filtered pool, so
        // activating one before any filter has been applied is premature. The
        // gate is UX sequencing — the pipeline never required the order — so it
        // must also *say* why, not merely refuse.
        var cut = await RenderWithUiPickAsync();

        // The hint is up from the moment the panel appears — before any row
        // exists — so the ordering is learned before the composing starts.
        Assert.Contains("the mix draws its problems from the", cut.Markup);

        // A complete, valid one-row mix: from here the host gate is the only
        // thing keeping the box dark (the draft validates, and zero-vs-some
        // rows never gates the checkbox — ruled).
        await cut.Find("#mixAddRow").ClickAsync(new());
        Assert.Null(Services.GetRequiredService<MixDraft>().ValidationError);
        Assert.True(MixActivationDisabled(cut));

        await ApplyFiltersAsync(cut);

        Assert.False(MixActivationDisabled(cut));
        Assert.DoesNotContain("the mix draws its problems from the", cut.Markup);
    }

    [Fact]
    public async Task Home_GatedActivation_LeavesClearMixLive()
    {
        // Clear mix is a way out, never a way in — ungated in every state, so
        // the rows can always be deliberately removed even while activation is
        // sequenced behind the filter. (Un-activated rows no longer gate Start
        // at all, so no wedge is possible either way; this pins the affordance
        // itself.)
        var cut = await RenderWithUiPickAsync();
        await cut.Find("#mixAddRow").ClickAsync(new());

        Assert.True(MixActivationDisabled(cut));
        Assert.False(cut.Find("#mixClear").HasAttribute("disabled"));

        await cut.Find("#mixClear").ClickAsync(new());

        Assert.Empty(cut.FindAll(".mix-row"));
    }

    [Fact]
    public async Task Home_MixActivation_RevokedByADirtyFilter_AndRestoredByReApplying()
    {
        // The spec's Fork A, ruled strict: activation reads the filter in
        // effect *now* — the same fact Start reads — so a filter edit takes
        // the unchecked box's check gesture away and re-applying gives it
        // back. No fact of the form "this corpus was filtered at some point"
        // exists anywhere in the model (§3). The accepted cost is exactly what
        // the second half pins — mid-composition friction, recoverable by one
        // re-Apply.
        var cut = await RenderWithUiPickAsync();
        await cut.Find("#mixAddRow").ClickAsync(new()); // a valid, activatable draft
        await ApplyFiltersAsync(cut);
        Assert.False(MixActivationDisabled(cut));

        await EditFilterControlAsync(cut);

        // One fact, read by both gates — no state in which they disagree.
        Assert.Null(FilterInEffect());
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.True(MixActivationDisabled(cut));
        // And the gate says why it closed, as it does on a fresh pick.
        Assert.Contains("the mix draws its problems from the", cut.Markup);

        await ApplyFiltersAsync(cut);

        Assert.NotNull(FilterInEffect());
        Assert.False(MixActivationDisabled(cut));
        Assert.DoesNotContain("the mix draws its problems from the", cut.Markup);
    }

    [Fact]
    public async Task Home_CheckedBox_StaysOperableThroughADirtyFilter()
    {
        // The ruled asymmetry, on the page: the filter edit gates only the
        // CHECK gesture. A box already checked stays enabled — unchecking is
        // the universal way out and is never taken away — and the bit itself
        // is untouched (the app flips consent in neither direction). Nothing
        // can run wrong in the window: Start is dark on the filter's own gate.
        var cut = await RenderWithUiPickAsync();
        await ApplyFiltersAsync(cut);
        await ActivateMixThroughPanelAsync(cut);
        var consent = Services.GetRequiredService<MixConsent>();
        Assert.True(consent.Applies);

        await EditFilterControlAsync(cut);

        Assert.True(consent.Applies);                              // not auto-unchecked
        Assert.False(MixActivationDisabled(cut));                  // uncheck still live
        Assert.True(StartButton(cut).HasAttribute("disabled"));    // the filter gate holds

        await cut.Find("#mixApplies").ChangeAsync(new ChangeEventArgs { Value = false });
        Assert.False(consent.Applies); // withdrawing consent worked mid-dirty-filter
    }

    [Fact]
    public async Task Home_NewPick_UnchecksTheBox_AndReGatesActivation()
    {
        // A pick ends the setup: consent is revoked (§4 — choices outlive the
        // setup, consent does not) and the new corpus has no filter in effect,
        // so the check gesture is gated again until a fresh Apply.
        var cut = await RenderWithUiPickAsync();
        await ApplyFiltersAsync(cut);
        await ActivateMixThroughPanelAsync(cut);
        var consent = Services.GetRequiredService<MixConsent>();
        Assert.True(consent.Applies);

        _folderAccess.NextPickOutcome =
            OneFileOutcome("Second", "second.xg", FolderWriteCapability.Enabled);
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.False(consent.Applies); // consent died with the setup
        Assert.False(cut.Find("#mixApplies").HasAttribute("checked"));
        Assert.True(MixActivationDisabled(cut));
        Assert.Contains("the mix draws its problems from the", cut.Markup);
    }

    [Fact]
    public async Task Home_GatedActivation_IgnoresAProgrammaticCheck()
    {
        // The disabled attribute is the affordance, not the contract: the
        // panel's handler drops a check arriving past the gate, so a dispatch
        // that ignores `disabled` still cannot activate.
        var cut = await RenderWithUiPickAsync();
        var consent = Services.GetRequiredService<MixConsent>();
        await cut.Find("#mixAddRow").ClickAsync(new());

        await cut.Find("#mixApplies").ChangeAsync(new ChangeEventArgs { Value = true });

        Assert.False(consent.Applies); // the gated gesture never landed
        Assert.True(StartButton(cut).HasAttribute("disabled")); // still the filter's gate
    }

    [Fact]
    public async Task Home_CheckedInvalidMix_GatesStart_WithTheFixOrUncheckHint()
    {
        // Confirmation 2, ruled: checked + invalid is the ONE mix state that
        // gates Start. The box stays checked (it records intent — only the
        // user moves it), the hint is the exact ruled sentence, and either
        // repair path works: fixing the mix, or unchecking. Both halves are
        // pinned here.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: FolderWriteCapability.Enabled, withStatsHistory: true);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();

        var cut = Render<HomePage>();
        await ActivateMixThroughPanelAsync(cut); // in effect: NeverSeen at 100%
        Assert.False(StartButton(cut).HasAttribute("disabled"));

        // Breaking the on-screen mix while checked gates, with the reason —
        // the exact ruled sentence, pinned via TextContent (entity-decoded).
        await cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!
            .InputAsync(new ChangeEventArgs { Value = "90" });
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains(cut.FindAll("small"),
            s => s.TextContent.Trim() == "Mix applies but isn't valid — fix it or uncheck.");
        Assert.True(cut.Find("#mixApplies").HasAttribute("checked")); // intent recorded, not flipped

        // …fixing it un-gates…
        await cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!
            .InputAsync(new ChangeEventArgs { Value = "100" });
        Assert.False(StartButton(cut).HasAttribute("disabled"));
        Assert.DoesNotContain("fix it or uncheck", cut.Markup);

        // …and so does the other ruled escape: break again, then uncheck.
        await cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!
            .InputAsync(new ChangeEventArgs { Value = "90" });
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        await cut.Find("#mixApplies").ChangeAsync(new ChangeEventArgs { Value = false });
        Assert.False(StartButton(cut).HasAttribute("disabled")); // not in effect — nothing to gate
    }

    [Fact]
    public async Task Home_UncheckedInvalidDraft_NeverGatesStart()
    {
        // The contrast that keeps the gate honest: the same broken mix with
        // the box unchecked is simply not in effect — no gate, no hint. The
        // panel's own validation line still reports the problem for whenever
        // the user comes back to it.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: FolderWriteCapability.Enabled, withStatsHistory: true);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();

        var cut = Render<HomePage>();
        await cut.Find("#mixAddRow").ClickAsync(new());
        await cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!
            .InputAsync(new ChangeEventArgs { Value = "85" }); // sum ≠ 100

        Assert.False(StartButton(cut).HasAttribute("disabled"));
        Assert.DoesNotContain("fix it or uncheck", cut.Markup);
        Assert.Contains("must reach 100", cut.Markup); // the panel still says what's wrong
    }

    [Fact]
    public async Task Home_CheckedMixEmptiedToZeroRows_IsPassthrough_StartStaysLive()
    {
        // Ruled (design point B): checked-but-inert. Emptying the mix while
        // the box is checked leaves the box exactly where the user put it and
        // the effect passthrough — the blank mix builds Empty, never null, so
        // nothing gates and the run is plain. (The app unchecks nothing; the
        // old auto-commit machinery has no successor because there is nothing
        // left to reconcile.)
        var c = WithWeighableController(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty;
        WithPickedFolder(capability: FolderWriteCapability.Enabled, withStatsHistory: true);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();

        var cut = Render<HomePage>();
        await ActivateMixThroughPanelAsync(cut);

        await cut.FindAll(".mix-row")[0].QuerySelector("button[title='Remove']")!.ClickAsync(new());

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.True(cut.Find("#mixApplies").HasAttribute("checked")); // untouched
        Assert.False(StartButton(cut).HasAttribute("disabled"));

        await StartButton(cut).ClickAsync(new());
        Assert.True(c.HasStarted);
        Assert.Null(c.LastComposition); // vacuous consent = passthrough run
    }

    [Fact]
    public async Task Home_MixRestore_FreshLoad_ShowsStoredMixInert_UntilChecked()
    {
        // The spec's rule 3 at the reload boundary: the persisted mix hydrates
        // into the draft — visible, updateable — but has NO effect until
        // activated in this setup. Consent is Scoped state, so a cold boot's
        // box is unchecked: Start is live (an un-activated mix never gates)
        // and one check puts exactly what is shown into effect. Driven through
        // the real hydration wire (localStorage → MixDraft → panel).
        var c = WithWeighableController(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty;
        WithPickedFolder(capability: FolderWriteCapability.Enabled, withStatsHistory: true);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult(NeverSeenMix().ToJson());

        var cut = Render<HomePage>();

        // The panel shows the stored rows, inert: box unchecked, Start live.
        Assert.NotEmpty(cut.FindAll(".mix-row"));
        Assert.False(cut.Find("#mixApplies").HasAttribute("checked"));
        Assert.False(StartButton(cut).HasAttribute("disabled"));

        // Checking the box activates exactly what is shown.
        await cut.Find("#mixApplies").ChangeAsync(new ChangeEventArgs { Value = true });
        await StartButton(cut).ClickAsync(new());

        Assert.True(c.HasStarted);
        Assert.NotNull(c.LastComposition); // the restored mix, composed
    }

    [Fact]
    public async Task Home_MixRestore_ClearMixRemovesTheRows_StartStaysLive()
    {
        // The deliberate way to be rid of a restored mix: Clear removes the
        // rows and (write-through) the stored blob. Start was never gated by
        // the inert restore and stays live throughout.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: FolderWriteCapability.Enabled, withStatsHistory: true);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult(NeverSeenMix().ToJson());

        var cut = Render<HomePage>();
        Assert.NotEmpty(cut.FindAll(".mix-row"));
        Assert.False(StartButton(cut).HasAttribute("disabled"));

        await cut.Find("#mixClear").ClickAsync(new());

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.False(StartButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void Home_MixRestore_Passthrough_BlankBuilder()
    {
        // A persisted passthrough (e.g. after a prior Clear mix) hydrates to
        // zero rows — the blank builder, nothing in effect, Start free.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: FolderWriteCapability.Enabled, withStatsHistory: true);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult(QuizMix.Empty.ToJson());

        var cut = Render<HomePage>();

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.False(StartButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void Home_NavigateBack_ActiveMix_StaysInEffect()
    {
        // Navigate-back with an active mix: consent and draft are both Scoped,
        // so the box comes back checked over the same rows — §4's "navigating
        // away and back changes nothing", with no reconcile arm deciding whom
        // to believe because there is only one copy of the mix to believe.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: FolderWriteCapability.Enabled, withStatsHistory: true);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        var consent = WithActiveMix(NeverSeenMix()); // activated earlier this session

        var cut = Render<HomePage>();

        Assert.NotEmpty(cut.FindAll(".mix-row"));
        Assert.True(consent.Applies);
        Assert.True(cut.Find("#mixApplies").HasAttribute("checked"));
        Assert.False(StartButton(cut).HasAttribute("disabled"));
        // And the in-effect derivations read it live: the mix owns order.
        Assert.True(cut.Find("#shuffleOrder").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_MixEditedThenNavigatedAway_DraftSurvives_NeverGates()
    {
        // Finding (AK)'s scenario under the ratified model: the draft is
        // app-scoped, so an edit survives navigate-away/back — still on
        // screen, still inert (the box was never checked), Start live the
        // whole time. The (AK) wedge — Start gated over a blank panel with the
        // edit existing nowhere — is unrepresentable: nothing about an
        // un-activated draft can gate anything.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: FolderWriteCapability.Enabled, withStatsHistory: true); // the mix predicate: can-save-stats AND has-stats
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        var consent = WithMixConsent();

        var cut = Render<HomePage>();

        // Add a category and stop there — rows on screen, box unchecked.
        await cut.Find("#mixAddRow").ClickAsync(new());
        Assert.False(StartButton(cut).HasAttribute("disabled"));

        // Navigate away and back: Home and its MixPanel unmount, but the draft
        // and the consent bit — Scoped (Singleton here) — survive, as on a
        // real in-app navigation to Help and back.
        await DisposeComponentsAsync();
        var back = Render<HomePage>();

        // The edit is still on screen, still inert, and one check activates it.
        var row = Assert.Single(back.FindAll(".mix-row"));
        Assert.Equal("NeverSeen", row.QuerySelector("option[selected]")!.GetAttribute("value"));
        Assert.False(back.Find("#mixApplies").HasAttribute("checked"));
        Assert.False(StartButton(back).HasAttribute("disabled"));

        await back.Find("#mixApplies").ChangeAsync(new ChangeEventArgs { Value = true });

        Assert.True(consent.Applies);
        Assert.False(StartButton(back).HasAttribute("disabled")); // valid mix in effect
    }

    [Fact]
    public async Task Home_CheckedInvalidMix_ClearMixUngates_BoxStaysChecked()
    {
        // The in-panel escape from checked-and-broken, and design point B's
        // Clear-while-checked ruling in one: Clear removes the rows, the blank
        // builds Empty (never null), so the checked box reads as passthrough —
        // un-gated — and the bit itself is exactly where the user left it.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: FolderWriteCapability.Enabled, withStatsHistory: true);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        var consent = WithMixConsent();

        var cut = Render<HomePage>();
        await ActivateMixThroughPanelAsync(cut);
        await cut.FindAll(".mix-row")[0].QuerySelector(".mix-percent")!
            .InputAsync(new ChangeEventArgs { Value = "85" }); // sum ≠ 100

        Assert.True(StartButton(cut).HasAttribute("disabled"));

        await cut.Find("#mixClear").ClickAsync(new());

        Assert.Empty(cut.FindAll(".mix-row"));
        Assert.True(consent.Applies); // the app never flips the bit
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
        sink.CanWeightMix = true;    // capability peek passes (stage 1)
        sink.CurrentDocument = null; // ...but the bind yields no document (stage 2: unreadable file)
        WithPickedFolder(capability: FolderWriteCapability.Enabled, withStatsHistory: true);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        var consent = WithActiveMix(NeverSeenMix());

        var cut = Render<HomePage>();
        await StartButton(cut).ClickAsync(new());

        Assert.False(c.HasStarted);
        Assert.Contains("weighted mix can't be applied", cut.Markup);
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        Assert.DoesNotContain("/quiz", nav.Uri);

        await cut.Find("#startWithoutMix").ClickAsync(new());

        Assert.True(c.HasStarted);
        Assert.Null(c.LastComposition);          // passthrough run
        Assert.True(consent.Applies);            // per-run escape: the checkbox untouched…
        Assert.NotEmpty(cut.FindAll(".mix-row")); // …and the rows kept, as the notice promises
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
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.BrowserUnsupported);
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        await ApplyFiltersAsync(cut); // the pick reset the applied filter

        Assert.Empty(cut.FindComponents<MixPanelComponent>()); // no mix panel
        var startBtn = StartButton(cut);
        Assert.False(startBtn.HasAttribute("disabled")); // enabled, not mix-gated
        Assert.DoesNotContain("fix it or uncheck", cut.Markup);

        await startBtn.ClickAsync(new());

        Assert.True(c.HasStarted);
        Assert.Null(c.LastComposition); // passthrough — no composition
        Assert.EndsWith("/quiz", nav.Uri);
    }

    [Fact]
    public async Task Home_MixActiveThenRepickNoStats_ConsentRevoked_NoRefusal()
    {
        // Task X unreachability proof (why the early "your mix can't be provided"
        // advisory was removable): activate a mix under an Enabled pick, then
        // re-pick a no-stats folder. Every pick revokes the consent bit, and the
        // no-stats pick hides the panel — so a stats-less pick can never coexist
        // with a mix in effect, the exact state that advisory reported.
        // Start then runs plain, with no refusal.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        var consent = WithMixConsent();
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        var cut = Render<HomePage>();

        // A mix-capable pick (can save stats, has some) → panel shows; activate
        // a mix through the real UI. The filter Apply is not optional here: a
        // pick expires the applied filter, and the check gesture is gated on a
        // filter in effect for the current pick (§ MixActivationEnabled).
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.Enabled);
        _folderAccess.PickedStatsJson = StatsDocumentJson(BestPlay());
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        await ApplyFiltersAsync(cut);
        await ActivateMixThroughPanelAsync(cut);
        Assert.True(consent.Applies); // in effect

        // Re-pick a no-stats folder → the pick revokes consent and discards the
        // draft; the panel hides, so nothing re-hydrates — nothing is in effect
        // and nothing can gate.
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.BrowserUnsupported);
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.False(consent.Applies); // consent died with the setup
        Assert.Empty(Services.GetRequiredService<MixDraft>().Rows);
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
    public async Task Home_MixSurfaceAcrossPickRepickClear_ConsentDiesRowsPersist()
    {
        // The setup lifecycle across all three transitions, under §4's law
        // (choices outlive the setup; consent does not). With a persisted mix
        // in localStorage:
        //  • pick (Enabled): panel mounts, the draft hydrates the stored mix —
        //    visible, inert, Start live.
        //  • check "Mix applies": in effect.
        //  • re-pick (Enabled): consent revoked, draft discarded; the keyed
        //    panel re-mounts and re-hydrates, re-offering the same rows with
        //    the box unchecked — the rows persisted, the consent did not.
        //  • Clear (the setup affordance): the whole mix surface vanishes;
        //    consent revoked again, nothing to Start.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        var consent = WithMixConsent();
        JSInterop.Setup<string?>("localStorage.getItem", MixDraft.StorageKey)
            .SetResult(NeverSeenMix().ToJson()); // persisted from a prior session
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.Enabled);
        // Every pick in this lifecycle lands on a folder that satisfies the mix
        // predicate — the transitions under test are pick/re-pick/Clear, not the
        // predicate, and the fake serves the same stats document to each.
        _folderAccess.PickedStatsJson = StatsDocumentJson(BestPlay());

        var cut = Render<HomePage>();

        // Pick: panel mounts, hydration re-offers the persisted mix — inert.
        // (Each pick also resets the applied filter, so re-arm that half after
        // every pick; the check gesture is gated on it.)
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        await ApplyFiltersAsync(cut);
        Assert.NotEmpty(cut.FindAll(".mix-row"));
        Assert.False(cut.Find("#mixApplies").HasAttribute("checked"));
        Assert.False(StartButton(cut).HasAttribute("disabled"));

        // Check: in effect.
        await cut.Find("#mixApplies").ChangeAsync(new ChangeEventArgs { Value = true });
        Assert.True(consent.Applies);
        Assert.False(StartButton(cut).HasAttribute("disabled"));

        // Re-pick (Enabled): revoke + discard + keyed re-mount → re-hydrated,
        // re-offered, unchecked. Same rows, no effect.
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        await ApplyFiltersAsync(cut);
        Assert.NotEmpty(cut.FindAll(".mix-row"));  // the rows persisted…
        Assert.False(consent.Applies);              // …the consent did not
        Assert.False(cut.Find("#mixApplies").HasAttribute("checked"));
        Assert.False(StartButton(cut).HasAttribute("disabled"));

        // Clear: the mix surface (and Start) vanish entirely; consent revoked.
        await cut.Find("#mixApplies").ChangeAsync(new ChangeEventArgs { Value = true });
        await cut.FindAll("button").First(b => b.TextContent.Trim() == "Clear").ClickAsync(new());
        Assert.Empty(cut.FindComponents<MixPanelComponent>());
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Start Quiz");
        Assert.False(consent.Applies);
        Assert.Empty(Services.GetRequiredService<MixDraft>().Rows);
    }

    [Fact]
    public async Task Home_MixComposesToZero_MixAwareNotice_StaysHome()
    {
        // Parallel to the filtered-to-zero banner: a weighted start that drew
        // nothing stays on Home with wording that names the mix, not the
        // filters. One decision, already seen — a 100% never-seen mix draws 0.
        var d = TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());
        var c = WithWeighableController(out var sink, d);
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty.Plus(
            new SubmittedPlay(TestFixtures.KeyOf(d), BestPlay(), 0, 0.0, IsCorrect: true),
            TimeProvider.System);
        WithPickedFolder(capability: FolderWriteCapability.Enabled, withStatsHistory: true);
        WithAppliedFilter(new FilterConfig());
        WithShuffleOption();
        WithActiveMix(NeverSeenMix());

        var cut = Render<HomePage>();
        await StartButton(cut).ClickAsync(new());

        Assert.Contains("Your mix drew no problems", cut.Markup);
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        Assert.DoesNotContain("/quiz", nav.Uri);
    }

    [Fact]
    public async Task Home_ShuffleCheckbox_DisabledUnderActiveMix_ValueNeverRewritten()
    {
        // Disabled must not mean rewritten: the checkbox greys out while a mix
        // in effect owns order, but ShuffleOption keeps the user's value, so
        // turning the mix off (unchecking) restores the prior preference. The
        // derivation is live — the uncheck alone re-enables, no commit moment.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithPickedFolder(capability: FolderWriteCapability.Enabled, withStatsHistory: true); // the mix predicate: can-save-stats AND has-stats
        WithAppliedFilter(new FilterConfig());
        var shuffle = WithShuffleOption(enabled: true);
        WithActiveMix(NeverSeenMix());

        var cut = Render<HomePage>();

        Assert.True(cut.Find("#shuffleOrder").HasAttribute("disabled"));
        Assert.Contains("order comes from the mix", cut.Markup);
        Assert.True(shuffle.Enabled);

        await cut.Find("#mixApplies").ChangeAsync(new ChangeEventArgs { Value = false });

        Assert.False(cut.Find("#shuffleOrder").HasAttribute("disabled"));
        Assert.True(shuffle.Enabled); // untouched throughout
    }

    // -----------------------------------------------------------------------
    //  A weighted mix requires stats: the shared predicate at the page (#87)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Pick a folder through the real gesture with <paramref name="pickedStats"/>
    /// as whatever its <c>bgquiz-stats.json</c> holds — the arrangement all three
    /// degrade arms share, differing only in that one string. Write capability is
    /// <see cref="FolderWriteCapability.Enabled"/> throughout, so the predicate's
    /// <i>other</i> half is satisfied and the stats document is the only variable.
    /// </summary>
    private async Task<IRenderedComponent<HomePage>> RenderWithPickedStatsAsync(string? pickedStats)
    {
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.Enabled);
        _folderAccess.PickedStatsJson = pickedStats;

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        return cut;
    }

    [Fact]
    public async Task Home_PickWithEmptyStatsDocument_OffersNoMix_AndCommitsNone()
    {
        // #87's headline, and the fix-prover: the folder CAN save stats and has
        // a stats file — it is simply empty. Before the predicate this mounted
        // the panel on capability alone, and a mix built there composed against
        // a record with nothing in it. Now the panel is not offered at all, and
        // there is correspondingly nothing committed for Start to honor.
        var cut = await RenderWithPickedStatsAsync(EmptyStatsDocumentJson());

        Assert.True(Services.GetRequiredService<PickedProblemFolder>().HasFiles); // the pick landed…
        Assert.NotEmpty(cut.FindAll("#shuffleOrder"));                            // …surface disclosed…
        Assert.Empty(cut.FindComponents<MixPanelComponent>());                    // …but no mix
        Assert.False(Services.GetRequiredService<MixConsent>().Applies);          // and none in effect
    }

    [Fact]
    public async Task Home_PickWithNoStatsDocument_OffersNoMix()
    {
        // The brand-new folder — the case the ruling accepts as emergent: no
        // file yet, so nothing to weight by, so no panel until its first quiz
        // creates one.
        var cut = await RenderWithPickedStatsAsync(null);

        Assert.Empty(cut.FindComponents<MixPanelComponent>());
        Assert.False(Services.GetRequiredService<MixConsent>().Applies);
    }

    [Fact]
    public async Task Home_PickWithUnreadableStatsDocument_OffersNoMix_WithoutFailingThePick()
    {
        // The third arm, degrading identically — and quietly. An unreadable
        // stats file is not a pick failure: the folder is held, the surface
        // discloses, the quiz is fully runnable, and the only consequence is
        // that the mix isn't offered. No banner, no notice, nothing thrown.
        var cut = await RenderWithPickedStatsAsync("not json at all");

        Assert.Empty(cut.FindComponents<MixPanelComponent>());
        Assert.True(Services.GetRequiredService<PickedProblemFolder>().HasFiles);
        Assert.DoesNotContain("Could not read the folder", cut.Markup);

        // Fully runnable: apply the filters and Start is live, unweighted.
        await ApplyFiltersAsync(cut);
        Assert.False(StartButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Home_PickWithStatsContent_OffersMix_AndStartComposesWithIt()
    {
        // The positive contrast to the three arms above, through the same real
        // pick gesture: with a stats record present the panel is offered, a mix
        // commits, and Start composes with it (LastComposition is the
        // composed-layer signature). Same arrangement, one different string.
        var c = WithWeighableController(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty;
        WithAppliedFilter();
        WithShuffleOption();
        _folderAccess.NextPickOutcome = OneFileOutcome(capability: FolderWriteCapability.Enabled);
        _folderAccess.PickedStatsJson = StatsDocumentJson(BestPlay());

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        await ApplyFiltersAsync(cut);           // the pick expires the filter stamp
        Assert.Single(cut.FindComponents<MixPanelComponent>());

        await ActivateMixThroughPanelAsync(cut);
        await StartButton(cut).ClickAsync(new());

        Assert.True(c.HasStarted);
        Assert.NotNull(c.LastComposition);
    }

    [Fact]
    public async Task Home_ActiveMix_ThenPickWithoutStatsHistory_RevokesTheConsent()
    {
        // Ruling 3, at the case the predicate newly creates: the outgoing folder
        // had a stats record and a mix in effect; the incoming one can save
        // stats but has none. A non-passthrough mix must not survive into a
        // folder that cannot honor it — and doesn't, because the pick's
        // unconditional consent revoke takes it before the new folder is even
        // known. The rows survive in storage (§4 — they are choice); the
        // effect does not.
        WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        WithAppliedFilter();
        WithShuffleOption();
        var consent = WithMixConsent();
        _folderAccess.NextPickOutcome = OneFileOutcome("WithStats", "a.xg", FolderWriteCapability.Enabled);
        _folderAccess.PickedStatsJson = StatsDocumentJson(BestPlay());

        var cut = Render<HomePage>();
        await cut.Find("#pickProblemFolder").ClickAsync(new());
        await ApplyFiltersAsync(cut);
        await ActivateMixThroughPanelAsync(cut);
        Assert.True(consent.Applies); // in effect for the old folder

        // Re-pick: same write capability, no stats record.
        _folderAccess.NextPickOutcome = OneFileOutcome("Fresh", "b.xg", FolderWriteCapability.Enabled);
        _folderAccess.PickedStatsJson = null;
        await cut.Find("#pickProblemFolder").ClickAsync(new());

        Assert.False(consent.Applies);                         // revoked outright
        Assert.Empty(cut.FindComponents<MixPanelComponent>()); // and not re-offered
        Assert.DoesNotContain("fix it or uncheck", cut.Markup);
    }

    [Fact]
    public async Task Home_FirstQuizCreatesStats_MixOfferedOnTheNextVisit()
    {
        // The accepted emergent behavior, and the reason the predicate has a
        // second reading point. A fresh folder offers no mix; once a quiz has
        // written a record, returning to Home re-initializes the page, the probe
        // re-runs, and the mix is offered from then on — no re-pick required,
        // which is what makes "until its first quiz creates stats" true as
        // written rather than "until you pick the folder again".
        var cut = await RenderWithPickedStatsAsync(null);
        Assert.Empty(cut.FindComponents<MixPanelComponent>());

        // The quiz writes its record into the folder the probe reads from.
        _folderAccess.PickedStatsJson = StatsDocumentJson(BestPlay());

        // Navigate away and back — Home is re-instantiated exactly as it is on
        // the return from Done's "Back to setup".
        await DisposeComponentsAsync();
        var back = Render<HomePage>();

        Assert.Single(back.FindComponents<MixPanelComponent>());
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
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty;
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
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"), away: 1),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"), away: 2),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("c.xgp"), away: 3));
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty;
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
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"), away: 1),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"), away: 2),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("c.xgp"), away: 3));
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty;
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
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty;
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
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty;
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
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty;
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
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("a.xgp"), away: 1),
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId("b.xgp"), away: 2));
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty;
        await c.StartAsync(new FilterConfig(), NeverSeenMix(quizLength: 5));

        var cut = Render<QuizPage>();
        Assert.Contains("Your quiz has", cut.Markup);

        await cut.FindAll("button").First(b => b.TextContent.Trim() == "Skip").ClickAsync(new());

        Assert.Contains("Your quiz has", cut.Markup);
    }

    // -----------------------------------------------------------------------
    //  Dismissible notices (SPEC-quiz-view.md §4). Every notice on the Quiz page
    //  dismisses on a click — the answer to the board space they cost, since the
    //  maximize mode is forbidden from suppressing them. Per occurrence,
    //  transient, and app-scoped, which is three separate claims: these pin each
    //  one, plus the slot key that keeps two notices from dismissing each other.
    // -----------------------------------------------------------------------

    /// <summary>The dismiss button rendered inside <paramref name="alert"/>.</summary>
    private static AngleSharp.Dom.IElement CloseButton(AngleSharp.Dom.IElement alert) =>
        alert.QuerySelector("button.btn-close")!;

    [Fact]
    public async Task Quiz_StatsNotice_ClickingTheAlertDismissesIt()
    {
        // The oversized target: a click anywhere in the alert, not only on the
        // button. That is the low-vision affordance the arc exists for.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        await WithStatsStoreInStatusAsync(QuizStatsStatus.WriteFailed);

        var cut = Render<QuizPage>();
        Assert.Contains("could not be saved", cut.Markup);

        await cut.Find(".quiz-notice").ClickAsync(new());

        Assert.DoesNotContain("could not be saved", cut.Markup);
    }

    [Fact]
    public async Task Quiz_StatsNotice_CloseButtonDismissesIt_AndCarriesItsOwnLabel()
    {
        // The discoverable half, and the accessible one: a bare clickable region
        // has no keyboard or screen-reader affordance at all, so the standard
        // btn-close renders beside it and carries the semantics. What this pins
        // is the button's presence, its label, and that activating it dismisses;
        // it deliberately does NOT claim to distinguish the button's own handler
        // from the alert's via bubbling, which the render layer's event dispatch
        // makes indistinguishable from here. Both are wired, and Dismiss is
        // idempotent, so either route is correct.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        await WithStatsStoreInStatusAsync(QuizStatsStatus.LoadFailed);

        var cut = Render<QuizPage>();
        var close = CloseButton(cut.Find(".quiz-notice"));
        Assert.Equal("Dismiss this message", close.GetAttribute("aria-label"));

        await close.ClickAsync(new());

        Assert.DoesNotContain("couldn't be read", cut.Markup);
    }

    [Fact]
    public async Task Quiz_StatsNotice_DismissedThenShowStatsRoundTrip_StaysDismissed()
    {
        // App-scoped, not a page field: "Show stats" is a mainline mid-quiz
        // gesture and returning re-instantiates this page. A dismissal the user
        // made must not come back with it.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        await WithStatsStoreInStatusAsync(QuizStatsStatus.WriteFailed);

        var cut = Render<QuizPage>();
        await cut.Find(".quiz-notice").ClickAsync(new());

        Assert.DoesNotContain("could not be saved", Render<QuizPage>().Markup);
    }

    [Fact]
    public async Task Quiz_StatsNotice_ANewBind_ShowsFreshEvenOnTheSameStatus()
    {
        // "The next occurrence shows fresh", in the case that a status-value key
        // would get wrong: a second quiz bound against the same unreadable file
        // lands on the status already showing, so SetStatus reports no
        // transition — and that run still records nothing, which the user has not
        // been told. BeginQuizAsync mints the occurrence, so the notice returns.
        var c = WithController(TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        var store = await WithStatsStoreInStatusAsync(QuizStatsStatus.LoadFailed);

        var cut = Render<QuizPage>();
        await cut.Find(".quiz-notice").ClickAsync(new());
        Assert.DoesNotContain("couldn't be read", cut.Markup);

        await store.BeginQuizAsync();                       // the next run binds
        Assert.Equal(QuizStatsStatus.LoadFailed, store.Status); // same status…

        Assert.Contains("couldn't be read", Render<QuizPage>().Markup); // …new notice
    }

    [Fact]
    public async Task Quiz_MixNotice_ClickDismissesIt_WithoutWaitingForAnAnswer()
    {
        // The composition notice gains the click gesture and keeps its
        // retire-on-first-answer: either gesture ends it. This is the half the
        // existing pins could not cover — a user who has read it before
        // answering gets the board space back immediately.
        var c = WithWeighableController(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty;
        await c.StartAsync(new FilterConfig(), NeverSeenMix(quizLength: 5));

        var cut = Render<QuizPage>();
        Assert.Contains("Your quiz has", cut.Markup);

        await cut.Find(".quiz-notice").ClickAsync(new());

        Assert.DoesNotContain("Your quiz has", cut.Markup);
        Assert.Null(c.Review);   // nothing was answered to get here
        Assert.DoesNotContain("Your quiz has", Render<QuizPage>().Markup); // and it survives the round trip
    }

    [Fact]
    public async Task Quiz_DismissingOneNotice_LeavesTheOtherStanding()
    {
        // The slot key's whole job. Both notices render together (a weighted run
        // whose stats context then degrades), and they dismiss independently —
        // one holder slot per notice, never a single "last dismissed" token that
        // the second dismissal would overwrite and the first would lose.
        var c = WithWeighableController(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty;
        await c.StartAsync(new FilterConfig(), NeverSeenMix(quizLength: 5));
        await WithStatsStoreInStatusAsync(QuizStatsStatus.WriteFailed);

        var cut = Render<QuizPage>();
        Assert.Equal(2, cut.FindAll(".quiz-notice").Count);

        // Dismiss the stats one (it renders first, above the mix notices).
        await cut.FindAll(".quiz-notice")[0].ClickAsync(new());

        Assert.DoesNotContain("could not be saved", cut.Markup);
        Assert.Contains("Your quiz has", cut.Markup);

        // …and now the other, independently.
        await cut.Find(".quiz-notice").ClickAsync(new());
        Assert.Empty(cut.FindAll(".quiz-notice"));
    }

    [Fact]
    public async Task Quiz_LengthBoundMixExactFill_NoNotice()
    {
        // Target met with every entry filling its own share: the quiz matches
        // the ask exactly, so no mix notice of any kind renders.
        var c = WithWeighableController(out var sink,
            TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay()));
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty;
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
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty;
        await c.StartAsync(new FilterConfig(), NeverSeenMix());
        c.SubmitPlay(BestPlay());
        await c.ContinueAsync(); // exhausts the one-problem source → finished
        Assert.True(c.IsFinished);

        sink.CanWeightMix = false; // e.g. the pick was cleared between quizzes
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
        out GatedProblemSetSource source, out FakeProblemStatsSink sink,
        params BgDecisionData[] items)
    {
        var gated = new GatedProblemSetSource(items);
        source = gated;
        sink = new FakeProblemStatsSink();
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

            // End quiz is a transition too — the gate would no-op it anyway, so
            // it disables with its neighbours rather than looking live.
            Assert.True(EndQuizButton(cut).HasAttribute("disabled"));
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
            """
            {"homeBoardOnRight":false,"randomizeSidePerProblem":true,
             "keepNavigationPanelFolded":true,"maximizeBoardWhileAnswering":true}
            """);

        var cut = Render<SettingsPage>();

        Assert.False(cut.Find("#settingsSideRight").HasAttribute("checked"));
        Assert.True(cut.Find("#settingsSideLeft").HasAttribute("checked"));
        Assert.True(cut.Find("#settingsRandomizeSide").HasAttribute("checked"));
        Assert.True(cut.Find("#settingsKeepNavFolded").HasAttribute("checked"));
        Assert.True(cut.Find("#settingsMaximizeBoard").HasAttribute("checked"));
    }

    [Fact]
    public void Settings_MaximizeBoard_IsTheSoleControlForTheMode_AndSitsWithTheBoard()
    {
        // Fork D (SPEC-quiz-view.md §3): the Settings checkbox is the ONLY write
        // surface for the maximize mode — no on-page toggle on the quiz itself,
        // which would be a second writer of one fact and would force QuizSettings
        // to grow notify plumbing its contract defers. Pinned here as placement
        // (it belongs to the board's fieldset, beside the side settings, not to
        // the navigation panel's) plus the label a user reads.
        WithController();

        var cut = Render<SettingsPage>();

        var control = cut.Find("#settingsMaximizeBoard");
        Assert.False(control.HasAttribute("checked")); // default off — today's page
        Assert.Same(
            control.Closest("fieldset"),
            cut.Find("#settingsRandomizeSide").Closest("fieldset"));
        Assert.Contains(
            "Make the board as large as possible while you answer",
            Normalize(control.Closest("fieldset")!.TextContent));
    }

    [Fact]
    public void Settings_MaximizeBoard_StatesTheSizeChangeItCauses()
    {
        // The consequence the model ratified rather than hid: the board is
        // deliberately a different size while answering than while reading. A
        // user who sees it move must be able to read that as the feature working.
        // Keyed on the fieldset's own text, so a reworded row that drops the
        // claim fails here instead of going vacuously green.
        WithController();

        var cut = Render<SettingsPage>();

        var fieldset = Normalize(cut.Find("#settingsMaximizeBoard").Closest("fieldset")!.TextContent);
        Assert.Contains("changes size between answering and reading", fieldset);
        Assert.Contains("Everything comes back when you submit", fieldset);
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

        await cut.Find("#settingsMaximizeBoard").ChangeAsync(new() { Value = true });
        Assert.True(Settings().MaximizeBoardWhileAnswering);

        // …and each landed in the one storage entry, with no further gesture.
        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == QuizSettings.StorageKey).Arguments[1] as string;
        Assert.Equal(
            """{"homeBoardOnRight":false,"randomizeSidePerProblem":true,"keepNavigationPanelFolded":true,"maximizeBoardWhileAnswering":true}""",
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
