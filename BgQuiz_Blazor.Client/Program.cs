// *** CLIENT PROJECT — BgQuiz_Blazor.Client (WASM) ***
//
// Hosts the interactive quiz surface. Everything the quiz needs runs in the
// browser-wasm runtime: the quiz state machine (QuizController), the active
// problem-set source, board rendering, and in-browser .xg/.xgp parsing.

using BgFolderAccess_Razor;
using BgQuiz_Blazor.Client.Quiz;
using BgUiPrimitives_Razor;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using XgFilter_Razor;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// The clock seam: BgGame_Lib's ProblemStatsDocument folds resolve GetUtcNow
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
// stage-1 refusal both read (issue halheinrich/backgammon#87); that probe touches the picked slot
// only and never the active context above. Registered once and aliased as
// IProblemStatsSink so the controller's sink and the pages' status notices
// observe the same instance.
builder.Services.AddScoped<QuizStatsStore>();
builder.Services.AddScoped<IProblemStatsSink>(sp => sp.GetRequiredService<QuizStatsStore>());

// The one guarded access to the browser's localStorage and sessionStorage
// (BgUiPrimitives_Razor, halheinrich/backgammon#374): QuizSettings, MixDraft,
// NotesPlacementStore and QuizLiveMarker make every storage call through it,
// and each keeps its own parsing, defaults and refusal reporting.
// AddFilterSurface below registers it too; stated here because this app's own
// stores use it, and a dependency a project uses is one it states. A second
// call adds nothing.
builder.Services.AddBrowserStorage();

// The filter surface (XgFilter_Razor): the app-scoped setup-state owner,
// FilterSetup — the draft, what is applied, what this boot restored — which
// Home and the FilterSurface it hosts both inject, so they share one instance
// by construction, and the surface's storage, whose every refusal goes to the
// sink named here. The sink is this app's adapter onto BrowserStorageCondition
// (registered below), so the surface's refusals and the app's own stores'
// reach one occurrence and one notice (SPEC-notices.md §2). Scoped is the app
// in WebAssembly, which is what lets the setup outlive every page. Client
// only: every BgQuiz page renders with prerender: false, so the server never
// renders the surface.
builder.Services.AddScoped<FilterStorageRefusalSink>();
builder.Services.AddFilterSurface<FilterStorageRefusalSink>();

// Home's match count, keyed by the selection, the filter in effect and the
// ranking, held here so a navigate-back reuses the count it left — and the
// known-zero Start gate it feeds — rather than losing it with the page.
// Scoped beside the filter setup it describes.
builder.Services.AddScoped<MatchCount>();

// The document-storage seam: XgFilter_Razor's IDocumentStorage over
// BgFolderAccess_Razor's picked-slot file I/O — the one-line adapter glue the
// two producers deliberately leave to the host. Home hands it to FilterSurface
// while the pick's capability exposes a readable handle; the composite owns the
// document lifecycle (read at mount/source change, save/delete edits, degrade
// states) over it. Scoped: the composite rebuilds its store when the bound
// adapter *reference* changes, so the instance must be stable per app.
builder.Services.AddScoped<PickedFolderDocumentStorage>();

// Per-app holder for the "Shuffle order" toggle — a presentation-only choice,
// deliberately separate from the filter setup and its FilterConfig. Scoped for
// the same navigate-back-survival reason as the other start-gate holders.
builder.Services.AddScoped<ShuffleOption>();

// The stats-weighted mix, as two sibling per-app services with one lifetime:
// the visible derivation (MixVisibility — the setting is on AND the picked
// folder holds stats, SPEC-filtering.md §5's "Visible means in effect") and
// the mix draft (MixDraft — the panel's edit state, hoisted out of the
// component so mix edits survive in-app navigation). There is NO committed
// copy: what runs, while the panel is visible, is the draft itself
// (MixDraft.Build), so screen and effect cannot diverge. The draft owns the
// one localStorage key with last-valid write-through persistence — every
// mutation that validates writes, blank included — plus a once-per-setup
// hydration that re-offers the stored mix.
//
// MixVisibility holds no state: its two inputs are a QuizSettings choice,
// which outlives every setup (§4), and the pick-time stats probe, which
// expires with the pick. Nothing here is reset at setup end any more — the
// consent bit that was is gone with the ruling that superseded it.
builder.Services.AddScoped<MixVisibility>();
builder.Services.AddScoped<MixDraft>();

// Per-app dismissal state for every notice on the Quiz page: the composition
// notice (which the first submitted answer also retires — it says how the
// running quiz was composed, read before answering and stale chrome after) and
// the stats-context degrade notice. Scoped rather than a page field so the
// Show-stats round trip can't resurrect a dismissal; each keyed on its notice's
// current occurrence, so the next run — or the next stats transition — shows
// again with no reset call site. The controller's composition telemetry and the
// store's status are never touched.
builder.Services.AddScoped<QuizNoticeDismissal>();

// The one fact that the browser has refused a storage call this visit
// (halheinrich/backgammon#360): every store below that touches localStorage or
// sessionStorage reports a refusal here, and so does the filter surface,
// through FilterStorageRefusalSink above, so Home's storage notice has one
// occurrence however many keys were refused, and by whom. Scoped beside the dismissal holder, which keys that
// notice on it: the occurrence lasts the visit — remounts, navigation and later
// refusals keep it, a reload starts afresh — so a dismissal does too.
builder.Services.AddScoped<BrowserStorageCondition>();

// Per-app user settings (localStorage-backed): the home-board side, whether it
// re-rolls per problem, whether the board is maximized while answering, and
// whether the navigation panel stays folded. Scoped like the holders, so one
// hydration serves the whole app and every page reads the same instance.
// Deliberately no draft/commit lifecycle — a setting applies and persists the
// moment it changes.
builder.Services.AddScoped<QuizSettings>();

// Per-app holder of the decision's notes' placement preference (localStorage-
// backed, its own key): where the notes overlay opens, set by moving it and
// cleared by its Reset (SPEC-quiz-view.md §4, halheinrich/backgammon#344).
// Scoped like the settings, so the placement survives closing the notes, the
// next problem, navigation and a new quiz in memory, and a reload in storage —
// and a write the browser refuses still holds for the session.
builder.Services.AddScoped<NotesPlacementStore>();

// Per-app marker (sessionStorage-backed) recording that a quiz is live in this
// tab, so a full reload — which reboots the runtime and discards quiz state —
// can be acknowledged on the next boot instead of dumping the user on a blank
// Home. Scoped like the holders; Home sets/reads it, Done clears it on
// completion. See QuizLiveMarker for why the store is sessionStorage.
builder.Services.AddScoped<QuizLiveMarker>();

// Source factory: the layer stack the running quiz draws through — parse-once
// cache over the pick, position dedupe, then a conditional shuffle. Which
// layers, in which order, and why lives with PickedFolderSourceFactory,
// deliberately: the composition is the app's most wiring-sensitive code, so it
// sits in a named type the tests can call rather than in a lambda they could
// only re-type by hand. This registration's whole job is to resolve the
// app-scoped ingredients and hand them over; every one of them is read live at
// invocation (QuizController.StartAsync), not here — so a pick or a shuffle
// toggle made before Start takes effect on that Start.
builder.Services.AddScoped<ProblemSetSourceFactory>(sp =>
    PickedFolderSourceFactory.Create(
        sp.GetRequiredService<PickedProblemFolder>(),
        sp.GetRequiredService<ShuffleOption>(),
        sp.GetRequiredService<ILoggerFactory>(),
        sp.GetRequiredService<TimeProvider>()));

await builder.Build().RunAsync();
