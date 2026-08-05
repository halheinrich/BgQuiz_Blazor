namespace BgQuiz_Blazor.Client.Quiz;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

/// <summary>
/// The JS-backed <see cref="IFolderAccess"/>: a thin, typed facade over the
/// <c>folderAccess.js</c> ES module. The one type in the app that holds an
/// <see cref="IJSObjectReference"/> — everything above it (pages, the stats
/// store) sees only the interface, and the browser-side directory handles
/// never leave the module's own state.
///
/// <para>
/// Lifetime: <b>Scoped</b> (one per tab, like the holders). The module import
/// is lazy and cached — first use pays the fetch, later calls reuse it.
/// </para>
///
/// <para>
/// Caps are enforced against the pick's metadata, <i>before</i> any bytes move,
/// and the two caps end differently. A file larger than
/// <see cref="PickedFileLimits.MaxFileBytes"/> fails the whole pick here with an
/// <see cref="InvalidOperationException"/> Home surfaces as its pick-error
/// banner — mirroring the old <c>InputFile</c> path, where
/// <c>GetMultipleFiles</c> / <c>OpenReadStream</c> threw the same way. The
/// per-extension <i>count</i> caps
/// (<see cref="PickedFileLimits.MaxFileCounts"/>) instead <b>truncate</b>: they
/// are applied JS-side, where the extension is known before any transfer, and
/// what they left behind rides back as
/// <see cref="FolderPickOutcome.Truncations"/> for Home to report (issue #59).
/// This type's part in that is to hand the caps table down and map the reply —
/// it holds no cap logic of its own. The per-file byte transfer additionally
/// passes the byte cap to <see cref="IJSStreamReference.OpenReadStreamAsync"/>
/// as a belt-and-braces bound on what actually crosses the boundary.
/// </para>
/// </summary>
internal sealed class JsFolderAccess : IFolderAccess, IAsyncDisposable
{
    private const string ModulePath = "./js/folderAccess.js";

    private readonly IJSRuntime _js;
    private Task<IJSObjectReference>? _module;

    public JsFolderAccess(IJSRuntime js)
    {
        _js = js ?? throw new ArgumentNullException(nameof(js));
    }

    // The wire DTOs are internal (not private) solely so the bUnit module tests
    // can construct scripted results; nothing outside this type and those tests
    // touches them.

    /// <summary>
    /// The first half of the pick as the JS module shapes it (camelCase on the
    /// wire): what the browser's prompts settled — cancelled or not, the folder
    /// name, and whether write was granted. Carries no files; the enumeration
    /// is the second call (see <see cref="PickFolderAsync"/>).
    /// </summary>
    internal sealed record JsPickStart(string Status, string DirectoryName, bool Writable);

    /// <summary>
    /// The enumeration result: the picked folder's top-level problem files,
    /// metadata only, already truncated to the caps table this call passed in —
    /// plus <see cref="JsOmittedFiles"/> for whatever that truncation dropped.
    /// </summary>
    internal sealed record JsEnumerateResult(JsPickedFile[] Files, JsOmittedFiles[] Omitted);

    /// <summary>The fallback-collection result — no status (nothing to cancel) and no writable claim.</summary>
    internal sealed record JsFallbackResult(string DirectoryName, JsPickedFile[] Files, JsOmittedFiles[] Omitted);

    /// <summary>One enumerated file's metadata, before its bytes are pulled.</summary>
    internal sealed record JsPickedFile(string Name, long Size);

    /// <summary>
    /// One kind's left-behind count as the module reports it. The <i>cap</i> is
    /// not on the wire: it came from this side in the first place, so
    /// <see cref="PickTruncation"/> derives it back rather than trusting a
    /// round-tripped copy.
    /// </summary>
    internal sealed record JsOmittedFiles(string Extension, int OmittedCount);

    private Task<IJSObjectReference> ModuleAsync() =>
        _module ??= _js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();

    public async ValueTask<bool> SupportsDirectoryPickerAsync()
    {
        var module = await ModuleAsync();
        return await module.InvokeAsync<bool>("supportsDirectoryPicker");
    }

    /// <summary>
    /// Two module calls behind one method, so the stateful half-picked slot the
    /// split creates never escapes this type: <c>beginPick</c> runs the browser's
    /// prompts, then — with <paramref name="onPickAccepted"/> awaited in
    /// between, which is what lets a caller paint before the wait —
    /// <c>enumeratePicked</c> lists the folder and its files are buffered across.
    /// </summary>
    public async Task<FolderPickOutcome> PickFolderAsync(Func<Task> onPickAccepted)
    {
        ArgumentNullException.ThrowIfNull(onPickAccepted);

        var module = await ModuleAsync();
        var start = await module.InvokeAsync<JsPickStart>("beginPick");
        if (start.Status == "cancelled")
        {
            return FolderPickOutcome.CancelledOutcome;
        }

        // The prompts are done and the app's own work starts now. Everything
        // below is the "no feedback" stretch the hook exists to cover.
        await onPickAccepted();

        var enumerated = await module.InvokeAsync<JsEnumerateResult>(
            "enumeratePicked", PickedFileLimits.MaxFileCounts);
        var files = await BufferFilesAsync(module, enumerated.Files);
        var capability = start.Writable ? StatsSaveCapability.Enabled : StatsSaveCapability.PermissionDenied;
        return new FolderPickOutcome(
            Cancelled: false, start.DirectoryName, files, capability, ToTruncations(enumerated.Omitted));
    }

    public async Task TriggerFallbackPickerAsync(ElementReference fallbackInput)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("clickElement", fallbackInput);
    }

    public async Task<FolderPickOutcome> CollectFallbackAsync(ElementReference fallbackInput)
    {
        var module = await ModuleAsync();
        var result = await module.InvokeAsync<JsFallbackResult>(
            "collectFallbackFiles", fallbackInput, PickedFileLimits.MaxFileCounts);
        var files = await BufferFilesAsync(module, result.Files);
        return new FolderPickOutcome(
            Cancelled: false, result.DirectoryName, files, StatsSaveCapability.BrowserUnsupported,
            ToTruncations(result.Omitted));
    }

    /// <summary>
    /// Map the module's per-kind left-behind report into
    /// <see cref="PickTruncation"/>s — the module's one shape for both
    /// mechanisms, so this mapping is written once. Order is the module's, which
    /// is <see cref="PickedFileLimits.MaxFileCounts"/>'s, so a two-kind notice
    /// reads the same way every time.
    /// </summary>
    private static IReadOnlyList<PickTruncation> ToTruncations(JsOmittedFiles[] omitted) =>
        [.. omitted.Select(o => new PickTruncation(o.Extension, o.OmittedCount))];

    public async ValueTask<bool> PromoteToActiveAsync()
    {
        var module = await ModuleAsync();
        return await module.InvokeAsync<bool>("promoteToActive");
    }

    public async Task<string?> ReadStatsJsonAsync()
    {
        var module = await ModuleAsync();
        return await module.InvokeAsync<string?>("readStatsFile", QuizStatsFile.FileName);
    }

    public async Task WriteStatsJsonAsync(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("writeStatsFile", QuizStatsFile.FileName, json);
    }

    public async Task<string?> ReadFiltersJsonAsync()
    {
        var module = await ModuleAsync();
        return await module.InvokeAsync<string?>("readPickedFile", QuizFiltersFile.FileName);
    }

    public async Task WriteFiltersJsonAsync(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("writePickedFile", QuizFiltersFile.FileName, json);
    }

    public async ValueTask ClearPickedAsync()
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("clearPicked");
    }

    /// <summary>
    /// Pull every enumerated file's bytes across the boundary into
    /// <see cref="PickedFile"/>s, size-checking the metadata first so an
    /// oversized file fails fast before any transfer starts. The count caps have
    /// already been applied JS-side — what arrives here is what the pick took.
    /// </summary>
    private static async Task<IReadOnlyList<PickedFile>> BufferFilesAsync(
        IJSObjectReference module, JsPickedFile[] metadata)
    {
        foreach (var file in metadata)
        {
            if (file.Size > PickedFileLimits.MaxFileBytes)
            {
                throw new InvalidOperationException(
                    $"'{file.Name}' is larger than the {PickedFileLimits.MaxFileMegabytes} MB per-file limit.");
            }
        }

        var picked = new List<PickedFile>(metadata.Length);
        foreach (var file in metadata)
        {
            // Stream the bytes rather than marshaling one giant byte[] result:
            // IJSStreamReference is the supported large-payload path, and its
            // maxAllowedSize re-asserts the byte cap on what actually crosses.
            var streamRef = await module.InvokeAsync<IJSStreamReference>("readFileData", file.Name);
            await using var stream = await streamRef.OpenReadStreamAsync(
                maxAllowedSize: PickedFileLimits.MaxFileBytes);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            // file.Name carries the extension — required by the stream
            // iterator's DecisionId stamping (see XgFileStream).
            picked.Add(new PickedFile(file.Name, ms.ToArray()));
        }

        return picked;
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;
        try
        {
            var module = await _module;
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // Runtime already torn down (tab close / reload) — nothing to release.
        }
    }
}
