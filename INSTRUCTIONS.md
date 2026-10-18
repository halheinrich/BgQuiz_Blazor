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
its libraries in 2026-10 (halheinrich/backgammon#421): increment 1 the
first complete path, increment 2 the folder context, lifetime stats, the
Stats page and the weighted mix, increment 3 resample, Done's resample-and-
restart, the order tie-break, the reload notice and the help text.

Depends on BgGame_Lib (scoring, lifetime stats, the mix, the run's pool
functions `DistinctProblems` and `ProblemSequence`, `ProblemOrder`,
`QuizScore`), ConvertXgToJson_Lib and XgFilter_Lib (the parse and the
filters), XgFilter_Razor (the shared filter surface and its app-scoped
`FilterSetup`), BgFolderAccess_Razor (the browser's folder facilities, as a
context per landed folder), BgDiag_Razor and BackgammonDiagram_Lib (the
board), BgUiPrimitives_Razor (notices, browser storage), BgMoveGen
(forced-play detection).

## Layout

- **`BgQuiz_Blazor`** — the host: `App.razor` (the shell, vendored Bootstrap,
  `navFold.js`), `MainLayout` / `NavMenu`, `/healthz`, the not-found route.
- **`BgQuiz_Blazor.Client/Quiz`** — the app layer: `QuizApp` (the one
  coordinator), `QuizSetup` (the folder in hand as its `IFolderContext`, its
  `LoadedProblems`, its `StatsProbe` and its `FolderDocumentStorage`),
  `Selection` (the count for one filter and ranking), `QuizRun` (the run
  over a fixed sequence, immutable), `FinishedQuiz`, `ProblemLoader` (the
  parse, once, at the pick), `QuizStats` (a run's recording into its
  folder's stats file; `QuizStatsReport` is what it came to, `QuizStatsFile`
  the names and the serializer contract, `StatsProbe` what the file holds:
  read at the pick, brought up to date as a run's recording touches it),
  `MixDraft` (the mix builder's edit state, persisted as the
  user types) with `MixDisplay`, `QuizLiveMarker` (the live mark in
  `sessionStorage` the reload notice reads), `QuizSettings`, `NotesPlacementStore`,
  `BrowserStorageCondition`, `PickedFileLimits` (the pick caps: this app's
  policy).
- **`Components/Pages`** — `Home` (setup, the mix panel and Start), `Quiz`
  (the board, the answer and navigation row, the run's notices), `Stats`
  (the progress so far), `Done` (the summary and Restart), `Settings`,
  `Help`; `MixPanel`, `ScorePanel`, `ScoreBreakdown`. **`Components`** —
  `DecisionNotes` (the notes overlay and its placement), `TailMenu`,
  `ProblemLocator`, `XgidLabel` / `XgidCopy`, `ReturnControl`,
  `ProblemFolderLabel`.
- **`wwwroot/js`** — `quizKeys.js` (Space), `actionRowFit.js` (the row's
  fit measurement), `decisionNotes.js`, `menuButton.js`.
- **Tests** — `BgQuiz_Blazor.Tests` (xUnit + bUnit: `QuizAppTests` is the
  coordinator's table, `PageTests` the pages' wiring, `QuizStatsTests` the
  recording and the probe, `MixDraftTests` / `MixPanelTests` the mix
  builder, `QuizRunTests` the run) and `BgQuiz_Blazor.E2eTests` (Playwright
  over a published build, the main path and the browser-only behaviour,
  the File System Access path over a fake directory handle; CI runs both).

## How it works

`QuizApp` owns one **phase** (idle, picking, loading, preparing, recording:
the one operation in flight), the **setup** (none, or the folder's parsed
problems with the folder's context, its stats probe and a filter-source
token of its own), the **run** (live, with its stats recording, or a
finished result with the filter and mix it ran under and its stats report),
and the **selection** (the count for the setup, the filter in effect and the
current ranking, valid for exactly those three). Pages render it and send
commands; a command that does not fit the phase is refused before any await,
so nothing overlaps. `Changed` fires on every move, on the renderer's context.

- **Pick.** The browser's prompts are the user's turn (picking); a folder it
  accepts lands as an `IFolderContext` and is parsed once, in full, with the
  screen locked and a progress line (loading), then its stats file is probed
  (what the mix's visibility and Home's forecasts read). A new sample ends
  the run in progress as it lands, keeping its summary, and releases the old
  setup, its count and its folder before the new files are read
  (SPEC-quiz-history §9: two parsed collections never coexist); a cancelled
  pick, a folder with no files and a pick that fails before its sample lands
  keep the setup and the run as they were, by either picker (ruled
  2026-10-16 and 2026-10-18); a folder that landed but cannot be read as
  problems leaves no setup, and a run the landing finished stays finished.
- **Resample.** The setup's own context drawn again at the limits, landing
  as a pick lands — the run ends, the files parse afresh, the stats file is
  re-probed — while the context, the filter source (so the filter in
  effect), the mix and the saved filters stay; a failed or empty draw keeps
  the sample. Offered while `QuizSetup.CanResample`, the producer's per-kind
  report (SPEC-filtering §4).
- **Filter.** XgFilter_Razor's `FilterSetup` is the one owner of the draft
  and what is applied; the app reports each setup's token to it and raises
  nothing from its snapshot stream. Its `Applied` event is the one entry for
  the count, which runs as the preparing operation. The ready-empty filter
  is in effect without an Apply; an edit that empties an applied filter puts
  it in effect too, and is counted only at the next Clear or Apply.
- **The mix.** Visible means in effect (SPEC-filtering §5): the panel is on
  Home exactly while the Settings choice is on and the setup's probe — the
  pick's reading, then what the recording leaves in the file, bind and
  write by write — found a stats document with content, and the rows on it
  are what Start composes with. The draft is app-scoped and written through on every valid edit; a
  visible draft that does not build holds Start; a weighted Start whose bind
  finds no document is refused with a one-quiz "Start without mix" escape.
- **Start.** Bind the stats recording over the setup's folder, filter the
  problems under the ranking, deduplicate by position (`DistinctProblems`),
  compose the sequence with the mix in the order (`ProblemSequence`), present
  the first problem. The prepared run and its recording replace whatever was
  in hand only on success, so a refused or faulted attempt leaves the run in
  hand recording where it was (halheinrich/backgammon#404). Restart composes
  again from the setup in hand under the finished run's filter and mix,
  weighted only while the mix is in effect now. Done's *Restart with a new
  sample* (`ResampleAndRestartAsync`) draws and restarts as one operation:
  the summary stands whatever it comes to, and a sample that landed is held.
- **The run** indexes its sequence: Next, Back, first, last are synchronous.
  Each answer of record is folded into the folder's stats file and written
  back before the next command is accepted (recording), with the pre-write
  guard of SPEC-stats-identity §5; an unreadable file records nothing and is
  never written, a failed write stops writing, and the quiz goes on either
  way. `QuizRun` is immutable; each transition returns the run that follows.
- **Clear** lets the folder go and keeps the run, which keeps recording into
  its folder. A folder neither the setup nor the live run holds is released.
- **The live mark** (`QuizLiveMarker`, `sessionStorage`) is set when a run
  starts and cleared wherever one finishes; Home reads it on a boot with no
  run at all, says the reload reset the quiz, once, and clears it.

## Running it

`dotnet run --project BgQuiz_Blazor` serves http://localhost:5087 as a Debug
host, twenty times slower at the parse than the Release publish; judge speed
on `dotnet publish BgQuiz_Blazor/BgQuiz_Blazor.csproj -c Release`, which is
what the e2e suite and the deploy use (the deploy adds
`-p:StampGitShaSuffix=false`; dev builds carry a `+g<sha>` suffix in the
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
  that does not describe what is on screen. The count is filter-only: with a
  mix in effect it is the pool the mix draws from, and Home says so.
- **An active mix contributes each decision id once** (`ProblemSequence`), so
  a fixture whose decisions share the default id composes to one problem.
- **Nothing expensive runs from a snapshot notification.** The filter
  owner's `Applied` event is the entry for the count; pages observe
  snapshots for rendering only.
- **The parse is the one long operation** and it yields on the clock's 50 ms
  budget so the progress line paints. Keep it inside the loading phase.
- **A dismissal lives on the thing it is about** (SPEC-notices §2): the
  setup's notices on `QuizSetup`, the run's on `QuizStatsReport` and the
  live run, the storage refusal on `BrowserStorageCondition`.
- **`BrowserStoragePlan` is strict about every storage key a page reads**:
  the mix panel's mount reads `MixDraft.StorageKey`.
- **The shell runs `navFold.js` parser-blocking after `blazor.web.js`**; the
  action row's fit relies on it having run before any page renders.
- **The app never localises**: `InvariantGlobalization` is on in the client
  so equities and XGIDs format period-decimal under every browser locale.
- **Bootstrap is vendored and tracked** (`wwwroot/lib/bootstrap`, the one
  re-included path): a fresh clone builds styled with no network.
