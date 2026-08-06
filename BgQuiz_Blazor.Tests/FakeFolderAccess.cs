using BgFolderAccess_Razor;
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
/// the <b>active</b> slot serves only the stats document
/// (<see cref="StatsJson"/> / <see cref="Writes"/> — the fake also records the
/// names, so a test can pin the filename SSOT), and the <b>picked</b> slot
/// serves the saved-filters document under the producer's two names —
/// <see cref="FiltersJson"/> is the canonical
/// <see cref="SavedFiltersDocument.FileName"/> content and
/// <see cref="LegacyFiltersJson"/> the
/// <see cref="SavedFiltersDocument.LegacyFileName"/> content, so a host test
/// can stage the read-fallback exactly. Any other picked name reads as absent.
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

    /// <summary>Stats-file content <see cref="ReadActiveFileAsync"/> returns; null = no file yet.</summary>
    public string? StatsJson { get; set; }

    /// <summary>When set, <see cref="ReadActiveFileAsync"/> throws it instead.</summary>
    public Exception? ReadException { get; set; }

    /// <summary>When set, <see cref="WriteActiveFileAsync"/> throws it (after recording nothing).</summary>
    public Exception? WriteException { get; set; }

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

    public Task<string?> ReadPickedFileAsync(string fileName)
    {
        if (FiltersReadException is { } ex) return Task.FromException<string?>(ex);
        return Task.FromResult(fileName switch
        {
            SavedFiltersDocument.FileName => FiltersJson,
            SavedFiltersDocument.LegacyFileName => LegacyFiltersJson,
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
        return ReadException is { } ex ? Task.FromException<string?>(ex) : Task.FromResult(StatsJson);
    }

    public Task WriteActiveFileAsync(string fileName, string json)
    {
        if (WriteException is { } ex) return Task.FromException(ex);
        ActiveFileNames.Add(fileName);
        Writes.Add(json);
        return Task.CompletedTask;
    }

    public ValueTask ClearPickedAsync()
    {
        ClearPickedCallCount++;
        return ValueTask.CompletedTask;
    }
}
