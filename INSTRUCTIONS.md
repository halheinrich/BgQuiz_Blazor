# BgQuiz_Blazor — Instructions

## Purpose
Blazor web application for backgammon quizzes and play.
Consumes the `BackgammonDiagram` component from `BgDiag_Razor` to render
interactive board diagrams. Handles quiz selection, quiz flow, scoring,
and performance tracking.

## Repo
- GitHub: `halheinrich/BgQuiz_Blazor`
- Umbrella submodule path: `BgQuiz_Blazor`
- Local path: `D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\BgQuiz_Blazor`

## Stack
- C# / .NET 10
- Blazor Web App, Interactive Server render mode
- Visual Studio 2026
- Future migration path: WebAssembly or Auto for Azure global hosting

## Dependencies
- **BgDiag_Razor** — Razor Class Library providing `BackgammonDiagram` component
  - Transitively brings in **BackgammonDiagram_Lib**
  - Component API: `DiagramRequest? Request`, `DiagramOptions Options`
  - EventCallbacks: `OnPointClicked(int)`, `OnBarClicked`, `OnCubeClicked`, `OnTrayClicked`

## Umbrella
- Repo: `halheinrich/backgammon`
- AGENTS.md lives at umbrella root — this project references it, does not keep its own copy.

## Project structure
```
BgQuiz_Blazor/
├── Components/
│   ├── App.razor
│   ├── Routes.razor
│   ├── Layout/
│   │   └── MainLayout.razor
│   └── Pages/
│       ├── Home.razor
│       └── Home.razor.cs
├── wwwroot/
│   └── app.css
├── Properties/
│   └── launchSettings.json
├── Program.cs
├── BgQuiz_Blazor.csproj
├── appsettings.json
├── appsettings.Development.json
└── INSTRUCTIONS.md
```

## Current state
- Milestone 1 complete: renders a `BackgammonDiagram` with a hardcoded empty
  position, reports click events (point, bar, cube, tray) as text below the board.
- Board is responsive — scales to fit viewport using `min()` in CSS.
- "About" link removed from `MainLayout.razor`.
- Orientation toggle implemented via `_onRollBearsOffRight` / `ToggleOrientation()`.
  Board SVG flips correctly. Hit regions do not flip — bug in `BgDiag_Razor`
  (`GetHitRegions` not receiving `Request`). Handoff written for `BgDiag_Razor`.
- `DiagramOrientation` enum replaced by `HomeBoardOnRight` bool on `DiagramRequest`
  as of `BackgammonDiagram_Lib` commit `01432a7`.
- `CreateOpeningPosition()` removed — `DiagramRequest` constructed with defaults only.

## Known issues
- Hit regions don't reflect `HomeBoardOnRight` after toggle. `GetHitRegions(Options)`
  in `BackgammonDiagram.razor.cs` does not receive `Request`, so point-to-rectangle
  mapping is always computed for the default orientation. See `BgDiag_Razor` handoff.

## Session start
1. Fetch AGENTS.md from umbrella root.
2. Fetch this file (INSTRUCTIONS.md).
3. Fetch key source files as needed.

### Source file URLs
```
https://raw.githack.com/halheinrich/BgQuiz_Blazor/2d9b415/BgQuiz_Blazor/Components/Pages/Home.razor
https://raw.githack.com/halheinrich/BgQuiz_Blazor/2d9b415/BgQuiz_Blazor/Components/Pages/Home.razor.cs
https://raw.githack.com/halheinrich/BgQuiz_Blazor/2d9b415/BgQuiz_Blazor/Program.cs
```

## Dependency files

### BackgammonDiagram_Lib
Files needed from this dependency (fetch URLs from umbrella INSTRUCTIONS.md):
* Models/DiagramRequest.cs
* Models/DiagramOptions.cs
* Models/Enums.cs
* Models/BoardHitRegions.cs
* Rendering/DiagramRenderer.cs

### BgDiag_Razor
Files needed from this dependency (fetch URLs from umbrella INSTRUCTIONS.md):
* Components/BackgammonDiagram.razor
* Components/BackgammonDiagram.razor.cs

## Commit log
| Date | Hash | Summary |
|------|------|---------|
| 2026-03-30 | `b8ef2f2` | Milestone 1 stub: diagram + click reporting |
| 2026-03-30 | `2b747f3` | Milestone 1: responsive board sizing, remove About link, orientation toggle stub |
| 2026-03-31 | `4ce6ded` | Fix: update DiagramRequest to use HomeBoardOnRight, remove DiagramOrientation |
| 2026-03-31 | `2d9b415` | Fix CS1729: replace DiagramRequest direct construction with Builder |
