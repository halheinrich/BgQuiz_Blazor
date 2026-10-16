namespace BgQuiz_Blazor.Client.Quiz;

using BgFolderAccess_Razor;
using Microsoft.JSInterop;
using XgFilter_Razor;

/// <summary>
/// XgFilter_Razor's <see cref="IDocumentStorage"/> seam over one picked folder, the adapter
/// glue the two libraries deliberately do not ship: saved filters live beside the corpus in
/// the folder the setup was picked from. Its whole job is the error translation: the store
/// degrades on <see cref="DocumentStorageException"/> and nothing else, so every
/// <see cref="JSException"/> is wrapped; an absent document is a <c>null</c> read on both
/// sides and passes through. One per setup, since the filter surface rebuilds its store when
/// the storage reference changes.
/// </summary>
internal sealed class FolderDocumentStorage(IFolderContext folder) : IDocumentStorage
{
    /// <inheritdoc/>
    public async Task<string?> ReadAsync(string fileName)
    {
        try
        {
            return await folder.ReadFileAsync(fileName);
        }
        catch (JSException ex)
        {
            throw new DocumentStorageException($"Reading '{fileName}' from the picked folder failed.", ex);
        }
    }

    /// <inheritdoc/>
    public async Task WriteAsync(string fileName, string json)
    {
        try
        {
            await folder.WriteFileAsync(fileName, json);
        }
        catch (JSException ex)
        {
            throw new DocumentStorageException($"Writing '{fileName}' into the picked folder failed.", ex);
        }
    }
}
