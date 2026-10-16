// BgQuiz_Blazor.Client (WASM): the quiz runs in the browser. One coordinator,
// QuizApp, owns the app's state; the stores below hold the user's settings, the
// mix draft and the one storage-refusal flag; the two Razor libraries supply the
// folder seam and the filter surface.

using BgFolderAccess_Razor;
using BgQuiz_Blazor.Client.Quiz;
using BgUiPrimitives_Razor;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using XgFilter_Razor;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton(TimeProvider.System);

// The folder seam: the pick limits are this app's policy, the library ships none.
builder.Services.AddSingleton(new FolderPickLimits(PickedFileLimits.MaxFileCounts, PickedFileLimits.MaxFileBytes));
builder.Services.AddScoped<IFolderAccess, JsFolderAccess>();

// Browser storage, the one flag that it refused a call this visit, and the
// stores that use it. "Scoped" is one instance per loaded app in WebAssembly.
builder.Services.AddBrowserStorage();
builder.Services.AddScoped<BrowserStorageCondition>();
builder.Services.AddScoped<QuizSettings>();
builder.Services.AddScoped<MixDraft>();
builder.Services.AddScoped<NotesPlacementStore>();

// The shared filter surface, with this app's refusal sink; saved filters live in
// the picked folder, through the setup's document storage over its folder context.
builder.Services.AddScoped<FilterStorageRefusalSink>();
builder.Services.AddFilterSurface<FilterStorageRefusalSink>();

builder.Services.AddScoped<ProblemLoad>(sp =>
    (files, progress, cancellationToken) => ProblemLoader.LoadAsync(
        files, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>(), progress, cancellationToken));
builder.Services.AddScoped<QuizApp>();

await builder.Build().RunAsync();
