using BgQuiz_Blazor.Components;
using BgQuiz_Blazor.Quiz;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOptions<QuizOptions>()
    .Bind(builder.Configuration.GetSection("Quiz"));

// Phase 1 source: server-disk directory configured via Quiz:ProblemSetDirectory.
// Phase 2+ alternatives (upload, deployed bundles, curated libraries) plug in
// by registering a different factory; the controller is unchanged. The empty-
// config throw fires at invocation time (StartAsync), not at controller resolution
// — so pages that merely observe state (Done, etc.) load even with bad config and
// Home.razor's friendly empty-config banner still gates Start.
builder.Services.AddSingleton<ProblemSetSourceFactory>(sp => filters =>
{
    var dir = sp.GetRequiredService<IOptions<QuizOptions>>().Value.ProblemSetDirectory;
    if (string.IsNullOrWhiteSpace(dir))
        throw new InvalidOperationException(
            "Quiz:ProblemSetDirectory is not configured.");
    return new ServerDiskProblemSetSource(dir, filters);
});

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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
