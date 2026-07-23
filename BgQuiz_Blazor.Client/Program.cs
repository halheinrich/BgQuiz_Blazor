// *** CLIENT PROJECT — BgQuiz_Blazor.Client (WASM) ***
//
// Hosts the interactive quiz surface. Everything the quiz needs runs in the
// browser-wasm runtime: the quiz state machine (QuizController), the active
// problem-set source, board rendering, and in-browser .xg/.xgp parsing.

using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;

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

// The one gateway to the browser's folder facilities (folderAccess.js): both
// pick mechanisms, buffered file reads, and the stats file's read/write. Pages
// and the stats store depend on the interface; directory handles stay in JS
// module state and never cross the interop boundary.
builder.Services.AddScoped<IFolderAccess, JsFolderAccess>();

// Per-app holder for the user's picked problem folder (files + pick-time
// stats-saving capability); Home writes it, the source factory below reads it
// at quiz-start, the stats store reads the capability at its Start-time bind.
builder.Services.AddScoped<PickedProblemFolder>();

// Lifetime-stats document lifecycle: binds the picked folder's stats context at
// every quiz Start/Restart (the promote), folds each finalized submission, and
// writes bgquiz-stats.json back after every fold. Registered once and aliased
// as IDecisionStatsSink so the controller's sink and the pages' status notices
// observe the same instance.
builder.Services.AddScoped<QuizStatsStore>();
builder.Services.AddScoped<IDecisionStatsSink>(sp => sp.GetRequiredService<QuizStatsStore>());

// Per-app holder for the filter half of Home's start gate: the config the user
// deliberately applied. Scoped so the gate survives in-app navigation (Home is
// re-instantiated on navigate-back); read only by Home.
builder.Services.AddScoped<AppliedFilter>();

// Per-app holder for the picked folder's saved named-filter collection
// (bgquiz-filters.json, beside the corpus and the stats file). Home loads it at
// pick time and drives its save/delete edits; the store reads/writes the JS
// module's picked slot, so it never touches a running quiz's active-slot stats.
// Degrade-never-block like QuizStatsStore. Read only by Home.
builder.Services.AddScoped<SavedFiltersStore>();

// Per-app holder for the "Shuffle order" toggle — a presentation-only choice,
// deliberately separate from AppliedFilter/FilterConfig. Scoped for the same
// navigate-back-survival reason as the other start-gate holders.
builder.Services.AddScoped<ShuffleOption>();

// Per-app holder for the committed stats-weighted mix — the mix sibling of
// AppliedFilter. Blank (QuizMix.Empty) is the valid default, so only its
// dirty state gates Start; the mix panel's localStorage restore re-adopts the
// previously-applied mix here on boot. Scoped for navigate-back survival like
// the other start-gate holders.
builder.Services.AddScoped<AppliedMix>();

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
