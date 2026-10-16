# BgQuiz_Blazor

> Collaboration contract: [`../AGENTS.md`](../AGENTS.md)
> Umbrella status & dependency graph: [`../INSTRUCTIONS.md`](../INSTRUCTIONS.md)
> Mission & principles: [`../VISION.md`](../VISION.md)

## What it is

The player-facing quiz: pick a folder of `.xg` / `.xgp` files, filter the
decisions in it, and answer them on the board. C# / .NET 10 Blazor Web App in
WebAssembly render mode: a thin ASP.NET Core host (`BgQuiz_Blazor`) serves a
WASM client (`BgQuiz_Blazor.Client`) that does everything in the browser; the
files never leave the machine. Solution
`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\BgQuiz_Blazor\BgQuiz_Blazor.slnx`;
repo https://github.com/halheinrich/BgQuiz_Blazor, branch `main`. Rebuilt on
its libraries in 2026-10 (halheinrich/backgammon#421); increment 1 is the
first complete path, with lifetime stats, the weighted mix and resample to
follow.

Depends on BgGame_Lib (scoring, the run's pool functions `DistinctProblems`
and `ProblemSequence`, `ProblemOrder`, `QuizScore`), ConvertXgToJson_Lib and
XgFilter_Lib (the parse and the filters), XgFilter_Razor (the shared filter
surface and its app-scoped `FilterSetup`), BgFolderAccess_Razor (the
browser's folder facilities), BgDiag_Razor and BackgammonDiagram_Lib (the
board), BgUiPrimitives_Razor (notices, browser storage), BgMoveGen (forced-play
detection).

## Layout

- **`BgQuiz_Blazor`** — the host: `App.razor` (the shell, vendored Bootstrap,
  `navFold.js`), `MainLayout` / `NavMenu`, `/healthz`, the not-found route.
- **`BgQuiz_Blazor.Client/Quiz`** — the app layer: `QuizApp` (the one
  coordinator), `QuizSetup` (the folder in hand and its `LoadedProblems`),
  `Selection` (the count for one filter and ranking), `QuizRun` (the run
  over a fixed sequence, immutable), `FinishedQuiz`, `ProblemLoader` (the
  parse, once, at the pick), `QuizSettings`, `NotesPlacementStore`,
  `BrowserStorageCondition`, `PickedFolderDocumentStorage` (saved filters
  live in the picked folder), `PickedFileLimits` (the pick caps: this app's
  policy).
- **`Components/Pages`** — `Home` (setup and Start), `Quiz` (the board, the
  answer and navigation row), `Done` (the summary and Restart), `Settings`,
  `Help`; `ScorePanel`, `ScoreBreakdown`. **`Components`** — `DecisionNotes`
  (the notes overlay and its placement), `TailMenu`, `ProblemLocator`,
  `XgidLabel` / `XgidCopy`, `ReturnControl`, `ProblemFolderLabel`.
- **`wwwroot/js`** — `quizKeys.js` (Space), `actionRowFit.js` (the row's
  fit measurement), `decisionNotes.js`, `menuButton.js`.
- **Tests** — `BgQuiz_Blazor.Tests` (xUnit + bUnit: `QuizAppTests` is the
  coordinator's table, `PageTests` the pages' wiring, `QuizRunTests` the run)
  and `BgQuiz_Blazor.E2eTests` (Playwright over a published build, the main
  path and the browser-only behaviour; CI runs both).

## How it works

`QuizApp` owns one **phase** (idle, picking, loading, preparing: the one
operation in flight), the **setup** (none, or the folder's parsed problems
with a filter-source token of its own), the **run** (live, or a finished
result with the filter it ran under), and the **selection** (the count for
the setup, the filter in effect and the current ranking, valid for exactly
those three). Pages render it and send commands; a command that does not fit
the phase is refused before any await, so nothing overlaps. `Changed` fires
on every move, on the renderer's context.

- **Pick.** The browser's prompts are the user's turn (picking); a folder it
  accepts is parsed once, in full, with the screen locked and a progress line
  (loading). Forced plays are set aside. A new sample ends the run in
  progress as it lands, keeping its summary, and releases the old setup and
  its count before the new files are read (SPEC-quiz-history §9: two parsed
  collections never coexist); a cancelled pick keeps the setup in hand; a
  folder with no files, or one that cannot be read, leaves no setup, and a
  run the landing finished stays finished.
- **Filter.** XgFilter_Razor's `FilterSetup` is the one owner of the draft
  and what is applied; the app reports each setup's token to it and raises
  nothing from its snapshot stream. Its `Applied` event is the one entry for
  the count, which runs as the preparing operation (or waits for the one in
  flight). The ready-empty filter is in effect without an Apply; an edit
  that empties an applied filter puts it in effect too, and is counted only
  at the next Clear or Apply — Home says so meanwhile.
- **Start.** Filter the problems under the ranking, deduplicate by position
  (`DistinctProblems`), compose the sequence in the order
  (`ProblemSequence`, blank mix for now), present the first problem. The
  prepared run replaces whatever was in hand only on success; nothing
  matching keeps the run or summary in hand. Restart composes again from the
  setup in hand under the finished run's filter.
- **The run** indexes its sequence: Next, Back, first, last and the
  submissions are synchronous. `QuizRun` is immutable; each transition returns
  the run that follows. "Problem n of N" is exact.
- **Clear** lets the folder go and keeps the run.

## Running it

`dotnet run --project BgQuiz_Blazor` serves http://localhost:5087. The
published artifact is what the e2e suite and the deploy use:
`dotnet publish BgQuiz_Blazor/BgQuiz_Blazor.csproj -c Release` (the deploy
adds `-p:StampGitShaSuffix=false`; dev builds carry a `+g<sha>` suffix in the
footer). `dotnet test BgQuiz_Blazor.Tests` for the unit and bUnit suite;
`dotnet test BgQuiz_Blazor.E2eTests` publishes and drives the app in
Playwright's Chromium (install once with the `playwright.ps1` under the test
build's output).

## Pitfalls

- **Every routable page is `.Client`-side with `prerender: false`**: the
  coordinator does not exist during a server prerender.
- **The pick caps are this app's** (`PickedFileLimits`): BgFolderAccess_Razor
  ships no numbers. The truncation notice reads the sample's kinds.
- **Filter before deduplicating.** Copies are not interchangeable to a
  filter; `Selection.ComputeAsync` does it in that order.
- **The count is keyed by setup, filter and ranking** (`Selection.Describes`);
  a resample changes the setup, Settings the ranking. Never show a selection
  that does not describe what is on screen.
- **Nothing expensive runs from a snapshot notification.** The filter
  owner's `Applied` event is the entry for the count; pages observe
  snapshots for rendering only.
- **The parse is the one long operation** and it yields on the clock's 50 ms
  budget so the progress line paints. Keep it inside the loading phase.
- **The shell runs `navFold.js` parser-blocking after `blazor.web.js`**; the
  action row's fit relies on it having run before any page renders.
- **The app never localises**: `InvariantGlobalization` is on in the client
  so equities and XGIDs format period-decimal under every browser locale.
- **Bootstrap is vendored and tracked** (`wwwroot/lib/bootstrap`, the one
  re-included path): a fresh clone builds styled with no network.
