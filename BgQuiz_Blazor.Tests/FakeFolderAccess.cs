using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Scriptable in-memory <see cref="IFolderAccess"/> for store and page tests:
/// every browser-side behavior (pick outcome, promote verdict, stats-file
/// content, write failure) is a settable property, and every side-effectful
/// call is recorded so tests can assert exactly what the app drove.
/// </summary>
internal sealed class FakeFolderAccess : IFolderAccess
{
    /// <summary>What <see cref="SupportsDirectoryPickerAsync"/> reports (default: FS-Access available).</summary>
    public bool SupportsDirectoryPicker { get; set; } = true;

    /// <summary>Outcome the next <see cref="PickFolderAsync"/> returns (default: cancelled).</summary>
    public FolderPickOutcome NextPickOutcome { get; set; } = FolderPickOutcome.CancelledOutcome;

    /// <summary>Outcome the next <see cref="CollectFallbackAsync"/> returns (default: empty non-cancelled).</summary>
    public FolderPickOutcome NextCollectOutcome { get; set; } =
        new(Cancelled: false, DirectoryName: "", Files: [], StatsSaveCapability.BrowserUnsupported);

    /// <summary>When set, <see cref="PickFolderAsync"/> / <see cref="CollectFallbackAsync"/> throw it instead.</summary>
    public Exception? PickException { get; set; }

    /// <summary>What <see cref="PromoteToActiveAsync"/> returns (default: an FS-Access handle is active).</summary>
    public bool PromoteResult { get; set; } = true;

    /// <summary>Stats-file content <see cref="ReadStatsJsonAsync"/> returns; null = no file yet.</summary>
    public string? StatsJson { get; set; }

    /// <summary>When set, <see cref="ReadStatsJsonAsync"/> throws it instead.</summary>
    public Exception? ReadException { get; set; }

    /// <summary>When set, <see cref="WriteStatsJsonAsync"/> throws it (after recording nothing).</summary>
    public Exception? WriteException { get; set; }

    /// <summary>Every payload successfully written, in order.</summary>
    public List<string> Writes { get; } = [];

    public int PromoteCallCount { get; private set; }
    public int TriggerFallbackCallCount { get; private set; }
    public int ClearPickedCallCount { get; private set; }

    public ValueTask<bool> SupportsDirectoryPickerAsync() => ValueTask.FromResult(SupportsDirectoryPicker);

    public Task<FolderPickOutcome> PickFolderAsync() =>
        PickException is { } ex ? Task.FromException<FolderPickOutcome>(ex) : Task.FromResult(NextPickOutcome);

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

    public Task<string?> ReadStatsJsonAsync() =>
        ReadException is { } ex ? Task.FromException<string?>(ex) : Task.FromResult(StatsJson);

    public Task WriteStatsJsonAsync(string json)
    {
        if (WriteException is { } ex) return Task.FromException(ex);
        Writes.Add(json);
        return Task.CompletedTask;
    }

    public ValueTask ClearPickedAsync()
    {
        ClearPickedCallCount++;
        return ValueTask.CompletedTask;
    }
}
