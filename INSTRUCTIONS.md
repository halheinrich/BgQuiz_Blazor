# BgQuiz_Blazor

> Session conventions: [`../CLAUDE.md`](../CLAUDE.md)
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
  `QuizScore`. The controller talks to the source through this interface and
  scores via `QuizScore.Plus(SubmittedPlay)`.
- **BgDataTypes_Lib** — data types. `BgDecisionData`, `Play`,
  `PlayCandidate`, `BoardState`. The structural matcher hashes
  `Play.DeduplicationKey()` against each `PlayCandidate.Play`'s key.
- **BgMoveGen** — `MoveGenerator.GeneratePlays`, used by the controller's
  pass-position auto-skip detection.
- **BgDiag_Razor** — `BackgammonPlayEntry` (click-driven play assembly) +
  the underlying `BackgammonDiagram` (read-only board view).
- **BackgammonDiagram_Lib** — `DiagramRequest` + `DiagramOptions` +
  `DiagramRequest.FromDecisionData(BgDecisionData, DiagramMode.Problem)`,
  the canonical data-to-renderer mapping. Picked up transitively via
  BgDiag_Razor; referenced directly here so the page can call the factory.
- **XgFilter_Lib** — `DecisionFilterSet`, `DecisionTypeFilter`,
  `DecisionTypeOption.CheckerPlaysOnly` for the Phase 1 cube-policy filter
  the controller appends.
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
    QuizOptions.cs
    ServerDiskProblemSetSource.cs
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
      Quiz.razor / .razor.cs        — active problem
      Done.razor / .razor.cs        — final summary
      ScorePanel.razor              — shared header strip
      Error.razor
      NotFound.razor
  wwwroot/
BgQuiz_Blazor.Tests/
  BgQuiz_Blazor.Tests.csproj
  TestFixtures.cs
  FakeProblemSetSource.cs
  QuizControllerTests.cs
  ServerDiskProblemSetSourceTests.cs
  PageTests.cs
```

## Architecture

### Phase 1 quiz flow

```
/        Home.razor    → FilterPanel + Start Quiz button
                          on Start: Controller.StartAsync(filters), Nav→/quiz

/quiz    Quiz.razor    → BackgammonPlayEntry over Controller.Current
                          + Submit / Skip / Undo last / Undo all / Restart
                          IsFinished → Nav→/done

/done    Done.razor    → ScorePanel (final) + Restart / Start over
```

### `QuizController` — per-circuit state machine

Scoped DI lifetime — one instance per Blazor Server circuit. Holds the
active `IProblemSetSource` enumerator, the running `QuizScore`, the per-
problem `SubmittedPlay` history, and a `SkippedCount` for user-driven
non-scoring outcomes. Pages observe state transitions via the
`StateChanged` event; the controller fires it after every `AdvanceAsync`
(start, submit, skip, restart).

**Source construction is factory-injected.** The controller takes a
`ProblemSetSourceFactory` delegate (`DecisionFilterSet → IProblemSetSource`)
rather than constructing `ServerDiskProblemSetSource` directly. The factory
is registered as a singleton in `Program.cs`. Phase 2+ alternatives
(uploaded files, deployed bundles, curated libraries) plug in by registering
a different factory; the controller is unchanged. Unit tests substitute a
fake source the same way.

**Filter ownership.** `StartAsync` takes a `FilterConfig` (the wire DTO
emitted by `XgFilter_Razor.FilterPanel.OnFilterConfigChanged`), not a
materialized `DecisionFilterSet`. The controller calls `FilterConfig.Build()`
to produce its own filter pipeline, which it owns end-to-end — no shared
mutable state ever exists between the page and the controller. The
`ProblemSetSourceFactory` delegate still takes the runtime
`DecisionFilterSet` (the source's contract is the runtime pipeline; the
controller is the authority on assembling it).

**Cube policy.** `StartAsync` adds a `DecisionTypeFilter(CheckerPlaysOnly)`
to its own pipeline once, after `Build()`. Restart re-uses the augmented
pipeline without re-building. AND-semantics: if the user picked CubeOnly
the intersection is empty (the Home banner sets that expectation).

**Pass-position auto-skip.** Each `AdvanceAsync` step pulls the next
decision and tests it with `MoveGenerator.GeneratePlays(board, d1, d2)`.
The no-legal-play sentinel from BgMoveGen is `count == 1 && plays[0].Count == 0`
(a single Play of zero moves — dice forfeited), **not** an empty list. Pass
positions are silently skipped; they don't show to the user and don't
count toward `SkippedCount`.

**Off-list submission.** `SubmitPlayAsync(Play)` matches the user's play
against `Current.Decision.Plays` by comparing `Play.DeduplicationKey()`
tuples. An in-list match scores: `EquityLoss == 0.0` is the "best play"
test (matching the established `PlayCandidate` convention — multiple
candidates may share zero loss). An off-list match counts as a skip
(`SkippedCount++`, no history entry, score unchanged) — there's no equity
loss to record, and off-list submissions usually signal an analysis
omission rather than a user mistake.

### `ServerDiskProblemSetSource` — Phase 1 source

Wraps `XgFilter_Lib.FilteredDecisionIterator.IterateXgDirectoryDiagrams`
(both `*.xg` match files and `*.xgp` position files). The constructor
takes `(directory, filters, ILoggerFactory)` and builds a single
`FilteredDecisionIterator` instance held for the source's lifetime;
`ILoggerFactory` is preferred over `ILogger<FilteredDecisionIterator>`
so the source's contract doesn't leak the inner type.

Each call to `EnumerateAsync` invokes the iterator's instance method
(itself a lazy `IEnumerable`), so directory walks remain per-call and
re-iteration is the trivial case. `Count` is null (computing it would
mean a full pre-pass through a potentially large filtered iterator).
`Name` returns the directory's leaf name.

The directory is configured via `Quiz:ProblemSetDirectory` in
`appsettings.json` (or any standard ASP.NET Core configuration source).
Empty / whitespace surfaces as a friendly banner on `/`; the Start button
stays disabled until both filters are applied and configuration is set.

### Pages

- **`Home.razor`** — `FilterPanel` from XgFilter_Razor, Start gated on
  Apply-clicked + non-empty config. Subscribes to `OnFilterConfigChanged`
  (the panel's emit-event after Apply), captures the `FilterConfig`, and
  hands it to `Controller.StartAsync`. Catches start-time exceptions
  (missing directory, `FilterConfig.Build()` validation failure, etc.)
  and surfaces them as a banner instead of crashing the circuit.
- **`Quiz.razor`** — hosts `BackgammonPlayEntry` (click-driven play
  assembly) over `DiagramRequest.FromDecisionData(Current, DiagramMode.Problem)`.
  Subscribes to `Controller.StateChanged` in `OnInitialized`,
  unsubscribes in `IDisposable.Dispose`. Submit is gated on
  `OnPlayCompleted` having fired; resets to disabled on every transition.
  Undo last / Undo all delegate to `BackgammonPlayEntry.UndoLast()` /
  `UndoAll()` and clear the latched completed play (the component does
  not notify the consumer on undo).
- **`Done.razor`** — final ScorePanel + total problems shown +
  Restart-with-same-filters / Start-over.
- **`ScorePanel.razor`** — single dense status strip used by both Quiz
  and Done. Renders Submitted / Correct (with %) / Skipped / average
  equity loss; optional Source name and Heading.

### Render mode

`InteractiveServer` — chosen for Phase 1 simplicity (server-side state,
no WASM payload to ship .xg parsing into). State lives on the server;
each client needs an active SignalR circuit. Reload loses the active
quiz. Pre-Azure-deployment will revisit render mode and persistence.

The render mode is set **globally** on `<Routes @rendermode="InteractiveServer" />`
in `App.razor`. Page-level `@rendermode InteractiveServer` directives
(`Home.razor`, `Quiz.razor`, `Done.razor`) are kept for documentation
but are redundant — the global Routes setting is what propagates
interactivity to RCL-imported child components like `FilterPanel`
(XgFilter_Razor) and `BackgammonPlayEntry` (BgDiag_Razor). A page-level
directive alone does not reliably cross RCL assembly boundaries; without
the global `<Routes>` setting, those child components prerender static
HTML and `@onclick` handlers silently fail. See Pitfalls.

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
- **Cube decisions are filtered at the source.** The controller adds
  `DecisionTypeFilter(CheckerPlaysOnly)` to its own pipeline on
  `StartAsync`; `BackgammonPlayEntry` would throw `NotImplementedException`
  on a cube decision but in practice never sees one. A future cube-entry
  sibling component (BgDiag_Razor Deferred entry) lifts this limitation.
- **Razor silently drops bindings to non-existent component parameters.**
  `<FilterPanel OnFiltersChanged="..."/>` against a panel that exposes
  `OnFilterConfigChanged` does not fail at build or render time — the
  binding is simply never invoked. Symptom is a callback that "obviously"
  fires never firing. When wiring an event from an RCL-imported
  component, verify the parameter name against the source. Phase 1's
  filter-not-applied bug landed via exactly this mechanism; the bUnit
  regression test in `PageTests.Home_FilterPanelEmitsConfig_EnablesStartButton`
  now guards against it.
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
- **Source factory throws lazily.** `Program.cs` registers a
  `ProblemSetSourceFactory` whose closure validates
  `Quiz:ProblemSetDirectory` only at invocation time. Pages that merely
  observe state (Done, etc.) load even with bad config; the throw fires
  on `Controller.StartAsync` and the Home page surfaces it as a banner.
- **Render mode propagates from `<Routes>`, not from `@page`.** Setting
  `@rendermode InteractiveServer` only on the page directive leaves
  RCL-imported child components (`FilterPanel`, `BackgammonPlayEntry`)
  rendered as static prerender HTML. Their `@onclick` handlers wire up
  in JS but never dispatch over SignalR — clicks silently no-op. The
  fix is `<Routes @rendermode="InteractiveServer" />` in `App.razor`,
  which propagates interactivity across RCL boundaries. Discovered
  during Phase 1 browser verification; the bUnit page tests do not
  exercise render-mode dispatch and therefore did not catch it.

## Subproject-internal next steps

- **Post-submit solution feedback.** `Quiz.razor` renders
  `BackgammonPlayEntry` over `DiagramRequest.FromDecisionData(Current,
  DiagramMode.Problem)`; Problem mode hides the analysis panel by design,
  and `SubmitPlayAsync` advances directly to the next problem. The user
  sees their running score in `ScorePanel` but never sees per-problem
  feedback — what the best play was, what their equity loss was, why a
  candidate was preferred. Phase 1 ships without this; surfaced during
  the (β) consumer-adaptation session's smoke verification. Likely needs
  an intermediate "review" state in `QuizController` (not just
  `Current`/`IsFinished`/`SkippedCount`) and a corresponding render
  branch in `Quiz.razor` that flips to a solution view post-submit and
  back to the entry form on a Continue click.
- **Empty-collection UX.** When a filter set produces zero decisions,
  the current flow bounces Start through `/quiz` straight to `/done`
  with a 0/0 score, with no feedback that the filter was empty. Add a
  pre-flight check (or post-Start `IsFinished` detection) and surface a
  "no decisions match these filters" banner on `/` instead. User-
  suggested during Phase 1 verification.
- **Phase 2+ design.** Answer tracking with weighted re-recurrence on
  wrong answers; the three two-agent modes (user-vs-user, user-vs-bot,
  bot-vs-bot tournament). All three queue behind the umbrella's
  decision-identification scheme (umbrella queue item 2) — Phase 2+
  needs stable per-decision IDs to track correctness over time.
- **Persistence.** Quiz state currently dies with the SignalR circuit.
  When the app moves toward Azure deployment, decide between
  cookie-based session keys, an in-memory cache survival across reloads,
  or a persistence store keyed to authenticated users.
- **Render-mode revisit pre-Azure.** Interactive Server is the right
  Phase 1 default; global hosting may want WebAssembly or Auto. The
  controller's scoped lifetime and the source factory abstraction were
  designed so render-mode migration only touches Program.cs registration
  and page lifecycle, not the state machine.
- **Cube-decision support.** Awaits the cube-entry sibling component in
  BgDiag_Razor (Deferred). Once that lands, drop the
  `CheckerPlaysOnly` auto-append from `StartAsync` and route cube vs.
  checker decisions to the appropriate entry component.
