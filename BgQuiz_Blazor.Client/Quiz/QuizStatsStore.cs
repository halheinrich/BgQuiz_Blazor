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
internal interface IProblemStatsSink
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
    /// problem in it. Every consumer of that question routes through this —
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
    ProblemStatsDocument? CurrentDocument { get; }
}

/// <summary>
/// The active stats context's condition, driving the quiz-context notices on
/// the Quiz and Done pages. Scoped to the running quiz: every
/// <see cref="IProblemStatsSink.BeginQuizAsync"/> re-derives it from scratch.
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
/// Owns the persistent <see cref="ProblemStatsDocument"/> for the running
/// quiz: binds it at quiz start (<see cref="BeginQuizAsync"/>), folds each
/// finalized submission via the producer's <c>Plus</c>, and writes the
/// document back through <see cref="IFolderAccess"/> after every fold
/// (small file; crash-safe — a lost tab loses nothing already answered).
///
/// <para>
/// Lifetime: <b>Scoped</b>, like the controller it serves. Registered once
/// and aliased as <see cref="IProblemStatsSink"/> so the controller's sink
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
internal sealed class QuizStatsStore : IProblemStatsSink
{
    private readonly IFolderAccess _folderAccess;
    private readonly TimeProvider _clock;
    private readonly PickedProblemFolder _folder;

    private ProblemStatsDocument _doc = ProblemStatsDocument.Empty;

    /// <summary>
    /// The pick-time probe's verdict: whether the picked folder's stats
    /// document exists and holds at least one problem. Written only by
    /// <see cref="RefreshPickedStatsAsync"/>, read only through
    /// <see cref="CanWeightMix"/>, and deliberately never consulted by the
    /// bind path — a fresh folder with no stats still binds and still records.
    /// </summary>
    private bool _pickedHasStats;

    /// <summary>
    /// The retired schema version the picked folder's stats document declared,
    /// or <see langword="null"/> when the probe found no document, a current-
    /// version one, or one it could not identify. Written only by
    /// <see cref="RefreshPickedStatsAsync"/> and read only through
    /// <see cref="ForecastStatsSetAsideName"/>.
    ///
    /// <para>
    /// <b>A second fact out of the same read, never a second answer to the mix
    /// question.</b> A retired document is "no stats to weight by" exactly as
    /// before (<see cref="_pickedHasStats"/> stays false on this path), and the
    /// probe still writes nothing and retires nothing — the version is simply
    /// no longer thrown away, so <c>Home</c> can say at pick time what the next
    /// bind will do (issue <c>halheinrich/backgammon#146</c>).
    /// </para>
    /// </summary>
    private int? _pickedRetiredSchemaVersion;

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
    /// <b>The identity of the condition <see cref="Status"/> currently reports</b>
    /// — a token with no content whatsoever, whose only meaning is that it is or
    /// is not the same object as one held earlier. It is replaced in exactly two
    /// places, both of which mean "what this store is reporting is a different
    /// occurrence now": at the top of <see cref="BeginQuizAsync"/>, which
    /// re-derives the whole context for a new run, and in <see cref="SetStatus"/>
    /// on a real transition.
    ///
    /// <para>
    /// It exists for the Quiz page's dismissible stats notice
    /// (<see cref="QuizNoticeDismissal"/>), which needs to distinguish "the user
    /// dismissed <i>this</i> report" from "a report is showing". The two cases
    /// that make a plain <see cref="Status"/> comparison wrong are both real: a
    /// <c>Ready → WriteFailed</c> transition mid-run is a new thing to say, and a
    /// second quiz bound against the same unreadable file is <i>also</i> a new
    /// thing to say — that run records nothing either, which the user has not
    /// been told yet. Re-binding therefore mints a fresh token even when the
    /// status it lands on is the one already showing.
    /// </para>
    ///
    /// <para>
    /// Deliberately an opaque object rather than a generation counter: the
    /// dismissal holder keys every notice by reference identity (the composition
    /// notice's token is its <c>MixComposition</c>, a record whose value equality
    /// would silently dismiss a later identical run), so a value token would have
    /// to be compared by a second rule. One rule, one kind of token.
    /// </para>
    /// </summary>
    public object StatusOccurrence { get; private set; } = new();

    /// <summary>
    /// <b>The occurrence of this run's stats-retirement report, or
    /// <see langword="null"/> when this run retired nothing</b> — the nullable
    /// token <i>is</i> the flag, so there is no companion boolean that could
    /// disagree with it. Minted only in
    /// <see cref="RetirePreviousStatsAsync"/>, once the set-aside has actually
    /// happened, and cleared at the top of every <see cref="BeginQuizAsync"/>:
    /// a second quiz over the now-current file has nothing to report, without
    /// anyone having to remember to reset anything.
    ///
    /// <para>
    /// The token carries what the report has to say — the name the old document
    /// went to, which varies by the version it declared. One object rather than
    /// a token beside a nullable name, so "there is a report" and "this is what
    /// it says" cannot come apart.
    /// </para>
    ///
    /// <para>
    /// Deliberately <b>not</b> a <see cref="QuizStatsStatus"/> value. After a
    /// retirement the context is <see cref="QuizStatsStatus.Ready"/> and can
    /// still fail its next write — retired-ness is a fact about how this run
    /// began, orthogonal to the condition <see cref="Status"/> reports, and
    /// folding it into that enum would make every <c>== Ready</c> site grow an
    /// "or retired" clause.
    /// </para>
    ///
    /// <para>
    /// Its own token rather than a share of <see cref="StatusOccurrence"/>,
    /// for the same reason the two notices are separate: a mid-run
    /// <c>Ready → WriteFailed</c> mints a fresh status token, which must not
    /// resurrect a retirement report the user has already read and dismissed.
    /// </para>
    /// </summary>
    public StatsRetirement? StatsRetiredOccurrence { get; private set; }

    /// <summary>
    /// Whether the picked folder can hold a stats document at all — the write-
    /// capability half of both <see cref="CanWeightMix"/> and the
    /// <see cref="BeginQuizAsync"/> bind, single-sourced so the question
    /// "could this folder carry stats?" has one spelling in this class.
    /// </summary>
    private bool FolderCanHoldStats => _folder.Capability == FolderWriteCapability.Enabled;

    /// <summary>
    /// Whether the last probe still describes the folder currently held — the
    /// expires-by-construction rule of <see cref="_statsProbeGeneration"/>, in
    /// one spelling, so every fact the probe surfaces expires on the same
    /// terms rather than on its own copy of the comparison.
    /// </summary>
    private bool ProbeDescribesTheCurrentPick => _statsProbeGeneration == _folder.PickGeneration;

    /// <inheritdoc/>
    public bool CanWeightMix =>
        FolderCanHoldStats && _pickedHasStats && ProbeDescribesTheCurrentPick;

    /// <summary>
    /// <b>The name the picked folder's stats document would be set aside under
    /// when a quiz next binds against it</b> — the pick-time <i>forecast</i> of
    /// the retirement <see cref="StatsRetiredOccurrence"/> reports afterwards
    /// (issue <c>halheinrich/backgammon#146</c>) — or <see langword="null"/>
    /// when no retirement is in prospect for the folder in hand.
    ///
    /// <para>
    /// <b>The nullable name is the flag</b>, the same discipline as
    /// <see cref="StatsRetiredOccurrence"/>: there is no companion boolean to
    /// disagree with it. And it is derived through the same
    /// <see cref="QuizStatsFile.RetiredNameFor"/> the act itself calls, from the
    /// version the document declared — so the forecast and the report are two
    /// tenses of one event that cannot name two different files, and a holder of
    /// a v1 document is told about <c>.v1.json</c> rather than about whatever
    /// version happens to retire most often.
    /// </para>
    ///
    /// <para>
    /// <b>A forecast, not a promise, and above all not an act.</b> The probe
    /// that feeds this reads and never writes — a pick must not mutate the
    /// folder (SPEC-stats-identity.md §3: the set-aside is the bind's, where
    /// write permission is settled), so between this notice and the next Start
    /// the file can still change under it. That is the same standing of the
    /// stats-location notice beside it, which also says what a later quiz will
    /// write.
    /// </para>
    ///
    /// <para>
    /// Expires with the pick that produced it
    /// (<see cref="ProbeDescribesTheCurrentPick"/>), so a verdict about the
    /// previous folder can never be read as one about this one — and a
    /// non-<see langword="null"/> value therefore implies a held pick whose
    /// capability let the probe read at all.
    /// </para>
    /// </summary>
    public string? ForecastStatsSetAsideName =>
        _pickedRetiredSchemaVersion is { } version && ProbeDescribesTheCurrentPick
            ? QuizStatsFile.RetiredNameFor(version)
            : null;

    /// <summary>
    /// Take the pick-time probe <see cref="CanWeightMix"/> reads: does the
    /// folder currently picked already hold a stats document with something in
    /// it — and, if what it holds is a document of a retired schema version,
    /// which version (<see cref="ForecastStatsSetAsideName"/>)? Driven by
    /// <c>Home</c> at the two moments the answer can change
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
    /// <b>The retired-version rung is told apart for what it says, not for what
    /// it answers</b> (issue <c>halheinrich/backgammon#146</c>). It is still
    /// "no stats to weight by" — the producer's recognition signal derives from
    /// <see cref="JsonException"/>, so before this it simply fell into the
    /// swallow below with the corrupt files. Catching it first keeps the mix
    /// answer identical (nothing sets <see cref="_pickedHasStats"/> on this
    /// path) and keeps the read read-only, while remembering the one fact
    /// <c>Home</c>'s forecast notice needs: the version, from which the
    /// set-aside name derives. A corrupt file, a foreign one, and a
    /// newer-schema one keep the swallow — none of them will be retired at the
    /// next bind, so none of them has a forecast to make.
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
        _pickedRetiredSchemaVersion = null;

        if (!FolderCanHoldStats) return;

        try
        {
            var json = await _folderAccess.ReadPickedFileAsync(QuizStatsFile.FileName);
            _pickedHasStats = json is not null
                && JsonSerializer.Deserialize(json, QuizStatsFile.DocumentTypeInfo) is { Count: > 0 };
        }
        catch (RetiredStatsSchemaException retired)
        {
            // A document the next bind will set aside. Remembered — the version
            // only — so Home can forecast that act before the user commits to
            // it; caught ahead of the swallow below because the signal derives
            // from JsonException. Nothing is written and nothing is retired
            // here: this is a read of the picked slot, and the act is the
            // bind's alone.
            _pickedRetiredSchemaVersion = retired.SchemaVersion;
        }
        catch (Exception ex) when (ex is JsonException or JSException)
        {
            // Unreadable reads exactly as absent — see the summary. The file
            // itself is left alone; only the bind decides what to do about a
            // document it cannot parse.
        }
    }

    /// <inheritdoc/>
    public ProblemStatsDocument? CurrentDocument =>
        Status is QuizStatsStatus.Ready or QuizStatsStatus.WriteFailed ? _doc : null;

    /// <summary>
    /// Raised when <see cref="Status"/> changes, so observing pages re-render
    /// their stats notices (mirrors <see cref="QuizController.StateChanged"/>).
    /// </summary>
    public event Action? StatusChanged;

    public async Task BeginQuizAsync()
    {
        // Re-derive the whole context: a re-bind clears any prior LoadFailed /
        // WriteFailed and replaces the previous quiz's document outright — and
        // starts a new notice occurrence, so a dismissal recorded against the
        // last run cannot silence this one. That matters precisely when the
        // status below lands on the value already showing (the same unreadable
        // file, a second time): SetStatus would report no transition, but this
        // run still has something unsaid to say.
        _doc = ProblemStatsDocument.Empty;
        StatusOccurrence = new object();
        StatsRetiredOccurrence = null;

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

        // Two try blocks, not one, so the file's own text is still in hand at
        // the retirement catch below: the bytes set aside there are the bytes
        // just read, never a second read of a file that may have moved on.
        string? json;
        try
        {
            if (!await _folderAccess.PromoteToActiveAsync())
            {
                SetStatus(QuizStatsStatus.Disabled);
                return;
            }

            json = await _folderAccess.ReadActiveFileAsync(QuizStatsFile.FileName);
        }
        catch (JSException)
        {
            // The browser failed the read: this quiz records nothing and the
            // existing file is never written.
            SetStatus(QuizStatsStatus.LoadFailed);
            return;
        }

        try
        {
            _doc = json is null
                ? ProblemStatsDocument.Empty                          // fresh corpus — first quiz here
                : JsonSerializer.Deserialize(json, QuizStatsFile.DocumentTypeInfo)
                  ?? throw new JsonException("Stats document deserialized to null.");
        }
        catch (RetiredStatsSchemaException retired)
        {
            // The producer's deliberate recognition signal: a genuine document
            // in one of the retired formats. Caught BEFORE the general
            // JsonException below — an existing tester's file must not surface
            // as a hard load error with their stats silently dead. The version
            // it declared travels with it: the set-aside name is derived from
            // it, so two retirements in the same folder cannot collide.
            await RetirePreviousStatsAsync(json!, retired.SchemaVersion);
            return;
        }
        catch (JsonException)
        {
            // Corrupt, foreign, or a NEWER schema than this build reads. Newer
            // is deliberately not retired: it is a file this version has no
            // business rewriting, so it keeps the untouched posture.
            SetStatus(QuizStatsStatus.LoadFailed);
            return;
        }

        SetStatus(QuizStatsStatus.Ready);
    }

    /// <summary>
    /// Retire a stats file in a retired schema version (SPEC-stats-identity.md
    /// §3): copy its bytes aside under
    /// <see cref="QuizStatsFile.RetiredNameFor"/> unparsed, put a fresh
    /// current-version document under the standard name, and mint
    /// <see cref="StatsRetiredOccurrence"/> so the run says so. There is no
    /// migration — the retired content is never read, only preserved.
    ///
    /// <para>
    /// <b>The set-aside name is derived from the version the document declared,
    /// never fixed.</b> Every schema below the current one retires, so a folder
    /// can see two retirements in sequence — a tester who skipped a release
    /// holds a v1 file, meets one release that sets it aside, then a later one
    /// that retires what that release wrote. One name for both would put the
    /// second copy over the first and destroy the file the first set-aside
    /// existed to preserve.
    /// </para>
    ///
    /// <para>
    /// <b>Set aside first, replace second, and never the other way round.</b> A
    /// failure on the copy leaves the user's file exactly as it was and reports
    /// <see cref="QuizStatsStatus.LoadFailed"/>: replacing a file we could not
    /// first preserve would destroy it. A failure on the replace is the same
    /// answer — the standard name still holds the retired document, so the next
    /// bind recognises it and retries. The whole operation is idempotent under
    /// that retry: the second attempt copies identical bytes over the sidecar it
    /// wrote the first time.
    /// </para>
    ///
    /// <para>
    /// Built from the existing named-file primitives rather than a rename API,
    /// deliberately: one consumer does not justify lifting a rename into
    /// BgFolderAccess_Razor's surface. A second one would.
    /// </para>
    /// </summary>
    private async Task RetirePreviousStatsAsync(string retiredJson, int retiredSchemaVersion)
    {
        string setAsideName = QuizStatsFile.RetiredNameFor(retiredSchemaVersion);

        try
        {
            await _folderAccess.WriteActiveFileAsync(setAsideName, retiredJson);
            await _folderAccess.WriteActiveFileAsync(
                QuizStatsFile.FileName,
                JsonSerializer.Serialize(ProblemStatsDocument.Empty, QuizStatsFile.DocumentTypeInfo));
        }
        catch (JSException)
        {
            SetStatus(QuizStatsStatus.LoadFailed);
            return;
        }

        _doc = ProblemStatsDocument.Empty;
        StatsRetiredOccurrence = new StatsRetirement(setAsideName);
        SetStatus(QuizStatsStatus.Ready);
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
    ///
    /// <para>
    /// <b>The pre-write guard</b> (ruled; SPEC-stats-identity.md §5). Each fold
    /// re-reads the file and applies this submission to <i>that</i> document
    /// rather than to the bind-time snapshot. Every write is a whole-document
    /// write, so two stats contexts over one folder — a second tab, or an
    /// external edit mid-quiz — otherwise mean the last writer silently
    /// discards everything the other recorded since its bind. The re-read
    /// shrinks that loss window to a same-instant race, for one small read per
    /// answer. It does not make concurrent recording safe, and is not meant to:
    /// it is the boring lost-update answer, not a lock.
    /// </para>
    ///
    /// <para>
    /// A no-key submission (the ratified rung) folds to the same document and
    /// is still written. Skipping the write would have to key on the producer's
    /// return being reference-identical — a fragile cross-check for a write
    /// already paid per answer.
    /// </para>
    /// </summary>
    private async Task FoldAndPersistAsync(Func<ProblemStatsDocument, ProblemStatsDocument> fold)
    {
        if (Status != QuizStatsStatus.Ready) return;

        _doc = fold(await ReadFoldBaseAsync());
        try
        {
            await _folderAccess.WriteActiveFileAsync(
                QuizStatsFile.FileName,
                JsonSerializer.Serialize(_doc, QuizStatsFile.DocumentTypeInfo));
        }
        catch (JSException)
        {
            SetStatus(QuizStatsStatus.WriteFailed);
        }
    }

    /// <summary>
    /// The document this fold applies to: the file as it stands right now, or —
    /// on any trouble reading it — the in-memory document, which is exactly the
    /// fold-and-write behaviour that predated the guard.
    ///
    /// <para>
    /// Missing, unreadable and unparseable are one answer here, deliberately,
    /// and none of them changes <see cref="Status"/>: the guard is an
    /// improvement on the overwrite window, never a new way for a quiz to stop
    /// recording. The retired-schema exception is caught with the rest — a file
    /// swapped to v1 mid-quiz is read trouble, not a second retirement, and the
    /// bind is the one place that decides to set a file aside.
    /// </para>
    /// </summary>
    private async Task<ProblemStatsDocument> ReadFoldBaseAsync()
    {
        try
        {
            var json = await _folderAccess.ReadActiveFileAsync(QuizStatsFile.FileName);
            return json is null
                ? _doc
                : JsonSerializer.Deserialize(json, QuizStatsFile.DocumentTypeInfo) ?? _doc;
        }
        catch (Exception ex) when (ex is JsonException or JSException)
        {
            return _doc;
        }
    }

    private void SetStatus(QuizStatsStatus status)
    {
        if (Status == status) return;
        Status = status;
        // A different condition is a different thing to report, so it gets its
        // own occurrence: a dismissal of the load-failure notice must not
        // pre-dismiss a write failure that happens later in the same run.
        StatusOccurrence = new object();
        StatusChanged?.Invoke();
    }
}
