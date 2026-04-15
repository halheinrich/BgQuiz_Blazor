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

- **BgDiag_Razor** — `BackgammonDiagram` component. Parameters
  `DiagramRequest? Request` and `DiagramOptions Options`; event callbacks
  `OnPointClicked(int)`, `OnBarClicked(int)`, `OnCubeClicked`,
  `OnTrayClicked`. Referenced as a project reference, not a package.
- **BackgammonDiagram_Lib** (transitive via `BgDiag_Razor`) —
  `DiagramRequest` + inner `Builder`, `DiagramOptions`. Used directly in
  `Home.razor.cs` to construct the diagram's request.

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
  Components/
    _Imports.razor
    App.razor
    Routes.razor
    Layout/
      MainLayout.razor
      MainLayout.razor.css
      NavMenu.razor
      NavMenu.razor.css
      ReconnectModal.razor
      ReconnectModal.razor.css
      ReconnectModal.razor.js
    Pages/
      Home.razor
      Home.razor.cs
      Error.razor
      NotFound.razor
  wwwroot/
```

## Architecture

### Host setup

`Program.cs` is the stock Blazor Web App template: `AddRazorComponents()
.AddInteractiveServerComponents()` on the service side,
`MapRazorComponents<App>().AddInteractiveServerRenderMode()` on the
endpoint side, plus HTTPS redirection, antiforgery, and static files.
No custom services yet.

### Home page — Milestone 1 click-test harness

`Home.razor` is a single page at route `/` with `@rendermode
InteractiveServer`. It hosts a `BackgammonDiagram` inside a
`.board-container` div, a "Flip Orientation" button, and a click-report
`<p>` bound to `_clickMessage`.

Code-behind (`Home.razor.cs`) is a `ComponentBase` partial that:

- Holds `_request` (`DiagramRequest`) and `_options` (`DiagramOptions`) as
  fields. `_request` is built via `DiagramRequest.Builder` with
  `HomeBoardOnRight = true` and `Dice = [1, 1]`. `_options` is `new()`.
- Tracks orientation via a `_onRollBearsOffRight` bool.
  `ToggleOrientation()` flips it and **rebuilds** `_request` through a
  fresh `Builder` — `DiagramRequest` is immutable, so the field is
  replaced, not mutated.
- Exposes four async click handlers (`HandlePointClicked`,
  `HandleBarClicked`, `HandleCubeClicked`, `HandleTrayClicked`) that
  update `_clickMessage` and return `Task.CompletedTask`. They are wired
  to the corresponding `BackgammonDiagram` event callbacks in markup.

The `Dice = [1, 1]` value is a placeholder; the real opening position and
dice will come from a future `CreateOpeningPosition()` helper.

### Responsive board sizing

`.board-container` uses CSS `min()` in `wwwroot/app.css` to cap the
diagram at the viewport's smaller dimension, so the board scales with
the window instead of overflowing. `BackgammonDiagram` itself has no
intrinsic size; the container is what makes it visible.

### Layout chrome

`MainLayout.razor` is the default Blazor template minus the "About"
link that ships in the stock `NavMenu`. `ReconnectModal` is the default
Interactive Server reconnect UI.

## Public API

This is an application, not a library — no exported types or HTTP
endpoints. The only externally visible surface is the set of Razor
routes:

- `/` → `Home` — Milestone 1 click-test page described above.
- Default error page → `Error.razor`.
- Default 404 page → `NotFound.razor`.

## Pitfalls

- **`DiagramRequest` is immutable.** Orientation toggles and any other
  "state change" on the request must go through `DiagramRequest.Builder`
  and replace the field. Do not try to mutate an existing request
  in-place — the lib does not expose setters and will not recompute
  derived state.
- **Render mode is Interactive Server.** Component state lives on the
  server; every client needs an active SignalR circuit. Migrating to
  WebAssembly or Auto later (for Azure global hosting) will change how
  quiz state is modelled and where dependencies execute — treat the
  render mode as a design decision, not an incidental default.
- **Board has no intrinsic size.** `BackgammonDiagram` must live inside
  a sized container; without `.board-container`'s `min()` rule the SVG
  has zero layout size and the page looks empty.
- **`Dice = [1, 1]` is a placeholder, not a real opening roll.** Every
  build path currently passes it. When `CreateOpeningPosition()` lands,
  both the initial `_request` and the `ToggleOrientation()` rebuild
  must be updated together — they are the only two construction sites.
- **Orientation flip affects the diagram but not hit regions.** The
  rendered board flips when `HomeBoardOnRight` changes, but the click
  overlay stays in the default orientation. The fix belongs in
  `BgDiag_Razor`, not here; don't try to work around it from the app.

## Subproject-internal next steps

- **Implement `CreateOpeningPosition()`.** Replace the two
  `Builder { HomeBoardOnRight, Dice = [1,1] }` construction sites in
  `Home.razor.cs` with a helper that returns a realistic opening
  position (15-checker starting layout, real rolled dice). Keep the
  immutable-rebuild pattern for the orientation toggle.
- **Quiz flow scaffolding.** Once the diagram bug in `BgDiag_Razor` is
  resolved and `CreateOpeningPosition()` is in place, introduce quiz
  selection, scoring, and history beyond the current click-test
  harness. Likely wants real services in `Program.cs` and a state
  model that survives a render-mode change.
