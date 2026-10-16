using BgFolderAccess_Razor;
using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Scriptable in-memory <see cref="IFolderAccess"/> for the coordinator and page tests: the
/// outcome each pick mechanism returns (its <see cref="FakeFolderContext"/> and sample), the
/// failures a pick can meet before and after landing, the observation points a test asserts
/// from, and the record of every folder landed, so a test can see which were released.
/// </summary>
internal sealed class FakeFolderAccess : IFolderAccess
{
    /// <summary>What <see cref="SupportsDirectoryPickerAsync"/> reports (default: FS-Access available).</summary>
    public bool SupportsDirectoryPicker { get; set; } = true;

    /// <summary>Outcome the next <see cref="PickFolderAsync"/> returns (default: cancelled).</summary>
    public FolderPickOutcome NextPickOutcome { get; set; } = FolderPickOutcome.CancelledOutcome;

    /// <summary>Outcome the next <see cref="CollectFallbackAsync"/> returns (default: an empty folder, no handle).</summary>
    public FolderPickOutcome NextCollectOutcome { get; set; } =
        new(new FakeFolderContext("", FolderWriteCapability.BrowserUnsupported), TestFixtures.Sample([]));

    /// <summary>When set, a pick or a collection throws it before any folder lands, as a failure in the prompts does.</summary>
    public Exception? PickException { get; set; }

    /// <summary>
    /// When set, <see cref="CollectFallbackAsync"/> throws it after the folder landed, as a
    /// failed draw does; the library releases the folder, and so does this. The File System
    /// Access mechanism's counterpart is a throw from <see cref="OnScanning"/>.
    /// </summary>
    public Exception? CollectScanException { get; set; }

    /// <summary>Invoked at the top of <see cref="PickFolderAsync"/>: the instant the browser's prompts go up.</summary>
    public Action? OnPickCalled { get; set; }

    /// <summary>
    /// Invoked inside <see cref="PickFolderAsync"/> after the busy hook, from the stretch the
    /// real pick spends drawing; the seam a test throws from to fail a pick after its prompts.
    /// Never invoked for a cancelled pick.
    /// </summary>
    public Action? OnScanning { get; set; }

    /// <summary>Awaited while the browser's prompts are up, so a test can hold a pick there.</summary>
    public Func<Task>? OnPrompts { get; set; }

    /// <summary>Awaited while a pick or a collection draws, after the folder landed.</summary>
    public Func<Task>? OnDrawing { get; set; }

    public int PickCallCount { get; private set; }
    public int CollectCallCount { get; private set; }
    public int TriggerFallbackCallCount { get; private set; }

    /// <summary>Every folder a pick or a collection landed, in order.</summary>
    public List<FakeFolderContext> Landed { get; } = [];

    public ValueTask<bool> SupportsDirectoryPickerAsync() => ValueTask.FromResult(SupportsDirectoryPicker);

    /// <summary>
    /// Mirrors the real call's shape: the prompts (<see cref="OnPickCalled"/>, <see cref="OnPrompts"/>),
    /// then, for a folder the browser granted, the landing, the busy hook and the draw
    /// (<see cref="OnScanning"/>, <see cref="OnDrawing"/>); a draw that throws releases the
    /// landed folder, as the library does.
    /// </summary>
    public async Task<FolderPickOutcome> PickFolderAsync(Func<Task> onPickAccepted)
    {
        ArgumentNullException.ThrowIfNull(onPickAccepted);
        PickCallCount++;
        OnPickCalled?.Invoke();
        if (OnPrompts is { } prompts) await prompts();
        if (PickException is { } ex) throw ex;

        if (NextPickOutcome.Folder is FakeFolderContext folder)
        {
            Landed.Add(folder);
            try
            {
                await onPickAccepted();
                OnScanning?.Invoke();
                if (OnDrawing is { } drawing) await drawing();
            }
            catch
            {
                await folder.DisposeAsync();
                throw;
            }
        }

        return NextPickOutcome;
    }

    public Task TriggerFallbackPickerAsync(ElementReference fallbackInput)
    {
        TriggerFallbackCallCount++;
        return Task.CompletedTask;
    }

    public async Task<FolderPickOutcome> CollectFallbackAsync(ElementReference fallbackInput)
    {
        CollectCallCount++;
        if (PickException is { } ex) throw ex;
        var folder = (FakeFolderContext)NextCollectOutcome.Folder!;
        Landed.Add(folder);
        try
        {
            if (OnDrawing is { } drawing) await drawing();
            if (CollectScanException is { } scan) throw scan;
        }
        catch
        {
            await folder.DisposeAsync();
            throw;
        }

        return NextCollectOutcome;
    }
}
