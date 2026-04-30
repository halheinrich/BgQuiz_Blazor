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
/// <c>DecisionFilterSet</c> to the scoped <see cref="QuizController"/>
/// (which appends Phase 1's CheckerPlaysOnly cube policy) and navigates to
/// <c>/quiz</c>.
/// </para>
/// </summary>
public partial class Home : ComponentBase
{
    private DecisionFilterSet? _filterSet;
    private bool _filtersApplied;
    private string? _startError;

    private bool CanStart =>
        _filtersApplied
        && !string.IsNullOrWhiteSpace(QuizOpts.Value.ProblemSetDirectory);

    private void HandleFiltersApplied(DecisionFilterSet set)
    {
        _filterSet = set;
        _filtersApplied = true;
        _startError = null;
    }

    private void HandleFiltersDirty()
    {
        _filtersApplied = false;
    }

    private async Task StartQuizAsync()
    {
        if (_filterSet is null) return;
        try
        {
            await Controller.StartAsync(_filterSet);
            Nav.NavigateTo("/quiz");
        }
        catch (Exception ex)
        {
            // Configured directory missing, etc. Surface to the user rather
            // than crashing the circuit.
            _startError = ex.Message;
        }
    }
}
