namespace BgQuiz_Blazor.Client.Quiz;

using System.Text.Json;
using BgGame_Lib;
using Microsoft.JSInterop;

/// <summary>
/// The controller-facing seam for lifetime-stats recording: bind a stats
/// context at quiz start, then fold finalized submissions into it. Implemented
/// by <see cref="QuizStatsStore"/>; the split lets
/// <see cref="QuizController"/> depend on exactly the two operations it
/// drives, and lets tests substitute a recording fake.
/// </summary>
internal interface IDecisionStatsSink
{
    /// <summary>
    /// Bind the active stats context for the quiz now starting: promote the
    /// picked folder to the active slot and load (or seed) its stats document.
    /// Called by the controller on every Start/Restart; re-binding replaces
    /// the previous context and clears its failure states.
    /// </summary>
    Task BeginQuizAsync();

    /// <summary>Fold a finalized checker-play submission into the active document and persist it.</summary>
    Task RecordAsync(SubmittedPlay play);

    /// <summary>Fold a finalized cube submission (one decision — both halves) into the active document and persist it.</summary>
    Task RecordAsync(SubmittedCubeAction cube);
}

/// <summary>
/// The active stats context's condition, driving the quiz-context notices on
/// the Quiz and Done pages. Scoped to the running quiz: every
/// <see cref="IDecisionStatsSink.BeginQuizAsync"/> re-derives it from scratch.
/// </summary>
internal enum QuizStatsStatus
{
    /// <summary>No stats for this quiz — unsupported browser, denied write permission, or nothing promoted. Not a failure; no notice renders.</summary>
    Disabled,

    /// <summary>Document loaded (or seeded) and recording; each fold writes straight back.</summary>
    Ready,

    /// <summary>
    /// The existing stats file couldn't be parsed (corrupt, foreign, or a
    /// newer schema). Terminal for this quiz: nothing records and the file is
    /// <b>never</b> written — the user's data is preserved untouched.
    /// </summary>
    LoadFailed,

    /// <summary>
    /// A write-back failed. The folded document is kept in memory but no
    /// further writes are attempted this quiz (no per-answer error spam).
    /// </summary>
    WriteFailed,
}

/// <summary>
/// Owns the persistent <see cref="DecisionStatsDocument"/> for the running
/// quiz: binds it at quiz start (<see cref="BeginQuizAsync"/>), folds each
/// finalized submission via the producer's <c>Plus</c>, and writes the
/// document back through <see cref="IFolderAccess"/> after every fold
/// (small file; crash-safe — a lost tab loses nothing already answered).
///
/// <para>
/// Lifetime: <b>Scoped</b>, like the controller it serves. Registered once
/// and aliased as <see cref="IDecisionStatsSink"/> so the controller's sink
/// and the pages' status reads observe the same instance.
/// </para>
///
/// <para>
/// <b>Active-context only.</b> The store has no pick-time involvement: picking
/// and clearing touch <see cref="PickedProblemFolder"/> and the JS module's
/// picked slot, never this store. The stats context (document + write handle)
/// binds at Start/Restart via the promote operation, so a mid-quiz Clear or
/// re-pick cannot affect the running quiz's recording; <see cref="Status"/>
/// failure states likewise scope to the active context and reset on the next
/// bind.
/// </para>
///
/// <para>
/// <b>Degrade, never block.</b> No member of this class throws for stats
/// trouble: a load failure records nothing and preserves the file untouched
/// (<see cref="QuizStatsStatus.LoadFailed"/>); a write failure keeps folding
/// in memory but stops writing (<see cref="QuizStatsStatus.WriteFailed"/>).
/// The quiz itself is never interrupted — no-stats mode is fully functional.
/// </para>
///
/// <para>
/// The clock enters through the injected <see cref="TimeProvider"/>, handed to
/// the document's <c>Plus</c> (the producer resolves <c>GetUtcNow</c> itself)
/// — ambient time is never read here.
/// </para>
/// </summary>
internal sealed class QuizStatsStore : IDecisionStatsSink
{
    private readonly IFolderAccess _folderAccess;
    private readonly TimeProvider _clock;
    private readonly PickedProblemFolder _folder;

    private DecisionStatsDocument _doc = DecisionStatsDocument.Empty;

    public QuizStatsStore(IFolderAccess folderAccess, TimeProvider clock, PickedProblemFolder folder)
    {
        _folderAccess = folderAccess ?? throw new ArgumentNullException(nameof(folderAccess));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _folder = folder ?? throw new ArgumentNullException(nameof(folder));
    }

    /// <summary>The active context's condition; see <see cref="QuizStatsStatus"/>.</summary>
    public QuizStatsStatus Status { get; private set; } = QuizStatsStatus.Disabled;

    /// <summary>
    /// Raised when <see cref="Status"/> changes, so observing pages re-render
    /// their stats notices (mirrors <see cref="QuizController.StateChanged"/>).
    /// </summary>
    public event Action? StatusChanged;

    public async Task BeginQuizAsync()
    {
        // Re-derive the whole context: a re-bind clears any prior LoadFailed /
        // WriteFailed and replaces the previous quiz's document outright.
        _doc = DecisionStatsDocument.Empty;

        // Capability is the pick-time verdict; the promote is the handle-level
        // half of the same check (false when the picked slot holds no
        // FS-Access handle — fallback pick, cleared, or never picked).
        if (_folder.Capability != StatsSaveCapability.Enabled)
        {
            SetStatus(QuizStatsStatus.Disabled);
            return;
        }

        try
        {
            if (!await _folderAccess.PromoteToActiveAsync())
            {
                SetStatus(QuizStatsStatus.Disabled);
                return;
            }

            var json = await _folderAccess.ReadStatsJsonAsync();
            _doc = json is null
                ? DecisionStatsDocument.Empty                          // fresh corpus — first quiz here
                : JsonSerializer.Deserialize<DecisionStatsDocument>(json)
                  ?? throw new JsonException("Stats document deserialized to null.");
            SetStatus(QuizStatsStatus.Ready);
        }
        catch (Exception ex) when (ex is JsonException or JSException)
        {
            // Corrupt / foreign / newer-schema file (JsonException), or the
            // browser failed the read (JSException). Either way: this quiz
            // records nothing and the existing file is never written.
            SetStatus(QuizStatsStatus.LoadFailed);
        }
    }

    public Task RecordAsync(SubmittedPlay play)
    {
        ArgumentNullException.ThrowIfNull(play);
        return FoldAndPersistAsync(doc => doc.Plus(play, _clock));
    }

    public Task RecordAsync(SubmittedCubeAction cube)
    {
        ArgumentNullException.ThrowIfNull(cube);
        return FoldAndPersistAsync(doc => doc.Plus(cube, _clock));
    }

    /// <summary>
    /// The shared fold-then-write step. Only a <see cref="QuizStatsStatus.Ready"/>
    /// context folds; a write failure keeps the folded document (the fold
    /// itself succeeded) but flips to <see cref="QuizStatsStatus.WriteFailed"/>
    /// so no further writes are attempted this quiz. Never throws — the
    /// controller's Continue must not fault on stats trouble.
    /// </summary>
    private async Task FoldAndPersistAsync(Func<DecisionStatsDocument, DecisionStatsDocument> fold)
    {
        if (Status != QuizStatsStatus.Ready) return;

        _doc = fold(_doc);
        try
        {
            await _folderAccess.WriteStatsJsonAsync(
                JsonSerializer.Serialize(_doc, QuizStatsFile.SerializerOptions));
        }
        catch (JSException)
        {
            SetStatus(QuizStatsStatus.WriteFailed);
        }
    }

    private void SetStatus(QuizStatsStatus status)
    {
        if (Status == status) return;
        Status = status;
        StatusChanged?.Invoke();
    }
}
