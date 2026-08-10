// *** CLIENT PROJECT — BgQuiz_Blazor.Client (WASM) ***
//
// Hosts the interactive quiz surface. Everything the quiz needs runs in the
// browser-wasm runtime: the quiz state machine (QuizController), the active
// problem-set source, board rendering, and in-browser .xg/.xgp parsing.

using BgFolderAccess_Razor;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using XgFilter_Razor;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// The clock seam: BgGame_Lib's DecisionStatsDocument folds resolve GetUtcNow
// from a TimeProvider, so the app hands the system clock in exactly once here
// and nothing ever reads ambient time (tests substitute a fixed provider).
builder.Services.AddSingleton(TimeProvider.System);

// Per-app quiz state. In the WASM client "scoped" resolves to one instance per
// loaded app (one tab), so state survives in-app navigation and resets only on a
// full reload — see QuizController's lifetime docs. Pages observe via StateChanged.
builder.Services.AddScoped<QuizController>();

// The one gateway to the browser's folder facilities — BgFolderAccess_Razor's
// JS-backed implementation (its folderAccess.js ships as the lib's static web
// asset): both pick mechanisms, buffered file reads, and named-file I/O on the
// picked/active slots. Pages and the stats store depend on the interface;
// directory handles stay in JS module state and never cross the interop
// boundary. The caps the pick enforces are host policy: the one FolderPickLimits
// instance is built from PickedFileLimits' table here — the lib ships no numbers.
builder.Services.AddSingleton(new FolderPickLimits(
    PickedFileLimits.MaxFileCounts, PickedFileLimits.MaxFileBytes));
builder.Services.AddScoped<IFolderAccess, JsFolderAccess>();

// Per-app holder for the user's picked problem folder (files + pick-time
// stats-saving capability); Home writes it, the source factory below reads it
// at quiz-start, and the stats store reads the capability at its Start-time
// bind and again — with the pick generation — for the mix predicate's probe.
builder.Services.AddScoped<PickedProblemFolder>();

// Lifetime-stats document lifecycle: binds the picked folder's stats context at
// every quiz Start/Restart (the promote), folds each finalized submission, and
// writes bgquiz-stats.json back after every fold. It also owns the pick-time
// probe behind CanWeightMix — the one predicate for "can a weighted mix mean
// anything for this folder", which Home's panel gate and the controller's
// stage-1 refusal both read (issue #87); that probe touches the picked slot
// only and never the active context above. Registered once and aliased as
// IDecisionStatsSink so the controller's sink and the pages' status notices
// observe the same instance.
builder.Services.AddScoped<QuizStatsStore>();
builder.Services.AddScoped<IDecisionStatsSink>(sp => sp.GetRequiredService<QuizStatsStore>());

// Per-app holder for the filter half of Home's start gate — XgFilter_Razor's
// AppliedFilter, mediated by the FilterSurface Home hosts (a commit Sets it
// keyed to the pick's source token; uncommitted-edit reports Clear it).
// Scoped so the gate survives in-app navigation (Home is re-instantiated on
// navigate-back, and the composite dies with the page while this holder must
// not); read only by Home, and only ever source-relatively.
builder.Services.AddScoped<AppliedFilter>();

// The restored-filter notice's state, beside the holder above and for the same
// reason: a full reload constructs a fresh instance, and that construction is
// precisely what tells a boot's localStorage restore (say so — the spec's §4
// legibility rule) apart from a navigate-back remount over the same setup
// (say nothing). Scoped, therefore, not per-page. Deliberately opaque to this
// host: every member that moves it is producer-internal, so Home's whole
// contract is to register the instance here and bind it to FilterSurface.
builder.Services.AddScoped<FilterRestoreNotice>();

// The saved-filters storage seam: XgFilter_Razor's IFilterDocumentStorage over
// BgFolderAccess_Razor's picked-slot file I/O — the one-line adapter glue the
// two producers deliberately leave to the host. Home hands it to FilterSurface
// while the pick's capability exposes a readable handle; the composite owns the
// document lifecycle (read at mount/source change, save/delete edits, degrade
// states) over it. Scoped: the composite rebuilds its store when the bound
// adapter *reference* changes, so the instance must be stable per app.
builder.Services.AddScoped<PickedFolderFilterStorage>();

// Per-app holder for the "Shuffle order" toggle — a presentation-only choice,
// deliberately separate from AppliedFilter/FilterConfig. Scoped for the same
// navigate-back-survival reason as the other start-gate holders.
builder.Services.AddScoped<ShuffleOption>();

// The stats-weighted mix, as two sibling per-app services with one lifetime:
// the consent bit (MixConsent — the "Mix applies" checkbox state; checked
// means the on-screen mix is in effect, SPEC-filtering.md §5) and the mix
// draft (MixDraft — the panel's edit state, hoisted out of the component so
// mix edits survive in-app navigation). There is NO committed copy: what
// runs, when consented, is the draft itself (MixDraft.Build), so screen and
// effect cannot diverge. The draft owns the one localStorage key with
// last-valid write-through persistence — every mutation that validates
// writes, blank included — plus a once-per-setup hydration that re-offers
// the stored mix, inert until the user checks the box. Consent is reset at
// setup end and dies on reload with the scope: §4's "choices outlive the
// setup; consent does not", by construction.
builder.Services.AddScoped<MixConsent>();
builder.Services.AddScoped<MixDraft>();

// Per-app dismissal state for the Quiz page's mix composition notice: the notice
// says how the running quiz was composed — read before answering, stale chrome
// after — so the first submitted answer retires it. Scoped rather than a page
// field so the Show-stats round trip can't resurrect it; keyed on the
// composition's identity, so a new run's notice shows again with no reset call
// site. The controller's composition telemetry is never touched.
builder.Services.AddScoped<MixNoticeDismissal>();

// Per-app user settings (localStorage-backed): the home-board side, whether it
// re-rolls per problem, and whether the navigation panel stays folded. Scoped
// like the holders, so one hydration serves the whole app and every page reads
// the same instance. Deliberately no draft/commit lifecycle — a setting applies
// and persists the moment it changes.
builder.Services.AddScoped<QuizSettings>();

// Per-app marker (sessionStorage-backed) recording that a quiz is live in this
// tab, so a full reload — which reboots the runtime and discards quiz state —
// can be acknowledged on the next boot instead of dumping the user on a blank
// Home. Scoped like the holders; Home sets/reads it, Done clears it on
// completion. See QuizLiveMarker for why the store is sessionStorage.
builder.Services.AddScoped<QuizLiveMarker>();

// Source factory: builds a CachedProblemSetSource over whatever files the
// user has picked at quiz-start — the parse-once layer that parses the pick
// unfiltered on the first Start and serves every later Start/Restart by
// filtering the cached decisions (the cache slot rides PickedProblemFolder,
// so a re-pick/Clear invalidates it by construction) — then wraps it in a
// ShuffledProblemSetSource when the user asked to shuffle. Both the picked
// set and the shuffle toggle are read at invocation time
// (QuizController.StartAsync), not registration, so choices made before Start
// take effect. The unseeded ShuffledProblemSetSource ctor is used here
// deliberately — reproducibility is a test-only concern (see
// ShuffledProblemSetSource's seeded ctor), never user-facing.
//
// Shuffle applies only to a passthrough (blank-mix) run: an active mix owns
// presentation order through its own RandomOrder toggle, and a shuffled inner
// under the composing decorator would silently break RandomOrder:false's
// fully-deterministic contract (draws and presentation in source order). The
// composition layer itself is the controller's to wire, not the factory's.
builder.Services.AddScoped<ProblemSetSourceFactory>(sp =>
{
    var picked = sp.GetRequiredService<PickedProblemFolder>();
    var shuffle = sp.GetRequiredService<ShuffleOption>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var clock = sp.GetRequiredService<TimeProvider>();
    return (filters, mix) =>
    {
        IProblemSetSource inner = new CachedProblemSetSource(picked, filters, loggerFactory, clock);
        return mix.IsPassthrough && shuffle.Enabled ? new ShuffledProblemSetSource(inner) : inner;
    };
});

await builder.Build().RunAsync();
