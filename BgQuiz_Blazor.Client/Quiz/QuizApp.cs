namespace BgQuiz_Blazor.Client.Quiz;

using BgDataTypes_Lib;
using BgFolderAccess_Razor;
using BgGame_Lib;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;

/// <summary>The one operation the app can have in flight, or none.</summary>
internal enum QuizPhase
{
    Idle,

    /// <summary>The browser's folder prompts are up; the user's turn.</summary>
    Picking,

    /// <summary>A folder's files are being read and parsed; the screen is locked and shows progress.</summary>
    Loading,

    /// <summary>A count or a Start is composing over the loaded problems.</summary>
    Preparing,
}

internal enum StartOutcome
{
    Started,
    Busy,
    NoSetup,
    NoFilterInEffect,
    NothingToRestart,

    /// <summary>The selection composed to nothing; the run or summary in hand is kept.</summary>
    NoProblems,
}

/// <summary>
/// The app's one coordinator: it owns the phase (the one operation in flight), the setup
/// (the folder in hand and its problems), the live run or the finished result, and the
/// selection (the count for the filter in effect). Pages render it and send commands; a
/// command that does not fit the phase is refused before any await, so nothing overlaps.
/// The one long operation is the parse, in <see cref="QuizPhase.Loading"/>; a count or a
/// Start prepares in <see cref="QuizPhase.Preparing"/>, yielding to the browser as it goes;
/// the run itself navigates a fixed sequence and is synchronous.
/// </summary>
internal sealed class QuizApp : IDisposable
{
    private readonly IFolderAccess _folders;
    private readonly FilterSetup _filterSetup;
    private readonly QuizSettings _settings;
    private readonly ProblemLoad _load;
    private readonly ILogger<QuizApp> _logger;
    private readonly TimeProvider _clock;

    private int _setups;
    private Selection? _selection;
    private LiveQuiz? _live;
    private bool _recountPending;

    public QuizApp(
        IFolderAccess folders, FilterSetup filterSetup, QuizSettings settings, ProblemLoad load,
        ILogger<QuizApp> logger, TimeProvider clock)
    {
        _folders = folders ?? throw new ArgumentNullException(nameof(folders));
        _filterSetup = filterSetup ?? throw new ArgumentNullException(nameof(filterSetup));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _load = load ?? throw new ArgumentNullException(nameof(load));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _filterSetup.InEffectChanged += OnFilterInEffectChanged;
    }

    // ── State pages render ───────────────────────────────────────────────────────

    public QuizPhase Phase { get; private set; }
    public bool IsBusy => Phase != QuizPhase.Idle;

    /// <summary>How far the load has got, while <see cref="Phase"/> is <see cref="QuizPhase.Loading"/>.</summary>
    public LoadProgress? Progress { get; private set; }

    public QuizSetup? Setup { get; private set; }
    public QuizRun? Run => _live?.Run;
    public FinishedQuiz? Finished { get; private set; }

    /// <summary>The folder name the run or the finished result was composed under.</summary>
    public string? Name => _live?.Name ?? Finished?.Name;

    public bool HasStarted => _live is not null || Finished is not null;
    public bool IsFinished => Finished is not null;

    // The run's facts, read through here so pages hold no run of their own.
    public BgDecisionData? Current => Run?.Cursor?.Problem;
    public ProblemReview? Review => Run?.Review;
    public bool RandomHomeBoardOnRight => Run?.Cursor?.RandomHomeBoardOnRight ?? false;
    public QuizScore Score => Run?.Score ?? Finished?.Result.Score ?? QuizScore.Empty;
    public int SkippedCount => Run?.SkippedCount ?? Finished?.Result.SkippedCount ?? 0;
    public int PresentedCount => Run?.Presented.Length ?? Finished?.Result.PresentedCount ?? 0;
    public int ProblemNumber => Run?.Cursor?.StreamSlot ?? 0;
    public int? ProblemCount => Run?.ProblemCount ?? Finished?.Result.ProblemCount;
    public bool CanGoBack => Run?.CanGoBack ?? false;
    public bool CanGoToLast => Run?.CanGoToLast ?? false;
    public bool NextAddsToSkipCount => Run?.NextAddsToSkipCount ?? false;

    public PlayRanking Ranking => Run?.Ranking ?? Finished?.Result.Ranking
        ?? throw new InvalidOperationException("No quiz has been started, so there is no quiz ranking.");

    /// <summary>The filter in effect for the setup in hand, or null when there is none or nothing is applied.</summary>
    public FilterConfig? FilterInEffect =>
        Setup is { } setup ? _filterSetup.Current.ConfigInEffectFor(setup.FilterSource) : null;

    /// <summary>The count for the setup, the filter in effect and the current ranking, or null while unknown.</summary>
    public Selection? CurrentSelection =>
        Setup is { } setup && FilterInEffect is { } config && _selection is { } selection
        && selection.Describes(setup, config, _settings.Ranking)
            ? selection
            : null;

    /// <summary>Raised whenever anything here moves, on the renderer's context.</summary>
    public event Action? Changed;

    // ── Folder ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// The "choose a folder" gesture. The mechanism is probed at the click: with the
    /// browser's directory picker, the prompts are the user's turn and a folder the browser
    /// accepts replaces the setup, whatever it holds, while cancelling keeps the setup in
    /// hand; without it, the hidden <c>webkitdirectory</c> input's picker opens and the
    /// selection arrives later through <see cref="CollectFallbackAsync"/>.
    /// </summary>
    public async Task<FolderOutcome> PickFolderAsync(ElementReference fallbackInput)
    {
        if (!TryBegin(QuizPhase.Picking)) return FolderOutcome.Refused;
        try
        {
            FolderPickOutcome picked;
            try
            {
                if (!await _folders.SupportsDirectoryPickerAsync())
                {
                    await _folders.TriggerFallbackPickerAsync(fallbackInput);
                    return FolderOutcome.AwaitingSelection;
                }

                picked = await _folders.PickFolderAsync(onPickAccepted: () =>
                {
                    Enter(QuizPhase.Loading);
                    return Task.CompletedTask;
                });
            }
            catch (Exception failure) when (IsAcquisitionFailure(failure))
            {
                await DropSetupAsync();
                return FolderOutcome.Failed(failure);
            }

            if (picked.Cancelled) return FolderOutcome.Cancelled;
            return await LandAsync(picked);
        }
        finally
        {
            End();
        }
    }

    /// <summary>Take the fallback input's selection as the new setup.</summary>
    public async Task<FolderOutcome> CollectFallbackAsync(ElementReference fallbackInput)
    {
        if (!TryBegin(QuizPhase.Loading)) return FolderOutcome.Refused;
        try
        {
            FolderPickOutcome collected;
            try
            {
                collected = await _folders.CollectFallbackAsync(fallbackInput);
            }
            catch (Exception failure) when (IsAcquisitionFailure(failure))
            {
                await DropSetupAsync();
                return FolderOutcome.Failed(failure);
            }

            return await LandAsync(collected);
        }
        finally
        {
            End();
        }
    }

    /// <summary>Let the folder go. The run in hand, live or finished, stays.</summary>
    public async Task<bool> ClearFolderAsync()
    {
        if (!TryBegin(QuizPhase.Loading)) return false;
        try
        {
            await DropSetupAsync();
            return true;
        }
        finally
        {
            End();
        }
    }

    private async Task<FolderOutcome> LandAsync(FolderPickOutcome picked)
    {
        Enter(QuizPhase.Loading);
        if (picked.Sample.Files.IsEmpty)
        {
            await DropSetupAsync();
            return FolderOutcome.NoFiles;
        }

        LoadedProblems loaded;
        try
        {
            loaded = await _load(
                picked.Sample.Files,
                progress =>
                {
                    Progress = progress;
                    Changed?.Invoke();
                },
                CancellationToken.None);
        }
        catch (Exception failure)
        {
            // The folder arrived but could not be read as problems. The browser's slot
            // moved to it, so no folder is in hand; the run in progress, not yet ended,
            // goes on.
            await DropSetupAsync();
            return FolderOutcome.Failed(failure);
        }

        // A new sample ends the run in progress (ruled 2026-10-16, halheinrich/backgammon#421).
        EndRun(endedByNewSample: true);
        var setup = new QuizSetup(
            picked.DirectoryName, picked.Capability, picked.Sample, loaded,
            FilterSourceToken.FromGeneration(++_setups));
        Setup = setup;
        _selection = null;
        _filterSetup.ReportSource(setup.FilterSource);
        Changed?.Invoke();
        await RecountCoreAsync();
        return FolderOutcome.Landed;
    }

    /// <summary>No folder is in hand any more: the module's slot is released, the filter owner told.</summary>
    private async Task DropSetupAsync()
    {
        Setup = null;
        _selection = null;
        _filterSetup.ReportSource(null);
        Changed?.Invoke();
        try
        {
            await _folders.ClearPickedAsync();
        }
        catch (Exception releaseFault)
        {
            _logger.LogWarning(releaseFault, "Releasing the folder failed; nothing is held either way.");
        }
    }

    /// <summary>
    /// Whether <paramref name="failure"/>, raised by the folder seam while a sample was
    /// being acquired, is the folder failing to arrive. Every exception is, except the
    /// seam's caller error, <see cref="InvalidOperationException"/>, which is this app's bug.
    /// </summary>
    private static bool IsAcquisitionFailure(Exception failure) => failure is not InvalidOperationException;

    // ── Selection ────────────────────────────────────────────────────────────────

    /// <summary>Compute the count for the filter in effect if none describes it; a page asks on mount.</summary>
    public Task EnsureSelectionAsync() => IsBusy ? Task.CompletedTask : RecountAsync();

    /// <summary>What is in effect moved (an Apply, a Clear, a restoration settling): count it, now or when the operation in flight ends.</summary>
    private void OnFilterInEffectChanged(FilterSetupSnapshot snapshot)
    {
        if (Setup is not { } setup || !snapshot.IsInEffectFor(setup.FilterSource)) return;
        if (IsBusy)
            _recountPending = true;   // picked up when the operation in flight ends
        else
            _ = RecountAsync();
    }

    private async Task RecountAsync()
    {
        if (!TryBegin(QuizPhase.Preparing)) return;
        try
        {
            await RecountCoreAsync();
        }
        catch (Exception failure)
        {
            _logger.LogWarning(failure, "The match count failed; it is shown as unknown.");
        }
        finally
        {
            End();
        }
    }

    private async Task RecountCoreAsync()
    {
        await _settings.EnsureHydratedAsync();
        if (Setup is not { } setup || FilterInEffect is not { } config) return;
        var ranking = _settings.Ranking;
        if (_selection?.Describes(setup, config, ranking) == true) return;
        _selection = await Selection.ComputeAsync(
            setup, config, ranking, new CooperativeYielder(_clock), CancellationToken.None);
        Changed?.Invoke();
    }

    // ── Starting a run ───────────────────────────────────────────────────────────

    /// <summary>Compose a new run from the setup under the filter in effect and the current settings.</summary>
    public Task<StartOutcome> StartAsync()
    {
        if (IsBusy) return Task.FromResult(StartOutcome.Busy);
        if (Setup is not { } setup) return Task.FromResult(StartOutcome.NoSetup);
        if (FilterInEffect is not { } config) return Task.FromResult(StartOutcome.NoFilterInEffect);
        return StartCoreAsync(setup, config);
    }

    /// <summary>Compose a new run from the setup under the finished run's filter and the current settings.</summary>
    public Task<StartOutcome> RestartAsync()
    {
        if (IsBusy) return Task.FromResult(StartOutcome.Busy);
        if (Finished is not { } finished) return Task.FromResult(StartOutcome.NothingToRestart);
        if (Setup is not { } setup) return Task.FromResult(StartOutcome.NoSetup);
        return StartCoreAsync(setup, finished.Filters);
    }

    private async Task<StartOutcome> StartCoreAsync(QuizSetup setup, FilterConfig config)
    {
        if (!TryBegin(QuizPhase.Preparing)) return StartOutcome.Busy;
        try
        {
            await _settings.EnsureHydratedAsync();
            var ranking = _settings.Ranking;
            var yielder = new CooperativeYielder(_clock);
            var selection = _selection?.Describes(setup, config, ranking) == true
                ? _selection
                : await Selection.ComputeAsync(setup, config, ranking, yielder, CancellationToken.None);
            if (config.Equals(FilterInEffect)) _selection = selection;

            var sequence = await ProblemSequence.BuildAsync(
                selection.Pool, QuizMix.Empty, _settings.Order, ranking,
                stats: null, _clock.GetUtcNow(), new Random(), yielder, CancellationToken.None);
            if (sequence.Problems.Count == 0) return StartOutcome.NoProblems;

            // The prepared run replaces whatever was in hand only now, on success.
            var run = QuizRun.Begin(ranking, sequence.Problems).PresentNext(RandomSide());
            _live = new LiveQuiz(run, config, setup.Name);
            Finished = null;
            return StartOutcome.Started;
        }
        finally
        {
            End();
        }
    }

    // ── The run ──────────────────────────────────────────────────────────────────

    public Task SubmitPlayAsync(Play play)
    {
        if (!IsBusy && _live is { Run.IsAnswering: true } live)
        {
            live.Run = live.Run.SubmitPlay(play, out _);
            Changed?.Invoke();
        }

        return Task.CompletedTask;
    }

    public Task SubmitCubeAnswerAsync(CubeAnswer answer)
    {
        if (!IsBusy && _live is { Run.IsAnswering: true } live)
        {
            live.Run = live.Run.SubmitCubeAnswer(answer, out _);
            Changed?.Invoke();
        }

        return Task.CompletedTask;
    }

    /// <summary>Move on; at the frontier, present the next problem or finish the run.</summary>
    public void Next()
    {
        if (IsBusy || _live is not { Run.Cursor: not null } live) return;
        var moved = live.Run.Next(out var bringsNewProblem);
        if (bringsNewProblem)
        {
            var next = moved.PresentNext(RandomSide());
            if (next.IsEnded)
            {
                Finish(next, endedByNewSample: false);
                Changed?.Invoke();
                return;
            }

            moved = next;
        }

        live.Run = moved;
        Changed?.Invoke();
    }

    public void GoToFirst()
    {
        if (IsBusy || _live is not { Run.CanGoBack: true } live) return;
        live.Run = live.Run.GoToFirst();
        Changed?.Invoke();
    }

    public void GoBack()
    {
        if (IsBusy || _live is not { Run.CanGoBack: true } live) return;
        live.Run = live.Run.GoBack();
        Changed?.Invoke();
    }

    public void GoToLast()
    {
        if (IsBusy || _live is not { Run.CanGoToLast: true } live) return;
        live.Run = live.Run.GoToLast();
        Changed?.Invoke();
    }

    public void EndQuiz()
    {
        if (IsBusy || _live is null) return;
        EndRun(endedByNewSample: false);
        Changed?.Invoke();
    }

    private void EndRun(bool endedByNewSample)
    {
        if (_live is { } live) Finish(live.Run.End(), endedByNewSample);
    }

    private void Finish(QuizRun ended, bool endedByNewSample)
    {
        var live = _live ?? throw new InvalidOperationException("Only a live run finishes.");
        Finished = new FinishedQuiz(ended.ToResult(), live.Filters, live.Name, endedByNewSample);
        _live = null;
    }

    private bool RandomSide() => _settings.RandomizeSidePerProblem && Random.Shared.Next(2) == 0;

    // ── The one operation in flight ──────────────────────────────────────────────

    private bool TryBegin(QuizPhase phase)
    {
        if (IsBusy) return false;
        Enter(phase);
        return true;
    }

    private void Enter(QuizPhase phase)
    {
        Phase = phase;
        Changed?.Invoke();
    }

    private void End()
    {
        Phase = QuizPhase.Idle;
        Progress = null;
        Changed?.Invoke();
        if (_recountPending)
        {
            _recountPending = false;
            _ = RecountAsync();
        }
    }

    public void Dispose() => _filterSetup.InEffectChanged -= OnFilterInEffectChanged;

    /// <summary>A run in progress with what a Restart needs beside it.</summary>
    private sealed class LiveQuiz(QuizRun run, FilterConfig filters, string name)
    {
        public QuizRun Run { get; set; } = run;
        public FilterConfig Filters { get; } = filters;
        public string Name { get; } = name;
    }
}
