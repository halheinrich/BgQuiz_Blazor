namespace BgQuiz_Blazor.Client.Quiz;

using System.Text.Json;
using BgFolderAccess_Razor;
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

    /// <summary>Fold a finalized cube submission (two decisions — one per half) into the active document and persist it.</summary>
    Task RecordAsync(SubmittedCubeAction cube);

    /// <summary>
    /// <b>The one predicate for "can a weighted mix mean anything here"</b>
    /// (issue <c>halheinrich/backgammon#87</c>): the picked folder can save
    /// stats <i>and</i> already holds a stats document with at least one
    /// decision in it. Every consumer of that question routes through this —
    /// <c>Home</c>'s decision to offer the mix panel at all, and the
    /// controller's stage-1 refusal below — so the two can never disagree
    /// about whether a mix is meaningful for the folder in hand.
    ///
    /// <para>
    /// <b>An empty stats document counts as no stats document</b> (ratified):
    /// weighting composes <i>from</i> the lifetime record, so a record with
    /// nothing in it can express no weighting anybody asked for. Missing,
    /// empty, and unreadable therefore all read <see langword="false"/> —
    /// one outcome, no rungs.
    /// </para>
    ///
    /// <para>
    /// Side-effect-free and cheap: it reads a probe
    /// (<see cref="QuizStatsStore.RefreshPickedStatsAsync"/>) taken at pick
    /// time, so nothing is promoted, read, or reset on this rung and a refusal
    /// costs nothing. <see langword="true"/> stays a necessary, not sufficient,
    /// signal — the bind itself can still fail on a file that changed since the
    /// probe, which <see cref="CurrentDocument"/> reports after the fact.
    /// </para>
    ///
    /// <para>
    /// <b>Naming trigger.</b> This puts a <i>mix</i> policy on a stats
    /// abstraction, deliberately: the honest fact-level alternative
    /// (<c>PickedFolderHasStats</c>) would scatter the "a mix needs stats"
    /// rule across both consumers instead, which is worse at two call sites.
    /// The first consumer of "does this folder have stats" that is <i>not</i>
    /// about the mix — a stats viewer, or issue #43's saved-mix gating — is
    /// when this should split into the fact plus the policy over it.
    /// </para>
    /// </summary>
    bool CanWeightMix { get; }

    /// <summary>
    /// The live lifetime-stats document of the active context, or
    /// <see langword="null"/> when the context holds none (disabled, or the
    /// existing file failed to load). Non-null while recording — including
    /// after a write failure, where folds continue in memory — and advances
    /// with every fold, so a per-enumeration reader (the composing source's
    /// stats provider) sees the record as it stands <i>now</i>, this
    /// session's folds included.
    /// </summary>
    DecisionStatsDocument? CurrentDocument { get; }
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
/// <b>Two states, two lifetimes, no traffic between them.</b> The <i>active
/// context</i> (<see cref="CurrentDocument"/>, <see cref="Status"/>) binds at
/// Start/Restart via the promote operation and belongs to the running quiz, so
/// a mid-quiz Clear or re-pick cannot affect its recording and its failure
/// states reset on the next bind. Beside it — and touching none of it — sits
/// the <i>pick-time probe</i> behind <see cref="CanWeightMix"/>
/// (<see cref="RefreshPickedStatsAsync"/>): a read of the <b>picked</b> slot
/// that promotes nothing, writes nothing, and never assigns the active
/// document or status. It lives here because this class already owns both of
/// its ingredients — how a stats document is read out of a folder and what
/// counts as unreadable, and the pick's write capability — so hosting it
/// anywhere else would duplicate the recipe.
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

    /// <summary>
    /// The pick-time probe's verdict: whether the picked folder's stats
    /// document exists and holds at least one decision. Written only by
    /// <see cref="RefreshPickedStatsAsync"/>, read only through
    /// <see cref="CanWeightMix"/>, and deliberately never consulted by the
    /// bind path — a fresh folder with no stats still binds and still records.
    /// </summary>
    private bool _pickedHasStats;

    /// <summary>
    /// The <see cref="PickedProblemFolder.PickGeneration"/> the probe above was
    /// taken against, so <see cref="CanWeightMix"/> <b>expires by
    /// construction</b> rather than by anyone remembering to reset it: every
    /// <see cref="PickedProblemFolder.Set"/> and
    /// <see cref="PickedProblemFolder.Clear"/> bumps the generation, and a
    /// probe stamped with an older one simply stops matching. The same
    /// expires-by-key idiom the applied filter uses, where the generation is
    /// the source token an applied config is keyed to
    /// (<c>AppliedFilter.ConfigFor</c>).
    ///
    /// <para>
    /// Starts at <c>-1</c>, not <c>0</c>: a never-probed store must not match
    /// the generation a freshly-constructed holder sits on, or the pre-probe
    /// state would read as a probe that found nothing — true by accident today,
    /// and wrong the moment the initial value changes.
    /// </para>
    /// </summary>
    private int _statsProbeGeneration = -1;

    public QuizStatsStore(IFolderAccess folderAccess, TimeProvider clock, PickedProblemFolder folder)
    {
        _folderAccess = folderAccess ?? throw new ArgumentNullException(nameof(folderAccess));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _folder = folder ?? throw new ArgumentNullException(nameof(folder));
    }

    /// <summary>The active context's condition; see <see cref="QuizStatsStatus"/>.</summary>
    public QuizStatsStatus Status { get; private set; } = QuizStatsStatus.Disabled;

    /// <summary>
    /// Whether the picked folder can hold a stats document at all — the write-
    /// capability half of both <see cref="CanWeightMix"/> and the
    /// <see cref="BeginQuizAsync"/> bind, single-sourced so the question
    /// "could this folder carry stats?" has one spelling in this class.
    /// </summary>
    private bool FolderCanHoldStats => _folder.Capability == FolderWriteCapability.Enabled;

    /// <inheritdoc/>
    public bool CanWeightMix =>
        FolderCanHoldStats && _pickedHasStats && _statsProbeGeneration == _folder.PickGeneration;

    /// <summary>
    /// Take the pick-time probe <see cref="CanWeightMix"/> reads: does the
    /// folder currently picked already hold a stats document with something in
    /// it? Driven by <c>Home</c> at the two moments the answer can change
    /// without this store hearing about it — its own first render (a folder may
    /// already be held, and a quiz since the last probe may have created the
    /// very record this asks about) and the landing of each successful pick.
    ///
    /// <para>
    /// <b>Degrade-tolerant by construction, because that <i>is</i> the
    /// ruling.</b> A missing file, an empty document, and an unreadable one
    /// (corrupt, foreign, newer schema, or a browser read failure) are not
    /// three outcomes to distinguish — they are one answer, "no stats to weight
    /// by". So there is no status, no notice, and nothing thrown: every path
    /// out of here leaves <see cref="_pickedHasStats"/> false and the mix simply
    /// isn't offered.
    /// </para>
    ///
    /// <para>
    /// <b>Reads the picked slot, never the active one</b>
    /// (<see cref="IFolderAccess.ReadPickedFileAsync"/>) — a setup-time read on
    /// the folder being configured, the same slot the saved-filters document
    /// uses. It deliberately does <i>not</i> promote: promotion is the
    /// bind-at-Start contract's, and this probe running earlier must not move
    /// it. The active context is untouched here in every sense — a probe during
    /// a running quiz changes nothing that quiz is recording through.
    /// </para>
    ///
    /// <para>
    /// The capability short-circuit is not an optimization dressed as a guard:
    /// under a fallback pick there is no handle to read through at all, and the
    /// predicate is false on that rung regardless — so the interop is skipped
    /// through the same <see cref="FolderCanHoldStats"/> the predicate uses,
    /// leaving no way for the two to drift.
    /// </para>
    /// </summary>
    public async Task RefreshPickedStatsAsync()
    {
        // Stamp first, then answer: a re-pick landing while the read is in
        // flight bumps the generation past this stamp, so whatever this probe
        // concludes expires instead of describing the wrong folder.
        _statsProbeGeneration = _folder.PickGeneration;
        _pickedHasStats = false;

        if (!FolderCanHoldStats) return;

        try
        {
            var json = await _folderAccess.ReadPickedFileAsync(QuizStatsFile.FileName);
            _pickedHasStats = json is not null
                && JsonSerializer.Deserialize<DecisionStatsDocument>(json) is { Count: > 0 };
        }
        catch (Exception ex) when (ex is JsonException or JSException)
        {
            // Unreadable reads exactly as absent — see the summary. The file
            // itself is left alone; only the bind decides what to do about a
            // document it cannot parse.
        }
    }

    /// <inheritdoc/>
    public DecisionStatsDocument? CurrentDocument =>
        Status is QuizStatsStatus.Ready or QuizStatsStatus.WriteFailed ? _doc : null;

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
        // Deliberately NOT gated on CanWeightMix: a brand-new folder has no
        // stats to weight by and still binds, still seeds, still records — that
        // first quiz is what creates the record the mix later composes from.
        if (!FolderCanHoldStats)
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

            var json = await _folderAccess.ReadActiveFileAsync(QuizStatsFile.FileName);
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
            await _folderAccess.WriteActiveFileAsync(
                QuizStatsFile.FileName,
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
