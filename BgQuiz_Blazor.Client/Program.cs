// BgQuiz_Blazor.Client (WASM): the quiz runs in the browser. One coordinator,
// QuizApp, owns the app's state; the stores below hold the user's settings and the
// mix draft; the Razor libraries supply browser storage and its one refusal
// condition, the folder seam and the filter surface.

using BgFolderAccess_Razor;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using XgFilter_Razor;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton(TimeProvider.System);

// The folder seam: the pick limits are this app's policy, the library ships none.
builder.Services.AddSingleton(new FolderPickLimits(PickedFileLimits.MaxFileCounts, PickedFileLimits.MaxFileBytes));
builder.Services.AddScoped<IFolderAccess, JsFolderAccess>();

// The filter surface, the shared storage condition as its refusal sink, and the reporting
// storage access the stores below use, so every refusal is the one occurrence Home's notice
// reads (halheinrich/backgammon#424). Saved filters live in the picked folder, through the
// setup's document storage over its folder context. "Scoped" is one instance per loaded app
// in WebAssembly.
builder.Services.AddFilterSurface();
builder.Services.AddScoped<QuizSettings>();
builder.Services.AddScoped<MixDraft>();
builder.Services.AddScoped<NotesPreferencesStore>();
builder.Services.AddScoped<IQuizLiveMarker, QuizLiveMarker>();

builder.Services.AddScoped<ProblemLoad>(sp =>
    (files, progress, cancellationToken) => ProblemLoader.LoadAsync(
        files, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>(), progress, cancellationToken));
builder.Services.AddScoped<QuizApp>();

await builder.Build().RunAsync();
