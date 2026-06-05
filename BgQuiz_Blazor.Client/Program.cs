// *** CLIENT PROJECT — BgQuiz_Blazor.Client (WASM) ***
//
// Hosts the interactive quiz surface. Everything the quiz needs runs in the
// browser-wasm runtime: the quiz state machine (QuizController), the active
// problem-set source, board rendering, and in-browser .xg/.xgp parsing.

using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Per-app quiz state. In the WASM client "scoped" resolves to one instance per
// loaded app (one tab), so state survives in-app navigation and resets only on a
// full reload — see QuizController's lifetime docs. Pages observe via StateChanged.
builder.Services.AddScoped<QuizController>();

// Per-app holder for the user's browser-picked files; Home writes it, the source
// factory below reads it at quiz-start.
builder.Services.AddScoped<PickedProblemSet>();

// Per-app holder for the filter half of Home's start gate: the config the user
// deliberately applied. Scoped so the gate survives in-app navigation (Home is
// re-instantiated on navigate-back); read only by Home.
builder.Services.AddScoped<AppliedFilter>();

// Source factory: builds a WasmUploadedProblemSetSource over whatever files the
// user has picked at quiz-start, applying the user's filter set. The picked set
// is read at invocation time (QuizController.StartAsync), not registration, so a
// file choice made before Start takes effect. Files are parsed in-browser only.
builder.Services.AddScoped<ProblemSetSourceFactory>(sp =>
{
    var picked = sp.GetRequiredService<PickedProblemSet>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return filters => new WasmUploadedProblemSetSource(picked.Files, filters, loggerFactory);
});

await builder.Build().RunAsync();
