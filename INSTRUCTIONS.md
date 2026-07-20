# BgQuiz_Blazor

> Collaboration contract: [`../AGENTS.md`](../AGENTS.md)
> Umbrella status & dependency graph: [`../INSTRUCTIONS.md`](../INSTRUCTIONS.md)
> Mission & principles: [`../VISION.md`](../VISION.md)

## Stack

C# / .NET 10 / Blazor Web App, **WebAssembly** render mode — a thin
ASP.NET Core host project (`BgQuiz_Blazor`) serving a WASM client project
(`BgQuiz_Blazor.Client`) that runs the entire interactive quiz in the
browser. Visual Studio 2026 on Windows.

## Solution

`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\BgQuiz_Blazor\BgQuiz_Blazor.slnx`

## Repo

https://github.com/halheinrich/BgQuiz_Blazor — branch `main`.

## Depends on

- **BgGame_Lib** — substrate. `IProblemSetSource`, `ShuffledProblemSetSource`,
  `SubmittedPlay`, `SubmittedCubeAction`, `QuizScore` (segmented:
  `PlayDecisions` / `DoubleDecisions` / `TakeDecisions` + derived `Total`),
  the stats-weighted composition surface — `QuizCategory`/`QuizCategoryKind`
  (predicates over lifetime stats), `QuizMix`/`QuizMixEntry` (the versioned
  strict-JSON mix config; `ToJson`/`FromJson`/`TryFromJson` is the
  localStorage trio), `MixedProblemSetSource` (the composing decorator the
  controller wires for a non-blank mix) + `MixComposition` telemetry —
  and the lifetime-stats model `DecisionStats` / `DecisionStatsDocument`
  (immutable; `doc = doc.Plus(submission, TimeProvider)`; bundled type-level
  JSON converter, so `JsonSerializer.Deserialize<DecisionStatsDocument>` needs
  no registration and any bad load throws `JsonException`; a cube position
  folds as **two** lifetime decisions — one per half, counted separately —
  matching `QuizScore`'s two-half fold, so a half-right cube reads 1-of-2).
  The controller talks to the source through `IProblemSetSource` and scores
  via `QuizScore.Plus(SubmittedPlay)` / `QuizScore.Plus(SubmittedCubeAction)`;
  the stats store folds finalized submissions via the document's `Plus`.
  `ShuffledProblemSetSource` is the decorator the source factory wraps the
  picked set in when "Shuffle order" is on; per its own doc (BgGame_Lib
  INSTRUCTIONS.md) it draws a **fresh Fisher-Yates permutation per
  enumeration** — so a Restart reshuffles rather than replaying the same
  order — and exposes two constructors: the **unseeded** one (used here, in
  production) seeds from a non-deterministic source, the **seeded** one is for
  deterministic tests. It materializes the inner source up front to shuffle,
  and passes `Name` / `Count` through unchanged.
- **BgDataTypes_Lib** — data types. `BgDecisionData`, `Play`,
  `PlayCandidate`, `BoardState`, `CubeDecisionPair`, `CubeAction`. The
  matcher compares the submitted `Play` against each `PlayCandidate.Play`
  by canonical `Play` equality (order- and decomposition-insensitive,
  hit-sensitive); cube scoring reads `DecisionData`'s
  `BestDoublerAction` / `BestTakerAction` / `DoublerActionError` /
  `TakerActionError`.
- **BgMoveGen** — `MoveGenerator.GeneratePlays`, used by the controller's
  pass-position auto-skip detection.
- **BgDiag_Razor** — `BackgammonPlayEntry` (click-driven play assembly),
  `BackgammonCubeActions` (a free-standing, board-free four-radio group for the
  cube answer — `CubeDecisionPair? Value` + `EventCallback<CubeDecisionPair?>
  ValueChanged`, the `@bind-Value` convention) + the underlying
  `BackgammonDiagram` (read-only board view, used directly for both the review
  diagram and the cube-answering board).
- **BackgammonDiagram_Lib** — `DiagramRequest` + `DiagramOptions`. The
  answering view uses `DiagramRequest.FromDecisionData(BgDecisionData,
  DiagramMode.Problem)`, the canonical data-to-renderer mapping (Problem mode
  blanks the analysis panel for both play and cube decisions, so it never leaks
  the answer). The review view uses `DiagramRequest.Builder.From(..., 
  DiagramMode.Solution)` and overrides `UserPlayIndex` / `UserDoubleError` /
  `UserTakeError` to mark the quiz user's answer (`FromDecisionData` can't be
  used there — it would default those marks from the recorded player). Direct
  `<ProjectReference>` — the page calls the factory by name, so the dependency
  is made explicit rather than relying on BgDiag_Razor's transitive surface.
  Only the **native-free core** is referenced; the raster/export sibling
  `BackgammonDiagram_Lib.ExportRaster` (SkiaSharp / QuestPDF / OpenXml) is
  deliberately **not** referenced — the quiz renders SVG only, and the core
  must stay native-free to run under browser-wasm (see Pitfalls).
- **XgFilter_Lib** — `DecisionFilterSet`, `FilterConfig`,
  `DecisionTypeFilter` / `DecisionTypeOption` (materialized from the user's
  decision-type choice; the controller adds no filter of its own).
- **XgFilter_Razor** — `FilterPanel.razor`. Hosted on `/` so quiz-start
  filters share the same UI used by `ExtractFromXgToCsv`.
- **ConvertXgToJson_Lib** — picked up transitively via the filter pipeline
  (parses the user's browser-picked `.xg` / `.xgp` bytes in-browser, via
  `FilteredDecisionIterator.IterateXgStreamDiagrams`).

## Directory tree

```
BgQuiz_Blazor.slnx

BgQuiz_Blazor/                      — thin ASP.NET Core WASM host (server)
  BgQuiz_Blazor.csproj              — Sdk.Web; references only the .Client
  Program.cs                        — AddInteractiveWebAssemblyComponents,
                                      MapRazorComponents<App> + WASM render mode
  appsettings.json
  appsettings.Development.json
  Properties/
    launchSettings.json
  Components/
    _Imports.razor
    App.razor                       — host shell (<head>, blazor.web.js, <Routes/>)
    Routes.razor                    — <Router> over the .Client _Imports assembly
    Layout/
      MainLayout.razor / .razor.css
      NavMenu.razor / .razor.css
    Pages/
      Error.razor
      NotFound.razor
  wwwroot/                          — static assets (favicon, app.css, Bootstrap)

BgQuiz_Blazor.Client/              — WASM client (the whole interactive surface)
  BgQuiz_Blazor.Client.csproj       — Sdk.BlazorWebAssembly; the bg-lib closure
  Program.cs                        — WebAssemblyHostBuilder; registers
                                      TimeProvider.System (singleton) +
                                      QuizController, IFolderAccess/JsFolderAccess,
                                      PickedProblemFolder, QuizStatsStore
                                      (aliased as IDecisionStatsSink),
                                      AppliedFilter, AppliedMix, ShuffleOption,
                                      QuizLiveMarker, ProblemSetSourceFactory
                                      (all scoped)
  _Imports.razor
  wwwroot/
    js/folderAccess.js              — the app's ONE authored JS module: both pick
                                      mechanisms + stats read/write; two-slot
                                      (picked/active) directory-handle state
  Quiz/
    QuizController.cs                 — + ProblemSetSourceFactory, QuizStartOutcome
    ProblemReview.cs
    FolderAccess.cs                 — StatsSaveCapability, FolderPickOutcome,
                                      IFolderAccess (the interop facade contract)
    JsFolderAccess.cs               — the one type touching IJSObjectReference
    PickedProblemFolder.cs          — picked-folder holder (+ PickedFile, Summary,
                                      pick-time StatsSaveCapability)
    PickedFileLimits.cs             — pick caps (bytes / count / derived MB)
    QuizStatsFile.cs                — stats filename + JsonSerializerOptions SSOT
    QuizStatsStore.cs               — IDecisionStatsSink + the stats document
                                      lifecycle (bind at Start, fold + write-back)
    AppliedFilter.cs                — applied-filter holder (start-gate half)
    AppliedMix.cs                   — committed-mix holder (start-gate third)
    MixDisplay.cs                   — mix wording SSOT (labels + refusal reason)
    ShuffleOption.cs                — "shuffle order" toggle holder
    QuizLiveMarker.cs               — sessionStorage was-a-quiz-live marker
    WasmUploadedProblemSetSource.cs — in-browser stream-backed source (the parser)
    CachedProblemSetSource.cs       — parse-once layer over the holder's cache;
                                      the production source the factory builds
  Components/
    Pages/
      Home.razor / .razor.cs        — landing: folder picker + filter panel +
                                      mix panel + Start
      MixPanel.razor / .razor.cs    — stats-weighted mix builder (xg_quizMix)
      Quiz.razor / .razor.cs        — active problem (play or cube)
      Done.razor / .razor.cs        — final summary
      Stats.razor / .razor.cs       — read-only mid-quiz stats (live Controller)
      Help.razor / .razor.cs        — end-user documentation (never redirects)
      ScorePanel.razor              — compact header strip (Total only)
      ScoreBreakdown.razor          — four-way Play/Double/Take/Total table

BgQuiz_Blazor.Tests/
  BgQuiz_Blazor.Tests.csproj
  TestFixtures.cs
  FakeProblemSetSource.cs
  GatedProblemSetSource.cs          — externally-completable MoveNextAsync
                                      (freezes the controller mid-advance)
  FakeFolderAccess.cs               — scriptable IFolderAccess double (store + pages)
  FakeDecisionStatsSink.cs          — recording sink double (controller + pages)
                                      + scriptable RecordGate (freezes the fold)
  QuizControllerTests.cs
  QuizControllerOverlapTests.cs     — the transition-gate overlap suite
  CachedProblemSetSourceTests.cs    — parse-once / invalidation / equivalence
  MixPanelTests.cs                  — builder round-trip / validation / order pins
  AppliedMixTests.cs
  QuizStatsStoreTests.cs            — bind / fold / write-back / degrade guarantees
  JsFolderAccessTests.cs            — interop result mapping via bUnit SetupModule
  WasmUploadedProblemSetSourceTests.cs
  PickedProblemFolderTests.cs
  AppliedFilterTests.cs
  PageTests.cs
  NavMenuTests.cs                   — the sidebar Help link (sole /help entry point)
  MainLayoutTests.cs
  NotFoundPipelineTests.cs          — WebApplicationFactory wire tests: unmatched
                                      paths 404 with the NotFound page body

BgQuiz_Blazor.E2eTests/            — browser e2e smoke gate (Playwright/Chromium
                                      against the published artifact — see
                                      Architecture § The e2e smoke gate)
  BgQuiz_Blazor.E2eTests.csproj     — xunit + Microsoft.Playwright; deliberately
                                      references no app project (black-box, over HTTP)
  Fixtures/                         — committed single-decision .xgp problem files
    BothAnalysis.xgp                — cube decision; best action "No Double"
    Opening 32 65 64 31 65.xgp      — 6-5 checker play; best play 24/13
  PublishedAppFixture.cs            — publish (Release) + spawn once per run;
                                      base-URL seam (BGQUIZ_E2E_BASE_URL)
  PlaywrightFixture.cs              — Chromium lifecycle; fail-loud on missing browsers
  E2eCollection.cs                  — the single (sequential) test collection
  E2eTestBase.cs                    — per-test browser context + shared flow helpers
                                      (+ ContextInitScript seam; temp-dir folder picks)
  FsAccessFakeTestBase.cs           — the fake showDirectoryPicker seam, shared by
                                      the stats-persistence and mix-weighting suites
  QuizFlowTests.cs                  — cube + checker primary paths, pick → Done
  EmptyFilterBannerTests.cs         — empty-result banner; no 0/0 bounce
  ReloadNoticeTests.cs              — reload-reset notice, Start and Restart paths
  StatsPersistenceTests.cs          — FS-Access stats path via the fake (+ fallback
                                      notice pin)
  MixWeightingTests.cs              — weighted start to Done; composed-to-zero via
                                      the app's own write fed back; refusal + override
  HelpAndTitlesTests.cs             — /help renders; document.title contract
  NotFoundTests.cs                  — unknown URL → 404 status + styled body
```

Each page carries a per-page
`@rendermode @(new InteractiveWebAssemblyRenderMode(prerender: false))`
directive — that is how interactivity is set under WASM (see Render mode).

## Architecture

### Quiz flow

```
/        Home.razor    → "Choose folder…" pick + FilterPanel + MixPanel +
                          "Shuffle order" checkbox + Start Quiz button
                          on Start: Controller.StartAsync(filters, mix) — which
                          binds the lifetime-stats context (promote + load) and,
                          for a non-blank mix, composes the quiz from lifetime
                          stats (or REFUSES when stats are unavailable — the
                          actionable notice with "Start without mix") — then
                          Nav→/quiz
                          (shuffle read live at Start; off the start gate;
                          disabled while a committed mix owns order)

/quiz    Quiz.razor    → per problem: answering → review → advance
                          "Show stats" button (both states, trailing ms-auto
                          slot of the action row) → Nav→/stats
                          answering (Controller.Review null):
                            routes by Controller.Current.Decision.IsCube:
                            checker → BackgammonPlayEntry
                                      + Submit / Skip / Undo last / Undo all
                            cube    → board-only BackgammonDiagram
                                      + BackgammonCubeActions radios /
                                        Submit / Skip (no Undo)
                          review (Controller.Review set, after Submit):
                            read-only BackgammonDiagram (DiagramMode.Solution,
                            user's answer marked, OnDiceClicked bound to the same
                            handler as Continue) + verdict line
                            + Continue / Redo
                          Redo (review only) → Controller.RedoAsync(), falls
                          back to answering on the same problem
                          IsFinished (on Continue / Skip) → Nav→/done

/stats   Stats.razor   → read-only, live ScorePanel + ScoreBreakdown against the
                          same in-progress Controller + Back to quiz (Nav→/quiz)
                          Reachable only from /quiz; redirects to / if no quiz
                          in progress, to /done if already finished.

/done    Done.razor    → ScorePanel (Total) + ScoreBreakdown (four-way)
                          + Restart with same filters / Back to setup

/help    Help.razor    → end-user documentation. Reachable from any state (the
                          host NavMenu's Help link is the only entry point) and
                          never redirects. Offers "Back to quiz" only while
                          HasStarted && !IsFinished.
```

### `QuizController` — per-app state machine

Scoped DI lifetime — but in the WASM client "scoped" resolves to **one
instance per loaded app (one browser tab)**, not per Blazor Server circuit.
The practical effect: quiz state **survives in-app navigation** between `/`,
`/quiz`, and `/done`, and is reset only by a **full browser reload** (which
tears down and re-boots the WASM runtime, constructing a fresh instance).
Reload-survival (persistence) is a deferred arc — see *next steps*.

The controller holds the active `IProblemSetSource` enumerator, the running
`QuizScore`, the per-problem `SubmittedPlay` (`History`) and
`SubmittedCubeAction` (`CubeHistory`) histories, and a `SkippedCount` for
non-scoring outcomes (off-list submissions, explicit Skip). The two
histories are kept separate (mirroring `_history`/`History`) because the two
scored-result types are distinct shapes — a unified history would force
consumers to type-test. Pages observe state transitions via the
`StateChanged` event: each gated async transition (below) fires it exactly
twice — busy-on, then busy-off with the end state in place — and the
synchronous mutators (Submit, Redo) fire it once.

**The transition gate.** The four async transitions — `StartAsync` /
`RestartAsync` / `ContinueAsync` / `SkipCurrentAsync` — share one busy gate:
a second gesture arriving while a transition is in flight **no-ops** (it does
not queue). The controller owns exactly one live enumerator, and an
overlapped `MoveNextAsync` — or a dispose during one — throws on a
thread-pool continuation no page can catch, terminating the WASM runtime
(the v1.0.4 double-Start crash). The per-method state guards can't close
that window: mid-advance they read *stale* state (`Review` already null,
`Current` still the outgoing problem), so Skip/Submit would stale-pass, and
a Continue suspended in the awaited stats fold still has `Review` set, so a
second Continue would double-fold. The gate lives in the controller — pages
never need the enumerator contract to be safe (the Quiz page's dice-click +
Continue double-binding stays as-is; the gate is what makes it safe). The
synchronous mutators (`SubmitPlay` / `SubmitCubeAction` / `RedoAsync`)
can't overlap an await themselves but can land *inside* one, so they no-op
on `IsBusy` too. Mechanics: `IsBusy` (observable; pages drive their busy
affordances from it) flips on inside the gate's check-and-set, `StateChanged`
fires, and the gate then **yields once, deliberately**, so the busy state
can render and paint before the transition's churn begins (the sources'
time-budgeted yields keep paints possible during the churn itself); a
`try`/`finally` releases the gate on completion *and* failure, firing
`StateChanged` again — the single completion signal (`AdvanceAsync` itself
no longer fires). Overlapped Start/Restart return `QuizStartOutcome.Busy`,
which callers treat as do-nothing (no navigation, no notice — the in-flight
transition owns the UI); overlapped Continue/Skip return silently. The
never-started `RestartAsync` throw is checked *inside* the gate — an
overlap is an outcome (Busy), not the caller bug the throw exists for.
`QuizControllerOverlapTests` pins all of it via `GatedProblemSetSource`
(externally-completable `MoveNextAsync`) and the fake sink's `RecordGate`
(freezes the fold window).

**Three-state per-problem flow.** Each problem moves through *answering*
→ *review* → *advance*, surfaced via `Current` and the nullable `Review`
property:

- **Submit** — `SubmitPlay(Play)` / `SubmitCubeAction(CubeDecisionPair)`
  are **synchronous** (the only `await` was the advance, now deferred).
  They score the answer, set `Review`, and fire `StateChanged` **without
  advancing** — `Current` still points at the answered problem. Both are
  no-ops outside the answering state (before start, after finish, or while
  a `Review` is already set — guarding against double-scoring).
- **`Review`** — a closed `ProblemReview` record (`Play` / `Cube`, see
  below) carrying exactly the marks the solution diagram needs. Non-null
  marks the review state.
- **`RedoAsync`** — the inverse of Submit: pops the just-added entry from
  `History` / `CubeHistory` (or decrements `SkippedCount` for an off-list
  play, which never added a history entry), recomputes `Score` by refolding
  both histories from `QuizScore.Empty`, and clears `Review` — returning to
  the *answering* state on the same `Current` problem. `Current`, the source
  enumerator, and `IsFinished` are untouched. No-op outside review.
- **`ContinueAsync`** — the only *forward* exit from review: folds the
  just-reviewed submission into the `IDecisionStatsSink` (the lifetime-stats
  fold point — see the folder/stats section and Pitfalls: on Continue, never
  at Submit), clears `Review`, and advances to the next problem. Exhausting
  the source here flips `IsFinished` — after the fold, so the final answer
  records. No-op outside review.
- **`SkipCurrentAsync`** — bypasses review and advances immediately, but
  only from the answering state (no-op while a `Review` is showing).

`ProblemReview` lives in `BgQuiz_Blazor.Client` (not BgGame_Lib): it is
per-app UI state, and adding it to the `SubmittedPlay` / `SubmittedCubeAction`
submodule would cross the boundary. `ProblemReview.Play` carries the
matched candidate index (`-1` off-list); `ProblemReview.Cube` carries the
two per-half equity losses. The Quiz page maps these onto the solution
request's `UserPlayIndex` / `UserDoubleError` + `UserTakeError` so the
diagram marks the *quiz user's* answer rather than the .xg-recorded
player's.

**Source construction is factory-injected.** The controller takes a
`ProblemSetSourceFactory` delegate (`(DecisionFilterSet, QuizMix) →
IProblemSetSource`) rather than constructing a source directly. The client's
`Program.cs` registers the delegate scoped as a lambda that reads the
`PickedProblemFolder` holder and builds a `CachedProblemSetSource` over
the picked folder (the parse-once layer — see its section), then reads the
`ShuffleOption` holder and conditionally wraps that source — `mix.IsPassthrough && shuffle.Enabled ? new
ShuffledProblemSetSource(inner) : inner` (the unseeded ctor: shuffling is
user-facing, reproducibility is a test-only concern). The mix parameter exists
for exactly that one rule — **shuffle arbitration**: an active mix owns
presentation order through its own `RandomOrder`, and a shuffled inner under
the composing decorator would silently break `RandomOrder: false`'s
source-order determinism. The factory never wires the composition layer
itself (that is the controller's — below). Both holders are read at
**invocation** time (`StartAsync`), not at DI registration, so a folder choice
*and* a shuffle-toggle choice made before Start take effect. Future
alternatives (deployed bundles, curated libraries) plug in by registering a
different factory; the controller is unchanged. Unit tests substitute a fake
source the same way.

**Mix ownership mirrors filter ownership, and a weighted start can be
refused.** `StartAsync(FilterConfig, QuizMix, bool ignoreMix = false)` takes
the committed mix beside the filter config — user config in at Start, stored
for Restart, no caller-set mutation — and returns a `QuizStartOutcome`. For a
non-blank *effective* mix (the stored mix, unless the per-run `ignoreMix`
override), `ResetAndAdvanceAsync` wires the producer's
`MixedProblemSetSource` around the factory source, holding the typed
reference so `LastComposition` telemetry surfaces without type-testing; the
stats provider resolves `IDecisionStatsSink.CurrentDocument` fresh per
enumeration, so **Restart recomposes against the lifetime record as it
stands, this session's folds included** (deliberate, producer-documented).
Composing without stats is banned (ratified: no stats → feature unavailable,
never silently unweighted; the producer's provider contract throws on null by
design), so the start is **refused** in two stages: stage 1, the
side-effect-free `IDecisionStatsSink.CanBindStats` capability peek (fallback
pick / denied / nothing picked — refuses before even the stats bind); stage 2,
after `BeginQuizAsync` (now ordered **before** the source build, because the
wrap decision needs the bound context), when the bind yielded no document
(unreadable file). Either refusal returns `MixRequiresStats` having touched
**no quiz state** — the prior quiz, its scores, and the stored config all
survive, and the only `StateChanged` firings are the transition gate's two
busy flips (delivering unchanged quiz state) — so Done's summary stands
behind a refused Restart. `RestartAsync(bool ignoreMix = false)` re-attempts the
stored mix every time, so the mix re-applies whenever stats allow; the
override is strictly per-run and the stored mix is never rewritten.

**Lifetime-stats sink is ctor-injected.** The controller's second dependency
is the `IDecisionStatsSink` (production: `QuizStatsStore`). It drives the sink
at exactly two points: `ResetAndAdvanceAsync` calls `BeginQuizAsync()` — the
one shared path under Start *and* Restart, so the stats context (document +
write handle) binds there and nowhere else — and `ContinueAsync` calls
`RecordAsync` with the just-reviewed submission. The sink never throws for
stats trouble, so quiz flow is independent of whether stats are recording.
Tests substitute a recording `FakeDecisionStatsSink`.

**Filter ownership.** `StartAsync` takes a `FilterConfig` (the wire DTO
emitted by `XgFilter_Razor.FilterPanel.OnFilterConfigChanged`), not a
runtime `DecisionFilterSet`. The controller calls `FilterConfig.Build()`
to produce its own filter pipeline, which it owns end-to-end — no shared
mutable state ever exists between the page and the controller. The
`ProblemSetSourceFactory` delegate still takes the runtime
`DecisionFilterSet` (the source's contract is the runtime pipeline; the
controller is the authority on assembling it), plus the run's effective
`QuizMix` for shuffle arbitration (see the factory paragraph above).

**Decision-type policy.** The user's `FilterConfig.DecisionType` choice
governs which decisions the quiz admits — checker plays, cube decisions, or
both. `FilterConfig.Build()` adds a `DecisionTypeFilter` only for a
non-`Both` choice; the controller adds none of its own. Both checker plays
(`SubmitPlay`) and cube decisions (`SubmitCubeAction`) flow when
the user's filter admits them.

**Cube scoring.** A cube position is two independent atomic decisions — the
doubler's offer and the taker's response. `SubmitCubeAction(CubeDecisionPair)`
always scores both halves (no off-list / skip path, unlike plays): per-half
equity loss via `DecisionData.DoublerActionError` / `TakerActionError` and
per-half correctness against `BestDoublerAction` / `BestTakerAction`,
folded into the score's `DoubleDecisions` and `TakeDecisions` segments via
`QuizScore.Plus(SubmittedCubeAction)`.

**Pass-position auto-skip.** Each `AdvanceAsync` step pulls the next
decision and tests it with `MoveGenerator.GeneratePlays(board, d1, d2)`.
The no-legal-play sentinel from BgMoveGen is `count == 1 && plays[0].Count == 0`
(a single Play of zero moves — dice forfeited), **not** an empty list. Pass
positions are silently skipped; they don't show to the user and don't
count toward `SkippedCount`.

**Off-list submission.** `SubmitPlay(Play)` matches the user's play
against `Current.Decision.Plays` by canonical `Play` equality (order- and
decomposition-insensitive, hit-sensitive) — so a candidate entered as
decomposed hops still matches its combined listing, but a play whose
intermediate hop hits stays off-list against a non-hitting candidate. An
in-list match contributes to the score: `EquityLoss == 0.0`
is the "best play" test (matching the established `PlayCandidate`
convention — multiple candidates may share zero loss). An off-list match
counts as a skip (`SkippedCount++`, no history entry, score unchanged).
There's no equity loss to record, and off-list submissions usually signal
an analysis omission rather than a user mistake. Either way a `Review`
(`ProblemReview.Play`, `OffList` true and index `-1` for off-list) is set
so the user still sees the best play on the solution diagram.

### `WasmUploadedProblemSetSource` — the in-browser source

Wraps `XgFilter_Lib.FilteredDecisionIterator.IterateXgStreamDiagrams`
(both `*.xg` match files and `*.xgp` position files). The constructor takes
`(IReadOnlyList<PickedFile> files, DecisionFilterSet filters, ILoggerFactory)`
and builds a single `FilteredDecisionIterator` held for the source's
lifetime; `ILoggerFactory` is preferred over `ILogger<…>` so the source's
contract doesn't leak the inner type. The files are parsed **entirely in the
browser** and never leave it.

**Re-iterability.** The source holds the file *bytes* (`PickedFile.Bytes`),
not open streams, and mints a fresh `MemoryStream` at position zero for every
`EnumerateAsync` call (wrapped in an `XgFileStream` carrying the
extension-bearing name). The stream iterator reads each stream exactly once,
forward, so buffering up front is what lets a Restart re-enumerate the same
set. `EnumerateAsync` also yields cooperatively so a long synchronous run
doesn't monopolise the single WASM thread — via BgGame_Lib's
`CooperativeYielder` (one per enumeration; time-budgeted, ~50 ms), not a
per-item `Task.Yield`: the per-item form paid an event-loop round-trip for
every decision, which dominated large parses, while the budgeted form yields
often enough for the browser to repaint (the busy cursor) and rarely enough
to cost nothing. The pacing clock is a ctor `TimeProvider` (production: the
DI system clock) — pure pacing, never affecting which decisions flow.

`Count` is null (an up-front count would require a full filtered pre-pass).
`Name` is `"No files"` / the single file's name / `"{N} files"`. Decision-type
admission is governed entirely by the supplied `filters`; the source injects
no policy of its own.

### `CachedProblemSetSource` — the parse-once cache

The production source the `Program.cs` factory builds (the stream source
above remains the parser under it): parse the picked files **once**, then
serve every Start/Restart by filtering the cached decisions in memory.
Measured against v1.0.4, every shuffled/weighted Start re-parsed the corpus
from the picked bytes (~7.5 s warm); with the cache only the first Start
after a pick parses — repeat Starts are milliseconds.

- **Cache home & lifecycle.** The cache slot is
  `PickedProblemFolder.ParsedDecisions` — on the holder, so cache lifecycle
  *is* pick lifecycle: `Set`/`Clear` null it (freeing the old parse — and,
  transitively, interest in the old bytes — immediately) and bump
  `PickGeneration`; there is no separate invalidation wiring to forget.
  `CachedProblemSetSource` is the slot's only writer, via
  `StoreParsed(generation, decisions)`, which **drops** a store whose pick
  has been superseded (the pick gesture is async, so a re-pick can complete
  inside a Start's own await points — a stale parse must never masquerade as
  the new pick's cache).
- **Unfiltered cache, per-Start filters.** The cached parse applies **no
  filters** so any filter config reuses it; each enumeration re-filters via
  `DecisionFilterSet.Matches`. That is exactly equivalent to filtering
  during the parse because the iterator's other hooks are contractually pure
  early-exit hints (`ShouldSkipMatch`/`ShouldSkipGame` may skip only when
  *no row inside can match*; `ShouldAdvanceGame`/`ShouldAdvanceMatch` only
  when *no further row can match*) — every row they cut fails `Matches`
  anyway. `CachedProblemSetSourceTests` pins the equivalence shape-level
  over the rotating corpus.
- **Staleness.** Files + generation are captured at construction (factory
  invocation = Start time, the read-live-at-Start discipline); the holder's
  cache is consulted only while the generation still matches, and the source
  keeps its own reference to whatever it parsed/adopted — so a Restart after
  a mid-quiz re-pick still replays *this quiz's* files without re-parsing
  and without polluting the new pick's cache.
- The stream sources stay **stream-pure** (the parse delegates to
  `WasmUploadedProblemSetSource` with an empty `DecisionFilterSet`); caching
  is entirely this app-side layer. Both the parse and the filter pass pace
  their cooperative yields with `CooperativeYielder`, so the busy cursor
  keeps painting through either. `Name` delegates to the inner naming rule;
  `Count` stays null.

### Folder picking & lifetime stats

One "pick a folder" gesture on `Home`, served by whichever mechanism the
browser offers — probed **at pick time**, per gesture:

- **File System Access** (`showDirectoryPicker`, Chromium): native directory
  picker, then a `requestPermission({mode:'readwrite'})` on the picked handle.
  Granted ⇒ `StatsSaveCapability.Enabled` — lifetime stats save into the
  folder; declined ⇒ `PermissionDenied` — quiz runs read-only.
- **`webkitdirectory` fallback** (everywhere else): a hidden
  `<input type="file" webkitdirectory>` opened by the same button. Read-only
  by construction ⇒ `BrowserUnsupported` — quiz runs without stats.

Either way the folder's **top-level** `.xg` / `.xgp` files (subfolders
ignored; case-insensitive extension filter) are buffered into `PickedFile`s
and the pick lands in `PickedProblemFolder`. The degrade ladder is total: no
capability rung ever blocks the quiz — no-stats mode is fully functional.

**`IFolderAccess` / `JsFolderAccess` / `folderAccess.js`.** The app's one
gateway to the browser's folder facilities. `folderAccess.js` (the first
app-authored JS, an ES module under the client's `wwwroot/js/`) owns the
browser-side state; `JsFolderAccess` is the single C# type holding an
`IJSObjectReference` (lazy, cached import); everything above it — pages, the
stats store — depends on the `IFolderAccess` interface. Directory handles
**never cross the interop boundary**: C# sees names, sizes, bytes, and
booleans. Error signaling is by kind: expected outcomes are result values (a
cancelled picker ⇒ `FolderPickOutcome.Cancelled`, a denied write ⇒ the
capability enum, a missing stats file ⇒ `null` read); only unexpected browser
failures throw (`JSException`), which callers catch and degrade on. Byte
transfer is `IJSStreamReference` per file; `JsFolderAccess` enforces the
`PickedFileLimits` caps against the enumerated *metadata* before any bytes
move, and re-asserts the byte cap as `OpenReadStreamAsync(maxAllowedSize:)`.
The fallback collection also happens JS-side because the top-level-only
filter needs `webkitRelativePath`, which Blazor's `InputFile` never exposes —
one reason the picker is a plain `<input>`, not `InputFile`.

**Two-slot model — the mid-quiz-Clear ruling.** The JS module keeps a
*picked* slot (latest pick: handle + name→handle/File map) and an *active*
slot (the running quiz's stats handle). The stats context (document + write
handle) **binds at Start/Restart, never at pick**: the controller's
`ResetAndAdvanceAsync` drives `QuizStatsStore.BeginQuizAsync()`, which
promotes picked → active (`promoteToActive`) and loads the stats file through
the active handle. Home's Clear resets **only the picked slot**
(`clearPicked`), so a mid-quiz Clear or re-pick never affects the running
quiz's recording — recording changes only when the next Start re-binds.

**`QuizStatsFile`** — the persistence SSOT: `FileName`
(`bgquiz-stats.json`) and the one fixed `JsonSerializerOptions`
(`WriteIndented = true` — whitespace is the only options-controlled aspect;
the bundled converter pins names and ordering). The filename is passed *into*
JS per call and rendered by `Help` from the constant — neither restates it.

**`QuizStatsStore`** (scoped; aliased as `IDecisionStatsSink` so the
controller's sink and the pages' status notices observe one instance; deps:
`IFolderAccess`, `TimeProvider`, `PickedProblemFolder`) owns the
`DecisionStatsDocument` lifecycle:

- `BeginQuizAsync` (every Start/Restart) re-derives the whole context and
  resets any prior failure state: capability ≠ `Enabled` or no promoted
  handle ⇒ `Disabled`; `null` read ⇒ `Ready` over `Empty` (fresh corpus);
  `JsonException` / read `JSException` ⇒ **`LoadFailed`** — this quiz records
  nothing and the file is **never written** (the user's data stays untouched;
  recovery is user-side, no overwrite offer).
- `RecordAsync` (from `ContinueAsync`, only while `Ready`): fold via
  `doc.Plus(submission, clock)` then **write back immediately** — per-fold
  write-back is the crash-safety choice (small file; a lost tab loses no
  answered problem). A write `JSException` keeps the folded document in
  memory, flips `WriteFailed`, raises `StatusChanged`, and stops writing (no
  per-answer error spam). The store **never throws** — Continue cannot fault
  on stats trouble.
- The clock is the DI `TimeProvider` (registered `TimeProvider.System` in
  `Program.cs`), handed to the document's `Plus` — ambient time is never read.

**Status surfacing** splits by context. Pick-time (Home, capability-based,
all polite `role="status"` outcomes): stats-will-be-saved (`Enabled`, naming
`QuizStatsFile.FileName`) / browser-can't-save (`BrowserUnsupported`) /
declined-write (`PermissionDenied`) — plus the empty-folder outcome and the
`role="alert"` pick-failure banner. Quiz-context (Quiz **and** Done — a
failure on the final Continue lands on Done without ever showing Quiz's
notice): `LoadFailed` as a polite status, `WriteFailed` as an assertive
alert. Quiz-context notices scope to the active context and reset at the next
Start's re-bind.

### `PickedProblemFolder` — the picked-folder holder

The per-app (`Scoped`, one-per-tab in WASM) holder for the picked folder:
`Files` (buffered `PickedFile`s), `FolderName`, and the pick-time
`StatsSaveCapability`. `Home.razor` writes it (`Set` / `Clear`); the
`ProblemSetSourceFactory` reads it to build a
`CachedProblemSetSource`; `QuizStatsStore` reads `Capability` at its
Start-time bind. Files are buffered byte arrays (read out of the browser once
at pick time) so the source can re-enumerate on Restart. Carrying the
capability here (not in a component field) keeps Home's stats status notice
alive across navigate-back — the same holder-vs-field rationale as the start
gate. The holder also carries the **parse-once cache seam** —
`ParsedDecisions` / `PickGeneration` / `StoreParsed` — so that invalidation
is intrinsic to `Set`/`Clear`; see the `CachedProblemSetSource` section for
the contract.

- **`Summary`** (`string?`) — the holder-owned label:
  `"'{FolderName}' — {N} problem file(s)"`, `null` when nothing is picked.
  The **single source of truth** for how a pick describes itself; `Home`
  renders it directly rather than caching text in a component field (the old
  field desynced on navigate-back). `PickedProblemFolderTests` pins the
  branches; the bUnit `Home_PrePopulatedHolder_RendersSummaryAndEnablesStart`
  pins the navigate-back render.

The pick is **in-memory only**: it survives in-app navigation but is reset by
a full browser reload (the WASM runtime re-boots). Reload-survival is a
deferred arc, matching `QuizController` — but note the stats *file* is not
lost with it: it lives in the user's folder, and re-picking the folder
resumes it.

### `PickedFileLimits` — the pick caps, single-sourced

`internal static class PickedFileLimits` (Quiz/) holds the two caps the
folder pick applies — `MaxFileBytes` (50 MB per file) and `MaxFileCount`
(500 per pick) — plus `MaxFileMegabytes`, **derived** from `MaxFileBytes`.

The caps have two consumers: `JsFolderAccess` *enforces* them (against the
enumerated metadata before any bytes cross the boundary; the byte cap is also
re-asserted as the `IJSStreamReference.OpenReadStreamAsync` max on the actual
transfer), `Help` *documents* them. Leaving them as private constants on the
enforcing type would have forced the help page to restate "50 MB" / "500" as
prose, so raising a cap would silently make the documentation wrong. Deriving
the megabyte figure (rather than writing `50` twice) is what makes the SSOT
actually hold.
`PageTests.Help_StatesFileCaps_SourcedFromTheConstantsThePickEnforces` asserts
the rendered prose against the constants, not against the literals, so page and
rule cannot drift (and `Help_NamesTheStatsFile_FromTheConstantTheStoreWrites`
extends the same discipline to `QuizStatsFile.FileName`). The constants stay
`internal`; the `.Client` csproj grants `InternalsVisibleTo` to the test
project rather than widening them to public.

### `AppliedFilter` — the filter half of the start gate

The per-app (`Scoped`, one-per-tab in WASM) holder for the `FilterConfig` the
user has **deliberately applied** on `Home` — the sibling of `PickedProblemFolder`
for the filter half of the start gate. `Home.razor` writes it: `Set(config)`
when the panel raises `OnFilterConfigChanged` (Apply / Reset), `Clear()` when
it raises `OnFilterDirty` (any control edit). `IsApplied` (= `Config is not
null`) and `Config` are read only by `Home` (`CanStart`, `StartQuizAsync`).

Holding the applied state here rather than in a transient component field is
what lets the gate survive in-app navigation: on navigate-back `Home` is
re-instantiated, but it re-derives `CanStart` from the two persisted holders
instead of resetting to "not applied" and forcing a needless re-click of
Apply (the filter-side twin of the picked-summary nit).

**Gate semantics — applied, not merely present.** `IsApplied` means the user
took the Apply action, so a half-edited set must clear it (`OnFilterDirty →
Clear`). The interaction with `FilterPanel`'s localStorage restore is safe by
construction: restore writes the panel's own fields directly and raises
**neither** callback, so it can't spuriously mark applied or clear an existing
applied state — the holder is the sole authority on "applied". `AppliedFilterTests`
pins the holder; the bUnit `Home_PreAppliedFilterHolder_EnablesStartWithoutReApply`
and `Home_FiltersDirty_ClearsAppliedState_DisablesStart` pin the gate.

In-memory only, reset on full reload — same deferred-arc caveat as its sibling
holders (`PickedProblemFolder`, `ShuffleOption`).

### `MixPanel` / `AppliedMix` — the stats-weighted mix

**`MixPanel`** (Components/Pages) is the FilterPanel of quiz composition: an
ordered list of (category, percent) rows — category picker over the seven
`QuizCategoryKind`s, a parameter input where the kind takes one (defaults
seeded on selection: 3 times / 30 days / 0.05 equity / 25%), percent 1–100
summing to exactly 100 — plus the Random-order toggle (default on) and an
optional quiz length (disabled with a hint at zero rows;
length-without-entries is invalid by producer rule, and "cap without
weighting" is one Everything-else row at 100 plus a length). Row order is
**semantic** (earlier rows win contested overlap — producer contract), so
rows carry explicit ↑/↓ reorder buttons and both commit and restore preserve
order exactly. The wrong-rate row *displays* percent and *stores* the
producer's fraction — thresholds are fractions; rendering is a display
concern. Validation disables Apply with an inline reason; category
construction goes through the producer's validating factories with a
try/catch backstop. A blank builder is always valid and commits
`QuizMix.Empty` — the inert passthrough default.

**Commit model mirrors FilterPanel** — `OnMixApplied` on Apply/Reset only
(Reset is an explicit apply of Empty, the one sanctioned overwrite of the
stored mix), `OnMixDirty` per edit — with one deliberate divergence: the
first-render localStorage restore raises **`OnMixRestored`**, and Home
*adopts* it into the holder. A persisted mix is by construction a
previously-applied one, so holder and rendered rows agree without a re-Apply
(the filter panel's restore deliberately raises nothing because its
"applied" means a gesture in *this* visit). Both wiring-critical callbacks
are `[EditorRequired]`. Persistence is the lib trio over one key,
**`xg_quizMix`**: `ToJson` on Apply, `TryFromJson` on restore —
absent/corrupt yields a blank builder, never an error; the component never
touches a JSON serializer.

**`AppliedMix`** (Quiz/) is the committed-mix holder beside `AppliedFilter`:
`Current` (default `QuizMix.Empty`) + `IsDirty`. Blank is the valid default,
so there is no "never applied blocks Start" state — only dirtiness gates
(`CanStart` requires `!AppliedMix.IsDirty`), preventing Start from running a
mix that differs from what the panel shows. Scoped for navigate-back
survival like its siblings; unlike them the underlying choice also survives
a reload (localStorage) and re-adopts on the next boot.

**`MixDisplay`** (Quiz/) is the wording SSOT: kind labels (the panel's
picker), full category labels (the Quiz page's shortfall notice), and the
refusal reason (Home's Start and Done's Restart render the same
capability/status rule — neither page hand-words it).

**Honest notices, all four.** (1) *Signal early*: Home shows a polite
advisory the moment a stats-less pick coexists with a committed non-blank
mix — before Start. (2) *Gate late*: a refused weighted Start/Restart
renders an actionable `role="alert"` with the reason and the one-click
per-run override ("Start without mix" / "Restart without mix"); the stored
mix is kept either way, and the notice says so. (3) *Composed-to-zero*:
Home's empty-result branch keys on `LastComposition is { DrawnCount: 0 }`
for mix-aware wording, parallel to filtered-to-zero. (4) *Shortfall*: Quiz
renders requested-vs-drawn from `Controller.LastComposition`
(`role="alert"` — the quiz underway differs from what was asked), with a
per-entry drew-N-of-M line for every category that ran short, covering both
the missed-target and redistributed-share shapes.

### `ShuffleOption` — the "Shuffle order" toggle holder

The per-app (`Scoped`, one-per-tab in WASM) holder for the **"Shuffle order"**
checkbox on `Home` — a sibling of `PickedProblemFolder` and `AppliedFilter`, same
lifetime, so the toggle survives in-app navigation (`Home` is re-instantiated
on navigate-back and re-reads `Enabled`). Surface is minimal: `bool Enabled`
(private setter) + `Set(bool)`. `Home.razor` writes it on the checkbox's
`@onchange`; the `ProblemSetSourceFactory` reads `Enabled` to decide whether to
wrap the picked set in a `ShuffledProblemSetSource` — at **invocation** time
(`StartAsync`), the same read-live-at-Start discipline as `PickedProblemFolder`.

**Presentation-only, and off the start gate.** Shuffling changes only the
*order* decisions are presented in, never which decisions are *admitted*, so it
is deliberately **not** folded into `FilterConfig` and plays **no part in
`CanStart`** (`AppliedFilter.IsApplied && Folder.HasFiles &&
!AppliedMix.IsDirty`). Toggling it
does **not** dirty the start gate — `HandleShuffleToggled` only records the
choice; there is no applied/dirty machinery the way `AppliedFilter` needs it,
because a checkbox has no half-edited intermediate state. Every toggle is a
complete, immediately valid choice read live at Start; there is nothing to
"apply".

**Disabled — never rewritten — under an active mix.** While the committed
mix has entries, presentation order belongs to the mix's own Random-order
setting, so Home disables the checkbox with a hint and the factory suppresses
the shuffle wrap; `Enabled` keeps the user's value untouched throughout, so
clearing the mix restores their prior shuffle preference (pinned in
`PageTests`).

In-memory only, reset on full reload — same deferred-arc caveat as the other
holders.

### `QuizLiveMarker` — the reload-reset honesty marker

The per-app (`Scoped`, one-per-tab in WASM) service recording that a quiz is
**live** in this tab, backed by the browser's `sessionStorage` through
`IJSRuntime`. BgQuiz's first JS-interop *service* (the clipboard call in
`XgidLabel` and the filter panel's `localStorage` are inline in their
components); it's encapsulated because it has a lifecycle spread across two pages
and a storage constraint worth stating once.

This is the **honesty slice of reload-resume, not resume itself**. A full reload
reboots the WASM runtime and silently discards all quiz state; the user lands on
a fresh `Home` with no hint their quiz vanished. The marker is the one thing that
survives a reload, so a fresh boot that finds it can say so. It *explains* the
loss; it does not prevent it (real resume — persisting the picked bytes and
progress — remains the deferred IndexedDB arc).

Surface: `MarkLiveAsync()` / `WasLiveAsync()` / `ClearAsync()`. Lifecycle:

- **Set wherever a quiz becomes live** (`MarkLiveAsync`): `Home` on a successful
  Start — *after* the empty-result guard, so the no-match path (which stays on `/`
  with no live quiz) never marks — **and** `Done` on *Restart*, which makes a quiz
  live again from the same pipeline. Both writers matter: without the Restart one,
  a reload during a restarted quiz falls back to the old silent reset, a
  one-click-wide hole in the very guarantee this marker exists to make.
- **`Home` reads** it on boot (`OnInitializedAsync`): `WasLiveAsync() &&
  !Controller.HasStarted` ⇒ show the polite reset notice, then `ClearAsync()` so
  it shows once. The `HasStarted` guard is the discriminator — a set marker with
  a *live* controller is in-app navigation back to `Home` mid-quiz (same per-tab
  controller, quiz survived), **not** a reload, so no notice fires and the marker
  is left in place for a genuine later reload.
- **`Done` clears** it (`ClearAsync`) on honest completion — reaching Done means
  the quiz ended as intended, so there's no reset to announce on a later boot. (A
  reload that killed a live quiz never reaches Done, which requires the surviving
  in-memory controller.) *Restart* re-sets it immediately after (above), so the
  clear-then-re-set order across a Done→Restart round trip is deliberate.

`PageTests` pins all of it: `Home_BootWithLiveMarker_…` (fails without the boot
check), `Home_BootWithoutMarker_…` and `Home_MarkerPresentButQuizLive_…`
(over-trigger guards), `Home_StartClick_MarksQuizLive` /
`…_EmptyResult_DoesNotMarkQuizLive`, and `Done_ReachingDone_ClearsLiveQuizMarker`.

**Storage is `sessionStorage`, deliberately — not `localStorage`** (see
Pitfalls). Reset on full reload otherwise (the marker's whole job is to be the
*exception* that survives one boot, then get cleared).

### Pages

- **`Home.razor`** — a **"Choose folder…"** button (`#pickProblemFolder`)
  above the `FilterPanel` from XgFilter_Razor, plus a hidden, always-rendered
  `<input type="file" webkitdirectory>` fallback the same button opens where
  File System Access is absent (a plain `<input>`, not `InputFile` — the JS
  module reads the FileList itself for `webkitRelativePath`; always in the DOM
  so the e2e suite can drive it directly). The whole pick — mechanism probe,
  permission, top-level enumeration, caps from `PickedFileLimits`, buffering —
  runs behind `IFolderAccess`; the page never touches raw interop. The pick
  lands in the per-app `PickedProblemFolder` (extension-bearing names +
  bytes + pick-time `StatsSaveCapability`); the bytes are parsed in-browser
  and never uploaded. A cancelled picker changes nothing, silently; an empty
  folder shows a polite outcome notice and leaves the holder clear; the
  capability drives the pick-time stats status notice (see Folder picking &
  lifetime stats). The pick label renders straight from
  `PickedProblemFolder.Summary` (the SSOT — not a transient field), with a
  **Clear** affordance beside it (`Folder.Clear()` +
  `FolderAccess.ClearPickedAsync()`); the summary then disappears and the
  folder half of the gate re-disables Start by construction. Clearing is safe
  mid-quiz and is left unguarded on purpose — the picked files are read only
  at Start time (the source factory reads `Files` in `StartAsync`) and the
  clear touches only the JS *picked* slot, so a running quiz keeps both its
  enumerator and its bound stats context
  (`PageTests.Home_ClearPickedFolder_RemovesSummaryDisablesStartClearsPickedSlotOnly`).
  Start is gated on **two** conditions, both read from per-app scoped holders
  so the gate survives navigation: filters Applied at least once *and* a
  folder picked with at least one problem file
  (`CanStart => AppliedFilter.IsApplied && Folder.HasFiles`).
  Below the filter panel sits the **`MixPanel`** (its three callbacks land in
  `AppliedMix`: Apply/restore → `Apply`, dirty → `MarkDirty`; see the
  `MixPanel`/`AppliedMix` section), then a **"Shuffle order" checkbox** bound
  to the `ShuffleOption` holder (`HandleShuffleToggled` → `ShuffleOption.Set`).
  It is presentation-only and **not** part of `CanStart` — toggling it never
  gates or dirties Start; the source factory reads it live at Start to decide
  whether to wrap the picked set in a `ShuffledProblemSetSource` (see the
  `ShuffleOption` section), and it renders disabled (value untouched) while
  the committed mix owns order. Start hands `AppliedMix.Current` to
  `Controller.StartAsync` beside the filter config and checks the returned
  outcome **before** the empty-result `IsFinished` check (a refused start
  leaves prior state, including a stale `IsFinished`): `MixRequiresStats`
  renders the actionable refusal alert (`_mixRefused`, reason via
  `MixDisplay.RefusalReason`, the "Start without mix" per-run override, a
  pointer to the panel's Reset), the mix-aware composed-to-zero wording rides
  the existing no-match branch, and the early won't-apply advisory renders
  from pick capability × committed mix with no Start needed.
  **Busy affordances:** the whole setup surface (pick controls, both panels,
  shuffle, Start, the refusal override) sits inside one
  `<fieldset disabled="@Controller.IsBusy">` — the native element disables
  every form control within, including the Apply buttons *inside* the
  imported `FilterPanel`/`MixPanel`, which expose no disabled parameter —
  and the page container carries `app-busy` (the `cursor: progress` rule in
  `app.css`) while `Controller.IsBusy`. The controller's gate yield is what
  lets that state paint before the Start churn; Home needs no `StateChanged`
  subscription because its own suspended handler triggers the re-renders.
  Subscribes to `OnFilterConfigChanged` → `AppliedFilter.Set` (the panel's
  emit-event after Apply) and `OnFilterDirty` → `AppliedFilter.Clear`; on
  Start hands `AppliedFilter.Config` to `Controller.StartAsync`. Catches pick
  failures (unexpected `JSException`, caps exceeded — the `_pickError` banner)
  and start-time exceptions (`FilterConfig.Build()` validation failure, source
  construction failure — the `_startError` banner) and surfaces them instead
  of faulting the WASM app. A *successful* Start that leaves the controller
  already `IsFinished` (the source admitted no showable problem) is caught the
  same way — the page stays on `/` and shows a neutral `role="status"` no-match
  banner rather than navigating into a `0/0` `/quiz` → `/done` bounce. This is a
  post-Start check, not a pre-flight enumeration: `StartAsync` already advances
  to the first showable problem, so `IsFinished` immediately after it *is* the
  empty-result signal — no second pass over the buffered files. Two
  indistinguishable causes flip it (zero filter matches; every match auto-skipped
  as a pass position), so the wording claims neither. The notice is a sibling
  component field to the error banner (`_noMatchNotice` vs `_startError`) —
  distinct because it reports an *outcome*, not a *failure*: `alert-warning` +
  polite `role="status"`, not the error's `alert-danger` + assertive
  `role="alert"`. Both are genuinely per-visit state, so component fields (see
  Pitfalls). `PageTests.Home_StartClick_EmptyFilterResult_ShowsBannerAndStaysHome`
  pins the zero-match path, `…_AllMatchesAutoSkippedPasses_ShowsSameBanner` the
  pass-only path, and `…_NonEmptyResult_NavigatesToQuizWithoutBanner` guards
  against over-triggering. A **third** per-visit notice (`_showReloadNotice`,
  polite `role="status"` like the no-match one) fires on a boot that finds the
  `QuizLiveMarker` set with no live controller — a full reload reset a quiz that
  was underway. `Home` sets the marker on Start (past the empty-result guard) and
  clears it when it shows the notice; see the `QuizLiveMarker` section for the
  full lifecycle and the `HasStarted` discriminator.
- **`Quiz.razor`** — mirrors the controller's three-state flow, branching on
  `Controller.Review`. In the **answering** state (`Review` null) it routes the
  board region by `Current.Decision.IsCube` over
  `DiagramRequest.FromDecisionData(Current, DiagramMode.Problem)`: checker
  decisions to `BackgammonPlayEntry` (click-driven play assembly); cube decisions
  to a **board-only** `BackgammonDiagram` — the same read-only shape as the review
  branch, because the cube answer is not entered on the board. `BackgammonPlayEntry`
  is strict — it throws `NotImplementedException` on a cube decision — so the
  checker route must be exact; the cube route renders a plain view carrying no such
  guard. Submit (a synchronous handler, since the controller's Submit no longer
  awaits) is gated on the relevant answer being held: a play via `OnPlayCompleted`
  → `_completedPlay`; a cube via the `BackgammonCubeActions` radios in the action
  row, whose `@bind-Value` keeps `_completedCube` current. Both fields reset on
  every transition. The radios re-fire on every selection change, so
  `_completedCube` always holds the latest pair and the user can revise before
  Submit. The action row varies by kind: cube places the `BackgammonCubeActions`
  radios ahead of Submit / Skip and has no Undo (no partial-move state); checker
  keeps Undo last / Undo all (clearing the latched play, since the component does
  not notify on undo). Both trail with a "Show stats" button in the row's
  `ms-auto` slot. In the **review** state
  (`Review` set, after Submit) it renders a read-only `BackgammonDiagram`
  in `DiagramMode.Solution` — the filled analysis panel, the same view the PPTX
  exporter renders — plus Continue / Redo / Show stats (Show stats again the
  row's trailing `ms-auto` slot). Between the score panel and either action row
  sits an always-rendered, **fixed-height status strip** (`.status-strip`,
  `app.css`): a one-line legend slot and a two-line-clamped verdict band. While
  answering it shows a neutral state-appropriate prompt (legend empty); at
  review the legend (`* played · † your answer`) and the outcome-coloured
  verdict fill in. Because the strip's height is a designed constant, chrome
  height — and therefore the board's flex remainder under the desktop fold
  cap — is identical across the answering and review states and across
  questions, so the board does not change size when Submit flips into review.
  The board itself is bounded through BgDiag_Razor's bounded-height contract:
  the desktop fold column hands `.board-container`'s definite post-flex height
  to the `BackgammonPlayEntry` wrapper (`height: 100%`), whose internal
  `bg-board-slot` + `.bg-diagram` contain-fit default letterbox the board. The
  cube-answering and review states render a bare `.bg-diagram` as a direct child
  of `.board-container`, so the contain-fit default engages against the region's
  definite height with no wrapper glue at all. The
  solution request is built with `DiagramRequest.Builder.From(Current.Position,
  Current.Decision, Current.Descriptive, DiagramMode.Solution)`, then the user's
  marks are overridden from `Review`: `UserPlayIndex` for a play (`-1` off-list
  draws no marker), or `UserDoubleError` / `UserTakeError` for a cube.
  `FromDecisionData` is **not** used here because it defaults those marks from
  the .xg-recorded player, not the quiz user. **Busy affordances:** every
  transition-driving button (Submit, Skip, Undo, Continue, Redo) also
  disables on `Controller.IsBusy` and the container carries `app-busy` — the
  honest mirror of the controller's transition gate, which would no-op the
  clicks anyway; "Show stats" stays enabled (navigation only). The page's
  existing `StateChanged` subscription re-renders the busy flips. The review diagram's
  `OnDiceClicked` is bound to the same `ContinueAsync` handler as the Continue
  button, so clicking the dice hit-region advances past the solution exactly
  like Continue. Redo calls `Controller.RedoAsync()`, falling back to the
  answering branch on the same problem; no explicit reset or `@key` is needed
  to give the returning entry component a clean slate — Submit already
  unmounted it when the page swapped into the review branch, so Blazor
  constructs a genuinely new instance on the way back regardless of whether
  the incoming request describes the same problem (which is exactly what
  Redo returns to). "Show stats" navigates to `/stats`. Subscribes to
  `Controller.StateChanged` **and** `QuizStatsStore.StatusChanged` in
  `OnInitialized`, unsubscribes from both in `IDisposable.Dispose`; redirects
  to `/done` when `IsFinished` flips. Above the board it renders the
  active-context stats notices — `LoadFailed` as a polite `role="status"`
  (quiz runs, file untouched), `WriteFailed` as an assertive `role="alert"`
  (quiz continues, writes stopped); the store-status subscription is what
  surfaces a mid-quiz write failure the moment it happens. The mix-shortfall
  notice renders in the same block from `Controller.LastComposition` (see the
  `MixPanel`/`AppliedMix` section's honest-notices list).
- **`Stats.razor`** — read-only mid-quiz stats view: the same `ScorePanel` /
  `ScoreBreakdown` pair `Done` shows at the end, rendered here against the
  live, in-progress `QuizController` (`Heading="Progress so far"` /
  `"Detailed evaluation so far"` — honest mid-quiz wording, not `Done`'s
  `"Final"`). Reachable only from `Quiz`'s "Show stats" button; a "Back to
  quiz" button returns to `/quiz`. Never calls Submit / Continue / Skip, so
  the round trip leaves `Controller.Current` / `Controller.Review` untouched —
  combined with the controller's per-tab scoped lifetime, this is what gives
  "resume where you left off" for free, with no state to persist or restore.
  Direct nav with no quiz in progress bounces to `/`; with the quiz already
  finished, to `/done` — the same guards `Quiz` applies to itself.
- **`Help.razor`** — end-user documentation: the six beats of the flow (pick
  folder → filters → answering → scoring → review → stats/done), a **Making a
  checker play** section sitting inside the answering beat, a **Lifetime
  stats** section (what's saved and where, the Chromium requirement, only
  answered problems count, a cube records two decisions there too, an unreadable
  file is left alone, and — extending the privacy stance — the stats file
  never leaves the machine), and then the semantics a user cannot discover by
  clicking around — pass positions are auto-skipped and never shown, an
  off-list play counts as a skip rather than a wrong answer, a cube position
  scores as two decisions in-quiz, clicking the dice on the solution diagram
  advances like Continue, and a full browser reload resets everything (in-app
  navigation does not — and the stats file survives reload in the user's own
  folder). The checker-play section documents
  the one-click entry model the board actually ships, and is organized **by
  click target** — mirroring the component's own dispatch, so each bullet is
  exhaustive about one thing the user can click: a point you occupy
  (source-advance by one die, leftmost die preferred, bearing off when the move
  carries the checker off), a point that is empty or holds an opposing blot
  (make-the-point, else land one checker, else — on two equally short ways — a
  silent no-op; it also enters from the bar, the entry taken first and the rest
  of the roll free to move any checker), the bar (enter; the only route onto a
  point you already occupy while on the bar), and the tray (bear-off-max, a
  no-op when two ways bear off equally many). The dice click (swap the displayed
  order while incomplete, submit once complete) and Undo last / Undo all follow.
  Its source of truth is BgDiag_Razor's
  `BackgammonPlayEntry` + BgMoveGen's `MoveEntryState` (whose legality rests in
  turn on `MoveGenerator.NextMove`, where bar-first is enforced), whose doc
  comments are the contract this prose restates; it deliberately says nothing
  about legal-click highlighting, which `MoveEntryState.LegalNextClicks`
  supports but no shipped BgQuiz surface renders. Text-first; the illustrative board
  diagram is a deferred nicety. Lives in the `.Client` (not as a static host
  page) so a mid-quiz Help → Back round trip doesn't disturb the WASM runtime
  holding quiz state — the same reason `Stats` does. Unlike `Stats` it **never
  redirects**: help is reachable from any state, including a cold visit or a
  bookmark. Only the "Back to quiz" button is conditional, on the exact
  predicate `Stats` guards with (`HasStarted && !IsFinished`). It does not
  subscribe to `StateChanged` — nothing changes while the user reads. The file
  caps render from `PickedFileLimits` and the stats filename from
  `QuizStatsFile.FileName`, never as literals. The host's
  `NavMenu` Help link is the **only** entry point; `Quiz`'s action row
  deliberately gets no "?" button, because its fixed height is load-bearing for
  board sizing.
- **`Done.razor`** — final `ScorePanel` (Total) + `ScoreBreakdown`
  (four-way) + total problems shown + **Restart with same filters** /
  **Back to setup**. "Problems shown" is `PlayDecisions.Submitted +
  DoubleDecisions.Submitted + SkippedCount` — **not** `Total.Submitted`,
  which counts decisions and so double-counts each cube position (one
  Double + one Take). The second button is **navigation only** — it navigates to
  `Home`, and the start-gate holders persist across that, so `Home` arrives armed
  with the same picks and filters. Its label describes that navigation ("Back to
  setup") rather than promising a reset it doesn't perform; the former "Start over
  (new filters)" lied (nothing resets — Restart and Back-to-setup differ only in
  *where they land*, not in what they clear). Done also participates in the
  `QuizLiveMarker` lifecycle both ways: reaching it clears the marker
  (`OnInitializedAsync` — honest completion has no reload-reset to announce), and
  *Restart* re-sets it (a restarted quiz is live again, so a reload during it is
  acknowledged like one during a fresh Start). Done also mirrors Quiz's
  active-context stats notices (`LoadFailed` status / `WriteFailed` alert) —
  a failure on the *final* Continue lands the user here without ever seeing
  the in-quiz notice. No subscription needed: the status cannot change while
  Done is shown (no folds happen here; Restart navigates away and re-binds).
  Restart re-attempts the stored mix and handles the refusal like Home's
  Start: `MixRequiresStats` renders the refusal alert with **"Restart without
  mix"**, the summary underneath survives by the refusal's touches-no-state
  guarantee, and the `QuizLiveMarker` stays cleared (nothing became live).
  Both Restart buttons disable on `Controller.IsBusy` and the container
  carries `app-busy` while a Restart transition runs ("Back to setup" stays
  enabled — navigation only); no subscription needed, the suspended Restart
  handler's own re-renders cover the flips.
- **`ScorePanel.razor`** — compact status strip used by both Quiz and Done.
  Renders the `Total` segment: Submitted / Correct (with %) / Skipped /
  average equity loss; optional Source name and Heading. Kept Total-only to
  avoid mid-quiz clutter.
- **`ScoreBreakdown.razor`** — the four-way detailed evaluation, hosted on
  Done. A Play / Double / Take / Total table (Submitted · Correct (%) · Avg
  loss per row), reading the three `QuizScore` segments and the derived
  `Total`. Kept separate from `ScorePanel` rather than a `Detailed` flag so
  each component owns one layout.

### Render mode

`InteractiveWebAssembly` — the whole quiz runs in the browser-wasm runtime
(no server-side state, no SignalR circuit). The host (`BgQuiz_Blazor`) is a
thin shell: `Program.cs` calls `AddInteractiveWebAssemblyComponents()` and
`MapRazorComponents<App>().AddInteractiveWebAssemblyRenderMode()`, registering
the `.Client` `_Imports` assembly as an additional routable-component source.
It references **only** the `.Client` — the entire backgammon-library closure
ships into the WASM payload, not the server.

The host pipeline also carries `UseStatusCodePagesWithReExecute("/not-found")`.
It sits **before** `UseAntiforgery()`: the re-execute replays the pipeline from
that point downstream, and a Razor Component endpoint throws unless the
antiforgery middleware ran on the request that reaches it. `NotFoundPipelineTests`
exercises this through the real pipeline with `WebApplicationFactory` — bUnit
renders components in isolation and is structurally blind to middleware and
endpoint routing, so no component test can cover it.

Each routable page (`Home`, `Quiz`, `Stats`, `Done`, `Help`) carries its own
`@rendermode @(new InteractiveWebAssemblyRenderMode(prerender: false))`
directive — that page-level directive is how interactivity is set in this
model (there is no global `<Routes @rendermode>` setting). `prerender: false`
skips the static-prerender pass: the picked-file holder and quiz state live in
WASM-runtime memory that doesn't exist during a server prerender, so prerender
would render an empty first frame and double-run `OnInitialized`. A full
browser reload re-boots the runtime and resets all state (persistence is a
deferred arc).

That choice propagates to `<head>`. Because no routable page renders in the
static pass, `App.razor` carries **both** halves of the title contract:

- a static `<title>BgQuiz</title>` — the pre-boot and no-JS/crawler title, shown
  for as long as the ~19.5 MB WASM payload takes to boot; and
- `<HeadOutlet @rendermode="@(new InteractiveWebAssemblyRenderMode(prerender: false))" />`
  — the outlet the pages' `<PageTitle>` writes into once the runtime is up.

Neither alone is sufficient (see Pitfalls). There is no duplicate-`<title>`
hazard: with `prerender: false` the outlet emits nothing into the static pass,
so every route serves exactly one `<title>`.

### The e2e smoke gate (`BgQuiz_Blazor.E2eTests`)

The primary-path smoke gate AGENTS.md mandates: scenarios driving the
**published artifact in a real Chromium** via Microsoft.Playwright — the
pick→done flows, the reload notice, the empty-filter banner, the nb-NO
comma-decimal guard, 404/titles, and the stats-persistence suite. It exists
because four production defects in a row — inert titles, blank 404 bodies, the
phantom auth gate, the silent 0/0 empty-filter bounce — were
invisible-by-construction to both existing layers: bUnit renders components in
isolation, and the `WebApplicationFactory` wire tests run the host pipeline
in-process with no browser. All four lived in the one layer neither sees: the
publish output booting a real WASM runtime in a real browser.

**Layer under test = the publish output.** A collection fixture
(`PublishedAppFixture`) runs `dotnet publish` (Release) once per test run,
spawns `dotnet BgQuiz_Blazor.dll --urls http://127.0.0.1:0 --contentRoot
<publish dir>`, resolves the OS-assigned port from Kestrel's "Now listening on"
line, probes readiness, and tears the process down on dispose. Not `dotnet run`
and not `TestServer` — those put a different layer under test. The
`--contentRoot` is load-bearing: without it `MapStaticAssets` resolves against
the wrong web root and serves 0-byte framework assets (unstyled page, WASM
never boots). The publish output carries three `runtimeconfig.json`; the host's
`BgQuiz_Blazor.dll` is the entry point.

**Base-URL seam.** `BGQUIZ_E2E_BASE_URL` overrides the target: when set, the
suite skips publish/spawn and drives that URL — the same scenarios can
point at the deployed site (`https://bgquiz-gobetzu.azurewebsites.net`) or at a
locally spawned instance kept alive across iterations. The seam is deliberately
just the URL; no further live-mode plumbing exists.

**Folder picks.** The WASM boot marker is Home's `#pickProblemFolder` button.
`PickFixtureAsync` stages each committed fixture into a fresh temp directory
and hands the *directory* to the hidden `#problemFolderFallback`
`webkitdirectory` input via `SetInputFilesAsync` — a genuine directory upload
through the app's real fallback collection path (top-level filter, buffering,
holder), no native dialog involved. Staged dirs are cleaned per test. The
migrated flow scenarios therefore run as no-stats quizzes by construction;
that's correct — they assert quiz flow, not stats.

**The FS-Access path** lives in `FsAccessFakeTestBase`, riding the base
class's second customization seam, `ContextInitScript` (applied via
`AddInitScriptAsync` *before* the page is created — parallel to the
`ContextOptions` locale seam): Playwright cannot drive the native directory
picker or its permission prompts, so the base injects a fake
`window.showDirectoryPicker` — a scripted directory handle (async `values()`
enumeration over the real fixture's bytes, `getFileHandle`, `createWritable`
capturing writes, scripted permissions). The faking stops at the browser-API
boundary: the app ships **no test seams**, and everything from the app's own
`folderAccess.js` inward runs for real — if the module's use of the File
System Access surface drifts from what the fake mirrors, the pick fails
visibly and the scenarios fail loudly. Per-scenario variation (corrupt stats
file, denied permission) is a page-level init script overriding the fake's
config object — context scripts run first, so the override wins — and a
mid-test `EvaluateAsync` can mutate it between quizzes (the app re-reads the
stats file at every Start's re-bind). Two suites ride the fake.
`StatsPersistenceTests` pins: one fold ⇒ one captured write with
`schemaVersion` 1, one decision record, a cube-as-two-decisions tally
(2 submitted / 2 correct), indented; corrupt file ⇒ polite notice + **zero
writes**; denied ⇒ denied notice + zero writes; and the fallback pick's
"can't save stats" notice. The stats filename and wire property names are
deliberately hardcoded in the suite — it is the consumer-side pin of those
contracts (the e2e project references no app assembly by design).
`MixWeightingTests` drives the weighted path: a 100%-never-seen mix built
through the panel UI composes the unseen fixture and runs to Done (one write
captured — the weighted run still records); and the composed-to-zero
scenario runs a blank-mix quiz first, **feeds the app's own captured write
back** as the pre-existing stats file (no hand-crafted wire format, no
decision-id knowledge), then starts weighted and asserts the mix-aware zero
notice with no 0/0 bounce. `MixRefusalTests` (no fake — the real fallback
pick, like the migrated flow scenarios) pins the no-stats ruling end to end:
early advisory → refused Start → "Start without mix" override → Done.

**Fail loud, never skip.** Missing Playwright browsers, a missing committed
fixture, a publish failure, a port-bind or readiness failure — each fails the
suite with an actionable message (the browser-missing failure names the install
command). Nothing skips: a skipped smoke that reads as green is the exact
defect class the gate exists to kill.

**Determinism.** No `Task.Delay` sleeps anywhere — Playwright auto-wait and
explicit `Expect` assertions only. Every flow helper ends by awaiting the
user-visible consequence of the transition it triggered. The two committed
fixtures are single-decision `.xgp` files (the `.xgp` emission policy yields at
most one decision per file), so each quiz is exactly one problem long with
shuffle left off. In-app navigation is asserted with polling URL assertions
(`Expect(Page).ToHaveURLAsync`), **not** `WaitForURLAsync` — Blazor navigates by
`pushState` (same-document), and the navigation-event wait can lose the race
when the push lands between the triggering click and the wait's registration
(observed as a rare timeout with the app already on the target URL).

**Fixtures are safe to publish.** Both are synthetic, carry no player names
(verified before committing), and are *copies* — the umbrella's
`TestData/FixtureFiles/` stays append-only and untouched.

**Board driving.** The checker scenario enters a real play by clicking the
diagram's transparent SVG hit-region rects. Region identity is positional: the
producer renders points 1–24 first (point order), then bar/cube/tray/dice, so
rect index `point − 1` addresses a point. The rects carry no identifying
attributes, so that render-order contract is the only test-side handle; if it
ever changes, the play never assembles and the scenario fails loudly at its
Submit-enabled gate. Making it contractual (a `data-point` attribute on the
rects) is a BgDiag_Razor producer arc, not something to patch from here.

**Running it.**

```
# one-time per machine, after building the e2e project:
pwsh BgQuiz_Blazor.E2eTests/bin/Debug/net10.0/playwright.ps1 install chromium

# the gate (publishes + spawns the artifact itself):
dotnet test BgQuiz_Blazor.E2eTests/BgQuiz_Blazor.E2eTests.csproj

# against a deployed or already-running instance instead:
BGQUIZ_E2E_BASE_URL=https://bgquiz-gobetzu.azurewebsites.net \
  dotnet test BgQuiz_Blazor.E2eTests/BgQuiz_Blazor.E2eTests.csproj
```

The fast unit suite stays browser-free: run it via
`dotnet test BgQuiz_Blazor.Tests/BgQuiz_Blazor.Tests.csproj`. A solution-level
`dotnet test` now runs both. Expect the gate's *first-ever* Release publish of
the WASM closure to take several minutes (IL trimming, cold); incremental
republishes take seconds.

## Public API

This is an application, not a library — no exported types or HTTP
endpoints, and the `.Client` assembly enforces that at the type level: every
plain-C# client type (`QuizController` + `QuizStartOutcome`, the scoped holders
`PickedProblemFolder` / `AppliedFilter` / `AppliedMix` / `ShuffleOption` /
`QuizLiveMarker`,
`PickedFile`, `IFolderAccess` / `JsFolderAccess` (+ its wire DTOs),
`StatsSaveCapability`, `FolderPickOutcome`, `QuizStatsFile`,
`IDecisionStatsSink` / `QuizStatsStore` / `QuizStatsStatus`, `MixDisplay`,
`WasmUploadedProblemSetSource` / `CachedProblemSetSource`, `ProblemReview`, and the `ProblemSetSourceFactory`
delegate) is `internal`, reachable by the test project only through the
`InternalsVisibleTo` grant. The only `public` types are the Razor components — the
framework requires them public (see Pitfalls). The externally visible surface is
the route map:

- `/` → `Home` — filter selection + Start
- `/quiz` → `Quiz` — active problem (redirects to `/` if no quiz, `/done` if finished)
- `/stats` → `Stats` — read-only mid-quiz stats (redirects to `/` if no quiz, `/done` if finished)
- `/done` → `Done` — final summary (redirects to `/` if no quiz)
- `/help` → `Help` — end-user documentation (never redirects; linked from the nav menu)
- Default error page → `Error.razor`
- `/not-found` → `NotFound.razor` — the 404 page, and a **mapped route in its own
  right** (requesting it directly is a 200). It is reached two ways, and both are
  needed: `Routes.razor`'s `NotFoundPage` covers *client-side* navigation once the
  runtime has booted, and `Program.cs`'s `UseStatusCodePagesWithReExecute("/not-found")`
  covers *server-side* unmatched paths, which never reach Blazor at all (see
  Pitfalls). The re-execute preserves the 404 status.

## Pitfalls

- **The e2e suite is the smoke gate AGENTS.md mandates — pointer bumps run it,
  and it must never learn to skip.** `BgQuiz_Blazor.E2eTests` is the layer that
  sees what bUnit and the wire tests structurally cannot (see Architecture § The
  e2e smoke gate), so it is the gate to run before a pointer bump ships. Two
  standing rules: (1) never convert a broken precondition — missing browsers,
  missing fixture, failed publish — into a `Skip`; the suite's fail-loud posture
  is deliberate, because a skipped smoke reads as green, which is the defect
  class it was built to kill. (2) Its `Fixtures/` are committed copies; the
  umbrella's `TestData/FixtureFiles/` stays append-only and untouched. First-run
  setup (`playwright install chromium`) and the run commands are in the
  Architecture section.
- **State resets on full reload, not on in-app navigation.** "Scoped" in
  WASM is one instance per loaded app (one tab), so `QuizController`,
  `PickedProblemFolder`, `AppliedFilter`, and `ShuffleOption` survive `/` ↔
  `/quiz` ↔ `/done` navigation but a full browser reload re-boots the runtime
  and loses everything (picked folder, applied filter, shuffle choice, quiz
  progress — though not the stats *file*, which lives on disk in the user's
  folder and resumes on re-pick). Reload-survival
  is a deferred arc — don't assume reload resumes. Anything that *should*
  survive navigation belongs in a scoped holder, not a component field —
  the two halves of Home's start gate were moved off transient fields for
  exactly this reason. (Genuinely per-visit page state — e.g. Home's
  `_startError` banner — correctly stays a component field and resets on
  navigate-back.) The one thing that *does* survive a reload is the
  `QuizLiveMarker` (`sessionStorage`), and that is deliberate — it exists solely
  to acknowledge the reset on the next boot; see the next bullet.
- **The `QuizLiveMarker` is `sessionStorage`, not `localStorage` — don't
  "upgrade" it.** The marker records "a quiz is live in *this tab*" so a reload
  can be acknowledged on the next boot. `sessionStorage` is per-tab: it survives
  a reload but is invisible to other tabs and dies with the tab — exactly those
  semantics. `localStorage` is shared across every tab of the origin, so a quiz
  live in tab A would set a marker a freshly-opened tab B reads on *its* first
  boot, making B falsely announce "your quiz was reset" for a quiz it never ran.
  It looks like the "bigger, more durable" store; it is the wrong one here. (The
  real reload-*resume* arc will need durable storage — IndexedDB for the buffered
  bytes — but that is a different concern from this per-tab liveness flag.) The
  controller-side `HasStarted` guard in `Home.OnInitializedAsync` is the
  complementary defence: it suppresses the notice during in-app navigation back to
  `Home` mid-quiz, where the marker is legitimately set but no reload happened.
- **Cube decisions carry `Dice == [0, 0]` — never auto-skip them.**
  `IsPassPosition` runs `MoveGenerator.GeneratePlays` on the dice; a cube
  decision's `[0, 0]` produces the no-legal-play sentinel, so without the
  `if (data.Decision.IsCube) return false;` guard at the top of
  `IsPassPosition`, every cube decision is silently auto-skipped and the
  whole cube feature is invisible. The guard is the first line; don't
  remove it.
- **`BackgammonPlayEntry` is strict on decision type.** It throws
  `NotImplementedException` on a cube decision, so `Quiz.razor`'s checker route
  must be exact — a cube decision reaching it fails loudly at render. The cube
  route renders a plain read-only `BackgammonDiagram` (no such guard); routing by
  `IsCube` stays page-side.
- **`BackgammonCubeActions.ValueChanged` is `[EditorRequired]`.** It backs the
  `@bind-Value="_completedCube"` binding; omitting the binding surfaces as
  `RZ2012` (→ error under `-warnaserror`), not a silent splat — unlike the play
  side's `OnPlayCompleted`. Keep the `@bind-Value` present: the radios are
  strictly controlled, so without the binding they are inert.
- **Razor silently drops bindings to non-existent component parameters.**
  `<FilterPanel OnFiltersChanged="..."/>` against a panel that exposes
  `OnFilterConfigChanged` does not fail at build or render time — the
  binding is simply never invoked. Symptom is a callback that "obviously"
  fires never firing. When wiring an event from an RCL-imported
  component, verify the parameter name against the source. The bUnit
  regression test `PageTests.Home_FilterPanelEmitsConfig_EnablesStartButton`
  guards against this trap.
- **Client plain-C# types are `internal`; only Razor components are `public`.**
  The controller, the scoped holders, `PickedFile`, the folder/stats types
  (`IFolderAccess` / `JsFolderAccess`, `QuizStatsFile`, `QuizStatsStore` and
  friends), `WasmUploadedProblemSetSource` / `CachedProblemSetSource`, `ProblemReview`, and the
  `ProblemSetSourceFactory` delegate are all `internal`, with `InternalsVisibleTo`
  granting the test project access. Don't widen one to `public`: the tests already
  see it through the IVT grant, and a page reaches it through `@inject` — which
  binds a service **by type from DI**, not through the public surface. The
  narrowing is total (9 candidates → 9 internalized / 0 held) precisely because
  Razor's `@inject` generates a **private** property, so a DI-injected type never
  lands in a public signature, and none of these types is a component
  `[Parameter]`. The one move that *forces* a client type back to `public` is
  putting it in a public component's `[Parameter]` (or any other public member
  signature) — that trips **CS0053** (inconsistent accessibility), the same
  constraint the earlier component-surface narrowing hit; the fix is to keep the
  crossing type a library/wire type, not to re-widen the app type. The pages, in
  turn, **cannot** go internal: the router discovers routable components by
  scanning the assembly's *public* (`ExportedTypes`) surface, so that public
  boundary is framework-required, not a missed narrowing.
- **Off-list submission semantics.** A structurally-legal play that
  doesn't appear in the analyzer's candidate list counts as a skip, not
  a scoring miss. This is rare on well-analyzed positions and signals
  an analysis omission rather than user error. Don't expect every
  user-submitted play to land in History.
- **Pass-position sentinel is not empty-list.** `MoveGenerator.GeneratePlays`
  signals "no legal play" with `count == 1 && plays[0].Count == 0`
  (a single zero-move Play, dice forfeited). Code that gates on
  `legal.Count == 0` will silently miss every pass position.
- **`Quiz` is both a namespace (`BgQuiz_Blazor.Client.Quiz`) and the page
  type (`BgQuiz_Blazor.Client.Components.Pages.Quiz`).** Test code that does
  `Render<Quiz>()` after `using BgQuiz_Blazor.Client.Quiz;` hits a CS0118
  ambiguity. Test files use a `using QuizPage = ...` alias to
  disambiguate.
- **A picked file's name must keep its extension.** The stream iterator
  discriminates `.xg` vs `.xgp` from the file-name extension to stamp the
  `DecisionId`, so an extensionless `PickedFile.FileName` is a usage error
  the iterator throws `ArgumentException` on when it reaches that entry —
  lazily, mid-enumeration, not at construction. Both of `folderAccess.js`'s
  pick paths preserve the browser's extension-bearing entry names precisely
  for this (`JsFolderAccessTests` pins the carry;
  `WasmUploadedProblemSetSourceTests.EnumerateAsync_ExtensionlessName_…`
  guards the failure mode). Start-time exceptions (this, plus
  `FilterConfig.Build()` validation) surface on `Controller.StartAsync` and
  `Home.razor` shows them as a banner rather than faulting the app.
- **Lifetime stats fold on Continue, never at Submit.** `RedoAsync` pops the
  last submission *while `Review` is set*, and `DecisionStatsDocument` has no
  `Minus` — folding at Submit would let a redone answer fold twice with no way
  back. An answer is final only when the user moves forward past it
  (`ContinueAsync`), and the deliberate flip side is that an answer abandoned
  in review (tab close, Start/Restart without Continue) never folds — don't
  "fix" that into a double-fold hazard. Skips, off-list plays, and
  auto-skipped pass positions never reach the sink at all (producer contract).
- **Never silently clear or rewrite the stored `QuizMix`.** The persisted mix
  (`xg_quizMix`) outlives any session that can't honor it: a refused weighted
  start, the per-run "Start/Restart without mix" override, and a corrupt
  restore all leave it untouched (corrupt just yields a blank *builder*). The
  one sanctioned overwrite is the panel's own Apply/Reset — an explicit user
  gesture. Same spirit as the never-overwrite-unreadable-stats rule below.
- **A refused weighted start touches no quiz state — check the outcome before
  `IsFinished`.** `StartAsync`/`RestartAsync` returning `MixRequiresStats`
  leaves the prior quiz (enumerator, scores, `Current`, `IsFinished`) and the
  stored config exactly as they were; the only `StateChanged` firings are the
  transition gate's two busy flips, which deliver unchanged quiz state.
  Callers must branch on the outcome *first*: Home's empty-result check reads
  `IsFinished`, which after a refusal is stale state from the previous quiz.
  Ordering them the other way shows a bogus no-match banner (or worse,
  navigates) off a quiz that never started. `Busy` sits before both checks
  and means do-nothing-at-all: the call was ignored by the gate, so the
  handler must change nothing (no banner, no navigation).
- **Overlap safety lives in the controller's transition gate — don't re-guard
  it page-side, and don't "fix" the dice-click + Continue double-binding.**
  The gate (see Architecture § `QuizController`) is what makes a second
  mid-transition gesture safe; page-level debouncing would duplicate the
  rule and rot. Two load-bearing details: the gate's post-set yield is what
  lets the busy state paint before the churn (don't "simplify" it away), and
  `AdvanceAsync` deliberately fires no `StateChanged` — the gate's busy-off
  fire is the completion signal, so re-adding a fire there double-renders
  every transition and breaks the pinned fire counts.
- **The stage-2 refusal's re-bind is a real side effect — including the
  WriteFailed sub-case.** Stage 1 (capability peek) refuses with zero side
  effects, but a stage-2 refusal has already run `BeginQuizAsync`, which
  unconditionally resets the in-memory document and reloads from disk. If the
  *prior* quiz sat in `WriteFailed` with folds living only in memory, those
  folds are dropped by the refused start even though no new quiz begins — the
  one variant of the re-bind that loses data rather than merely re-reading
  (the same in-memory loss any Start/Restart always caused; the file itself
  is never overwritten on the LoadFailed path). Rare×rare and accepted: a
  "skip the re-load when the bound context is WriteFailed on the same folder"
  guard would need JS handle-identity interop. Don't move the bind back after
  the source build to "fix" it — the wrap decision needs the bound context.
- **`QuizMix` entry order is semantic — preserve it everywhere.** Earlier
  entries win contested overlap (producer contract), so the mix panel's rows,
  the persisted JSON, the restore hydration, and the shortfall notice's
  per-entry lines must all keep declared order. Reordering is a real edit
  (dirties the gate); `MixPanelTests` pins order surviving Apply.
- **An active mix suppresses the shuffle wrap — in the factory, not the UI.**
  The mix's `RandomOrder: false` promises source-order determinism, which a
  `ShuffledProblemSetSource` under the composing decorator would silently
  break. The `Program.cs` factory wraps shuffle only when `mix.IsPassthrough`;
  Home's disabled checkbox is the honest *mirror* of that rule, not its
  enforcement — and disabled must never mean rewritten (`ShuffleOption` keeps
  the user's value; pinned).
- **The mix stats provider must never see a null document.** The controller
  wires `MixedProblemSetSource` only past the two-stage refusal, and the
  provider throws `InvalidOperationException` if `CurrentDocument` is null
  anyway (mirroring the producer's own contract) — composing against a
  fabricated empty document would mask a wiring bug as an all-never-seen
  quiz. An *empty* document is the legitimate everything-never-seen input;
  *null* is always a bug.
- **Never write over a stats file that failed to parse.** A load
  `JsonException` (corrupt, foreign, or newer-schema file) flips
  `QuizStatsStore` to `LoadFailed`, which is terminal *for that quiz*: no
  records, and — the actual guarantee — **zero writes**, so the user's
  existing data survives whatever went wrong. It resets only at the next
  Start's re-bind. `QuizStatsStoreTests` and the e2e corrupt-file scenario
  both pin the zero-writes half; keep them.
- **The stats context binds at Start/Restart (two-slot promote) — mid-quiz
  Clear/re-pick must never affect the running quiz's recording.** The JS
  module's *picked* slot belongs to Home (pick/Clear); the *active* slot
  belongs to the running quiz, bound only by the controller's
  `ResetAndAdvanceAsync` via `promoteToActive`. Wiring Clear (or a new pick)
  to touch the active slot — or moving the bind to pick time — re-opens the
  bug this shape exists to prevent: a user tidying up Home mid-quiz silently
  killing (or retargeting!) the quiz's stats recording.
- **The parse cache must stay unfiltered, holder-homed, and
  generation-guarded.** `PickedProblemFolder.ParsedDecisions` is the parse of
  the *whole* pick with no filters — caching a filtered parse would silently
  serve one filter config's subset to every later Start. Its invalidation is
  `Set`/`Clear` nulling it (cache lifecycle = pick lifecycle); don't move the
  slot off the holder and re-create the forgotten-invalidation-wiring
  hazard, and don't drop `StoreParsed`'s generation check — the pick gesture
  is async, so a re-pick can complete inside a Start's await points, and an
  unguarded store would install the *old* pick's parse as the *new* pick's
  cache. Post-hoc `Matches` over the cache is equivalent to
  filter-during-parse only because the iterator's skip/advance votes are
  contractually pure early-exit hints; a future filter whose votes cut rows
  its `Matches` would admit breaks that contract (and this cache) — the
  contract lives on `IDecisionFilter`/`IMatchFilter` in XgFilter_Lib.
- **Browser directory handles live in JS module state only.**
  `FileSystemDirectoryHandle` / `File` objects cannot round-trip the interop
  boundary; `folderAccess.js` owns them and C# sees names/bytes/booleans
  through `IFolderAccess`. Don't try to hold a handle (or an
  `IJSObjectReference` to one) in a C# holder — `JsFolderAccess` is the one
  type that touches interop, and pages depend on the interface.
- **The WASM dependency closure must stay native-free.** Everything the
  `.Client` references ships into the browser-wasm runtime, which has no
  native interop. Reference the `BackgammonDiagram_Lib` **core** (native-free
  SVG) only — pulling in `BackgammonDiagram_Lib.ExportRaster` (SkiaSharp /
  QuestPDF / OpenXml) would fault at runtime in the browser. The quiz renders
  SVG, never raster. This is why the split exists; don't re-add the raster
  reference to make some export "just work" client-side.
- **`BackgammonPlayEntry` doesn't need a `@key` to reset across Redo — the
  branch swap already does it.** It suppresses its own internal reset when the
  incoming `Request` describes the same problem as last time (same Mop/Dice) — a
  defense against losing in-progress click state on a same-problem re-render.
  It's tempting to assume `RedoAsync` (which returns to that exact same problem)
  needs an explicit reset call or a changing `@key` to work around that
  suppression. It doesn't: the entry lives in the `else` branch of
  `@if (Controller.Review is { } review) { ... } else { ... }`, and Submit already
  unmounts it entirely when the page swaps into the review branch. By the time
  Redo swaps back, the entry did not exist in the immediately prior render at
  all, so Blazor cannot reuse an instance that wasn't there — a fresh one is
  constructed unconditionally, same-problem suppression or not. Verified (not just
  reasoned about) by temporarily adding a redo-generation `@key` and confirming
  the suite stayed green with it removed. Don't reintroduce that key defensively;
  if a future refactor keeps the entry mounted across review (e.g. overlaying the
  solution instead of swapping branches), *that's* the point to re-examine. The
  cube answer needs none of this: `BackgammonCubeActions` is strictly controlled
  off `_completedCube`, which `HandleStateChanged` nulls on every transition, so
  its radios clear on Redo with no internal state to reset.
- **The status strip must stay fixed-height, and the board-sizing glue must
  stay retired.** The strip's whole purpose is state-invariant chrome: equal
  chrome height ⇒ equal board flex remainder ⇒ no answering↔review board-size
  jump. Sizing it by content (`min-height`, auto height) reintroduces the
  per-question jitter it was built to remove — long content clamps instead
  (legend one line, verdict two). On the board side, sizing belongs to
  BgDiag_Razor's bounded-height contract (bound the `BackgammonPlayEntry` wrapper
  with a real height; the producer's `bg-board-slot` and `.bg-diagram` contain-fit
  default do the rest) — re-adding consumer `max-height` glue, `display: contents`
  on a wrapper, or styles inside `.bg-board-slot` breaks the contract (see the
  producer's pitfalls; `AppCss_RetiredBoundedHeightGlue_StaysGone` pins this). The
  cube-answering board is now a bare `.bg-diagram` (a direct child of
  `.board-container`, like review) — the cube radios moved out of the board region
  into the action row — so all three states (play-answering, cube-answering,
  review) size the board identically under the fold cap. This retired the former
  residual (the cube-answering board running radios-height shorter than its
  review); unifying it any other way would have meant re-encoding producer chrome
  height in the consumer, the magic-constant pattern this arc removed.
- **Pages set render mode per-page, not via `<Routes>`.** Each routable page
  carries `@rendermode @(new InteractiveWebAssemblyRenderMode(prerender:
  false))`. There is no global `<Routes @rendermode>` here (that was the old
  Interactive Server arrangement). The bUnit page tests render components
  directly and don't exercise WASM render-mode dispatch, so verify real
  interactivity in a browser, not just in tests.
- **A bare `<HeadOutlet />` participates only in the static render pass.** It is
  not a render-mode-agnostic sink. Since every routable page here is a `.Client`
  page with `prerender: false`, none of them render server-side, so a bare outlet
  receives nothing — and once WASM boots, the pages' interactive `<PageTitle>`
  has no interactive outlet to write into. The symptom is an empty
  `document.title` on every page a user actually visits, with the six
  `<PageTitle>` components looking correct in source. The outlet must carry
  `@rendermode="@(new InteractiveWebAssemblyRenderMode(prerender: false))"`.
  The static `<title>BgQuiz</title>` above it is equally load-bearing and must
  not be deleted as redundant: with `prerender: false` the outlet cannot set a
  title until the ~19.5 MB payload boots, so removing the static title reinstates
  a titleless first-load window and leaves crawlers and no-JS clients with no
  title ever.
- **`/Error` shows `BgQuiz`, not `Error` — deliberately.** `Error.razor` is a
  server-side, statically-rendered page, so its `<PageTitle>Error</PageTitle>` can
  only reach a static-pass outlet, and the outlet is now interactive (above). Its
  title therefore falls back to the static `<title>`. Verified, not assumed:
  `/Error` renders its `Error.` heading with `document.title === "BgQuiz"`.
  (`/not-found` also shows `BgQuiz`, but it never declared a `<PageTitle>` at all,
  so nothing regressed there.) This is an accepted trade — a terminal page nobody
  navigates to on purpose, in exchange for correct titles on the five pages people
  use. **Do not "fix" it** by reverting the outlet to a bare one; that restores
  `<title>Error</title>` on `/Error` and silently re-breaks all five real pages.
  The title is not the whole cost. The render-moded `HeadOutlet` is a WASM **root
  component on every page**, `/Error` and `/not-found` included — so both of those
  server-rendered terminal pages now boot the ~19.5 MB payload to accomplish
  nothing, since neither has a `<PageTitle>` the outlet can receive. The pages
  render and read correctly before the boot completes; the payload is pure waste on
  them. Accepted for the same reason as the title: they are pages nobody reaches on
  purpose. If that ever stops being true (a heavily-linked 404, say), the fix is to
  give the head outlet a narrower home, **not** to un-render-mode it.
- **`NotFoundPage` covers client-side navigation only; server-side unmatched paths
  need `UseStatusCodePagesWithReExecute`.** `Routes.razor`'s
  `NotFoundPage="typeof(Pages.NotFound)"` is the Router's answer for a route the
  *booted WASM runtime* can't match — i.e. in-app navigation. It does nothing for a
  cold request: `MapRazorComponents` registers endpoints only for known routes, so
  an unmatched URL never reaches Blazor and falls through to a bare ASP.NET 404 with
  a **zero-byte body**. The symptom is a completely blank page — no HTML, no title —
  which reads as "the site is down" rather than "that page doesn't exist," while
  `/not-found` requested directly renders fine at 200 (it's a mapped route). The
  host pipeline's `UseStatusCodePagesWithReExecute("/not-found")` is what closes it.
  Keep it **before** `UseAntiforgery()` (see Render mode). `NotFoundPipelineTests`
  pins the status contract; a bUnit render cannot.
- **The re-execute also catches missing *assets*, and that is accepted — on
  purpose.** `UseStatusCodePagesWithReExecute` intercepts every bodyless 4xx/5xx, so
  `/_framework/no-such-asset.js` and `/no-such.json` come back as 404 with the
  NotFound page's `text/html` body rather than an empty one. This is not a
  misrepresentation: on a 4xx the body is an *error document*, not a representation
  of the requested resource — RFC 9110 has the server send "a representation
  containing an explanation of the error situation," and `text/html` truthfully
  describes the body actually sent. The 404 status, which is what every consumer
  keys on (Blazor's boot loader included), is correct and preserved, and the body is
  inert: nothing executes a script-tag document returned to a `fetch` for a `.js`
  file. Assets that *exist* are untouched — the middleware only engages on an error
  response with no body.
  - **Reordering cannot fix the asset case.** A missing static file is not answered
    by `UseStaticFiles`/`MapStaticAssets`; those call `next()` and the 404 is
    produced downstream by routing. The status-code-pages middleware wraps
    everything downstream of itself, so moving it *after* the static-file
    middleware changes nothing. Don't try.
  - **The trigger for revisiting.** When the attribution/persistence arc adds
    server-side JSON API endpoints, a typed client calling `ReadFromJsonAsync`
    against a 404 will throw a confusing `JsonException` instead of surfacing the
    status. *That* is when narrowing acquires a real consumer. The only defensible
    discriminator at that point is **content negotiation on the `Accept` header** —
    a path-prefix or extension sniff duplicates routing knowledge inside middleware
    and still misses cases like `/no-such.json`.

## Subproject-internal next steps

- **Phase 2+ design.** Stats-weighted composition now ships (the `MixPanel` →
  `QuizMix` → `MixedProblemSetSource` pipeline over the lifetime record).
  Still open from the phase-2 sketch: an in-session history model /
  re-queue-on-wrong (distinct from lifetime weighting), the Done-page
  retrospective below, and the three two-agent modes (user-vs-user,
  user-vs-bot, bot-vs-bot tournament).
- **Reload-resume (persistence).** A full browser reload re-boots the WASM
  runtime and loses the picked folder and quiz progress. Surviving a reload
  needs the picked bytes + decisions/progress persisted client-side
  (IndexedDB — `localStorage` is too small for buffered `.xg` bytes); this
  is a deferred arc of its own, distinct from the stats file (which lives on
  the user's disk and already survives reload via re-pick). Until then,
  reload-reset is the intended default.
- **Mobile folder picking.** Logged under the umbrella's mobile-assessment
  item: `webkitdirectory` is weak-to-absent on mobile browsers (iOS Safari
  and many Android browsers offer no real directory upload), so on phones the
  fallback pick — and with it the whole app's pick gesture — may not work at
  all. Assess alongside the general mobile-layout pass; not solved here.
- **Done-page retrospective.** Per-problem review now ships *in-quiz* (the
  review state's solution diagram shows the best play/action, the equity gap,
  and — for cube — which half the user got wrong). What's still missing is a
  *post-quiz* retrospective on Done: the four-way `ScoreBreakdown` reports only
  aggregate Play/Double/Take/Total accuracy, with no way to revisit individual
  problems after finishing. A scrollable list of the `History` / `CubeHistory`
  entries (each re-rendering its solution diagram) would close the loop.
