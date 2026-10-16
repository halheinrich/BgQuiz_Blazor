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

    /// <summary>An answer of record is being folded into the folder's stats file; the next answer waits for it.</summary>
    Recording,
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

    /// <summary>The mix on screen does not build; it is in effect, so Start waits for it to be fixed or turned off.</summary>
    MixInvalid,

    /// <summary>The mix needs the folder's stats and the bind found no document; the run in hand is kept.</summary>
    MixRequiresStats,
}

/// <summary>
/// The app's one coordinator: it owns the phase (the one operation in flight), the setup
/// (the folder in hand, its problems and its stats destination), the live run or the
/// finished result, and the selection (the count for the filter in effect). Pages render it
/// and send commands; a command that does not fit the phase is refused before any await, so
/// nothing overlaps. The one long operation is the parse, in <see cref="QuizPhase.Loading"/>;
/// a count or a Start prepares in <see cref="QuizPhase.Preparing"/>; each answer of record's
/// write is awaited in <see cref="QuizPhase.Recording"/>; the run itself navigates a fixed
/// sequence and is synchronous.
///
/// <para>
/// The folder context is captured with the setup at the pick and activated with the run at
/// Start or Restart, on success only, so a later pick cannot retarget a live quiz's recording
/// (halheinrich/backgammon#404). A live run keeps its folder across Clear and across a
/// failed replacement; a landing ends the run; a folder nothing holds is released.
/// </para>
/// </summary>
internal sealed class QuizApp : IDisposable
{
    private readonly IFolderAccess _folders;
    private readonly FilterSetup _filterSetup;
    private readonly QuizSettings _settings;
    private readonly MixDraft _mixDraft;
    private readonly ProblemLoad _load;
    private readonly ILogger<QuizApp> _logger;
    private readonly TimeProvider _clock;

    private int _setups;
    private Selection? _selection;
    private LiveQuiz? _live;
    private bool _recountPending;

    public QuizApp(
        IFolderAccess folders, FilterSetup filterSetup, QuizSettings settings, MixDraft mixDraft, ProblemLoad load,
        ILogger<QuizApp> logger, TimeProvider clock)
    {
        _folders = folders ?? throw new ArgumentNullException(nameof(folders));
        _filterSetup = filterSetup ?? throw new ArgumentNullException(nameof(filterSetup));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _mixDraft = mixDraft ?? throw new ArgumentNullException(nameof(mixDraft));
        _load = load ?? throw new ArgumentNullException(nameof(load));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _filterSetup.Applied += OnFilterApplied;
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

    /// <summary>The stats report of the run in hand, live or finished, or null before any run.</summary>
    public QuizStatsReport? Stats => _live?.Stats.Report ?? Finished?.Stats;

    /// <summary>How the mix composed the live run, or null for a blank mix or no live run.</summary>
    public MixComposition? Composition => _live?.Composition;

    /// <summary>Whether the live run's composition notice was dismissed; it is the run's, so a page revisit does not revive it.</summary>
    public bool CompositionNoticeDismissed => _live?.CompositionNoticeDismissed ?? true;

    public void DismissCompositionNotice()
    {
        if (_live is { } live) live.CompositionNoticeDismissed = true;
    }

    /// <summary>
    /// Visible means in effect (SPEC-filtering.md §5): the mix panel is on screen, and the
    /// rows on it compose the next Start, exactly when the setting is on and the folder in
    /// hand holds a stats document with content.
    /// </summary>
    public bool MixInEffect => _settings.WeightQuizzesByStats && Setup is { Stats.HasStats: true };

    /// <summary>The mix the next Start composes with: the draft while in effect, else blank; null while the draft on screen does not build.</summary>
    public QuizMix? MixForStart => MixInEffect ? _mixDraft.Build() : QuizMix.Empty;

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

            if (picked.Folder is not { } folder) return FolderOutcome.Cancelled;
            return await LandAsync(folder, picked.Sample);
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

            return await LandAsync(collected.Folder!, collected.Sample);
        }
        finally
        {
            End();
        }
    }

    /// <summary>Let the folder go. The run in hand, live or finished, stays; a live run keeps recording into its folder.</summary>
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

    private async Task<FolderOutcome> LandAsync(IFolderContext folder, FolderSample sample)
    {
        Enter(QuizPhase.Loading);
        if (sample.Files.IsEmpty)
        {
            await ReleaseIfUnheldAsync(folder);
            await DropSetupAsync();
            return FolderOutcome.NoFiles;
        }

        // The sample has landed, and the run in progress ends here, its summary kept; the old
        // setup, its count, its filter source and its folder go with it, before the new files
        // are read, so two parsed collections never coexist (SPEC-quiz-history.md §9; ruled
        // 2026-10-16, halheinrich/backgammon#421).
        EndRun(endedByNewSample: true);
        await DropSetupAsync();

        LoadedProblems loaded;
        try
        {
            loaded = await _load(
                sample.Files,
                progress =>
                {
                    Progress = progress;
                    Changed?.Invoke();
                },
                CancellationToken.None);
        }
        catch (Exception failure)
        {
            // The folder arrived but could not be read as problems: no folder is in hand, and
            // the run the landing finished stays finished, with its summary (§9).
            await ReleaseIfUnheldAsync(folder);
            return FolderOutcome.Failed(failure);
        }

        var setup = new QuizSetup(
            folder, sample, loaded, await StatsProbe.OfAsync(folder), FilterSourceToken.FromGeneration(++_setups));
        Setup = setup;
        _filterSetup.ReportSource(setup.FilterSource);
        Changed?.Invoke();
        await RecountCoreAsync();
        return FolderOutcome.Landed;
    }

    /// <summary>No folder is in hand any more: the setup, its count and its filter source go, and its folder is released unless a live run still records into it.</summary>
    private async Task DropSetupAsync()
    {
        var setup = Setup;
        Setup = null;
        _selection = null;
        _filterSetup.ReportSource(null);
        Changed?.Invoke();
        if (setup is not null) await ReleaseIfUnheldAsync(setup.Folder);
    }

    /// <summary>Release <paramref name="folder"/> unless the setup or the live run still holds it. Never throws: a release that fails is logged, and nothing reads the handle again.</summary>
    private async Task ReleaseIfUnheldAsync(IFolderContext folder)
    {
        if (ReferenceEquals(Setup?.Folder, folder) || ReferenceEquals(_live?.Folder, folder)) return;
        try
        {
            await folder.DisposeAsync();
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

    /// <summary>
    /// A selection was committed (an Apply, a Clear, a restoration settling the ready empty
    /// selection): count it, now or when the operation in flight ends. An edit commits nothing,
    /// so nothing runs as the user types; what an edit alone puts in effect — the empty
    /// selection — is counted at the next commit and composed at Start.
    /// </summary>
    private void OnFilterApplied(FilterSetupSnapshot snapshot)
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

    /// <summary>
    /// Compose a new run from the setup under the filter in effect, the current settings
    /// and the mix in effect; <paramref name="withoutMix"/> runs this one quiz unweighted
    /// while the mix stays what the user built.
    /// </summary>
    public Task<StartOutcome> StartAsync(bool withoutMix = false)
    {
        if (IsBusy) return Task.FromResult(StartOutcome.Busy);
        if (Setup is not { } setup) return Task.FromResult(StartOutcome.NoSetup);
        if (FilterInEffect is not { } config) return Task.FromResult(StartOutcome.NoFilterInEffect);
        var mix = withoutMix ? QuizMix.Empty : MixForStart;
        if (mix is null) return Task.FromResult(StartOutcome.MixInvalid);
        return StartCoreAsync(setup, config, mix);
    }

    /// <summary>
    /// Compose a new run from the setup under the finished run's filter and mix and the
    /// current settings. Restart weights iff the mix is in effect at this moment
    /// (SPEC-filtering.md §5): with the mix gone, it runs unweighted.
    /// </summary>
    public Task<StartOutcome> RestartAsync(bool withoutMix = false)
    {
        if (IsBusy) return Task.FromResult(StartOutcome.Busy);
        if (Finished is not { } finished) return Task.FromResult(StartOutcome.NothingToRestart);
        if (Setup is not { } setup) return Task.FromResult(StartOutcome.NoSetup);
        var mix = withoutMix || !MixInEffect ? QuizMix.Empty : finished.Mix;
        return StartCoreAsync(setup, finished.Filters, mix);
    }

    private async Task<StartOutcome> StartCoreAsync(QuizSetup setup, FilterConfig config, QuizMix mix)
    {
        if (!TryBegin(QuizPhase.Preparing)) return StartOutcome.Busy;
        try
        {
            await _settings.EnsureHydratedAsync();
            var ranking = _settings.Ranking;
            var yielder = new CooperativeYielder(_clock);

            // The stats destination activates with the run: bound now, over the setup's
            // folder, and held by the run only on success, so a refused or faulted attempt
            // leaves the run in hand recording where it was (halheinrich/backgammon#404).
            var stats = await QuizStats.BindAsync(setup.Folder, _clock);
            if (!mix.IsPassthrough && stats.Document is null) return StartOutcome.MixRequiresStats;

            var selection = _selection?.Describes(setup, config, ranking) == true
                ? _selection
                : await Selection.ComputeAsync(setup, config, ranking, yielder, CancellationToken.None);
            if (config.Equals(FilterInEffect)) _selection = selection;

            var sequence = await ProblemSequence.BuildAsync(
                selection.Pool, mix, _settings.Order, ranking,
                stats.Document, _clock.GetUtcNow(), new Random(), yielder, CancellationToken.None);
            if (sequence.Problems.Count == 0) return StartOutcome.NoProblems;

            // The prepared run replaces whatever was in hand only now, on success.
            var run = QuizRun.Begin(ranking, sequence.Problems).PresentNext(RandomSide());
            _live = new LiveQuiz(run, config, mix, setup.Name, setup.Folder, stats, sequence.Composition);
            Finished = null;
            return StartOutcome.Started;
        }
        finally
        {
            End();
        }
    }

    // ── The run ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Submit a play. The first submission retires the composition notice (it said how the
    /// quiz was built, and the user has now acted on it); an answer of record is folded into
    /// the folder's stats file before the call returns, with the app busy meanwhile.
    /// </summary>
    public async Task SubmitPlayAsync(Play play)
    {
        if (IsBusy || _live is not { Run.IsAnswering: true } live) return;
        live.Run = live.Run.SubmitPlay(play, out var answer);
        live.CompositionNoticeDismissed = true;
        Changed?.Invoke();
        if (answer is not null) await RecordAsync(live, answer);
    }

    /// <summary>Submit a cube answer, as <see cref="SubmitPlayAsync"/> submits a play.</summary>
    public async Task SubmitCubeAnswerAsync(CubeAnswer answer)
    {
        if (IsBusy || _live is not { Run.IsAnswering: true } live) return;
        live.Run = live.Run.SubmitCubeAnswer(answer, out var answerOfRecord);
        live.CompositionNoticeDismissed = true;
        Changed?.Invoke();
        if (answerOfRecord is not null) await RecordAsync(live, answerOfRecord);
    }

    /// <summary>The one stats write per answer of record, awaited before the next command is accepted; the recording never throws.</summary>
    private async Task RecordAsync(LiveQuiz live, AnswerOfRecord answer)
    {
        Enter(QuizPhase.Recording);
        try
        {
            await live.Stats.RecordAsync(answer);
        }
        finally
        {
            End();
        }
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
        Finished = new FinishedQuiz(ended.ToResult(), live.Filters, live.Mix, live.Name, live.Stats.Report, endedByNewSample);
        _live = null;
        // The finished run holds nothing of the folder; nothing waits on the release and nothing reads the handle again.
        _ = ReleaseIfUnheldAsync(live.Folder);
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

    public void Dispose() => _filterSetup.Applied -= OnFilterApplied;

    /// <summary>A run in progress: its folder and stats recording, and what a Restart needs beside it.</summary>
    private sealed class LiveQuiz(
        QuizRun run, FilterConfig filters, QuizMix mix, string name, IFolderContext folder, QuizStats stats, MixComposition? composition)
    {
        public QuizRun Run { get; set; } = run;
        public FilterConfig Filters { get; } = filters;
        public QuizMix Mix { get; } = mix;
        public string Name { get; } = name;
        public IFolderContext Folder { get; } = folder;
        public QuizStats Stats { get; } = stats;
        public MixComposition? Composition { get; } = composition;
        public bool CompositionNoticeDismissed { get; set; }
    }
}
