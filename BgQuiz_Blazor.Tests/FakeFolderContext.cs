using BgFolderAccess_Razor;
using XgFilter_Razor;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// An in-memory <see cref="IFolderContext"/>: one landed folder with a name-keyed file store
/// that round-trips its writes (the pre-write guard is invisible against a folder that forgets
/// them), scriptable failures, a scriptable writability answer, a scriptable resample, and
/// the record of what the app did with it: every payload written, every name read or written,
/// and whether it was released. After <see cref="DisposeAsync"/> every operation throws
/// <see cref="ObjectDisposedException"/>, as the real context does.
/// </summary>
internal sealed class FakeFolderContext(string name, FolderWriteCapability capability) : IFolderContext
{
    // The stats file names BgQuiz writes, as literals: this is the pin that the app's constants spell them so.
    internal const string StatsFileName = "bgquiz-stats.json";
    internal static string RetiredStatsName(int schemaVersion) => $"bgquiz-stats.v{schemaVersion}.json";
    internal static string MergedStatsName(int schemaVersion) => $"bgquiz-stats.v{schemaVersion}.merged.json";

    private readonly Dictionary<string, string> _files = [];

    public string Name => name;

    public FolderWriteCapability Capability => capability;

    /// <summary>The stats file's content; null removes it (an absent read).</summary>
    public string? StatsJson
    {
        get => _files.GetValueOrDefault(StatsFileName);
        set => SetFile(StatsFileName, value);
    }

    /// <summary>The content under the retired name for <paramref name="schemaVersion"/>; null = no such file.</summary>
    public string? RetiredStatsJson(int schemaVersion) => _files.GetValueOrDefault(RetiredStatsName(schemaVersion));

    /// <summary>Stage a set-aside file for retired <paramref name="schemaVersion"/>; null removes it.</summary>
    public void SetRetiredStatsJson(int schemaVersion, string? content) => SetFile(RetiredStatsName(schemaVersion), content);

    /// <summary>The content under the merged name for foldable <paramref name="schemaVersion"/>; null = no such file.</summary>
    public string? MergedStatsJson(int schemaVersion) => _files.GetValueOrDefault(MergedStatsName(schemaVersion));

    /// <summary>The canonical saved-filters content (<see cref="SavedFiltersDocument.FileName"/>); null = no such file.</summary>
    public string? FiltersJson
    {
        get => _files.GetValueOrDefault(SavedFiltersDocument.FileName);
        set => SetFile(SavedFiltersDocument.FileName, value);
    }

    /// <summary>The legacy saved-filters content (<see cref="SavedFiltersDocument.LegacyFileName"/>); null = no such file.</summary>
    public string? LegacyFiltersJson
    {
        get => _files.GetValueOrDefault(SavedFiltersDocument.LegacyFileName);
        set => SetFile(SavedFiltersDocument.LegacyFileName, value);
    }

    private void SetFile(string fileName, string? content)
    {
        if (content is null) _files.Remove(fileName);
        else _files[fileName] = content;
    }

    /// <summary>When set, every read throws it.</summary>
    public Exception? ReadException { get; set; }

    /// <summary>When set, every write throws it, before recording anything.</summary>
    public Exception? WriteException { get; set; }

    /// <summary>Called with the file name before a write lands; throw from it to fail that one write.</summary>
    public Action<string>? OnWrite { get; set; }

    /// <summary>Awaited before a write lands, so a test can hold the write there.</summary>
    public Func<Task>? OnWriting { get; set; }

    /// <summary>Every payload successfully written, in order.</summary>
    public List<string> Writes { get; } = [];

    /// <summary>The file name of every read and write, in call order.</summary>
    public List<string> FileNames { get; } = [];

    /// <summary>What the writability probe answers (default: the ordinary folder).</summary>
    public FileWritability Writability { get; set; } = FileWritability.Writable;

    /// <summary>When set, the writability probe throws it.</summary>
    public Exception? WritabilityProbeException { get; set; }

    /// <summary>The file name of every writability probe, in call order.</summary>
    public List<string> WritabilityProbeNames { get; } = [];

    /// <summary>What the next <see cref="ResampleAsync"/> returns (default: a draw holding nothing).</summary>
    public FolderSample NextResample { get; set; } = TestFixtures.Sample([]);

    /// <summary>When set, <see cref="ResampleAsync"/> throws it.</summary>
    public Exception? ResampleException { get; set; }

    public int ResampleCallCount { get; private set; }

    /// <summary>Awaited while a resample draws, so a test can hold it there.</summary>
    public Func<Task>? OnResampling { get; set; }

    /// <summary>Whether the app released this folder.</summary>
    public bool IsReleased { get; private set; }

    public int ReleaseCount { get; private set; }

    /// <summary>When set, the next release throws it, once, after marking the folder released.</summary>
    public Exception? ReleaseException { get; set; }

    public Task<string?> ReadFileAsync(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ObjectDisposedException.ThrowIf(IsReleased, this);
        FileNames.Add(fileName);
        return ReadException is { } ex
            ? Task.FromException<string?>(ex)
            : Task.FromResult(_files.GetValueOrDefault(fileName));
    }

    public Task WriteFileAsync(string fileName, string text)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(text);
        ObjectDisposedException.ThrowIf(IsReleased, this);
        if (WriteException is { } ex) return Task.FromException(ex);
        if (OnWriting is { } writing) return LandAfterAsync(writing, fileName, text);
        Land(fileName, text);
        return Task.CompletedTask;
    }

    private async Task LandAfterAsync(Func<Task> writing, string fileName, string text)
    {
        await writing();
        Land(fileName, text);
    }

    private void Land(string fileName, string text)
    {
        OnWrite?.Invoke(fileName);
        FileNames.Add(fileName);
        Writes.Add(text);
        _files[fileName] = text;
    }

    public Task<FileWritability> ProbeFileWritabilityAsync(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ObjectDisposedException.ThrowIf(IsReleased, this);
        WritabilityProbeNames.Add(fileName);
        return WritabilityProbeException is { } ex
            ? Task.FromException<FileWritability>(ex)
            : Task.FromResult(Writability);
    }

    public async Task<FolderSample> ResampleAsync()
    {
        ObjectDisposedException.ThrowIf(IsReleased, this);
        ResampleCallCount++;
        if (OnResampling is { } drawing) await drawing();
        if (ResampleException is { } ex) throw ex;
        return NextResample;
    }

    public ValueTask DisposeAsync()
    {
        if (IsReleased) return ValueTask.CompletedTask;
        IsReleased = true;
        ReleaseCount++;
        if (ReleaseException is { } ex)
        {
            ReleaseException = null;
            return ValueTask.FromException(ex);
        }

        return ValueTask.CompletedTask;
    }
}
