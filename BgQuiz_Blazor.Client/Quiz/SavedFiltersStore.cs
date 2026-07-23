namespace BgQuiz_Blazor.Client.Quiz;

using Microsoft.JSInterop;
using XgFilter_Lib.Filtering;

/// <summary>
/// The condition of the saved-filters context for the currently picked folder,
/// driving Home's saved-filters panel and its notices. Re-derived from scratch
/// on every pick (<see cref="SavedFiltersStore.LoadForPickAsync"/>); reset by
/// Clear. The stats sibling is <see cref="QuizStatsStatus"/>, and the two share
/// a posture — degrade, never block.
/// </summary>
internal enum SavedFiltersStatus
{
    /// <summary>
    /// No readable saved-filters context: nothing picked, or a fallback pick
    /// (<see cref="StatsSaveCapability.BrowserUnsupported"/>) whose mechanism
    /// exposes no directory handle. No panel renders.
    /// </summary>
    Disabled,

    /// <summary>
    /// The saved-filters document loaded (or was seeded empty for a fresh
    /// folder). Loads work; saves work too when the pick granted write access
    /// (<see cref="StatsSaveCapability.Enabled"/>).
    /// </summary>
    Ready,

    /// <summary>
    /// The saved-filters file couldn't be read (an FS read error — e.g. write
    /// access declined and read genuinely withheld) or couldn't be parsed
    /// (corrupt, foreign, or a newer schema). Terminal for this pick: the file
    /// is <b>never</b> written, so the user's data is preserved untouched, and
    /// the panel degrades to a notice.
    /// </summary>
    LoadFailed,

    /// <summary>
    /// A persist failed. The in-memory collection keeps the edit (the pick list
    /// stays truthful) but no further writes are attempted this pick — the
    /// stats store's <see cref="QuizStatsStatus.WriteFailed"/> posture, filters
    /// edition.
    /// </summary>
    WriteFailed,
}

/// <summary>
/// Owns the <see cref="NamedFilterCollection"/> for the currently picked
/// folder: reads its <c>bgquiz-filters.json</c> at pick time, applies save-as
/// and delete edits, and writes the document back through the picked slot of
/// <see cref="IFolderAccess"/>. The saved-filters sibling of
/// <see cref="QuizStatsStore"/>.
///
/// <para>
/// Lifetime: <b>Scoped</b> — one per loaded app (one tab), like the other
/// setup-time holders. Read only by <c>Home</c>, which drives every transition
/// through an awaited call (pick, save, delete, clear), so the page re-renders
/// off <see cref="Filters"/> / <see cref="Status"/> after each — no separate
/// change event is needed (unlike <see cref="QuizStatsStore"/>, whose status is
/// observed by other pages).
/// </para>
///
/// <para>
/// <b>Picked slot, setup-time.</b> Saved filters are configured on the picked
/// folder before a quiz starts, so this store reads and writes the JS module's
/// <i>picked</i> slot — never the active slot a running quiz records stats
/// through. A mid-quiz re-pick or Clear re-derives this context without
/// touching the active quiz.
/// </para>
///
/// <para>
/// <b>Degrade, never block.</b> No member throws for saved-filters trouble: a
/// read or parse failure records nothing and preserves the file untouched
/// (<see cref="SavedFiltersStatus.LoadFailed"/>); a write failure keeps the
/// in-memory collection but stops writing
/// (<see cref="SavedFiltersStatus.WriteFailed"/>). The pick and the quiz are
/// never interrupted — a folder with no usable saved-filters context is fully
/// functional, minus the saved-filters affordance.
/// </para>
///
/// <para>
/// <b>Serialization is the document's own.</b>
/// <see cref="NamedFilterCollection"/> carries a type-level
/// <c>[JsonConverter]</c>, so this store round-trips through the collection's
/// <see cref="NamedFilterCollection.ToJson"/> /
/// <see cref="NamedFilterCollection.TryFromJson"/> — the app owns no
/// <c>JsonSerializerOptions</c> (see <see cref="QuizFiltersFile"/>).
/// </para>
/// </summary>
internal sealed class SavedFiltersStore
{
    private readonly IFolderAccess _folderAccess;
    private readonly PickedProblemFolder _folder;

    private NamedFilterCollection _filters = NamedFilterCollection.Empty;

    public SavedFiltersStore(IFolderAccess folderAccess, PickedProblemFolder folder)
    {
        _folderAccess = folderAccess ?? throw new ArgumentNullException(nameof(folderAccess));
        _folder = folder ?? throw new ArgumentNullException(nameof(folder));
    }

    /// <summary>
    /// The current saved-filters document — <see cref="NamedFilterCollection.Empty"/>
    /// until a pick loads one, and again after a Clear. A fresh instance after
    /// every save/delete (the wither returns a new collection), which is what
    /// lets Home's <c>SavedFiltersPanel</c> observe the change and clear its
    /// pending confirms.
    /// </summary>
    public NamedFilterCollection Filters => _filters;

    /// <summary>The saved-filters context's condition; see <see cref="SavedFiltersStatus"/>.</summary>
    public SavedFiltersStatus Status { get; private set; } = SavedFiltersStatus.Disabled;

    /// <summary>
    /// Read the picked folder's saved-filters document, re-deriving the whole
    /// context. Called by Home after a successful pick. Guards on the pick-time
    /// capability (only the File System Access mechanisms expose a readable
    /// handle) and swallows every failure into <see cref="SavedFiltersStatus"/>
    /// — it never throws, so a pick is never blocked by saved-filters trouble.
    /// </summary>
    public async Task LoadForPickAsync()
    {
        // Staleness key: a pick that supersedes this one while the read is in
        // flight owns the outcome; this read must not clobber it (the parse
        // cache's PickGeneration discipline). Home serializes picks on the sync
        // context, so this is defensive — cheap insurance, not a live race.
        var generation = _folder.PickGeneration;

        // A fallback pick (BrowserUnsupported) has no directory handle, and a
        // cleared/never-populated holder has no folder — neither can hold a
        // saved-filters document. Short-circuit without touching the JS slot.
        if (_folder.Capability is not (StatsSaveCapability.Enabled or StatsSaveCapability.PermissionDenied))
        {
            _filters = NamedFilterCollection.Empty;
            Status = SavedFiltersStatus.Disabled;
            return;
        }

        try
        {
            var json = await _folderAccess.ReadFiltersJsonAsync();
            if (generation != _folder.PickGeneration) return;

            if (json is null)
            {
                // No file yet — a fresh folder. Ready over the empty collection;
                // the first Save writes the file into being.
                _filters = NamedFilterCollection.Empty;
                Status = SavedFiltersStatus.Ready;
            }
            else if (NamedFilterCollection.TryFromJson(json, out var loaded))
            {
                _filters = loaded;
                Status = SavedFiltersStatus.Ready;
            }
            else
            {
                // The file exists but is corrupt / foreign / newer-schema
                // (TryFromJson false on non-null input). Preserve it untouched —
                // never write the empty fallback over the user's file — and
                // disable the panel in favour of a notice.
                _filters = NamedFilterCollection.Empty;
                Status = SavedFiltersStatus.LoadFailed;
            }
        }
        catch (JSException)
        {
            if (generation != _folder.PickGeneration) return;
            // The browser failed the read: an FS error, or read access genuinely
            // withheld under PermissionDenied. Degrade — no saved filters this
            // pick, the file untouched. This is the read-failure-tolerant path
            // that keeps load-only's read assumption from being load-bearing.
            _filters = NamedFilterCollection.Empty;
            Status = SavedFiltersStatus.LoadFailed;
        }
    }

    /// <summary>
    /// Save <paramref name="config"/> under <paramref name="name"/> (add or
    /// replace) and persist. No-op unless the context is
    /// <see cref="SavedFiltersStatus.Ready"/> — the handler guard behind Home's
    /// <c>CanPersist</c> gate, holding even if the disabled button is bypassed.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="config"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> violates the collection's name rule (blank or
    /// untrimmed) — the panel pre-normalizes, so this is a caller-contract
    /// guard, not a user-facing path.
    /// </exception>
    public Task SaveAsync(string name, FilterConfig config)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(config);
        if (Status != SavedFiltersStatus.Ready) return Task.CompletedTask;
        return PersistAsync(_filters.With(name, config));
    }

    /// <summary>
    /// Remove the named filter (idempotent) and persist. No-op unless the
    /// context is <see cref="SavedFiltersStatus.Ready"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public Task DeleteAsync(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Status != SavedFiltersStatus.Ready) return Task.CompletedTask;
        return PersistAsync(_filters.Without(name));
    }

    /// <summary>
    /// Reset to the no-context state — called when the pick is cleared (or a
    /// pick yields no problem files). The next <see cref="LoadForPickAsync"/>
    /// re-derives everything.
    /// </summary>
    public void Reset()
    {
        _filters = NamedFilterCollection.Empty;
        Status = SavedFiltersStatus.Disabled;
    }

    /// <summary>
    /// Adopt <paramref name="updated"/> in memory first, then write it back.
    /// On a write failure the in-memory collection is kept (the pick list
    /// reflects the change) while writes stop for this pick — the stats store's
    /// fold-then-write / WriteFailed posture.
    /// </summary>
    private async Task PersistAsync(NamedFilterCollection updated)
    {
        _filters = updated;
        try
        {
            await _folderAccess.WriteFiltersJsonAsync(updated.ToJson());
        }
        catch (JSException)
        {
            Status = SavedFiltersStatus.WriteFailed;
        }
    }
}
