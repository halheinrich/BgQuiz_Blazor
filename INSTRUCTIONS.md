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
  (the shuffle decorator the source factory wraps the picked set in; the app
  uses the **unseeded** ctor — the seeded one is test-only),
  `DistinctPositionProblemSetSource` (the content-identity dedupe decorator at
  the bottom of the composition — one item per distinct `ProblemKey`, first
  occurrence surviving; keyless items pass through unmerged), `SubmittedPlay`,
  `SubmittedCubeAction`, `QuizScore` (segmented: `PlayDecisions` /
  `DoubleDecisions` / `TakeDecisions` + derived `Total`), the stats-weighted
  composition surface — `QuizCategory`/`QuizCategoryKind`,
  `QuizMix`/`QuizMixEntry` (the versioned strict-JSON mix config;
  `ToJson`/`FromJson`/`TryFromJson` is the localStorage trio),
  `MixedProblemSetSource` (the composing decorator the controller wires for a
  non-blank mix) + `MixComposition` telemetry — `AnswerTypeDistribution` (the
  answer-type fold behind Home's pre-Start summary), and the lifetime-stats
  model `ProblemStats` / `ProblemStatsDocument` (immutable, keyed by
  `ProblemKey`; `doc = doc.Plus(submission, TimeProvider)`; bundled type-level
  JSON converter — deserializes with no registration, any bad load throws
  `JsonException`, and a genuine schema-v1 document throws the
  `RetiredStatsSchemaException` subtype the store retires on).
  The controller talks to the source through `IProblemSetSource` and scores
  via `QuizScore.Plus`; the stats store folds finalized submissions via the
  document's `Plus`. Producer behavior — the per-enumeration reshuffle, the
  fold contracts — lives in BgGame_Lib's own INSTRUCTIONS.md.
- **BgDataTypes_Lib** — data types. `BgDecisionData`, `Play`,
  `PlayCandidate`, `BoardState`, `CubeDecisionPair`, `CubeAction`,
  `ProblemKey` (content identity; `TryDerive` is the one factory — the
  controller stamps every submission through it, and `false` is the no-key
  rung, never a guess). The matcher
  compares the submitted `Play` against each `PlayCandidate.Play` by canonical
  `Play` equality; cube scoring reads `DecisionData`'s `BestDoublerAction` /
  `BestTakerAction` / `DoublerActionError` / `TakerActionError`.
- **BgMoveGen** — `MoveGenerator.GeneratePlays`, used by the controller's
  pass-position auto-skip detection.
- **BgDiag_Razor** — `BackgammonPlayEntry` (click-driven play assembly),
  `BackgammonCubeActions` (a board-free four-radio group for the cube answer,
  on the `@bind-Value` convention) + the underlying `BackgammonDiagram`
  (read-only board view, used for both the review diagram and the
  cube-answering board).
- **BackgammonDiagram_Lib** — `DiagramRequest` + `DiagramOptions`. The
  answering view uses `DiagramRequest.FromDecisionData(…, DiagramMode.Problem)`
  (Problem mode blanks the analysis panel, so it never leaks the answer); the
  review view uses `DiagramRequest.Builder.From(…, DiagramMode.Solution)` and
  overrides the user marks (§ Pages → Quiz). `DiagramOptions.Aspect` carries the
  canvas preset: the producer's default everywhere except maximized answering,
  which asks for **`AspectPreset.BoardOnly`** — the panel allocation **and the
  title strip** dropped, so the canvas is the board proper alone (the strip
  joined the crop at `SPEC-quiz-view.md` §4's 2026-08-13 amendment, issue
  `halheinrich/backgammon#98`; its texts are ruled lost while answering
  maximized and restored at review). It is **Problem-mode only**: a Solution request carrying it throws `ArgumentException` from
  `RenderSvg` and `GetHitRegions` alike, which `Quiz.BoardOptions`' derivation
  prevents structurally rather than by a check. Direct `<ProjectReference>` —
  the page calls the factory by name, so the dependency is explicit rather than
  riding BgDiag_Razor's transitive surface. Only the **native-free core** is
  referenced (see Pitfalls).
- **XgFilter_Lib** — `DecisionFilterSet`, `FilterConfig`,
  `DecisionTypeFilter` / `DecisionTypeOption` (materialized from the user's
  decision-type choice; the controller adds no filter of its own).
- **XgFilter_Razor** — `FilterSurface.razor`, the one composite hosted on `/`:
  it owns `FilterPanel` + `SavedFiltersPanel` (both
  `XgFilter_Razor.Components.Internal` — banned from host use, host tests
  included) and the whole filter interaction lifecycle — load→stage,
  save/save-as/delete mediation, the applied-state mediation onto the shared
  `AppliedFilter` holder, the restored-selection notice, the saved-filters
  degrade/refusal notices with producer-owned copy, and the source-change rule
  over the host-minted `FilterSourceToken`. Also the non-visual model this app
  binds: `AppliedFilter` (the start-gate holder, registered Scoped here),
  `FilterRestoreNotice` (the restored-selection notice's state — registered
  Scoped and bound, nothing else: every member that moves it is
  producer-internal), `FilterSourceToken` (minted once, in
  `Home.CurrentFilterSource`, `FromGeneration(PickGeneration)`),
  `IFilterDocumentStorage` + `FilterStorageException` (the storage seam this
  app adapts over the folder library), and `SavedFiltersDocument`
  (`FileName` = `xg-filters.json` / `LegacyFileName` = `bgquiz-filters.json` —
  the saved-filters document identity and two-name migration rule, rendered
  wherever this app names the file). Also
  `FilterHelp.razor`, the producer's own facet documentation **and** its
  account of what the panel persists (`#fh-what-is-remembered`), embedded by
  `/help` — that prose has one owner, and it is not this app (see Pitfalls:
  never describe a facet, never restate what the panel stores).
- **BgFolderAccess_Razor** — the File System Access machinery this app
  originally grew app-side, rehomed (umbrella #79): `IFolderAccess` /
  `JsFolderAccess` (both pick mechanisms, name-parameterized picked/active
  slot file I/O, the two-slot isolation model), `FolderWriteCapability`,
  `FolderPickOutcome` / `PickedFile` / `PickTruncation`, and `FolderPickLimits`
  — the host-supplied caps configuration `Program.cs` builds from
  `PickedFileLimits`' values (the numbers stay host policy; the lib ships
  none). Its `folderAccess.js` ships as the lib's static web asset
  (`_content/BgFolderAccess_Razor/js/folderAccess.js`); this app authors no
  folder JS of its own any more. The FS-Access lore (two-prompt shape,
  cause-ambiguous cancels/denials, the busy-affordance seam) lives in that
  repo's Pitfalls now.
- **ConvertXgToJson_Lib** — picked up transitively via the filter pipeline
  (parses the user's browser-picked `.xg` / `.xgp` bytes in-browser, via
  `FilteredDecisionIterator.IterateXgStreamDiagrams`).

## Directory tree

```
BgQuiz_Blazor.slnx

BgQuiz_Blazor/                      — thin ASP.NET Core WASM host (server)
  BgQuiz_Blazor.csproj              — Sdk.Web; references only the .Client
  Program.cs                        — WASM components + render mode + pipeline
  appsettings.json
  appsettings.Development.json
  Properties/
    launchSettings.json
  Components/
    _Imports.razor
    App.razor                       — host shell (<head>, blazor.web.js +
                                      navFold.js, <Routes/>)
    Routes.razor                    — <Router> over the .Client _Imports
    Layout/
      MainLayout.razor / .razor.css
      NavMenu.razor / .razor.css
    Pages/
      Error.razor
      NotFound.razor
  wwwroot/                          — static assets (favicon, app.css, Bootstrap)
    js/navFold.js                   — 2nd authored JS; re-applies the nav fold
    robots.txt                      — Disallow: / (see Pitfalls: two wwwroots)

BgQuiz_Blazor.Client/              — WASM client (the whole interactive surface)
  BgQuiz_Blazor.Client.csproj       — Sdk.BlazorWebAssembly; the bg-lib closure
  Program.cs                        — TimeProvider.System + controller, holders,
                                      stores; registers the source factory by
                                      resolving PickedFolderSourceFactory.Create
  _Imports.razor
  AppInfo.cs                        — app-level identity SSOT (§ AppInfo)
  Quiz/
    QuizSettings.cs                 — user settings + xg_quizSettings owner
    QuizController.cs               — + ProblemSetSourceFactory, QuizStartOutcome
    ProblemReview.cs
    PickedProblemFolder.cs          — picked-folder holder + parse-cache seam
                                      (over BgFolderAccess_Razor's PickedFile)
    PickedFileLimits.cs             — pick-cap values (bytes / per-format counts /
                                      derived MB) — host policy feeding the
                                      registered FolderPickLimits
    PickedFolderFilterStorage.cs    — IFilterDocumentStorage over the picked
                                      slot (the two-producer adapter glue)
    FolderPickDisplay.cs            — folder-pick wording SSOT
    QuizStatsFile.cs                — stats filenames (live + retired sidecar) +
                                      JsonSerializerOptions SSOT
    QuizStatsStore.cs               — IProblemStatsSink + document lifecycle
    MixConsent.cs                   — the "Mix applies" bit (consent, not choice)
    MixDraft.cs                     — mix edit state + write-through xg_quizMix
    MixDisplay.cs                   — mix wording SSOT
    CubeActionDisplay.cs            — cube-verdict wording SSOT
    AnswerTypeDisplay.cs            — answer-type wording SSOT (always five)
    QuizNoticeDismissal.cs          — occurrence-keyed dismissal, one slot per
                                      Quiz-page notice (+ the QuizNotice enum)
    ShuffleOption.cs                — "shuffle order" toggle holder
    QuizLiveMarker.cs               — sessionStorage was-a-quiz-live marker
    WasmUploadedProblemSetSource.cs — in-browser stream-backed source (parser)
    CachedProblemSetSource.cs       — parse-once layer over the holder's cache
    PickedFolderSourceFactory.cs    — the source composition (cache → dedupe →
                                      shuffle?), the one layer-order statement
  Components/
    XgidLabel.razor / .razor.cs    — selectable+copyable XGID badge (in-flow;
                                      the quiz page's bottom row is its one home)
    Pages/
      Home.razor / .razor.cs        — landing: pick + filters + mix + Start
      MixPanel.razor / .razor.cs    — mix builder (a view over MixDraft)
      Quiz.razor / .razor.cs        — active problem (play or cube)
      Done.razor / .razor.cs        — final summary
      Stats.razor / .razor.cs       — read-only mid-quiz stats (live Controller)
      Settings.razor / .razor.cs    — user settings (a view over QuizSettings)
      Help.razor / .razor.cs        — end-user documentation (never redirects)
      ScorePanel.razor              — compact header strip (Total only)
      ScoreBreakdown.razor          — four-way Play/Double/Take/Total table

BgQuiz_Blazor.Tests/
  BgQuiz_Blazor.Tests.csproj
  TestFixtures.cs
  FakeProblemSetSource.cs
  GatedProblemSetSource.cs          — externally-completable MoveNextAsync
  FakeFolderAccess.cs               — scriptable IFolderAccess double
  FakeProblemStatsSink.cs           — recording sink double + RecordGate
  RetiredStatsFixture.cs            — stats files this build cannot write:
                                      the retired v1 doc + its near misses
  QuizControllerTests.cs
  QuizControllerOverlapTests.cs     — the transition-gate overlap suite
  CachedProblemSetSourceTests.cs    — parse-once / invalidation / equivalence
  PickedFolderSourceFactoryTests.cs — the real composition: layer wire, shuffle
                                      arbitration (corpus-level, skips if empty)
  PositionDedupeTests.cs            — the #84 repro: one fixture under two names
                                      (fixture absent ⇒ FAIL, never skip)
  CubeActionDisplayTests.cs
  AnswerTypeDisplayTests.cs         — bucket→field mapping, order, always-five
  MixPanelTests.cs                  — builder / validation / rebalance pins
  MixDraftTests.cs                  — build/write-through matrix + hydration
  QuizSettingsTests.cs              — the settings seam + the pinned wire bytes
  QuizStatsStoreTests.cs            — bind / fold / write-back / degrade,
                                      the v1 retirement, the pre-write guard
  WasmUploadedProblemSetSourceTests.cs
  PickedProblemFolderTests.cs
  PageTests.cs
  NavMenuTests.cs                   — the sidebar Help and Settings links
  MainLayoutTests.cs
  NotFoundPipelineTests.cs          — WebApplicationFactory 404 wire tests

BgQuiz_Blazor.E2eTests/            — browser e2e smoke gate (§ Architecture)
  BgQuiz_Blazor.E2eTests.csproj     — xunit + Playwright; references no app project
  Fixtures/                         — committed single-decision .xgp files
    BothAnalysis.xgp                — cube decision; best action "No Double"
    Opening 32 65 64 31 65.xgp      — 6-5 checker play; best play 24/13
    TooGoodAndTake.xgp              — cube decision, a *different* board
    match35253054_2_37.xgp          — cube decision, Double / Pass
                                      (the three cube fixtures are mutually
                                      distinct positions — the supply a
                                      multi-problem run is staged from)
  PublishedAppFixture.cs            — publish + spawn once; BGQUIZ_E2E_BASE_URL
  PlaywrightFixture.cs              — Chromium lifecycle; fail-loud
  E2eCollection.cs                  — the single (sequential) test collection
  E2eTestBase.cs                    — per-test context + shared flow helpers
  FsAccessFakeTestBase.cs           — the fake showDirectoryPicker seam
  QuizFlowTests.cs                  — cube + checker primary paths, pick → Done
  EmptyFilterBannerTests.cs         — known-zero pool darkens Start + recovery
  ReloadNoticeTests.cs              — reload-reset notice, Start and Restart
  StatsPersistenceTests.cs          — FS-Access stats path via the fake
  SavedFiltersPersistenceTests.cs   — saved-filters FS path via the fake
  MixWeightingTests.cs              — weighted start to Done (+ MixRefusalTests)
  ApplyMixGatingTests.cs            — mix activation sequenced behind Apply Filter
  PickBusyAffordanceTests.cs        — the pick's busy paint, scan held open
  CommaDecimalLocaleTests.cs        — nb-NO comma-decimal guard
  HelpAndTitlesTests.cs             — /help renders; document.title contract
  AnswerTypeBreakdownTests.cs       — the pre-Start breakdown: labels and zeros
  SidebarCollapseTests.cs           — fold, chevron state, how long it lasts
  SettingsTests.cs                  — board side by geometry; the fold setting
  MaximizeBoardTests.cs             — maximize on via the Settings checkbox →
                                      chrome absent answering, back at review;
                                      the score strip's real bottom position
  MidQuizNavigationTests.cs         — Home's way back into a running quiz
  EndQuizEarlyTests.cs              — ending a run before the source runs out
  BetaOnboardingTests.cs            — robots.txt over HTTP; the feedback mailto
  NotFoundTests.cs                  — unknown URL → 404 status + styled body
```

## Architecture

### Quiz flow

```
/        Home.razor    → "Choose folder…" pick; then (disclosed once files are
                          picked) FilterSurface (saved filters + filter panel,
                          producer-composited) + "N match" count +
                          MixPanel (Enabled picks only) + "Shuffle order"
                          checkbox + Start Quiz button
                          on Start: Controller.StartAsync(filters, mix) — binds
                          the lifetime-stats context and, for a non-blank mix,
                          composes from lifetime stats (or REFUSES — the
                          actionable notice with "Start without mix") — then
                          Nav→/quiz

/quiz    Quiz.razor    → per problem: answering → review → advance
                          "Show stats" (both states) → Nav→/stats
                          answering (Review null), routed by Decision.IsCube:
                            checker → BackgammonPlayEntry
                                      + Submit / Skip / Undo last / Undo all
                            cube    → board-only BackgammonDiagram
                                      + BackgammonCubeActions radios /
                                        Submit / Skip (no Undo)
                          review (Review set): read-only BackgammonDiagram
                            (Solution mode, user's answer marked, dice click
                            bound to Continue) + verdict + Continue / Redo
                          Redo → RedoAsync(), back to answering, same problem
                          "End quiz" (both states) → EndQuizAsync() → Nav→/done
                          IsFinished (on Continue / Skip / End quiz) → Nav→/done

/stats   Stats.razor   → read-only, live ScorePanel + ScoreBreakdown against the
                          same in-progress Controller + Back to quiz (Nav→/quiz)
                          Reachable only from /quiz; redirects to / if no quiz
                          in progress, to /done if already finished.

/done    Done.razor    → ScorePanel (Total) + ScoreBreakdown (four-way)
                          + Restart with same filters / Back to setup

/help    Help.razor    → end-user documentation. Reachable from any state;
                          never redirects.
```

### `QuizController` — per-app state machine

Scoped DI lifetime (see Pitfalls: resets on full reload, not on in-app
navigation). The controller holds the active `IProblemSetSource`
enumerator, the running `QuizScore`, the per-problem `SubmittedPlay`
(`History`) and `SubmittedCubeAction` (`CubeHistory`) histories — kept
separate because the two scored-result types are distinct shapes; a unified
history would force consumers to type-test — and a `SkippedCount` for
non-scoring outcomes (off-list submissions, explicit Skip). Pages observe
transitions via `StateChanged`: each gated async transition (below) fires it
exactly twice — busy-on, then busy-off with the end state in place — and the
synchronous mutators (Submit, Redo) fire it once.

**The transition gate.** The five async transitions — `StartAsync` /
`RestartAsync` / `ContinueAsync` / `SkipCurrentAsync` / `EndQuizAsync` — share
one busy gate:
a second gesture arriving while a transition is in flight **no-ops** (it does
not queue). The controller owns exactly one live enumerator, and an
overlapped `MoveNextAsync` — or a dispose during one — throws on a thread-pool
continuation no page can catch, terminating the WASM runtime. Per-method state
guards can't close that window: mid-advance they read *stale* state, so
Skip/Submit would stale-pass and a second Continue would double-fold. The gate
lives in the controller — pages never need the enumerator contract to be safe
(which is what makes the Quiz page's dice-click + Continue double-binding safe
as-is). The synchronous mutators (`SubmitPlay` / `SubmitCubeAction` /
`RedoAsync`) can't overlap an await themselves but can land *inside* one, so
they no-op on `IsBusy` too. Mechanics: `IsBusy` (observable; pages drive their
busy affordances from it) flips on inside the gate's check-and-set,
`StateChanged` fires, and the gate then **yields once, deliberately**, so the
busy state can paint before the transition's churn begins (the sources'
time-budgeted yields keep paints possible during the churn itself); a
`try`/`finally` releases the gate on completion *and* failure, firing
`StateChanged` again — the single completion signal (`AdvanceAsync` itself
fires none). Overlapped Start/Restart return `QuizStartOutcome.Busy`, which
callers treat as do-nothing; overlapped Continue/Skip return silently. The
never-started `RestartAsync` throw is checked *inside* the gate — an overlap
is an outcome (Busy), not the caller bug the throw exists for.
`QuizControllerOverlapTests` pins all of it via `GatedProblemSetSource` and
the fake sink's `RecordGate`.

**Three-state per-problem flow.** Each problem moves through *answering* →
*review* → *advance*, surfaced via `Current` and the nullable `Review`:

- **Submit** — `SubmitPlay(Play)` / `SubmitCubeAction(CubeDecisionPair)` are
  **synchronous** (the only `await` was the advance, now deferred): they score
  the answer, set `Review`, and fire `StateChanged` **without advancing** —
  `Current` still points at the answered problem. No-ops outside answering
  (guarding against double-scoring).
- **`Review`** — a closed `ProblemReview` record (`Play` / `Cube`) carrying
  exactly the marks the solution diagram needs. Non-null marks the state.
- **`RedoAsync`** — the inverse of Submit: pops the just-added entry from
  `History` / `CubeHistory` (or decrements `SkippedCount` for an off-list
  play, which never added one), recomputes `Score` by refolding both histories
  from `QuizScore.Empty`, and clears `Review` — back to *answering* on the
  same `Current`. Enumerator and `IsFinished` untouched. No-op outside review.
- **`ContinueAsync`** — the only *forward* exit from review: folds the
  just-reviewed submission into the `IProblemStatsSink` (see Pitfalls: on
  Continue, never at Submit), clears `Review`, and advances. Exhausting the
  source here flips `IsFinished` — after the fold, so the final answer
  records. No-op outside review.
- **`SkipCurrentAsync`** — bypasses review and advances immediately, but only
  from answering (no-op while a `Review` is showing).
- **`EndQuizAsync`** — the user's own exit from the run (issue #57), and the one
  path that leaves the three-state flow rather than moving through it: it
  finishes where it stands, with problems still unread. `IsFinished` flips,
  `Current` and `Review` clear, and the live enumerator is released early (safe
  because the gate guarantees no `MoveNextAsync` is in flight). No-op before
  start and after finish. **Two settled semantics, no new scoring path:** from
  *answering* the problem showing is **abandoned** — any in-progress input is
  discarded, it records no answer, and it takes the same non-scoring outcome an
  explicit Skip records (`SkippedCount++`), so Done's "problems shown" still
  counts a problem the user saw; from *review* the answer **stands and folds**,
  because it was submitted, scored, and read. Ending from review is a forward
  exit, so it goes through the same `RecordReviewedSubmissionAsync` Continue uses
  — which is what preserves the standing invariant that **every answer visible on
  Done has reached the lifetime record** (Done states it to the user; see
  Pitfalls). The run is a **completed quiz**, ruled: `/done` is unchanged, with no
  ended-early wording and no controller flag for one — the partial score is simply
  the score of the problems answered.

`ProblemReview` lives in `BgQuiz_Blazor.Client` (not BgGame_Lib): it is
per-app UI state, and adding it to the submodule would cross the boundary. Its
`Play` carries the matched candidate index (`-1` off-list), its `Cube` the two
per-half equity losses; the Quiz page maps these onto `UserPlayIndex` /
`UserDoubleError` + `UserTakeError` so the diagram marks the *quiz user's*
answer, not the .xg-recorded player's.

**Source construction is factory-injected.** The controller takes a
`ProblemSetSourceFactory` delegate (`(DecisionFilterSet, QuizMix) →
IProblemSetSource`). `PickedFolderSourceFactory.Create` builds the production
one and is the **single statement of the layer stack**; `Program.cs` registers
it scoped by resolving the app-scoped ingredients and handing them over. The
stack, innermost first:

1. `CachedProblemSetSource` over the pick — the parse-once layer (see its
   section).
2. `DistinctPositionProblemSetSource` — content-identity dedupe, always on.
3. `ShuffledProblemSetSource` — only when `mix.IsPassthrough &&
   shuffle.Enabled`. The mix parameter exists for exactly that one rule —
   **shuffle arbitration** (see Pitfalls). The factory never wires the
   composition layer itself (that is the controller's — below).

Every holder is read at **invocation** time (`StartAsync`), not at DI
registration, so choices made before Start take effect. Future alternatives
(deployed bundles, curated libraries) plug in by registering a different
factory; unit tests substitute a fake source the same way.

**Why a named type and not the DI lambda it replaced.** The composition is the
app's most wiring-sensitive code and the only place the layer *order* is
stated, so it has to be reachable from a test. As a lambda it was not, and
tests claiming to pin "the path `Program.cs` wires" could only re-type it by
hand — which drifted, silently. `PickedFolderSourceFactoryTests` calls `Create`
itself. The
one exception is deliberate and documented in place: pinning that a *shuffled*
source reorders needs `ShuffledProblemSetSource`'s seeded ctor, which
production deliberately does not use, so that test keeps a hand-built stack
rather than a permutation that flakes on the identity.

**Position dedupe sits beneath shuffle and mix** (issue
`halheinrich/backgammon#84`). A quiz could serve the same position twice:
`DecisionId` is file-relative, so two copies of one match in a folder — or an
identical early position reached in two different matches — carry distinct ids
yet render the same problem. Both were ruled in scope for the collapse. Wiring
the decorator at the bottom means **one rule for every quiz mode**: plain,
shuffled and weighted runs all draw from an already position-distinct supply,
with no per-mode variant to keep in step. Two things follow without their own
wiring — the pre-Start match summary counts the same deduped pool the quiz
draws (same factory; see below), and the mix's pool sizes and composition
notice tally deduped supply.

- **Above the filter, not below it.** The decorator wraps the *filtered*
  stream; deduping the raw parse first can silently lose a matching position
  (Pitfalls owns why, and why the reverse edit is the tempting one).
- **Which copy survives is nobody's business here.** First occurrence, for
  display and provenance only. Lifetime stats are keyed by content, so every
  copy reads and writes the same record whichever one the quiz shows — which is
  why this factory no longer takes the stats seam at all. (The stats-bearing
  survivor preference it used to pass existed solely to keep id-keyed stats
  reachable across content-identical copies; #95 deleted the fragmentation and
  the seam with it. Don't reintroduce one.)
- The producer's `Count` is null through this layer by contract — how many
  positions collapse is unknowable before enumeration.

**Mix ownership mirrors filter ownership, and a weighted start can be
refused.** `StartAsync(FilterConfig, QuizMix, bool ignoreMix = false)` takes
the caller's effective mix beside the filter config — user config in at
Start, stored for Restart, no caller-set mutation — and returns a
`QuizStartOutcome`. For a
non-blank *effective* mix (the stored mix, unless the per-run `ignoreMix`
override), `ResetAndAdvanceAsync` wires the producer's `MixedProblemSetSource`
around the factory source — so **the supply it composes from is already
position-distinct** (§ Source construction, which owns what follows
downstream) — holding the typed reference so `LastComposition`
telemetry surfaces without type-testing; the stats provider resolves
`IProblemStatsSink.CurrentDocument` fresh per enumeration, so **Restart
recomposes against the lifetime record as it stands, this session's folds
included** (deliberate, producer-documented). Composing without stats is
banned (ratified: no stats → feature unavailable, never silently unweighted),
so the start is **refused** in two stages: stage 1, the side-effect-free
`IProblemStatsSink.CanWeightMix` shared predicate (§ `QuizStatsStore`) —
before even the stats bind; stage 2, after `BeginQuizAsync` (ordered **before**
the source build, because the wrap decision needs the bound context), when the
bind yielded no document. Since #87 the refusal is a **backstop, not a routine
outcome**: the host offers no way to build a mix where `CanWeightMix` is false,
so what is left reachable is a bind that fails *after* the pick looked
capable — a stats file that changed or turned unparseable in between. Either refusal returns `MixRequiresStats` having touched **no quiz
state** (see Pitfalls). `RestartAsync(bool ignoreMix = false)` re-attempts the
stored mix every time, so the mix re-applies whenever stats allow; the
override is strictly per-run and the stored mix is never rewritten.

**Presentation telemetry for the Quiz page.** `ActiveMixHasLength` exposes the
one fact the mix-notice framing needs — whether the run's *effective* mix
bound its percentages to a requested `QuizLength` (false for passthrough, the
ignore-mix override, and capless mixes) — committed past the refusal checks so
a refused start leaves it untouched; intent over structure, no `QuizMix`
leaks. `ProblemNumber` / `ProblemCount` drive the "Problem N of M" indicator:
N is the 1-based **consumed stream slot** of `Current` (auto-skipped pass
positions included; reset by Start/Restart, untouched by Redo) and M is the
composition's `DrawnCount` (weighted) or the source's declared `Count`
(passthrough; null when streaming — the page then shows "Problem N" alone).
Slot-counting is the settled convention: both numbers count the stream, so N
never exceeds M and lands exactly on M at exhaustion; the accepted trade-off —
an auto-skip shows as a rare gap — is documented on `ProblemNumber`.

**Lifetime-stats sink is ctor-injected.** The controller's second dependency
is the `IProblemStatsSink` (production: `QuizStatsStore`), driven at exactly
two points: `ResetAndAdvanceAsync` calls `BeginQuizAsync()` — the one shared
path under Start *and* Restart, so the stats context binds there and nowhere
else — and the **forward exits from review** fold via `RecordAsync`, through the
one shared `RecordReviewedSubmissionAsync` (`ContinueAsync` and `EndQuizAsync`;
there is one encoding of which history entry a review finalizes, not two). The sink never throws for
stats trouble, so quiz flow is independent of whether stats are recording.

**Filter ownership.** `StartAsync` takes a `FilterConfig` (the wire DTO
emitted through `FilterSurface.OnFilterConfigChanged`), not a runtime
`DecisionFilterSet`, and calls `FilterConfig.Build()` to produce its own
pipeline, which it owns end-to-end — no shared mutable state ever exists
between page and controller. The `ProblemSetSourceFactory` delegate still
takes the runtime `DecisionFilterSet` (the source's contract is the runtime
pipeline; the controller is the authority on assembling it), plus the run's
effective `QuizMix` for shuffle arbitration.

**Pre-Start match summary.** `SummarizeMatchesAsync(FilterConfig)` reports
what a config would admit, as an `AnswerTypeDistribution`. It builds the same
controller-owned pipeline `StartAsync` would and folds a source from the
factory over a **throwaway** enumerator, so the shared enumerator, `Current`,
`Score`, and the histories are never touched and a summary is safe against a
live quiz; it deliberately takes **no** transition gate (no shared enumerator
to protect, and callers serialize Apply against Start on their side). The pass
is a byproduct of the source's in-memory `Matches` filter and it **warms the
parse cache**, front-loading Start's one-time corpus parse rather than adding
a cost on top of it. It counts every matching decision, forced-move pass
positions included, so it describes the **pre-mix pool** — "decisions that
match", not "problems you'll see".

**It counts deduped positions, and cannot disagree with the quiz.** The summary
draws from the *same* factory, so the position-dedupe layer is in the pool it
counts: "N decisions match your filters" means N distinct positions. The
agreement is structural, not a convention two call sites must honour: the
layer decides *which* copy survives, never how many do, so pool size cannot
vary between a pre-Start summary and the quiz it precedes.

**The count is `Total`, and there is no second surface for it.** The
producer's fold contract (every `Add` increments exactly one bucket) makes the
pool's size fall out of the same pass that classifies it, so "how many match"
and "what kinds are they" have **one** encoding — a second way to ask the
question is a second answer waiting to disagree. The fold takes
`BgDecisionData.Decision`: the composite forwards `IsCube` but not
`BestDoublerAction` / `BestTakerAction`, so folding it would misbucket every
cube decision (BgGame_Lib's Pitfalls carry the trap). Classification is never
re-derived here — a cube decision buckets once, on the analysis's declared
best pair, deliberately unlike the two-half convention `QuizScore` and
`ProblemStats` use for *answers*.

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
candidate). An in-list match contributes to the score: `EquityLoss == 0.0` is
the "best play" test (multiple candidates may share zero loss). An off-list
match counts as a skip — `SkippedCount++`, no history entry, score unchanged
(semantics in Pitfalls). Either way a `Review` (`OffList` true, index `-1`) is
set so the user still sees the best play on the solution diagram.

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
extension-bearing name) — the stream iterator reads each stream exactly once,
forward, so buffering up front is what lets a Restart re-enumerate.
`EnumerateAsync` also yields cooperatively so a long synchronous run doesn't
monopolise the single WASM thread — BgGame_Lib's `CooperativeYielder` (one per
enumeration; time-budgeted, not per-item `Task.Yield`, whose event-loop
round-trip per decision dominated large parses). The pacing clock is a ctor
`TimeProvider` — pure pacing, never affecting which decisions flow.

`Count` is null (an up-front count would require a full filtered pre-pass).
`Name` is `"No files"` / the single file's name / `"{N} files"`. Decision-type
admission is governed entirely by the supplied `filters`; the source injects
no policy of its own.

### `CachedProblemSetSource` — the parse-once cache

The production source the `Program.cs` factory builds (the stream source
above remains the parser under it): parse the picked files **once**, then
serve every Start/Restart by filtering the cached decisions in memory. Only
the first Start after a pick parses — the cache makes repeat Starts
milliseconds.

- **Cache home & lifecycle.** The cache slot is
  `PickedProblemFolder.ParsedDecisions` — on the holder, so cache lifecycle
  *is* pick lifecycle: `Set`/`Clear` null it and bump `PickGeneration`, with
  no separate invalidation wiring to forget. `CachedProblemSetSource` is the
  slot's only writer, via `StoreParsed(generation, decisions)`, which
  **drops** a store whose pick has been superseded (see Pitfalls).
- **Unfiltered cache, per-Start filters.** The cached parse applies **no
  filters** so any filter config reuses it; each enumeration re-filters via
  `DecisionFilterSet.Matches` — exactly equivalent to filtering during the
  parse, because the iterator's other hooks are contractually pure early-exit
  hints (see Pitfalls). `CachedProblemSetSourceTests` pins the equivalence
  shape-level over the rotating corpus.
- **Staleness.** Files + generation are captured at construction (factory
  invocation = Start time, the read-live-at-Start discipline); the holder's
  cache is consulted only while the generation still matches, and the source
  keeps its own reference to whatever it parsed/adopted — so a Restart after a
  mid-quiz re-pick still replays *this quiz's* files without re-parsing and
  without polluting the new pick's cache.
- The stream sources stay **stream-pure** (the parse delegates to
  `WasmUploadedProblemSetSource` with an empty `DecisionFilterSet`); caching
  is entirely this app-side layer. Both passes pace their yields with
  `CooperativeYielder`, so the busy cursor keeps painting. `Name` delegates to
  the inner naming rule; `Count` stays null.
- **This layer knows nothing about position dedupe**, and the ordering is the
  reason: the dedupe decorator wraps *this* source, so it sees filtered items.
  Folding a dedupe into the cached parse would put it below the filter, where it
  can lose a matching position (see the composition section and Pitfalls).

### Folder picking & lifetime stats

One "pick a folder" gesture on `Home`, served by whichever mechanism the
browser offers — probed **at pick time**, per gesture:

- **File System Access** (`showDirectoryPicker`, Chromium): native directory
  picker, then a `requestPermission({mode:'readwrite'})` on the picked handle —
  **two prompts, deliberately** (see Pitfalls), with asymmetric declines. The
  *first* is load-bearing: decline it and the pick aborts holding nothing (⇒
  `Cancelled`, indistinguishable from a dismissed picker). The *second* is the
  graceful rung: granted ⇒ `FolderWriteCapability.Enabled`; not granted ⇒
  `PermissionDenied` — the handle stays readable, so the file list loads and
  the quiz runs read-only. `PermissionDenied` likewise carries **two** causes
  it can't tell apart: the user answered no, *or* the request **auto-denied**
  with no prompt shown (some Chromium versions treat the transient user
  activation as consumed by the picker). So every surface for this rung opens
  with the cause-agnostic `FolderPickDisplay.WriteAccessNotGranted` — never
  "you declined", which on the auto-deny path attributes a decision the user
  never made, and never a *count* of prompts.
- **`webkitdirectory` fallback** (everywhere else): a hidden
  `<input type="file" webkitdirectory>` opened by the same button. Read-only
  by construction ⇒ `BrowserUnsupported` — quiz runs without stats.

Either way the folder's **top-level** `.xg` / `.xgp` files (subfolders
ignored; case-insensitive extension filter), up to each kind's own count cap,
are buffered into `PickedFile`s and the pick lands in `PickedProblemFolder`
together with whatever the caps left unread (§ `PickedFileLimits`). The degrade
ladder is total: no capability rung ever blocks the quiz — no-stats mode is
fully functional.

**`IFolderAccess` / `JsFolderAccess` / `folderAccess.js` — BgFolderAccess_Razor's
now.** The whole gateway (both pick mechanisms, the two-slot state model,
name-parameterized picked/active file I/O, the caps enforcement, the error
contract — expected outcomes as values, unexpected browser failures as
`JSException`, `Cancelled` deliberately cause-ambiguous) was rehomed to the
BgFolderAccess_Razor submodule (umbrella #79) and is documented there; its JS
module ships as that library's static web asset, so this app authors no folder
JS. What stays *here* is the host's side of the seam: `Program.cs` registers
the lib's `JsFolderAccess` (Scoped) plus one `FolderPickLimits` built from
`PickedFileLimits`' values, `Home` drives the pick gestures and renders the
outcomes, and the file-name constants stay host policy — the stats store passes
`QuizStatsFile.FileName` into the name-parameterized active-slot calls, and the
saved-filters names come from XgFilter_Razor's `SavedFiltersDocument` through
the composite.

**The FS-Access pick is split in two, on purpose (issue #48; the seam is the
library's contract now).** `PickFolderAsync` awaits a caller-supplied
`Func<Task> onPickAccepted` between the browser's prompts and the
enumeration/buffering — the only point at which a busy affordance can be
raised *truthfully* (earlier lies over a modal; later never paints — the
lib's Pitfalls carry the full rationale). It is **not** invoked for a
cancelled pick. `Home` passes `EnterBusyAsync`; the fallback reaches the same
meaning by a different route, its work beginning at the input's `change` event
so the whole of `HandleFallbackPickedAsync` runs under the affordance. On both
mechanisms the busy state means one thing — *the app is processing a selection
the user has made*.

**Two-slot model — the mid-quiz-Clear ruling (enforced lib-side, relied on
here).** The stats context **binds at Start/Restart, never at pick**: the
controller's `ResetAndAdvanceAsync` drives `QuizStatsStore.BeginQuizAsync()`,
which promotes picked → active (`PromoteToActiveAsync`) and loads the stats
file through the active slot. Home's Clear resets **only the picked slot**
(`ClearPickedAsync`), so a mid-quiz Clear or re-pick never affects the running
quiz's recording — that changes only when the next Start re-binds. The picked
slot also serves the **saved-filters** document (through
`PickedFolderFilterStorage` over `ReadPickedFileAsync`/`WritePickedFileAsync`):
a setup-time concern on the folder being configured, deliberately on the
picked slot so it never requires a promote and never touches a running quiz's
active handle.

**`QuizStatsFile`** — the persistence SSOT: `FileName`
(`bgquiz-stats.json`), `RetiredFileName` (`bgquiz-stats.v1.json` — the
set-aside name, bare and path-free), and the one fixed `JsonSerializerOptions`
(`WriteIndented = true` — whitespace is the only options-controlled aspect;
the bundled converter pins names and ordering). The filename is passed *into*
the lib's name-parameterized active-slot calls per call and rendered by `Help`
from the constant — neither restates it.

**`QuizStatsStore`** (scoped; aliased as `IProblemStatsSink` so the
controller's sink and the pages' status notices observe one instance; deps:
`IFolderAccess`, `TimeProvider`, `PickedProblemFolder`) owns the
`ProblemStatsDocument` lifecycle:

- `BeginQuizAsync` (every Start/Restart) re-derives the whole context and
  resets any prior failure state: capability ≠ `Enabled` or no promoted
  handle ⇒ `Disabled`; `null` read ⇒ `Ready` over `Empty` (fresh corpus);
  `JsonException` / read `JSException` ⇒ **`LoadFailed`** — records nothing,
  never writes (see Pitfalls; recovery is user-side, no overwrite offer).
- **The v1 retirement** (SPEC-stats-identity.md §3) is the one exception to
  that never-writes rule, and it is caught *before* the general
  `JsonException`: a `RetiredStatsSchemaException` means a genuine v1 document,
  so the store copies its bytes aside under `RetiredFileName` **unparsed and
  first**, then puts a fresh `Empty` under `FileName`, then mints
  `StatsRetiredOccurrence` and lands `Ready`. Order is the data-safety
  guarantee — a file that could not be preserved is never replaced — so a
  `JSException` from either write reports `LoadFailed` with `FileName` still
  holding v1, which the next bind recognises and retries over identical bytes.
  Newer-than-supported is **not** retired: it keeps the untouched `LoadFailed`
  posture, as does a document claiming v1 without v1's shape. No rename API was
  lifted into BgFolderAccess_Razor for this; a second consumer would be the
  trigger.
- `RecordAsync` (from `ContinueAsync`, only while `Ready`): fold then **write
  back immediately** — per-fold write-back is the crash-safety choice (small
  file; a lost tab loses no answered problem). The fold's base is a **fresh
  read** of the file, not the bind-time snapshot (the ruled pre-write guard,
  SPEC-stats-identity.md §5): whole-document writes mean a second context over
  the same folder would otherwise be silently overwritten, and the re-read
  shrinks that to a same-instant race. Read trouble — missing, unreadable,
  unparseable, *including* a file swapped to v1 mid-quiz — is one answer:
  degrade to the in-memory fold, touching no status. A write `JSException`
  keeps the folded document in memory, flips `WriteFailed`, raises
  `StatusChanged`, and stops writing (no per-answer error spam). The store
  **never throws** — Continue cannot fault on stats trouble.
- The clock is the DI `TimeProvider` (registered `TimeProvider.System` in
  `Program.cs`), handed to the document's `Plus` — ambient time is never read.

**Two states, two lifetimes, no traffic between them** (issue #87). Beside the
*active context* above sits the **pick-time probe** behind `CanWeightMix`:

- **`CanWeightMix`** (on `IProblemStatsSink`, replacing the old
  `CanBindStats`) — *the* predicate for "can a weighted mix mean anything
  here": `Capability == Enabled` **and** the probe found a stats document with
  `Count > 0` **and** the probe's generation stamp still matches
  `PickedProblemFolder.PickGeneration`. That stamp is why the answer **expires
  by construction** rather than by anyone remembering to reset it — every
  `Set`/`Clear` bumps the generation, and a probe about the previous folder
  simply stops matching. It starts at `-1`, not `0`, so "never probed" cannot
  read as "probed and found nothing".
- **`RefreshPickedStatsAsync()`** — the probe: a **picked**-slot read
  (`ReadPickedFileAsync(QuizStatsFile.FileName)`), deserialize, `Count > 0`.
  Degrade-tolerant *because that is the ruling*, not as a defensive extra:
  missing, empty, corrupt, foreign-schema, and browser-read-failure all leave
  it false, with no status, no notice, and nothing thrown. **A retired v1 file
  reads as "no stats to weight by" too**, and stays that way until the first
  quiz performs the set-aside: the probe never binds, so it never retires. That
  is the ruling working, not a gap to close (SPEC-stats-identity.md §3). It **promotes
  nothing** and never assigns the active document or `Status`, so a probe
  during a running quiz cannot disturb what that quiz records. Under a
  non-`Enabled` capability the interop is skipped through the same private
  half the predicate uses, so the two can't drift.
- **Two reading points, both `Home`'s**: each successful pick's landing
  (`ApplyPickOutcomeAsync`, after `Set` so it probes the generation it is
  about) and `OnInitializedAsync`. The second is what makes "no mix until its
  first quiz creates stats" resolve on the way back from that quiz — returning
  to Home re-instantiates the page and re-probes — instead of waiting for a
  re-pick the user has no reason to make.

The probe lives *here* rather than in a service of its own because this class
already owns both ingredients — how a stats document is read out of a folder
and what counts as unreadable, and the pick's write capability — so anywhere
else would duplicate the recipe and leave a second, capability-only answer to
the same question.

**Naming trigger (recorded deliberately).** `CanWeightMix` puts a *mix* policy
on a stats abstraction. The fact-level alternative (`PickedFolderHasStats`)
would scatter the "a mix needs stats" rule across both consumers instead, which
is worse at two call sites — but the trade flips with a third. **The first
consumer of "does this folder have stats" that is not about the mix** — a stats
viewer, or #43's saved-mix gating — **is when this splits into the fact plus
the policy over it.**

**Status surfacing** splits by context. Pick-time (Home, capability-based,
all polite `role="status"`): stats-will-be-saved (`Enabled`, naming
`QuizStatsFile.FileName`) / browser-can't-save / declined-write, plus the
empty-folder outcome, the truncated-pick notice (one line per kind the count
caps cut short — § `PickedFileLimits`), and the `role="alert"` pick-failure
banner. Quiz-context
(Quiz **and** Done — a failure on the final Continue lands on Done without
ever showing Quiz's notice): `LoadFailed` polite, `WriteFailed` assertive;
both scope to the active context and reset at the next Start's re-bind.

**Saved named filters — composite-owned now.** A per-directory saved-filters
document beside the corpus lets the user save and reload filter
configurations. The whole lifecycle moved into XgFilter_Razor with the
`FilterSurface` adoption (umbrella #63/#78/#38): the composite owns its
`SavedFiltersStore` over the host's `IFilterDocumentStorage` adapter, the
status taxonomy, the panel-offering rules (Ready hides a read-only *empty*
section — the clutter ruling, producer-owned now; WriteFailed keeps the panel
beside its notice; LoadFailed replaces it), the degrade-notice copy, and the
save-as/row-save refusal on an unparseable position pattern. The document
identity is `SavedFiltersDocument`: canonical `FileName` (`xg-filters.json`),
legacy `LegacyFileName` (`bgquiz-filters.json`) — read canonical first, fall
back to legacy only when canonical is *absent* (never when corrupt), write
canonical only, never delete the legacy file. This app's remaining half is
capability policy and glue: `PickedFolderFilterStorage` (Scoped) adapts the
seam onto the lib's picked-slot I/O wrapping `JSException` in
`FilterStorageException`; `Home` supplies it only while the pick's capability
exposes a readable handle (`Enabled` / `PermissionDenied` — `null` under
`BrowserUnsupported` ⇒ no saved-filters section), rules
`CanPersist = (Capability == Enabled)`, and words `PersistDisabledReason` from
`FolderPickDisplay.WriteAccessNotGranted`. `NamedFilterCollection`
(XgFilter_Lib) still owns the wire format end to end — no
`JsonSerializerOptions` anywhere host-side.

### `PickedProblemFolder` — the picked-folder holder

The Scoped holder (see Pitfalls: resets on full reload) for the picked folder:
`Files` (buffered `PickedFile`s — BgFolderAccess_Razor's record), `FolderName`,
and the two pick-time verdicts
about them — `FolderWriteCapability` and `Truncations`. `Home.razor` writes it
(`Set` / `Clear`); the `ProblemSetSourceFactory` reads it to build a
`CachedProblemSetSource`; `QuizStatsStore` reads `Capability` at its
Start-time bind. Files are buffered byte arrays (read out of the browser once
at pick time) so the source can re-enumerate on Restart. Carrying the
capability here (not in a component field) keeps Home's stats status notice
alive across navigate-back — the same holder-vs-field rationale as the start
gate, and the reason `Truncations` sits beside it: both describe the folder
being *held*, unlike the cancelled / empty-folder flags, which describe a
gesture that left nothing to describe and so stay per-visit page fields. `Set`
takes the truncation report rather than defaulting it, so a caller holding the
fact cannot drop it. The holder also carries the **parse-once cache seam** —
`ParsedDecisions` / `PickGeneration` / `StoreParsed` — so that invalidation
is intrinsic to `Set`/`Clear`; see the `CachedProblemSetSource` section for
the contract.

- **`Summary`** (`string?`) — the holder-owned label:
  `"'{FolderName}' — {N} problem file(s)"`, `null` when nothing is picked.
  The **single source of truth** for how a pick describes itself; `Home`
  renders it directly rather than caching text in a component field (see
  Pitfalls: holder, not field).

The pick is **in-memory only** — the stats *file* is not lost with it;
re-picking the folder resumes it.

### `PickedFileLimits` — the pick-cap values, single-sourced (enforcement is the lib's)

`internal static class PickedFileLimits` (Quiz/) holds the cap **values** the
folder pick applies — `MaxFileBytes` (50 MB per file) and the **per-extension**
file counts `MaxXgFileCount` (500) / `MaxXgpFileCount` (2000), tabled as
`MaxFileCounts` — plus `MaxFileMegabytes`, **derived** from `MaxFileBytes`.
Host **policy**, not machinery: `Program.cs` builds BgFolderAccess_Razor's one
registered `FolderPickLimits` from this table, and the lib enforces it (the
lib ships no numbers — each host's values encode its own cost model).

**The counts are per format because count is only a cost proxy within one
format** (issue #59): an `.xgp` is one position, an `.xg` averages ~120
decisions, so a flat cap would authorize ~4× the worst-case parse load for the
heavy format — or keep hard-blocking real position libraries, which is what
500 did. Each extension truncates at its own cap independently, so one folder
can admit its full quota of both.

**The two caps end differently, and that is the design.** An oversized *file*
throws (lib-side, before any bytes move) and lands on Home's pick-error
banner. A folder past a *count* cap **truncates and reports**: the pick takes
the first N of that kind, and the left-behind count rides back as
`FolderPickOutcome.Truncations` → `PickedProblemFolder.Truncations` → Home's
polite per-kind notice. Failing the whole pick threw away the 2000 files that
were perfectly readable; the caps are a cost ceiling, not an admissions test.
Everything downstream — match count, mix, stats — derives from the partial pool
with no special-casing at all. Each `PickTruncation.MaxFileCount` is derived
lib-side from the enforced `FolderPickLimits` instance (never round-tripped
across interop), so the figure Home's notice states is by construction the
figure the pick applied.

`MaxFileCounts` still has several consumers, which is why the table (not just
the numbers) is the unit: the registered `FolderPickLimits` *carries* it into
the lib's enforcement (the JS module derives **both** *which names are problem
files* and *how many of each to take* from the table it is handed, keeping no
copy); `Home` *renders* the truncation notice; `Help` *documents* the caps.
Leaving them as private constants on an enforcing type would have forced the
help page to restate "50 MB" / "500" as prose, so raising a cap would silently
make the documentation wrong; deriving the megabyte figure is what makes the
SSOT actually hold. `PageTests` pins Help's rendered prose against the
constants (and the stats filename against `QuizStatsFile.FileName`); the
table-crosses-the-wire pin lives producer-side now. The constants stay
`internal`; the `.Client` csproj grants `InternalsVisibleTo` to the test
project rather than widening them to public.

### `AppliedFilter` — the filter half of the start gate (XgFilter_Razor's holder now)

The Scoped holder (see Pitfalls: resets on full reload) for the `FilterConfig`
the user has **deliberately applied** on `Home` — the sibling of
`PickedProblemFolder` for the filter half of the start gate. The type is
**XgFilter_Razor's** since the `FilterSurface` adoption (it was hoisted from
this app's original), registered Scoped here and bound to the composite's
`[EditorRequired] AppliedFilter` parameter. **The composite mediates it**: a
commit (Apply / Clear filters) `Set`s it keyed to the bound `Source` token, an
uncommitted-edits report `Clear`s it, a clean re-affirm re-`Set`s it — Home
writes it in exactly one place (the setup-end clear below) and otherwise only
reads, always through `ConfigFor`.

**One fact, keyed to its source.** The holder is a single nullable
(config, source) pair. There is no bare `Config` / `IsApplied` and no
`WasAppliedFor` stamp: the only question it answers is the source-relative
*"what is applied for this source?"*, so a config applied against a superseded
pick is not applied at all, and reading applied-ness absolutely — the old
conflation — is unrepresentable. Nothing anywhere answers "has this corpus ever
been filtered"; that fact is deleted from the model with nothing replacing it
(`SPEC-filtering.md` §3, Fork A).

**One derivation, four readers.** Home mints the token once, in
`CurrentFilterSource` (`FromGeneration(Folder.PickGeneration)`), and derives
`FilterInEffect => AppliedFilter.ConfigFor(CurrentFilterSource)` from it. The
composite's `Source` binding reads the first; `CanStart`, its Apply-hint,
`MixActivationEnabled`, and `StartCoreAsync` all read the second. So what a commit
is keyed to and what every gate compares cannot encode the pick differently —
structurally, not by a documented promise that two inline mints agree.

The applied config is **edit-coupled** (a half-edited set clears it via the
composite's mediation) **and setup-coupled** (`PickGeneration` is monotonic and
bumped by both `PickedProblemFolder.Set` and `.Clear`, so ending a setup
expires it by key inequality — the staleness idiom `StoreParsed` already uses).
`EndCurrentSetupAsync`'s `Clear()` is a third, now-redundant safeguard; see
below.

**The setup-end clear is host-side, and the reason is structural (ruled, this
migration).** The composite owns a source-change rule that would do this — but
the rule runs on a *mounted* component receiving a changed parameter, and in
this host the composite lives behind Home's `HasFiles` gate: every token
change passes through an unmount (`Folder.Clear()` renders the gate closed
before any parameter could be observed), and the eventual re-mount's
first-parameters-set is, by the producer's ruled pin, initialization only — no
holder clear, precisely so navigate-back over an *unchanged* source leaves an
applied gate armed. So the composite's rule is **dormant in BgQuiz** (kept
bound as defence in depth); what each pick actually gets from the producer is
the remount: a fresh panel with Apply re-armed and the new folder's
saved-filters document read. `EndCurrentSetupAsync` keeps the single line of
filter choreography that covered that gap — but **source-keying demoted it from
gate-closing to residue-dropping**: `Folder.Clear()` on the line above bumps
the generation, so `FilterInEffect` is already null for every later read
whether or not the `Clear()` runs. What it still buys is that no config outlives
the setup that applied it even unreachably, which is the end-of-setup call the
holder documents as `Clear`'s purpose.

Holding the applied state in a Scoped holder rather than a transient component
field is what lets the gate survive in-app navigation: on navigate-back `Home`
re-derives `CanStart` from the persisted holders instead of resetting to
"not applied" and forcing a needless re-click of Apply.

**Gate semantics — applied, not merely present.** A non-null `ConfigFor` means
the user took the Apply action, so a half-edited set must clear it (the composite
mirrors the panel's `null` report onto the holder) — and an edit *undone* back
to the applied values makes the panel report the committed config again, which
re-`Set`s it. That direction is not a nicety: the panel disables its own Apply
whenever the buffers equal what it committed, so without the re-`Set` an
edit-then-undo would leave Start and Apply both dead (issue #49). The
interaction with the panel's localStorage restore is safe by construction:
restore writes the panel's own fields directly and raises **neither**
callback, so it can't spuriously mark applied or clear an existing applied
state — the holder is the sole authority on "applied".

### `FilterRestoreNotice` — the reload is legible (`SPEC-filtering.md` §4)

A reload ends the setup, and §4 rules that the resulting state must say what it
is rather than look like a defect. `FilterRestoreNotice` (XgFilter_Razor's,
rendered by the panel as `#filterRestoredNotice`) is the state that copy hangs
on.

**This host's entire contract is two lines**: register it Scoped in
`Program.cs` beside `AppliedFilter`, and bind it to `FilterSurface`. Every
member that moves it (`Arm` / `Dismiss` / `IsVisible`) is producer-internal, so
BgQuiz cannot read or steer it and the notice behaves identically in both
hosts by construction. Do not add host copy about it — the sentence is the
producer's.

**Scoped is the mechanism, not a convention.** A full reload reboots the WASM
app and constructs a fresh instance; *that construction* is what distinguishes
a boot from a navigate-back remount, which also restores a selection and also
finds nothing applied. Register it Transient and every navigation re-announces
a restore that already happened, which §4 forbids ("navigating away and back
changes nothing"). The pin is
`Home_RestoredFilterSelection_ShowsTheNotice_UntilAnEditSupersedesIt`, whose
last leg fails on a Transient registration while its binding half still passes.
It stages the stored selection *by exclusion* — answering every
`localStorage.getItem` for a key BgQuiz does not own — because the panel's key
is a producer internal no host may name.

### `MixPanel` / `MixDraft` / `MixConsent` — the stats-weighted mix

**The model is `SPEC-filtering.md` §5 / Fork B** (ratified 2026-08-09, rebuilt
here per umbrella #83) — read it there; what this app has is: **no committed
copy of the mix.** The sole activation control is the **"Mix applies"
checkbox** (`#mixApplies`), whose one boolean lives in the app-scoped
`MixConsent`; checked means *the mix on screen is in effect*, and what a
consented Start runs is the draft's own `Build()`. The checkbox is
**consent**, the rows are **choice** (§4's law): the rows persist — across
navigation, picks, and reloads — while the bit is revoked at every setup end
and dies on reload with the scope. (Nothing may reintroduce a committed copy —
see Pitfalls.)

**`MixPanel`** (Components/Pages) is the FilterPanel of quiz composition — a
**view over the app-scoped `MixDraft` and `MixConsent`** (all state lives in
the Scoped services; the component holds none, so mix edits and the activation
bit survive in-app navigation — ratified product behavior): an
ordered list of (category, percent) rows — category picker over the seven
`QuizCategoryKind`s, a parameter input where the kind takes one (defaults
seeded on selection: 3 times / 30 days / 0.05 equity / 25%), percent 1–100
summing to exactly 100 — plus the Random-order toggle (default on) and an
optional quiz length (disabled with a hint at zero rows;
length-without-entries is invalid by producer rule, and "cap without
weighting" is one Everything-else row at 100 plus a length). Row order is
**semantic** (earlier rows win contested overlap — producer contract), so
rows carry explicit ↑/↓ reorder buttons and both persistence and restore
preserve order exactly. The wrong-rate row *displays* percent and *stores* the
producer's fraction — thresholds are fractions; rendering is a display
concern. Validation reports the first problem inline (`ValidationError` — the
in-place account of what the fix-or-uncheck hint means by "fix it"); category
construction goes through the producer's validating factories with a
try/catch backstop. **The blank builder builds `QuizMix.Empty` — never null**
(ruled, load-bearing): null is reserved for genuinely invalid states, so
checked-over-blank reads as in-effect passthrough and Clear can persist blank.

**The row count owns the percents** (findings AH/AI). Add *and* Remove alike
re-derive every row's percent as an even split totalling exactly 100 (floor
share; the remainder handed out one apiece from the top, so the
overlap-winning early rows carry it), deliberately overwriting hand-edited
values: the panel demands a 100 total, so a structural edit that left the old
numbers standing only handed the user arithmetic, and the split always landing
on 100 means "must reach 100%" can never be the *consequence* of an
Add/Remove. A new row starts on the first kind no existing row uses, seeded
with that kind's default parameter, so successive Adds walk the picker order
and finish on the residual `Everything else` rather than stacking duplicate
`Never seen` rows. Seeding happens at Add/Remove time **only**: an existing
row's kind and percent stay the user's, a hand-picked duplicate is left to
stand as the validation error it is, and reordering — not a row-count change —
never rebalances.

**Add category is styled `btn-outline-primary`, not the panel's secondary grey
— don't "unify" it**: the button is never disabled (adding a row is always
valid), but at zero rows its neighbours (Random order, Quiz length, and the
gated checkbox pre-filter) *are*, and in secondary grey it read as another
switched-off control — the one misreading that must never happen, since it is
the only way out of the zero-row state. The class matches
Home's `Choose folder…`, the page's other required-but-unstarted step;
`MixPanelTests` pins state and appearance together, because the defect was the
gap between them.

**The checkbox's disable is asymmetric — only *checking* is gated.** The
check gesture requires the host's `CanActivate` (Fork A: a filter in effect
*now*), told through a parameter beside `ActivateDisabledReason` — both
`[EditorRequired]` (with the Apply event gone the component has no other
required binding, and a bare mount must not compile silently into an ungated
activation control; `CanActivate` still defaults `true` for a host that
doesn't sequence). The reason renders as the muted hint line and the disabled
box's `title`, mirroring `SavedFiltersPanel.CanPersist`'s contract. While the
box is **checked** it stays operable regardless — unchecking is consent
withdrawn and is never taken away — and `HandleAppliesChanged` drops a *check*
arriving past the gate, so a dispatch ignoring `disabled` still cannot
activate. **The app flips the bit in neither direction**: not on Clear, not on
invalid, not on a filter edit (auto-uncheck and its neighbours are rejected
alternatives `SPEC-filtering.md` §5 records). The one app-initiated
move is `MixConsent.Revoke()` at setup end. Zero rows do **not** disable the
box (ruled: checked-but-inert — vacuous consent is passthrough).

**Persistence follows the screen — last-valid write-through** over the one
key, **`xg_quizMix`**, owned by `MixDraft` in both directions, format
unchanged (one lib-owned `QuizMix` blob via `ToJson`/`TryFromJson`, so mixes
stored by earlier builds load with no migration). Every mutator, after
mutating and raising `Changed`, persists the built mix **when the draft
validates** — the blank draft included, so *Clear mix* and the last-row
removal persist `Empty` as ordinary edits (no auto-commit path, no event); an
invalid mutation skips the write, so **storage always holds the last
well-formed screen state** and a reload mid-half-edit restores that, not the
torn edit (ruled). Writes are best-effort (a storage fault must not break
typing — the same degrade posture as the read). Hydration stays
**once-per-setup** (`EnsureHydratedAsync`, a cached-task idempotent read —
absent/corrupt yields a blank draft, never an error, and only a *successful*
parse projects), triggered by the panel's init, filling the draft only: it
never writes storage and never touches the consent bit, so a restored mix is
**visible but inert until the user checks the box in *this* setup** (§5
rule 3). Nothing else touches a serializer or the key.

**The effective mix is derived, never stored** (see Pitfalls). Home's one
derivation, read by everything downstream:
`EffectiveMix => MixConsent.Applies ? MixDraft.Build() : QuizMix.Empty`.
Unchecked ⇒ passthrough — an un-activated draft, however divergent from
whatever ran last, **never gates Start** (issue #83 resolved by construction:
no disagreement exists to gate on). Checked ⇒ the on-screen build:
`QuizMix.Empty` for the blank draft (checked-but-inert), and **null exactly
when the draft fails to validate — the one mix state that gates Start**, with
the exact hint "Mix applies but isn't valid — fix it or uncheck.", the box
left checked (it records intent), and the panel's `ValidationError` saying
what to fix. Gated is never wedged: unchecking is always live, and *Clear
mix* clears the rows in every state.

**Offered only where a mix can mean something — one predicate, every consumer**
(issue #87). Home renders `MixPanel` only while
**`QuizStatsStore.CanWeightMix`**, and the controller's stage-1 refusal reads
the same member through `IProblemStatsSink`. Ruled: **a weighted
mix does not apply to an empty stats document, and an empty document is
treated exactly as no document**; missing, empty, and unreadable are one
answer, not three rungs. The predicate is therefore write capability **and** a
stats document with at least one decision in it.

Deliberately **not** routed through it: `FilterSurface`'s
`CanPersist`, which stays `Capability == Enabled`. Saved filters have nothing
to do with a stats record, and gating them on one would break saving on every
folder without quiz history.

**No disabled state, no reason string.** Where the predicate is false the panel
simply is not mounted — the same non-mount path a stats-less pick always took,
now driven by the whole predicate. The accepted emergent behavior: a brand-new
folder offers no mix **until its own first quiz creates stats**, which resolves
on the return to Home (see the probe's two reading points below), not at some
later re-pick.

**Every pick (and Clear) ends the mix's consent, never its choice** —
`MixConsent.Revoke()` plus `MixDraft.Discard()` in `EndCurrentSetupAsync`.
The revoke is **unconditional**, which is the whole of #87's "a
non-passthrough mix must not survive into a folder that can't honor it": a
consent that survives no pick cannot survive that one either, so there is no
predicate branch here to get wrong. `Discard` blanks the draft **and forgets
hydration** (with a generation guard so a read still in flight lands nothing)
— **deliberately without touching localStorage**, the Clear/Discard asymmetry
that is §4's choice-vs-consent line drawn through the draft: a mix-capable
pick's re-mounted panel re-hydrates the stored last-valid mix, visible but
inert until re-checked, while a pick that can't mean a mix mounts no panel,
re-hydrates nothing, and the revoked consent keeps the mix out of its Start
with **no capability fork in the gate**. Together those keep such a pick
unable to coexist with a mix in effect — which is what retired the old
won't-apply advisory. The panel is **`@key`-ed on
`PickedProblemFolder.PickGeneration`** so every pick re-mounts it and the
fresh mount re-hydrates the discarded draft (see Pitfalls: load-bearing).

**`MixDraft`** (Quiz/) is the app-scoped edit state behind the panel — and,
when consented, the mix that runs: rows (kind / parameter text / percent
text, read-only outside — every write goes through an async mutator so
`Changed` fires and the write-through runs), the Random-order toggle, the
length buffer, the picker's canonical kind order, validation
(`ValidationError`), `Build()` (**zero rows ⇒ `Empty`, never null**;
unbuildable ⇒ null), and the hydration lifecycle (`EnsureHydratedAsync` /
`ClearAsync` / `Discard` — `ClearAsync` is the blank *inside* a setup, stays
hydrated, and persists blank; `Discard` ends the setup and persists nothing).
Subscribers (Home) detach on dispose.

**`MixConsent`** (Quiz/) is the "Mix applies" bit beside the draft: `Applies`
+ `Set(bool)` (idempotent, `Changed` on real moves) + `Revoke()` (the
end-of-setup reset — the only app-initiated move). The two start-gate halves
block by **different mechanisms**, because their defaults differ: the filter
blocks via not-yet-applied (it has no valid default), the mix only via
checked-and-invalid (passthrough *is* its valid default, and an un-activated
mix is simply not in effect). Both mix services are Scoped (see Pitfalls);
the rows also survive a reload (localStorage write-through), and the next
boot's hydration re-offers them — inert until re-checked.

**`MixDisplay`** (Quiz/) is the wording SSOT: kind labels (the panel's
picker), full category labels (the Quiz page's mix notices), the
composition summary those notices lead with (`CompositionSummary` — "Your
quiz has N problems: 195 Never seen + 5 Ever got wrong.", every entry's
actual draw in declared order, zero-draw entries included), and the
refusal reason (Home's Start and Done's Restart render the same
capability/status rule — neither page hand-words it).

**Honest notices, all three** (a fourth — the *signal early* won't-apply
advisory — was retired when the panel became stats-gated, which made its state
unreachable; don't re-add it, it has no trigger left). (1) *Gate late*: a
refused weighted Start/Restart renders an actionable `role="alert"` with the
reason and the one-click per-run override ("Start without mix" / "Restart
without mix"); the mix rows and the checkbox are kept either way, and the
notice says so — its keep-your-mix escape is *uncheck*, not *Clear mix*,
because Clear now genuinely deletes the rows (screen-follows-storage). The
reachable refusal is **stage 2 over a file that changed after the pick** — the
pick-time probe found a readable record (or the panel would not have been
offered), and the Start-time bind then didn't. Stage 1 can no longer meet a
mix in effect through the UI at all, since #87 gates the panel on the same
predicate stage 1 reads. (2) *Composed-to-zero*: Home's empty-result branch keys on
`LastComposition is { DrawnCount: 0 }` for mix-aware wording, parallel to
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

**Both mix notices retire on the first submitted answer** — *or* on a click,
like every notice on the page (§ Dismissible notices). They say how *this*
quiz was built — worth reading before answering, stale chrome after — so
`Quiz.Submit` dismisses them once an answer lands, checker or cube alike.
Three deliberate choices: **dismissal, not deletion** (the controller's
telemetry is untouched — a presentation concern must not destroy load-bearing
state); **a scoped holder (`QuizNoticeDismissal`), not a page field** (*Show
stats* is a mainline mid-quiz gesture and returning re-instantiates `Quiz`, so
a field would resurrect a dismissed notice); **keyed on the composition
instance** (`ReferenceEquals`, never `==` — the record's value equality would
keep an identically-drawn Restart dismissed; each Start/Restart builds a fresh
`MixComposition`, so the next run's notice shows again with **no reset call
site** to forget). The trigger is `Controller.Review is not null` after the
submit call, not the call itself: both mutators no-op under the transition
gate, and dismissing on a submit that scored nothing would drop the notice
with no answer given; the predicate also covers an off-list play. **Skip is
deliberately not a dismissal** — it moves past a problem without answering it.

### Dismissible notices — `QuizNoticeDismissal` (issue #41, `SPEC-quiz-view.md` §4)

**Every notice on the Quiz page dismisses on a click**: the mix composition
notice (both framings), the stats-context degrade notice (`LoadFailed`'s
polite one, `WriteFailed`'s assertive one), and the stats-retirement report.
Dismissal is §4's answer to the
board space they cost, the mode being forbidden from suppressing them — the
ruling and its reasoning are the spec's.

**Per occurrence, transient, app-scoped.** The holder generalizes the old
composition-only `MixNoticeDismissal` by adding a **slot key** (`QuizNotice`)
and nothing else: one dismissal per notice, so dismissing one never dismisses
another. Every slot is keyed by **reference identity of an occurrence token**
— one rule, one kind of token — which is why the stats side needed a token to
exist at all:

- **Composition** → the `MixComposition` instance (unchanged).
- **StatsContext** → `QuizStatsStore.StatusOccurrence`, an **opaque object
  whose only meaning is identity**, replaced at the top of `BeginQuizAsync`
  (the context is re-derived) and in `SetStatus` on a real transition.
- **StatsRetired** → `QuizStatsStore.StatsRetiredOccurrence`, the same kind of
  token but **nullable — the token is the flag**, non-null only on a run that
  actually set a v1 file aside, so no companion boolean can disagree with it.
  Its own slot rather than a share of `StatusOccurrence`, because it can be
  showing *beside* a degrade notice: a later `Ready → WriteFailed` mints a
  fresh status token, which must not resurrect a retirement report already
  read. And deliberately not a `QuizStatsStatus` value — after a retirement
  the context is `Ready` and can still fail its next write, so retired-ness is
  orthogonal to the condition `Status` reports; folding it in would make every
  `== Ready` site grow an "or retired" clause. `Done` mirrors it
  non-dismissibly, as it mirrors the degrade notices.

Keying the stats notice on the `Status` *value* gets two real cases wrong: a
mid-run `Ready → WriteFailed` is a new thing to say, and **a second quiz bound
against the same unreadable file is also a new thing to say** — that run
records nothing either, and `SetStatus` reports no transition for it (hence
the bind-side replacement, not just the transition-side one). A generation
`int` would work for the store but would force the holder to compare two kinds
of token by two rules, and value equality is exactly the trap `MixComposition`
documents avoiding.

**The affordance is deliberately two things**: the whole alert is the click
target (large, low-vision-friendly — this arc's reason for existing) *and* a
standard Bootstrap `btn-close` renders inside it, because a bare clickable
region is undiscoverable and carries none of the keyboard / screen-reader
semantics. The button's click is `stopPropagation`'d; both routes call the
same idempotent `Dismiss`. **Never `data-bs-dismiss`** — it removes the node
behind Blazor's back, leaving the renderer's tree disagreeing with the DOM.
Nothing here is persisted: a dismissal is transient by design, and a reload
has no quiz to come back to.

### `ShuffleOption` — the "Shuffle order" toggle holder

The Scoped holder (see Pitfalls: resets on full reload) for the **"Shuffle
order"** checkbox on `Home` — a sibling of `PickedProblemFolder` and
`AppliedFilter`. Surface: `bool Enabled` (private setter) + `Set(bool)`.
`Home.razor` writes it on the checkbox's `@onchange`; the
`ProblemSetSourceFactory` reads `Enabled` at **invocation** time
(`StartAsync`) — the same read-live-at-Start discipline as
`PickedProblemFolder`. **Presentation-only, and off the start gate**:
shuffling changes only the *order* decisions are presented in, never which are
*admitted*, so it is not folded into `FilterConfig` and plays no part in
`CanStart`; a checkbox has no half-edited intermediate state, so every toggle
is a complete, immediately valid choice with nothing to "apply". **Disabled —
never rewritten — under an active mix** (the suppression itself lives in the
factory; see Pitfalls): `Enabled` keeps the user's value untouched, so
clearing the mix restores the prior preference (pinned).

### `QuizLiveMarker` — the reload-reset honesty marker

The app-scoped service recording that a quiz is **live** in this tab, backed
by the browser's `sessionStorage` through `IJSRuntime` — BgQuiz's first
JS-interop *service*, encapsulated because it has a lifecycle spread across
two pages and a storage constraint worth stating once. This is the **honesty
slice of reload-resume, not resume itself**: a full reload reboots the WASM
runtime and silently discards all quiz state; the marker is the one thing that
survives, so a fresh boot that finds it can *explain* the loss. Surface:
`MarkLiveAsync()` / `WasLiveAsync()` / `ClearAsync()`. Lifecycle:

- **Set wherever a quiz becomes live**: `Home` on a successful Start —
  *after* the empty-result guard, so the no-match path never marks — **and**
  `Done` on *Restart*, which makes a quiz live again from the same pipeline
  (without the Restart writer, a reload during a restarted quiz falls back to
  the old silent reset — a one-click-wide hole in the guarantee).
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

**`StorageKey` is `internal`, and named for its sibling.** `Help`'s data
section names this entry to the user, so the key is rendered from here rather
than typed as prose — the discipline `QuizStatsFile.FileName` established. It
is widened exactly as far as that one doc surface needs — `internal`, never
`public`. The name matches `MixDraft.StorageKey` deliberately: the two render
side by side in that section, and a documented pair reading `Key` /
`StorageKey` invites a reader to look for a distinction that isn't there.

### `QuizSettings` — the user settings service (issue #30 leg 1)

The app-scoped service behind `Settings.razor`, owning four settings and the
one `localStorage` entry (`xg_quizSettings`) they persist in: the home-board
side, whether that side re-rolls per problem, whether the board is maximized
while answering, and whether the navigation panel stays folded. Every change is
**recorded and persisted the moment it is made**; when it becomes *visible* is
a separate question, and the fold answers it differently (§ The fold it cannot
apply itself, below).
**Defaults reproduce the app that shipped before it existed** —
home board right (the producer's own `DiagramRequest.HomeBoardOnRight`
default), no randomization, no maximized board, panel unfolded — so a user who
never opens the page sees no change.

**No draft, no commit, no dirty flag, no `Changed` event.** Nothing here is
composed into a quiz at a Start gesture, so there is no half-edited state to
guard and no gate to derive — the reasoning `ShuffleOption` already records,
and the lifetime split that produced finding (AK)'s wedge is precisely what
this service must never grow. The page binds straight to the properties and is
the only component rendering them, so `MixDraft`'s state-container notify
plumbing buys nothing here; add it only if a real second consumer appears.

**Hydration.** `EnsureHydratedAsync` is idempotent (the `MixDraft` pattern) but
needs no stale-read generation guard — settings have no per-setup lifecycle, so
there is no `Discard` for an in-flight read to land behind. **`Home` kicks it
off**, since every quiz begins there; `Quiz` awaits the same cached task, by
then already completed and so provoking no extra render pass. That ordering —
not a render gate — is what keeps the board from painting on the default side
and flipping a frame later. `Settings` gates its own controls on hydration,
for the one visit that could see it pending: a cold deep link.

**The wire format is a two-language contract.** The payload is hand-written
with fixed property names (the `QuizMixJsonConverter` posture) and pinned
**byte-for-byte** by `QuizSettingsTests` — because `navFold.js` reads
`keepNavigationPanelFolded` out of it with no compiler checking either end.
Reads are **tolerant**, unlike the mix's fail-loud converter: a format two
readers share and later legs will extend must survive a missing field (that
setting's default), an unknown field (ignored), a non-boolean value (that
field's default), and anything that isn't a JSON object (every default). See
Pitfalls. **Field order is append-only** — `maximizeBoardWhileAnswering`
joined at the end, after the fold field, whatever the C#-side grouping — so an
older build's bytes stay a prefix of a newer one's and the pinned literal's
diff reads as "a field was added" rather than "the format moved under the
applier". Extending the format needed **no migration and no version stamp**:
the tolerant restore gives a payload predating a field that field's default,
and the default reproduces the pre-field behavior by construction.

**The maximize-board setting** (issue #41 / `SPEC-quiz-view.md` §3) is the one
field here that reverses a documented invariant rather than choosing between
equals. This service records the *choice* only — the composition it produces is
`Quiz`'s derivation (§ `Quiz.razor`), and **nothing stores "currently
maximized"** (§6: a second copy of the view mode is a divergence from the
model). It is a **choice, never consent** in §3's sense, so nothing here
expires it. The **Settings checkbox is its sole
control** (fork D, ruled) — an on-page toggle would be a second write surface
for one fact and would force this service to grow the notify plumbing its
contract defers until a real second consumer exists. Its fine print states the
ratified consequence (the board is deliberately a different size while
answering than while reading), so a user who sees the board move reads the
feature working rather than a bug — the same posture the fold row takes toward
its deferral.

**The side, and the roll.** `QuizController.RandomHomeBoardOnRight` is a coin
flip taken **unconditionally**, beside the assignment of `Current` and after
the pass-skip — one roll per problem the user actually sees, held steady across
submit, review and Redo, reset per run, never persisted, so the controller
knows nothing about settings. The composition rule lives in exactly one member,
`QuizSettings.EffectiveHomeBoardOnRight(randomSide)`, reaching the renderer
through a single `Quiz.HomeBoardOnRight` property that both request builders
read — so the three render branches (play answering, cube answering, solution)
cannot disagree. A board that flipped in some views and not others is the
failure mode, and the kind that survives review by looking like three correct
call sites.

**The fold it cannot apply itself, and the one it deliberately won't.** The
service owns and persists the value; restoring the fold is `navFold.js`'s job
(§ The host layout). The applier is invoked with the value **as an argument**,
so the setter carries no ordering dependency on its own storage write — but it
is invoked **for the unfold direction only** (finding #50, ruled 2026-08-03):

- **On → deferred.** The setting describes how pages *start*; folding the page
  the user is standing in strands them behind a panel that just vanished, with
  the checkbox they are looking at as the only clue why. Deferring needs no
  code — the choice is already in storage and the `enhancedload` handler
  applies it on the next navigation, where it also self-demonstrates. The
  control's fine print states the delay so "deferred" cannot read as "broken".
- **Off → immediate.** The user is asking for the panel back, and with it folded
  every navigation that would apply the new value is behind its own folded-away
  links. Without the seam the setting would be a one-way door.

The asymmetry is pinned three times over: at the service seam
(`QuizSettingsTests`), from the control (`PageTests`), and in a real browser
(`SettingsTests` — toggle on, panel stays, navigate, folded).

### Pages

- **`Home.razor`** — the setup page; wiring notes below, contracts in their
  owning sections.
  **Pick.** A **"Choose folder…"** button (`#pickProblemFolder`) above the
  filter surface, plus a hidden, always-rendered
  `<input type="file" webkitdirectory>` fallback the same button opens where
  File System Access is absent (a plain `<input>`, not `InputFile` — the JS
  module reads the FileList itself for `webkitRelativePath`; always in the
  DOM so the e2e suite can drive it directly). The whole pick runs behind
  `IFolderAccess` (§ Folder picking); the page never touches raw interop and
  the pick lands in `PickedProblemFolder`. The two no-folder outcomes each
  leave the holder clear and each show their own polite notice
  (`_cancelledPickNotice`, `_emptyFolderNotice`) — no pick ever returns the
  user to an unchanged page with no account of what happened; the capability
  drives the pick-time stats status notice. The cancelled notice is
  deliberately **cause-agnostic** (§ `IFolderAccess`) and **both mechanisms
  reach it by different routes**: only `PickFolderAsync` reports cancellation
  as an *outcome*, while a dismissed `webkitdirectory` picker fires no change
  event at all, so the fallback's dismissal comes through the input's own
  `cancel` event (`@oncancel` → `HandleFallbackCancelled`). That route is
  best-effort: where a browser never fires `cancel` the outcome degrades to
  silence — no wrong statement, only a missing one (bUnit pins the binding,
  not the browser's delivery). The pick label renders straight from
  `PickedProblemFolder.Summary` (the SSOT) under a markup-side
  `"Problem folder:"` caption — the caption frames the line on *this* screen
  and stays out of `Summary`, which other surfaces re-derive from — with a
  **Clear** affordance beside
  it bound to `EndCurrentSetupAsync`. Clearing is safe mid-quiz and left
  unguarded on purpose — files are read only at Start time and the clear
  touches only the JS *picked* slot (pinned).
  **Pre-pick advisories, both ungated by any probe of the *pick outcome*.**
  The **supported-browsers statement** (`FolderPickDisplay.SupportedBrowsers`,
  beside the pick button) is gated only on "no folder held": where the pick
  isn't supported the button is a *dead entry point* and no code path ever
  runs to say why, so the readers it exists for are exactly the ones a
  capability probe would exclude. The **two-step permission guidance** covers
  both rungs of the ladder (§ Folder picking) as an ordered list naming what
  declining each costs, shown **from page load** on
  `_fsAccessAvailable && !Folder.HasFiles` — continuous through an in-flight
  pick, hidden once a folder is held, back after **Clear**, and deliberately
  still there after a *cancelled* pick. Its gate is browser **capability**
  (`_fsAccessAvailable`, an init-time `SupportsDirectoryPickerAsync` snapshot
  whose only consequence is whether advisory guidance renders — the
  per-gesture probe in `PickFolderAsync` stays the authoritative mechanism
  fork), which keeps the note from promising prompts to fallback browsers.
  It is **static, not stage-aware** — stage-swapping was *declined, not
  deferred*: it needs a Blazor render to land between two prompts that arrive
  seconds apart on WASM's single thread. It promises no *number* of prompts
  and **quotes no browser's prompt text** (see Pitfalls).
  **Progressive disclosure.** Everything downstream of the pick — the
  `FilterSurface` (one producer composite: saved filters rendered above the
  filter panel so load-then-refine reads top-down, plus every saved-filters
  notice), the match-count line, the `MixPanel`,
  the shuffle checkbox, and Start — renders only once `Folder.HasFiles`, which
  also makes the filter half of the gate true by construction — and which
  makes the composite's mount lifecycle part of the choreography (see the
  setup-end paragraph below and § `AppliedFilter`). Home binds the composite:
  the shared `AppliedFilter` holder, the app-scoped `FilterRestoreNotice`
  (bound and nothing more — § `AppliedFilter`), `Source = CurrentFilterSource`
  (the one mint — inside this gate a folder is always held), `Storage` = the Scoped
  `PickedFolderFilterStorage` while the capability exposes a readable handle
  (`null` under `BrowserUnsupported` ⇒ no saved-filters section),
  `CanPersist = (Capability == Enabled)` with `PersistDisabledReason` from
  `FolderPickDisplay.WriteAccessNotGranted` — **capability-only, deliberately
  not the mix predicate** (see Pitfalls) — and the two re-raised
  panel-shaped events. The `MixPanel`
  carries a *second* gate, `StatsStore.CanWeightMix` (§ `QuizStatsStore`; can
  save stats **and** has some), and a `@key` on
  `Folder.PickGeneration` (see Pitfalls: load-bearing); it raises no events —
  gestures flow through the injected `MixDraft` and `MixConsent`, whose
  `Changed` events Home subscribes to (unsubscribed in `Dispose`; the consent
  handler also retires a standing weighted-start refusal) so the derived
  gates re-render. The shuffle checkbox binds to
  `ShuffleOption` (§ that section). Start is gated on **four** conditions,
  each with its own sibling hint, read from per-app scoped services (plus the
  advisory summary) so the gate survives navigation:
  `CanStart => FilterInEffect is not null && Folder.HasFiles
  && _matchSummary is not { Total: 0 } && EffectiveMix is not null`, where
  `EffectiveMix => MixConsent.Applies ? MixDraft.Build() : QuizMix.Empty` —
  derived per render, never stored (§ MixPanel / MixDraft / MixConsent).
  **The pool gate is known-zero only** (found dogfooding, ruled): a resolved
  count of 0 darkens Start with "No problems match the filters — adjust and
  re-apply them to enable Start."; a null or still-computing summary gates
  nothing (no async dependency in the gate), and the no-match outcome notice
  stays the backstop for a Start racing the count. The mix surface — panel,
  checkbox, row editing — is deliberately **not** pool-gated; composed-to-zero
  stays the backstop for a non-empty pool whose mix reaches nothing. The mix
  hint is the ruled "Mix applies but isn't valid — fix it or uncheck."
  (checked + invalid — the only mix state that gates).
  **Match summary and answer-type breakdown** (umbrella #35). On Apply, Home
  calls `Controller.SummarizeMatchesAsync` (§ Pre-Start match summary) and
  holds the returned `AnswerTypeDistribution` in `_matchSummary`. Home owns
  only display and lifecycle: a request id stamped per Apply discards a stale
  result landing after a newer Apply, and the summary clears on any filter
  edit or new/cleared pick. One `role="status"` region carries all of it — the
  count from `Total`, the mix caveat, and the breakdown — so a screen reader
  gets the pool and its make-up in one announcement. Settled rules:
  - **The count is a count of distinct positions** — "N decisions match your
    filters" is N *positions*, with no re-picked copy hiding behind the number,
    and it cannot disagree with what a capless quiz then serves (§ Pre-Start
    match summary for why that agreement is structural).
  - **The count is filter-only, and says so when a mix is in effect.** With
    `MixInEffect` (`EffectiveMix is { IsPassthrough: false }` — live per
    keystroke, since effect follows the screen) a caveat renders in the same
    region: the quiz is drawn from these matches rather than presenting all
    of them, so it **can** be much smaller. Hedged, not "will be" — a capless
    *Everything else* mix can legitimately draw the whole pool. A **pre-Start
    composition preview is deliberately not built**. `MixInEffect` is the
    single predicate behind both this caveat and (via `MixOwnsOrder`, a named
    consequence so the shuffle markup says *why*) the shuffle checkbox's
    disabled state.
  - **The breakdown is compact and always open, and every bucket renders,
    zeros included.** Not a disclosure — click-to-open would hide the finding
    behind a gesture nobody knows to make — and the absent categories *are*
    the signal, so a bucket at zero is a result, never a dropped row.
    `AnswerTypeDisplay` enforces the always-five; the page renders what it is
    given. One exception: an **empty pool renders no breakdown** (five zeros
    under "0 decisions match" is noise, not honesty). `PageTests` pins both.
  - **`Total` is not a sixth bucket** — it is the count line's number, and
    repeating it would put one figure on screen under two meanings.
    `PageTests` pins the buckets summing to the rendered count, the invariant
    that fails the moment a second computation appears. The lead-in is named
    for its axis, leaving the region free for issue #3's composition preview;
    nothing is built for that, the name is simply not claimed.
  The first count after a pick parses the corpus once (warming the cache), so
  `_isCounting` folds into the same busy boundary as the transition gate,
  which also serializes the count against a Start. Help documents the count in
  its own prose — a shared constant is earned only when two surfaces render
  the same sentence, which these don't.
  **Start.** Hands `FilterInEffect` + `EffectiveMix` (the on-screen draft's
  build when consented, the passthrough otherwise; the null backstop guards
  programmatic dispatch) to
  `Controller.StartAsync` and checks the returned outcome **before** the
  empty-result `IsFinished` check (see Pitfalls: a refused start touches no
  quiz state, so `IsFinished` is stale): `MixRequiresStats` renders the
  actionable refusal alert (`_mixRefused`, reason via
  `MixDisplay.RefusalReason`, the "Start without mix" per-run override, a
  pointer to unchecking *Mix applies*), and the mix-aware composed-to-zero
  wording rides the no-match branch. Since #87 that refusal is near-unreachable
  from the UI — where the mix predicate is false the panel is hidden and the
  pick revoked the consent, so what can still reach it is a stats
  file that stopped being readable between the pick and the Start.
  **A pick ends the current setup — at the click.** `EndCurrentSetupAsync`
  is the single reset behind *both* gestures that end a setup (the pick
  gesture and the `Clear` affordance — they encode the same decision, so they
  share one spelling): folder holder + JS picked slot, the mix consent
  (`MixConsent.Revoke`) and mix draft (`Discard` — the stored rows survive),
  the applied filter
  (`AppliedFilter.Clear` — the one line of filter choreography left host-side,
  see § `AppliedFilter` for the unmount-gap ruling), and every pick-scoped
  notice and the match summary. The saved-filters context needs no line — it
  dies with the composite the closing `HasFiles` gate unmounts (§
  `AppliedFilter` for that unmount/re-mount ruling). Nothing selected
  against the previous corpus can be assumed
  to mean the same thing against the next one, so **a pick re-gates Start** —
  never inherited across one. It runs at the **start of the gesture**, before
  the mechanism fork, so the screen is back at its initial no-folder state
  before the OS picker appears; a `StateHasChanged()` plus the awaited
  picked-slot interop lets that paint land first. Settled consequences: a
  **cancelled pick loses the folder that was held** (the gesture ended the
  setup whatever the picker then returned), and a successful pick re-mounts
  the composite, whose panel's `localStorage` restore re-stages the persisted
  config as dirty on **every** pick — accepted, and routine (staged without a
  commit, so it is shown but never claimed as applied; the same hands-off
  treatment `MixDraft.Discard` gives the stored mix). Two things are
  deliberately *not* reset: `ShuffleOption` (presentation-only preference) and
  the lifetime-stats slot, whose whole point is to *resume* when its folder is
  picked again. `PageTests` pins the reset, the at-the-click timing (sampled
  from inside the fake's picker), and the cancelled-re-pick loss.
  **Busy affordances.** The whole setup surface sits inside one
  `<fieldset disabled="@IsBusy">` — the native element disables every form
  control within, including the Apply buttons *inside* the imported
  `FilterSurface`/`MixPanel`, which expose no disabled parameter — and the page
  container carries `app-busy` (`cursor: progress`, `app.css`) on the *same*
  predicate, so cursor and disabled controls cannot disagree. `IsBusy` unions
  `Controller.IsBusy` (the transition gate), `_busy` (this page's own
  foreground work — the pick's scan), and `_isCounting`, which keeps its own
  flag because it also owns a message and a stale-request id;
  disabling the surface during the count also prevents a Start racing its
  parse. **Raising it is single-sourced**: `EnterBusyAsync()` sets `_busy`,
  calls `StateHasChanged`, and **yields**, while `RunBusyAsync(work)` is the
  whole-operation form (enter, run, lower in a `finally`). Every site uses one
  of them — the match count, the fallback pick's collection, and the FS-Access
  pick, whose raise point sits *inside* `IFolderAccess.PickFolderAsync` (handed
  `EnterBusyAsync` as the `onPickAccepted` hook — § Folder picking) while its
  lowering belongs to the whole gesture's `finally`. Cancelled picks never
  raise it. Why the yield is load-bearing, and why the pins sit where they do,
  is in Pitfalls.
  **Callback wiring.** No `StateChanged` subscription — the page's own
  suspended handlers trigger the re-renders. The composite mediates the
  holder *before* re-raising, so Home's handlers own only host side effects:
  `OnFilterConfigChanged` clears the start/no-match notices and counts;
  `OnAppliedStateChanged` manages only the match summary (the holder is
  already correct by the time it fires — § `AppliedFilter`). The count is
  single-sourced in `ShowMatchSummaryAsync`,
  called from both — and the applied-state handler skips it when a summary is
  shown or in flight, because a commit
  raises both callbacks and would otherwise parse the corpus twice.
  **Mix activation is sequenced behind Apply Filter** (umbrella #45, Fork A).
  The `MixPanel` is handed `CanActivate="MixActivationEnabled"` plus the
  reason sentence; `MixActivationEnabled => FilterInEffect is not null` — "a
  filter is in effect for the picked folder *right now*". Settled semantics,
  each half load-bearing:
  - **UX sequencing only, never a data-flow rule.** The mix composes over the
    filtered pool at *Start*, not at activation, so mix-before-filter was
    always legal; what it wasn't is legible. The gate states the dependency
    direction and the hint says *why* ("the mix draws its problems from the
    filtered pool"), because the bare rule read as arbitrary.
  - **A dirty filter revokes the check gesture** — the same fact Start reads
    (`SPEC-filtering.md` §5 Fork A, ruled strict); re-applying restores it,
    and a new pick revokes it by construction (the generation bumps). No
    "has this corpus been filtered?" fact exists anywhere in the model (§3),
    and Fork A records the cost this accepts. **The gate darkens checking
    only**: a checked box stays operable (unchecking is never sequenced away)
    and the bit is untouched.
  - **Nothing about the mix's own lifetimes takes part.** The gate reads the
    *filter* and the *pick* only, per render — which is what keeps it clear of
    the (AK) wedge, whose cause was a stored judgement outliving its inputs.
  - **Clear mix stays ungated in every state**: it is a way out (deliberate
    row removal), never a way into anything, so sequencing it would only
    manufacture dead ends.
  **Failure and outcome banners.** Pick failures (unexpected `JSException`,
  caps exceeded — `_pickError`) and start-time exceptions
  (`FilterConfig.Build()` validation, source construction — `_startError`)
  surface as banners instead of faulting the WASM app. A *successful* Start
  that leaves the controller already `IsFinished` stays on `/` with a neutral
  no-match banner rather than navigating into a `0/0` `/quiz` → `/done`
  bounce — a post-Start check, not a pre-flight enumeration: `StartAsync`
  already advances to the first showable problem, so `IsFinished` immediately
  after it *is* the empty-result signal. Two indistinguishable causes flip it
  (zero filter matches; every match auto-skipped as a pass position), so the
  wording claims neither. `_noMatchNotice` is a sibling field to
  `_startError`, distinct because it reports an *outcome*, not a *failure*:
  `alert-warning` + polite `role="status"`, not `alert-danger` + assertive
  `role="alert"`. Both are genuinely per-visit state, so component fields (see
  Pitfalls); `PageTests` pins both flip paths and the over-trigger guard. A
  **third** per-visit notice (`_showReloadNotice`, polite) fires on a boot
  that finds the `QuizLiveMarker` set with no live controller. The page
  **footer** carries `AppInfo.Version` (in a `#appVersion` span) and the beta
  feedback `mailto:` from the same `AppInfo` (§ that section).
  **Back to quiz** (issue #58). The same conditional button `Help` and
  `Settings` carry — same `HasStarted && !IsFinished` predicate, same markup,
  same words — closing the last page reachable mid-quiz that had no way back. It
  sits **outside** the busy `fieldset` (it navigates and drives no transition, so
  it follows the Show-stats convention of staying live while the page works) and
  outside the progressive-disclosure gate, so a mid-quiz Clear cannot take the
  way back with it. Nothing else is added: a mid-quiz Home visit is already safe
  by design — files are read at Start only, and Clear touches only the picked
  slot — so there is no guard and no warning. `PageTests` pins the predicate's
  two halves and the fieldset-independence; `MidQuizNavigationTests` drives the
  round trip in a browser.
- **`Quiz.razor`** — mirrors the controller's three-state flow, branching on
  `Controller.Review`. **Answering** (`Review` null): routes the board region
  by `Current.Decision.IsCube` over
  `DiagramRequest.FromDecisionData(Current, DiagramMode.Problem)` — checker
  decisions to `BackgammonPlayEntry` (click-driven play assembly; strict on
  decision type, so the route must be exact — see Pitfalls), cube decisions to
  a **board-only** `BackgammonDiagram` (the cube answer is not entered on the
  board). Submit is a synchronous handler gated on the relevant answer being
  held: a play via `OnPlayCompleted` → `_completedPlay`; a cube via the
  `BackgammonCubeActions` radios in the action row, whose `@bind-Value` keeps
  `_completedCube` current (re-fires on every change, so the user can revise
  before Submit). Both fields reset on every transition. The action row varies
  by kind: cube places the radios ahead of Submit / Skip and has no Undo (no
  partial-move state); checker keeps Undo last / Undo all (clearing the
  latched play, since the component does not notify on undo). **Both Undo
  buttons are disabled only while `Controller.IsBusy`** — deliberately *not*
  on `_playEntry` being assigned (see the `@ref`-timing pitfall).

  **There is ONE action row (`.action-row`), shared by both states** — only the
  leading answer instruments branch. Its trailing cluster (`.action-row-tail`,
  which carries the `ms-auto`) is the XGID badge, then "Show stats", then
  **"End quiz"** (issue #57 — § `QuizController` for what that transition does).
  End quiz is one-click and immediate, ruled: the confirmation the issue first
  sketched was dropped, so its placement at the far end of the row — as far from
  Submit / Continue as the row allows — *is* the mitigation, and `PageTests` pins
  it there; the badge therefore *opens* the cluster rather than closing it. The
  shared row is what keeps the two states' row heights equal (and so the board's
  flex remainder unchanged) **by construction** — the claim the old two-row
  arrangement had to make by hand. The `ms-auto` lives on the cluster, not on its
  first child: `XgidLabel` renders nothing for a decision with no XGID, so a
  first-child `ms-auto` would be a different element per problem. The semantic
  class names are the pins' hooks — the Bootstrap utilities beside them still do
  the layout. **Review** (`Review`
  set): a read-only `BackgammonDiagram` in `DiagramMode.Solution` plus
  Continue / Redo / Show stats, built with `DiagramRequest.Builder.From(...)`
  and then the user's marks overridden from `Review` — `UserPlayIndex` for a
  play (`-1` off-list draws no marker), or `UserDoubleError` / `UserTakeError`
  for a cube. `FromDecisionData` is **not** used here: it defaults those marks
  from the .xg-recorded player, not the quiz user. The review diagram's
  `OnDiceClicked` is bound to the same `ContinueAsync` handler as Continue
  (safe under the transition gate). Redo falls back to the answering branch on
  the same problem; no explicit reset or `@key` is needed (see Pitfalls).
  Above either action row sits a **fixed-height status strip**
  (`.status-strip`, `app.css`): a one-line legend slot and a two-line-clamped
  verdict band — a neutral prompt while answering; the legend
  (`* played · † your answer`) and outcome-coloured verdict at review. Its
  fixed height, and the board sizing that rides on it, are in Pitfalls.
  **Below the action row — the page's bottom chrome — sits the `ScorePanel`**
  (`SPEC-quiz-view.md` §5, issue #41): reference material read between
  problems, not while deciding one, so it sits below the controls the user is
  reaching for and leaves the chrome nearest the board to the chrome that
  speaks to the problem in hand. It stays *inside* `.board-chrome`, which is
  the move's whole no-interaction claim — reordering within the measured
  `flex: 0 0 auto` block leaves its total height, and so the board's flex
  remainder, unchanged. `Done` and `Stats` render their own `ScorePanel` with
  their own parameters and are untouched.

  **The XGID has one home: the bottom row** (`SPEC-quiz-view.md` §4's
  2026-08-13 amendment, issue `halheinrich/backgammon#98`). `XgidLabel` — the
  selectable-text-plus-copy badge, the DOM counterpart of the label the
  PDF/PPTX/PNG exporters bake in — renders at **one** site, opening
  `.action-row-tail`, present in both view modes and both states. It is off the
  canvas entirely (§4 for why), so the three board branches render the producer
  components bare.
  Consequences worth knowing before touching it: **one site, not three** (a
  per-branch badge is how one composition ends up rendering it differently);
  the badge is **in-flow and positions nothing**, so the old `position:
  relative` host requirement and the `container-type: inline-size` cqw anchor
  on `.board-container .bg-diagram` are both gone (pinned retired — see
  Pitfalls); and it must never be the element carrying the cluster's `ms-auto`,
  because it renders nothing for a decision with an empty `Xgid`. The
  producer's `Overlay` slot and the exporters' baked corner label are
  untouched — this is the quiz page's placement choice only.

  **The visible text is capped at `2.5rem`, and the cap is a board-size
  contract** (`AppCss_XgidLabelText_StaysCapped`). Uncapped, the badge wraps the
  action row wherever the board is height-bound — and because a cube row is
  wider than a checker row (four radios), the wrap width depends on the
  **problem kind**, which is per-problem board jitter inside Normal view and
  exactly what `SPEC-quiz-view.md` §2 forbids. The visible text
  does not try to show the value: 40px is `XGID=` (32.7px in this font) plus the
  ellipsis (6.5px), so it renders exactly `XGID=…` — the value's own
  self-labeling prefix, which doubles as the caption for the **icon-only** copy
  button beside it. **Re-measure that 40px if the font changes**; it is the whole
  basis of the cap. No pill either — in a row of buttons the padded chip was
  decoration priced in board pixels. §2 records the measured result the cap
  buys. Nothing is lost with the
  pixels: Copy writes the full value, `user-select: all` selects the whole
  string, `title` reveals it on hover, and the complete text is in the DOM for a
  screen reader. A horizontal-scroll affordance was considered and **declined**
  by the umbrella — tooltip plus copy covers the read path.
  **Not this rule's to fix:** the row's own wrapping below ~1270px (cube) /
  ~900px (checker) predates the badge and is `halheinrich/backgammon#99`. The
  badge adds a uniform ~80px to both thresholds (cube 1350, checker 980), so a
  cube-only divergence band survives between ~1280 and ~1350.

  **The maximize-board mode** (issue #41 / `SPEC-quiz-view.md` §4). With the
  user's `QuizSettings.MaximizeBoardWhileAnswering` on, the *answering*
  composition renders **the board and the action row and nothing else below the
  notices**: score panel and status strip suppressed (and with them the
  "Problem N of M" indicator and the neutral prompt — ratified consequences),
  and the board on `AspectPreset.BoardOnly`. **Both legs are required** — §2's
  measurement is what rules that, chrome suppression alone changing the rendered
  canvas not at all. Review **normalizes** to
  the full composition, because it needs the panel and needs it filled.

  The mode is **a pure derivation and stays one**: `MaximizedAnswering` is
  `setting && Review is null`, re-derived every render, and `BoardOptions`
  picks the canvas from it. **No holder, no page field, no "currently
  maximized" bit** (§6) — that second copy is what would let the chrome and the
  canvas disagree about which composition is on screen. Every transition falls
  out with no special case: Submit normalizes, Redo and Continue re-maximize,
  Undo never leaves answering. `BoardOptions` replaces the old shared
  `_diagramOptions` field and applies the `HomeBoardOnRight` pattern to the
  second thing all three board branches must agree about — and it is where the
  producer's throw is prevented **structurally**: `BoardOnly` is rejected for a
  `DiagramMode.Solution` request (`ArgumentException` from `RenderSvg` and
  `GetHitRegions` alike), and the derivation cannot select it while a review is
  rendering. The action row keeps **every** instrument, cube radios included,
  so every answer stays makeable without leaving the maximized view. Notices
  are deliberately *not* gated on the mode — see § Dismissible notices.

  **Busy affordances:** every transition-driving button
  (Submit, Skip, Undo, Continue, Redo, End quiz) disables on `Controller.IsBusy`
  and the container carries `app-busy` — the honest mirror of the gate, which
  would no-op the clicks anyway; "Show stats" stays enabled (navigation only).
  Subscribes to `Controller.StateChanged` **and** `QuizStatsStore.
  StatusChanged` in `OnInitialized`, unsubscribes from both in `Dispose`;
  redirects to `/done` when `IsFinished` flips.
  Above the board: the active-context stats notices (`LoadFailed` polite,
  `WriteFailed` assertive — the store subscription surfaces a mid-quiz write
  failure the moment it happens) and the mix notices from
  `Controller.LastComposition`, framed per § MixPanel's honest-notices list.
  Both are gated on `!Notices.IsDismissed(slot, occurrence)` and **both
  dismiss on a click** — § Dismissible notices. The `ScorePanel` carries
  "Problem N of M" from `Controller.ProblemNumber` / `ProblemCount`.
- **`Stats.razor`** — read-only mid-quiz stats view: the same `ScorePanel` /
  `ScoreBreakdown` pair `Done` shows, rendered against the live in-progress
  `QuizController` with honest mid-quiz wording ("Progress so far", not
  `Done`'s "Final"). Reachable only from `Quiz`'s "Show stats" button. Never
  calls Submit / Continue / Skip, so the round trip leaves `Current` /
  `Review` untouched — with the per-tab scoped controller that gives "resume
  where you left off" for free. Direct nav with no quiz in progress bounces to
  `/`; with it already finished, to `/done` — the same guards `Quiz` applies
  to itself.
- **`Settings.razor`** — the user settings page (issue #30 leg 1), a plain
  view over `QuizSettings` (§ that section for the contracts). Radios for
  the home-board side, checkboxes for randomize-per-problem,
  maximize-while-answering and keep-nav-folded; every control writes straight
  through, recording and persisting on the spot (the fold's *visible* effect
  defers by one navigation — § `QuizSettings`; the page's job in that split is
  the fine print that says so). The board's three rows share one fieldset; the
  fold's is its own. **No Apply button — pinned as a design constraint, not a
  coincidence:** an Apply is the front end of the draft/commit lifetime
  split behind finding (AK)'s wedge. The only page state is whether hydration
  landed, which gates the controls so none can paint a default the stored
  settings are about to overwrite. Reachable from the host `NavMenu` beside
  Help (`NavMenuTests` pins the link, as it does Help's); nothing else links
  to it, and the pages the settings affect deliberately carry no control of
  their own — for the maximize mode that is a *ruling* (fork D), not an open
  question; the broader mid-quiz-tweaking question booked on #30 still is one.
  It offers the same **"Back to quiz"** button `Help` does — same predicate,
  same markup, same words (§ `Help`) — copied rather than designed, because
  the two pages sit in the same position: reachable from any state, so neither
  redirects the way `Stats` does. It sits on the page and not in the nav panel
  because that panel renders statically and cannot know a quiz is live — the
  same constraint that put the fold applier in JS.
- **`Help.razor`** — end-user documentation. `PageTests` pins the full `h2`
  skeleton **in order**, so an edit cannot quietly drop or reorder a section.
  The order is the journey: a **Before you start** prerequisites lead (a
  folder of the reader's own `.xg` / `.xgp` files is required and BgQuiz ships
  none; the supported browsers; the two files BgQuiz writes) — it leads
  because everything after it assumes it — then **Your data stays yours**
  (§ below), then the six beats of the flow (pick folder → filters →
  answering → scoring → review → stats/done), with **Save filters you use
  often** and **Weight the quiz by your lifetime stats** between the filters
  and answering beats in the order the user meets them on Home (the mix
  section forward-references *Lifetime stats* rather than moving after it —
  journey order is the page's rhythm, and forward references are its idiom), a
  **Making a checker play** section inside the answering beat, a **Lifetime
  stats** section, and then the semantics a user cannot discover by clicking
  around — each owned by the section that implements it, and stated here in
  user terms only: what the match count counts and that a mix draws from that
  pool, the breakdown's exhaustiveness and what a zero means, pass-position
  auto-skip, off-list-as-skip, cube-as-two-decisions, the dice click
  advancing, the side panel's fold (§ The host layout — and see Pitfalls for
  what that note may say), and the reload reset. It closes with **Send
  feedback**.

  **Every documented constant renders from its SSOT, never as a literal** —
  file caps from `PickedFileLimits`, filenames from `QuizStatsFile` /
  `SavedFiltersDocument` (both saved-filters names: the canonical one in
  *Before you start* and the saved-filters section, and the legacy name in the
  fallback sentence the umbrella ruled in — a silently-read file would cut
  against the storage-transparency posture, and the fallback is a standing
  producer rule, so the sentence doesn't rot),
  the browser rule from `FolderPickDisplay` (rendered
  *verbatim*, so this and Home's line beside the pick button cannot say
  different things), feedback + version from `AppInfo`. The *Choose filters*
  section extends that discipline one tier up: it embeds `XgFilter_Razor`'s
  `FilterHelp` (render-only, parameterless) as its per-facet reference and
  writes **no facet prose of its own**, keeping only app-level framing
  `FilterHelp` cannot know. The breakdown paragraph applies it one tier down:
  it deliberately **does not recite the five bucket labels** — those are
  `AnswerTypeDisplay`'s copy, rendered on Home, and a second spelling here
  would drift the first time one is reworded (`PageTests` asserts their
  absence from the section). The checker-play section documents the one-click
  entry model organized **by click target**, mirroring the component's own
  dispatch so each bullet is exhaustive about one thing the user can click;
  its source of truth is BgDiag_Razor's `BackgammonPlayEntry` + BgMoveGen's
  `MoveEntryState` doc comments, and it deliberately says nothing about
  legal-click highlighting, which no shipped BgQuiz surface renders.

  Lives in the `.Client` (not a static host page) so a mid-quiz Help → Back
  round trip doesn't disturb the WASM runtime holding quiz state. Unlike
  `Stats` it **never redirects**: help is reachable from any state, including
  a cold visit or a bookmark; only the "Back to quiz" button is conditional,
  on the exact predicate `Stats` guards with (`HasStarted && !IsFinished`). No
  `StateChanged` subscription — nothing changes while the user reads. The host
  `NavMenu`'s Help link is the **only** entry point; `Quiz`'s action row
  deliberately gets no "?" button, because its fixed height is load-bearing
  for board sizing.

  **`Help`'s data section — "Your data stays yours".** Sits directly after
  *Before you start* and ahead of the flow: a reader deciding whether to hand
  over a folder wants it before doing so, not twelve sections later. It
  carries the ownership statement (the files are the reader's; parsed in the
  browser and never uploaded; no account, and the server it is downloaded from
  has nothing to receive them) and names **everything BgQuiz stores**, each
  from its owning constant:

  - `MixDraft.StorageKey` (`xg_quizMix`) — localStorage, the weighted mix as
    last well-formed on screen (the write-through's blob).
  - `QuizSettings.StorageKey` (`xg_quizSettings`) — localStorage, the
    Settings page's choices as one JSON object. Listed beside the mix (the
    other localStorage entry) so the sessionStorage one stays the trailing
    exception the paragraph after it explains.
  - `QuizLiveMarker.StorageKey` (`bgquiz.quizLive`) — **sessionStorage**,
    described as what it is: current-tab-only, invisible to other tabs, gone
    when the tab closes. Not an implementation detail to gloss —
    § `QuizLiveMarker` records why it must never become localStorage, and
    user copy claiming otherwise would be the same lie in a second place.

  Three things are **pointed at, never restated**: the two files written into
  the user's folder (*Before you start* already names them from
  `QuizStatsFile.FileName` / `SavedFiltersDocument.FileName`); the panel's own
  localStorage entries (one sentence in user terms plus a link into
  `FilterHelp`'s `#fh-what-is-remembered` — see Pitfalls); and the
  write-access caveat, a pointer into *Lifetime stats* — where the browser
  cannot write into the folder there is no record being kept, and the
  reassurance must not read as a promise that one is.

  Naming the three is only half of it: the section also says **what a reader can
  do about them** (issue #54). The route it names is the one a general reader
  already has — the browser's own setting for clearing what a site has stored,
  named by *what it does* and never by a menu path, since every browser words and
  places it differently (the claim class `FolderPickDisplay` rules out quoting
  for permission prompts). Devtools survive as a signposted trailing parenthesis:
  they are the only way to inspect the three entries individually — which is what
  makes the key names above findable — but they may never be the sentence's
  premise again, which is what the original wording made them. The paragraph also
  answers the question clearing site data actually raises for its reader (it does
  not reach the problem folder); that is the ownership point as a consequence,
  not a restatement of the writes-into-your-folder paragraph below.

  It then **draws the consequence** (issue #51, ruled 2026-08-03): closing the
  tab is safe at any moment, mid-quiz included. That belongs here and nowhere
  else — it is what the account above *implies*, and a reader told what is
  stored where will otherwise assume a quiz in progress is among it. **No
  button**, deliberately: a "finish and quit" control would invent an
  obligation the app does not have. `Done` carries a one-line echo beside its
  buttons, gated off the `LoadFailed` / `WriteFailed` statuses its own notices
  report: "nothing needs saving" printed under "your stats could not be saved"
  reads as a contradiction, and there the notice is the honest word. Composing
  rather than consolidating is the constraint — the nothing-leaves-your-machine
  claim was **moved** here out of *Before you start* and dropped from *Pick
  your folder*, so it is asserted once. `PageTests` pins the wiring (both keys
  from their constants; the section's `<code>` elements are *exactly* those
  two; the anchor link present; neither filename restated);
  `HelpAndTitlesTests` pins the phrasing as independent literals and clicks
  the anchor in a real browser.

  **`Help.PanelStorageHref`** — the anchor href is **computed**, never
  written as a bare `#fragment`. See Pitfalls (`<base href="/">`).
- **`Done.razor`** — final `ScorePanel` (Total) + `ScoreBreakdown`
  (four-way) + total problems shown + **Restart with same filters** /
  **Back to setup**, and — for the third exit, the one with no button — a
  muted line saying nothing needs saving (§ `Help`'s data section for the
  ruling and the gate). "Problems shown" is `PlayDecisions.Submitted +
  DoubleDecisions.Submitted + SkippedCount` — **not** `Total.Submitted`,
  which counts decisions and so double-counts each cube position (one Double
  + one Take). "Back to setup" is **navigation only** — the start-gate
  holders persist, so `Home` arrives armed with the same picks and filters;
  its label describes that navigation rather than promising a reset it
  doesn't perform — Restart and Back-to-setup differ only in *where they
  land*, not in what they clear.
  Done participates in the `QuizLiveMarker` lifecycle both ways (clears on
  reaching it, re-sets on Restart — § QuizLiveMarker) and mirrors Quiz's
  active-context stats notices (`LoadFailed` status / `WriteFailed` alert) —
  a failure on the *final* Continue lands the user here without ever seeing
  the in-quiz notice; no subscription needed, the status cannot change while
  Done is shown. Restart re-attempts the stored mix and handles refusal like
  Home's Start (§ Pages → Home): `MixRequiresStats` renders the alert with
  **"Restart without mix"**, and the marker stays cleared (nothing became
  live). Both Restart buttons disable on `Controller.IsBusy` + `app-busy`
  ("Back to setup" stays enabled — navigation only).
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

- **`Version`** — the running build's informational version (§ the version
  footer below). It lives here and not on a page class, which is the wrong
  owner of app-level metadata the moment another page reaches into it.
- **`FeedbackAddress` / `FeedbackMailto`** — the beta mailbox and the
  `mailto:` href Home's footer and Help's *Send feedback* section both render,
  with `Version` pre-filled into the subject (they sit side by side in the
  footer precisely so the version a tester reads and the version their mail
  quotes cannot be different builds). A plain mailbox is deliberate: the app
  has no server and nothing to POST to, so any other channel would contradict
  the privacy stance Help states.

The subject is **percent-encoded** (`Uri.EscapeDataString`), not interpolated
raw: a non-shipping build's version carries a `+` (`1.0.10+gabc1234`), and a
bare `+` in a URI query is decoded as a space by mail clients that treat the
query as form data — the commit the tester is reporting against would silently
arrive mangled. `PageTests` pins the escaped form; the e2e suite rebuilds the
expected href from its own literals against the version read off the rendered
footer, so app and pin stay independent.

### The version footer (`<Version>` + `StampGitShaSuffix`)

Home's `v{version}` footer renders `AppInfo.Version`, read at runtime from
the `.Client` assembly's `AssemblyInformationalVersionAttribute`.
`<Version>` in `BgQuiz_Blazor.Client.csproj` is the sole source of the
release number — no literal anywhere repeats it. The `#appVersion` span is
the e2e handle for reading the built version off a running artifact.

Build metadata is appended to that number, never substituted for it. The
`StampShortGitShaOnInformationalVersion` target (same csproj) suffixes
`+g<shortsha>` — 7 chars, the short form the umbrella's docs quote — so a
running build names its commit. **Default-on is the point**: the deploy recipe
hands the user a Release publish built at the current pointer for acceptance
*before* the `<Version>` bump, so that candidate would otherwise render the
previous release number — a build claiming to be something it isn't — and
`Configuration` cannot be the discriminator, since candidate and shipped
artifact are both Release. The shipping publish is the one caller that opts
out:

```
dotnet publish BgQuiz_Blazor/BgQuiz_Blazor.csproj -c Release -p:StampGitShaSuffix=false
```

Two mechanics worth knowing: the SDK's own
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

The host pipeline also carries `UseStatusCodePagesWithReExecute("/not-found")`,
**before** `UseAntiforgery()`: the re-execute replays the pipeline from that
point downstream, and a Razor Component endpoint throws unless the antiforgery
middleware ran on the request that reaches it. `NotFoundPipelineTests`
exercises it through the real pipeline with `WebApplicationFactory` (see
Pitfalls).

Each routable page (`Home`, `Quiz`, `Stats`, `Done`, `Settings`, `Help`)
carries its own
`@rendermode @(new InteractiveWebAssemblyRenderMode(prerender: false))`
directive — that page-level directive is how interactivity is set in this
model (there is no global `<Routes @rendermode>` setting). `prerender: false`
skips the static-prerender pass: the picked-file holder and quiz state live
in WASM-runtime memory that doesn't exist during a server prerender, so
prerender would render an empty first frame and double-run `OnInitialized`.

That choice propagates to `<head>`: since no routable page renders in the
static pass, `App.razor` carries **both** halves of the title contract, and
neither alone is sufficient (see Pitfalls).

- a static `<title>BgQuiz</title>` — the pre-boot and no-JS/crawler title; and
- `<HeadOutlet @rendermode="@(new InteractiveWebAssemblyRenderMode(prerender: false))" />`
  — the outlet the pages' `<PageTitle>` writes into once the runtime is up. No
  duplicate-`<title>` hazard: with `prerender: false` it emits nothing into
  the static pass.

### The host layout — the desktop navigation-panel collapse

`MainLayout` (host project) is `.page` → collapse checkbox → `.sidebar`
(holding `NavMenu`) → `main`. The checkbox folds the panel away on desktop
(`min-width: 641px`); mobile has its own `navbar-toggler` in
`NavMenu.razor.css` for unrelated reasons and is untouched by any of this.

**Why a checkbox and not state.** `MainLayout` **cannot be interactive**:
`RouteView` passes `@Body` in as a `RenderFragment`, and a `RenderFragment`
cannot cross a rendermode boundary — declaring `@rendermode` on the layout
throws at runtime. So there is no C# to hold the state and the collapse is a
CSS checkbox hack, mirroring the mobile toggler. Two consequences are
load-bearing:

- The checkbox **must stay a preceding sibling** of `.sidebar` — CSS's `~`
  combinator only reaches later siblings, so reordering silently kills the
  feature. `MainLayoutTests` pins the ordering (bUnit's AngleSharp has no CSS
  engine, so ordering is the most it can pin).
- `NavMenu` is a different component with its own scoped CSS, so the collapse
  reaches the nav inside it through the inherited custom property
  `--nav-scrollable-display`, which crosses the scoped-CSS boundary that a
  class selector could not.

**The rail (issue #29 — the defect was discoverability, not absence).** The
strip is a rail with a chevron chip: one SVG background (chip + chevron) that
**reverses direction on `:checked`**, so the control states both what it does
and which state it is in; plus a hover tint, a `:focus-visible` ring, and a
hairline `border-right` seam. Chip and chevron are one background image on
purpose — no element may be introduced between the checkbox and `.sidebar`
without breaking the `~` rule, so every state has to come from `:hover` /
`:focus-visible` / `:checked` alone. The accessible name
(`aria-label="Hide navigation panel"`) states what *checking* does, matching
the checkbox's own semantics; `title` is deliberately a different,
state-neutral string, because no CSS can rewrite an attribute and only the
chevron can carry the state.

**The choice is scoped to the route, not to time — measured, not assumed.**
The reset is triggered by **navigation** and nothing else: the layout renders
statically, so every in-app route change re-renders it and Blazor's
enhanced-navigation DOM synchronization resets the checkbox — on the app's own
`NavigationManager` path as much as on anchor clicks — and a full reload
resets it for the ordinary reason. `data-permanent` does **not** preserve it
(measured: the attribute governs element content, and form-control state is
synchronized regardless). Everything *inside* a route leaves it alone, which
is the half that matters to a user: Submit, Skip, Continue-within-a-run, and
Undo are in-page WASM re-renders that never re-render the layout, so **the
fold survives a whole worked run** and gives way only on the navigation that
ends it. `Help` states both halves, positive first, and `SidebarCollapseTests`
pins both alongside the fold and the chevron flip; the worked-run scenario
gates each step on the problem indicator advancing, so a click that failed to
land cannot masquerade as survival.

**Outliving navigation (issue #30 leg 1).** The rail's own click stays
route-scoped as above; what the *user* can now ask for is more. The **"Keep
the navigation panel folded" setting** (§ `QuizSettings`) persists the choice,
and `wwwroot/js/navFold.js` — a classic script `App.razor` loads right after
`blazor.web.js`, the app's second authored JS — re-applies it on initial load
and on every `Blazor.addEventListener` `enhancedload`. It lives in the **host**
project because it must run on static pages with no WASM runtime, reads the
storage entry itself in JS, and publishes `window.bgquizNavFold.apply(folded)`
as the seam `QuizSettings` invokes to move the fold without a navigation —
invoked **only to unfold** (§ `QuizSettings`). Two couplings the script holds
with no compiler behind them — the storage field name and the
`.sidebar-toggle-checkbox` selector — are in Pitfalls.

Re-applying on **every** `enhancedload`, late syncs included, is what
dissolves the live-latency artifact in umbrella issue #46 (the DOM
synchronization has been measured landing ~500ms after the navigation,
silently unfolding a rail folded on arrival). The e2e half uses that reset as
its settle signal, never a sleep or network-idle (the suite's
`WaitForTheEnhancedNavSettleAsync`).

**What collapsing buys is room, not reliably a bigger board.** `.board-page` is
height-capped, so the reclaimed 250px becomes board only while the board is
width-bound: measured at 1280×800 the diagram goes 922×519 → 977×550 and at
1280×1400 it goes 922×519 → 1172×659, but at 1600×900 and 1920×1080 it is
already height-capped and the width becomes margin. User-facing copy therefore
speaks to crowding, never to board size.

### The e2e smoke gate (`BgQuiz_Blazor.E2eTests`)

The primary-path smoke gate AGENTS.md mandates: scenarios driving the
**published artifact in a real Chromium** via Microsoft.Playwright — the
pick→done flows, the reload notice, the known-zero-pool Start gate, the
pre-Start
answer-type breakdown, the nb-NO comma-decimal guard, 404/titles, the sidebar
collapse, the settings page, the mid-quiz round trip through Home and the early
end of a run, the mix-activation gating and the pick busy affordance, and the
stats-persistence suite. It covers the one layer the other
two structurally cannot: bUnit renders components in isolation and the
`WebApplicationFactory` wire tests run the host pipeline in-process with no
browser, so only the published artifact booting a real WASM runtime in a real
browser sees this class of defect.

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
through the app's real fallback collection path, no native dialog involved.
Staged dirs are cleaned per test; those scenarios therefore run as no-stats
quizzes by construction, which is correct — they assert quiz flow, not stats.
Multi-file folders come in two shapes over one private stager:
`PickCubeProblemsAsync(n)` stages the first *n* of the committed, mutually
distinct `CubeFixtures` (every problem the same kind, so a scenario walking a
run needs no knowledge of source ordering), while `PickFixturesAsync` stages
one copy of each named fixture — the heterogeneous folder a scenario about what
a pool *contains* needs.

**A multi-problem run must be staged from distinct positions.** Staging N
*copies* of one fixture manufactures nothing — the position-dedupe layer
(§ Source construction) collapses them to a single problem (see Pitfalls).
Distinct cube fixtures buy the same ordering-independence by a route the app
agrees with. Distinctness is scarcer than it looks, because position files that
differ only in their *analysis* sections are the same position to the app: the
committed cube fixtures listed in the Directory tree are the whole supply, and
`PickCubeProblemsAsync` throws with the instruction to commit a genuinely
different position rather than silently padding. Its `CubeFixtures` remarks name
the specific look-alikes ruled out.

**The FS-Access path** lives in `FsAccessFakeTestBase`, riding the base
class's second customization seam, `ContextInitScript` (applied via
`AddInitScriptAsync` *before* the page is created): Playwright cannot drive
the native directory picker or its permission prompts, so the base injects a
fake `window.showDirectoryPicker` — a scripted directory handle over the real
fixture's bytes, `getFileHandle`, `createWritable` capturing writes, scripted
permissions. The faking stops at the browser-API boundary: the app ships **no
test seams**, and everything from `folderAccess.js` inward — BgFolderAccess_Razor's
module now, served from its `_content` path — runs for real; this suite is
that hoisted module's **only real-wire proof** (the lib's own tests script the
interop, never the browser). If
the module's use of the FS-Access surface drifts from what the fake mirrors,
the scenarios fail loudly. Per-scenario variation (corrupt stats file, denied
permission) is a page-level init script overriding the fake's config object; a
mid-test `EvaluateAsync` can mutate it between quizzes (the app re-reads the
stats file at every Start's re-bind). Three suites ride the fake.
`StatsPersistenceTests` pins: one fold ⇒ one captured write with
`schemaVersion` 2, one `problems` record whose key carries no filename, a
cube-as-two-decisions tally, indented; a **retired v1 file** ⇒ the set-aside
report (not the "couldn't be read" degrade), its bytes captured verbatim under
`bgquiz-stats.v1.json`, and two writes to the standard name (the empty seed,
then the fold); corrupt file ⇒ polite notice + **zero writes**; denied ⇒
denied notice + zero writes; and the fallback pick's "can't save stats" notice.
The stats filenames, the retired document's own bytes, and the wire property
names are deliberately hardcoded there — the consumer-side pin of those
contracts (the e2e project references no app assembly by design), and the v1
literal has no other possible source: the format has no writer left anywhere.
The fake's set-aside slot is **write-only by construction** (no `getFile`), so
an app that read it back would fail the gesture loudly. `MixWeightingTests` drives the weighted path to Done and pins #87's gating
smoke — a folder with **no stats history** offers no mix and the quiz runs
anyway, the state every first-time user of a folder is in. Every mix scenario
now needs a seeded history first, which `SeedStatsHistoryAsync` supplies the
only honest way: **run a quiz and feed the app's own captured write back** as
the folder's pre-existing file, so no scenario hand-crafts the stats wire
format. It ends by re-picking, which re-probes the seeded record *and* expires
the seeding quiz's applied filter (the generation bumps past its key) — the
state the Apply-Mix gate scenarios assume. Its wait is on the Apply-Mix gate
hint, the one thing true only after the re-pick lands (panel mounted **and**
no filter in effect);
waiting on the folder summary would race, since the outgoing pick's reads
identically. `MixRefusalTests` pins the refusal at its one remaining reachable
path: a stats file that turns unreadable **after** the mix is committed —
stage 2 → "Start without mix" → Done. Don't move it back to a corrupt-from-the-
start file: the pick-time probe would hide the panel, leaving no mix to
refuse.

**Settings.** `SettingsTests` pins the two halves neither bUnit nor the
in-process host tests can reach. The **home-board side** is asserted on the
rendered geometry — whether point 1's hit rect sits right of the **bar**, not
of the diagram's midline (the SVG carries the analysis panel down one side, so
its centre is nowhere near the board's) — with the setting changed mid-quiz
and the running quiz walked back into. The **fold setting** is followed
through an in-app navigation, the app's own `NavigationManager` path, and a
full reload, then turned back off. Every navigation there is driven from the
page body of necessity: once the setting is on the nav's links are folded away
and unclickable, which is the feature working and also why the setting needs a
page of its own to be switched off from.

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
user-visible consequence of the transition it triggered. Every committed
fixture is a single-decision `.xgp` file (the `.xgp` emission policy yields at
most one decision per file), so a one-fixture quiz is exactly one problem long
with shuffle left off, and an N-fixture folder is N problems. Their *answer
types* are a contract too: the breakdown suite stages `CheckerFixture` beside
`CubeFixture`, whose best **pair** is No Double / Take, so that folder is a pool
of exactly two answer types with three empty — which is what makes its zeros
real rather than arranged. In-app navigation is asserted with polling URL assertions
(`Expect(Page).ToHaveURLAsync`), **not** `WaitForURLAsync` — Blazor navigates by
`pushState` (same-document), and the navigation-event wait can lose the race
when the push lands between the triggering click and the wait's registration
(observed as a rare timeout with the app already on the target URL).

**Fixtures are safe to publish.** None carries a player name, verified on each
addition — anything sliced out of the corpus is exported with **anonymize ON**,
because these commit to a public repo; the copies rule is in Pitfalls.

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

**Every test failing at once with a ~5-minute wait and a 25 ms duration is a
publish failure, not a suite's worth of defects.** The fixture publishes
before any test runs, so a broken publish fails every test identically through
`PublishedAppFixture.InitializeAsync`; read the exception text, which carries
the whole `dotnet publish` log. One cause seen locally is `MSB4216` (*could
not create or connect to a task host*) — kill stale `MSBuild` / `dotnet` nodes
and re-run with `MSBUILDDISABLENODEREUSE=1`. Don't go looking for an app
regression until the publish itself succeeds.

## Public API

This is an application, not a library — no exported types or HTTP endpoints,
and the `.Client` assembly enforces that at the type level: **every plain-C#
client type is `internal`** (the controller and its outcome enum, all the
scoped holders and services, the storage adapter,
the file/wording SSOTs, the sources, `ProblemReview`, and the
`ProblemSetSourceFactory` delegate — see the Directory tree for the roster),
reachable by the test project only through the `InternalsVisibleTo` grant. The
only `public` types are the Razor components, which the framework requires
public (see Pitfalls). The externally visible surface is the route map:

- `/` → `Home` — filter selection + Start
- `/quiz` → `Quiz` — active problem (redirects to `/` if no quiz, `/done` if finished)
- `/stats` → `Stats` — read-only mid-quiz stats (redirects to `/` if no quiz, `/done` if finished)
- `/done` → `Done` — final summary (redirects to `/` if no quiz)
- `/settings` → `Settings` — user settings (never redirects; linked from the nav menu)
- `/help` → `Help` — end-user documentation (never redirects; linked from the nav menu)
- Default error page → `Error.razor`
- `/not-found` → `NotFound.razor` — the 404 page, and a **mapped route in
  its own right** (requesting it directly is a 200). Reached two ways, both
  needed — see Pitfalls (`NotFoundPage` covers client-side navigation only).

## Pitfalls

- **The e2e suite is the smoke gate AGENTS.md mandates — pointer bumps run
  it, and it must never learn to skip.** It sees what bUnit and the wire
  tests structurally cannot (see Architecture § The e2e smoke gate). Two
  standing rules: (1) never convert a broken precondition — missing
  browsers, missing fixture, failed publish — into a `Skip`; a skipped smoke
  reads as green, the defect class the gate was built to kill. (2) Its
  `Fixtures/` are committed copies; the umbrella's `TestData/FixtureFiles/`
  stays append-only and untouched.
- **Wire tests drive `FilterSurface`'s rendered DOM — no
  `FindComponent<FilterPanel>()`, ever, host tests included** (the producer's
  no-carve-out ruling; the panels live in `XgFilter_Razor.Components.Internal`).
  `PageTests`' helpers encode the sanctioned gestures: `ApplyFiltersAsync`
  clicks the panel's real *Apply Filter* button (committing whatever the
  buffers hold — defaults on a fresh mount under loose JS interop),
  `EditFilterControlAsync` / `UndoFilterEditAsync` drive the always-visible
  error-range Min input for dirty/clean reports. Two consequences the old
  synthetic helpers hid: Apply's own gate refuses an unchanged selection, so
  a second apply within one panel mount needs a real edit first; and a commit
  raises *both* events, exactly as production does. Locating the
  `FilterSurface` component itself is fine — it is consumer surface.
- **Never inventory the navigation panel in prose.** `Help`'s collapse note
  describes the *control* and its behaviour — where the rail is, which way the
  chevron points, how long the choice lasts — and names nothing the panel
  contains: its entries change, so "the panel holds Home and Help" rots
  silently the day the next entry lands. `HelpAndTitlesTests` scopes a guard
  to that bullet's own `<li>`, so the rule survives a rewrite of the wording.
  The same note must not promise a **bigger board** either (§ The host layout
  has the measurements).
- **Never describe a filter facet in BgQuiz's own prose.** What a facet
  admits is the lib's behavior, so its documentation lives with the lib:
  `/help` embeds `XgFilter_Razor`'s `FilterHelp` and adds app-level framing
  only. A description written here is a second encoding that passes every
  test on the day it ships and silently goes wrong the next time the lib
  changes. If `FilterHelp` lacks prose the app needs, extend it in
  `XgFilter_Razor`; don't restore it here.
  Corollary for the sweep after a producer facet change: grep BgQuiz for the
  retired *field* names **and** read the user-facing copy — the compiler
  catches the first class and nothing catches the second.
- **Same rule for what the panel *stores*: point, never restate.** `Help`'s
  data section says in plain user terms that the filter panel also remembers
  its settings in the reader's browser, and links into `FilterHelp`'s
  `#fh-what-is-remembered` for the detail. It **must not** name or describe
  `FilterPanel`'s localStorage keys — those are `internal` to
  `XgFilter_Razor` and rendered there from the panel's own constants, so a
  copy here could only be a prose literal nothing in this repo can catch
  drifting. Inlining that list "so the reader doesn't have to follow a link"
  is the tempting edit and is exactly the defect. The pin
  (`Help_DataSection_PointsAtTheFilterPanelsStorageInsteadOfDescribingIt`)
  asserts the section's `<code>` elements are *exactly* BgQuiz's own two keys
  — a form that survives the panel renaming its keys, which a
  `DoesNotContain("xg_filter_config")` would not.
- **A bare `#fragment` href navigates to Home, not down the page.** `App.razor`
  sets `<base href="/">`, and a fragment-only href resolves against the **base
  URI**, not the current address — so on `/help`, `href="#fh-what-is-remembered"`
  resolves to `/#fh-what-is-remembered`, the router matches `/`, and the reader
  lands on `Home` (observed, with markup that looked right).
  `Help.PanelStorageHref` composes the href from `NavigationManager.Uri`
  (fragment stripped) instead — correct under a sub-path deployment too, where
  `/help#…` would not be. Blazor then handles the same-document navigation
  itself, which is *also* not assumable — verify any in-page anchor in a
  running browser. A bUnit href assertion cannot see any of this; the e2e test
  clicks the link and asserts the target moves into the viewport, having first
  asserted it was outside it.
- **Most `FilterPanel` controls are behind its disclosure — a test that
  drives one must expand first.** The panel keeps the error-range section
  always visible and renders its other eight sections *only while expanded* —
  absent from the DOM when collapsed, not styled away — so a selector for any
  of them silently finds nothing. Both suites go through their own one-line
  helper (`ExpandMoreFiltersAsync`) that clicks the panel's real
  `#moreFiltersToggle` button, never a JS or field poke; toggling raises no
  applied-state report, so it never disturbs an applied/dirty expectation.
  Error-range edits, Apply, and Clear filters need no expansion. Two related
  traps: address the panel in an ordering assertion by an *always-rendered*
  element (`#moreFiltersToggle`), not `#positionPattern`; and Playwright's
  accessible-name match is a substring, so the panel's `Clear filters` button
  collides with Home's `Clear` — that locator needs `Exact = true`.
- **Never gate a control's `disabled` on an `@ref` field.** Blazor assigns a
  component `@ref` *after* the render that creates it, so any markup reading
  it renders one pass stale — the first render of a branch always sees
  `null`. Both quiz Undo buttons carried `disabled="@(_playEntry is null ||
  …)"` and were dead for exactly the window they exist to serve: nothing
  re-renders `Quiz` during click-by-click play assembly, so they enabled only
  once Undo was pointless. It reads as *intermittent* because Blazor never
  nulls a component ref on unmount, so the stale-but-non-null ref renders them
  enabled from the second problem on — **check `@ref` timing before believing
  a capability correlation.** The fix is to drop the term: the enclosing
  branch already guarantees the component is rendered, and a click can only
  land after the ref is assigned (pinned). Enabled-*iff*-undoable would be
  more honest but needs two producer surfaces `BackgammonPlayEntry` does not
  expose; booked umbrella-side against BgDiag_Razor, not worked around here.
- **State resets on full reload, not on in-app navigation.** "Scoped" in WASM
  is one instance per loaded app (one tab), so the controller and every holder
  survive `/` ↔ `/quiz` ↔ `/done` navigation, but a full browser reload
  re-boots the runtime and loses all of it (not the stats *file*, which lives
  on disk and resumes on re-pick). Reload-survival is a deferred arc — don't
  assume reload resumes. Anything that *should* survive navigation belongs in
  a scoped holder, not a component field; genuinely per-visit page state (e.g.
  Home's `_startError` banner) correctly stays a component field and resets on
  navigate-back. The one thing that *does* survive a reload is the
  `QuizLiveMarker` (`sessionStorage`), deliberately — see below.
- **`navFold.js` holds two contracts no compiler checks — and the layout it
  fixes can't be made interactive.** The applier is JS by *necessity*:
  `MainLayout` renders statically and cannot be made interactive (a
  `RenderFragment` `@Body` can't cross a rendermode boundary), its collapse
  control is therefore an uncontrolled checkbox, and enhanced navigation's DOM
  synchronization clears it — so no C# in the WASM assembly can restore the
  user's choice, on any page, ever. What the script is coupled to:
  1. **The storage payload.** It reads `keepNavigationPanelFolded` out of
     `xg_quizSettings` as a JS literal. `QuizSettingsTests`'s pinned bytes —
     not the C# constant — are the single source of truth for the JS side:
     rename a field without editing the script and the setting silently stops
     working, and the pinned-bytes test is what makes it fail in CI instead.
  2. **The DOM selector.** `.sidebar-toggle-checkbox` is duplicated from
     `MainLayout`, whose ordering contract `MainLayoutTests` pins. C# never
     restates the selector — it goes through the `window.bgquizNavFold.apply`
     seam — so the JS module is the one place it appears outside the markup.
  Two smaller rules ride along: the script tag must stay **after**
  `blazor.web.js` (that is where `Blazor.addEventListener` exists) and must
  keep going through `@Assets[...]` like its sibling, or a deploy leaves
  browsers running a cached applier against a changed payload. The script also
  never throws on the navigation path — every unreadable storage state means
  "not folded".
- **The `QuizLiveMarker` is `sessionStorage`, not `localStorage` — don't
  "upgrade" it.** `sessionStorage` is per-tab: it survives a reload but is
  invisible to other tabs and dies with the tab — exactly the semantics "a
  quiz is live in *this tab*" needs. `localStorage` is shared across every tab
  of the origin, so a quiz live in tab A would make a freshly-opened tab B
  falsely announce "your quiz was reset" on *its* first boot. It looks like
  the "bigger, more durable" store; it is the wrong one here. (The real
  reload-*resume* arc will need IndexedDB — a different concern from this
  per-tab liveness flag.) The controller-side `HasStarted` guard in
  `Home.OnInitializedAsync` is the complementary defence, suppressing the
  notice on in-app navigation back mid-quiz.
- **Cube decisions carry `Dice == [0, 0]` — never auto-skip them.**
  `IsPassPosition` runs `MoveGenerator.GeneratePlays` on the dice, and a cube
  decision's `[0, 0]` produces the no-legal-play sentinel — so without the
  `if (data.Decision.IsCube) return false;` guard at the top, every cube
  decision is silently auto-skipped and the whole cube feature is invisible.
  The guard is the first line; don't remove it.
- **`BackgammonPlayEntry` is strict on decision type.** It throws
  `NotImplementedException` on a cube decision, so `Quiz.razor`'s checker
  route must be exact — a cube decision reaching it fails loudly at render.
  The cube route renders a plain read-only `BackgammonDiagram` (no such
  guard); routing by `IsCube` stays page-side.
- **`BackgammonCubeActions.ValueChanged` is `[EditorRequired]`.** Omitting the
  `@bind-Value="_completedCube"` binding surfaces as `RZ2012` (→ error under
  `-warnaserror`), not a silent splat — unlike the play side's
  `OnPlayCompleted`. Keep it present: the radios are strictly controlled, so
  without the binding they are inert.
- **A binding to a parameter the component doesn't have is a *render*-time
  failure, not a build one.** `<FilterSurface OnFilterDirty="..."/>` against a
  composite that has since renamed it compiles clean and throws
  `InvalidOperationException` the first time it renders, so when
  adapting to a renamed producer callback `dotnet build` green proves nothing
  — the proof is a render-level test. (The reverse case, a binding *omitted*,
  is caught at build: the composite's holder and both callbacks are
  `[EditorRequired]`, so
  a missing one surfaces `RZ2012` → error under `-warnaserror`.) `PageTests`
  renders Home and asserts both callbacks' `HasDelegate` on the located
  `FilterSurface` — the *composite* is consumer surface, unlike the
  `.Internal` panels — which also rules out
  the attribute being silently splatted.
- **Client plain-C# types are `internal`; only Razor components are `public`**
  (the list is in Public API). Don't widen one: the tests already see it
  through the `InternalsVisibleTo` grant, and a page reaches it through
  `@inject`, which binds by type from DI and generates a **private** property,
  so a DI-injected type never lands in a public signature. The one move that
  *forces* a client type back to `public` is putting it in a public
  component's `[Parameter]` (or any other public member signature) — that
  trips **CS0053**; the fix is to keep the crossing type a library/wire type,
  not to re-widen the app type. The pages, in turn, **cannot** go internal:
  the router discovers routable components by scanning the assembly's *public*
  (`ExportedTypes`) surface — framework-required, not a missed narrowing.
- **Off-list submission semantics.** A structurally-legal play absent from the
  analyzer's candidate list counts as a skip, not a scoring miss — rare on
  well-analyzed positions, and a signal of an analysis omission rather than
  user error. Don't expect every user-submitted play to land in `History`.
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
  `DecisionId`, so an extensionless `PickedFile.FileName` is a usage error the
  iterator throws `ArgumentException` on — lazily, mid-enumeration, not at
  construction. Both of the lib module's pick paths preserve the browser's
  extension-bearing entry names precisely for this (a stated
  BgFolderAccess_Razor contract, pinned producer-side).
  Start-time exceptions (this, plus `FilterConfig.Build()` validation) surface
  on `Controller.StartAsync` and `Home.razor` banners them.
- **Lifetime stats fold on the forward exits from review, never at Submit.**
  `RedoAsync` pops the last submission *while `Review` is set*, and
  `ProblemStatsDocument` has no `Minus` — folding at Submit would let a redone
  answer fold twice with no way back. An answer is final only when the user moves
  forward past it, and the deliberate flip side is that an answer abandoned in
  review (tab close, Start/Restart without Continue) never folds — don't "fix"
  that into a double-fold hazard. There are **two** forward exits, not one:
  `ContinueAsync` and `EndQuizAsync` (issue #57), sharing one
  `RecordReviewedSubmissionAsync`. Ending the run folds for a reason worth
  keeping — **every answer visible on Done has reached the lifetime record**, an
  invariant that held for free while Continue was the only route to Done, and
  which Done's own "nothing here needs saving" line states to the user. A third
  fold site needs that same argument; a *silent* one would break the line. Skips, off-list plays, and auto-skipped pass positions
  never reach the sink at all (producer contract).
- **Never clear or rewrite the stored `QuizMix` outside the write-through.**
  The persisted mix (`xg_quizMix`) outlives any session that can't honor it: a
  refused weighted start, the per-run "Start/Restart without mix" override, a
  corrupt restore, and the pick/Clear ending of the setup (`MixConsent.Revoke`
  + `MixDraft.Discard`) all leave it untouched — corrupt just yields a blank
  *builder*, and the setup-end resets touch only the in-memory services
  (**`Discard` must never persist the blank it leaves**, or every pick would
  delete the user's mix — the Clear/Discard asymmetry is §4's
  choice-vs-consent line). The one sanctioned writer is the draft's own
  last-valid write-through (§ MixPanel / MixDraft / MixConsent owns its rules).
- **The mix hydration fills the draft; it must never activate or write.** The
  stored mix loads into `MixDraft` (once per setup) and stops there: the
  consent bit stays wherever the user left it (unchecked on any fresh setup),
  so a restored mix is visible but inert until checked in *this* setup (§5
  rule 3). Make hydration check the box — or write storage back — and a
  persisted mix silently acquires effect (the adopt bug finding W removed) or
  the blob churns with no gesture behind it. Only a *successful* parse
  **projects** — `TryFromJson`'s `Empty` fallback is a usable mix, but
  projecting it would overwrite the blank draft's defaults.
- **Don't reintroduce a committed copy of the mix — or any stored judgment
  about it.** What runs is `EffectiveMix`, derived per render from the consent
  bit and the draft's build; there is deliberately no second copy for the
  screen to diverge from, which is what makes the display-honesty wedge family
  (remove-last-row, finding AK's navigate-away, navigate-back-over-committed,
  and every draft-vs-committed reconcile arm) unrepresentable rather than
  carefully handled. The same goes for the consent bit itself: **the app never
  flips it while a setup stands** (no auto-uncheck on invalid or on Clear —
  rejected alternatives the spec's §5 records; a control the app flips stops
  being consent). Its one app-initiated move is `Revoke()` at setup end. If a
  new "is a mix in effect?" consumer appears, derive from `EffectiveMix` —
  never snapshot it.
- **`CanWeightMix` is the mix's gate and nothing else's — never widen a sweep
  onto `CanPersist`.** `FilterSurface`'s persist gate stays
  `Capability == Enabled`: saved filters have nothing to do with a stats
  record, and routing them through the mix predicate would silently break
  saving on every folder without quiz history. The two look alike at the call
  site and answer different questions.
- **The mix probe has *two* reading points, and the second one is the
  feature.** `RefreshPickedStatsAsync` runs on each pick's landing *and* in
  `Home.OnInitializedAsync`. Drop the second and the ruling's own sentence
  stops being true: a brand-new folder's first quiz would create the stats
  record but the mix would stay hidden until the user re-picked the folder,
  which nothing on screen would tell them to do. The probe is also stamped
  with `PickGeneration` — don't "simplify" that away, it is what makes a
  verdict about the previous folder expire instead of answering for this one.
- **`MixPanel`'s `@key` on `PickGeneration` is load-bearing — don't drop it.**
  A mix-capable → mix-capable re-pick leaves both the mix predicate and
  `HasFiles`
  true, so without the key the panel never re-mounts and nothing triggers the
  discarded draft's re-hydration — it would sit blank with the persisted mix
  never re-offered. The key forces the re-mount, whose init re-hydrates and
  re-offers the stored rows — inert, the consent having died with the setup.
- **Don't collapse the FS-Access pick to a single prompt.**
  `showDirectoryPicker({ mode: 'readwrite' })` looks like a free UX win and
  reads as an equivalent contract. It is not: **tried and reverted
  2026-07-24** — in real Chrome, declining that single readwrite prompt aborts
  the *whole* pick (`AbortError` ⇒ `cancelled`, no folder and no read handle),
  destroying the `PermissionDenied` rung (decline write, file list still
  loads, quiz runs without stats) — a deliberate degrade, not an accident. The
  full rationale lives in the comment above `beginPick`. The underlying
  concern — the prompt being missed in a busy UI — is already met by
  progressive disclosure plus the two-step guidance, so a collapse buys
  nothing.
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
  spuriously (the `PermissionDenied` e2e pin is `"which problems give you
  difficulty"`, not the whole sentence). Related: once a phrase is
  single-sourced into `FolderPickDisplay`, **two** surfaces render it
  verbatim, so a whole-markup `Contains` no longer proves *which* one — scope
  such assertions to the element (bUnit `Find(...).TextContent`) or pair them
  with a surface-specific lead-in.
- **A refused weighted start touches no quiz state — check the outcome before
  `IsFinished`.** `StartAsync`/`RestartAsync` returning `MixRequiresStats`
  leaves the prior quiz (enumerator, scores, `Current`, `IsFinished`) and the
  stored config exactly as they were; the only `StateChanged` firings are the
  gate's two busy flips. Callers must branch on the outcome *first*: Home's
  empty-result check reads `IsFinished`, which after a refusal is stale state
  from the previous quiz, so the other order shows a bogus no-match banner (or
  worse, navigates) off a quiz that never started. `Busy` sits before both
  checks and means do-nothing-at-all — no banner, no navigation.
- **Overlap safety lives in the controller's transition gate — don't re-guard
  it page-side, and don't "fix" the dice-click + Continue double-binding.**
  The gate (see Architecture § `QuizController`) is what makes a second
  mid-transition gesture safe; page-level debouncing would duplicate the
  rule and rot. Two load-bearing details: the gate's post-set yield (the
  busy-paint pitfall below owns why it must survive), and
  `AdvanceAsync` deliberately fires no `StateChanged` — the gate's busy-off
  fire is the completion signal, so re-adding a fire there double-renders
  every transition and breaks the pinned fire counts.
- **A busy state raised immediately before the work it describes never
  reaches the screen.** WASM runs Blazor on one thread and `StateHasChanged`
  only *queues* a render — the queue drains when the thread is handed back. So
  `_busy = true; StateHasChanged(); await DoTheWorkAsync();` paints nothing if
  `DoTheWorkAsync` holds the thread, and the affordance silently does nothing
  while looking correct in code review *and* in a component test (bUnit
  renders eagerly enough that the assertion passes either way). Always go
  through `Home.EnterBusyAsync` / `RunBusyAsync`, whose `await Task.Yield()`
  is the load-bearing line — and make sure the work that follows is genuinely
  async or yields. Corollaries: (1) the raise point must be *inside* whatever
  call actually waits — why the FS-Access pick is split at the prompt/scan
  seam and hands the raise back as `onPickAccepted` (§ Folder picking); (2)
  **only the e2e layer can prove a paint** — pin it there against the real DOM
  with the fake's enumeration held open, and treat a bUnit busy assertion as
  pinning the wiring, not the pixel; (3) the same yield discipline shows up in
  the controller's transition gate — don't "simplify" either of them.
- **Raising the busy state also renders Home at its reset, no-folder state
  mid-pick, which unmounts and re-mounts the whole `FilterSurface`.** The
  pick's reset
  runs at the click, so the paint that follows finds `HasFiles` false and the
  progressive-disclosure gate closed. That re-mount is production behavior, and
  load-bearing — § `AppliedFilter` owns what it buys and why the composite's
  source-change rule never runs here.
  So a page test that expands the panel's "more filters" disclosure before a
  pick must expand it *again* afterwards, and one that pre-arms
  `WithAppliedFilter` then picks through the UI must re-apply, exactly as a
  user would.
- **The stage-2 refusal's re-bind is a real side effect — including the
  WriteFailed sub-case.** Stage 1 (capability peek) refuses with zero side
  effects, but a stage-2 refusal has already run `BeginQuizAsync`, which
  unconditionally resets the in-memory document and reloads from disk. If the
  *prior* quiz sat in `WriteFailed` with folds living only in memory, those
  folds are dropped even though no new quiz begins — the same in-memory loss
  any Start/Restart always caused; the file itself is never overwritten on the
  LoadFailed path. Rare×rare and accepted: a skip-the-reload guard would need
  JS handle-identity interop. Don't move the bind back after the source build
  to "fix" it — the wrap decision needs the bound context.
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
- **Never write over a file that failed to read or parse — stats or saved
  filters.** A load `JsonException` (corrupt, foreign, or newer-schema file)
  flips `QuizStatsStore` to `LoadFailed`; the saved-filters equivalent is the
  producer store's `LoadFailed` (a rejected parse, or a read wrapped into
  `FilterStorageException` by the adapter — an FS error, or read genuinely
  withheld under `PermissionDenied`). Terminal for
  that quiz / that pick: no records, and — the actual guarantee — **zero
  writes**, so the user's existing data survives whatever went wrong (stats
  resets at the next Start's re-bind; the filters store additionally never
  falls back to the legacy file when the *canonical* one is present but
  corrupt — falling back would resurrect stale data over newer-but-broken
  data). The filters half is also what keeps
  load-only under `PermissionDenied` from being load-bearing: if the
  read-grant assumption is ever false in some browser, the composite degrades
  to its notice instead of the panel, never worse. `QuizStatsStoreTests`, the
  producer's `SavedFiltersStoreTests`, and the e2e corrupt-file scenario pin
  the zero-writes half; keep them.
- **The stats context binds at Start/Restart (two-slot promote) — mid-quiz
  Clear/re-pick must never affect the running quiz's recording** (the model is
  in Architecture § Folder picking). Wiring Clear or a new pick to touch the
  active slot, or moving the bind to pick time, re-opens the bug the shape
  exists to prevent: a user tidying up Home mid-quiz silently killing (or
  retargeting!) the quiz's recording.
- **Saved filters read/write the *picked* slot, not the active one.** The
  same isolation invariant as stats, from the other side:
  `PickedFolderFilterStorage` adapts the composite's storage seam onto
  `ReadPickedFileAsync`/`WritePickedFileAsync`, never the active-slot pair,
  so a mid-quiz re-pick reloads the
  saved-filters context off the *new* picked folder while the running quiz
  keeps recording through its *active* handle. Don't "unify" the two file ops
  onto one slot — the filters ops must not require a promote (they
  run before any quiz binds), and the two slots can legitimately be different
  folders when a quiz is live over an earlier pick.
- **The saved-filters visibility and degrade rules are producer-owned now —
  don't re-encode them host-side.** The panel-offering rule (a read-only
  *empty* section is clutter and hides; `LoadFailed` replaces the panel with
  the data-protection notice, never suppressed by emptiness; `WriteFailed`
  keeps the truthful panel beside its notice) and all the degrade copy live in
  `FilterSurface`. What Home rules is only its capability half: `Storage`
  null-vs-adapter, `CanPersist`, and the FS-Access `PersistDisabledReason`
  wording. Re-adding a host predicate over the composite's output (the old
  `SavedFiltersApplicable` split) would be a second encoding of a producer
  rule — the facet-prose drift hazard in gate form.
- **The parse cache must stay unfiltered, holder-homed, and
  generation-guarded.** `PickedProblemFolder.ParsedDecisions` is the parse of
  the *whole* pick with no filters — caching a filtered parse would silently
  serve one filter config's subset to every later Start. Its invalidation is
  `Set`/`Clear` nulling it (cache lifecycle = pick lifecycle); don't move the
  slot off the holder and re-create the forgotten-invalidation-wiring hazard,
  and don't drop `StoreParsed`'s generation check — the pick gesture is async,
  so a re-pick can complete inside a Start's await points and an unguarded
  store would install the *old* pick's parse as the *new* pick's cache.
  Post-hoc `Matches` over the cache is equivalent to filter-during-parse only
  because the iterator's skip/advance votes are contractually pure early-exit
  hints (the contract lives on `IDecisionFilter`/`IMatchFilter` in
  XgFilter_Lib); a filter whose votes cut rows its `Matches` would admit
  breaks that contract and this cache.
- **Position dedupe must stay *above* the filter and *below* shuffle and
  mix.** Both halves of that sandwich are load-bearing. Moving it below the
  filter — the tempting edit, since folding it into the cached parse would
  dedupe once instead of per enumeration — lets it elect a survivor the filter
  then rejects while dropping the content-equal copy that would have passed:
  filters are not purely positional (players, dates, error bands), so
  content-equal copies are *not* interchangeable to them, and a matching
  position vanishes with nothing reporting it. Moving it above shuffle or mix
  gives each quiz mode its own dedupe story, which is how #84 arose in the
  first place (id-level dedupe existed inside the mix; the bug was
  mix-independent). Also: don't reintroduce a survivor *preference*. The old
  one existed solely to keep id-keyed lifetime stats reachable across
  content-identical copies; content-keyed stats make every copy read and write
  the same record, so #95 deleted the fragmentation and the seam together —
  first occurrence survives, for display and provenance only.
  `PositionDedupeTests` pins all of it against a committed fixture streamed
  twice under two names, and **fails loudly if that fixture is missing** —
  never convert it to a skip (§ the e2e rule, same reasoning).
- **Never manufacture a multi-problem test run by duplicating a fixture.** It
  used to work and no longer can: the app collapses content-equal positions, so
  N copies of one file are one problem. A scenario that needs N problems needs N
  distinct positions (§ The e2e smoke gate). The failure mode is quiet in one
  direction — a scenario whose pool silently shrank to one problem can keep
  *passing* while no longer exercising what it names (the end-early-while-reading
  scenario did exactly that), so when a pool's size changes, re-read the
  scenarios that were green as well as the ones that broke.
- **Browser directory handles live in JS module state only** (a
  BgFolderAccess_Razor contract this app must not work around).
  `FileSystemDirectoryHandle` / `File` objects cannot round-trip the interop
  boundary; the lib's module owns them and C# sees names/bytes/booleans
  through `IFolderAccess`. Don't try to hold a handle (or an
  `IJSObjectReference` to one) in a C# holder — the lib's `JsFolderAccess` is
  the one type that touches that interop, and pages depend on the interface.
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
  needs an explicit reset or a changing `@key`. It doesn't: the entry lives in
  the `else` branch of the review `@if`, and Submit already unmounted it
  entirely when the page swapped into the review branch — by the time Redo
  swaps back, the entry did not exist in the immediately prior render, so
  Blazor constructs a **fresh instance unconditionally**. Don't add a
  defensive `@key`; re-examine only if a refactor keeps the entry mounted
  across review (e.g. overlaying the solution instead of swapping branches).
  The cube answer needs none of this: `BackgammonCubeActions` is strictly
  controlled off `_completedCube`, which nulls on every transition.
- **The status strip must stay fixed-height *within a view mode*, and the
  board-sizing glue must stay retired.** The strip's purpose is mode-invariant
  chrome: equal chrome height ⇒ equal board flex remainder ⇒ no
  answering↔review board-size jump. Sizing it by content (`min-height`, auto
  height) reintroduces the per-question jitter it was built to remove — long
  content clamps instead (legend one line, verdict two). **The invariance is
  scoped, not absolute** (`SPEC-quiz-view.md` §2, issue #41): with the maximize
  setting on, the answering composition suppresses this strip outright and
  renders a board-only canvas, so the board is deliberately larger while
  answering than at review and oscillates once per problem. That reversal is
  the opted-in feature, not this contract failing; what the contract still
  forbids is size drift nobody asked for, inside Normal view and inside
  Maximized view's own review composition alike. No CSS knows about the mode —
  which composition renders is `Quiz.razor`'s `MaximizedAnswering` derivation.
  On the board side, sizing belongs to
  BgDiag_Razor's bounded-height contract: bound the `BackgammonPlayEntry`
  wrapper with a real height (the fold column hands `.board-container`'s
  definite post-flex height down) and let the producer's `bg-board-slot` and
  `.bg-diagram` contain-fit default do the rest — re-adding consumer
  `max-height` glue, `display: contents` on a wrapper, or styles inside
  `.bg-board-slot` breaks it (`AppCss_RetiredBoundedHeightGlue_StaysGone`
  pins this). **Nothing consumer-side may style or contain the board box for a
  badge's benefit any more:** `container-type: inline-size` on
  `.board-container .bg-diagram` existed solely as the overlaid XGID's cqw
  anchor, and went with the badge (issue `halheinrich/backgammon#98`) —
  containment is layout, not decoration, so it was measured (board box
  identical on and off) and then removed rather than left behind.
  `AppCss_RetiredBadgeContainerQueryAnchor_StaysGone` pins both halves.
  The cube-answering and review boards are a bare `.bg-diagram`
  directly under `.board-container` — the cube radios live in the action row —
  so all three states size identically under the fold cap *within a mode*;
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
  static-pass outlet — and the outlet is now interactive (above), so its title
  falls back to the static `<title>` (verified: `/Error` renders its heading
  with `document.title === "BgQuiz"`; `/not-found` never declared a
  `<PageTitle>`). An accepted trade — a terminal page nobody navigates to on
  purpose, for correct titles on the six pages people use. **Do not "fix" it**
  by reverting the outlet to a bare one; that silently re-breaks all six real
  pages. The title is not the whole cost: the render-moded `HeadOutlet` is a
  WASM **root component on every page**, so both server-rendered terminal pages
  boot the ~19.5 MB payload to accomplish nothing (they render and read
  correctly before the boot completes). Accepted for the same reason; if a
  terminal page ever becomes heavily linked, the fix is a narrower home for the
  outlet, **not** un-render-moding it.
- **`NotFoundPage` covers client-side navigation only; server-side unmatched
  paths need `UseStatusCodePagesWithReExecute`.** `Routes.razor`'s
  `NotFoundPage="typeof(Pages.NotFound)"` is the Router's answer for a route
  the *booted WASM runtime* can't match — in-app navigation. It does nothing
  for a cold request: `MapRazorComponents` registers endpoints only for known
  routes, so an unmatched URL never reaches Blazor and falls through to a bare
  ASP.NET 404 with a **zero-byte body**. The symptom is a completely blank
  page — no HTML, no title — which reads as "the site is down" rather than
  "that page doesn't exist", while `/not-found` requested directly renders
  fine at 200 (it's a mapped route). The host pipeline's
  `UseStatusCodePagesWithReExecute("/not-found")` closes it; keep it **before**
  `UseAntiforgery()` (see Render mode). `NotFoundPipelineTests` pins the status
  contract; a bUnit render cannot.
- **The re-execute also catches missing *assets*, and that is accepted — on
  purpose.** `UseStatusCodePagesWithReExecute` intercepts every bodyless
  4xx/5xx, so `/_framework/no-such-asset.js` comes back 404 with the NotFound
  page's `text/html` body rather than an empty one. Not a misrepresentation:
  on a 4xx the body is an *error document* (RFC 9110), the 404 status every
  consumer keys on (Blazor's boot loader included) is preserved, and the body
  is inert. Assets that *exist* are untouched — the middleware only engages on
  an error response with no body. **Reordering cannot fix the asset case**: a
  missing static file is not answered by `UseStaticFiles`/`MapStaticAssets` —
  those call `next()` and the 404 is produced downstream by routing, which
  status-code-pages wraps wherever it sits. **The trigger for revisiting**:
  when server-side JSON API endpoints arrive, a typed client's
  `ReadFromJsonAsync` against a 404 throws a confusing `JsonException` instead
  of surfacing the status — at that point the only defensible discriminator is
  content negotiation on the `Accept` header; a path-prefix or extension sniff
  duplicates routing knowledge inside middleware and still misses cases like
  `/no-such.json`.
- **A served static file belongs to the host's `wwwroot` — the only one there
  is.** `BgQuiz_Blazor/wwwroot` is what the host serves (`app.css`,
  `favicon.png`, `lib/`, `robots.txt`, `js/navFold.js`); the `.Client` project
  has no `wwwroot` at all since the BgFolderAccess_Razor adoption took the
  folder module (it ships as that library's `_content` asset, and `navFold.js`
  — the app's one remaining authored script — is a classic script the host
  shell tags because it must run on static pages before any runtime boots).
  A file that must
  answer at a fixed URL (`/robots.txt`, and anything else a crawler, browser,
  or platform probe asks for by name) goes in the host's, and the mistake is
  silent in every layer but one: it still builds, still publishes, and 404s at
  runtime — where the re-execute above dresses the 404 in the styled NotFound
  page, so it doesn't even look bare. `BetaOnboardingTests` is the only thing
  that catches it.

## Subproject-internal next steps

- **Phase 2+ design.** Still open from the phase-2 sketch: an in-session
  history model / re-queue-on-wrong (distinct from lifetime weighting), and
  the three two-agent modes (user-vs-user, user-vs-bot, bot-vs-bot
  tournament).
- **Reload-resume (persistence).** Surviving a reload needs the picked bytes
  + progress persisted client-side (IndexedDB — `localStorage` is too small
  for buffered `.xg` bytes); a deferred arc of its own, distinct from the
  stats file (which survives via re-pick). The rule itself is in Pitfalls.
- **Device assessment — what tablet emulation settled, and what it can't.**
  Tablet *layout* has now been driven at 768x1024, 1024x768, 820x1180 and
  1180x820 (halheinrich/backgammon#67). Settled: no page overflows
  horizontally at any of them, and no chrome falls below the fold; the
  desktop nav applies (the rail's threshold is 641px) and stays usable
  folded and unfolded. The one layout defect found and fixed was width
  accounting — the desktop panel and page padding were taking 358px of a
  768px viewport from a board that is **width**-bound there, which the
  641–1200px band in `MainLayout.razor.css` now returns (+23% board width
  at 768x1024). Two findings stay open and are **not** this repo's to
  close: the wide cube action row wraps to two or three lines across the
  whole tablet band (halheinrich/backgammon#99), and in portrait the
  vertical law leaves 287–588px of blank page between board and chrome
  because the board cannot spend height it is not width-bound to use —
  a `SPEC-quiz-view.md` §2 question, not a CSS one.
- **Device assessment — what only hardware can answer.** Emulation cannot
  reach the questions that actually gate a tablet or phone visitor: the
  browser pane is desktop Chromium, so it still exposes File System Access
  and desktop pointer semantics, and it emulates touch only below 768px.
  So the pick gesture end to end, one-click checker entry by touch, the
  real hit size of the cube radios and the collapse rail, and whether stats
  persist at all remain hardware questions. `showDirectoryPicker` is absent
  on Android Chrome and `webkitdirectory` behaves differently there, so the
  fallback path is the one a tablet exercises — and on phones it may not
  work at all, which the honest phone notice
  (halheinrich/backgammon#105) owns.
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
  to end — no committed cube fixture is one (`E2eTestBase.CubeFixtures` names
  what each is); a bUnit case
  covers the verdict + scoring path meanwhile. Close by sourcing a Too-Good
  single-decision `.xgp` (`nd ≥ 1.0 && dt ≥ 1.0`) from the corpus via
  ExtractFromXgToCsv's slice export — **anonymize ON**, the fixture commits
  to a public repo — into `E2eTests/Fixtures/`, plus a `QuizFlowTests` case
  (banner "Too Good" + `No Double: … · Pass: …` verdict → Done). Synthesis
  was rejected: the producer's clean writer surface is unanalyzed by design.
  Surfaced 2026-07-22.
