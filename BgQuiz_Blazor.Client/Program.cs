// *** CLIENT PROJECT — BgQuiz_Blazor.Client (WASM) ***
//
// Hosts the interactive quiz surface. Everything the quiz needs runs in the
// browser-wasm runtime: the quiz state machine (QuizController), the active
// problem-set source, board rendering, and in-browser .xg/.xgp parsing.

using BgGame_Lib;
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

// Per-app holder for the "Shuffle order" toggle — a presentation-only choice,
// deliberately separate from AppliedFilter/FilterConfig. Scoped for the same
// navigate-back-survival reason as the other start-gate holders.
builder.Services.AddScoped<ShuffleOption>();

// Per-app marker (sessionStorage-backed) recording that a quiz is live in this
// tab, so a full reload — which reboots the runtime and discards quiz state —
// can be acknowledged on the next boot instead of dumping the user on a blank
// Home. Scoped like the holders; Home sets/reads it, Done clears it on
// completion. See QuizLiveMarker for why the store is sessionStorage.
builder.Services.AddScoped<QuizLiveMarker>();

// Source factory: builds a WasmUploadedProblemSetSource over whatever files the
// user has picked at quiz-start, applying the user's filter set, then wraps it
// in a ShuffledProblemSetSource when the user asked to shuffle. Both the picked
// set and the shuffle toggle are read at invocation time
// (QuizController.StartAsync), not registration, so choices made before Start
// take effect. The unseeded ShuffledProblemSetSource ctor is used here
// deliberately — reproducibility is a test-only concern (see
// ShuffledProblemSetSource's seeded ctor), never user-facing.
builder.Services.AddScoped<ProblemSetSourceFactory>(sp =>
{
    var picked = sp.GetRequiredService<PickedProblemSet>();
    var shuffle = sp.GetRequiredService<ShuffleOption>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return filters =>
    {
        IProblemSetSource inner = new WasmUploadedProblemSetSource(picked.Files, filters, loggerFactory);
        return shuffle.Enabled ? new ShuffledProblemSetSource(inner) : inner;
    };
});

await builder.Build().RunAsync();
