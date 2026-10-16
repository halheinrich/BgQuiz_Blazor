using BackgammonDiagram_Lib;
using BgDataTypes_Lib;
using BgDiag_Razor.Components;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// The quiz page: the board, the answer controls and the navigation row over the
/// coordinator's live run. Two JS modules serve it: the Space key and the action row's
/// fit measurement.
/// </summary>
public partial class Quiz : ComponentBase, IAsyncDisposable
{
    internal const string KeysModulePath = "./js/quizKeys.js";
    internal const string RowFitModulePath = "./js/actionRowFit.js";

    private BackgammonPlayEntry? _playEntry;
    private Play? _completedPlay;
    private CubeAnswer? _completedCube;
    private IJSObjectReference? _keys;
    private IJSObjectReference? _rowFit;
    private DotNetObjectReference<Quiz>? _self;
    private ElementReference _actionRow;
    private ElementReference _actionRowRuler;
    private string? _observedRow;
    private (bool TailFits, bool FullCubeLabelsFit)? _rowFitReport;
    private bool _disposed;

    private static readonly DiagramOptions FullCanvas = new();
    private static readonly DiagramOptions BoardOnlyCanvas = new() { Aspect = AspectPreset.BoardOnly };

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await Settings.EnsureHydratedAsync();
        App.Changed += HandleStateChanged;
        if (!App.HasStarted)
        {
            Nav.NavigateTo("/", replace: true);
            return;
        }

        if (App.IsFinished)
            Nav.NavigateTo("/done", replace: true);
    }

    private void HandleStateChanged()
    {
        _completedPlay = null;
        _completedCube = null;
        if (App.IsFinished)
        {
            Nav.NavigateTo("/done");
            return;
        }

        _ = InvokeAsync(StateHasChanged);
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await ImportModulesAsync();
            if (RowFitReady) StateHasChanged();
            return;
        }

        await ObserveActionRowAsync();
    }

    private bool RowFitReady => _rowFit is not null;

    private async Task ImportModulesAsync()
    {
        var keys = await JS.InvokeAsync<IJSObjectReference>("import", KeysModulePath);
        var rowFit = await JS.InvokeAsync<IJSObjectReference>("import", RowFitModulePath);
        if (_disposed)
        {
            await keys.DisposeAsync();
            await rowFit.DisposeAsync();
            return;
        }

        _keys = keys;
        _rowFit = rowFit;
        _self = DotNetObjectReference.Create(this);
        await _keys.InvokeVoidAsync("attach", _self, nameof(HandleSpaceKeyAsync), QuizKeysMark.AttachedAttribute);
    }

    private async Task ObserveActionRowAsync()
    {
        if (_rowFit is null || _self is null || App.Current is null) return;
        if (_actionRow.Id == _observedRow) return;
        _observedRow = _actionRow.Id;
        await _rowFit.InvokeVoidAsync("observe", _actionRow, _actionRowRuler, _self, nameof(HandleRowFit));
    }

    /// <summary>The row-fit module's measurement: whether the tail and the full cube labels fit.</summary>
    [JSInvokable]
    public void HandleRowFit(bool tailFits, bool fullCubeLabelsFit)
    {
        var before = (RowFitPending, ShortCubeLabels, TailFolded);
        _rowFitReport = (tailFits, fullCubeLabelsFit);
        if ((RowFitPending, ShortCubeLabels, TailFolded) != before) StateHasChanged();
    }

    private bool RowFitPending => _rowFitReport is null;
    private bool TailFolded => _rowFitReport is not { TailFits: true };
    private bool ShortCubeLabels => _rowFitReport is not { FullCubeLabelsFit: true };

    /// <summary>The ruler's coordinate: three digits, the widest the locator ever needs.</summary>
    private const int RulerCoordinate = 999;

    /// <summary>The Space key: Submit when a submission is ready, else Next.</summary>
    [JSInvokable]
    public async Task HandleSpaceKeyAsync()
    {
        var pressing =
            CanSubmit ? SubmitAsync()
            : CanGoNext ? NextAsync()
            : Task.CompletedTask;
        StateHasChanged();
        await pressing;
        StateHasChanged();
    }

    private bool CanSubmit => App.Review is null && !App.IsBusy && (_completedCube is not null || _completedPlay is not null);
    private bool CanContinue => App.Review is not null && !App.IsBusy;
    private bool CanGoBack => App.CanGoBack && !App.IsBusy;
    private bool CanGoNext => App.Current is not null && !App.IsBusy;
    private bool CanGoToLast => App.CanGoToLast && !App.IsBusy;
    private bool CanEndQuiz => !App.IsBusy;

    internal const string NavigationGroupName = "Problems";
    internal const string GoToFirstName = "Go to first";
    internal const string BackName = "Back";
    internal const string NextName = "Next";
    internal const string SkipName = "Skip";
    internal const string GoToLastName = "Go to last";
    internal const string ShowStatsName = "Show stats";
    internal const string EndQuizName = "End quiz";

    /// <summary>Show stats and End quiz as the tail's "⋯" list offers them, each built from its button's name, handler and gate.</summary>
    private IReadOnlyList<TailMenuAction> TailMenuActions =>
    [
        new(ShowStatsName, EventCallback.Factory.Create(this, ShowStats), Disabled: false),
        new(EndQuizName, EventCallback.Factory.Create(this, EndQuizAsync), Disabled: !CanEndQuiz),
    ];

    /// <summary>
    /// Whether a length-bound composition fell short of what was asked: overall, or any
    /// entry's pool ran dry and its share was redistributed. Capless, an outdrawn entry is
    /// apportionment of the union rather than a user's ask, so the page shows the
    /// composition line instead.
    /// </summary>
    private static bool HasShortfall(MixComposition comp) =>
        comp.DrawnCount < comp.TargetCount || comp.Entries.Any(e => e.Drawn < e.Requested);

    private void ShowStats() => Nav.NavigateTo("/stats");

    private string NextButtonName => App.NextAddsToSkipCount ? SkipName : NextName;

    private bool HomeBoardOnRight => Settings.EffectiveHomeBoardOnRight(App.RandomHomeBoardOnRight);

    private bool MaximizedAnswering => Settings.MaximizeBoardWhileAnswering && App.Review is null;

    private DiagramOptions BoardOptions => MaximizedAnswering ? BoardOnlyCanvas : FullCanvas;

    private DiagramRequest BuildRenderRequest(BgDecisionData current) =>
        DiagramRequest.ForDecision(current, App.Ranking) with { HomeBoardOnRight = HomeBoardOnRight };

    private DiagramRequest BuildSolutionRequest(BgDecisionData current, ProblemReview review)
    {
        var request = BuildRenderRequest(current) with
        {
            Mode = DiagramMode.Solution,
            MaximumHiddenCandidateAnalysisLevel = Settings.MaximumHiddenCandidateAnalysisLevel,
        };
        return review is ProblemReview.Play play
            ? request with { SecondaryPlayIndex = play.CandidateIndex }
            : request;
    }

    internal const string PracticePrefix = "Practice — ";

    private static string VerdictText(ProblemReview review) =>
        review.IsPractice ? PracticePrefix + ScoredVerdict(review) : ScoredVerdict(review);

    private static string ScoredVerdict(ProblemReview review) => review switch
    {
        ProblemReview.Play play => PlayVerdict(play),
        ProblemReview.Cube cube => CubeVerdict(cube),
        _ => string.Empty,
    };

    internal const string NotScoredVerdict =
        "Not scored under depth-first ranking: this play was analyzed less deeply than the best play, and at that depth it rated higher.";

    private static string PlayVerdict(ProblemReview.Play review) => review.Submission.Match(
        scored: submitted => submitted.IsCorrect
            ? "Correct."
            : $"Not best — your play lost {EquityDisplay.FormatLoss(submitted.EquityLoss)} equity. The best play is shown above.",
        notScored: _ => NotScoredVerdict,
        offList: () =>
            $"Off list — your play, {review.UserPlay.ToNotation()}, wasn't among the analyzed candidates. The best play is shown above.");

    private const string BestListSeparator = ", ";

    private static string CubeVerdict(ProblemReview.Cube review)
    {
        var submission = review.Submission;
        var decision = review.Decision;
        var label = CubeLabels.Label(submission.Answer, decision);
        if (submission.IsCorrect)
            return $"Correct — {label}.";
        var best = string.Join(
            BestListSeparator,
            decision.ZeroCostAnswers.Select(answer => CubeLabels.Label(answer, decision)));
        return $"Not best — {label} lost {EquityDisplay.FormatLoss(submission.Cost.Total)}. Best: {best}.";
    }

    private static string? SolutionLegend(ProblemReview review, BgDecisionData decision)
    {
        if (review is not ProblemReview.Play play || decision is not CheckerPlayDecision checkerPlay)
            return null;
        var recorded = checkerPlay.Decision.UserPlayIndex;
        var parts = new List<string>(2);
        if (recorded is not null)
            parts.Add("* played");
        if (play.CandidateIndex is int answer && answer != recorded)
            parts.Add("† your answer");
        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }

    private static string StatusText(ProblemReview? review, BgDecisionData decision) =>
        review is not null
            ? VerdictText(review)
            : decision.Match(
                checkerPlay: _ => "Click the board to build your play, then Submit.",
                cube: _ => "Pick the cube decision, then Submit.");

    private static string StatusVerdictColor(ProblemReview? review) => review switch
    {
        null => "alert-secondary",
        ProblemReview.Play play => play.Submission.Match(
            scored: submitted => submitted.IsCorrect ? "alert-success" : "alert-danger",
            notScored: _ => "alert-warning",
            offList: () => "alert-warning"),
        ProblemReview.Cube cube => cube.Submission.IsCorrect ? "alert-success" : "alert-danger",
        _ => "alert-secondary",
    };

    private void HandlePlayCompleted(Play play)
    {
        _completedPlay = play;
        StateHasChanged();
    }

    private Task SubmitAsync() =>
        _completedCube is { } cube ? App.SubmitCubeAnswerAsync(cube)
        : _completedPlay is { } play ? App.SubmitPlayAsync(play)
        : Task.CompletedTask;

    private Task NextAsync()
    {
        App.Next();
        return Task.CompletedTask;
    }

    private void GoToFirst() => App.GoToFirst();
    private void GoBack() => App.GoBack();
    private void GoToLast() => App.GoToLast();

    private Task EndQuizAsync()
    {
        App.EndQuiz();
        return Task.CompletedTask;
    }

    private void UndoLast()
    {
        _playEntry?.UndoLast();
        _completedPlay = null;
    }

    private void UndoAll()
    {
        _playEntry?.UndoAll();
        _completedPlay = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        App.Changed -= HandleStateChanged;
        if (_keys is not null)
        {
            await _keys.InvokeVoidAsync("detach");
            await _keys.DisposeAsync();
            _keys = null;
        }

        if (_rowFit is not null)
        {
            await _rowFit.InvokeVoidAsync("unobserve");
            await _rowFit.DisposeAsync();
            _rowFit = null;
        }

        _self?.Dispose();
        _self = null;
    }
}
