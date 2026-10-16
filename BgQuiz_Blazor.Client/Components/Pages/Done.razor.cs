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
        _restartError = null;
        _restartNotice = null;
        _mixRefused = false;
        try
        {
            switch (await App.RestartAsync(withoutMix))
            {
                case StartOutcome.Started:
                    Nav.NavigateTo("/quiz");
                    break;
                case StartOutcome.NoProblems:
                    _restartNotice = "No quiz problems could be presented from the folder in hand — the summary above stands.";
                    break;
                case StartOutcome.NoSetup:
                    _restartNotice = "No folder is in hand; pick one on the setup page to restart.";
                    break;
                case StartOutcome.MixRequiresStats:
                    _mixRefused = true;
                    break;
                case StartOutcome.Busy:
                case StartOutcome.NoFilterInEffect:
                case StartOutcome.NothingToRestart:
                case StartOutcome.MixInvalid:
                    break;
            }
        }
        catch (Exception ex)
        {
            // A faulted Restart keeps the finished run, so the summary stands and Restart can be pressed again.
            _restartError = ex.Message;
        }
    }

    private void BackToSetup() => Nav.NavigateTo("/");
}
