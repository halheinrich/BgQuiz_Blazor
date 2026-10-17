using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>The finished run's summary and stats notices, with Restart and the way back to setup.</summary>
public partial class Done : ComponentBase, IDisposable
{
    [Inject] private QuizApp App { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private string? _restartError;
    private string? _restartNotice;
    private bool _mixRefused;

    /// <summary>Whether the finished run was weighted while a restart of it would not be: the one state to disclose before the click.</summary>
    private bool RestartWillDropTheMix => App.Finished is { Mix.IsPassthrough: false } && !App.MixInEffect;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        if (!App.HasStarted)
        {
            Nav.NavigateTo("/", replace: true);
            return;
        }

        App.Changed += OnAppChanged;
    }

    /// <inheritdoc />
    public void Dispose() => App.Changed -= OnAppChanged;

    private void OnAppChanged() => _ = InvokeAsync(StateHasChanged);

    private Task RestartAsync() => RestartCoreAsync(withoutMix: false);

    /// <summary>The refusal notice's escape: one quiz unweighted, the stored mix untouched.</summary>
    private Task RestartWithoutMixAsync() => RestartCoreAsync(withoutMix: true);

    private async Task RestartCoreAsync(bool withoutMix)
    {
        ClearNotices();
        try
        {
            ReportRestart(await App.RestartAsync(withoutMix), overANewSample: false);
        }
        catch (Exception ex)
        {
            // A faulted Restart keeps the finished run, so the summary stands and Restart can be pressed again.
            _restartError = ex.Message;
        }
    }

    /// <summary>
    /// Done's resample-and-restart (halheinrich/backgammon#417): one operation, and the summary
    /// stands whatever it comes to. A draw that fails or lands nothing is said as the setup page
    /// says it; once a sample has landed it is held, and what the restart came to is said with that.
    /// </summary>
    private async Task RestartWithNewSampleAsync()
    {
        ClearNotices();
        try
        {
            var outcome = await App.ResampleAndRestartAsync();
            switch (outcome.Draw.Kind)
            {
                case FolderOutcomeKind.Failed:
                    _restartError = $"Could not read the folder: {outcome.Draw.Failure?.Message}";
                    break;
                case FolderOutcomeKind.NoFiles:
                    _restartNotice = "Resample found no .xg / .xgp files directly in that folder, so the files "
                        + "already read are still the ones in use — the summary above stands.";
                    break;
                case FolderOutcomeKind.Landed:
                    ReportRestart(outcome.Restart!.Value, overANewSample: true);
                    break;
                case FolderOutcomeKind.Refused:
                case FolderOutcomeKind.Cancelled:
                case FolderOutcomeKind.AwaitingSelection:
                    break;
            }
        }
        catch (Exception ex)
        {
            // Past the landing, a fault is the restart's: the new sample is held and the summary stands.
            _restartError = $"{ex.Message}{HeldSuffix}";
        }
    }

    private const string HeldSuffix = " The new sample is held, so Restart with same filters now runs over it.";

    private void ReportRestart(StartOutcome outcome, bool overANewSample)
    {
        var held = overANewSample ? HeldSuffix : "";
        switch (outcome)
        {
            case StartOutcome.Started:
                Nav.NavigateTo("/quiz");
                break;
            case StartOutcome.NoProblems:
                _restartNotice = $"No quiz problems could be presented from the folder in hand — the summary above stands.{held}";
                break;
            case StartOutcome.NoSetup:
                _restartNotice = "No folder is in hand; pick one on the setup page to restart.";
                break;
            case StartOutcome.MixRequiresStats:
                _mixRefused = true;   // its override restarts unweighted over the sample held, the new one included
                break;
            case StartOutcome.Busy:
            case StartOutcome.NoFilterInEffect:
            case StartOutcome.NothingToRestart:
            case StartOutcome.MixInvalid:
                break;
        }
    }

    private void ClearNotices()
    {
        _restartError = null;
        _restartNotice = null;
        _mixRefused = false;
    }

    private void BackToSetup() => Nav.NavigateTo("/");
}
