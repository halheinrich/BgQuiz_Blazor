using BgQuiz_Blazor.Components;

var builder = WebApplication.CreateBuilder(args);

// Thin WASM host: the interactive quiz surface (pages, QuizController, problem
// sources, scoring) lives entirely in the BgQuiz_Blazor.Client project and runs
// in the browser. This project only serves the host shell + the client's static
// web assets, so it registers the WebAssembly render mode alone — there are no
// server-interactive components.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// The one dependency of the health endpoint mapped below (shared framework, no
// package). Deliberately no registered checks: what the probe asks is "is this
// instance up and serving HTTP", and a check reaching past the process would
// let some other system's outage mark this site unhealthy and take it out of
// rotation.
builder.Services.AddHealthChecks();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Serve the NotFound page for server-side unmatched paths. MapRazorComponents
// registers endpoints only for known routes, so an unmatched URL never reaches
// Blazor and would otherwise fall through to a bare, empty-bodied 404. The
// Router's NotFoundPage covers only the client-side case (in-app navigation
// after the runtime has booted). Re-executing preserves the 404 status code.
// Ordering: before UseAntiforgery, because the re-execute replays the pipeline
// from here downstream and the Razor Component endpoint requires the antiforgery
// middleware to have run.
app.UseStatusCodePagesWithReExecute("/not-found");

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseStaticFiles();
// Serves the WASM client's fingerprinted static web assets (the _framework boot
// files); also backs the @Assets[...] lookups in App.razor.
app.MapStaticAssets();

// The liveness endpoint Azure App Service pings once a minute per instance
// once the deploy sets healthCheckPath (halheinrich/backgammon#24). Anonymous,
// and no response contract beyond the default 200 "Healthy" — the probe reads
// the status code.
//
// Where it sits, and what that does and does not buy. Endpoint *matching*
// happens in the routing middleware WebApplication inserts ahead of everything
// above, so no position for this call could move the endpoint past
// UseStatusCodePagesWithReExecute or UseHttpsRedirection; ahead of the
// Razor-components registration is a statement of precedence to a reader, not
// a mechanism. What actually keeps those two off it, measured against the
// published artifact rather than read off the pipeline: the status-code pages
// engage only on a >= 400 response, and a mapped /healthz answers 200 —
// unmapped it would come back as the re-executed not-found page, which is how
// the pin in EnvironmentFidelityTests is mutation-checked; and
// UseHttpsRedirection resolves no HTTPS port when the app binds http only (the
// e2e fixture, and App Service, which terminates TLS at the front end), so the
// probe's plain-HTTP request passes through instead of taking a 307.
app.MapHealthChecks("/healthz");

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(BgQuiz_Blazor.Client._Imports).Assembly);

app.Run();
