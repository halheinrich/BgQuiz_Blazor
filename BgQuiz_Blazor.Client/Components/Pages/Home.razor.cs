using BgFolderAccess_Razor;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// Setup: pick a folder, filter it, see what matches, start. Everything shown here is the
/// coordinator's; this page holds only its own notices and the fallback input.
/// </summary>
public partial class Home : ComponentBase, IDisposable
{
    [Inject] private QuizApp App { get; set; } = default!;
    [Inject] private FilterSetup FilterSetup { get; set; } = default!;
    [Inject] private QuizSettings Settings { get; set; } = default!;
    [Inject] private PickedFolderDocumentStorage DocumentStorage { get; set; } = default!;
    [Inject] private IFolderAccess FolderAccess { get; set; } = default!;
    [Inject] private BrowserStorageCondition StorageCondition { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private ElementReference _fallbackInput;
    private bool _fsAccessAvailable;
    private IDisposable? _filterAttachment;

    // Per-visit notices: the page's whole state.
    private string? _pickError;
    private bool _emptyFolderNotice;
    private bool _cancelledPickNotice;
    private string? _startError;
    private string? _noMatchNotice;
    private object? _storageNoticeDismissedFor;
    private QuizSetup? _truncationNoticeDismissedFor;
    private FinishedQuiz? _endedNoticeDismissedFor;

    private QuizSetup? Setup => App.Setup;

    /// <summary>
    /// The page locks while the app works (loading, preparing), not while the browser's
    /// prompts are up: the picking phase is the user's turn, and a disabled fieldset would
    /// disable the hidden input the fallback picker opens through.
    /// </summary>
    private bool IsBusy => App.Phase is QuizPhase.Loading or QuizPhase.Preparing;
    private FilterConfig? FilterInEffect => App.FilterInEffect;
    private Selection? CurrentSelection => App.CurrentSelection;

    private bool IsCounting =>
        App.Phase == QuizPhase.Preparing && Setup is not null && FilterInEffect is not null && CurrentSelection is null;

    private bool CanStart =>
        Setup is { } setup
        && FilterInEffect is not null
        && !setup.Loaded.Report.AllRejected
        && CurrentSelection is not { AnswerTypes.Total: 0 };

    /// <summary>Saved filters live in the picked folder when the browser can read it.</summary>
    private IDocumentStorage? Storage =>
        Setup?.Capability is FolderWriteCapability.Enabled or FolderWriteCapability.PermissionDenied
            ? DocumentStorage
            : null;

    private string? SavedFiltersDisabledReason =>
        Setup?.Capability == FolderWriteCapability.PermissionDenied
            ? $"{FolderPickDisplay.WriteAccessNotGranted} — saved filters can be loaded but not changed or deleted."
            : null;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        App.Changed += OnAppChanged;
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
    }

    private async Task StartQuizAsync()
    {
        _startError = null;
        _noMatchNotice = null;
        try
        {
            switch (await App.StartAsync())
            {
                case StartOutcome.Started:
                    Nav.NavigateTo("/quiz");
                    break;
                case StartOutcome.NoProblems:
                    _noMatchNotice = "No quiz problems could be presented — adjust the filters or pick different files.";
                    break;
                case StartOutcome.Busy:
                case StartOutcome.NoSetup:
                case StartOutcome.NoFilterInEffect:
                case StartOutcome.NothingToRestart:
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
