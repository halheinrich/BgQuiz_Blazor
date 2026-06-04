// *** CLIENT PROJECT — BgQuiz_Blazor.Client (WASM) ***
//
// Hosts the interactive quiz surface. Everything the quiz needs runs in the
// browser-wasm runtime: the quiz state machine (QuizController), the active
// problem-set source, board rendering, and in-browser .xg/.xgp parsing.

using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Per-app quiz state. In the WASM client "scoped" resolves to one instance per
// loaded app (one tab), so state survives in-app navigation and resets only on a
// full reload — see QuizController's lifetime docs. Pages observe via StateChanged.
builder.Services.AddScoped<QuizController>();

// TEMPORARY source factory: the built-in sample set, enough to prove the
// migrated flow. Replaced by the browser file-picker source
// (WasmUploadedProblemSetSource) in the next step; the QuizController ctor and
// the ProblemSetSourceFactory delegate shape are unchanged across that swap.
builder.Services.AddScoped<ProblemSetSourceFactory>(_ =>
    filters => new SampleProblemSetSource(filters));

await builder.Build().RunAsync();
