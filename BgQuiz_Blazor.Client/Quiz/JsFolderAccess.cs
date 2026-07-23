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
/// Caps are enforced here, against the pick's metadata, <i>before</i> any
/// bytes move: a folder with more matching files than
/// <see cref="PickedFileLimits.MaxFileCount"/> or a file larger than
/// <see cref="PickedFileLimits.MaxFileBytes"/> fails the whole pick with an
/// <see cref="InvalidOperationException"/> Home surfaces as its pick-error
/// banner — mirroring the old <c>InputFile</c> path, where
/// <c>GetMultipleFiles</c> / <c>OpenReadStream</c> threw the same way. The
/// per-file byte transfer additionally passes the byte cap to
/// <see cref="IJSStreamReference.OpenReadStreamAsync"/> as a belt-and-braces
/// bound on what actually crosses the boundary.
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

    /// <summary>The pick result as the JS module shapes it (camelCase on the wire).</summary>
    internal sealed record JsPickResult(string Status, string DirectoryName, bool Writable, JsPickedFile[] Files);

    /// <summary>The fallback-collection result — no status (nothing to cancel) and no writable claim.</summary>
    internal sealed record JsFallbackResult(string DirectoryName, JsPickedFile[] Files);

    /// <summary>One enumerated file's metadata, before its bytes are pulled.</summary>
    internal sealed record JsPickedFile(string Name, long Size);

    private Task<IJSObjectReference> ModuleAsync() =>
        _module ??= _js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();

    public async ValueTask<bool> SupportsDirectoryPickerAsync()
    {
        var module = await ModuleAsync();
        return await module.InvokeAsync<bool>("supportsDirectoryPicker");
    }

    public async Task<FolderPickOutcome> PickFolderAsync()
    {
        var module = await ModuleAsync();
        var result = await module.InvokeAsync<JsPickResult>("pickDirectory");
        if (result.Status == "cancelled")
        {
            return FolderPickOutcome.CancelledOutcome;
        }

        var files = await BufferFilesAsync(module, result.Files);
        var capability = result.Writable ? StatsSaveCapability.Enabled : StatsSaveCapability.PermissionDenied;
        return new FolderPickOutcome(Cancelled: false, result.DirectoryName, files, capability);
    }

    public async Task TriggerFallbackPickerAsync(ElementReference fallbackInput)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("clickElement", fallbackInput);
    }

    public async Task<FolderPickOutcome> CollectFallbackAsync(ElementReference fallbackInput)
    {
        var module = await ModuleAsync();
        var result = await module.InvokeAsync<JsFallbackResult>("collectFallbackFiles", fallbackInput);
        var files = await BufferFilesAsync(module, result.Files);
        return new FolderPickOutcome(
            Cancelled: false, result.DirectoryName, files, StatsSaveCapability.BrowserUnsupported);
    }

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
    /// <see cref="PickedFile"/>s, cap-checking the metadata first so an
    /// over-limit folder fails fast before any transfer starts.
    /// </summary>
    private static async Task<IReadOnlyList<PickedFile>> BufferFilesAsync(
        IJSObjectReference module, JsPickedFile[] metadata)
    {
        if (metadata.Length > PickedFileLimits.MaxFileCount)
        {
            throw new InvalidOperationException(
                $"The folder has {metadata.Length} .xg / .xgp files; " +
                $"at most {PickedFileLimits.MaxFileCount} are supported in one pick.");
        }

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
