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

- **BgGame_Lib** — substrate. `IProblemSetSource`, `ShuffledProblemSetSource`
  (the decorator the source factory wraps the picked set in when "Shuffle
  order" is on; the app uses the **unseeded** ctor — shuffling is
  user-facing, the seeded ctor is test-only), `SubmittedPlay`,
  `SubmittedCubeAction`, `QuizScore` (segmented: `PlayDecisions` /
  `DoubleDecisions` / `TakeDecisions` + derived `Total`), the stats-weighted
  composition surface — `QuizCategory`/`QuizCategoryKind`,
  `QuizMix`/`QuizMixEntry` (the versioned strict-JSON mix config;
  `ToJson`/`FromJson`/`TryFromJson` is the localStorage trio),
  `MixedProblemSetSource` (the composing decorator the controller wires for
  a non-blank mix) + `MixComposition` telemetry — and the lifetime-stats
  model `DecisionStats` / `DecisionStatsDocument` (immutable;
  `doc = doc.Plus(submission, TimeProvider)`; bundled type-level JSON
  converter — deserializes with no registration, any bad load throws
  `JsonException`; a cube position folds as **two** lifetime decisions,
  matching `QuizScore`'s two-half fold, so a half-right cube reads 1-of-2).
  The controller talks to the source through `IProblemSetSource` and scores
  via `QuizScore.Plus`; the stats store folds finalized submissions via the
  document's `Plus`. Producer behavior — e.g. the per-enumeration reshuffle
  that makes a Restart reshuffle rather than replay — lives in BgGame_Lib's
  own INSTRUCTIONS.md.
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
  DiagramMode.Problem)` (Problem mode blanks the analysis panel, so it never
  leaks the answer); the review view uses `DiagramRequest.Builder.From(...,
  DiagramMode.Solution)` and overrides the user marks (`FromDecisionData`
  can't be used there — it would default them from the recorded player).
  Direct `<ProjectReference>` — the page calls the factory by name, so the
  dependency is explicit rather than riding BgDiag_Razor's transitive
  surface. Only the **native-free core** is referenced; the raster/export
  sibling `BackgammonDiagram_Lib.ExportRaster` is deliberately **not** —
  the quiz renders SVG only (see Pitfalls: the WASM closure stays
  native-free).
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
    robots.txt                      — Disallow: / for every crawler (the URL
                                      is hand-distributed to invited beta
                                      testers; indexing only harvests it).
                                      Must live in the HOST wwwroot — see
                                      Pitfalls (two wwwroots)

BgQuiz_Blazor.Client/              — WASM client (the whole interactive surface)
  BgQuiz_Blazor.Client.csproj       — Sdk.BlazorWebAssembly; the bg-lib closure
  Program.cs                        — WebAssemblyHostBuilder; registers
                                      TimeProvider.System (singleton) + the
                                      controller, holders, stores, and
                                      ProblemSetSourceFactory (all scoped)
  _Imports.razor
  AppInfo.cs                        — app-level identity SSOT (version + beta
                                      feedback mailto); see § AppInfo
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
    FolderPickDisplay.cs            — folder-pick wording SSOT (cause-agnostic
                                      premise; supported-browsers statement;
                                      never quote prompts or promise a count)
    QuizStatsFile.cs                — stats filename + JsonSerializerOptions SSOT
    QuizStatsStore.cs               — IDecisionStatsSink + the stats document
                                      lifecycle (bind at Start, fold + write-back)
    QuizFiltersFile.cs              — saved-filters filename SSOT (no options —
                                      the collection owns its wire format)
    SavedFiltersStore.cs            — saved named filters over the picked slot
    AppliedFilter.cs                — applied-filter holder (start-gate half)
    AppliedMix.cs                   — committed-mix holder (start-gate third)
    MixDisplay.cs                   — mix wording SSOT (labels + refusal reason)
    CubeActionDisplay.cs            — cube-verdict wording SSOT (labels the
                                      halves by the user's submitted actions)
    MixNoticeDismissal.cs           — Quiz's composition-notice dismissal,
                                      keyed on the composition's identity
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
  CubeActionDisplayTests.cs
  MixPanelTests.cs                  — builder round-trip / validation / order pins
  AppliedMixTests.cs
  QuizStatsStoreTests.cs            — bind / fold / write-back / degrade guarantees
  SavedFiltersStoreTests.cs         — load / persist / degrade (zero-writes pins)
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
  FsAccessFakeTestBase.cs           — the fake showDirectoryPicker seam, shared
                                      by the FS-Access-path suites
  QuizFlowTests.cs                  — cube + checker primary paths, pick → Done
  EmptyFilterBannerTests.cs         — empty-result banner; no 0/0 bounce
  ReloadNoticeTests.cs              — reload-reset notice, Start and Restart paths
  StatsPersistenceTests.cs          — FS-Access stats path via the fake (+ fallback
                                      notice pin)
  SavedFiltersPersistenceTests.cs   — saved-filters FS path via the fake
  MixWeightingTests.cs              — weighted start to Done; composed-to-zero via
                                      the app's own write fed back; + MixRefusalTests
                                      (refusal + "Start without mix" override)
  CommaDecimalLocaleTests.cs        — nb-NO comma-decimal guard
  HelpAndTitlesTests.cs             — /help renders; document.title contract
  BetaOnboardingTests.cs            — robots.txt served over HTTP; the one
                                      feedback mailto on Home and Help, subject
                                      carrying the built version off the footer
  NotFoundTests.cs                  — unknown URL → 404 status + styled body
```

## Architecture

### Quiz flow

```
/        Home.razor    → "Choose folder…" pick; then (disclosed once files are
                          picked) SavedFilters + FilterPanel + "N match" count +
                          MixPanel (Enabled picks only) + "Shuffle order"
                          checkbox + Start Quiz button
                          on Start: Controller.StartAsync(filters, mix) — binds
                          the lifetime-stats context and, for a non-blank mix,
                          composes from lifetime stats (or REFUSES — the
                          actionable notice with "Start without mix") — then
                          Nav→/quiz

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

Scoped DI lifetime — in the WASM client "scoped" resolves to **one instance
per loaded app (one browser tab)**, so quiz state survives in-app navigation
and is reset only by a full browser reload (see Pitfalls; reload-survival is
a deferred arc). The controller holds the active `IProblemSetSource`
enumerator, the running `QuizScore`, the per-problem `SubmittedPlay`
(`History`) and `SubmittedCubeAction` (`CubeHistory`) histories — kept
separate because the two scored-result types are distinct shapes; a unified
history would force consumers to type-test — and a `SkippedCount` for
non-scoring outcomes (off-list submissions, explicit Skip). Pages observe
transitions via `StateChanged`: each gated async transition (below) fires it
exactly twice — busy-on, then busy-off with the end state in place — and the
synchronous mutators (Submit, Redo) fire it once.

**The transition gate.** The four async transitions — `StartAsync` /
`RestartAsync` / `ContinueAsync` / `SkipCurrentAsync` — share one busy gate:
a second gesture arriving while a transition is in flight **no-ops** (it does
not queue). The controller owns exactly one live enumerator, and an
overlapped `MoveNextAsync` — or a dispose during one — throws on a
thread-pool continuation no page can catch, terminating the WASM runtime
(the v1.0.4 double-Start crash). The per-method state guards can't close
that window: mid-advance they read *stale* state, so Skip/Submit would
stale-pass and a second Continue would double-fold. The gate lives in the
controller — pages never need the enumerator contract to be safe (which is
what makes the Quiz page's dice-click + Continue double-binding safe as-is).
The synchronous mutators (`SubmitPlay` / `SubmitCubeAction` / `RedoAsync`)
can't overlap an await themselves but can land *inside* one, so they no-op
on `IsBusy` too. Mechanics: `IsBusy` (observable; pages drive their busy
affordances from it) flips on inside the gate's check-and-set, `StateChanged`
fires, and the gate then **yields once, deliberately**, so the busy state
can render and paint before the transition's churn begins (the sources'
time-budgeted yields keep paints possible during the churn itself); a
`try`/`finally` releases the gate on completion *and* failure, firing
`StateChanged` again — the single completion signal (`AdvanceAsync` itself
no longer fires). Overlapped Start/Restart return `QuizStartOutcome.Busy`,
which callers treat as do-nothing (the in-flight transition owns the UI);
overlapped Continue/Skip return silently. The never-started `RestartAsync`
throw is checked *inside* the gate — an overlap is an outcome (Busy), not
the caller bug the throw exists for. `QuizControllerOverlapTests` pins all
of it via `GatedProblemSetSource` and the fake sink's `RecordGate`.

**Three-state per-problem flow.** Each problem moves through *answering* →
*review* → *advance*, surfaced via `Current` and the nullable `Review`:

- **Submit** — `SubmitPlay(Play)` / `SubmitCubeAction(CubeDecisionPair)` are
  **synchronous** (the only `await` was the advance, now deferred). They
  score the answer, set `Review`, and fire `StateChanged` **without
  advancing** — `Current` still points at the answered problem. No-ops
  outside the answering state (guarding against double-scoring).
- **`Review`** — a closed `ProblemReview` record (`Play` / `Cube`) carrying
  exactly the marks the solution diagram needs. Non-null marks the review
  state.
- **`RedoAsync`** — the inverse of Submit: pops the just-added entry from
  `History` / `CubeHistory` (or decrements `SkippedCount` for an off-list
  play, which never added a history entry), recomputes `Score` by refolding
  both histories from `QuizScore.Empty`, and clears `Review` — returning to
  *answering* on the same `Current` problem. The source enumerator and
  `IsFinished` are untouched. No-op outside review.
- **`ContinueAsync`** — the only *forward* exit from review: folds the
  just-reviewed submission into the `IDecisionStatsSink` (the lifetime-stats
  fold point — see Pitfalls: on Continue, never at Submit), clears `Review`,
  and advances. Exhausting the source here flips `IsFinished` — after the
  fold, so the final answer records. No-op outside review.
- **`SkipCurrentAsync`** — bypasses review and advances immediately, but
  only from the answering state (no-op while a `Review` is showing).

`ProblemReview` lives in `BgQuiz_Blazor.Client` (not BgGame_Lib): it is
per-app UI state, and adding it to the submodule would cross the boundary.
`ProblemReview.Play` carries the matched candidate index (`-1` off-list);
`ProblemReview.Cube` the two per-half equity losses. The Quiz page maps
these onto `UserPlayIndex` / `UserDoubleError` + `UserTakeError` so the
diagram marks the *quiz user's* answer, not the .xg-recorded player's.

**Source construction is factory-injected.** The controller takes a
`ProblemSetSourceFactory` delegate (`(DecisionFilterSet, QuizMix) →
IProblemSetSource`). The client's `Program.cs` registers it scoped as a
lambda that reads the `PickedProblemFolder` holder, builds a
`CachedProblemSetSource` over the pick (the parse-once layer — see its
section), then reads the `ShuffleOption` holder and conditionally wraps:
`mix.IsPassthrough && shuffle.Enabled ? new ShuffledProblemSetSource(inner)
: inner`. The mix parameter exists for exactly that one rule — **shuffle
arbitration**: an active mix owns presentation order through its own
`RandomOrder`, and a shuffled inner under the composing decorator would
silently break `RandomOrder: false`'s source-order determinism. The factory
never wires the composition layer itself (that is the controller's —
below). Both holders are read at **invocation** time (`StartAsync`), not at
DI registration, so choices made before Start take effect. Future
alternatives (deployed bundles, curated libraries) plug in by registering a
different factory; unit tests substitute a fake source the same way.

**Mix ownership mirrors filter ownership, and a weighted start can be
refused.** `StartAsync(FilterConfig, QuizMix, bool ignoreMix = false)` takes
the committed mix beside the filter config — user config in at Start, stored
for Restart, no caller-set mutation — and returns a `QuizStartOutcome`. For
a non-blank *effective* mix (the stored mix, unless the per-run `ignoreMix`
override), `ResetAndAdvanceAsync` wires the producer's
`MixedProblemSetSource` around the factory source, holding the typed
reference so `LastComposition` telemetry surfaces without type-testing; the
stats provider resolves `IDecisionStatsSink.CurrentDocument` fresh per
enumeration, so **Restart recomposes against the lifetime record as it
stands, this session's folds included** (deliberate, producer-documented).
Composing without stats is banned (ratified: no stats → feature unavailable,
never silently unweighted), so the start is **refused** in two stages: stage
1, the side-effect-free `IDecisionStatsSink.CanBindStats` capability peek —
refuses before even the stats bind; stage 2, after `BeginQuizAsync` (ordered
**before** the source build, because the wrap decision needs the bound
context), when the bind yielded no document (unreadable file). Either
refusal returns `MixRequiresStats` having touched **no quiz state** — the
prior quiz, its scores, and the stored config all survive, and the only
`StateChanged` firings are the gate's two busy flips — so Done's summary
stands behind a refused Restart. `RestartAsync(bool ignoreMix = false)`
re-attempts the stored mix every time, so the mix re-applies whenever stats
allow; the override is strictly per-run and the stored mix is never
rewritten.

**Presentation telemetry for the Quiz page.** `ActiveMixHasLength` exposes
the one fact the mix-notice framing needs — whether the run's *effective*
mix bound its percentages to a requested `QuizLength` (false for
passthrough, the ignore-mix override, and capless mixes) — committed past
the refusal checks so a refused start leaves it, like all active-run state,
untouched; intent over structure, no `QuizMix` leaks. `ProblemNumber` /
`ProblemCount` drive the "Problem N of M" indicator: N is the 1-based
**consumed stream slot** of `Current` (auto-skipped pass positions
included; reset by Start/Restart, untouched by Redo) and M is the
composition's `DrawnCount` (weighted) or the source's declared `Count`
(passthrough; null when streaming — the page then shows "Problem N" alone).
Slot-counting is the settled convention: both numbers count the stream, so
N never exceeds M and lands exactly on M at exhaustion; the accepted
trade-off — an auto-skip shows as a rare gap in the presented sequence —
is documented on `ProblemNumber`.

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

**Pre-Start match count.** `CountMatchesAsync(FilterConfig)` reports how
many decisions a config would admit — the number Home shows on Apply. It
builds the same controller-owned pipeline `StartAsync` would and counts a
source from the factory over a **throwaway** enumerator: the shared
enumerator, `Current`, `Score`, and the histories are never touched, so a
count is safe against a live quiz. It deliberately takes **no** transition
gate — it owns no shared enumerator to protect, and callers serialize Apply
against Start on their side. The pass is a byproduct of the source's
in-memory `Matches` filter, and it **warms the parse cache**, so the Start
that follows reuses it — the count front-loads Start's one-time corpus parse
rather than adding a cost on top of it. It counts every matching decision,
forced-move pass positions included, so the number is the **pre-mix pool**
the quiz and any weighted mix draw from — "decisions that match", not
"problems you'll see".

**Decision-type policy.** The user's `FilterConfig.DecisionType` choice
governs which decisions the quiz admits; `FilterConfig.Build()` adds a
`DecisionTypeFilter` only for a non-`Both` choice, and the controller adds
none of its own.

**Cube scoring.** A cube position is two independent atomic decisions — the
doubler's offer and the taker's response. `SubmitCubeAction(CubeDecisionPair)`
always scores both halves (no off-list / skip path, unlike plays): per-half
equity loss via `DecisionData.DoublerActionError` / `TakerActionError` and
per-half correctness against `BestDoublerAction` / `BestTakerAction`,
folded into the score's `DoubleDecisions` and `TakeDecisions` segments via
`QuizScore.Plus(SubmittedCubeAction)`.

**Pass-position auto-skip.** Each `AdvanceAsync` step pulls the next
decision and tests it with `MoveGenerator.GeneratePlays(board, d1, d2)`; the
no-legal-play sentinel (see Pitfalls) marks a pass position, which is
silently skipped — never shown, never counted toward `SkippedCount`.

**Off-list submission.** `SubmitPlay(Play)` matches the user's play against
`Current.Decision.Plays` by canonical `Play` equality (order- and
decomposition-insensitive, hit-sensitive — decomposed hops match their
combined listing; an intermediate hit stays off-list against a non-hitting
candidate). An in-list match contributes to the score: `EquityLoss == 0.0`
is the "best play" test (multiple candidates may share zero loss). An
off-list match counts as a skip (`SkippedCount++`, no history entry, score
unchanged) — see Pitfalls for the semantics. Either way a `Review`
(`OffList` true, index `-1`) is set so the user still sees the best play on
the solution diagram.

### `WasmUploadedProblemSetSource` — the in-browser source

Wraps `XgFilter_Lib.FilteredDecisionIterator.IterateXgStreamDiagrams`
(both `*.xg` match files and `*.xgp` position files). The constructor takes
`(IReadOnlyList<PickedFile> files, DecisionFilterSet filters, ILoggerFactory)`
and builds a single `FilteredDecisionIterator` held for the source's
lifetime; `ILoggerFactory` is preferred over `ILogger<…>` so the source's
contract doesn't leak the inner type. The files are parsed **entirely in the
browser** and never leave it.

**Re-iterability.** The source holds the file *bytes* (`PickedFile.Bytes`),
not open streams, and mints a fresh `MemoryStream` at position zero for
every `EnumerateAsync` call (wrapped in an `XgFileStream` carrying the
extension-bearing name) — the stream iterator reads each stream exactly
once, forward, so buffering up front is what lets a Restart re-enumerate.
`EnumerateAsync` also yields cooperatively so a long synchronous run doesn't
monopolise the single WASM thread — BgGame_Lib's `CooperativeYielder` (one
per enumeration; time-budgeted ~50 ms, not per-item `Task.Yield`, whose
event-loop round-trip per decision dominated large parses). The pacing clock
is a ctor `TimeProvider` (production: the DI system clock) — pure pacing,
never affecting which decisions flow.

`Count` is null (an up-front count would require a full filtered pre-pass).
`Name` is `"No files"` / the single file's name / `"{N} files"`. Decision-type
admission is governed entirely by the supplied `filters`; the source injects
no policy of its own.

### `CachedProblemSetSource` — the parse-once cache

The production source the `Program.cs` factory builds (the stream source
above remains the parser under it): parse the picked files **once**, then
serve every Start/Restart by filtering the cached decisions in memory.
Pre-cache (v1.0.4), every shuffled/weighted Start re-parsed the corpus
(~7.5 s warm); with the cache only the first Start after a pick parses —
repeat Starts are milliseconds.

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
  picker, then a `requestPermission({mode:'readwrite'})` on the picked handle —
  **two prompts, deliberately** (see Pitfalls), with wildly asymmetric declines.
  The *first* is load-bearing: decline it and the pick aborts holding nothing
  (⇒ `Cancelled`, indistinguishable from a dismissed picker). The *second* is
  the graceful rung: granted ⇒ `StatsSaveCapability.Enabled` — lifetime stats
  save into the folder; not granted ⇒ `PermissionDenied` — the handle stays
  readable, so the file list loads and the quiz runs read-only. `PermissionDenied`
  likewise carries **two** causes and can't tell them apart: the user answered
  no, *or* the request **auto-denied** with no prompt shown (some Chromium
  versions treat the transient user activation as consumed by the picker). So
  every surface for this rung opens with the cause-agnostic
  `FolderPickDisplay.WriteAccessNotGranted` — never "you declined", which on the
  auto-deny path attributes a decision the user never made. Home's pre-pick
  guidance names both grants and both consequences up front (see `Home`), and
  promises no *count* of prompts for the same reason.
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
failures throw (`JSException`), which callers catch and degrade on.
`Cancelled` carries **two** causes and does not say which — the picker was
dismissed, *or* the load-bearing view-files permission was declined; the browser
reports both as `AbortError`. Callers must read it as "no folder was picked",
never as "the user changed their mind" (Home's cancelled notice is worded to be
true under either). Byte
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
quiz's recording — recording changes only when the next Start re-binds. The
picked slot also serves the **saved-filters** read/write pair
(`readPickedFile`/`writePickedFile`): a setup-time concern on the folder
being configured, deliberately on the picked slot so it never requires a
promote and never touches a running quiz's active handle.

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

**Saved named filters.** A per-directory `bgquiz-filters.json` beside the
corpus lets the user save and reload `FilterPanel` configurations.
`QuizFiltersFile` is the filename SSOT — and, unlike `QuizStatsFile`, carries
**no** `JsonSerializerOptions`: `NamedFilterCollection` (XgFilter_Lib) owns
its wire format, so the app round-trips via the document's own
`ToJson`/`TryFromJson`. `SavedFiltersStore` (scoped; deps `IFolderAccess`,
`PickedProblemFolder`; read only by Home) owns the collection:
`LoadForPickAsync` reads the picked slot at pick time (`generation`-guarded
like the parse cache), `SaveAsync`/`DeleteAsync` apply the collection's
withers and persist, `Reset` clears on Clear. Same **degrade-never-block**
posture as `QuizStatsStore`, one status enum `SavedFiltersStatus` — `Ready` /
`LoadFailed` (unreadable *or* unparseable: file preserved untouched, zero
writes) / `WriteFailed` (in-memory kept, writes stop) / `Disabled` (no FS
pick). The `SavedFiltersPanel` (XgFilter_Razor) is persistence-agnostic — it
raises load/save/delete *requests* and Home mediates them; the store owns
every document mutation. Home's capability mapping: `Enabled` → full panel,
**even with zero saved filters** (you can save into it); `PermissionDenied` →
load-only (`CanPersist=false` + a reason naming both barred gestures — the
pick gesture grants read without the readwrite grant, and the
read-failure-tolerant `LoadFailed` path keeps that assumption from being
load-bearing) **and only when at least one filter is saved** — read-only
over an empty collection can neither load nor save, so the section is
hidden; `BrowserUnsupported` → no panel (the fallback can't see the file).
Two predicates gate this, deliberately: `SavedFiltersApplicable` (the rule
above) gates the *panel offering*; `SavedFiltersContextApplicable` (folder
held + FS-Access pick) gates the `LoadFailed` / `WriteFailed` *degrade
notices* — they must never collapse into one (see Pitfalls). Save-as of an
unparseable position pattern is refused by `FilterPanel.TryGetEditedConfig`
(exactly Apply's gate) and Home surfaces the refusal as a notice — the panel
already cleared its typed name optimistically, so a silent no-op would read
as a lost save.

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
  field desynced on navigate-back).

The pick is **in-memory only**: it survives in-app navigation but is reset
by a full browser reload — same deferred-arc caveat as the other holders
(the stats *file* is not lost with it; re-picking the folder resumes it).

### `PickedFileLimits` — the pick caps, single-sourced

`internal static class PickedFileLimits` (Quiz/) holds the two caps the
folder pick applies — `MaxFileBytes` (50 MB per file) and `MaxFileCount`
(500 per pick) — plus `MaxFileMegabytes`, **derived** from `MaxFileBytes`.

The caps have two consumers: `JsFolderAccess` *enforces* them (against the
enumerated metadata before any bytes cross the boundary; the byte cap is also
re-asserted as the `IJSStreamReference.OpenReadStreamAsync` max on the actual
transfer), `Help` *documents* them. Leaving them as private constants on the
enforcing type would have forced the help page to restate "50 MB" / "500" as
prose, so raising a cap would silently make the documentation wrong; deriving
the megabyte figure is what makes the SSOT actually hold. `PageTests` pins
Help's rendered prose against the constants (and the stats filename against
`QuizStatsFile.FileName`), so page and rule cannot drift. The constants stay
`internal`; the `.Client` csproj grants `InternalsVisibleTo` to the test
project rather than widening them to public.

### `AppliedFilter` — the filter half of the start gate

The per-app (`Scoped`, one-per-tab in WASM) holder for the `FilterConfig` the
user has **deliberately applied** on `Home` — the sibling of `PickedProblemFolder`
for the filter half of the start gate. `Home.razor` writes it: `Set(config)`
when the panel raises `OnFilterConfigChanged` (Apply / Clear filters), `Clear()` when
it raises `OnFilterDirty` (any control edit). `IsApplied` (= `Config is not
null`) and `Config` are read only by `Home` (`CanStart`, `StartQuizAsync`).

Holding the applied state here rather than in a transient component field is
what lets the gate survive in-app navigation: on navigate-back `Home`
re-derives `CanStart` from the persisted holders instead of resetting to
"not applied" and forcing a needless re-click of Apply.

**Gate semantics — applied, not merely present.** `IsApplied` means the user
took the Apply action, so a half-edited set must clear it (`OnFilterDirty →
Clear`). The interaction with `FilterPanel`'s localStorage restore is safe by
construction: restore writes the panel's own fields directly and raises
**neither** callback, so it can't spuriously mark applied or clear an existing
applied state — the holder is the sole authority on "applied".

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
`QuizMix.Empty` — the inert passthrough default. **Add category is styled
`btn-outline-primary`, not the panel's secondary grey — don't "unify" it**:
the button is never disabled (adding a row is always valid), but at zero
rows its three neighbours *are*, and in secondary grey it read as a fourth
switched-off control — the one misreading that must never happen, since it
is the only way out of the zero-row state. The class matches Home's `Choose
folder…`, the page's other required-but-unstarted step; `MixPanelTests` pins
state and appearance together, because the defect was the gap between them.

**Commit model mirrors FilterPanel** — `OnMixApplied` on Apply, Reset, and
**removing the last row** (both Reset and the last-row removal are an
explicit apply of `QuizMix.Empty` through the shared `GoBlankAsync`, the
sanctioned way this panel writes Empty over a stored mix; the last-row case
closes the wedge where a mix edited down to zero rows stayed `IsDirty` with
Apply disabled at zero rows, stranding Start until Reset), `OnMixDirty` per
other edit. The first-render localStorage restore raises **`OnMixRestored`**
carrying the restored mix, and Home ***reconciles*** it against the holder —
marked dirty only on a fresh load, never adopted, never re-gating a
surviving committed mix; the full rule and its rationale live in Pitfalls
(*the mix restore reconciles; it must never adopt*). No content-equality is
needed: Start requires `!IsDirty`, so a surviving committed mix was either
Applied (holder non-passthrough) or Reset/left blank (holder passthrough
*and* localStorage Empty, so the restore is passthrough too). Both
wiring-critical callbacks are `[EditorRequired]`. Persistence is the lib
trio over one key, **`xg_quizMix`**: `ToJson` on Apply, `TryFromJson` on
restore — absent/corrupt yields a blank builder, never an error; the
component never touches a JSON serializer.

**Offered only when the pick can provide stats.** Home renders `MixPanel`
only for `StatsSaveCapability.Enabled`. The mix composes from lifetime
stats, so under any other rung it has no valid role: the panel is hidden,
there is no way to build a mix, and **every pick resets `AppliedMix` to
passthrough+clean** (`AppliedMix.Reset()` in `EndCurrentSetupAsync`, which
both the pick gesture and Clear run — the invariant is "no pick →
passthrough"). Together those make a stats-less pick unable to coexist with
a committed non-blank mix — which is what retired the old early won't-apply
advisory. The panel is **`@key`-ed on `PickedProblemFolder.PickGeneration`**
so every pick re-mounts it and the fresh mount's restore re-offers the
persisted config as dirty (reconciled against the just-reset holder); the
key is load-bearing (see Pitfalls).

**`AppliedMix`** (Quiz/) is the committed-mix holder beside `AppliedFilter`:
`Current` (default `QuizMix.Empty`) + `IsDirty` + `Reset()`. Blank is the valid
default, so there is no "never applied blocks Start" state — only dirtiness gates
(`CanStart` requires `!AppliedMix.IsDirty`), preventing Start from running a
mix that differs from what the panel shows. The two start-gate halves block by
**different mechanisms**, because their defaults differ: the filter blocks via
not-yet-applied (it has no valid default), the mix via dirty (passthrough *is*
its valid default, so "never applied" can't be the gate). `Current` is
pick-coupled (reset on every pick and on Clear); `AppliedFilter` is
edit-coupled and deliberately is not. Scoped for navigate-back survival like its
siblings; unlike them the underlying choice also survives a reload
(localStorage), and the panel re-shows it — as a dirty, uncommitted mix — on the
next boot.

**`MixDisplay`** (Quiz/) is the wording SSOT: kind labels (the panel's
picker), full category labels (the Quiz page's mix notices), the
composition summary those notices lead with (`CompositionSummary` — "Your
quiz has N problems: 195 Never seen + 5 Ever got wrong.", every entry's
actual draw in declared order, zero-draw entries included), and the
refusal reason (Home's Start and Done's Restart render the same
capability/status rule — neither page hand-words it).

**Honest notices, all three.** (A fourth — the *signal early* won't-apply
advisory, shown when a stats-less pick coexisted with a committed mix — was
removed when the panel became stats-gated, which made that state
unreachable; don't re-add it, it has no trigger left.) (1) *Gate late*: a
refused weighted Start/Restart renders an actionable `role="alert"` with the
reason and the one-click per-run override ("Start without mix" / "Restart
without mix"); the stored mix is kept either way, and the notice says so.
The reachable refusal is **stage 2** — an `Enabled` pick whose stats file is
unreadable — since stage 1 (no capability) can no longer meet a committed
mix through the UI. (2) *Composed-to-zero*: Home's empty-result branch keys
on `LastComposition is { DrawnCount: 0 }` for mix-aware wording, parallel to
filtered-to-zero. (3) *Composition-first mix notices on Quiz*: every mix
notice leads with the effective quiz — `MixDisplay.CompositionSummary` over
`Controller.LastComposition` — before any apportionment internals. A
**length-bound** mix that fell short keeps the assertive `role="alert"`
framing under that lead: the asked-for-X-drew-Y line plus per-entry
drew-N-of-M when the target itself was missed, or per-entry
filled-N-of-its-P%-share lines when the target was met but a pool ran dry
(the internals demoted to explanation). A **capless** mix renders a
composition-only `role="status"` info line instead and never says
"requested": without a `QuizLength` the percentages bind to nothing —
per-entry `Requested` is largest-remainder apportionment of the pool union,
so an outdrawn entry is composition noise, not shortfall (the producer
guarantees Drawn == Target capless). The page keys the split on
`Controller.ActiveMixHasLength`; a length-bound mix that filled exactly
shows no notice at all.

**Both mix notices retire on the first submitted answer.** They say how
*this* quiz was built — worth reading before answering, stale chrome after —
so `Quiz.Submit` dismisses them once an answer lands, checker or cube alike.
Three deliberate choices: **dismissal, not deletion** (the controller's
telemetry is untouched — `LastComposition` and `ActiveMixHasLength` still
choose the framing and Home's composed-to-zero branch still reads them; a
presentation concern must not destroy load-bearing state); **a scoped holder
(`MixNoticeDismissal`), not a page field** (*Show stats* is a mainline
mid-quiz gesture and returning re-instantiates `Quiz`, so a field would
resurrect a dismissed notice); **keyed on the composition instance**
(`ReferenceEquals`, never `==` — the record's value equality would keep an
identically-drawn Restart dismissed; each Start/Restart builds a fresh
`MixComposition`, so the next run's notice shows again with **no reset call
site** on Home or Done to forget). The trigger is `Controller.Review is not
null` after the submit call, not the call itself: both mutators no-op under
the transition gate, and dismissing on a submit that scored nothing would
drop the notice with no answer given; the predicate also covers an off-list
play (a submitted answer with a review to read, just an unscored one).
**Skip is deliberately not a dismissal** — it moves past a problem without
answering it.

### `ShuffleOption` — the "Shuffle order" toggle holder

The per-app (`Scoped`, one-per-tab in WASM) holder for the **"Shuffle
order"** checkbox on `Home` — a sibling of `PickedProblemFolder` and
`AppliedFilter`, same lifetime, so the toggle survives in-app navigation.
Surface: `bool Enabled` (private setter) + `Set(bool)`. `Home.razor` writes
it on the checkbox's `@onchange`; the `ProblemSetSourceFactory` reads
`Enabled` at **invocation** time (`StartAsync`) — the same
read-live-at-Start discipline as `PickedProblemFolder`.
**Presentation-only, and off the start gate**: shuffling changes only the
*order* decisions are presented in, never which are *admitted*, so it is not
folded into `FilterConfig` and plays no part in `CanStart`; toggling never
dirties the gate — a checkbox has no half-edited intermediate state, so
every toggle is a complete, immediately valid choice with nothing to
"apply". **Disabled — never rewritten — under an active mix**: while the
committed mix has entries, presentation order belongs to the mix's own
Random-order setting, so Home disables the checkbox with a hint and the
factory suppresses the shuffle wrap; `Enabled` keeps the user's value
untouched, so clearing the mix restores the prior preference (pinned).
In-memory only, reset on full reload — same deferred-arc caveat as the
other holders.

### `QuizLiveMarker` — the reload-reset honesty marker

The per-app (`Scoped`, one-per-tab in WASM) service recording that a quiz is
**live** in this tab, backed by the browser's `sessionStorage` through
`IJSRuntime` — BgQuiz's first JS-interop *service*, encapsulated because it
has a lifecycle spread across two pages and a storage constraint worth
stating once. This is the **honesty slice of reload-resume, not resume
itself**: a full reload reboots the WASM runtime and silently discards all
quiz state; the marker is the one thing that survives, so a fresh boot that
finds it can *explain* the loss (real resume remains the deferred IndexedDB
arc). Surface: `MarkLiveAsync()` / `WasLiveAsync()` / `ClearAsync()`.
Lifecycle:

- **Set wherever a quiz becomes live**: `Home` on a successful Start —
  *after* the empty-result guard, so the no-match path never marks — **and**
  `Done` on *Restart*, which makes a quiz live again from the same pipeline
  (without the Restart writer, a reload during a restarted quiz falls back
  to the old silent reset — a one-click-wide hole in the very guarantee the
  marker exists to make).
- **`Home` reads** it on boot: `WasLiveAsync() && !Controller.HasStarted` ⇒
  show the polite reset notice, then `ClearAsync()` so it shows once. The
  `HasStarted` guard is the discriminator — a set marker with a *live*
  controller is in-app navigation back mid-quiz, **not** a reload, so no
  notice fires and the marker stays for a genuine later reload.
- **`Done` clears** it on honest completion — no reset to announce (a reload
  that killed a live quiz never reaches Done, which requires the surviving
  in-memory controller). *Restart* re-sets it immediately after, so the
  clear-then-re-set order across a Done→Restart round trip is deliberate.

`PageTests` pins the whole lifecycle. **Storage is `sessionStorage`,
deliberately — not `localStorage`** (see Pitfalls).

### Pages

- **`Home.razor`** — the setup page; wiring notes below, contracts in their
  owning sections.
  **Pick.** A **"Choose folder…"** button (`#pickProblemFolder`) above the
  `FilterPanel`, plus a hidden, always-rendered
  `<input type="file" webkitdirectory>` fallback the same button opens where
  File System Access is absent (a plain `<input>`, not `InputFile` — the JS
  module reads the FileList itself for `webkitRelativePath`; always in the
  DOM so the e2e suite can drive it directly). The whole pick runs behind
  `IFolderAccess` (§ Folder picking); the page never touches raw interop,
  the pick lands in `PickedProblemFolder`, and the bytes are parsed
  in-browser and never uploaded. The two no-folder outcomes each leave the
  holder clear and each show their own polite notice (`_cancelledPickNotice`,
  `_emptyFolderNotice`) — no pick ever returns the user to an unchanged page
  with no account of what happened; the capability drives the pick-time
  stats status notice (§ Folder picking). The cancelled notice is
  deliberately **cause-agnostic** (a dismissed picker and a declined
  view-files permission are indistinguishable — see `IFolderAccess`): it
  says only that no folder is held, non-accusatory toward a user who simply
  backed out. **Both mechanisms reach it, by different routes:** only
  `PickFolderAsync` reports cancellation as an *outcome*; a dismissed
  `webkitdirectory` picker fires no change event at all, so the fallback's
  dismissal is caught through the input's own `cancel` event (`@oncancel` →
  `HandleFallbackCancelled`) — wired when the setup reset moved to the
  click, where silence would leave the user on a screen the gesture had just
  emptied. That route is best-effort: where a browser never fires `cancel`
  the outcome degrades to silence — no wrong statement, only a missing one
  (Blazor's half is not in doubt; bUnit pins the binding, not the browser's
  delivery). The pick label renders straight from
  `PickedProblemFolder.Summary` (the SSOT), with a **Clear** affordance
  beside it bound to `EndCurrentSetupAsync`, the same handler the pick
  gesture runs; the summary then disappears and the folder half of the gate
  re-disables Start by construction. Clearing is safe mid-quiz and left
  unguarded on purpose — files are read only at Start time and the clear
  touches only the JS *picked* slot, so a running quiz keeps both its
  enumerator and its bound stats context (pinned).
  **Supported-browsers statement** (`FolderPickDisplay.SupportedBrowsers`),
  beside the pick button, **ungated by any capability probe**: where the pick
  isn't supported (phones — `webkitdirectory` is weak-to-absent) the button
  is a *dead entry point* and no code path ever runs to say why, so only a
  statement made *before* the gesture reaches that visitor — the readers it
  exists for are exactly the ones a probe excludes. Its gate is only "no
  folder held" (a completed pick proves the browser works; the caution is
  then stale noise). It lives in `FolderPickDisplay` because Help's *Before
  you start* lead renders the same sentence verbatim — the sole exception to
  that class's "Help's prose stays prose" rule, recorded on the constant:
  one sentence of fact, and the two surfaces must agree exactly. Its middle
  clause is hedged on purpose (a desktop non-Chromium browser is the working
  `BrowserUnsupported` rung, not a broken one; only the phone case is "may
  not work at all", and it says *may*).
  **Two-step permission guidance.** On an FS-Access-capable browser, an
  in-page note covers **both** easily-missed permission prompts as an
  ordered list naming what declining each costs: step 1 (view the selected
  folder's files) is required — decline and no folder is picked; step 2
  (save files into the folder) is optional — the quiz runs either way, but
  the lifetime record is not kept. Shown **from page load** (knowing what is
  coming is only useful *before* the gesture); its visibility window is
  `_fsAccessAvailable && !Folder.HasFiles` — continuous through an in-flight
  pick, hidden once a folder is held, back after **Clear**, and deliberately
  still there after a *cancelled* pick, which may be about to be retried.
  The gate is browser **capability**, not which mechanism served a pick:
  `_fsAccessAvailable` is an init-time `SupportsDirectoryPickerAsync`
  snapshot whose only consequence is whether advisory guidance renders; the
  per-gesture probe in `PickFolderAsync` remains the authoritative mechanism
  fork. Capability-gating keeps the note from promising prompts to fallback
  browsers, which raise none. Its lead-in promises no *number* of prompts
  (the readwrite request auto-denies on some Chromium versions, so only one
  prompt may appear — the list says what the browser may ask, not what it
  guarantees). It is **static, not stage-aware** — stage-swapping was
  *declined, not deferred*: it needs a Blazor render to land between two
  back-to-back prompts on WASM's single thread, and the two arrive seconds
  apart, so one read covers both. It **quotes no browser's prompt text**
  (see Pitfalls).
  **Progressive disclosure.** Everything downstream of the pick — the
  saved-filters panel (rendered *above* the `FilterPanel`, so load-then-
  refine reads top-down), the `FilterPanel`, the match-count line, the
  `MixPanel`, the shuffle checkbox, and Start — renders only once
  `Folder.HasFiles`. Hiding what has nothing to act on yet keeps the
  required first step unmistakable and makes the filter half of the gate
  true by construction (no panel to apply pre-pick). The `MixPanel` carries
  a *second* gate (Enabled picks only) and a `@key` on
  `Folder.PickGeneration` (§ MixPanel); its three callbacks land in
  `AppliedMix` (Apply → `Apply`, dirty → `MarkDirty`, restore → reconcile).
  The shuffle checkbox binds to the `ShuffleOption` holder —
  presentation-only, off the gate, rendered disabled (value untouched) while
  the committed mix owns order (§ ShuffleOption). The wrapping `@if` follows
  the enclosing `<fieldset>`'s whole-surface convention (no body re-indent).
  Start is gated on **three** conditions, all read from per-app scoped
  holders so the gate survives navigation:
  `CanStart => AppliedFilter.IsApplied && Folder.HasFiles && !AppliedMix.IsDirty`.
  **Match count.** On Apply, Home calls `Controller.CountMatchesAsync`
  (mechanism in § Pre-Start match count) and renders "N decisions match your
  filters" (the pre-mix pool). Home owns only display and lifecycle: a
  request id stamped per Apply discards a stale result landing after a newer
  Apply, and the count clears on any filter edit or new/cleared pick. **The
  count is filter-only, and says so when a mix is committed**: with
  `HasCommittedMix` a caveat renders *inside* the same `role="status"`
  region (count and qualification announced together) — the mix draws the
  quiz from these matches rather than presenting all of them, so the quiz
  **can** be much smaller. Hedged, not "will be": a capless *Everything
  else* mix can legitimately draw the whole pool. The count stays pool-only —
  a **pre-Start composition preview is deliberately not built** (composing
  against the lifetime stats is Start's work). `HasCommittedMix` is the
  single predicate behind both this caveat and (via `MixOwnsOrder`, kept as
  a named consequence so the shuffle markup says *why* it is disabled) the
  shuffle checkbox's disabled state. Help documents the count in its own
  prose — a shared constant is earned only when two surfaces render the same
  sentence, which these don't. The first count after a pick parses the
  corpus once (warming the cache so Start is then instant), so `_isCounting`
  folds into the same busy boundary as the transition gate, which also
  serializes the count against a Start.
  **Start.** Hands `AppliedFilter.Config` + `AppliedMix.Current` to
  `Controller.StartAsync` and checks the returned outcome **before** the
  empty-result `IsFinished` check (a refused start leaves prior state,
  including a stale `IsFinished` — see Pitfalls): `MixRequiresStats` renders
  the actionable refusal alert (`_mixRefused`, reason via
  `MixDisplay.RefusalReason`, the "Start without mix" per-run override, a
  pointer to the panel's Reset), and the mix-aware composed-to-zero wording
  rides the no-match branch. Under a no-stats pick none of that can fire:
  the mix panel is hidden and the pick reset `AppliedMix` to passthrough, so
  Start runs plain.
  **A pick ends the current setup — at the click.** `EndCurrentSetupAsync`
  is the single reset behind *both* gestures that end a setup (the pick
  gesture and the `Clear` affordance — they encode the same decision, so
  they share one spelling): folder holder + JS picked slot, saved filters,
  committed mix (`AppliedMix.Reset`), filter surface (`ResetFilterSurface`),
  and every pick-scoped notice and match count. Nothing selected against the
  previous corpus can be assumed to mean the same thing against the next
  one, so Start is always re-gated by a pick, never inherited across one
  (the bug this closed: a re-pick leaving the old filter applied, with Start
  live against a folder that filter had never been weighed against). It runs
  at the **start of the gesture**, before the mechanism fork — the screen is
  back at its initial no-folder state (guidance up) before the OS picker and
  permission prompts appear; a `StateHasChanged()` plus the awaited
  picked-slot interop lets that paint land first (the same
  paint-before-the-churn idiom the count uses). Settled consequences: a
  **cancelled pick loses the folder that was held** (no snapshot/restore —
  the gesture ended the setup whatever the picker then returned), and a
  successful pick re-mounts the `FilterPanel`, whose `localStorage` restore
  re-stages the persisted config as dirty on **every** pick — the accepted
  fresh-load behavior, now routine. `AppliedFilter` is reset here too: under
  one shared reset it is coupled to the *setup* (superseding the earlier
  "edit-coupled, not pick-coupled" ruling), and it stays edit-coupled as
  well via `HandleFiltersDirty` — two independent rules, not duplicates. The
  explicit `AppliedFilter.Clear()` is **not** redundant with `LoadConfig`'s
  dirty signal: a gesture made from the no-folder state has no panel to
  call, and a filter applied in an earlier setup would keep satisfying the
  gate. `LoadConfig(new FilterConfig())` stages without persisting, so the
  user's last-applied filter survives in the panel's `localStorage` — the
  same hands-off treatment `AppliedMix.Reset` gives the stored mix. The
  reset is deliberately **not** `@key`-based like `MixPanel`: re-mounting
  would re-stage the *persisted* config, the opposite of defaults (MixPanel
  is keyed to get exactly that effect); what the un-keyed panel still buys
  is the cancelled pick and the Clear, which end with no panel and no
  restore to re-stage anything. Two things are deliberately *not* reset:
  `ShuffleOption` (presentation-only preference) and the lifetime-stats
  slot, whose whole point is to *resume* when its folder is picked again.
  `PageTests` pins the reset, the at-the-click timing (sampled from inside
  the fake's picker), and the cancelled-re-pick loss.
  **Busy affordances.** The whole setup surface sits inside one
  `<fieldset disabled="@(Controller.IsBusy || _isCounting)">` — the native
  element disables every form control within, including the Apply buttons
  *inside* the imported `FilterPanel`/`MixPanel`, which expose no disabled
  parameter — and the page container carries `app-busy` (the
  `cursor: progress` rule in `app.css`) while either flag is set. Disabling
  the surface during the count also prevents a Start from racing its parse;
  the controller's gate yield lets the state paint before the Start churn,
  and Home needs no `StateChanged` subscription because its own suspended
  handlers trigger the re-renders. Subscribes to `OnFilterConfigChanged` →
  `AppliedFilter.Set` and `OnFilterDirty` → `AppliedFilter.Clear`.
  **Failure and outcome banners.** Pick failures (unexpected `JSException`,
  caps exceeded — `_pickError`) and start-time exceptions
  (`FilterConfig.Build()` validation, source construction — `_startError`)
  surface as banners instead of faulting the WASM app. A *successful* Start
  that leaves the controller already `IsFinished` (no showable problem)
  stays on `/` with a neutral no-match banner rather than navigating into a
  `0/0` `/quiz` → `/done` bounce — a post-Start check, not a pre-flight
  enumeration: `StartAsync` already advances to the first showable problem,
  so `IsFinished` immediately after it *is* the empty-result signal. Two
  indistinguishable causes flip it (zero filter matches; every match
  auto-skipped as a pass position), so the wording claims neither.
  `_noMatchNotice` is a sibling field to `_startError`, distinct because it
  reports an *outcome*, not a *failure*: `alert-warning` + polite
  `role="status"`, not `alert-danger` + assertive `role="alert"`. Both are
  genuinely per-visit state, so component fields (see Pitfalls); `PageTests`
  pins both flip paths and the over-trigger guard. A **third** per-visit
  notice (`_showReloadNotice`, polite) fires on a boot that finds the
  `QuizLiveMarker` set with no live controller (§ QuizLiveMarker). The page
  **footer** carries `AppInfo.Version` (in a `#appVersion` span) and, beside
  it, the beta feedback `mailto:` from the same `AppInfo` — see that
  section.
- **`Quiz.razor`** — mirrors the controller's three-state flow, branching on
  `Controller.Review`. **Answering** (`Review` null): routes the board region
  by `Current.Decision.IsCube` over
  `DiagramRequest.FromDecisionData(Current, DiagramMode.Problem)` — checker
  decisions to `BackgammonPlayEntry` (click-driven play assembly; strict on
  decision type, so the route must be exact — see Pitfalls), cube decisions
  to a **board-only** `BackgammonDiagram` (the cube answer is not entered on
  the board). Submit (a synchronous handler, since the controller's Submit
  no longer awaits) is gated on the relevant answer being held: a play via
  `OnPlayCompleted` → `_completedPlay`; a cube via the
  `BackgammonCubeActions` radios in the action row, whose `@bind-Value`
  keeps `_completedCube` current (re-fires on every change, so the user can
  revise before Submit). Both fields reset on every transition. The action
  row varies by kind: cube places the radios ahead of Submit / Skip and has
  no Undo (no partial-move state); checker keeps Undo last / Undo all
  (clearing the latched play, since the component does not notify on undo).
  **Both Undo buttons are disabled only while `Controller.IsBusy`** —
  deliberately *not* on `_playEntry` being assigned (see the `@ref`-timing
  pitfall). Both rows trail with a "Show stats" button in the `ms-auto`
  slot. **Review** (`Review` set): a read-only `BackgammonDiagram` in
  `DiagramMode.Solution` plus Continue / Redo / Show stats. The solution
  request is built with `DiagramRequest.Builder.From(Current.Position,
  Current.Decision, Current.Descriptive, DiagramMode.Solution)`, then the
  user's marks are overridden from `Review`: `UserPlayIndex` for a play
  (`-1` off-list draws no marker), or `UserDoubleError` / `UserTakeError`
  for a cube — `FromDecisionData` is **not** used here because it defaults
  those marks from the .xg-recorded player, not the quiz user. The review
  diagram's `OnDiceClicked` is bound to the same `ContinueAsync` handler as
  Continue, so clicking the dice advances exactly like Continue (safe under
  the transition gate). Redo falls back to the answering branch on the same
  problem; no explicit reset or `@key` is needed (see Pitfalls). Between the
  score panel and either action row sits an always-rendered, **fixed-height
  status strip** (`.status-strip`, `app.css`): a one-line legend slot and a
  two-line-clamped verdict band — a neutral prompt while answering; the
  legend (`* played · † your answer`) and outcome-coloured verdict at
  review. Because the strip's height is a designed constant, chrome height —
  and therefore the board's flex remainder under the desktop fold cap — is
  identical across states and questions, so the board never changes size
  when Submit flips into review. Board sizing rides BgDiag_Razor's
  bounded-height contract: the fold column hands `.board-container`'s
  definite post-flex height to the `BackgammonPlayEntry` wrapper
  (`height: 100%`); the cube-answering and review states render a bare
  `.bg-diagram` as a direct child of `.board-container`, contain-fit
  engaging with no wrapper glue (see Pitfalls). **Busy affordances:** every
  transition-driving button (Submit, Skip, Undo, Continue, Redo) disables on
  `Controller.IsBusy` and the container carries `app-busy` — the honest
  mirror of the gate, which would no-op the clicks anyway; "Show stats"
  stays enabled (navigation only). Subscribes to `Controller.StateChanged`
  **and** `QuizStatsStore.StatusChanged` in `OnInitialized`, unsubscribes
  from both in `Dispose`; redirects to `/done` when `IsFinished` flips.
  Above the board: the active-context stats notices (`LoadFailed` polite,
  `WriteFailed` assertive — the store subscription surfaces a mid-quiz write
  failure the moment it happens) and the mix notices from
  `Controller.LastComposition`, framed per § MixPanel's honest-notices list
  and gated on `!MixNotice.IsDismissed(comp)`. The `ScorePanel` carries
  "Problem N of M" from `Controller.ProblemNumber` / `ProblemCount`.
- **`Stats.razor`** — read-only mid-quiz stats view: the same `ScorePanel` /
  `ScoreBreakdown` pair `Done` shows, rendered against the live in-progress
  `QuizController` with honest mid-quiz wording ("Progress so far", not
  `Done`'s "Final"). Reachable only from `Quiz`'s "Show stats" button;
  "Back to quiz" returns to `/quiz`. Never calls Submit / Continue / Skip,
  so the round trip leaves `Current` / `Review` untouched — combined with
  the per-tab scoped controller, this gives "resume where you left off" for
  free. Direct nav with no quiz in progress bounces to `/`; with it already
  finished, to `/done` — the same guards `Quiz` applies to itself.
- **`Help.razor`** — end-user documentation. Structure (`PageTests` pins the
  full `h2` skeleton in order, so an edit cannot quietly drop or reorder a
  section): a **Before you start** prerequisites lead — a folder of the
  reader's own `.xg` / `.xgp` files is required and BgQuiz ships none; the
  supported browsers, rendered *verbatim* from
  `FolderPickDisplay.SupportedBrowsers` so this and Home's line beside the
  pick button cannot say different things; the two files BgQuiz writes,
  named from `QuizStatsFile.FileName` / `QuizFiltersFile.FileName`; and the
  nothing-leaves-your-machine stance. It leads because everything after it
  assumes it. Then the six beats of the flow (pick folder → filters →
  answering → scoring → review → stats/done), with **Save filters you use
  often** and **Weight the quiz by your lifetime stats** between the filters
  and answering beats in the order the user meets them on Home (the mix
  section forward-references *Lifetime stats* rather than moving after it —
  journey order is the page's rhythm, and forward references are its idiom),
  a **Making a checker play** section inside the answering beat, a
  **Lifetime stats** section, and then the semantics a user cannot discover
  by clicking around — the match count counts matching *decisions* and
  describes the filters alone (an applied mix draws from that pool, so the
  quiz can be much smaller); pass positions are auto-skipped and never
  shown; an off-list play counts as a skip, not a wrong answer; a cube
  position scores as two decisions; clicking the dice on the solution
  diagram advances like Continue; a full browser reload resets everything
  (in-app navigation does not, and the stats file survives in the user's own
  folder). It closes with **Send feedback**, rendering
  `AppInfo.FeedbackMailto` — the same link Home's footer carries, from the
  same value, so a report can never quote a build the tester isn't running.
  The checker-play section documents the one-click entry model, organized
  **by click target** — mirroring the component's own dispatch, so each
  bullet is exhaustive about one thing the user can click; its source of
  truth is BgDiag_Razor's `BackgammonPlayEntry` + BgMoveGen's
  `MoveEntryState`, whose doc comments are the contract this prose restates,
  and it deliberately says nothing about legal-click highlighting, which no
  shipped BgQuiz surface renders. Every documented constant renders from its
  SSOT — file caps from `PickedFileLimits`, filenames from `QuizStatsFile` /
  `QuizFiltersFile`, the browser rule from `FolderPickDisplay`, feedback +
  version from `AppInfo` — never as literals. Lives in the `.Client` (not a
  static host page) so a mid-quiz Help → Back round trip doesn't disturb the
  WASM runtime holding quiz state. Unlike `Stats` it **never redirects**:
  help is reachable from any state, including a cold visit or a bookmark;
  only the "Back to quiz" button is conditional, on the exact predicate
  `Stats` guards with (`HasStarted && !IsFinished`). No `StateChanged`
  subscription — nothing changes while the user reads. The host `NavMenu`'s
  Help link is the **only** entry point; `Quiz`'s action row deliberately
  gets no "?" button, because its fixed height is load-bearing for board
  sizing.
- **`Done.razor`** — final `ScorePanel` (Total) + `ScoreBreakdown`
  (four-way) + total problems shown + **Restart with same filters** /
  **Back to setup**. "Problems shown" is `PlayDecisions.Submitted +
  DoubleDecisions.Submitted + SkippedCount` — **not** `Total.Submitted`,
  which counts decisions and so double-counts each cube position (one Double
  + one Take). "Back to setup" is **navigation only** — the start-gate
  holders persist, so `Home` arrives armed with the same picks and filters;
  its label describes that navigation rather than promising a reset it
  doesn't perform (the former "Start over (new filters)" lied — Restart and
  Back-to-setup differ only in *where they land*, not in what they clear).
  Done participates in the `QuizLiveMarker` lifecycle both ways (clears on
  reaching it, re-sets on Restart — § QuizLiveMarker) and mirrors Quiz's
  active-context stats notices (`LoadFailed` status / `WriteFailed` alert) —
  a failure on the *final* Continue lands the user here without ever seeing
  the in-quiz notice; no subscription needed, the status cannot change while
  Done is shown. Restart re-attempts the stored mix and handles refusal like
  Home's Start: `MixRequiresStats` renders the refusal alert with **"Restart
  without mix"**, the summary underneath survives by the refusal's
  touches-no-state guarantee, and the marker stays cleared (nothing became
  live). Both Restart buttons disable on `Controller.IsBusy` + `app-busy`
  ("Back to setup" stays enabled — navigation only); the suspended Restart
  handler's own re-renders cover the flips.
- **`ScorePanel.razor`** — compact status strip used by both Quiz and Done.
  Renders the `Total` segment: Submitted / Correct (with %) / Skipped /
  average equity loss; optional Source name and Heading. Kept Total-only to
  avoid mid-quiz clutter. Optional `ProblemNumber` / `ProblemCount`
  parameters render the problem-position indicator ("Problem N of M", or
  "Problem N" when the total is unknowable) — opt-in per surface: Quiz
  passes the controller's stream position; Stats and Done omit it.
- **`ScoreBreakdown.razor`** — the four-way detailed evaluation, hosted on
  Done. A Play / Double / Take / Total table (Submitted · Correct (%) · Avg
  loss per row), reading the three `QuizScore` segments and the derived
  `Total`. Kept separate from `ScorePanel` rather than a `Detailed` flag so
  each component owns one layout.

### `AppInfo` — app-level identity, and the beta feedback link

`internal static class AppInfo` at the **client root** (not under `Quiz/` —
nothing here is about quizzing) owns the two facts the app states about
*itself*:

- **`Version`** — the running build's informational version (see the version
  footer section below). Hoisted off `Home.AppVersion` when Help became a
  second consumer: a page class is the wrong owner of app-level metadata the
  moment another page reaches into it.
- **`FeedbackAddress` / `FeedbackMailto`** — the beta mailbox and the
  `mailto:` href Home's footer and Help's *Send feedback* section both
  render, with `Version` pre-filled into the subject. A plain mailbox is
  deliberate: the app has no server and nothing to POST to, so any other
  channel would contradict the privacy stance Help states.

The subject is **percent-encoded** (`Uri.EscapeDataString`), not
interpolated raw: a non-shipping build's version carries a `+`
(`1.0.10+gabc1234`), and a bare `+` in a URI query is decoded as a space by
mail clients that treat the query as form data — the commit the tester is
reporting against would silently arrive mangled. `PageTests` pins the
escaped form; the e2e suite rebuilds the expected href from its own literals
against the version read off the rendered footer, so app and pin stay
independent.

### The version footer (`<Version>` + `StampGitShaSuffix`)

Home's `v{version}` footer renders `AppInfo.Version`, read at runtime from
the `.Client` assembly's `AssemblyInformationalVersionAttribute`.
`<Version>` in `BgQuiz_Blazor.Client.csproj` is the sole source of the
release number — no literal anywhere repeats it. The `#appVersion` span is
the e2e handle for reading the built version off a running artifact; the
feedback link sits beside it precisely so the version a tester reads and the
version their mail quotes cannot be different builds.

Build metadata is appended to that number, never substituted for it. The
`StampShortGitShaOnInformationalVersion` target (same csproj) suffixes
`+g<shortsha>` — 7 chars, the short form the umbrella's docs quote — so a
running build names its commit. It is **on by default**; the shipping
publish is the one caller that opts out:

```
dotnet publish BgQuiz_Blazor/BgQuiz_Blazor.csproj -c Release -p:StampGitShaSuffix=false
```

Default-on is the point. The deploy recipe hands the user a Release publish
built at the current pointer for acceptance *before* the `<Version>` bump,
so that candidate would otherwise render the previous release number — a
build claiming to be something it isn't; `Configuration` cannot be the
discriminator, since candidate and shipped artifact are both Release. Two
mechanics worth knowing: the SDK's own
`IncludeSourceRevisionInInformationalVersion` stays `false` (it appends the
*full* 40-char sha and would stack both suffixes), and the stamp is doubly
guarded (`SourceControlInformationFeatureSupported`, non-empty
`SourceRevisionId`) because `Substring(0, 7)` on an empty property is a hard
build failure — a build outside a git working copy degrades to the clean
SemVer. Read a built assembly back with:

```
pwsh -c "[System.Diagnostics.FileVersionInfo]::GetVersionInfo('BgQuiz_Blazor.Tests/bin/Debug/net10.0/BgQuiz_Blazor.Client.dll').ProductVersion"
```

`PageTests` pins both halves without hardcoding a version: the footer equals the
assembly's informational version whatever its shape, and that version's leading
SemVer equals `AssemblyVersion` (the same `<Version>`, but immune to build
metadata) with any suffix matching `^\+g[0-9a-f]{7}$`. The suffix assertion is
gated on presence, so the suite passes for a clean-release build too.

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
skips the static-prerender pass: the picked-file holder and quiz state live
in WASM-runtime memory that doesn't exist during a server prerender, so
prerender would render an empty first frame and double-run `OnInitialized`.

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
<publish dir>`, resolves the OS-assigned port from Kestrel's listening line,
probes readiness, and tears down on dispose. Not `dotnet run` and not
`TestServer` — those put a different layer under test. The `--contentRoot`
is load-bearing: without it `MapStaticAssets` resolves against the wrong web
root and serves 0-byte framework assets (unstyled page, WASM never boots).
The host's `BgQuiz_Blazor.dll` is the entry point.

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
`AddInitScriptAsync` *before* the page is created): Playwright cannot drive
the native directory picker or its permission prompts, so the base injects a
fake `window.showDirectoryPicker` — a scripted directory handle over the
real fixture's bytes, `getFileHandle`, `createWritable` capturing writes,
scripted permissions. The faking stops at the browser-API boundary: the app
ships **no test seams**, and everything from the app's own `folderAccess.js`
inward runs for real — if the module's use of the FS-Access surface drifts
from what the fake mirrors, the scenarios fail loudly. Per-scenario
variation (corrupt stats file, denied permission) is a page-level init
script overriding the fake's config object; a mid-test `EvaluateAsync` can
mutate it between quizzes (the app re-reads the stats file at every Start's
re-bind). Three suites ride the fake. `StatsPersistenceTests` pins: one fold
⇒ one captured write with `schemaVersion` 1, one decision record, a
cube-as-two-decisions tally, indented; corrupt file ⇒ polite notice + **zero
writes**; denied ⇒ denied notice + zero writes; and the fallback pick's
"can't save stats" notice. The stats filename and wire property names are
deliberately hardcoded in the suite — the consumer-side pin of those
contracts (the e2e project references no app assembly by design).
`MixWeightingTests` drives the weighted path: a 100%-never-seen mix built
through the panel UI runs to Done (one write captured — the weighted run
still records), and the composed-to-zero scenario **feeds the app's own
captured write back** as the pre-existing stats file (no hand-crafted wire
format), then starts weighted and asserts the mix-aware zero notice with no
0/0 bounce. `MixRefusalTests` pins the refusal at its one reachable path: an
`Enabled` pick whose existing stats file is corrupt — capability peek
passes, the bind reads no document, refusal at stage 2 → "Start without mix"
override → Done. Don't move it back to the fallback rung: the mix panel is
offered only for an `Enabled` pick and every pick resets the committed mix,
so no mix can be committed there any more.

**Beta-onboarding surfaces.** `BetaOnboardingTests` covers the two things
only a real request against the publish output can see: `robots.txt` (a
**host** static file, not a Blazor route — bUnit is structurally blind to
it; status *and* body are asserted, since a misplaced copy 404s in a styled
NotFound dressing), and the feedback `mailto:` checked against the version
the **built** assembly reports off the `#appVersion` footer — which is what
proves the `+g<shortsha>` suffix survives into the subject without the suite
ever referencing an app assembly.

**Fail loud, never skip.** Every broken precondition (missing browsers,
fixture, publish, port-bind) fails the suite with an actionable message —
the never-skip ruling lives in Pitfalls.

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
plain-C# client type (`QuizController` + `QuizStartOutcome`, the scoped
holders `PickedProblemFolder` / `AppliedFilter` / `AppliedMix` /
`ShuffleOption` / `QuizLiveMarker` / `MixNoticeDismissal`, `PickedFile`,
`IFolderAccess` / `JsFolderAccess` (+ its wire DTOs),
`StatsSaveCapability`, `FolderPickOutcome`, `QuizStatsFile` /
`QuizFiltersFile`, `IDecisionStatsSink` / `QuizStatsStore` /
`QuizStatsStatus`, `SavedFiltersStore` / `SavedFiltersStatus`,
`MixDisplay`, `CubeActionDisplay`, `FolderPickDisplay`, `AppInfo`,
`WasmUploadedProblemSetSource` / `CachedProblemSetSource`, `ProblemReview`,
and the `ProblemSetSourceFactory` delegate) is `internal`, reachable by the
test project only through the `InternalsVisibleTo` grant. The only `public` types are the Razor components — the
framework requires them public (see Pitfalls). The externally visible surface is
the route map:

- `/` → `Home` — filter selection + Start
- `/quiz` → `Quiz` — active problem (redirects to `/` if no quiz, `/done` if finished)
- `/stats` → `Stats` — read-only mid-quiz stats (redirects to `/` if no quiz, `/done` if finished)
- `/done` → `Done` — final summary (redirects to `/` if no quiz)
- `/help` → `Help` — end-user documentation (never redirects; linked from the nav menu)
- Default error page → `Error.razor`
- `/not-found` → `NotFound.razor` — the 404 page, and a **mapped route in
  its own right** (requesting it directly is a 200). Reached two ways, both
  needed: `Routes.razor`'s `NotFoundPage` for client-side navigation,
  `UseStatusCodePagesWithReExecute("/not-found")` for server-side unmatched
  paths (see Pitfalls); the re-execute preserves the 404 status.

## Pitfalls

- **The e2e suite is the smoke gate AGENTS.md mandates — pointer bumps run
  it, and it must never learn to skip.** It sees what bUnit and the wire
  tests structurally cannot (see Architecture § The e2e smoke gate). Two
  standing rules: (1) never convert a broken precondition — missing
  browsers, missing fixture, failed publish — into a `Skip`; a skipped smoke
  reads as green, the defect class the gate was built to kill. (2) Its
  `Fixtures/` are committed copies; the umbrella's `TestData/FixtureFiles/`
  stays append-only and untouched.
- **Most `FilterPanel` controls are behind its disclosure — a test that
  drives one must expand first.** The panel keeps the error-range section
  always visible and renders its other eight sections *only while
  expanded* — absent from the DOM when collapsed, not styled away — so a
  selector for any of them silently finds nothing. Both suites go through
  their own one-line helper (`ExpandMoreFiltersAsync`) that clicks the
  panel's real `#moreFiltersToggle` button, never a JS or field poke;
  toggling raises no `OnFilterDirty`, so it never disturbs an applied/dirty
  expectation. Error-range edits, Apply, and Clear filters need no
  expansion. Two related traps: address the panel in an ordering assertion
  by an *always-rendered* element (`#moreFiltersToggle`), not by
  `#positionPattern`; and Playwright's accessible-name match is a substring,
  so the panel's `Clear filters` button collides with Home's `Clear` — that
  locator needs `Exact = true`.
- **Never gate a control's `disabled` on an `@ref` field.** Blazor assigns a
  component `@ref` *after* the render that creates it, so any markup reading
  it renders one pass stale — the first render of a branch always sees
  `null`. Both quiz Undo buttons carried `disabled="@(_playEntry is null ||
  …)"` and were dead for exactly the window they exist to serve: nothing
  re-renders `Quiz` during click-by-click play assembly, so they stayed
  disabled until the play completed and enabled only once Undo was
  pointless. It read as intermittent because Blazor never nulls a component
  ref on unmount (from the second play problem on, the stale-but-non-null
  ref rendered them enabled), and it was first observed under a write-denied
  run that had nothing to do with it — **check `@ref` timing before
  believing a capability correlation.** The fix is to drop the term: the
  enclosing branch already guarantees the component is rendered, and a click
  can only land after the ref is assigned (pinned).
  Enabled-*iff*-undoable would be more honest but needs two producer
  surfaces `BackgammonPlayEntry` does not expose; that is booked
  umbrella-side against BgDiag_Razor, not worked around here.
- **State resets on full reload, not on in-app navigation.** "Scoped" in
  WASM is one instance per loaded app (one tab), so the controller and the
  holders survive `/` ↔ `/quiz` ↔ `/done` navigation, but a full browser
  reload re-boots the runtime and loses everything (though not the stats
  *file*, which lives on disk and resumes on re-pick). Reload-survival is a
  deferred arc — don't assume reload resumes. Anything that *should* survive
  navigation belongs in a scoped holder, not a component field — the two
  halves of Home's start gate were moved off transient fields for exactly
  this reason. (Genuinely per-visit page state — e.g. Home's `_startError`
  banner — correctly stays a component field and resets on navigate-back.)
  The one thing that *does* survive a reload is the `QuizLiveMarker`
  (`sessionStorage`), deliberately — see the next bullet.
- **The `QuizLiveMarker` is `sessionStorage`, not `localStorage` — don't
  "upgrade" it.** The marker records "a quiz is live in *this tab*" so a
  reload can be acknowledged on the next boot. `sessionStorage` is per-tab:
  it survives a reload but is invisible to other tabs and dies with the
  tab — exactly those semantics. `localStorage` is shared across every tab
  of the origin, so a quiz live in tab A would make a freshly-opened tab B
  falsely announce "your quiz was reset" on *its* first boot. It looks like
  the "bigger, more durable" store; it is the wrong one here. (The real
  reload-*resume* arc will need durable storage — IndexedDB — but that is a
  different concern from this per-tab liveness flag.) The controller-side
  `HasStarted` guard in `Home.OnInitializedAsync` is the complementary
  defence, suppressing the notice on in-app navigation back mid-quiz, where
  the marker is legitimately set but no reload happened.
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
  fires never firing. When wiring an event from an RCL-imported component,
  verify the parameter name against the source. A bUnit regression test
  guards the FilterPanel wiring.
- **Client plain-C# types are `internal`; only Razor components are
  `public`** (the list is in Public API). Don't widen one to `public`: the
  tests already see it through the `InternalsVisibleTo` grant, and a page
  reaches it through `@inject`, which binds a service **by type from DI**
  and generates a **private** property — so a DI-injected type never lands
  in a public signature. The one move that *forces* a client type back to
  `public` is putting it in a public component's `[Parameter]` (or any other
  public member signature) — that trips **CS0053** (inconsistent
  accessibility); the fix is to keep the crossing type a library/wire type,
  not to re-widen the app type. The pages, in turn, **cannot** go internal:
  the router discovers routable components by scanning the assembly's
  *public* (`ExportedTypes`) surface, so that boundary is
  framework-required, not a missed narrowing.
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
  the iterator throws `ArgumentException` on — lazily, mid-enumeration, not
  at construction. Both of `folderAccess.js`'s pick paths preserve the
  browser's extension-bearing entry names precisely for this (pinned on both
  sides). Start-time exceptions (this, plus `FilterConfig.Build()`
  validation) surface on `Controller.StartAsync` and `Home.razor` shows them
  as a banner rather than faulting the app.
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
  start, the per-run "Start/Restart without mix" override, a corrupt restore,
  and the pick/Clear resets of `AppliedMix` all leave it untouched (corrupt just
  yields a blank *builder*; the resets touch only the in-memory holder). The
  one sanctioned overwrite is the panel's own Apply/Reset — an explicit user
  gesture. Same spirit as the never-overwrite-unreadable-stats rule below.
- **The mix restore reconciles; it must never adopt.** The panel's first-render
  restore raises `OnMixRestored`, and Home marks the mix dirty **only** when the
  restore is non-passthrough *and* `AppliedMix.Current` is still passthrough.
  Both halves are load-bearing. Drop the second and every navigate-back re-gates
  a mix the user already applied. Make it adopt (commit the restored mix
  straight into the holder) and a persisted mix silently becomes committed
  with no user gesture, which is what let a stats-less pick inherit
  one. And it can't simply raise nothing the way `FilterPanel`'s restore does:
  the filter's default already blocks Start (`IsApplied` false), whereas the
  mix's passthrough default does *not*, so a silent restore would leave rows
  showing while Start ran passthrough.
- **`MixPanel`'s `@key` on `PickGeneration` is load-bearing — don't drop it.**
  An Enabled→Enabled re-pick leaves both the capability gate and `HasFiles`
  true, so without the key the panel never re-mounts, its first-render restore
  never re-fires, and it keeps showing the previous pick's rows while
  `EndCurrentSetupAsync` has just reset `AppliedMix` to passthrough+clean —
  Start un-gated over a displayed mix, the exact divergence the dirty machinery
  exists to prevent. The key forces the re-mount, and the reconcile then
  re-offers the persisted config as dirty.
- **Don't collapse the FS-Access pick to a single prompt.**
  `showDirectoryPicker({ mode: 'readwrite' })` looks like a free UX win (one
  prompt instead of the pick-then-`requestPermission` pair) and reads as an
  equivalent contract. It is not: **tried and reverted 2026-07-24** — in
  real Chrome, declining that single readwrite prompt aborts the *whole*
  pick (`AbortError` ⇒ `cancelled`, no folder and no read handle), which
  destroys the `PermissionDenied` rung — decline write, file list still
  loads, quiz runs without stats — a deliberate degrade, not an accident.
  The two-prompt flow is retained deliberately; the full rationale lives in
  the comment above `pickDirectory`. (The underlying concern — the prompt
  being missed in a busy UI — is already met by progressive disclosure plus
  the two-step guidance, so a collapse buys nothing.)
- **Never quote a browser's permission-prompt text — describe the grant.**
  Chrome and Edge word both File System Access prompts differently, and Edge
  interpolates the picked folder's *own name* into the write prompt, so any
  string claiming to be what the user will read is wrong somewhere. Home's
  two-step guidance therefore names the grant being asked for, hedged ("your
  browser will ask…"), and asserts no exact prompt string — in markup, comments,
  or docs. `FolderPickDisplay` carries the rule. It also asserts nothing about
  *how many* prompts appear, or that a missing grant was a user decision — both
  are false on the auto-deny path.
- **Don't pin a user-visible string wider than it needs to be.** A test pin
  should be the *minimum discriminating substring*: long enough to prove the
  right surface rendered, short enough that a copy polish doesn't break it
  spuriously. The `PermissionDenied` e2e pin is
  `"which problems give you difficulty"` — the distinctive content — not the
  whole sentence, whose lead-in buys no discrimination. Related: once a phrase
  is single-sourced into `FolderPickDisplay`, **two** surfaces render it
  verbatim, so a whole-markup `Contains` on it no longer proves *which* one —
  scope such assertions to the element (bUnit `Find(...).TextContent`) or pair
  them with a surface-specific lead-in.
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
  unconditionally resets the in-memory document and reloads from disk. If
  the *prior* quiz sat in `WriteFailed` with folds living only in memory,
  those folds are dropped by the refused start even though no new quiz
  begins — the same in-memory loss any Start/Restart always caused; the file
  itself is never overwritten on the LoadFailed path. Rare×rare and
  accepted: a skip-the-reload guard would need JS handle-identity interop.
  Don't move the bind back after the source build to "fix" it — the wrap
  decision needs the bound context.
- **`QuizMix` entry order is semantic — preserve it everywhere.** Earlier
  entries win contested overlap (producer contract), so the mix panel's rows,
  the persisted JSON, the restore hydration, and the mix notices'
  composition summary and per-entry lines must all keep declared order.
  Reordering is a real edit (dirties the gate); `MixPanelTests` pins order
  surviving Apply.
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
- **Saved filters read/write the *picked* slot, not the active one.** The
  saved-filters document is a setup-time concern on the folder being
  configured, so `SavedFiltersStore` goes through
  `readPickedFile`/`writePickedFile`, never the active-slot
  `readStatsFile`/`writeStatsFile` — the same isolation invariant as stats,
  from the other side: a mid-quiz re-pick reloads the saved-filters context
  off the *new* picked folder while the running quiz keeps recording through
  its *active* handle, untouched. Don't "unify" the two file ops onto one
  slot — the picked-vs-active split is the whole point, the filters ops must
  not require a `promoteToActive` (they run before any quiz binds), and the
  two slots can legitimately be different folders when a quiz is live over
  an earlier pick.
- **Never write over a saved-filters file that failed to read or parse.** A
  non-null read that `NamedFilterCollection.TryFromJson` rejects (corrupt,
  foreign, newer-schema), *or* a read that throws (`JSException` — an FS
  error, or read genuinely withheld under `PermissionDenied`), both flip
  `SavedFiltersStore` to `LoadFailed`: terminal for that pick, **zero
  writes**, file preserved untouched. The stats store's `LoadFailed`
  guarantee, filters edition — and what keeps load-only under
  `PermissionDenied` from being load-bearing: if the read-grant assumption
  is ever false in some browser, the store degrades to the notice instead of
  the panel, never worse. Keep the store and page tests that pin the
  zero-writes half.
- **Don't gate the saved-filters degrade notices on the panel's empty rule.**
  Home carries two predicates on purpose: `SavedFiltersApplicable` (panel
  offering — hides a read-only section with nothing to load) and
  `SavedFiltersContextApplicable` (degrade reporting). Collapsing them back into
  one looks like tidy de-duplication and is a correctness bug: `LoadFailed`
  always leaves the collection empty, so the panel's "hide when read-only and
  empty" rule would suppress the "couldn't be read, left untouched"
  data-protection notice **every time it fires** — exactly when the user most
  needs it. Emptiness is irrelevant to whether a failure gets reported.
- **The parse cache must stay unfiltered, holder-homed, and
  generation-guarded.** `PickedProblemFolder.ParsedDecisions` is the parse
  of the *whole* pick with no filters — caching a filtered parse would
  silently serve one filter config's subset to every later Start. Its
  invalidation is `Set`/`Clear` nulling it (cache lifecycle = pick
  lifecycle); don't move the slot off the holder and re-create the
  forgotten-invalidation-wiring hazard, and don't drop `StoreParsed`'s
  generation check — the pick gesture is async, so a re-pick can complete
  inside a Start's await points, and an unguarded store would install the
  *old* pick's parse as the *new* pick's cache. Post-hoc `Matches` over the
  cache is equivalent to filter-during-parse only because the iterator's
  skip/advance votes are contractually pure early-exit hints (the contract
  lives on `IDecisionFilter`/`IMatchFilter` in XgFilter_Lib); a filter whose
  votes cut rows its `Matches` would admit breaks that contract and this
  cache.
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
  branch swap already does it.** The component suppresses its own internal
  reset when the incoming `Request` describes the same problem as last time,
  so it's tempting to assume Redo (which returns to that exact problem)
  needs an explicit reset or a changing `@key` to work around the
  suppression. It doesn't: the entry lives in the `else` branch of the
  review `@if`, and Submit already unmounted it entirely when the page
  swapped into the review branch — by the time Redo swaps back, the entry
  did not exist in the immediately prior render, so Blazor constructs a
  fresh instance unconditionally. Verified, not just reasoned about: a
  temporary redo-generation `@key` was added and the suite stayed green with
  it removed. Don't reintroduce the key defensively; if a future refactor
  keeps the entry mounted across review (e.g. overlaying the solution
  instead of swapping branches), *that's* the point to re-examine. The cube
  answer needs none of this: `BackgammonCubeActions` is strictly controlled
  off `_completedCube`, which nulls on every transition.
- **The status strip must stay fixed-height, and the board-sizing glue must
  stay retired.** The strip's whole purpose is state-invariant chrome: equal
  chrome height ⇒ equal board flex remainder ⇒ no answering↔review
  board-size jump. Sizing it by content (`min-height`, auto height)
  reintroduces the per-question jitter it was built to remove — long content
  clamps instead (legend one line, verdict two). On the board side, sizing
  belongs to BgDiag_Razor's bounded-height contract (bound the
  `BackgammonPlayEntry` wrapper with a real height; the producer's
  `bg-board-slot` and `.bg-diagram` contain-fit default do the rest) —
  re-adding consumer `max-height` glue, `display: contents` on a wrapper, or
  styles inside `.bg-board-slot` breaks the contract (see the producer's
  pitfalls; `AppCss_RetiredBoundedHeightGlue_StaysGone` pins this). The
  cube-answering board is a bare `.bg-diagram` directly under
  `.board-container`, like review — the cube radios live in the action
  row — so all three states size the board identically under the fold cap;
  unifying it any other way would re-encode producer chrome height in the
  consumer, the magic-constant pattern this arc removed.
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
- **`/Error` shows `BgQuiz`, not `Error` — deliberately.** `Error.razor` is
  server-side, statically rendered, so its `<PageTitle>` can only reach a
  static-pass outlet — and the outlet is now interactive (above), so its
  title falls back to the static `<title>`. Verified, not assumed: `/Error`
  renders its heading with `document.title === "BgQuiz"` (`/not-found` never
  declared a `<PageTitle>`, so nothing regressed there). An accepted trade —
  a terminal page nobody navigates to on purpose, in exchange for correct
  titles on the five pages people use. **Do not "fix" it** by reverting the
  outlet to a bare one; that restores `<title>Error</title>` on `/Error` and
  silently re-breaks all five real pages. The title is not the whole cost:
  the render-moded `HeadOutlet` is a WASM **root component on every page**,
  so both server-rendered terminal pages boot the ~19.5 MB payload to
  accomplish nothing (they render and read correctly before the boot
  completes). Accepted for the same reason; if a terminal page ever becomes
  heavily linked, the fix is a narrower home for the outlet, **not**
  un-render-moding it.
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
  purpose.** `UseStatusCodePagesWithReExecute` intercepts every bodyless
  4xx/5xx, so `/_framework/no-such-asset.js` comes back 404 with the
  NotFound page's `text/html` body rather than an empty one. Not a
  misrepresentation: on a 4xx the body is an *error document* (RFC 9110);
  the 404 status every consumer keys on (Blazor's boot loader included) is
  preserved, and the body is inert. Assets that *exist* are untouched — the
  middleware only engages on an error response with no body. **Reordering
  cannot fix the asset case**: a missing static file is not answered by
  `UseStaticFiles`/`MapStaticAssets` — those call `next()` and the 404 is
  produced downstream by routing, which the status-code-pages middleware
  wraps wherever it sits. Don't try. **The trigger for revisiting**: when
  server-side JSON API endpoints arrive, a typed client's `ReadFromJsonAsync`
  against a 404 throws a confusing `JsonException` instead of surfacing the
  status — at that point the only defensible discriminator is content
  negotiation on the `Accept` header; a path-prefix or extension sniff
  duplicates routing knowledge inside middleware and still misses cases like
  `/no-such.json`.
- **There are two `wwwroot`s — a served static file belongs to the host's.**
  `BgQuiz_Blazor/wwwroot` is what the host serves (`app.css`, `favicon.png`,
  `lib/`, `robots.txt`); `BgQuiz_Blazor.Client/wwwroot` holds `js/` and reaches
  the browser only as the client's static *web assets*, under its own path. A
  file that must answer at a fixed URL (`/robots.txt`, and anything else a
  crawler, a browser, or a platform probe asks for by name) goes in the host's,
  and the mistake is silent in every layer but one: it still builds, still
  publishes, and 404s at runtime — where the re-execute above dresses the 404 in
  the styled NotFound page, so it doesn't even look bare. `BetaOnboardingTests`
  is the only thing that catches it.

## Subproject-internal next steps

- **Phase 2+ design.** Stats-weighted composition now ships (the `MixPanel` →
  `QuizMix` → `MixedProblemSetSource` pipeline over the lifetime record).
  Still open from the phase-2 sketch: an in-session history model /
  re-queue-on-wrong (distinct from lifetime weighting), the Done-page
  retrospective below, and the three two-agent modes (user-vs-user,
  user-vs-bot, bot-vs-bot tournament).
- **Reload-resume (persistence).** A full browser reload re-boots the WASM
  runtime and loses the picked folder and quiz progress. Surviving it needs
  the picked bytes + progress persisted client-side (IndexedDB —
  `localStorage` is too small for buffered `.xg` bytes); a deferred arc of
  its own, distinct from the stats file (which lives on the user's disk and
  survives via re-pick). Until then, reload-reset is the intended default.
- **Mobile assessment — layout and folder picking.** Mobile layout has never
  been assessed (the beta-readiness live drive was text/DOM-only). The
  quiz-stats arc raised the stakes: `webkitdirectory` is weak-to-absent on
  mobile browsers, so on phones the fallback pick — and with it the whole
  app's pick gesture — may not work at all. Assess the pick alongside the
  layout pass; neither is solved here.
- **Done-page retrospective.** Per-problem review ships *in-quiz*; what's
  missing is a *post-quiz* retrospective on Done — the four-way
  `ScoreBreakdown` reports only aggregates, with no way to revisit
  individual problems after finishing. A scrollable list of the `History` /
  `CubeHistory` entries (each re-rendering its solution diagram) would close
  the loop.
- **Evaluate `RunAOTCompilation` for the Client publish.** The deployed WASM
  runs the Mono interpreter — measured ~8× native on the start-path parse
  (2026-07-20) — and AOT would cut the residual *first*-Start parse cost.
  Costs to measure before committing: publish time, payload size, and the
  umbrella `infra/` zip-deploy recipe re-verified.
- **e2e Too-Good coverage gap.** No e2e exercises a Too-Good cube answer end
  to end — the committed cube fixture is (NoDouble, Take); a bUnit case
  covers the verdict + scoring path meanwhile. Close by sourcing a Too-Good
  single-decision `.xgp` (`nd ≥ 1.0 && dt ≥ 1.0`) from the corpus via
  ExtractFromXgToCsv's slice export — **anonymize ON**, the fixture commits
  to a public repo — into `E2eTests/Fixtures/`, plus a `QuizFlowTests` case
  (banner "Too Good" + `No Double: … · Pass: …` verdict → Done). Synthesis
  was rejected: the producer's clean writer surface is unanalyzed by design.
  Surfaced 2026-07-22.
