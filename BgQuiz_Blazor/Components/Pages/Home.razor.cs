using BgQuiz_Blazor.Quiz;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Components.Pages;

/// <summary>
/// Phase 1 landing page: problem-set directory selection, filter selection,
/// and the quiz-start gate.
///
/// <para>
/// Hosts a directory text input above the shared <c>FilterPanel</c> from
/// XgFilter_Razor. The directory is held in the per-circuit
/// <see cref="ProblemSetSelection"/>, seeded from the configured
/// <see cref="QuizOptions.ProblemSetDirectory"/> default and persisted to /
/// rehydrated from <c>localStorage</c> so a user's choice survives a reload
/// even though the active quiz does not.
/// </para>
///
/// <para>
/// Start is gated on three conditions: filters Applied at least once, a
/// non-blank directory, and that directory existing on the server. The
/// existence check is a filesystem call, so it runs once per edit (and once
/// after rehydration) and is cached in <see cref="_directoryExists"/> —
/// <see cref="CanStart"/> is evaluated every render and must not do I/O.
/// </para>
///
/// <para>
/// On Start, hands the captured <see cref="FilterConfig"/> to the scoped
/// <see cref="QuizController"/> and navigates to <c>/quiz</c>; the
/// <see cref="ServerDiskProblemSetSourceFactory"/> reads the selected
/// directory at that point. Start-time exceptions (directory removed between
/// validation and Start, <see cref="FilterConfig.Build"/> failure, etc.) are
/// caught and surfaced as a banner rather than crashing the circuit.
/// </para>
/// </summary>
public partial class Home : ComponentBase
{
    private const string DirectoryStorageKey = "bgquiz_problemsetdirectory";

    private FilterConfig? _filterConfig;
    private bool _filtersApplied;
    private bool _directoryExists;
    private string? _startError;

    private bool CanStart =>
        _filtersApplied
        && !string.IsNullOrWhiteSpace(Selection.Directory)
        && _directoryExists;

    /// <summary>
    /// Rehydrate the directory from localStorage on first render — a stored
    /// value overrides the appsettings seed — then validate. localStorage
    /// access requires an established JS runtime, so it cannot run before the
    /// first render completes.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        var stored = await JS.InvokeAsync<string?>("localStorage.getItem", DirectoryStorageKey);
        if (!string.IsNullOrWhiteSpace(stored))
            Selection.Directory = stored;

        RevalidateDirectory();
        StateHasChanged();
    }

    private async Task HandleDirectoryChangedAsync(ChangeEventArgs e)
    {
        Selection.Directory = e.Value?.ToString()?.Trim() ?? string.Empty;
        RevalidateDirectory();
        await JS.InvokeVoidAsync("localStorage.setItem", DirectoryStorageKey, Selection.Directory);
    }

    /// <summary>
    /// Refresh the cached directory-exists flag. Called on edit and after
    /// rehydration only — never per render, since the existence check is a
    /// filesystem call.
    /// </summary>
    private void RevalidateDirectory() =>
        _directoryExists =
            !string.IsNullOrWhiteSpace(Selection.Directory)
            && Directory.Exists(Selection.Directory);

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
            // Selected directory removed since validation, FilterConfig.Build()
            // validation failure, etc. Surface to the user rather than crashing
            // the circuit.
            _startError = ex.Message;
        }
    }
}
