namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The one thing in this project that is about the fixture rather than the app:
/// <see cref="PublishedAppFixture.ResetPublishDirectory"/>, the step that makes
/// "the layer under test is the publish output" true of a directory reused run
/// after run. <c>dotnet publish -o</c> only ever copies in, and fingerprinted
/// assets are written under new names, so without the reset the directory
/// becomes the union of every publish that ever ran there.
///
/// <para>
/// Deliberately <b>not</b> in <see cref="E2eCollection"/>: it neither publishes
/// nor drives a browser, and it must never be handed the live publish directory
/// — the spawned host is running out of that one. It works on scratch
/// directories it owns, which is also why the reset takes its target as a
/// parameter.
/// </para>
/// </summary>
public sealed class PublishDirectoryResetTests : IDisposable
{
    /// <summary>Scratch root, removed whatever the tests leave behind.</summary>
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "bgquiz-publish-reset-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>
    /// The subject: what an earlier publish left behind is gone afterwards, and
    /// the directory the publish is about to write into still exists. The planted
    /// file is shaped like the real leftover — a fingerprinted framework asset
    /// nested two levels down — so the pin covers the recursive case, which is
    /// the only case that occurs.
    /// </summary>
    [Fact]
    public void ResetPublishDirectory_RemovesWhatAnEarlierPublishLeft()
    {
        string publishDir = Path.Combine(_root, "host-publish");
        string frameworkDir = Path.Combine(publishDir, "wwwroot", "_framework");
        Directory.CreateDirectory(frameworkDir);
        string stale = Path.Combine(frameworkDir, "BgQuiz_Blazor.Client.staleha5h.wasm");
        File.WriteAllText(stale, "an earlier run's generation of a fingerprinted asset");

        PublishedAppFixture.ResetPublishDirectory(publishDir);

        Assert.False(File.Exists(stale));
        Assert.True(Directory.Exists(publishDir));
        Assert.Empty(Directory.EnumerateFileSystemEntries(publishDir));
    }

    /// <summary>
    /// A first-ever run has no directory to clear; the reset still owes the
    /// publish a directory to write into.
    /// </summary>
    [Fact]
    public void ResetPublishDirectory_CreatesTheDirectoryWhenItIsAbsent()
    {
        string publishDir = Path.Combine(_root, "host-publish");

        PublishedAppFixture.ResetPublishDirectory(publishDir);

        Assert.True(Directory.Exists(publishDir));
    }

    /// <summary>
    /// The guard on the recursive delete. Both halves are the point: the call is
    /// refused, and — the half that would actually hurt — the directory it named
    /// is untouched.
    /// </summary>
    [Fact]
    public void ResetPublishDirectory_RefusesAndSparesADirectoryThatIsNotTheFixtures()
    {
        string notOurs = Path.Combine(_root, "src");
        Directory.CreateDirectory(notOurs);
        string precious = Path.Combine(notOurs, "Program.cs");
        File.WriteAllText(precious, "// not the fixture's to delete");

        var refusal = Assert.Throws<ArgumentException>(
            () => PublishedAppFixture.ResetPublishDirectory(notOurs));

        Assert.Equal("publishDir", refusal.ParamName);
        Assert.True(File.Exists(precious));
    }
}
