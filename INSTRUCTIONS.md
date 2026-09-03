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
  `SubmittedCubeAction` (claim-typed since `halheinrich/backgammon#86`: the
  user's and the derived-truth `CubeClaimPair`s plus the two per-half losses,
  per-half correctness **derived** claim-vs-claim / action-vs-action — built
  only through `SubmittedCubeAction.From(key, answer, decision)`, never by
  hand), `QuizScore` (segmented: `PlayDecisions` /
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
  `JsonException`, and a genuine document in **any** schema below the current
  one throws the `RetiredStatsSchemaException` subtype the store retires on,
  carrying the version it declared).
  The controller talks to the source through `IProblemSetSource` and scores
  via `QuizScore.Plus`; the stats store folds finalized submissions via the
  document's `Plus`. Producer behavior — the per-enumeration reshuffle, the
  fold contracts — lives in BgGame_Lib's own INSTRUCTIONS.md.
- **BgDataTypes_Lib** — data types. `BgDecisionData`, `Play`,
  `PlayCandidate`, `BoardState`, `CubeAction`, `CubeClaim` (the three-valued
  doubler claim — No Double / Double / Too Good — SPEC-scoring §3),
  `CubeClaimPair` (the two-part cube answer, claim × taker; a closed 3×2 of
  which the **four reachable pairs** — NoDoubleTake, DoubleTake, DoublePass,
  TooGoodPass — are the option set since SPEC-scoring §3's 2026-09-02
  amendment, `halheinrich/backgammon#187`; `TooGoodTake` is a retired
  verdict and the incoherent `NoDoublePass`, named by `IsIncoherent`, is
  never offered), `BgDecisionData.CanBeTooGood` (the producer's
  offerability fact: false only at money / Jacoby / cube-centred; the
  quiz page passes it through, never re-derives it),
  `CubeClaimExtensions.ToCubeAction` (the one claim→action collapse),
  `ProblemKey` (content identity; `TryDerive` is the one factory — the
  controller stamps every submission through it, and `false` is the no-key
  rung, never a guess). **A money record (`0`-away/`0`-away) with no
  `PositionData.IsJacoby` is on that rung** — the money key spells the rule.
  The rung itself is *silent* by design, so a fixture or corpus that stops
  being keyed just stops being recorded, and `TestFixtureContractTests` is why
  that cannot happen to a fixture here unnoticed. **This one rung case no
  longer reaches a quiz silently, though**: since
  `halheinrich/backgammon#142` a money record without the fact fails the
  folder load at pool composition, naming the file
  (`JacobyStampedProblemSetSource`, § Source construction —
  `../SPEC-stats-identity.md` §2, amended 2026-08-24). Every *other* rung case
  (unstamped dice, empty board, missing `Xgid`) is unchanged: no key,
  pass-through unmerged, not recorded, nothing said. The matcher
  compares the submitted `Play` against each `PlayCandidate.Play` by canonical
  `Play` equality; cube scoring never reads an equity here — the producer's
  `SubmittedCubeAction.From` reads `DecisionData.BestClaimPair` (the one
  derivation site of the truth claim) and `DoublerActionError` /
  `TakerActionError` for it.
- **BgMoveGen** — `MoveGenerator.GeneratePlays`, used by the controller's
  no-play-choice auto-skip detection.
- **BgDiag_Razor** — `BackgammonPlayEntry` (click-driven play assembly),
  `BackgammonCubeActions` (the board-free cube answer row: one radio group
  over the four reachable pairs, on the `@bind-Value` convention over
  `CubeClaimPair?` — null only while untouched, every pill a complete pair —
  with a required `OfferTooGood` the page feeds from
  `BgDecisionData.CanBeTooGood`) + the underlying `BackgammonDiagram`
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
  `FilterHelp.razor`, the producer's own documentation of every facet, of the
  panel chrome that governs them, **and** of what the panel persists, embedded
  by `/help` at a host-stated `HeadingLevel` — that prose has one owner, and it
  is not this app (see Pitfalls: never describe a facet or a control, never
  restate what the panel stores). Its storage section's identity is exported
  for exactly this app's deep link: `FilterHelp.StorageSectionAnchorId` and
  `FilterHelp.StorageSectionHeading`, the two values `Help`'s data-section
  pointer is built from. The other `fh-*` ids stay `internal` — nothing here
  links to them.
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
    lib/bootstrap/dist/css/         — VENDORED: bootstrap.min.css (5.3.3) is the
      bootstrap.min.css               one tracked file under lib/ (see Pitfalls)
    robots.txt                      — Disallow: / (see Pitfalls: two wwwroots)

BgQuiz_Blazor.Client/              — WASM client (the whole interactive surface)
  BgQuiz_Blazor.Client.csproj       — Sdk.BlazorWebAssembly; the bg-lib closure
  Program.cs                        — TimeProvider.System + controller, holders,
                                      stores; registers the source factory by
                                      resolving PickedFolderSourceFactory.Create
  _Imports.razor
  AppInfo.cs                        — app-level identity SSOT (§ AppInfo)
  wwwroot/js/quizKeys.js            — the quiz page's spacebar module (an ES
                                      module the page imports; served at the
                                      app root as a static web asset)
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
    FolderPickDisplay.cs            — folder-pick wording SSOT (+ the
                                      folder-load refusal copy, composed in
                                      the source stack, read on the page)
    QuizStatsFile.cs                — stats filenames (live + retired sidecar) +
                                      the document's serializer contract
                                      (DocumentTypeInfo) SSOT
    QuizStatsStore.cs               — IProblemStatsSink + document lifecycle
    MixConsent.cs                   — the "Mix applies" bit (consent, not choice)
    MixDraft.cs                     — mix edit state + write-through xg_quizMix
    MixDisplay.cs                   — mix wording SSOT
    CubeActionDisplay.cs            — cube-verdict wording SSOT (claims + actions)
    AnswerTypeDisplay.cs            — answer-type wording SSOT (always five)
    QuizNoticeDismissal.cs          — occurrence-keyed dismissal, one slot per
                                      dismissible notice, Quiz page and Home's
                                      pick band (+ the QuizNotice enum)
    ShuffleOption.cs                — "shuffle order" toggle holder
    QuizLiveMarker.cs               — sessionStorage was-a-quiz-live marker
    WasmUploadedProblemSetSource.cs — in-browser stream-backed source (parser)
    CachedProblemSetSource.cs       — parse-once layer over the holder's cache
    JacobyStampedProblemSetSource.cs — pool-composition guard: an unstamped
                                      money record fails the load, named
    PickedFolderSourceFactory.cs    — the source composition (cache → Jacoby
                                      guard → dedupe → shuffle?), the one
                                      layer-order statement
    ComposedProblemSource.cs        — the factory's product: stack + collapse
                                      magnitude reader
    MatchSummary.cs                 — pre-Start pool + what it deduped away
  Components/
    XgidLabel.razor / .razor.cs    — selectable+copyable XGID badge (in-flow;
                                      the quiz page's bottom row is its one home)
    ProblemLocator.razor / .razor.cs — the problem's locator chip: source file
                                      name (middle-truncated) + game/move, same
                                      home, same row
    Pages/
      Home.razor / .razor.cs        — landing: pick + filters + mix + Start
      MixPanel.razor / .razor.cs    — mix builder (a view over MixDraft)
      Quiz.razor / .razor.cs        — active problem (play or cube)
      Done.razor / .razor.cs        — final summary
      Stats.razor / .razor.cs       — read-only mid-quiz stats (live Controller)
      Settings.razor / .razor.cs    — user settings (a view over QuizSettings)
      Help.razor / .razor.cs        — end-user documentation (never redirects)
      HelpSections.cs               — Help's structure: the five parts, their
                                      fourteen sections, ids + headings (SSOT)
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
                                      the retired v1, v2 and v3 docs + near misses
  TestFixtureContractTests.cs       — every TestFixtures factory yields a
                                      key-derivable position (the silent rung)
  QuizControllerTests.cs
  QuizControllerOverlapTests.cs     — the transition-gate overlap suite
  CachedProblemSetSourceTests.cs    — parse-once / invalidation / equivalence
  PickedFolderSourceFactoryTests.cs — the real composition: layer wire, shuffle
                                      arbitration (corpus-level, skips if empty)
  JacobyStampedProblemSetSourceTests.cs — the pool-composition guard: the
                                      throw, the file naming, the multi-file
                                      count, pass-through, and the guard's
                                      presence in the real stack
  PositionDedupeTests.cs            — the #84 repro: one fixture under two names
                                      (fixture absent ⇒ FAIL, never skip)
  CubeActionDisplayTests.cs
  AnswerTypeDisplayTests.cs         — bucket→field mapping, order, always-five
  MixPanelTests.cs                  — builder / validation / rebalance pins
  MixDraftTests.cs                  — build/write-through matrix + hydration
  QuizSettingsTests.cs              — the settings seam + the pinned wire bytes
  QuizStatsStoreTests.cs            — bind / fold / write-back / degrade,
                                      the retirements (v1 + v2, each under its
                                      own name), the v4 fold (with and without
                                      the v3 sibling; copy-before-replace; the
                                      probe's verdict), the pre-write guard
  WasmUploadedProblemSetSourceTests.cs
  PickedProblemFolderTests.cs
  PageTests.cs
  NavMenuTests.cs                   — the sidebar Help and Settings links
  MainLayoutTests.cs
  NotFoundPipelineTests.cs          — WebApplicationFactory 404 wire tests

BgQuiz_Blazor.E2eTests/            — browser e2e smoke gate (§ Architecture)
  BgQuiz_Blazor.E2eTests.csproj     — xunit + Playwright; references no app project
  Fixtures/                         — committed single-decision .xgp files
    BothAnalysis.xgp                — cube decision; best pair No Double / Take;
                                      money, Jacoby, cube centred — the one
                                      position where Too good is withheld
    Opening 32 65 64 31 65.xgp      — 6-5 checker play; best play 24/13
    TooGoodAndTake.xgp              — cube decision, a *different* board (a
                                      match); XG's "Too good to double/Take",
                                      a No Double / Take **by ruling** since
                                      SPEC-scoring §3's 2026-09-02 amendment
                                      (the position that decided it)
    match35253054_2_37.xgp          — cube decision (a match), Double / Pass
                                      (the three cube fixtures are mutually
                                      distinct positions — the supply a
                                      multi-problem run is staged from)
    ForcedPlay.xgp                  — forced checker play (both checkers on
                                      the bar, one entry per die); the quiz
                                      must never show it
  PublishedAppFixture.cs            — publish + spawn once; BGQUIZ_E2E_BASE_URL
  PublishDirectoryResetTests.cs     — the clean-publish rule and its guard
  PublishOutputHygieneTests.cs      — one generation per asset in the publish
  PlaywrightFixture.cs              — Chromium lifecycle; fail-loud
  E2eCollection.cs                  — the single (sequential) test collection
  E2eTestBase.cs                    — per-test context + shared flow helpers
  SyntheticXgMatch.cs               — the .xg match fixture, built at run time
  FsAccessFakeTestBase.cs           — the fake showDirectoryPicker seam
  EnvironmentFidelityTests.cs       — the gate's first line: every route serves
                                      what it asks for and logs nothing; the
                                      three linked stylesheets applied
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
  DeduplicatedCountTests.cs         — the count as a deduplicated count (#104):
                                      duplicated files collapse, magnitude says
                                      how many
  ForcedPlaySkipTests.cs            — a forced play never reaches the user
                                      (halheinrich/backgammon#140): two
                                      decisions match, one shows
  SidebarCollapseTests.cs           — fold, chevron state, how long it lasts
  SettingsTests.cs                  — board side by geometry; the fold setting
  MaximizeBoardTests.cs             — chrome absent answering, back at review;
                                      maximize off via the Settings checkbox →
                                      chrome stays; the strip's bottom position
  MidQuizNavigationTests.cs         — Home's way back into a running quiz
  EndQuizEarlyTests.cs              — ending a run before the source runs out
  BetaOnboardingTests.cs            — robots.txt over HTTP; the feedback mailto
  DeadPickGestureTests.cs           — the pick-capability pair: no
                                      showDirectoryPicker ⇒ the silent-gesture
                                      account; the fake installed ⇒ absent
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
                                 (practice — the first answer stays of record)
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

- **Submit** — `SubmitPlay(Play)` / `SubmitCubeAction(CubeClaimPair)` are
  **synchronous** (the only `await` was the advance, now deferred): they score
  the answer, set `Review`, and fire `StateChanged` **without advancing** —
  `Current` still points at the answered problem. No-ops outside answering
  (guarding against double-scoring).
- **`Review`** — a closed `ProblemReview` record (`Play` / `Cube`) carrying
  exactly the marks the solution diagram needs. Non-null marks the state.
- **`RedoAsync`** — **not** the inverse of Submit: it re-opens the problem for
  *practice* and clears `Review`, back to *answering* on the same `Current`,
  changing nothing that was recorded. `History` / `CubeHistory`, `Score`,
  `SkippedCount`, the enumerator and `IsFinished` are all untouched. The
  submission that follows is practice — scored and reviewed, then discarded
  (SPEC-scoring.md §2). No-op outside review.
- **`ContinueAsync`** — the forward exit from review: folds the **answer of
  record** into the `IProblemStatsSink` (see Pitfalls: as the run advances past
  the problem, never at Submit), clears `Review`, and advances. Exhausting the
  source here flips `IsFinished` — after the fold, so the final answer records.
  No-op outside review.
- **`SkipCurrentAsync`** — bypasses review and advances immediately, but only
  from answering (no-op while a `Review` is showing). Which answering state
  matters: on an unanswered problem the skip is the answer of record
  (`SkippedCount++`, nothing folds); mid-practice-cycle the problem is already
  answered, so this is the run advancing past it — the answer of record folds
  and no skip is counted.
- **`EndQuizAsync`** — the user's own exit from the run (issue #57), and the one
  path that leaves the three-state flow rather than moving through it: it
  finishes where it stands, with problems still unread. `IsFinished` flips,
  `Current` and `Review` clear, and the live enumerator is released early (safe
  because the gate guarantees no `MoveNextAsync` is in flight). No-op before
  start and after finish. **Two settled semantics, no new scoring path,** parting
  on the *answer of record* rather than on `Review`: with **no** record the
  problem showing is **abandoned** — any in-progress input is discarded, it
  records no answer, and it takes the same non-scoring outcome an explicit Skip
  records (`SkippedCount++`), so Done's "problems shown" still counts a problem
  the user saw; **with** one the answer **stands and folds**, because it was
  submitted, scored, and read — whether the review is still showing or a redo
  re-opened the problem for practice. Folding goes through the same
  `FoldAnswerOfRecordAsync` Continue uses — which is what preserves the standing
  invariant that **every answer visible on Done has reached the lifetime record**
  (Done states it to the user; see Pitfalls). The run is a **completed quiz**,
  ruled: `/done` is unchanged, with no ended-early wording and no controller flag
  for one — the partial score is simply the score of the problems answered.

`ProblemReview` lives in `BgQuiz_Blazor.Client` (not BgGame_Lib): it is
per-app UI state, and adding it to the submodule would cross the boundary. Its
`Play` carries the matched candidate index (`-1` off-list); its `Cube` wraps
the scored `SubmittedCubeAction` whole (user pair, truth pair, both losses,
derived correctness — copying the fields out would put a second spelling of
the derived correctness beside the producer's). The Quiz page maps these onto
`UserPlayIndex` / `UserDoubleError` + `UserTakeError` so the diagram marks the
*quiz user's* answer, not the .xg-recorded player's. It is the **displayed**
review, which
after a redo is not the answer of record — `IsPractice` (init-only, defaulted
false) rides on the record type itself rather than beside it in the controller,
so a review and its practice status cannot be assigned apart and drift.

**Source construction is factory-injected.** The controller takes a
`ProblemSetSourceFactory` delegate (`(DecisionFilterSet, QuizMix) →
ComposedProblemSource` — the stack plus its collapse-magnitude reader).
`PickedFolderSourceFactory.Create` builds the production one and is the
**single statement of the layer stack**; `Program.cs` registers it scoped by
resolving the app-scoped ingredients and handing them over. The stack,
innermost first:

1. `CachedProblemSetSource` over the pick — the parse-once layer (see its
   section).
2. `JacobyStampedProblemSetSource` — the pool-composition guard (below).
3. `DistinctPositionProblemSetSource` — content-identity dedupe, always on.
4. `ShuffledProblemSetSource` — only when `mix.IsPassthrough &&
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

**The pool-composition guard: a money record must state its Jacoby rule**
(`JacobyStampedProblemSetSource`; issue `halheinrich/backgammon#142`,
ratifying `../SPEC-stats-identity.md` §2's 2026-08-24 amendment). A money
record (`0`-away/`0`-away) carrying no `PositionData.IsJacoby` has no
`ProblemKey` — the money key spells that rule — so it would quiz normally and
be recorded nowhere. The guard drains the pool once, before anything is
yielded, and throws when it finds one; Home's existing start-error banner
renders the message, which is `FolderPickDisplay.MalformedForQuizzing` over
the offending file.

- **Why this rung alone fails loud.** Its siblings (unstamped dice, empty
  board, missing `Xgid`) describe data a producer can plausibly emit, so
  silence there is robustness. The in-tree converter cannot write *this* shape
  at all, so silence tolerated exactly one thing — a converter defect — while
  the user lost lifetime stats for every money position and was never told.
- **Boundary-only.** The wire stays tolerant (`PositionData.IsJacoby` is
  still `bool?`; a data type cannot name a file), `ProblemKey.TryDerive`,
  dedupe and the stats fold keep their degrade rungs beneath this boundary,
  and report-only tools keep fail-open-and-count. A folder that loads composes
  exactly the pool it composed before.
- **Beneath the dedupe on purpose.** The layer above collapses content-equal
  copies to one survivor, which would hide the other files those copies came
  from — and naming files is the whole product here.
- **It names the first file and counts the rest** (`"…and 3 other files"`),
  not a list: reaching this state means a converting parser wrote a whole
  folder that way, and a banner-length list of names says nothing the first
  name and the count do not. The name comes from the record's `DecisionId`,
  not `Descriptive.SourceFile` — the id is `required` with a validated
  non-null `Filename`, and an error whose job is to name a file must never be
  the one with no name to give.
- **Testing it needs a synthesized record**, because no producer emits this
  shape and `TestFixtureContractTests` forbids keyless fixtures living in
  `TestFixtures` — so `JacobyStampedProblemSetSourceTests` builds its own, and
  drives the *real* composition by seeding the holder's parse cache
  (`PickedProblemFolder.StoreParsed`) so the parse-once layer adopts records
  instead of reading picked bytes. There is deliberately no e2e scenario: the
  e2e corpus is real XG bytes (committed `.xgp` files, a synthesized `.xg`
  match), and no bytes the converter reads produce this.

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

**Presentation telemetry for the Quiz page.** The mix-notice framing fact —
whether the run's *effective* mix bound its percentages to a requested
`QuizLength` — is the composition's own `MixComposition.HasRequestedLength`,
read off `LastComposition`: the producer records the capped/capless split
precisely so no consumer re-derives it (the controller carried a local
`ActiveMixHasLength` duplicate until halheinrich/backgammon#12's recording
landed). Passthrough runs (blank mix, or the ignore-mix override) wire no
composition at all, and the notice block is gated on the composition's
existence, so the no-framing case falls out structurally. A refused start
replaces no active-run state, `LastComposition` included, so a running quiz
keeps its framing behind a refusal.
`ProblemNumber` / `ProblemCount` drive the "Problem N of M" indicator:
N is the 1-based **consumed stream slot** of `Current` (auto-skipped
no-choice positions included; reset by Start/Restart, untouched by Redo) and M is the
composition's `DrawnCount` (weighted) or the source's declared `Count`
(passthrough; null when streaming — the page then shows "Problem N" alone).
Slot-counting is the settled convention: both numbers count the stream, so N
never exceeds M and lands exactly on M at exhaustion; the accepted trade-off —
an auto-skip shows as a gap — is documented on `ProblemNumber`.

**Lifetime-stats sink is ctor-injected.** The controller's second dependency
is the `IProblemStatsSink` (production: `QuizStatsStore`), driven at exactly
two points: `ResetAndAdvanceAsync` calls `BeginQuizAsync()` — the one shared
path under Start *and* Restart, so the stats context binds there and nowhere
else — and **the exits that advance the run past a problem** fold via
`RecordAsync`, through the one shared `FoldAnswerOfRecordAsync` (`ContinueAsync`,
`SkipCurrentAsync` and `EndQuizAsync`; there is one encoding of what folds, not
three). The sink never throws for stats trouble, so quiz flow is independent of
whether stats are recording.

**Filter ownership.** `StartAsync` takes a `FilterConfig` (the wire DTO
emitted through `FilterSurface.OnFilterConfigChanged`), not a runtime
`DecisionFilterSet`, and calls `FilterConfig.Build()` to produce its own
pipeline, which it owns end-to-end — no shared mutable state ever exists
between page and controller. The `ProblemSetSourceFactory` delegate still
takes the runtime `DecisionFilterSet` (the source's contract is the runtime
pipeline; the controller is the authority on assembling it), plus the run's
effective `QuizMix` for shuffle arbitration. It returns a
`ComposedProblemSource` — the stack to enumerate paired with a reader for the
dedupe layer's collapse magnitude (§ It counts deduped positions).

**Pre-Start match summary.** `SummarizeMatchesAsync(FilterConfig)` reports
what a config would admit, as a `MatchSummary`. It builds the same
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

**It counts deduped positions, says so, and cannot disagree with the quiz.**
The summary draws from the *same* factory, so the position-dedupe layer is in
the pool it counts: "N decisions match your filters" means N distinct
positions. The agreement is structural, not a convention two call sites must
honour: the layer decides *which* copy survives, never how many do, so pool
size cannot vary between a pre-Start summary and the quiz it precedes. The
collapse **magnitude** rides in the same `MatchSummary` (issue #104): the
factory folds the producer's duplicate-class telemetry to one number and the
summary reads it after the drain, since it is telemetry of that enumeration.
`Total + DuplicatesCollapsed` is the whole filtered stream — the accounting
identity `PositionDedupeTests` pins against a real parse. The factory returns
the pair rather than the decorator so the shuffle wrapper above it needs no
type-test, and so a substitute stack with no dedupe layer reports `0` honestly
instead of fabricating one.

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
doubler's three-valued *claim* (No Double / Double / Too Good) and the taker's
response if doubled (SPEC-scoring §3, `halheinrich/backgammon#86`, amended
2026-09-02 by `halheinrich/backgammon#187`: **Too Good requires the pass**,
so the reachable verdicts are exactly the four coherent pairs, and the answer
row offers exactly those — No double, Double / Take, Double / Pass, Too good
— each pill a complete pair; Too good is withheld where the producer says
the verdict cannot occur, `BgDecisionData.CanBeTooGood`, false only for
money under Jacoby with the cube centred, passed through as the row's
`OfferTooGood` and never re-derived here).
`SubmitCubeAction(CubeClaimPair)` always scores both halves (no off-list /
skip path, unlike plays; it accepts any pair — the incoherent (No Double,
Pass) cell is no longer offered by the row but still scores per half if it
arrives) through the producer's one factory,
`SubmittedCubeAction.From(key, answer, decision)`: it reads the derived truth
(`DecisionData.BestClaimPair`) and both per-half losses off the one decision,
and the record derives correctness **claim vs. claim** on the doubler half —
so No Double answered to a too-good position scores incorrect at +0.000, the
ruled "right action, wrong reason" verdict, and so does Too Good answered to
XG's "too good to double/Take" position, a No Double / Take by ruling under
the amendment. Nothing in this app reads an equity or compares an action for
scoring. Folded into the score's `DoubleDecisions` and `TakeDecisions`
segments via `QuizScore.Plus(SubmittedCubeAction)`. The review's verdict line
names the doubler half by the claim submitted and, when wrong, the truth
claim; the right-action-wrong-claim case is said in those words in both
directions (decided on the board action behind each claim via
`ToCubeAction`, not on the loss being zero); the incoherent cell gets a
trailing explanation. The solution diagram's Best banner
(BackgammonDiagram_Lib) speaks board actions, so a too-good position reads
"Best: No Double" there beside a "Too Good" verdict line — the label SSOT
arc (`halheinrich/backgammon#185`) recomposes the banner over claims and
re-sources `CubeActionDisplay` / `AnswerTypeDisplay`; neither is patched
here.

**No-play-choice auto-skip.** Each `AdvanceAsync` step pulls the next
decision and tests it with `HasNoPlayChoice`, which runs
`MoveGenerator.GeneratePlays(board, d1, d2)` over the record's own board and
dice. **One rule, both cases** (`halheinrich/backgammon#140`): the roll admits
exactly one legal play, whether that play moves nothing (a pass — the
no-legal-play sentinel, see Pitfalls) or moves something (a forced checker
play). Either way the position poses no question, so it is silently skipped —
never shown, never counted toward `SkippedCount`, nothing folded to stats.
Cube decisions are excluded by the guard on the rule's first line (Pitfalls).

Distinctness is the **producer's** contract, so list length is the test.
`GeneratePlays` emits each legal play exactly once, canonically distinct, and
never returns an empty list — so `legal.Count == 1` *is* the forced test, and
the pass case needs no branch of its own (the no-legal-play sentinel is one
entry, see Pitfalls). The rule once compared every entry against the first,
because a two-die bear-off came back twice — one play, two candidates, since a
bear-off move encodes as `(point, 0)` whichever die paid. That was a
consumer-side workaround for a producer defect
(`halheinrich/backgammon#140`'s verdict), retired once the producer was fixed
(`halheinrich/backgammon#141`). Since the count is only as honest as that
contract, `CanonicalPlayEquivalenceTests` pins it from this side — a producer
regression is caught where the miscount would silently happen, rather than
inferred from an over-quizzed run — and `TestFixtures` holds the boards.

Not a rounding error: **about one checker decision in eleven** across the
umbrella's corpus is forced or a pass, so the pre-Start match count ("decisions
that match") and the number of problems a run actually shows genuinely
diverge.

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
  it can't tell apart: the user answered no, *or* **the browser refused to
  ask** — `requestPermission` requires transient user activation and *throws*
  `SecurityError` without it (never resolves "denied"), which `beginPick`
  catches and degrades onto this rung. That is Chrome for Android on *every*
  pick: the picker leaves no live activation behind
  (halheinrich/backgammon#109). So every surface for this rung opens with the
  cause-agnostic `FolderPickDisplay.WriteAccessNotGranted` — never "you
  declined", which on the refused-to-ask path attributes a decision the user
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
(`bgquiz-stats.json`), `RetiredNameFor(schemaVersion)`
(`bgquiz-stats.v{n}.json` — the set-aside name, bare and path-free),
`MergedNameFor(schemaVersion)` (`bgquiz-stats.v{n}.merged.json` — the name
a **folded** document is copied aside under; a distinct spelling from the
retired name for the same number because it says the contents live on in
the current file), and `DocumentTypeInfo`, the one serializer contract every stats read and write
names to `JsonSerializer`'s type-info overloads (`WriteIndented = true` —
whitespace is the only options-controlled aspect; the bundled converter pins
names and ordering, and the resolver is BgGame_Lib's source-generated
`BgGameJsonContext`, which is what keeps the stats wire visible to the trim
analyzer — halheinrich/backgammon#129; byte-identity to the old reflection
path is pinned in `QuizStatsStoreTests`). Every name is passed *into* the
lib's name-parameterized
active-slot calls per call and rendered by `Help` and the notices from this
type — nothing restates one. **The set-aside name is a function of the retired
version, not a constant**: every schema below the current one retires, so a
folder can see two retirements in sequence (a tester who skipped a release),
and one fixed name would put the second copy over the first.

**`QuizStatsStore`** (scoped; aliased as `IProblemStatsSink` so the
controller's sink and the pages' status notices observe one instance; deps:
`IFolderAccess`, `TimeProvider`, `PickedProblemFolder`) owns the
`ProblemStatsDocument` lifecycle:

- `BeginQuizAsync` (every Start/Restart) re-derives the whole context and
  resets any prior failure state: capability ≠ `Enabled` or no promoted
  handle ⇒ `Disabled`; `null` read ⇒ `Ready` over `Empty` (fresh corpus);
  `JsonException` / read `JSException` ⇒ **`LoadFailed`** — records nothing,
  never writes (see Pitfalls; recovery is user-side, no overwrite offer).
- **The retirement** (SPEC-stats-identity.md §3) is the one exception to
  that never-writes rule, and it is caught *before* the general
  `JsonException`: a `RetiredStatsSchemaException` means a genuine document in
  a retired schema, so the store copies its bytes aside under
  `RetiredNameFor(ex.SchemaVersion)` **unparsed and first**, then puts a fresh
  `Empty` under `FileName`, then mints `StatsRetiredOccurrence` — a
  `StatsRetirement` carrying that set-aside name, so "a retirement happened"
  and "this is the file it wrote" cannot come apart — and lands `Ready`. Order
  is the data-safety guarantee — a file that could not be preserved is never
  replaced — so a `JSException` from either write reports `LoadFailed` with
  `FileName` still holding the retired document, which the next bind recognises
  and retries over identical bytes. Newer-than-supported is **not** retired: it
  keeps the untouched `LoadFailed` posture, as does a document claiming a
  retired version without that version's shape. No rename API was lifted into
  BgFolderAccess_Razor for this; a second consumer would be the trigger.
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
- **The fold** (SPEC-stats-identity.md §3, amended 2026-09-02;
  `halheinrich/backgammon#187`) is the other exception, caught the same way:
  a `FoldableStatsSchemaException` — the producer's sibling signal for the
  one version that folds instead of retiring, the interim **v4** that never
  shipped — means a document whose tallies carry forward. The store reads it
  through `ProblemStatsDocument.ReadFoldable`, reads the set-aside of the
  version now current (`RetiredNameFor(CurrentSchemaVersion)`, the v3 file
  the interim build wrote) as the base when that sibling exists and
  `Merge`s the folded records into it — else the folded records stand alone
  — then copies the v4 bytes aside under `MergedNameFor(4)` **first** and
  writes the merge under `FileName` second, the same data-safety order as
  the retirement, with the same retry-on-the-next-bind for a failed replace
  (the merge is recomputed from the same two inputs, so the retry is
  idempotent). Reads before any write: a v4 body the fold reader rejects,
  or a sibling that will not parse, is `LoadFailed` with nothing written.
  **No `StatsRetiredOccurrence`**: nothing restarts, so the Quiz and Done
  restart note must not fire (`PageTests` pins both pages silent). One pass
  — the folder then holds a current v3 file, and the next bind is the
  ordinary read. The file dance is this consumer's; the read and combine
  halves are the producer's (`ReadFoldable`, `Merge`), restated nowhere.
- **`RefreshPickedStatsAsync()`** — the probe: a **picked**-slot read
  (`ReadPickedFileAsync(QuizStatsFile.FileName)`), deserialize, `Count > 0`.
  Degrade-tolerant *because that is the ruling*, not as a defensive extra:
  missing, empty, corrupt, foreign-schema, and browser-read-failure all leave
  it false, with no status, no notice, and nothing thrown. **A retired file
  reads as "no stats to weight by" too**, and stays that way until the first
  quiz performs the set-aside: the probe never binds, so it never retires. That
  is the ruling working, not a gap to close (SPEC-stats-identity.md §3). **A
  foldable (v4) file reads as stats** — its records carry forward, so it is
  read through `ReadFoldable` and counted like a current document, with no
  forecast (nothing restarts); the v3 sibling the fold would merge in is
  deliberately not consulted by the probe, which reads one file. What it
  *does* now do with that file is **remember the version it declared**
  (`ForecastStatsSetAsideName`, halheinrich/backgammon#146): the producer's
  recognition signal derives from `JsonException`, so it used to fall into the
  swallow with the corrupt files and the fact was lost. Caught ahead of that
  swallow, the mix answer is byte-identical (nothing sets `_pickedHasStats` on
  that path) and the read stays read-only — the one fact Home's forecast notice
  needs is simply no longer thrown away. It **promotes nothing** and never
  assigns the active document or `Status`, so a probe during a running quiz
  cannot disturb what that quiz records. Under a
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
caps cut short — § `PickedFileLimits`), the stats-retirement **forecast**
(§ Dismissible notices), and the `role="alert"` pick-failure banner.
Quiz-context (Quiz **and** Done — a failure on the final Continue lands on
Done without ever showing Quiz's notice): `LoadFailed` polite, `WriteFailed`
assertive;
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
the contract — and **`PickOccurrence`**, the opaque per-pick identity token
(replaced exactly where `PickGeneration` bumps) that keys Home's dismissible
pick-outcome notices in `QuizNoticeDismissal`; opaque rather than the boxed
generation for the holder's one-rule-one-kind-of-token discipline (issue
#107, mirroring `QuizStatsStore.StatusOccurrence`).

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
a **uniformly random** N of that kind — never a prefix, so repeated picks of
one folder reach the whole corpus rather than the same slice forever
(`BgFolderAccess_Razor`'s `PickTruncation` holds the rationale; Home's notice
says "chosen at random" because the shifting match count is otherwise
unexplained, issue #106) — and the left-behind count rides back as
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
guarantees Drawn == Target capless). The page keys the split on the
composition's own `HasRequestedLength`; a length-bound mix that filled
exactly shows no notice at all.

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

### Dismissible notices — `QuizNoticeDismissal` (issues #41 / #107, `SPEC-quiz-view.md` §4)

**Every notice on the Quiz page dismisses on a click**: the mix composition
notice (both framings), the stats-context degrade notice (`LoadFailed`'s
polite one, `WriteFailed`'s assertive one), and the stats-retirement report.
Dismissal is §4's answer to the
board space they cost, the mode being forbidden from suppressing them — the
ruling and its reasoning are the spec's.

**So does every outcome/status notice in Home's pick band** (issue #107, the
ruling "a colored info message should go away when clicked"). The
holder-backed trio — the truncation alert, the stats-capability notice
(its three branches share one slot: mutually exclusive renderings of one
per-pick verdict) and the stats-retirement forecast — key on
`PickedProblemFolder.PickOccurrence`, so a re-pick shows fresh and
navigate-back stays dismissed. The per-visit pair — cancelled pick, empty
folder — get the same click affordance but clear their own page
fields: their transience already scopes the dismissal, so a token would have
nothing to outlive. **Not dismissible, deliberately**: the red pick-error
banner (a failure report, `role="alert"`, a different claim class) and the
pre-pick advisory lines (guidance with their own retirement rules — #105's
silent-gesture account is folder-held-gated, not a pick outcome).

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
  actually set a retired file aside, so no companion boolean can disagree with
  it. A `StatsRetirement` rather than a bare `object`, because the notice needs
  the name that run wrote (which follows the retired version) and two nullable
  members could come apart; a **class, not a record**, so the holder's
  reference-identity comparison still means one occurrence.
  Its own slot rather than a share of `StatusOccurrence`, because it can be
  showing *beside* a degrade notice: a later `Ready → WriteFailed` mints a
  fresh status token, which must not resurrect a retirement report already
  read. And deliberately not a `QuizStatsStatus` value — after a retirement
  the context is `Ready` and can still fail its next write, so retired-ness is
  orthogonal to the condition `Status` reports; folding it in would make every
  `== Ready` site grow an "or retired" clause. `Done` mirrors it
  non-dismissibly, as it mirrors the degrade notices.
- **PickTruncations / PickStatsCapability / PickStatsRetirementForecast** →
  `PickedProblemFolder.PickOccurrence`, the same opaque token for all three of
  Home's pick-band slots (they render side by side and dismiss independently).

Keying the stats notice on the `Status` *value* gets two real cases wrong: a
mid-run `Ready → WriteFailed` is a new thing to say, and **a second quiz bound
against the same unreadable file is also a new thing to say** — that run
records nothing either, and `SetStatus` reports no transition for it (hence
the bind-side replacement, not just the transition-side one). A generation
`int` would work for the store but would force the holder to compare two kinds
of token by two rules, and value equality is exactly the trap `MixComposition`
documents avoiding.

**The retirement is said twice, in two tenses** (halheinrich/backgammon#146).
The *act* stays at the quiz bind — set-aside-before-replace is the data-safety
ordering and a pick must never mutate the folder, write permission not being
settled until then (**SPEC-stats-identity.md §3**, which this leg does not
move) — and `StatsRetired` on Quiz/Done stays its **report**, also the only
surface a straight-to-quiz navigation sees. What #146 adds is the
**forecast**: Home's pick band says, *before* the pick is acted on, that this
folder's stats file will be set aside, because the fact is knowable at pick
(the mix-gating probe opens the file then) and the page's own ordering
standard — "Your data stays yours" precedes the pick — puts
consequence-bearing information before the action. Both tenses render the
name through `QuizStatsFile.RetiredNameFor` over the version the *file*
declared, so they cannot name two different files; the forecast reads
`QuizStatsStore.ForecastStatsSetAsideName`, whose **nullable name is the
flag**, expiring with the pick generation like `CanWeightMix`. Only the
producer's retired-schema signal forecasts: a corrupt, foreign or
newer-schema file is the `LoadFailed` family's story, told after the bind,
and promising a set-aside for one would promise an act that never comes.

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
**Defaults state the product's answers, not the app's history** — home board
right (the producer's own `DiagramRequest.HomeBoardOnRight` default), no
randomization, panel unfolded, and **the board maximized while answering**.
Three of the four still reproduce the app that shipped before this page
existed; the fourth deliberately does not (§ The maximize-board setting).

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
applier". Extending the format needed **no migration and no version stamp**: the
tolerant restore gives a payload predating a field that field's *current*
default. That same rule is what lets a **default change** ship without a
migration — it reaches exactly the users who never chose, because an explicit
stored value is still read and still wins. The asymmetry is pinned from both
sides in `QuizSettingsTests` (absent ⇒ the new default; stored `false` ⇒
`false`, forever); it is load-bearing, not incidental, and #113 is the arc that
first leaned on it.

**The maximize-board setting** (issue #41 / `SPEC-quiz-view.md` §3) is the one
field here that reverses a documented invariant rather than choosing between
equals — and the one whose default is not the pre-settings app. **Default on**
since 2026-08-19 (§3 amended, `halheinrich/backgammon#113`): it shipped off
because off reproduced the pre-arc page exactly, which is a migration-safety
argument and pre-beta protects nobody, so the default now states the product's
own answer to *how large should the board be while you answer*. Users who had
already turned it off keep it off — see the absent-field asymmetry above.
This service records the *choice* only — the composition it produces is
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
  deliberately **cause-agnostic** (§ `IFolderAccess`) — its causes include a
  *present-but-inert* `showDirectoryPicker` that aborts without ever opening
  (halheinrich/backgammon#116, observed live), so its advice is a
  **conditional** ("if your browser asks…"), and on the FS-Access branch only
  it carries the post-gesture sibling of #105's dead-gesture conditional
  ("If no folder chooser opened, …" + `FolderPickDisplay.DeadPickVerdict`,
  the clause both accounts share) — omitted on the fallback branch, where the
  notice only fires via a `cancel` event a chooser must have opened to raise
  and the #105 grey line stands beside it. **Both mechanisms
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
  **Pre-pick advisories, none of them gated by any probe of the *pick
  outcome*.** The **supported-browser statement**
  (`FolderPickDisplay.SupportedBrowsers`, beside the pick button) is gated only
  on "no folder held": where the pick isn't supported the button is a *dead
  entry point* and no code path ever runs to say why, so the readers it exists
  for are exactly the ones a capability probe would exclude. It states a
  **capability, never a device class** — it once said "on phones, choosing a
  folder may not work at all", which hardware falsified in both directions
  (halheinrich/backgammon#108/#109); a dead pick gesture is capability-shaped,
  not screen-shaped. The two remaining advisories are the **two branches of
  one `_fsAccessAvailable` snapshot**, so exactly one of them is ever on
  screen. The **two-step permission guidance** covers
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
  and **quotes no browser's prompt text** (see Pitfalls). The
  **silent-gesture account** is the other branch —
  `!_fsAccessAvailable && !Folder.HasFiles` — and it is what
  halheinrich/backgammon#105 bought. Where `showDirectoryPicker` is absent the
  hidden `webkitdirectory` input is the only mechanism left, and **whether a
  browser honors it cannot be feature-detected**: the attribute is present on
  the input object even where the picker never opens (observed on a tablet,
  halheinrich/backgammon#108, where Choose folder raised no chooser of any
  kind). So the gate does **not** claim the pick is dead — nothing can. It
  claims the *undetectable* mechanism is the one that will run, which is the
  most the probe can honestly report, and the notice it gates is a
  **conditional** ("if choosing a folder opens nothing at all…") that asserts
  nothing about the browser rendering it — vacuously true, never false, on a
  desktop browser whose fallback works. **Honest hedge over false certainty**
  is the standing rule here: a detection that overclaimed would be worse than
  the hedge. It deliberately survives the gesture (a dead tap leaves the
  cancelled-pick notice *beside* it, not instead of it) and retires only on a
  folder held — the one event that refutes it.
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
  holds the returned `MatchSummary` in `_matchSummary`. Home owns
  only display and lifecycle: a request id stamped per Apply discards a stale
  result landing after a newer Apply, and the summary clears on any filter
  edit or new/cleared pick. One `role="status"` region carries all of it — the
  count from `AnswerTypes.Total`, the dedupe sentences, the mix caveat, and the
  breakdown — so a screen reader gets the pool and its make-up in one
  announcement. Settled rules:
  - **The count is a count of distinct positions, and the line says so**
    (umbrella #104) — "N decisions match your filters" is N *positions*, and it
    cannot disagree with what a capless quiz then serves (§ Pre-Start match
    summary for why that agreement is structural). Two sentences carry it. The
    standing one, "Repeated positions are counted once.", renders on any
    non-empty pool, so the number reads as deduplicated even where nothing
    collapsed; it is suppressed on an empty pool for the reason the breakdown
    is. The magnitude, "That left out N more matching decision(s).", renders
    only when N > 0 — and it is the half that actually works, since "distinct"
    alone still leaves the user's file-count subtraction unexplained. Both
    claim exactly what the telemetry measures: matching *decisions* dropped,
    never files (a file holds many decisions, and the magnitude is measured on
    the filtered stream). Neither inventories what makes two positions the
    same — that is the producer's identity rule, not this app's to restate.
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
  (zero filter matches; every match auto-skipped for offering no play choice), so the
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
  `BackgammonCubeActions` four-pair row in the action row, whose
  `@bind-Value` keeps `_completedCube` current — null until a pill is
  chosen, and every pill is a complete pair, so the Submit gate lights on
  the first click; re-fires on every change thereafter, so the user can
  revise before Submit. The row's `OfferTooGood` is the record's
  `CanBeTooGood`, passed through. Both fields reset on every transition,
  which clears the row outright — it holds no state the pair does not
  express, so the `@key` remount of the two-group era is gone (see
  Pitfalls). The action row varies
  by kind: cube places the radios ahead of Submit / Skip and has no Undo (no
  partial-move state); checker keeps Undo last / Undo all (clearing the
  latched play, since the component does not notify on undo). **Both Undo
  buttons are disabled only while `Controller.IsBusy`** — deliberately *not*
  on `_playEntry` being assigned (see the `@ref`-timing pitfall).

  **There is ONE action row (`.action-row`), shared by both states** — only the
  leading answer instruments branch. Its trailing cluster (`.action-row-tail`)
  holds, **in this order**, the XGID badge, the problem's locator, "Show stats",
  then **"End quiz"** (issue #57 — § `QuizController` for what that transition
  does). End quiz is one-click and immediate, ruled: the confirmation the issue
  first sketched was dropped, so its placement at the far end of the row — as far
  from Submit / Continue as the row allows — *is* the mitigation, and `PageTests`
  pins the whole sequence; the two provenance chips therefore *open* the cluster
  rather than closing it. The shared row is what keeps the two states' row
  heights equal (and so the board's flex remainder unchanged) **by
  construction** — the claim the old two-row arrangement had to make by hand. The
  semantic class names are the pins' hooks — the Bootstrap utilities beside them
  still do the layout. **Review** (`Review`
  set): a read-only `BackgammonDiagram` in `DiagramMode.Solution` plus
  Continue / Redo / Show stats, built with `DiagramRequest.Builder.From(...)`
  and then the user's marks overridden from `Review` — `UserPlayIndex` for a
  play (`-1` off-list draws no marker), or `UserDoubleError` / `UserTakeError`
  for a cube. `FromDecisionData` is **not** used here: it defaults those marks
  from the .xg-recorded player, not the quiz user. The review diagram's
  `OnDiceClicked` is bound to the same `ContinueAsync` handler as Continue
  (safe under the transition gate). Redo falls back to the answering branch on
  the same problem; no explicit reset or `@key` is needed (see Pitfalls). A
  practice review — `Review.IsPractice`, i.e. any submission after a Redo — is
  rendered identically but for one clause the verdict band leads with
  ("Practice retry — your first answer stands."): SPEC-scoring.md §2 leaves the
  treatment to this app, and an unbadged "Correct" beside a score panel that
  does not move reads as a bug. It rides in the band's text, not as a badge or a
  third strip line, because the strip is a fixed-height contract.
  **The action row leads the chrome** (halheinrich/backgammon#148): the
  chrome reads board → action row → status strip → score panel, so the
  controls reached for on every problem sit nearest the board. Its primary
  button — Submit while answering, Continue at review — is `btn-lg fw-bold`;
  nothing else in the row changes size, and the row's height is that button's
  in every state, since each state's row carries exactly one primary.
  Below the action row sits a **fixed-height status strip**
  (`.status-strip`, `app.css`): a one-line legend slot and a two-line-clamped
  verdict band — a neutral prompt while answering; the legend
  (`* played · † your answer`) and outcome-coloured verdict at review. Its
  fixed height, and the board sizing that rides on it, are in Pitfalls.
  **Below the status strip — the page's bottom chrome — sits the `ScorePanel`**
  (`SPEC-quiz-view.md` §5, issue #41): reference material read between
  problems, not while deciding one, so it sits below the controls the user is
  reaching for and leaves the chrome nearest the board to the chrome that
  speaks to the problem in hand. It stays *inside* `.board-chrome`, which is
  the move's whole no-interaction claim — reordering within the measured
  `flex: 0 0 auto` block leaves its total height, and so the board's flex
  remainder, unchanged. `Done` and `Stats` render their own `ScorePanel` with
  their own parameters and are untouched.

  **The spacebar performs the primary action** (halheinrich/backgammon#149;
  always on, no setting). Continue at review, Submit while answering once a
  complete answer has enabled it, nothing while the controller is busy —
  the rule a dice click already follows. The page owns it twice over:
  `CanSubmit` / `CanContinue` are the one expression each that both the
  buttons' `disabled` and the `[JSInvokable]` `PerformPrimaryActionAsync`
  read, so the keyboard cannot enable what the button shows disabled; and
  `wwwroot/js/quizKeys.js` — imported as an `IJSObjectReference` on the
  first render, detached and disposed with the page (`IAsyncDisposable`) —
  decides *eligibility* in the browser, synchronously, from the event alone:
  Space, unmodified, not a repeat, and focus on nothing that consumes space
  (typing surfaces, buttons and links, checkboxes; radios only when already
  checked, so a pill still selects). `preventDefault` only when it fires.
  The callback's name travels with the reference (`nameof`), so it is
  spelled once. It is the app's first `[JSInvokable]`, and the e2e suite
  against the trimmed AOT publish is what proves it survives
  (`KeyboardShortcutTests`); no trim warning arose. Help says so in one
  sentence beside each dice-click sentence, never as an inventory.

  **The XGID has one home: the bottom row** (`SPEC-quiz-view.md` §4's
  2026-08-13 amendment, issue `halheinrich/backgammon#98`). `XgidLabel` — the
  selectable-text-plus-copy badge, the DOM counterpart of the label the
  PDF/PPTX/PNG exporters bake in — renders at **one** site, opening
  `.action-row-tail`, present in both view modes and both states. It is off the
  canvas entirely (§4 for why), so the three board branches render the producer
  components bare.
  Consequences worth knowing before touching it: **one site, not three** (a
  per-branch badge is how one composition ends up rendering it differently);
  and the badge is **in-flow and positions nothing**, so the old `position:
  relative` host requirement and the `container-type: inline-size` cqw anchor
  on `.board-container .bg-diagram` are both gone (pinned retired — see
  Pitfalls). The producer's `Overlay` slot and the exporters' baked corner label
  are untouched — this is the quiz page's placement choice only. Nothing in the
  cluster may be load-bearing for its layout, because either of its two leading
  components can render nothing at all (an empty `Xgid`; a record that locates
  nothing) — which is why the cluster right-aligns itself rather than being
  pushed by an `ms-auto` on a first child that varies per problem.

  **The visible text is capped at `2.5rem`, and the cap is a board-size
  contract** (`AppCss_XgidLabelText_StaysCapped`). Uncapped, the badge wraps the
  action row wherever the board is height-bound — and because a cube row is
  wider than a checker row (four pair pills since
  `halheinrich/backgammon#187`; five pills in two groups under
  `halheinrich/backgammon#86`; four compound pills when measured), the wrap width depends on the
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
  **Not this rule's to fix:** the row's own wrapping at narrow widths predates
  the badge and is `halheinrich/backgammon#99`. Since the cluster stopped taking
  part in the row's line-breaking (below), the *leading* segment is the only
  thing that decides it: measured 2026-08-21 with the cluster at full width, the
  row is one line down to ~900px (cube) / ~640px (checker) of viewport with the
  nav panel showing, where it used to be ~1350 / ~980.

  **Every one of those widths is font-stack-dependent, and any claim of the form
  "one line at width W" must name the stack it was measured under.** The row's
  budget is text: the cube instruments' 836px (banked 2026-08-18, against the
  pre-`halheinrich/backgammon#86` four-compound-pill row — the two-group row
  is unmeasured here; the producer reports it narrower) is Windows
  Helvetica/Arial. CI's Linux Chromium and Android devices have neither and fall
  back wider — enough that a row measured with slack here can be a taller row
  there. A local re-measure at 1280 with the nav panel showing puts the cube
  instruments at 544.9px against 922px available under Windows metrics and
  589.7px under Verdana (a genuinely wider real font), so the slack is large;
  but the producer's cube-pill block is the part that wraps first when it goes,
  and when it does the row's `align-items: center` moves every *short* item down
  past that block's top without anything having wrapped in the row itself. A
  test that reads geometry off `.action-row > :first-child` will see that as a
  wrap and fail — which is how umbrella CI run 32520062178 went red on Linux
  against a commit green on Windows (`ProblemLocatorTests`, since re-keyed to
  the *last* instrument; its remarks carry the measured signatures).

  **The problem's locator shares that home, and the cluster now shrinks rather
  than wraps** (`SPEC-quiz-view.md` §4's 2026-08-21 amendment and its two
  build-time rulings, issue `halheinrich/backgammon#115`). `ProblemLocator`
  renders at **one** site, immediately after the badge, present in both view
  modes and both states — read §4 for why the tail and not the producer's title
  strip, and for what the chip may and may not show. What lives here is only
  what a reader of this code needs:

  - **The cluster is `flex: 1 1 0` + `min-width: 0` + `flex-nowrap`, and all
    three are the fixed-height contract** (`AppCss_ActionRowTail_ShrinksAndNeverWraps`).
    Flexbox breaks lines on each item's *hypothetical* main size, which for
    `flex-basis: auto` is max-content — so a contents-sized cluster puts itself
    on a second line, and an auto margin cannot prevent it. That is why the
    `ms-auto` this replaced had to go rather than be kept beside the new rules.
  - **The shrink order is weights, not breakpoints**
    (`AppCss_TailChips_ShrinkInTheRuledOrder`): the badge's `flex-shrink: 1000`
    against the locator's `1` empties the XGID text — down to a floor spelled as
    the copy button plus its gap — before the file name loses a character, and
    the game/move numbers are `flex: none` and never move. §4 ruling (i) is what
    ordered them that way.
  - **Measured 2026-08-21** on the widest row there is (a cube problem from a
    long-named `.xg` match): row height **38px, unchanged**, and board size
    unchanged to the pixel, at 1440×900 and 1280×800, in both view modes. The
    ruled fallback of a second row for cube rows only was **not needed**. Below
    the desktop band the two text chips run out of width and the cluster
    overflows its line rather than wrapping — the deliberate price of "never
    grows", and no page-level horizontal scrollbar appears at any width down to
    400px.
  - **An `.xgp` source shows its file name and no numbers** (§4 ruling (ii)).
    The discriminant is the record's `DecisionId` *kind* — `XgpDecisionId` keys
    on a bare filename precisely because there are no within-file coordinates —
    read as a type, never sniffed from the extension and never parsed out of the
    id's canonical string. The converter's synthetic `Game 1 · Move 1` on such
    records is a producer wart, booked separately; do not work around it here.
  - **The move number was verified against XG, not against the converter.** XG
    exports a single position as `<match>_<game>_<move>.xgp`; reading the parent
    `.xg` through the converter, the only decision whose XGID matches
    `match35253054_2_37.xgp` is stamped game 2, move 37, and the same holds for
    the play in `MTCH4064_1_22.xgp`. So the cube stamp (`ctx.MoveNumber + 1` —
    the number of the play it precedes) **is** XG's own number, for cube and
    play alike. Re-check this way if the stamping ever changes.
  - **Both branches are smoked in a browser** (`ProblemLocatorTests`,
    `halheinrich/backgammon#125`). The committed money `.xgp` carries the
    file-name-only branch and the truncation derivation; a synthesized `.xg`
    match carries the coordinates branch and, at the tail's widest state — a
    name past the visible cap *plus* `Game n · Move m` — ruling (i)'s two
    halves at once: the numbers read in full and the cluster still costs the
    row no line. Mutation-checked both ways (2026-08-27): breaking the
    derivation by one reddens the text pin against the app's real
    `Game 2 · Move 4`, and contents-sizing the cluster drops it 46px below
    Skip.
  - **None of it is identity.** The three facts reach display and stop
    (`TestFixtureContractTests`), which is what lets `SPEC-stats-identity.md` go
    on keying by content while the chip names a file.

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
- **`Help.razor`** — end-user documentation. Its information architecture is
  `../SPEC-help.md`'s, not this doc's: **five parts at `<h2>`** (*Before you
  start* / *Setting up a quiz* / *Answering* / *After the quiz* / *Reference*)
  with the fourteen sections at `<h3>` beneath them, each part opening with at
  most one lead sentence. Read the spec for the model; what follows is only what
  a session editing this page has to know.

  **`HelpSections` is the structure's single source** — the five parts, their
  sections, in document order, each with a hand-named `help-*` anchor id and its
  heading text. Every heading and every contents entry renders from it and every
  structural pin reads from it, so **nothing in the page or the unit tests
  restates a heading string or an id**; the e2e suite keeps hardcoded literals
  by design (it references no app assembly), which is the half of the copy-pin
  split that says which words are right. Ids are stable and never derived from
  heading text — a reword must not break a bookmark — and the `help-` prefix is
  enforced in `HelpEntry`'s constructor rather than stated, because
  `FilterHelp`'s `fh-*` ids share this document. `PageTests` pins the parts in
  order, the sections grouped **under their part**, and the contents entries as
  equal to `HelpSections` in order with every `href` resolving to an element
  that really rendered.

  The **contents block** is a `<nav>` named *Contents* listing two levels, parts
  and sections; `FilterHelp`'s own sections are not listed. It is rendered
  **once**, directly after the `<h1>` and its lead, and `app.css` decides where
  it lands. Placement is a **fit condition, not a breakpoint**
  (`../SPEC-help.md` §5, amended 2026-08-21): the block is a sticky rail beside
  the document wherever the **content area** can hold the document at its full
  800 px reading width plus the rail's track and the gap, and an ordinary list
  in the flow otherwise. **The document column is never narrowed to make room
  for the rail** — that is the amendment, ruled after the `lg` breakpoint was
  measured squeezing the reading column to 667 px at 1280 and 473 px at 992. So
  a 1280 px window has no rail with the navigation panel showing and gains one
  when it is folded, and no viewport number describes that.

  Mechanically: `article.content` is a **named inline-size container**
  (`app-content`), and the rule is `@container app-content (min-width: 1064px)`.
  A media query is ruled out by name in §5 — it cannot see the panel's state.
  The threshold is the grid's own three numbers (`--help-doc-width` +
  `--help-rail-width` + `--help-rail-gap`) plus `.container`'s gutters, since
  the query asks about the content area while the grid is laid out inside
  `.help-page`; a container query condition cannot substitute `var()`, so
  `AppCss_HelpRailThreshold_IsTheGridsOwnNumbers` is what keeps the literal and
  the properties from drifting. `.help-page` cancels Bootstrap's tier
  `max-width` for the same reason — a cap between the queried box and the grid
  would let them disagree (at 1199 px folded the content area says the rail fits
  while the lg cap leaves the grid 936 px).

  Two consequences worth knowing before editing `app.css`. **The board-area
  pins are selector-scoped, deliberately.** The two retired-glue pins
  (`…BadgeContainerQueryAnchor_StaysGone`, `…BoundedHeightGlue_StaysGone`)
  assert `container-type` / `max-height` absent from **the rules that name the
  board**,
  not from the whole file: what each retired is a declaration *on the board*,
  and the file-wide form only said that while `app.css` described nothing else
  that could legitimately carry one. It now also lays out `/help`, whose content
  area is a query container and whose rail bounds its own height. And
  **containment is real layout** — measured before landing, not after: with
  `article.content` contained, the board box is identical at 1440×900 and
  1280×800 in both view modes, and so is Home.

  A second CSS-hidden copy of the block would put two navigations named
  *Contents* in the accessibility tree, so placement is CSS's job alone; bUnit
  can evaluate no query at all, so
  `AppCss_HelpContents_IsARailOnlyWhereTheDocumentAndRailBothFit` is what holds
  the rule, and the live re-measure across widths **and both panel states** is
  the measurement. Every in-page link on
  this page goes through `AnchorHref`: a bare `#fragment` resolves against
  `<base href="/">` and lands the reader on Home.

  The section order is the journey, unchanged by the grouping: a **What you
  need** prerequisites lead (a
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
  pool, the breakdown's exhaustiveness and what a zero means, no-play-choice
  auto-skip, off-list-as-skip, cube-as-two-decisions and the claim the
  doubling half is judged on, the dice click
  advancing, the side panel's fold (§ The host layout — and see Pitfalls for
  what that note may say), and the reload reset. It closes with **Send
  feedback**.

  **Every documented constant renders from its SSOT, never as a literal** —
  file caps from `PickedFileLimits`, filenames from `QuizStatsFile` /
  `SavedFiltersDocument` (both saved-filters names: the canonical one in
  *What you need* and the saved-filters section, and the legacy name in the
  fallback sentence the umbrella ruled in — a silently-read file would cut
  against the storage-transparency posture, and the fallback is a standing
  producer rule, so the sentence doesn't rot),
  the browser rule from `FolderPickDisplay` (rendered
  *verbatim*, so this and Home's line beside the pick button cannot say
  different things), feedback + version from `AppInfo`. The *Choose filters*
  section extends that discipline one tier up: it embeds `XgFilter_Razor`'s
  `FilterHelp` as its panel reference and writes **no facet or chrome prose of
  its own**, keeping only app-level framing `FilterHelp` cannot know — that an
  applied filter gates Start, what the match count means, that the mix draws
  from that pool, and that `Shuffle order` is this app's control. `FilterHelp`
  takes one `[EditorRequired]` parameter, `HeadingLevel`, bound to **4** here:
  this page's parts are `<h2>` and its sections `<h3>` (the `h4` class on a
  section is Bootstrap sizing, not a level) and the block is embedded inside a
  section, so its lead sits at `h4` and its sections at `h5`. It was **3**
  while the page was flat; the tier the parts added pushed it down one, which
  stays inside `FilterHelp`'s `1..5` range and so needed nothing from the
  producer. `PageTests` pins that as a relationship, not as literals — which is
  why the parts moved it without the pin being rewritten — plus a page-wide
  no-skipped-level walk; the hard-coded `h4`/`h5` pair this replaced had been
  jumping `h2` → `h4` invisibly. The `no chrome
  prose` pins read **host prose only**, with the embedded block subtracted by
  its `fh-*` anchors: `FilterHelp` renders inside this very section, so a
  section-wide pin on chrome wording is vacuous in one direction and
  impossible in the other. The breakdown paragraph applies it one tier down:
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
  *What you need* and ahead of the flow: a reader deciding whether to hand
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
  the user's folder (*What you need* already names them from
  `QuizStatsFile.FileName` / `SavedFiltersDocument.FileName`); the panel's own
  localStorage entries (one sentence in user terms plus a link into
  `FilterHelp`'s storage section, both halves of it built from that section's
  exported identity — see Pitfalls); and the
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
  claim was **moved** here out of *What you need* and dropped from *Pick
  your folder*, so it is asserted once. `PageTests` pins the wiring (both keys
  from their constants; the section's `<code>` elements are *exactly* those
  two; the pointer's href and text both from `FilterHelp`'s exported
  constants, *and* landing on a heading that exists in the same render and
  carries those words; neither filename restated); `HelpAndTitlesTests` pins
  the phrasing as independent literals and clicks the anchor in a real
  browser.

  **`Help.PanelStorageHref`** — the anchor href is **computed**, never
  written as a bare `#fragment`, and its fragment is
  `FilterHelp.StorageSectionAnchorId` rather than a slug spelled here. See
  Pitfalls (`<base href="/">`).
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

It also maps **`/healthz`** — `AddHealthChecks()` + `MapHealthChecks()`, shared
framework, no registered checks — the liveness endpoint the Azure App Service
probe pings once a minute per instance (`halheinrich/backgammon#24`), pinned in
`EnvironmentFidelityTests`. **Deploy-ordering trap**: the Bicep
`healthCheckPath` apply must follow the zip deploy that ships this endpoint, or
the probe reads a 404 and the site is marked unhealthy; the umbrella runbook
owns the sequence.

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

**Environment fidelity is the gate's first line** (issues `#126`, `#127`).
Before any behavioural scenario is worth reading, `EnvironmentFidelityTests`
asks whether the app under it is the one that ships. On each route reachable
cold (`/`, `/help`, `/settings`) and on `/quiz` at the end of the pick → apply
→ start flow, it records the page's **own** requests and requires every one to
have arrived and nothing to have been logged as an error. The page is the
inventory: the test names no asset and no producer `_content/` path, so an
asset added tomorrow is covered the day it is linked and no second list can
drift out of step with the shell. **An empty 200 counts as unserved**, and that
is the load-bearing part — `MapStaticAssets` serves from a manifest built at
publish time, so an asset the manifest names but the disk lacks comes back
`200 OK` with `Content-Length: 0`, which a status check alone would wave
through (measured 2026-08-21; it is the same shape a wrong `--contentRoot`
produces). Three applied pins survive that, one per linked stylesheet, because
a 200 cannot say the browser understood the bytes: Bootstrap's `--bs-primary`
and the `xl` container width, `app.css`'s named `app-content` query container,
and `MainLayout.razor.css`'s `.sidebar` gradient reaching the page through the
generated `BgQuiz_Blazor.styles.css` bundle.

**Pin the fact once.** Those three pins are the *only* place this suite proves
a stylesheet applied. No other scenario may tighten an assertion merely so that
it would fail on an unstyled page — that is a second source for a fact already
stated, and it costs the scenario its own subject. `SidebarCollapseTests` is
the worked example: its open-panel assertion reads `> 0` rather than the
panel's designed 250px, because what the fold owes is zero-versus-not-zero and
the 250px belongs to `EnvironmentFidelityTests`.

**Layer under test = the publish output.** A collection fixture
(`PublishedAppFixture`) runs `dotnet publish` (Release) once per test run,
spawns `dotnet BgQuiz_Blazor.dll --urls http://127.0.0.1:0 --contentRoot
<publish dir>`, resolves the OS-assigned port from Kestrel's listening line,
probes readiness, and tears down on dispose. Not `dotnet run` and not
`TestServer` — those put a different layer under test. The `--contentRoot`
is load-bearing: without it `MapStaticAssets` resolves against the wrong web
root and serves 0-byte framework assets (unstyled page, WASM never boots).
The host's `BgQuiz_Blazor.dll` is the entry point.

**A publish never lands in a directory a previous run filled**
(halheinrich/backgammon#145). `dotnet publish -o` only copies in — it removes
nothing. The build is deterministic and the assets are content-fingerprinted,
so republishing unchanged sources overwrites in place and looks harmless; but
the client stamps the short git sha into its `InformationalVersion`, so *every
commit* gives the client assembly — and the AOT runtime linked against it — a
new fingerprint, written beside the old one rather than over it. In an ordinary
edit-commit-test loop that is a fresh generation per run (measured 2026-08-27:
thirteen `BgQuiz_Blazor.Client.<hash>.wasm` trios in the fixture's Debug output
— 492 files where a clean publish writes 375). The manifest names only the
current generation, so nothing complains — until a scenario reads the directory
rather than the manifest, or a stale asset outlives the change that should have
retired it and the gate green-lights an artifact that no longer matches its
sources.

`ResetPublishDirectory` deletes and recreates the directory as the publish's
first act; because that is a recursive delete it refuses any path not named
`host-publish`. Both halves are pinned. `PublishDirectoryResetTests` covers the
method over scratch directories — the clearing, the first-ever run, and the
refusal *sparing* the foreign directory rather than merely throwing; it is the
one class outside the collection, because it neither publishes nor drives a
browser and must never be handed the live publish directory the spawned host is
running out of. `PublishOutputHygieneTests` covers the outcome: it reads the
publish the collection fixture already produced (no second publish, no browser)
and requires no logical asset to appear under two fingerprints. That one is
trivially green on a fresh runner and bites exactly where accumulation happens
— a reused directory, i.e. every local run. Proven to bite: with the reset call
neutered, one run after a commit turned it red naming six assets under two
hashes each; restored, green.

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

**The one `.xg` fixture is synthesized, not committed**
(halheinrich/backgammon#125). Every committed fixture is an `.xgp`, and
`SPEC-quiz-view.md` §4 ruling (ii) forks the locator on exactly that
distinction — so the branch that shows `Game n · Move m`, and the tail's
shrink order at its widest, had shipped without ever being smoked. Real `.xg`
exports cannot fill the gap: the ones on this machine carry real players'
names, and they live under gitignored `TestData/`, which CI has never seen.
`SyntheticXgMatch` builds a short match in memory instead — `XgFileBuilder` +
`XgFileWriter` from ConvertXgToJson_Lib, whose output is byte-deterministic by
that builder's own contract — with invented player names and exactly one
analysed decision (the plays around it are unanalysed, so the file is as
single-problem as an `.xgp`). Those two libraries are this project's only
project references and they are **fixture producers only**: no scenario may
take an expectation from them, which is what keeps the independent-literal
posture intact. The pins' coordinates are derived from the builder's own
parameters — the games staged before the cube's game, the plays staged before
the cube within it — so a change in what the builder emits fails at a stated
expectation instead of quietly redefining one.

**Staging is by content, not by path.** `StageAndPickAsync` takes
`(name, bytes)` pairs, so a committed fixture (`FixtureBytes`) and a
synthesized one (`PickSynthesizedFileAsync`) reach the browser through the one
stager; what the fallback input is handed is a directory either way.

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
The one deliberate exception is `PickDuplicatedFixtureAsync`, which stages
copies *because* they collapse: it is how the #104 scenario — a file count and
a smaller match count on one screen — is set up at all.

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
stats file at every Start's re-bind). Four suites ride the fake — the fourth
being `LivePickGestureTests`, which uses it for the *opposite* purpose: it is
the only place the seam **removes** rather than supplies a capability. Its
sibling `DeadPickGestureTests` sets `window.showDirectoryPicker = undefined`
(shadowing the operation, which lives on `Window.prototype`, so a `delete`
would not bite) and the two halves differ in exactly one fact about the
browser. Headless Chromium **does** expose the picker — verified by neutering
the init script and watching the dead half fail — so the removal is
load-bearing and every other e2e scenario runs FS-Access-capable. Both halves
key on one shared `SilentPickGestureCopy.Account` fragment, because an absence
pin written against its own literal goes vacuously green the moment the notice
is reworded.
`StatsPersistenceTests` pins: one fold ⇒ one captured write with
`schemaVersion` 3, one `problems` record whose key carries no filename and
whose value is the bare tally-plus-date record (no answer-kind token — the
flat v3 record reinstated by SPEC-stats-identity §3's 2026-09-02
amendment), a cube-as-two-decisions tally, indented; a **v3 file** ⇒ read
as current: no forecast, no set-aside report, the mix offered off it, one
write folding this quiz's problem in beside its record; a **v4 file beside
its v3 sibling** ⇒ the fold across the real `folderAccess.js`: the v4 bytes
captured verbatim under `bgquiz-stats.v4.merged.json`, nothing under a
retired name, the bind's one write the merged current document (the shared
record summed, its later date kept, the v4-only record through, no kind
token), no restart note anywhere, and the quiz then recording into it;
corrupt file ⇒ polite notice + **zero writes**; denied ⇒ denied notice +
zero writes; and the fallback pick's "can't save stats" notice. Which
set-aside name each *retired* version earns is the store suite's pin (v1,
v2); the stats filenames, the staged documents' own bytes, and the wire
property names are deliberately hardcoded here — the consumer-side pin of
those contracts (the e2e project references no app assembly by design), and
the v4 literal has no other possible source: the format has no writer left
anywhere.
The fake's set-aside slot serves content only when a scenario stages a v3
sibling (`retiredV3Json`) — the fold's base — and is NotFound otherwise, so
a retirement that read it back would still fail the gesture loudly; its
merged slot is write-only. `MixWeightingTests` drives the weighted path to Done and pins `halheinrich/backgammon#87`'s gating
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

**Determinism.** No sleeps — Playwright auto-wait and explicit `Expect`
assertions only. Every flow helper ends by awaiting the
user-visible consequence of the transition it triggered. Every committed
fixture is a single-decision `.xgp` file (the `.xgp` emission policy yields at
most one decision per file), so a one-fixture quiz is exactly one problem long
with shuffle left off, and an N-fixture folder is N problems. Their *answer
types* are a contract too: the breakdown suite stages `CheckerFixture` beside
`CubeFixture`, whose best **pair** is No Double / Take, so that folder is a pool
of exactly two answer types with three empty — which is what makes its zeros
real rather than arranged — and beside `TooGoodTakeFixture`, XG's "Too good
to double/Take" position, to pin that it counts under No double / take **by
ruling** (SPEC-scoring §3's 2026-09-02 amendment) and that no too-good /
take row exists. `CubeFixture` is also money, Jacoby, cube centred — the
one position where Too good is withheld — so `QuizFlowTests` pins the
three-pill row on it, and the four-pill row (with the Too good pill that is
then the wrong claim) on the match `TooGoodTakeFixture`. In-app navigation is asserted with polling URL assertions
(`Expect(Page).ToHaveURLAsync`), **not** `WaitForURLAsync` — Blazor navigates by
`pushState` (same-document), and the navigation-event wait can lose the race
when the push lands between the triggering click and the wait's registration
(observed as a rare timeout with the app already on the target URL).

**One-shot reads after an action are timing assertions in disguise** (issues
`#126`, `#127`). A value read straight after a click, a navigation or a
re-render passes or fails on how fast the runner happened to be, and umbrella
CI is slower than any machine here — that is how Help's anchor pins stayed
green on an unstyled page that jumped instead of smooth-scrolling, and how two
of the locator's geometry reds were first misread as layout bugs. So: use a
retrying `Expect` form; where the claim relates **two** elements and no such
assertion exists (the .NET binding has no `ToPass`), wrap the measurement in
`E2eTestBase.ExpectToPassAsync`, which re-runs an ordinary xunit assertion
until it holds, so the claim stays written once and in C#. Its poll interval is
the suite's only delay and is not the sleep ruled out above — it waits out
nothing, ends the moment the assertion holds, and is the interval Playwright's
own assertions poll on. A single read stays correct only where it follows an
`Expect` that already proved the settled state **and** nothing can still be
moving, and every such site says so in a remark
(`SidebarCollapseTests.PanelWidthAsync`, `CommaDecimalLocaleTests`).

**A geometry pin checks its yardstick first.** Every "A sits below B" claim
here is arithmetic over two boxes, and a box that is absent or zero-sized makes
it trivially true — `a.Y >= b.Y + 0` holds for a board that never rendered at
all. `E2eTestBase.LaidOutBoxAsync` is the guard, and it names the degenerate
element rather than reporting a comparison nobody can read.

**Fixtures are safe to publish.** None carries a player name, verified on each
addition — anything sliced out of the corpus is exported with **anonymize ON**,
because these commit to a public repo; the copies rule is in Pitfalls.

**Board driving.** The checker scenario enters a real play by clicking the
diagram's transparent SVG hit-region rects. Point identity is contractual:
the producer stamps every point rect with `data-point="N"` and no other rect
carries the attribute (BgDiag_Razor's `BackgammonDiagram`), so
`ClickBoardPointAsync` selects `rect[data-point='N']` — adopted here and in
the bUnit `ClickPointAsync` helper at the `halheinrich/backgammon#86` leg,
replacing the render-order convention (`HitRects.Nth(point − 1)`). The bar
carries no attribute and `BarHitRect` still finds it by render order (index
24, immediately after the 24 point rects); the bUnit dice click likewise.
**The cube scenario answers with two clicks** — a claim radio and a taker
radio — through `AnswerCubeAsync(claim, response)`; `AnswerCubeNoDoubleTakeAsync`
is the No double / Take shorthand the `CubeFixture`-based scenarios rely on
(fully correct against that fixture). One click is half an answer and Submit
stays dark, so a helper that waited for Submit after one radio would time out
at exactly that gate.

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
`dotnet test` now runs both.

**The publish is AOT.** `RunAOTCompilation=true` in
`BgQuiz_Blazor.Client.csproj` (beside `InvariantGlobalization`) is the single
switch every publish inherits — the deploy recipe, this fixture, CI — so the
gate always tests the artifact that ships. Nothing else in this repo sets or
passes the flag. A cold publish (no `obj/`, no `bin/`) takes ≈ 2.5 min on the
dev machine (130 s measured 2026-08-23), so the fixture's cold run is that
plus ≈ 1 min of tests — no longer plus the ~15 min of dead time MSBuild's
reused worker nodes used to add: `PublishedAppFixture.PublishHostAsync`
enforces that itself, and its doc comment carries the mechanism
(halheinrich/backgammon#130). Incremental republishes take seconds. For a twin
non-AOT build to compare against, override on the command line only — never in
a file:

```
dotnet publish BgQuiz_Blazor/BgQuiz_Blazor.csproj -c Release -p:RunAOTCompilation=false
```

`WasmStripILAfterAOT` is implied by AOT in .NET 10 and is not set. Trim
analysis is a live gate on every publish (halheinrich/backgammon#129, closed
2026-09-01): any new trim-unsafe reflection fails the publish, and the
framework-inherent warnings that remain are suppressed member by member,
each with its justification, in `ILLink.LinkAttributes.xml` — the
`BgQuiz_Blazor.Client.csproj` comment beside the trim properties carries the
mechanics. Evidence that the gate holds against real reflection: the
standalone Release publish of the tree that added the app's first
`[JSInvokable]` callback (halheinrich/backgammon#149, 2026-09-02) exited 0
with no ILLink warning.

**Every test failing at once with a ~5-minute wait and a 25 ms duration is a
publish failure, not a suite's worth of defects.** The fixture publishes
before any test runs, so a broken publish fails every test identically through
`PublishedAppFixture.InitializeAsync`; read the exception text, which carries
the whole `dotnet publish` log. One cause seen locally is `MSB4216` (*could
not create or connect to a task host*) — kill stale `MSBuild` / `dotnet` nodes
and re-run. Don't go looking for an app regression until the publish itself
succeeds.

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
- **Never describe a filter facet — or a panel control — in BgQuiz's own
  prose.** What a facet admits and what a control does are both the lib's
  behavior, so their documentation lives with the lib: `/help` embeds
  `XgFilter_Razor`'s `FilterHelp` and adds app-level framing only. The
  producer's Pitfall draws the line: app-level framing means *where the panel
  sits in this app and what applying it unlocks here*, never what the controls
  do — so the disclosure and its hidden-active badge, Apply's two disabled
  states and `Clear filters` are all off-limits here (umbrella #36 removed the
  copy that described them). A description written here is a second encoding
  that passes every test on the day it ships and silently goes wrong the next
  time the lib changes. If `FilterHelp` lacks prose the app needs, extend it in
  `XgFilter_Razor`; don't restore it here.
  Corollary for the sweep after a producer facet change: grep BgQuiz for the
  retired *field* names **and** read the user-facing copy — the compiler
  catches the first class and nothing catches the second.
- **Same rule for what the panel *stores*: point, never restate.** `Help`'s
  data section says in plain user terms that the filter panel also remembers
  its settings in the reader's browser, and links into `FilterHelp`'s storage
  section for the detail. It **must not** name or describe `FilterPanel`'s
  localStorage keys — those are `internal` to `XgFilter_Razor` and rendered
  there from the panel's own constants, so a copy here could only be a prose
  literal nothing in this repo can catch drifting. Inlining that list "so the
  reader doesn't have to follow a link" is the tempting edit and is exactly the
  defect. The pin
  (`Help_DataSection_PointsAtTheFilterPanelsStorageInsteadOfDescribingIt`)
  asserts the section's `<code>` elements are *exactly* BgQuiz's own two keys
  — a form that survives the panel renaming its keys, which a
  `DoesNotContain("xg_filter_config")` would not.
- **The deep link's slug and its words are both the producer's.** Build them
  from `FilterHelp.StorageSectionAnchorId` / `StorageSectionHeading`; never
  spell either here. Held as host literals they drifted in a way nothing
  caught: the e2e tripwire looks the link up *by BgQuiz's own text*, so a
  producer reword left the by-name lookup matching, the id unchanged, the
  scroll assertion green — and the link naming something other than the
  section it lands on. Note the limit of the unit half: a wiring pin reading
  the same constant the page reads agrees with a host literal that happens to
  equal it today, so it cannot prove the binding — `HelpAndTitlesTests`'
  independent literals are what say which value is right, and the *structural*
  half of the bUnit pin (the target heading exists in the render and carries
  the link's words) is what catches the producer drifting off its own
  constant.
- **A bare `#fragment` href navigates to Home, not down the page.** `App.razor`
  sets `<base href="/">`, and a fragment-only href resolves against the **base
  URI**, not the current address — so on `/help` a bare `href="#…"` resolves to
  the app root plus that fragment, the router matches `/`, and the reader lands
  on `Home` (observed, with markup that looked right).
  `Help.AnchorHref` composes the href from `NavigationManager.Uri`
  (fragment stripped) instead — correct under a sub-path deployment too, where
  `/help#…` would not be. **Every** in-page link on that page goes through it,
  the storage pointer and all nineteen contents entries alike: the trap belongs
  to the page, not to any one link, so a new link written the obvious way would
  simply be broken. Blazor then handles the same-document navigation
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
  `HasNoPlayChoice` runs `MoveGenerator.GeneratePlays` on the dice, and a cube
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
  `OnPlayCompleted`. Keep it present: the row is controlled on the pair, so
  without the binding its selections are never adopted.
- **The cube row has no half-answered state any more, and no `@key`.** Under
  `halheinrich/backgammon#86` the row was two radio groups holding its two
  half-selections as its own state; a half-answered row composed to no pair,
  agreed with the null the page held, and survived a Skip — which is why the
  row carried `@key="current"`. Since `halheinrich/backgammon#187` every
  pill is a complete pair and the row renders its checked pill from `Value`,
  so `HandleStateChanged`'s `_completedCube = null` clears it outright and a
  key would be a defensive remount guarding nothing (the same reasoning that
  keeps a key off the play entry). Don't add one back:
  `Quiz_CubeActions_ChosenThenSkip_NextProblemStartsClean_WithoutARemount`
  pins the same instance carrying over clean. Gating Submit on
  `_completedCube is null` is correct as is — that is "a pill chosen".
- **`OfferTooGood` is `[EditorRequired]` and the page feeds it
  `current.CanBeTooGood`, never a re-derivation.** The producer derives
  the offerability fact once, on the record, from money / Jacoby / cube
  owner together; a page-side `IsMoneyGame && IsJacoby == true && …` would
  be a second spelling of that rule and drift the day it changes. Note the
  default `TestFixtures.CubeDecision()` is money, Jacoby on, cube **turned**
  (`CubeOwner.OnRoll`), so it offers Too good; pass `cubeOwner:
  CubeOwner.Centered` for the withheld case.
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
  `legal.Count == 0` will silently miss every pass position. The auto-skip rule
  needs no case for it: the sentinel *is* one entry, which is exactly what
  `legal.Count == 1` already reads as "no choice".
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
- **Lifetime stats fold as the run advances past a problem, never at Submit,
  and what folds is the answer of record.** The model is SPEC-scoring.md §2
  (ratified 2026-08-26) — read it there. What it means here: the *first*
  submission against a problem is final for `Score` and for the fold the moment
  it is made; `RedoAsync` re-opens the problem for practice, and the practice
  submissions are discarded as if they never happened, so `Review` (the
  displayed review) and `_answerOfRecord` (what folds) genuinely differ after a
  redo. Folding at Submit would still be wrong, for a new reason: `Score` and
  `ProblemStatsDocument` are per-problem-once, and the fold's *trigger* is the
  run advancing past the problem. The deliberate flip side is unchanged — an
  answer of record the run never advances past (tab close, Start/Restart without
  continuing) never folds. There are **three** fold sites, not one: `ContinueAsync`,
  `SkipCurrentAsync` (reachable mid-practice-cycle) and `EndQuizAsync`
  (halheinrich/backgammon#57), sharing one `FoldAnswerOfRecordAsync`. Each folds
  for a reason worth keeping — **every answer visible on Done has reached the
  lifetime record**, an invariant that held for free while Continue was the only
  route to Done, and which Done's own "nothing here needs saving" line states to
  the user. A fourth fold site needs that same argument; a *silent* one would
  break the line. Skips, off-list plays, practice submissions, and auto-skipped
  no-choice positions never reach the sink at all (producer contract, plus §2).
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
  are false where the browser refused to ask.
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
  The cube answer reaches Redo's clean slate the same way (the review branch
  unmounted the row), and since `halheinrich/backgammon#187` carries no
  `@key` either — nulling the bound pair clears a row whose every pill is a
  complete pair; see the cube-row pitfall above.
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
  `AppCss_RetiredBadgeContainerQueryAnchor_StaysGone` pins both halves —
  `container-type` scoped to **the rules that name the board**, since `/help`'s
  fit condition makes the layout's content area a legitimate query container
  (§ `Help.razor`); `cqw` stays file-wide, because nothing in this app sizes in
  container units.
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
- **`wwwroot/lib/` is ignored except one file, and that is deliberate.**
  `.gitignore` excludes `**/wwwroot/lib/` and then re-includes exactly
  `BgQuiz_Blazor/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css` — the single
  file `App.razor` links (Bootstrap 5.3.3; the version is pinned in a comment
  beside that link, because that link is the only place naming the file).
  Nothing here restores `lib/`: no `libman.json`, no npm step, no MSBuild
  target. So while the folder was ignored it existed on one machine, and
  **umbrella CI and every fresh clone built and served the app unstyled for
  months** without a test noticing — behaviour survives a missing stylesheet, so
  behaviour tests do (issue `halheinrich/backgammon#126`; found only because the
  locator's geometry pin, `halheinrich/backgammon#115`, reported a `.btn` 21px
  tall and was at first read as a layout bug rather than as the messenger).
  Committing the file rather than adding a restore step was ruled: a clone must
  be styled with no network at all, and LibMan would put network weather in the
  build. Re-including a file inside an excluded directory needs the
  level-by-level negation ladder git requires — a bare `!path/to/file` cannot
  match, because git never descends into an excluded directory. **The guard is
  `EnvironmentFidelityTests`** (§ The e2e smoke gate): the cold-load scenarios
  catch the file failing to arrive — including as the `200`-but-empty response
  `MapStaticAssets` actually returns — and one applied pin asserts a computed
  value only Bootstrap produces. If `lib/` is ever re-lost, those fail instead
  of the layout quietly going missing.
- **A file that must answer at a fixed URL belongs to the host's `wwwroot`.**
  `BgQuiz_Blazor/wwwroot` is what the host serves by name (`app.css`,
  `favicon.png`, `lib/`, `robots.txt`, `js/navFold.js` — a classic script the
  host shell tags because it must run on static pages before any runtime
  boots). The `.Client` project has a `wwwroot` again since
  halheinrich/backgammon#149 — `js/quizKeys.js`, the quiz page's ES module,
  which the page imports by a document-relative path exactly as the folder
  module was imported when it lived there (static web assets of the
  referenced client serve at the app root; the folder module itself ships as
  BgFolderAccess_Razor's `_content` asset now). That is the client-side rule:
  a module a component imports may live beside the component's project. A
  file that must
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
  persist at all remain hardware questions. Hardware corrected this
  checklist's own expectation: `showDirectoryPicker` is **present** on
  current Chrome for Android and opens a real chooser there
  (halheinrich/backgammon#109), while a WebView-wrapping browser on the same
  tablet raised no chooser at all (halheinrich/backgammon#108). So a dead
  pick gesture is **capability**-shaped, not screen-shaped — which is what
  the honest-notice posture (halheinrich/backgammon#105) is written against.
- **Done-page retrospective.** Per-problem review ships *in-quiz*; what's
  missing is a *post-quiz* retrospective on Done — the four-way
  `ScoreBreakdown` reports only aggregates, with no way to revisit
  individual problems after finishing. A scrollable list of the `History` /
  `CubeHistory` entries (each re-rendering its solution diagram) would close
  the loop.
- **e2e Too-Good coverage.** The Too Good / *Take* verdict is retired
  (SPEC-scoring §3's 2026-09-02 amendment, `halheinrich/backgammon#187`):
  `TooGoodAndTake.xgp` is now the position that decided the amendment, a No
  Double / Take by ruling, and `QuizFlowTests.TooGoodToDoubleTakePath_…`
  runs it end to end (Too good is the wrong claim, then No double on a
  practice retry). Still open for Too Good / *Pass* — the one too-good
  verdict left: no committed fixture has `nd > 1 && dt ≥ 1` (it is pinned in
  bUnit on a synthesized record). Close by sourcing one from the corpus via
  ExtractFromXgToCsv's slice export — **anonymize ON**, the fixture commits
  to a public repo — into `E2eTests/Fixtures/`, plus a `QuizFlowTests` case
  (banner "Too Good" + `Too Good: correct · Pass: correct` verdict → Done).
  Synthesis was rejected: the producer's clean writer surface is unanalyzed
  by design. Surfaced 2026-07-22; narrowed 2026-09-01; re-scoped 2026-09-02.
