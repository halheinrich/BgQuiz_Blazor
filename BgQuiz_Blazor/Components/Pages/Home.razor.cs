using BgQuiz_Blazor.Quiz;
using Microsoft.AspNetCore.Components;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Components.Pages;

/// <summary>
/// Phase 1 landing page: filter selection and quiz-start gate.
///
/// <para>
/// Hosts the shared <c>FilterPanel</c> from XgFilter_Razor and a Start Quiz
/// button gated on (a) Apply having been clicked at least once and (b) a
/// configured <c>Quiz:ProblemSetDirectory</c>. On Start, hands the captured
/// <see cref="FilterConfig"/> (the wire DTO emitted by FilterPanel) to the
/// scoped <see cref="QuizController"/>, which materializes it into a
/// controller-owned <see cref="DecisionFilterSet"/> and appends Phase 1's
/// CheckerPlaysOnly cube policy. Then navigates to <c>/quiz</c>.
/// </para>
/// </summary>
public partial class Home : ComponentBase
{
    private FilterConfig? _filterConfig;
    private bool _filtersApplied;
    private string? _startError;

    private bool CanStart =>
        _filtersApplied
        && !string.IsNullOrWhiteSpace(QuizOpts.Value.ProblemSetDirectory);

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
            // Configured directory missing, FilterConfig.Build() validation
            // failure, etc. Surface to the user rather than crashing the circuit.
            _startError = ex.Message;
        }
    }
}
