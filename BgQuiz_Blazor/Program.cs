using BgQuiz_Blazor.Components;

var builder = WebApplication.CreateBuilder(args);

// Thin WASM host: the interactive quiz surface (pages, QuizController, problem
// sources, scoring) lives entirely in the BgQuiz_Blazor.Client project and runs
// in the browser. This project only serves the host shell + the client's static
// web assets, so it registers the WebAssembly render mode alone — there are no
// server-interactive components.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

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
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(BgQuiz_Blazor.Client._Imports).Assembly);

app.Run();
