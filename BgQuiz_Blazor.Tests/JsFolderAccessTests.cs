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

    [Fact]
    public async Task PickFolder_CancelledStatus_MapsToCancelledOutcomeNotException()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Setup<JsFolderAccess.JsPickResult>("pickDirectory")
            .SetResult(new JsFolderAccess.JsPickResult("cancelled", "", false, []));
        var sut = new JsFolderAccess(JSInterop.JSRuntime);

        var outcome = await sut.PickFolderAsync();

        Assert.True(outcome.Cancelled);
        Assert.Empty(outcome.Files);
    }

    [Fact]
    public async Task PickFolder_WritableDenied_BuffersBytesAndMapsPermissionDenied()
    {
        // writable=false from the module (readwrite request not granted) must
        // surface as PermissionDenied while the files still buffer normally —
        // the quiz-without-stats rung, not a failure.
        var module = JSInterop.SetupModule(ModulePath);
        module.Setup<JsFolderAccess.JsPickResult>("pickDirectory")
            .SetResult(new JsFolderAccess.JsPickResult(
                "ok", "Corpus", Writable: false, [new JsFolderAccess.JsPickedFile("match.xg", 3)]));
        module.Setup<IJSStreamReference>("readFileData", inv => true)
            .SetResult(new FakeJsStreamReference([1, 2, 3]));
        var sut = new JsFolderAccess(JSInterop.JSRuntime);

        var outcome = await sut.PickFolderAsync();

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
        module.Setup<JsFolderAccess.JsPickResult>("pickDirectory")
            .SetResult(new JsFolderAccess.JsPickResult(
                "ok", "Corpus", Writable: true, [new JsFolderAccess.JsPickedFile("a.xgp", 1)]));
        module.Setup<IJSStreamReference>("readFileData", inv => true)
            .SetResult(new FakeJsStreamReference([7]));
        var sut = new JsFolderAccess(JSInterop.JSRuntime);

        var outcome = await sut.PickFolderAsync();

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
        module.Setup<JsFolderAccess.JsPickResult>("pickDirectory")
            .SetResult(new JsFolderAccess.JsPickResult("ok", "Huge", true, tooMany));
        var sut = new JsFolderAccess(JSInterop.JSRuntime);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(sut.PickFolderAsync);
        Assert.Contains($"{PickedFileLimits.MaxFileCount}", ex.Message);
    }

    [Fact]
    public async Task PickFolder_OversizedFile_FailsBeforeAnyByteTransfer()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Setup<JsFolderAccess.JsPickResult>("pickDirectory")
            .SetResult(new JsFolderAccess.JsPickResult(
                "ok", "Corpus", true,
                [new JsFolderAccess.JsPickedFile("huge.xg", PickedFileLimits.MaxFileBytes + 1)]));
        var sut = new JsFolderAccess(JSInterop.JSRuntime);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(sut.PickFolderAsync);
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
}
