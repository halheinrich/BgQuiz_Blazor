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
    [Inject] private IQuizLiveMarker Marker { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private ElementReference _fallbackInput;
    private bool _fsAccessAvailable;
    private IDisposable? _filterAttachment;

    // Per-visit notices: the page's whole state. A dismissal of a notice about something
    // that outlives the page (the storage refusal, a setup's notices, a run ended by a new
    // sample) is kept on that thing, so it holds across navigation.
    private string? _pickError;
    private bool _pickErrorFromResample;
    private bool _emptyFolderNotice;
    private bool _emptyResampleNotice;
    private bool _cancelledPickNotice;
    private bool _showReloadNotice;
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

    /// <summary>
    /// A notice about another folder's pick stands — the empty-pick line or the failed-pick
    /// error — while the folder in hand is unchanged. The kept folder's own notices step aside
    /// while it does, since read beside it they describe the folder just picked, which they do
    /// not (Hal, 2026-10-17; halheinrich/backgammon#424).
    /// </summary>
    private bool AnotherPicksNoticeStands => _emptyFolderNotice || FailedPickNoticeStands;

    /// <summary>
    /// The error notice stands for a pick, not a resample: a failed draw is about the folder in
    /// hand, so it names no folder kept and that folder's notices stand, as beside an empty draw.
    /// </summary>
    private bool FailedPickNoticeStands => !string.IsNullOrEmpty(_pickError) && !_pickErrorFromResample;
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
        // A boot that finds the live mark with no run at all is a reload mid-quiz: say so once
        // and clear the mark. With a run in hand the mark is in-app navigation back here.
        if (!App.HasStarted && await Marker.WasLiveAsync())
        {
            _showReloadNotice = true;
            await Marker.ClearAsync();
        }

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

    /// <summary>Resample (SPEC-filtering.md §4): the setup's folder drawn again, the sample replaced, the setup kept; an empty draw replaces nothing and says so.</summary>
    private async Task ResampleAsync()
    {
        ClearPickNotices();
        Report(await App.ResampleAsync(), resample: true);
    }

    private void Report(FolderOutcome outcome, bool resample = false)
    {
        switch (outcome.Kind)
        {
            case FolderOutcomeKind.Failed:
                _pickError = outcome.Failure?.Message;
                _pickErrorFromResample = resample;
                break;
            case FolderOutcomeKind.Cancelled:
                _cancelledPickNotice = true;
                break;
            case FolderOutcomeKind.NoFiles when resample:
                _emptyResampleNotice = true;
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
        _pickErrorFromResample = false;
        _emptyFolderNotice = false;
        _emptyResampleNotice = false;
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
