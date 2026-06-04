using BgQuiz_Blazor.Components;
using BgQuiz_Blazor.Quiz;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddOptions<QuizOptions>()
    .Bind(builder.Configuration.GetSection("Quiz"));

// Per-circuit problem-set selection. Seeded at construction from the configured
// Quiz:ProblemSetDirectory default; Home.razor overrides it from the user's
// localStorage-persisted choice and writes back on every edit.
builder.Services.AddScoped<ProblemSetSelection>(sp => new ProblemSetSelection
{
    Directory = sp.GetRequiredService<IOptions<QuizOptions>>().Value.ProblemSetDirectory,
});

// Phase 1 source: a server-disk directory drawn from the per-circuit
// ProblemSetSelection. Phase 2+ alternatives (upload, deployed bundles, curated
// libraries) plug in by registering a different factory; the controller is
// unchanged. The no-directory-selected throw fires at invocation time
// (StartAsync), not at controller resolution — so pages that merely observe
// state (Done, etc.) load even with no selection, and Home.razor gates Start.
builder.Services.AddScoped<ServerDiskProblemSetSourceFactory>();
builder.Services.AddScoped<ProblemSetSourceFactory>(sp =>
    sp.GetRequiredService<ServerDiskProblemSetSourceFactory>().Create);

// Per-circuit quiz state. Pages observe via QuizController.StateChanged.
builder.Services.AddScoped<QuizController>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseStaticFiles();
// Serves the WASM client's fingerprinted static web assets (the _framework boot
// files); also backs the @Assets[...] lookups in App.razor.
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(BgQuiz_Blazor.Client._Imports).Assembly);

app.Run();
