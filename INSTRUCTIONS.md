# BgQuiz_Blazor

> Collaboration contract: [`../AGENTS.md`](../AGENTS.md)
> Umbrella status & dependency graph: [`../INSTRUCTIONS.md`](../INSTRUCTIONS.md)
> Mission & principles: [`../VISION.md`](../VISION.md)

## Stack

C# / .NET 10 / Blazor Web App, Interactive Server render mode.
Visual Studio 2026 on Windows.

## Solution

`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\BgQuiz_Blazor\BgQuiz_Blazor.slnx`

## Repo

https://github.com/halheinrich/BgQuiz_Blazor — branch `main`.

## Depends on

- **BgGame_Lib** — substrate. `IProblemSetSource`, `SubmittedPlay`,
  `SubmittedCubeAction`, `QuizScore` (segmented: `PlayDecisions` /
  `DoubleDecisions` / `TakeDecisions` + derived `Total`). The controller
  talks to the source through this interface and scores via
  `QuizScore.Plus(SubmittedPlay)` / `QuizScore.Plus(SubmittedCubeAction)`.
- **BgDataTypes_Lib** — data types. `BgDecisionData`, `Play`,
  `PlayCandidate`, `BoardState`, `CubeDecisionPair`, `CubeAction`. The
  structural matcher hashes `Play.DeduplicationKey()` against each
  `PlayCandidate.Play`'s key; cube scoring reads `DecisionData`'s
  `BestDoublerAction` / `BestTakerAction` / `DoublerActionError` /
  `TakerActionError`.
- **BgMoveGen** — `MoveGenerator.GeneratePlays`, used by the controller's
  pass-position auto-skip detection.
- **BgDiag_Razor** — `BackgammonPlayEntry` (click-driven play assembly) and
  `BackgammonCubeEntry` (two-group cube-decision entry, emitting
  `CubeDecisionPair` via `OnCubeDecisionCompleted`) + the underlying
  `BackgammonDiagram` (read-only board view).
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
- **XgFilter_Lib** — `DecisionFilterSet`, `FilterConfig`,
  `DecisionTypeFilter` / `DecisionTypeOption` (materialized from the user's
  decision-type choice; the controller adds no filter of its own).
- **XgFilter_Razor** — `FilterPanel.razor`. Hosted on `/` so quiz-start
  filters share the same UI used by `ExtractFromXgToCsv`.
- **ConvertXgToJson_Lib** — picked up transitively via the filter pipeline
  (parses the .xg files the server-disk source iterates).

## Directory tree

```
BgQuiz_Blazor.slnx
BgQuiz_Blazor/
  BgQuiz_Blazor.csproj
  Program.cs
  appsettings.json
  appsettings.Development.json
  Properties/
    launchSettings.json
  Quiz/
    QuizController.cs
    ProblemReview.cs
    QuizOptions.cs
    ProblemSetSelection.cs
    ServerDiskProblemSetSource.cs
    ServerDiskProblemSetSourceFactory.cs
  Components/
    _Imports.razor
    App.razor
    Routes.razor
    Layout/
      MainLayout.razor / .razor.css
      NavMenu.razor / .razor.css
      ReconnectModal.razor / .razor.css / .razor.js
    Pages/
      Home.razor / .razor.cs        — landing: filter panel + Start
      Quiz.razor / .razor.cs        — active problem (play or cube)
      Done.razor / .razor.cs        — final summary
      ScorePanel.razor              — compact header strip (Total only)
      ScoreBreakdown.razor          — four-way Play/Double/Take/Total table
      Error.razor
      NotFound.razor
  wwwroot/                          — static assets (favicon, app.css, Bootstrap)
BgQuiz_Blazor.Tests/
  BgQuiz_Blazor.Tests.csproj
  TestFixtures.cs
  FakeProblemSetSource.cs
  QuizControllerTests.cs
  ServerDiskProblemSetSourceTests.cs
  ServerDiskProblemSetSourceFactoryTests.cs
  PageTests.cs
```

## Architecture

### Quiz flow

```
/        Home.razor    → FilterPanel + Start Quiz button
                          on Start: Controller.StartAsync(filters), Nav→/quiz

/quiz    Quiz.razor    → per problem: answering → review → advance
                          answering (Controller.Review null):
                            routes by Controller.Current.Decision.IsCube:
                            checker → BackgammonPlayEntry
                                      + Submit / Skip / Undo last / Undo all / Restart
                            cube    → BackgammonCubeEntry
                                      + Submit / Skip / Restart (no Undo)
                          review (Controller.Review set, after Submit):
                            read-only BackgammonDiagram (DiagramMode.Solution,
                            user's answer marked) + verdict line
                            + Continue / Restart
                          IsFinished (on Continue / Skip) → Nav→/done

/done    Done.razor    → ScorePanel (Total) + ScoreBreakdown (four-way)
                          + Restart / Start over
```

### `QuizController` — per-circuit state machine

Scoped DI lifetime — one instance per Blazor Server circuit. Holds the
active `IProblemSetSource` enumerator, the running `QuizScore`, the per-
problem `SubmittedPlay` (`History`) and `SubmittedCubeAction`
(`CubeHistory`) histories, and a `SkippedCount` for non-scoring outcomes
(off-list submissions, explicit Skip). The two histories are kept separate
(mirroring `_history`/`History`) because the two scored-result types are
distinct shapes — a unified history would force consumers to type-test.
Pages observe state transitions via the `StateChanged` event; the
controller fires it on start, submit, continue, skip, and restart.

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
- **`ContinueAsync`** — the only exit from review: clears `Review` and
  advances to the next problem (current `AdvanceAsync` body). Exhausting
  the source here flips `IsFinished`. No-op outside review.
- **`SkipCurrentAsync`** — bypasses review and advances immediately, but
  only from the answering state (no-op while a `Review` is showing).

`ProblemReview` lives in BgQuiz_Blazor (not BgGame_Lib): it is per-circuit
UI state, and adding it to the `SubmittedPlay` / `SubmittedCubeAction`
submodule would cross the boundary. `ProblemReview.Play` carries the
matched candidate index (`-1` off-list); `ProblemReview.Cube` carries the
two per-half equity losses. The Quiz page maps these onto the solution
request's `UserPlayIndex` / `UserDoubleError` + `UserTakeError` so the
diagram marks the *quiz user's* answer rather than the .xg-recorded
player's.

**Source construction is factory-injected.** The controller takes a
`ProblemSetSourceFactory` delegate (`DecisionFilterSet → IProblemSetSource`)
rather than constructing `ServerDiskProblemSetSource` directly. `Program.cs`
registers the delegate scoped, bound to `ServerDiskProblemSetSourceFactory.Create`.
Phase 2+ alternatives (uploaded files, deployed bundles, curated libraries)
plug in by registering a different factory; the controller is unchanged.
Unit tests substitute a fake source the same way.

**Filter ownership.** `StartAsync` takes a `FilterConfig` (the wire DTO
emitted by `XgFilter_Razor.FilterPanel.OnFilterConfigChanged`), not a
runtime `DecisionFilterSet`. The controller calls `FilterConfig.Build()`
to produce its own filter pipeline, which it owns end-to-end — no shared
mutable state ever exists between the page and the controller. The
`ProblemSetSourceFactory` delegate still takes the runtime
`DecisionFilterSet` (the source's contract is the runtime pipeline; the
controller is the authority on assembling it).

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
against `Current.Decision.Plays` by comparing `Play.DeduplicationKey()`
tuples. An in-list match contributes to the score: `EquityLoss == 0.0`
is the "best play" test (matching the established `PlayCandidate`
convention — multiple candidates may share zero loss). An off-list match
counts as a skip (`SkippedCount++`, no history entry, score unchanged).
There's no equity loss to record, and off-list submissions usually signal
an analysis omission rather than a user mistake. Either way a `Review`
(`ProblemReview.Play`, `OffList` true and index `-1` for off-list) is set
so the user still sees the best play on the solution diagram.

### `ServerDiskProblemSetSource` — Phase 1 source

Wraps `XgFilter_Lib.FilteredDecisionIterator.IterateXgDirectoryDiagrams`
(both `*.xg` match files and `*.xgp` position files). The constructor
takes `(directory, filters, ILoggerFactory)` and builds a single
`FilteredDecisionIterator` instance held for the source's lifetime;
`ILoggerFactory` is preferred over `ILogger<FilteredDecisionIterator>`
so the source's contract doesn't leak the inner type.

Each call to `EnumerateAsync` (an async `IAsyncEnumerable` iterator)
freshly re-walks the inner sync `IterateXgDirectoryDiagrams` (a lazy
`IEnumerable`), so directory walks remain per-call and re-iteration is
the trivial case. `Count` is null (computing it would mean a full
pre-pass through a potentially large filtered iterator).
`Name` returns the directory's leaf name.

The directory is supplied by the caller (see *Problem-set source
selection* below), not read from configuration — the constructor is
directory-agnostic.

### Problem-set source selection

The source directory is chosen in the UI on `/`, not captured from
configuration at startup.

- **`ProblemSetSelection`** — a per-circuit (`Scoped`) mutable holder
  with a single `string Directory`. Seeded at construction from the
  configured `Quiz:ProblemSetDirectory` default; `Home.razor` overrides
  it from the user's localStorage-persisted choice and writes back on
  every edit. A bare holder by design — directory validity is enforced
  by its readers, not by the holder.
- **`ServerDiskProblemSetSourceFactory`** — the Phase 1
  `ProblemSetSourceFactory` implementation (`Scoped`). `Create` reads
  `ProblemSetSelection.Directory` at invocation time (quiz-start, not
  DI-registration), throws `InvalidOperationException` on a blank
  directory, and builds a `ServerDiskProblemSetSource`. `Program.cs`
  binds `Create` as the `ProblemSetSourceFactory` delegate.

`Quiz:ProblemSetDirectory` in `appsettings.json` is the *default seed*
for a fresh circuit, not the runtime authority.

### Pages

- **`Home.razor`** — a problem-set directory text input above the
  `FilterPanel` from XgFilter_Razor. The directory lives in the
  per-circuit `ProblemSetSelection`; the input writes through on
  `@onchange` and persists to localStorage (key
  `bgquiz_problemsetdirectory`), and `OnAfterRenderAsync` rehydrates
  from localStorage on first render. Start is gated on three
  conditions: filters Applied at least once, a non-blank directory, and
  that directory existing on the server. The existence check is a
  filesystem call, so it runs once per edit (and once after
  rehydration), cached in a field — `CanStart` is read every render and
  does no I/O. Subscribes to `OnFilterConfigChanged` (the panel's
  emit-event after Apply), captures the `FilterConfig`, and hands it to
  `Controller.StartAsync`. Catches start-time exceptions (directory
  removed since validation, `FilterConfig.Build()` validation failure,
  etc.) and surfaces them as a banner instead of crashing the circuit.
- **`Quiz.razor`** — mirrors the controller's three-state flow, branching on
  `Controller.Review`. In the **answering** state (`Review` null) it routes by
  `Current.Decision.IsCube` over
  `DiagramRequest.FromDecisionData(Current, DiagramMode.Problem)`: checker
  decisions to `BackgammonPlayEntry` (click-driven play assembly), cube
  decisions to `BackgammonCubeEntry` (two atomic button groups). Both entry
  components are strict — each throws `NotImplementedException` on the other
  half's decision type, so the route must be exact. Submit (a synchronous
  handler, since the controller's Submit no longer awaits) is gated on the
  relevant completion callback having fired (`OnPlayCompleted` →
  `_completedPlay`, `OnCubeDecisionCompleted` → `_completedCube`); both latches
  reset on every transition. `BackgammonCubeEntry` re-fires on every
  post-completion change, so `_completedCube` always holds the latest pair and
  the user can revise before Submit. The action row varies by kind: cube has no
  Undo (no partial-move state); checker keeps Undo last / Undo all (clearing the
  latched play, since the component does not notify on undo). In the **review**
  state (`Review` set, after Submit) it renders a read-only `BackgammonDiagram`
  in `DiagramMode.Solution` — the filled analysis panel, the same view the PPTX
  exporter renders — plus a compact verdict line and Continue / Restart. The
  solution request is built with `DiagramRequest.Builder.From(Current.Position,
  Current.Decision, Current.Descriptive, DiagramMode.Solution)`, then the user's
  marks are overridden from `Review`: `UserPlayIndex` for a play (`-1` off-list
  draws no marker), or `UserDoubleError` / `UserTakeError` for a cube.
  `FromDecisionData` is **not** used here because it defaults those marks from
  the .xg-recorded player, not the quiz user. Subscribes to
  `Controller.StateChanged` in `OnInitialized`, unsubscribes in
  `IDisposable.Dispose`; redirects to `/done` when `IsFinished` flips.
- **`Done.razor`** — final `ScorePanel` (Total) + `ScoreBreakdown`
  (four-way) + total problems shown + Restart with same filters / Start
  over. "Problems shown" is `PlayDecisions.Submitted +
  DoubleDecisions.Submitted + SkippedCount` — **not** `Total.Submitted`,
  which counts decisions and so double-counts each cube position (one
  Double + one Take).
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

`InteractiveServer` — chosen for Phase 1 simplicity (server-side state,
no WASM payload to ship .xg parsing into). State lives on the server;
each client needs an active SignalR circuit. Reload loses the active
quiz. Pre-Azure-deployment will revisit render mode and persistence.

The render mode is set **globally** on `<Routes @rendermode="InteractiveServer" />`
in `App.razor`. Page-level `@rendermode InteractiveServer` directives
(`Home.razor`, `Quiz.razor`, `Done.razor`) are kept as documentation
but are redundant in practice — the global Routes setting is what
propagates interactivity to RCL-imported (Razor Class Library) child
components like `FilterPanel` (XgFilter_Razor) and `BackgammonPlayEntry`
(BgDiag_Razor). A page-level directive alone does not reliably cross
RCL assembly boundaries; without the global `<Routes>` setting, those
child components prerender static HTML and `@onclick` handlers silently
fail. See Pitfalls.

## Public API

This is an application, not a library — no exported types or HTTP
endpoints. The externally visible surface is the route map:

- `/` → `Home` — filter selection + Start
- `/quiz` → `Quiz` — active problem (redirects to `/` if no quiz, `/done` if finished)
- `/done` → `Done` — final summary (redirects to `/` if no quiz)
- Default error page → `Error.razor`
- Default 404 page → `NotFound.razor`

## Pitfalls

- **State is per-circuit.** A page reload tears down the SignalR circuit
  and the scoped `QuizController`; the active quiz is lost. Persistence
  is a future concern — for Phase 1 the user starts over after reload.
- **Cube decisions carry `Dice == [0, 0]` — never auto-skip them.**
  `IsPassPosition` runs `MoveGenerator.GeneratePlays` on the dice; a cube
  decision's `[0, 0]` produces the no-legal-play sentinel, so without the
  `if (data.Decision.IsCube) return false;` guard at the top of
  `IsPassPosition`, every cube decision is silently auto-skipped and the
  whole cube feature is invisible. The guard is the first line; don't
  remove it.
- **Entry components are strict on decision type.** `BackgammonPlayEntry`
  throws `NotImplementedException` on a cube decision and
  `BackgammonCubeEntry` throws on a play decision. `Quiz.razor`'s
  `IsCube` route must be exact; a mis-route fails loudly at render.
- **`OnCubeDecisionCompleted` is `[EditorRequired]`.** Omitting the binding
  on `BackgammonCubeEntry` surfaces as `RZ2012` (→ error under
  `-warnaserror`), not a silent splat — unlike the play side's
  `OnPlayCompleted`. Keep the binding present.
- **Razor silently drops bindings to non-existent component parameters.**
  `<FilterPanel OnFiltersChanged="..."/>` against a panel that exposes
  `OnFilterConfigChanged` does not fail at build or render time — the
  binding is simply never invoked. Symptom is a callback that "obviously"
  fires never firing. When wiring an event from an RCL-imported
  component, verify the parameter name against the source. The bUnit
  regression test `PageTests.Home_FilterPanelEmitsConfig_EnablesStartButton`
  guards against this trap.
- **Off-list submission semantics.** A structurally-legal play that
  doesn't appear in the analyzer's candidate list counts as a skip, not
  a scoring miss. This is rare on well-analyzed positions and signals
  an analysis omission rather than user error. Don't expect every
  user-submitted play to land in History.
- **Pass-position sentinel is not empty-list.** `MoveGenerator.GeneratePlays`
  signals "no legal play" with `count == 1 && plays[0].Count == 0`
  (a single zero-move Play, dice forfeited). Code that gates on
  `legal.Count == 0` will silently miss every pass position.
- **`Quiz` is both a namespace (`BgQuiz_Blazor.Quiz`) and the page type
  (`BgQuiz_Blazor.Components.Pages.Quiz`).** Test code that does
  `Render<Quiz>()` after `using BgQuiz_Blazor.Quiz;` hits a CS0118
  ambiguity. Test files use a `using QuizPage = ...` alias to
  disambiguate.
- **Source factory throws lazily.** `ServerDiskProblemSetSourceFactory.Create`
  validates the selected directory only at invocation time — a blank
  directory throws `InvalidOperationException`, a non-existent one
  throws `DirectoryNotFoundException` from the source constructor.
  Pages that merely observe state (Done, etc.) load even with no
  selection; the throw fires on `Controller.StartAsync` and the Home
  page surfaces it as a banner. `Home.razor`'s Start gate normally
  pre-empts both throws, so they are a backstop for the
  validated-then-removed race, not the primary error path.
- **Render mode propagates from `<Routes>`, not from `@page`.** Setting
  `@rendermode InteractiveServer` only on the page directive leaves
  RCL-imported child components (`FilterPanel`, `BackgammonPlayEntry`)
  rendered as static prerender HTML. Their `@onclick` handlers wire up
  in JS but never dispatch over SignalR — clicks silently no-op. The
  fix is `<Routes @rendermode="InteractiveServer" />` in `App.razor`,
  which propagates interactivity across RCL boundaries. The bUnit page
  tests do not exercise render-mode dispatch, so they will not catch
  this — verify interactivity in a real browser.

## Subproject-internal next steps

- **Empty-collection UX.** When a filter set produces zero decisions,
  the current flow bounces Start through `/quiz` straight to `/done`
  with a 0/0 score, with no feedback that the filter was empty. Add a
  pre-flight check (or post-Start `IsFinished` detection) and surface a
  "no decisions match these filters" banner on `/` instead.
- **Phase 2+ design.** Answer tracking with weighted re-recurrence on
  wrong answers; the three two-agent modes (user-vs-user, user-vs-bot,
  bot-vs-bot tournament). All three queue behind the umbrella's
  decision-identification scheme — Phase 2+ needs stable per-decision
  IDs to track correctness over time.
- **Persistence.** Quiz state currently dies with the SignalR circuit.
  When the app moves toward Azure deployment, decide between
  cookie-based session keys, an in-memory cache survival across reloads,
  or a persistence store keyed to authenticated users.
- **Render-mode revisit pre-Azure.** Interactive Server is the right
  Phase 1 default; global hosting may want WebAssembly or Auto. The
  controller's scoped lifetime and the source factory abstraction were
  designed so render-mode migration only touches Program.cs registration
  and page lifecycle, not the state machine.
- **Directory picker is local-mode-only.** The `Home.razor` problem-set
  directory input picks a folder on the host filesystem — meaningful
  only when the app runs on the same machine as the user's `.xg` files
  (local / self-hosted). An Azure deployment has the user's data
  elsewhere, so a path picker is meaningless there; the source would be
  upload-based instead (a Phase 2+ source kind). When an upload/remote
  source and a mode concept land, the picker's visibility must be gated
  — shown in local mode, hidden in remote mode.
- **Done-page retrospective.** Per-problem review now ships *in-quiz* (the
  review state's solution diagram shows the best play/action, the equity gap,
  and — for cube — which half the user got wrong). What's still missing is a
  *post-quiz* retrospective on Done: the four-way `ScoreBreakdown` reports only
  aggregate Play/Double/Take/Total accuracy, with no way to revisit individual
  problems after finishing. A scrollable list of the `History` / `CubeHistory`
  entries (each re-rendering its solution diagram) would close the loop.
