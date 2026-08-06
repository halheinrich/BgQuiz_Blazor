namespace BgQuiz_Blazor.Client.Quiz;

using BgFolderAccess_Razor;
using Microsoft.JSInterop;
using XgFilter_Razor;

/// <summary>
/// The one-line adapter glue the two producer libraries deliberately don't ship
/// (they reference each other in neither direction): XgFilter_Razor's
/// <see cref="IFilterDocumentStorage"/> seam over BgFolderAccess_Razor's
/// <b>picked-slot</b> file I/O. <c>FilterSurface</c>'s composite-owned
/// <c>SavedFiltersStore</c> reads and writes the saved-filters document through
/// this, so the document lives beside the corpus in the user's picked folder —
/// and never touches the <i>active</i> slot a running quiz records stats
/// through.
///
/// <para>
/// Lifetime: <b>Scoped</b>, like <see cref="IFolderAccess"/> it wraps. A stable
/// instance matters beyond economy: <c>FilterSurface</c> rebuilds its store when
/// the bound <c>Storage</c> <i>reference</i> changes, so the adapter identity
/// changing per render would churn the store. <c>Home</c> passes this instance
/// while the pick's capability exposes a readable directory handle
/// (<see cref="FolderWriteCapability.Enabled"/> /
/// <see cref="FolderWriteCapability.PermissionDenied"/>) and <c>null</c>
/// otherwise — a fallback pick has no handle to read a document from, which the
/// composite renders as "no saved-filters section at all".
/// </para>
///
/// <para>
/// <b>Error translation is the whole job.</b> The store's degrade-never-block
/// posture rides on a typed catch: adapters must signal "the I/O failed" as
/// <see cref="FilterStorageException"/> and nothing else, so every
/// <see cref="JSException"/> — the lib's stated unexpected-browser-failure
/// surface — is wrapped here. An absent document is already a <c>null</c> read
/// on both sides of the seam, never an exception, so it passes through
/// untranslated.
/// </para>
/// </summary>
internal sealed class PickedFolderFilterStorage : IFilterDocumentStorage
{
    private readonly IFolderAccess _folderAccess;

    public PickedFolderFilterStorage(IFolderAccess folderAccess)
    {
        _folderAccess = folderAccess ?? throw new ArgumentNullException(nameof(folderAccess));
    }

    /// <inheritdoc/>
    public async Task<string?> ReadAsync(string fileName)
    {
        try
        {
            return await _folderAccess.ReadPickedFileAsync(fileName);
        }
        catch (JSException ex)
        {
            throw new FilterStorageException($"Reading '{fileName}' from the picked folder failed.", ex);
        }
    }

    /// <inheritdoc/>
    public async Task WriteAsync(string fileName, string json)
    {
        try
        {
            await _folderAccess.WritePickedFileAsync(fileName, json);
        }
        catch (JSException ex)
        {
            throw new FilterStorageException($"Writing '{fileName}' into the picked folder failed.", ex);
        }
    }
}
