using BgFolderAccess_Razor;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;
using XgFilter_Razor;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Scriptable in-memory <see cref="IFolderAccess"/> (BgFolderAccess_Razor's)
/// for store and page tests: every browser-side behavior (pick outcome,
/// promote verdict, named-file content, write failure) is a settable property,
/// and every side-effectful call is recorded so tests can assert exactly what
/// the app drove.
///
/// <para>
/// The named-file members model the lib's two slots the way the app uses them:
/// the <b>active</b> slot is a name-keyed store that round-trips its writes,
/// serving the stats document (<see cref="StatsJson"/>) and the set-aside
/// retired one (<see cref="RetiredStatsJson"/>) — <see cref="Writes"/> keeps the
/// payloads in order and the fake also records the names, so a test can pin the
/// filename SSOT — and the <b>picked</b> slot
/// serves the saved-filters document under the producer's two names —
/// <see cref="FiltersJson"/> is the canonical
/// <see cref="SavedFiltersDocument.FileName"/> content and
/// <see cref="LegacyFiltersJson"/> the
/// <see cref="SavedFiltersDocument.LegacyFileName"/> content, so a host test
/// can stage the read-fallback exactly — plus the <b>setup-time stats</b>
/// document under <see cref="QuizStatsFile.FileName"/>
/// (<see cref="PickedStatsJson"/>), which is what the mix predicate's pick-time
/// probe reads. Any other picked name reads as absent.
/// </para>
/// </summary>
internal sealed class FakeFolderAccess : IFolderAccess
{
    /// <summary>What <see cref="SupportsDirectoryPickerAsync"/> reports (default: FS-Access available).</summary>
    public bool SupportsDirectoryPicker { get; set; } = true;

    /// <summary>Outcome the next <see cref="PickFolderAsync"/> returns (default: cancelled).</summary>
    public FolderPickOutcome NextPickOutcome { get; set; } = FolderPickOutcome.CancelledOutcome;

    /// <summary>Outcome the next <see cref="CollectFallbackAsync"/> returns (default: empty non-cancelled).</summary>
    public FolderPickOutcome NextCollectOutcome { get; set; } =
        new(Cancelled: false, DirectoryName: "", Files: [], FolderWriteCapability.BrowserUnsupported,
            Truncations: []);

    /// <summary>When set, <see cref="PickFolderAsync"/> / <see cref="CollectFallbackAsync"/> throw it instead.</summary>
    public Exception? PickException { get; set; }

    /// <summary>
    /// Invoked at the top of <see cref="PickFolderAsync"/> — i.e. at the instant
    /// the real implementation would raise the OS picker and the browser's
    /// permission prompts. The observation point for "what does the user see
    /// behind the picker?": a test inspects app state from inside it. Nothing
    /// else in the fake calls it, so it observes only the FS-Access mechanism.
    /// </summary>
    public Action? OnPickCalled { get; set; }

    /// <summary>
    /// Invoked from inside <see cref="PickFolderAsync"/> <i>after</i> its
    /// <c>onPickAccepted</c> hook has been awaited — i.e. from the stretch the
    /// real implementation spends enumerating and buffering, with the caller's
    /// busy affordance up and painted. The observation point for "what does the
    /// user see while the app scans?", and the seam a test throws from to
    /// simulate a scan that fails after the prompts succeeded. Never invoked for
    /// a cancelled outcome — the real pick does no work there either.
    /// </summary>
    public Action? OnScanning { get; set; }

    /// <summary>What <see cref="PromoteToActiveAsync"/> returns (default: an FS-Access handle is active).</summary>
    public bool PromoteResult { get; set; } = true;

    /// <summary>
    /// The active slot's files by name — the real slot's shape, which the app
    /// now needs of it in two places: the store re-reads the stats document
    /// before every fold (the pre-write guard), so a write has to be readable
    /// back, and a retirement writes a <i>second</i> name into the same folder —
    /// one derived from the retired version, so a folder can hold more than one
    /// — and the slot has to tell its files apart. Both were invisible to the
    /// single-blob-for-every-name fake this replaced.
    /// </summary>
    private readonly Dictionary<string, string> _activeFiles = [];

    /// <summary>
    /// Stats-file content under <see cref="QuizStatsFile.FileName"/> — the
    /// staging property tests set, and the one they read back to see what the
    /// folder now holds. Setting null removes the file (an absent read).
    /// </summary>
    public string? StatsJson
    {
        get => _activeFiles.GetValueOrDefault(QuizStatsFile.FileName);
        set => SetActiveFile(QuizStatsFile.FileName, value);
    }

    /// <summary>
    /// Content of the document set aside for retired schema version
    /// <paramref name="schemaVersion"/>
    /// (<see cref="QuizStatsFile.RetiredNameFor"/>); null = no such file. The
    /// retirement's whole promise is that these bytes are the old file's, so a
    /// test reads them here and compares — and asking per version is how a test
    /// can tell a set-aside that landed under the right name from one that
    /// landed under some other.
    /// </summary>
    public string? RetiredStatsJson(int schemaVersion) =>
        _activeFiles.GetValueOrDefault(QuizStatsFile.RetiredNameFor(schemaVersion));

    /// <summary>
    /// Stage a set-aside file for retired schema version
    /// <paramref name="schemaVersion"/> — an earlier release's retirement,
    /// already sitting in the folder before this run binds. Null removes it.
    /// </summary>
    public void SetRetiredStatsJson(int schemaVersion, string? content) =>
        SetActiveFile(QuizStatsFile.RetiredNameFor(schemaVersion), content);

    /// <summary>
    /// Content of the document renamed aside as merged for foldable schema
    /// version <paramref name="schemaVersion"/>
    /// (<see cref="QuizStatsFile.MergedNameFor"/>); null = no such file. The
    /// fold's promise about these bytes is the retirement's — the old file's,
    /// unparsed — under the name that says its contents live on.
    /// </summary>
    public string? MergedStatsJson(int schemaVersion) =>
        _activeFiles.GetValueOrDefault(QuizStatsFile.MergedNameFor(schemaVersion));

    private void SetActiveFile(string fileName, string? content)
    {
        if (content is null) _activeFiles.Remove(fileName);
        else _activeFiles[fileName] = content;
    }

    /// <summary>When set, <see cref="ReadActiveFileAsync"/> throws it instead.</summary>
    public Exception? ReadException { get; set; }

    /// <summary>When set, <see cref="WriteActiveFileAsync"/> throws it (after recording nothing).</summary>
    public Exception? WriteException { get; set; }

    /// <summary>
    /// Called with the file name before each active-slot write lands, so a
    /// test can fail one named write and let the others through — the
    /// per-name half of <see cref="WriteException"/>, which fails them all.
    /// Throw from it to fail that write.
    /// </summary>
    public Action<string>? OnWrite { get; set; }

    /// <summary>Every active-slot payload successfully written, in order.</summary>
    public List<string> Writes { get; } = [];

    /// <summary>The file name of every active-slot read/write, in call order — the filename-SSOT pin.</summary>
    public List<string> ActiveFileNames { get; } = [];

    /// <summary>
    /// Canonical saved-filters content (<see cref="SavedFiltersDocument.FileName"/>)
    /// the picked slot serves; null = no such file.
    /// </summary>
    public string? FiltersJson { get; set; }

    /// <summary>
    /// Legacy saved-filters content (<see cref="SavedFiltersDocument.LegacyFileName"/>)
    /// the picked slot serves; null = no such file. Read by the producer's store
    /// only when the canonical file is absent — staging this with
    /// <see cref="FiltersJson"/> null is how a test exercises the fallback.
    /// </summary>
    public string? LegacyFiltersJson { get; set; }

    /// <summary>
    /// Stats-document content the <b>picked</b> slot serves under
    /// <see cref="QuizStatsFile.FileName"/>; null = no such file. This is what
    /// the mix predicate's pick-time probe
    /// (<c>QuizStatsStore.RefreshPickedStatsAsync</c>) reads, and it is
    /// deliberately a separate slot from <see cref="StatsJson"/>: that one is
    /// the <i>active</i> slot a running quiz records through, and a test must
    /// be able to give the two different content — that divergence is exactly
    /// the reachable refusal case (the pick looked capable, the bind then
    /// didn't).
    /// </summary>
    public string? PickedStatsJson { get; set; }

    /// <summary>When set, <see cref="ReadPickedFileAsync"/> throws it instead (the read-failed / denied path).</summary>
    public Exception? FiltersReadException { get; set; }

    /// <summary>When set, <see cref="WritePickedFileAsync"/> throws it (after recording nothing).</summary>
    public Exception? FiltersWriteException { get; set; }

    /// <summary>Every picked-slot payload successfully written, in order.</summary>
    public List<string> FiltersWrites { get; } = [];

    /// <summary>The file name of every picked-slot write, in call order — pins that saves target the canonical name.</summary>
    public List<string> PickedWriteNames { get; } = [];

    public int PromoteCallCount { get; private set; }
    public int TriggerFallbackCallCount { get; private set; }
    public int ClearPickedCallCount { get; private set; }

    public ValueTask<bool> SupportsDirectoryPickerAsync() => ValueTask.FromResult(SupportsDirectoryPicker);

    /// <summary>
    /// Mirrors the real call's shape, which is what the busy affordance depends
    /// on: prompts first (<see cref="OnPickCalled"/>), then — only when a folder
    /// was actually granted — the <c>onPickAccepted</c> hook, then the scan
    /// (<see cref="OnScanning"/>). <see cref="PickException"/> throws at the top,
    /// standing in for a failure during the prompts; a test wanting a failure
    /// during the <i>scan</i> throws from <see cref="OnScanning"/>.
    /// </summary>
    public async Task<FolderPickOutcome> PickFolderAsync(Func<Task> onPickAccepted)
    {
        ArgumentNullException.ThrowIfNull(onPickAccepted);
        OnPickCalled?.Invoke();
        if (PickException is { } ex) throw ex;

        if (!NextPickOutcome.Cancelled)
        {
            await onPickAccepted();
            OnScanning?.Invoke();
        }

        return NextPickOutcome;
    }

    public Task TriggerFallbackPickerAsync(ElementReference fallbackInput)
    {
        TriggerFallbackCallCount++;
        return Task.CompletedTask;
    }

    public Task<FolderPickOutcome> CollectFallbackAsync(ElementReference fallbackInput) =>
        PickException is { } ex ? Task.FromException<FolderPickOutcome>(ex) : Task.FromResult(NextCollectOutcome);

    public ValueTask<bool> PromoteToActiveAsync()
    {
        PromoteCallCount++;
        return ValueTask.FromResult(PromoteResult);
    }

    /// <summary>
    /// Serves the picked slot's three documents. <see cref="FiltersReadException"/>
    /// is scoped to the <i>saved-filters</i> names, matching what it is named
    /// for: a test staging a saved-filters read failure is saying nothing about
    /// the stats document, and letting it fail that read too would silently
    /// hide the mix panel in every such scenario.
    /// </summary>
    public Task<string?> ReadPickedFileAsync(string fileName)
    {
        if (FiltersReadException is { } ex
            && fileName is SavedFiltersDocument.FileName or SavedFiltersDocument.LegacyFileName)
        {
            return Task.FromException<string?>(ex);
        }
        return Task.FromResult(fileName switch
        {
            SavedFiltersDocument.FileName => FiltersJson,
            SavedFiltersDocument.LegacyFileName => LegacyFiltersJson,
            QuizStatsFile.FileName => PickedStatsJson,
            _ => null,
        });
    }

    public Task WritePickedFileAsync(string fileName, string json)
    {
        if (FiltersWriteException is { } ex) return Task.FromException(ex);
        PickedWriteNames.Add(fileName);
        FiltersWrites.Add(json);
        // Round-trip: a canonical write is readable back, as the real slot's is.
        if (fileName == SavedFiltersDocument.FileName) FiltersJson = json;
        return Task.CompletedTask;
    }

    public Task<string?> ReadActiveFileAsync(string fileName)
    {
        ActiveFileNames.Add(fileName);
        return ReadException is { } ex
            ? Task.FromException<string?>(ex)
            : Task.FromResult(_activeFiles.GetValueOrDefault(fileName));
    }

    public Task WriteActiveFileAsync(string fileName, string json)
    {
        if (WriteException is { } ex) return Task.FromException(ex);
        OnWrite?.Invoke(fileName);
        ActiveFileNames.Add(fileName);
        Writes.Add(json);
        // Round-trip, as the real slot does: what was written is what a later
        // read of that name returns. The pre-write guard's whole behaviour is
        // invisible against a slot that forgets its writes.
        _activeFiles[fileName] = json;
        return Task.CompletedTask;
    }

    public ValueTask ClearPickedAsync()
    {
        ClearPickedCallCount++;
        return ValueTask.CompletedTask;
    }
}
