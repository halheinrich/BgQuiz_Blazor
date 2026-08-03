using BgQuiz_Blazor.Client.Quiz;
using Bunit;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The C# half of the interop seam: <see cref="JsFolderAccess"/>'s mapping of
/// the JS module's results into <see cref="FolderPickOutcome"/>s, driven
/// through bUnit's module interop (<c>SetupModule</c>) — no real JS runs. The
/// JS half (and the real byte path) is covered by the e2e suite; these tests
/// pin the result-shape contract: cancelled is a value not an exception,
/// writable maps to capability, caps fail the pick before bytes move, and the
/// stats filename is passed in from the constant.
/// </summary>
public class JsFolderAccessTests : BunitContext
{
    private const string ModulePath = "./js/folderAccess.js";

    /// <summary>Minimal <see cref="IJSStreamReference"/> over in-memory bytes.</summary>
    private sealed class FakeJsStreamReference(byte[] bytes) : IJSStreamReference
    {
        public long Length => bytes.Length;

        public ValueTask<Stream> OpenReadStreamAsync(
            long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new MemoryStream(bytes));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Script <c>beginPick</c>'s reply — the browser-prompt half of the pick.</summary>
    private void SetupBeginPick(BunitJSModuleInterop module, string status, string directoryName, bool writable) =>
        module.Setup<JsFolderAccess.JsPickStart>("beginPick")
            .SetResult(new JsFolderAccess.JsPickStart(status, directoryName, writable));

    /// <summary>Script <c>enumeratePicked</c>'s reply — the app-work half.</summary>
    private void SetupEnumerate(BunitJSModuleInterop module, params JsFolderAccess.JsPickedFile[] files) =>
        module.Setup<JsFolderAccess.JsEnumerateResult>("enumeratePicked")
            .SetResult(new JsFolderAccess.JsEnumerateResult(files));

    /// <summary>A hook that records nothing — for the cases the hook isn't what's under test.</summary>
    private static Func<Task> NoHook => () => Task.CompletedTask;

    [Fact]
    public async Task PickFolder_CancelledStatus_MapsToCancelledOutcomeNotException()
    {
        var module = JSInterop.SetupModule(ModulePath);
        SetupBeginPick(module, "cancelled", "", false);
        var sut = new JsFolderAccess(JSInterop.JSRuntime);

        var outcome = await sut.PickFolderAsync(NoHook);

        Assert.True(outcome.Cancelled);
        Assert.Empty(outcome.Files);
    }

    [Fact]
    public async Task PickFolder_Cancelled_NeverRunsTheAcceptedHook()
    {
        // No folder, no work — so nothing may raise a busy affordance for work
        // that will not happen. (No enumeratePicked setup exists either: a pick
        // that enumerated after a cancellation would fail this test loudly.)
        var module = JSInterop.SetupModule(ModulePath);
        SetupBeginPick(module, "cancelled", "", false);
        var sut = new JsFolderAccess(JSInterop.JSRuntime);
        var hookRan = false;

        await sut.PickFolderAsync(() => { hookRan = true; return Task.CompletedTask; });

        Assert.False(hookRan);
    }

    [Fact]
    public async Task PickFolder_RunsTheAcceptedHook_AfterThePromptsAndBeforeTheScan()
    {
        // The seam issue #48 turns on: the hook must land after beginPick (the
        // browser is done asking) and before enumeratePicked (the app's work
        // that leaves the user waiting). Pinned by call order, because that
        // ordering — not the hook's existence — is what makes the busy state
        // both truthful and paintable.
        var module = JSInterop.SetupModule(ModulePath);
        SetupBeginPick(module, "ok", "Corpus", true);
        SetupEnumerate(module);
        var sut = new JsFolderAccess(JSInterop.JSRuntime);

        var hookRan = false;
        await sut.PickFolderAsync(() =>
        {
            hookRan = true;
            // Asserted from inside the hook — the only place the "after the
            // prompts, before the scan" position is observable.
            Assert.Single(module.Invocations["beginPick"]);
            Assert.Empty(module.Invocations["enumeratePicked"]);
            return Task.CompletedTask;
        });

        Assert.True(hookRan); // an unrun hook would vacuously pass the above
        Assert.Single(module.Invocations["enumeratePicked"]);
    }

    [Fact]
    public async Task PickFolder_WritableDenied_BuffersBytesAndMapsPermissionDenied()
    {
        // writable=false from the module (readwrite request not granted) must
        // surface as PermissionDenied while the files still buffer normally —
        // the quiz-without-stats rung, not a failure.
        var module = JSInterop.SetupModule(ModulePath);
        SetupBeginPick(module, "ok", "Corpus", writable: false);
        SetupEnumerate(module, new JsFolderAccess.JsPickedFile("match.xg", 3));
        module.Setup<IJSStreamReference>("readFileData", inv => true)
            .SetResult(new FakeJsStreamReference([1, 2, 3]));
        var sut = new JsFolderAccess(JSInterop.JSRuntime);

        var outcome = await sut.PickFolderAsync(NoHook);

        Assert.False(outcome.Cancelled);
        Assert.Equal(StatsSaveCapability.PermissionDenied, outcome.Capability);
        Assert.Equal("Corpus", outcome.DirectoryName);
        var file = Assert.Single(outcome.Files);
        Assert.Equal("match.xg", file.FileName); // extension-bearing name preserved
        Assert.Equal([1, 2, 3], file.Bytes);
    }

    [Fact]
    public async Task PickFolder_WritableGranted_MapsEnabled()
    {
        var module = JSInterop.SetupModule(ModulePath);
        SetupBeginPick(module, "ok", "Corpus", writable: true);
        SetupEnumerate(module, new JsFolderAccess.JsPickedFile("a.xgp", 1));
        module.Setup<IJSStreamReference>("readFileData", inv => true)
            .SetResult(new FakeJsStreamReference([7]));
        var sut = new JsFolderAccess(JSInterop.JSRuntime);

        var outcome = await sut.PickFolderAsync(NoHook);

        Assert.Equal(StatsSaveCapability.Enabled, outcome.Capability);
    }

    [Fact]
    public async Task PickFolder_TooManyFiles_FailsBeforeAnyByteTransfer()
    {
        // The count cap is enforced against metadata: no readFileData setup
        // exists, so any attempted transfer would fail the test — the throw
        // must come first.
        var tooMany = Enumerable.Range(0, PickedFileLimits.MaxFileCount + 1)
            .Select(i => new JsFolderAccess.JsPickedFile($"f{i}.xg", 1))
            .ToArray();
        var module = JSInterop.SetupModule(ModulePath);
        SetupBeginPick(module, "ok", "Huge", true);
        SetupEnumerate(module, tooMany);
        var sut = new JsFolderAccess(JSInterop.JSRuntime);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.PickFolderAsync(NoHook));
        Assert.Contains($"{PickedFileLimits.MaxFileCount}", ex.Message);
    }

    [Fact]
    public async Task PickFolder_OversizedFile_FailsBeforeAnyByteTransfer()
    {
        var module = JSInterop.SetupModule(ModulePath);
        SetupBeginPick(module, "ok", "Corpus", true);
        SetupEnumerate(module,
            new JsFolderAccess.JsPickedFile("huge.xg", PickedFileLimits.MaxFileBytes + 1));
        var sut = new JsFolderAccess(JSInterop.JSRuntime);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.PickFolderAsync(NoHook));
        Assert.Contains("huge.xg", ex.Message);
    }

    [Fact]
    public async Task StatsReadAndWrite_PassTheFileNameConstantToJs()
    {
        // SSOT: JS never hardcodes the stats filename — both calls carry
        // QuizStatsFile.FileName from the C# side.
        var module = JSInterop.SetupModule(ModulePath);
        module.Setup<string?>("readStatsFile", inv => true).SetResult(null);
        module.SetupVoid("writeStatsFile", inv => true).SetVoidResult();
        var sut = new JsFolderAccess(JSInterop.JSRuntime);

        await sut.ReadStatsJsonAsync();
        await sut.WriteStatsJsonAsync("{}");

        var read = module.VerifyInvoke("readStatsFile");
        Assert.Equal(QuizStatsFile.FileName, read.Arguments[0]);
        var write = module.VerifyInvoke("writeStatsFile");
        Assert.Equal(QuizStatsFile.FileName, write.Arguments[0]);
        Assert.Equal("{}", write.Arguments[1]);
    }

    [Fact]
    public async Task FiltersReadAndWrite_PassTheFileNameConstantToJs()
    {
        // SSOT, filters edition: the saved-filters read/write ride the picked-slot
        // JS ops and both carry QuizFiltersFile.FileName from the C# side — the JS
        // module never hardcodes it (the same pin as the stats pair above).
        var module = JSInterop.SetupModule(ModulePath);
        module.Setup<string?>("readPickedFile", inv => true).SetResult(null);
        module.SetupVoid("writePickedFile", inv => true).SetVoidResult();
        var sut = new JsFolderAccess(JSInterop.JSRuntime);

        await sut.ReadFiltersJsonAsync();
        await sut.WriteFiltersJsonAsync("{}");

        var read = module.VerifyInvoke("readPickedFile");
        Assert.Equal(QuizFiltersFile.FileName, read.Arguments[0]);
        var write = module.VerifyInvoke("writePickedFile");
        Assert.Equal(QuizFiltersFile.FileName, write.Arguments[0]);
        Assert.Equal("{}", write.Arguments[1]);
    }
}
