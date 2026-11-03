# BgQuiz_Blazor

> Collaboration contract: [`../AGENTS.md`](../AGENTS.md)
> Umbrella status & dependency graph: [`../INSTRUCTIONS.md`](../INSTRUCTIONS.md)
> Mission & principles: [`../VISION.md`](../VISION.md)
> Open work: the [tracker](https://github.com/halheinrich/backgammon/issues), label `BgQuiz_Blazor`.

## What it is

The player-facing quiz: pick a folder of `.xg` / `.xgp` files, filter the
decisions in it, and answer them on the board. C# / .NET 10 Blazor Web App in
WebAssembly render mode: a thin ASP.NET Core host (`BgQuiz_Blazor`) serves a
WASM client (`BgQuiz_Blazor.Client`) that does everything in the browser; the
files never leave the machine. It is an application: nothing references it,
so it has no public API. Rebuilt on its libraries in 2026-10
(halheinrich/backgammon#421); commits before that describe the earlier app
layer.

Depends on:

- **BgDataTypes_Lib** — the decision, position and identity types.
- **BgGame_Lib** — scoring, lifetime stats, the mix, the run's pool functions
  `DistinctProblems` and `ProblemSequence`, `ProblemOrder`, `QuizScore`.
- **ConvertXgToJson_Lib**, **XgFilter_Lib** — the parse and the filters, and
  the walk's report of what it left out, forced plays included.
- **XgFilter_Razor** — the shared filter surface and its app-scoped
  `FilterSetup`.
- **BgFolderAccess_Razor** — the browser's folder facilities, as a context per
  landed folder.
- **BgDiag_Razor**, **BackgammonDiagram_Lib** — the board.
- **BgUiPrimitives_Razor** — notices, browser storage and its refusal
  condition.

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
  `MixDraft` (the mix builder's edit state, persisted as the user types) with
  `MixDisplay`, `QuizLiveMarker` (the live mark in `sessionStorage` the reload
  notice reads), `QuizSettings`, `NotesPreferencesStore`, `PickedFileLimits`
  (the pick caps: this app's policy).
- **`Components/Pages`** — `Home` (setup, the mix panel and Start), `Quiz`
  (the board, the answer and navigation row, the run's notices), `Stats`
  (the progress so far), `Done` (the summary and Restart), `Settings`,
  `Help`; `MixPanel`, `ScorePanel`, `ScoreBreakdown`. **`Components`** —
  `DecisionNotes` (the notes overlay, its placement and its size), `TailMenu`,
  `ProblemLocator`, `XgidLabel`, `FeedbackAddress`, `ClipboardCopy` (the
  one copy-and-confirm every copy control shares), `ReturnControl`,
  `ProblemFolderLabel`.
- **`wwwroot/js`** — `quizKeys.js` (Space), `actionRowFit.js` (the row's
  fit measurement), `decisionNotes.js`, `menuButton.js`.
- **Tests** — `BgQuiz_Blazor.Tests` (xUnit + bUnit: `QuizAppTests` is the
  coordinator's table, `PageTests` the pages' wiring, `QuizStatsTests` the
  recording and the probe, `MixDraftTests` / `MixPanelTests` the mix
  builder, `QuizRunTests` the run) and `BgQuiz_Blazor.E2eTests` (Playwright
  over a published build, the main path and the browser-only behaviour,
  the File System Access path over a fake directory handle).

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
  2026-10-16 and 2026-10-18, halheinrich/backgammon#421); a folder that landed
  but cannot be read as problems leaves no setup, and a run the landing
  finished stays finished.
- **What the load left out.** The parse is unfiltered and keeps every
  decision the walk yields. What the walk left out is its `SourceReport`'s,
  the one owner: files it could not read, games and plays it skipped
  (SPEC-notices §7.1), forced plays it passed by, dances included. Home lists
  the first two and counts the third (SPEC-notices §4). Start needs a load
  with a problem, read from the load rather than the count, so a zero beside
  decisions is the filter's (halheinrich/backgammon#514).
- **Resample.** The setup's own context drawn again at the limits, landing
  as a pick lands — the run ends, the files parse afresh, the stats file is
  re-probed — while the context, the filter source (so the filter in
  effect), the mix and the saved filters stay; a failed or empty draw keeps
  the sample. Offered while `QuizSetup.CanResample`, the producer's per-kind
  report (SPEC-filtering §4).
- **Filter.** XgFilter_Razor's `FilterSetup` owns the draft and what is
  applied, and its rules are that repo's (Pitfalls, The setup state). The app
  reports each setup's token to it and raises nothing from its snapshot
  stream, which moves on every keystroke. `FilterSetup`'s `Applied` event is
  the one entry for the count, which runs as the preparing operation, so an
  edit that empties an applied filter, which puts the empty filter in effect,
  is counted only at the next Clear or Apply.
- **The mix.** Visible means in effect (SPEC-filtering §5): the panel is on
  Home exactly while the Settings choice is on and the setup's probe — the
  pick's reading, then what the recording leaves in the file, bind and
  write by write — found a stats document with content, and the rows on it
  are what Start composes with. The draft is app-scoped and written through
  on every valid edit; a visible draft that does not build holds Start; a
  weighted Start whose bind finds no document is refused with a one-quiz
  "Start without mix" escape.
- **Start.** Bind the stats recording over the setup's folder, filter the
  problems under the ranking, deduplicate (`DistinctProblems`), compose the
  sequence with the mix in the order (`ProblemSequence`; BgGame_Lib's
  Pitfalls, Composing a run, hold both functions' rules), present the first
  problem. The prepared run and its recording replace whatever was in hand
  only on success, so a refused or faulted attempt leaves the run in hand
  recording where it was (halheinrich/backgammon#404). Restart composes
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

## Building, running and testing

The library references are sibling paths (`..\..\BgGame_Lib\…`), so this builds
only inside an umbrella checkout with its submodules.

- **Build:** `dotnet build BgQuiz_Blazor.slnx`.
- **Run:** `dotnet run --project BgQuiz_Blazor` serves http://localhost:5087
  as a Debug host (the `https` profile adds https://localhost:7089).
- **Judge speed on the Release publish**, never on that host:
  `dotnet publish BgQuiz_Blazor/BgQuiz_Blazor.csproj -c Release`, which is what
  the e2e suite and the deploy use, and which compiles the WASM ahead of time
  (`RunAOTCompilation` in the client's csproj, whose comment records the
  comparison). The deploy adds `-p:StampGitShaSuffix=false`; dev builds carry
  a `+g<sha>` suffix in the footer.
- **Unit and bUnit:** `dotnet test BgQuiz_Blazor.Tests`.
  `PlayIdentityRealFileTests` (trait `RequiresFixtureFiles`) reads a gitignored
  `TestData/FixtureFiles` file and throws without it, so it never passes
  vacuously.
- **End to end:** `dotnet test BgQuiz_Blazor.E2eTests` publishes and drives the
  app in Playwright's Chromium (install once with the `playwright.ps1` under
  the test build's output; a missing browser fails the suite, it never skips).
  `BGQUIZ_E2E_BASE_URL` points it at a running app instead of a publish of its
  own, for a fast loop; the full gate is the publishing run.
- **CI:** the umbrella's `bgquiz` workflow runs both, the unit suite with
  `--filter "Category!=RequiresFixtureFiles"` since `TestData` is not in a
  clone.

## Pitfalls

- **Every routable page is `.Client`-side with `prerender: false`**: the
  coordinator does not exist during a server prerender.
- **The pick caps are this app's numbers** (`PickedFileLimits`); enforcement is
  BgFolderAccess_Razor's ("Caps: host-owned numbers, library-owned
  enforcement"). The truncation notice reads the sample's kinds.
- **Filter before deduplicating.** Copies are not interchangeable to a
  filter; `Selection.ComputeAsync` does it in that order.
- **The count is keyed by setup, filter and ranking** (`Selection.Describes`);
  a resample changes the setup, Settings the ranking. Never show a selection
  that does not describe what is on screen. The count is filter-only: with a
  mix in effect it is the pool the mix draws from, and Home says so.
- **The by-error order reads the selection's error**
  (`Selection.RecordedError`, from the filter set it filtered with), so the
  order reads the half the filter judged, never a second construction of it.
- **Fixtures for an active mix need distinct decision ids.** `ProblemSequence`
  dedupes an active mix by id (BgGame_Lib's Pitfalls, Composing a run), so
  decisions that share the default id compose to one problem.
- **Every board starts from `Quiz.BuildRenderRequest`.** It carries the
  side and *Anonymize players* (`QuizSettings.RailNames`, display only: the
  Player names filter still matches the record's names); the solution's
  request and the play entry's working board extend it. A board built from
  `DiagramRequest.ForDecision` anywhere else shows the names again
  (halheinrich/backgammon#448).
- **Nothing expensive runs from a snapshot notification.** Pages observe
  snapshots for rendering only; the reason and the one entry for the count are
  under Filter, above.
- **The parse is the one long operation** and it yields on BgGame_Lib's
  `CooperativeYielder.DefaultBudget` so the progress line paints. Keep it
  inside the loading phase. The walk is synchronous, so the loader can yield
  and check cancellation only between the decisions it yields: a file's
  decode runs through without either. Measured on the desktop runtime over
  `TestData/xg` (2026-11-02, halheinrich/backgammon#514), that decode is the
  longest gap, about 50 ms, and the forced plays the walk passes by add
  microseconds.
- **A dismissal lives on the thing it is about** (SPEC-notices §2): the
  setup's notices on `QuizSetup`, the run's on `QuizStatsReport` and the
  live run, the storage refusal on BgUiPrimitives_Razor's
  `BrowserStorageCondition`.
- **Browser storage has shared owners**: common code has one shared owner, Hal
  ruled (2026-10-19, halheinrich/backgammon#424), and the rules are
  BgUiPrimitives_Razor's (Pitfalls, Browser storage). Here `AddFilterSurface()`
  is the one registration, and each store's call passes its consequence phrase,
  what the refusal costs it ("the mix panel starts blank for this visit").
- **`BrowserStoragePlan` is strict about every storage key a page reads**:
  the mix panel's mount reads `MixDraft.StorageKey`.
- **The shell runs `navFold.js` parser-blocking after `blazor.web.js`; keep it
  a plain classic script.** It needs `Blazor.addEventListener`, so it comes
  after, and Blazor starts at `DOMContentLoaded`, which a parser-blocking script
  holds and an `async` one does not, so no page renders before the panel's
  owner (`window.bgquizNavFold`) is there or has failed (measured 2026-10-03,
  halheinrich/backgammon#8, the record in `PanelOwnerAbsence`). The action
  row's fit relies on that: an absent owner never means one still to come.
  It means the script failed, and the row stays pending for the page's life.
  It is not the phone layout, where the owner is present and answers null
  (`MissingPanelOwnerTests`).
- **The not-found re-execute takes missing assets too, on purpose.**
  `UseStatusCodePagesWithReExecute` catches every bodyless 4xx and 5xx, so a
  missing `_framework` asset comes back 404 with the NotFound page's body. The
  status is what its consumers key on (`NotFoundPipelineTests`), and reordering
  cannot exempt it: a missing static file's 404 comes from routing, downstream.
  Revisit if server-side JSON endpoints arrive, where a typed client would
  throw on the HTML body; the discriminator then is content negotiation on
  `Accept`, never a path or extension sniff.
- **The app never localises**: `InvariantGlobalization` is on in the client
  so equities and XGIDs format period-decimal under every browser locale.
- **Bootstrap is vendored and tracked**
  (`wwwroot/lib/bootstrap/dist/css/bootstrap.min.css`, the one file `.gitignore`
  re-includes under `lib/`): a fresh clone builds styled with no network, and
  nothing here restores `lib/`, so a further vendored file needs its own
  re-include.
