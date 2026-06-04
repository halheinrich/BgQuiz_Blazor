using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// Landing page: filter selection and the quiz-start gate.
///
/// <para>
/// Hosts the shared <c>FilterPanel</c> from XgFilter_Razor. Start is gated on the
/// filters having been Applied at least once; on Start the captured
/// <see cref="FilterConfig"/> is handed to the <see cref="QuizController"/> and
/// the app navigates to <c>/quiz</c>. <see cref="FilterConfig.Build"/> /
/// source-construction failures are caught and surfaced as a banner rather than
/// faulting the WebAssembly app.
/// </para>
///
/// <para>
/// Source note: this step runs against a built-in sample
/// <see cref="BgGame_Lib.IProblemSetSource"/> wired in the client's
/// <c>Program.cs</c>. The user-file picker (browser-parsed
/// <c>.xg</c>/<c>.xgp</c>) replaces that source in the next step; the page's
/// start flow is unaffected by the swap.
/// </para>
/// </summary>
public partial class Home : ComponentBase
{
    private FilterConfig? _filterConfig;
    private bool _filtersApplied;
    private string? _startError;

    private bool CanStart => _filtersApplied;

    private void HandleFilterConfigApplied(FilterConfig cfg)
    {
        _filterConfig = cfg;
        _filtersApplied = true;
        _startError = null;
    }

    private void HandleFiltersDirty()
    {
        _filtersApplied = false;
    }

    private async Task StartQuizAsync()
    {
        if (_filterConfig is null) return;
        try
        {
            await Controller.StartAsync(_filterConfig);
            Nav.NavigateTo("/quiz");
        }
        catch (Exception ex)
        {
            // FilterConfig.Build() validation failure, source construction
            // failure, etc. Surface to the user rather than faulting the app.
            _startError = ex.Message;
        }
    }
}
