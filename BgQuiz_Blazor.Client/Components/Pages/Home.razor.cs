using BgFolderAccess_Razor;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// Setup: pick a folder, filter it, see what matches, compose the mix where it is in effect,
/// start. Everything shown here is the coordinator's or the draft's; this page holds only its
/// own notices and the fallback input.
/// </summary>
public partial class Home : ComponentBase, IDisposable
{
    [Inject] private QuizApp App { get; set; } = default!;
    [Inject] private FilterSetup FilterSetup { get; set; } = default!;
    [Inject] private QuizSettings Settings { get; set; } = default!;
    [Inject] private MixDraft Draft { get; set; } = default!;
    [Inject] private IFolderAccess FolderAccess { get; set; } = default!;
    [Inject] private BrowserStorageCondition StorageCondition { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private ElementReference _fallbackInput;
    private bool _fsAccessAvailable;
    private IDisposable? _filterAttachment;

    // Per-visit notices: the page's whole state. A dismissal of a notice about something
    // that outlives the page (the storage refusal, a setup's notices, a run ended by a new
    // sample) is kept on that thing, so it holds across navigation.
    private string? _pickError;
    private bool _emptyFolderNotice;
    private bool _cancelledPickNotice;
    private string? _startError;
    private string? _noMatchNotice;
    private bool _mixRefused;

    private QuizSetup? Setup => App.Setup;

    /// <summary>
    /// The page locks while the app works (loading, preparing, recording), not while the
    /// browser's prompts are up: the picking phase is the user's turn, and a disabled
    /// fieldset would disable the hidden input the fallback picker opens through.
    /// </summary>
    private bool IsBusy => App.Phase is QuizPhase.Loading or QuizPhase.Preparing or QuizPhase.Recording;
    private FilterConfig? FilterInEffect => App.FilterInEffect;
    private Selection? CurrentSelection => App.CurrentSelection;

    private bool IsCounting =>
        App.Phase == QuizPhase.Preparing && Setup is not null && FilterInEffect is not null && CurrentSelection is null;

    private bool CanStart =>
        Setup is { } setup
        && FilterInEffect is not null
        && !setup.Loaded.Report.AllRejected
        && CurrentSelection is not { AnswerTypes.Total: 0 }
        && App.MixForStart is not null;

    /// <summary>Whether the mix in effect has rows, so the count is the pool it draws from rather than the quiz.</summary>
    private bool MixDrawsFromTheMatches => App.MixInEffect && Draft.Build() is { IsPassthrough: false };

    private string? SavedFiltersDisabledReason =>
        Setup?.Capability == FolderWriteCapability.PermissionDenied
            ? $"{FolderPickDisplay.WriteAccessNotGranted} — saved filters can be loaded but not changed or deleted."
            : null;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        App.Changed += OnAppChanged;
        Draft.Changed += OnAppChanged;
        StorageCondition.Began += OnAppChanged;
        _filterAttachment = FilterSetup.Attach(_ => OnAppChanged());
        await Settings.EnsureHydratedAsync();
        _fsAccessAvailable = await FolderAccess.SupportsDirectoryPickerAsync();
        await App.EnsureSelectionAsync();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        App.Changed -= OnAppChanged;
        Draft.Changed -= OnAppChanged;
        StorageCondition.Began -= OnAppChanged;
        _filterAttachment?.Dispose();
    }

    private void OnAppChanged() => _ = InvokeAsync(StateHasChanged);

    private async Task PickFolderAsync()
    {
        ClearPickNotices();
        Report(await App.PickFolderAsync(_fallbackInput));
    }

    private async Task HandleFallbackPickedAsync(ChangeEventArgs _) =>
        Report(await App.CollectFallbackAsync(_fallbackInput));

    private void HandleFallbackCancelled() => _cancelledPickNotice = true;

    private async Task ClearSetupAsync()
    {
        ClearPickNotices();
        await App.ClearFolderAsync();
    }

    private void Report(FolderOutcome outcome)
    {
        switch (outcome.Kind)
        {
            case FolderOutcomeKind.Failed:
                _pickError = outcome.Failure?.Message;
                break;
            case FolderOutcomeKind.Cancelled:
                _cancelledPickNotice = true;
                break;
            case FolderOutcomeKind.NoFiles:
                _emptyFolderNotice = true;
                break;
            case FolderOutcomeKind.Landed:
            case FolderOutcomeKind.Refused:
            case FolderOutcomeKind.AwaitingSelection:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome.Kind, null);
        }
    }

    private void ClearPickNotices()
    {
        _pickError = null;
        _emptyFolderNotice = false;
        _cancelledPickNotice = false;
        _startError = null;
        _noMatchNotice = null;
        _mixRefused = false;
    }

    private Task StartQuizAsync() => StartCoreAsync(withoutMix: false);

    /// <summary>The refusal notice's escape: this one quiz unweighted, the mix rows untouched.</summary>
    private Task StartWithoutMixAsync() => StartCoreAsync(withoutMix: true);

    private async Task StartCoreAsync(bool withoutMix)
    {
        _startError = null;
        _noMatchNotice = null;
        _mixRefused = false;
        var weighted = !withoutMix && MixDrawsFromTheMatches;
        try
        {
            switch (await App.StartAsync(withoutMix))
            {
                case StartOutcome.Started:
                    Nav.NavigateTo("/quiz");
                    break;
                case StartOutcome.NoProblems:
                    _noMatchNotice = weighted
                        ? "Your mix drew no problems — no decision in these files matched "
                          + "the selected categories against your lifetime stats. Adjust "
                          + "the mix, the filters, or the files."
                        : "No quiz problems could be presented — adjust the filters or pick different files.";
                    break;
                case StartOutcome.MixRequiresStats:
                    _mixRefused = true;
                    break;
                case StartOutcome.Busy:
                case StartOutcome.NoSetup:
                case StartOutcome.NoFilterInEffect:
                case StartOutcome.NothingToRestart:
                case StartOutcome.MixInvalid:
                    break;
            }
        }
        catch (Exception ex)
        {
            // The attempt faulted and kept whatever was in hand; say so rather than faulting the app.
            _startError = ex.Message;
        }
    }

    private void BackToQuiz() => Nav.NavigateTo("/quiz");
}
