using BgQuiz_Blazor.Components;
using BgQuiz_Blazor.Quiz;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOptions<QuizOptions>()
    .Bind(builder.Configuration.GetSection("Quiz"));

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
