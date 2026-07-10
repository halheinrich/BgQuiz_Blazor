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

- **BgGame_Lib** — substrate. `IProblemSetSource`, `SubmittedPlay`,
  `SubmittedCubeAction`, `QuizScore` (segmented: `PlayDecisions` /
  `DoubleDecisions` / `TakeDecisions` + derived `Total`). The controller
  talks to the source through this interface and scores via
  `QuizScore.Plus(SubmittedPlay)` / `QuizScore.Plus(SubmittedCubeAction)`.
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
                                      QuizController, PickedProblemSet,
                                      AppliedFilter, ProblemSetSourceFactory
                                      (all scoped)
  _Imports.razor
  Quiz/
    QuizController.cs
    ProblemReview.cs
    PickedProblemSet.cs             — picked-files holder (+ Summary)
    PickedFileLimits.cs             — pick caps (bytes / count / derived MB)
    AppliedFilter.cs                — applied-filter holder (start-gate half)
    WasmUploadedProblemSetSource.cs — in-browser stream-backed source
  Components/
    Pages/
      Home.razor / .razor.cs        — landing: file picker + filter panel + Start
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
  QuizControllerTests.cs
  WasmUploadedProblemSetSourceTests.cs
  PickedProblemSetTests.cs
  AppliedFilterTests.cs
  PageTests.cs
  NavMenuTests.cs                   — the sidebar Help link (sole /help entry point)
  MainLayoutTests.cs
  NotFoundPipelineTests.cs          — WebApplicationFactory wire tests: unmatched
                                      paths 404 with the NotFound page body
```

Each page carries a per-page
`@rendermode @(new InteractiveWebAssemblyRenderMode(prerender: false))`
directive — that is how interactivity is set under WASM (see Render mode).

## Architecture

### Quiz flow

```
/        Home.razor    → FilterPanel + Start Quiz button
                          on Start: Controller.StartAsync(filters), Nav→/quiz

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
                          + Restart / Start over

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
`StateChanged` event; the controller fires it on start, submit, redo,
continue, skip, and restart.

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
- **`ContinueAsync`** — the only *forward* exit from review: clears `Review`
  and advances to the next problem (current `AdvanceAsync` body). Exhausting
  the source here flips `IsFinished`. No-op outside review.
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
`ProblemSetSourceFactory` delegate (`DecisionFilterSet → IProblemSetSource`)
rather than constructing a source directly. The client's `Program.cs`
registers the delegate scoped as a lambda that reads the `PickedProblemSet`
holder and builds a `WasmUploadedProblemSetSource` over the user's picked
files. The picked set is read at **invocation** time (`StartAsync`), not at
DI registration, so a file choice made before Start takes effect. Future
alternatives (deployed bundles, curated libraries) plug in by registering a
different factory; the controller is unchanged. Unit tests substitute a fake
source the same way.

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
set. `EnumerateAsync` also `await Task.Yield()`s between items so a long
synchronous run doesn't monopolise the single WASM thread.

`Count` is null (an up-front count would require a full filtered pre-pass).
`Name` is `"No files"` / the single file's name / `"{N} files"`. Decision-type
admission is governed entirely by the supplied `filters`; the source injects
no policy of its own.

### `PickedProblemSet` — the browser-picked source holder

The per-app (`Scoped`, one-per-tab in WASM) holder for the user's
in-browser-picked `.xg` / `.xgp` files. `Home.razor` writes it
(`Set` / `Clear`); the `ProblemSetSourceFactory` reads `Files` to build a
`WasmUploadedProblemSetSource`. Files are buffered byte arrays (read out of
each `IBrowserFile` once at pick time) so the source can re-enumerate on
Restart.

- **`Summary`** (`string?`) — the holder-owned label for the picked set:
  the single file's name when one is picked, `"{N} files picked"` when
  several are, `null` when none are. This is the **single source of truth**
  for how a picked set describes itself. `Home.razor` renders it directly
  rather than caching the text in a component field — the field reset to
  null when the page was re-instantiated by in-app navigation, blanking the
  summary while the file gate stayed satisfied. Deriving from the persisted
  holder keeps the summary and the Start gate consistent by construction.
  `PickedProblemSetTests` pins the three branches; the bUnit
  `Home_PrePopulatedHolder_RendersSummaryAndEnablesStart` pins the
  navigate-back render.

The picked set is **in-memory only**: it survives in-app navigation but is
reset by a full browser reload (the WASM runtime re-boots). Reload-survival
is a deferred arc, matching `QuizController`.

### `PickedFileLimits` — the pick caps, single-sourced

`internal static class PickedFileLimits` (Quiz/) holds the two caps the file
picker applies — `MaxFileBytes` (50 MB, handed to `IBrowserFile.OpenReadStream`)
and `MaxFileCount` (500, handed to `InputFileChangeEventArgs.GetMultipleFiles`) —
plus `MaxFileMegabytes`, **derived** from `MaxFileBytes`.

The caps have two consumers: `Home` *enforces* them, `Help` *documents* them.
Leaving them as private constants on `Home` would have forced the help page to
restate "50 MB" / "500" as prose, so raising a cap would silently make the
documentation wrong. Deriving the megabyte figure (rather than writing `50`
twice) is what makes the SSOT actually hold.
`PageTests.Help_StatesFileCaps_SourcedFromTheConstantsHomeEnforces` asserts the
rendered prose against the constants, not against the literals, so page and rule
cannot drift. The constants stay `internal` (matching `Home.AppVersion`'s
internal-static-not-a-literal precedent); the `.Client` csproj grants
`InternalsVisibleTo` to the test project rather than widening them to public.

### `AppliedFilter` — the filter half of the start gate

The per-app (`Scoped`, one-per-tab in WASM) holder for the `FilterConfig` the
user has **deliberately applied** on `Home` — the sibling of `PickedProblemSet`
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

In-memory only, reset on full reload — same deferred-arc caveat as the other
two holders.

### Pages

- **`Home.razor`** — an `<InputFile multiple accept=".xg,.xgp">` file
  picker above the `FilterPanel` from XgFilter_Razor. On pick, each browser
  file is read out of its `IBrowserFile` stream once (per-file and per-pick
  caps from `PickedFileLimits`, the same constants `Help` documents) and
  buffered into a `PickedFile` (extension-bearing name +
  bytes) in the per-app `PickedProblemSet`; the bytes are parsed in-browser
  and never uploaded. The picked-set label renders straight from
  `PickedProblemSet.Summary` (the SSOT — not a transient field). Start is
  gated on **two** conditions, both read from per-app scoped holders so the
  gate survives navigation: filters Applied at least once *and* at least one
  file picked (`CanStart => AppliedFilter.IsApplied && ProblemSet.HasFiles`).
  Subscribes to `OnFilterConfigChanged` → `AppliedFilter.Set` (the panel's
  emit-event after Apply) and `OnFilterDirty` → `AppliedFilter.Clear`; on
  Start hands `AppliedFilter.Config` to `Controller.StartAsync`. Catches read
  failures and start-time exceptions (`FilterConfig.Build()` validation
  failure, source construction failure) and surfaces them as a banner instead
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
  against over-triggering.
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
  the .xg-recorded player, not the quiz user. The review diagram's
  `OnDiceClicked` is bound to the same `ContinueAsync` handler as the Continue
  button, so clicking the dice hit-region advances past the solution exactly
  like Continue. Redo calls `Controller.RedoAsync()`, falling back to the
  answering branch on the same problem; no explicit reset or `@key` is needed
  to give the returning entry component a clean slate — Submit already
  unmounted it when the page swapped into the review branch, so Blazor
  constructs a genuinely new instance on the way back regardless of whether
  the incoming request describes the same problem (which is exactly what
  Redo returns to). "Show stats" navigates to `/stats`. Subscribes to
  `Controller.StateChanged` in `OnInitialized`, unsubscribes in
  `IDisposable.Dispose`; redirects to `/done` when `IsFinished` flips.
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
  files → filters → answering → scoring → review → stats/done) followed by the
  semantics a user cannot discover by clicking around — pass positions are
  auto-skipped and never shown, an off-list play counts as a skip rather than a
  wrong answer, a cube position scores as two decisions, clicking the dice on
  the solution diagram advances like Continue, and a full browser reload resets
  everything (in-app navigation does not). Text-first; the illustrative board
  diagram is a deferred nicety. Lives in the `.Client` (not as a static host
  page) so a mid-quiz Help → Back round trip doesn't disturb the WASM runtime
  holding quiz state — the same reason `Stats` does. Unlike `Stats` it **never
  redirects**: help is reachable from any state, including a cold visit or a
  bookmark. Only the "Back to quiz" button is conditional, on the exact
  predicate `Stats` guards with (`HasStarted && !IsFinished`). It does not
  subscribe to `StateChanged` — nothing changes while the user reads. The file
  caps render from `PickedFileLimits`, never as literals. The host's
  `NavMenu` Help link is the **only** entry point; `Quiz`'s action row
  deliberately gets no "?" button, because its fixed height is load-bearing for
  board sizing.
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

## Public API

This is an application, not a library — no exported types or HTTP
endpoints. The externally visible surface is the route map:

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

- **State resets on full reload, not on in-app navigation.** "Scoped" in
  WASM is one instance per loaded app (one tab), so `QuizController`,
  `PickedProblemSet`, and `AppliedFilter` survive `/` ↔ `/quiz` ↔ `/done`
  navigation but a full browser reload re-boots the runtime and loses
  everything (picked files, applied filter, quiz progress). Reload-survival
  is a deferred arc — don't assume reload resumes. Anything that *should*
  survive navigation belongs in a scoped holder, not a component field —
  the two halves of Home's start gate were moved off transient fields for
  exactly this reason. (Genuinely per-visit page state — e.g. Home's
  `_startError` banner — correctly stays a component field and resets on
  navigate-back.)
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
  lazily, mid-enumeration, not at construction. The `Home` pick handler
  preserves `IBrowserFile.Name` (extension-bearing) precisely for this;
  `WasmUploadedProblemSetSourceTests.EnumerateAsync_ExtensionlessName_…`
  guards the failure mode. Start-time exceptions (this, plus
  `FilterConfig.Build()` validation) surface on `Controller.StartAsync` and
  `Home.razor` shows them as a banner rather than faulting the app.
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

- **Phase 2+ design.** Answer tracking with weighted re-recurrence on
  wrong answers; the three two-agent modes (user-vs-user, user-vs-bot,
  bot-vs-bot tournament). All three queue behind the umbrella's
  decision-identification scheme — Phase 2+ needs stable per-decision
  IDs to track correctness over time.
- **Reload-resume (persistence).** A full browser reload re-boots the WASM
  runtime and loses the picked files and quiz progress. Surviving a reload
  needs the picked bytes + decisions/progress persisted client-side
  (IndexedDB — `localStorage` is too small for buffered `.xg` bytes); this
  is a deferred arc of its own. Until then, reload-reset is the intended
  default.
- **Done-page retrospective.** Per-problem review now ships *in-quiz* (the
  review state's solution diagram shows the best play/action, the equity gap,
  and — for cube — which half the user got wrong). What's still missing is a
  *post-quiz* retrospective on Done: the four-way `ScoreBreakdown` reports only
  aggregate Play/Double/Take/Total accuracy, with no way to revisit individual
  problems after finishing. A scrollable list of the `History` / `CubeHistory`
  entries (each re-rendering its solution diagram) would close the loop.
