using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Once-per-run owner of the system under test: the <b>published artifact</b>,
/// spawned as its own process and addressed over real HTTP.
///
/// <para>
/// <b>Why the published artifact.</b> Four production defects in a row (inert
/// titles, blank 404 bodies, the silent 0/0 empty-filter bounce, …) were
/// invisible-by-construction to every existing test layer: bUnit renders
/// components in isolation, and the <c>WebApplicationFactory</c> wire tests run
/// the host pipeline in-process without a browser. All four lived in the layer
/// none of them see — the publish output booting a real WASM runtime in a real
/// browser. So this fixture runs <c>dotnet publish</c> (Release) once per test
/// run and spawns <c>dotnet BgQuiz_Blazor.dll</c> from the publish folder — not
/// <c>dotnet run</c>, not <c>TestServer</c>; those would put a different layer
/// under test.
/// </para>
///
/// <para>
/// <b>Base-URL seam.</b> <see cref="BaseUrlVariable"/> parameterizes the target:
/// when set (e.g. to the deployed Azure site), the suite runs against that URL
/// and this fixture neither publishes nor spawns. The seam is deliberately just
/// the URL — no further live-mode plumbing.
/// </para>
///
/// <para>
/// <b>Spawn mechanics.</b> The app binds <c>http://127.0.0.1:0</c> (an
/// OS-assigned free port — no fixed-port collisions), and the bound port is
/// resolved from Kestrel's "Now listening on" line. The content root must be
/// pointed at the publish folder explicitly: without it, <c>MapStaticAssets</c>
/// resolves against the wrong web root and serves 0-byte framework assets — the
/// page renders unstyled and the WASM runtime never boots.
/// </para>
///
/// <para>
/// <b>Fail loud, never skip.</b> A publish failure, a publish that overruns its
/// ceiling, a missing entry-point dll, a dead process, or a failed readiness
/// probe each throw with the captured process output. Nothing here (or anywhere in this suite) turns a broken
/// precondition into a skipped-but-green run — a smoke gate that can silently
/// skip is the exact defect class it exists to kill.
/// </para>
/// </summary>
public sealed class PublishedAppFixture : IAsyncLifetime
{
    /// <summary>
    /// Environment variable overriding the suite's target base URL. Unset: the
    /// suite publishes and spawns the artifact locally. Set (e.g.
    /// <c>https://bgquiz-gobetzu.azurewebsites.net</c>): the suite drives that
    /// URL and skips publish/spawn entirely.
    /// </summary>
    public const string BaseUrlVariable = "BGQUIZ_E2E_BASE_URL";

    /// <summary>
    /// Environment variable that switches MSBuild's worker-node reuse off. The
    /// fixture sets it on its own publish and nowhere else — see
    /// <see cref="PublishHostAsync"/>.
    /// </summary>
    private const string DisableNodeReuseVariable = "MSBUILDDISABLENODEREUSE";

    private const string HostDllName = "BgQuiz_Blazor.dll";

    /// <summary>
    /// Name of the fixture-owned directory the artifact is published into, under
    /// the test assembly's output folder. It doubles as the guard
    /// <see cref="ResetPublishDirectory"/> checks before it deletes anything.
    /// </summary>
    private const string PublishDirectoryName = "host-publish";

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Ceiling on the fixture's own <c>dotnet publish</c> — generous by design.
    /// A first-ever cold AOT publish measures a couple of minutes on the dev
    /// machine and a few on a two-core CI runner, so this fires only on a
    /// genuinely wedged build, where a named failure beats an unbounded wait.
    /// </summary>
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Grace allowed for the tail of the publish log to arrive after the process
    /// has exited. Bounded on purpose — see <see cref="PublishHostAsync"/>.
    /// </summary>
    private static readonly TimeSpan LogFlushGrace = TimeSpan.FromSeconds(5);

    private Process? _app;
    private readonly StringBuilder _appOutput = new();
    private readonly TaskCompletionSource<string> _listeningUrl =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Target of every test in the run, without a trailing slash.</summary>
    public string BaseUrl { get; private set; } = null!;

    /// <summary>
    /// The directory this run published into, or <see langword="null"/> when
    /// <see cref="BaseUrlVariable"/> aimed the suite at an external URL and no
    /// publish happened. Assembly-internal and read-only from outside: the
    /// suite's one consumer is <see cref="PublishOutputHygieneTests"/>, which
    /// reads the finished publish to prove the clean-publish rule held.
    /// </summary>
    internal string? PublishDirectory { get; private set; }

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable(BaseUrlVariable) is { Length: > 0 } external)
        {
            BaseUrl = external.TrimEnd('/');
            return;
        }

        string publishDir = Path.Combine(AppContext.BaseDirectory, PublishDirectoryName);
        PublishDirectory = publishDir;
        await PublishHostAsync(publishDir);
        SpawnHost(publishDir);
        BaseUrl = await ResolveBoundUrlAsync();
        await ProbeReadinessAsync();
    }

    public Task DisposeAsync()
    {
        if (_app is { HasExited: false })
        {
            _app.Kill(entireProcessTree: true);
            _app.WaitForExit();
        }
        _app?.Dispose();
        return Task.CompletedTask;
    }

    // -----------------------------------------------------------------------
    //  Publish
    // -----------------------------------------------------------------------

    /// <summary>
    /// Publishes the host into <paramref name="publishDir"/>, capturing the whole
    /// build log for the failure paths.
    ///
    /// <para>
    /// <b>Completion is the process exiting, never end-of-stream</b> — and the
    /// publish runs with node reuse off. MSBuild's worker nodes inherit the
    /// redirected pipe of the build they serve, and with reuse on they outlive
    /// that build; a <c>ReadToEnd</c> on the pipe therefore returns only when the
    /// last node times out idle, roughly fifteen minutes after the publish has
    /// finished. Measured cold on the dev machine (2026-08-24): 903 s of dead
    /// time between the publish's last write and the first test, 1104 s for the
    /// run — against 2 s and 195 s for the same run made this way, same 61/61.
    /// </para>
    ///
    /// <para>
    /// Both guards are here because each closes a different hole.
    /// <see cref="DisableNodeReuseVariable"/> in the child's own environment
    /// removes the cause — and puts it where no caller has to know about it, so
    /// a run costs the same locally, under umbrella verification and on CI.
    /// Draining the pipes asynchronously and waiting on
    /// <see cref="Process.Exited"/> removes the <i>dependency</i> on that cause:
    /// any other tool that outlives the build still holding the pipe (a compiler
    /// server, some future SDK helper) costs this fixture a bounded
    /// <see cref="LogFlushGrace"/>, not a run. The trade is what node reuse buys
    /// on a warm republish, measured at about a second, against the fifteen
    /// minutes at stake.
    /// </para>
    /// </summary>
    private static async Task PublishHostAsync(string publishDir)
    {
        string hostProject = Path.Combine(
            FindSolutionRoot(), "BgQuiz_Blazor", "BgQuiz_Blazor.csproj");
        if (!File.Exists(hostProject))
            throw new InvalidOperationException(
                $"Host project not found at '{hostProject}' — cannot publish the artifact under test.");

        // Never publish into a directory an earlier run filled — see
        // ResetPublishDirectory for what accumulates there and why it is silent.
        ResetPublishDirectory(publishDir);

        var psi = new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "publish", hostProject, "-c", "Release", "-o", publishDir, "--nologo" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment[DisableNodeReuseVariable] = "1";

        using var publish = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var log = new StringBuilder();
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var flushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int openPipes = 2;

        void OnPublishOutput(object? _, DataReceivedEventArgs e)
        {
            // A null line is that pipe reaching end-of-stream — bookkeeping for the
            // log's tail here, never the signal that the publish is done.
            if (e.Data is null)
            {
                if (Interlocked.Decrement(ref openPipes) == 0) flushed.TrySetResult();
                return;
            }
            lock (log) log.AppendLine(e.Data);
        }

        publish.OutputDataReceived += OnPublishOutput;
        publish.ErrorDataReceived += OnPublishOutput;
        publish.Exited += (_, _) => exited.TrySetResult();

        if (!publish.Start())
            throw new InvalidOperationException("Failed to start 'dotnet publish'.");
        publish.BeginOutputReadLine();
        publish.BeginErrorReadLine();

        if (await Task.WhenAny(exited.Task, Task.Delay(PublishTimeout)) != exited.Task)
        {
            try { publish.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* It won the race with its own exit. */ }
            throw new InvalidOperationException(
                "'dotnet publish' of the host did not finish within " +
                $"{PublishTimeout.TotalMinutes:0} minutes and was killed. " +
                $"The suite tests the publish output, so it cannot proceed.\n{PublishLog(log)}");
        }
        await Task.WhenAny(flushed.Task, Task.Delay(LogFlushGrace));

        if (publish.ExitCode != 0)
            throw new InvalidOperationException(
                $"'dotnet publish' of the host failed (exit {publish.ExitCode}). " +
                $"The suite tests the publish output, so it cannot proceed.\n{PublishLog(log)}");

        string hostDll = Path.Combine(publishDir, HostDllName);
        if (!File.Exists(hostDll))
            throw new InvalidOperationException(
                $"Publish succeeded but the entry point '{hostDll}' is missing — " +
                "the publish layout is not what this fixture expects.");
    }

    /// <summary>
    /// Empties <paramref name="publishDir"/> — deleting it outright, then creating
    /// it fresh — so the publish that follows lands in a directory holding nothing
    /// but its own output.
    ///
    /// <para>
    /// <b>Why.</b> <c>dotnet publish -o</c> copies into its output directory; it
    /// never removes what an earlier publish left there. Blazor's assets are
    /// content-fingerprinted and the build is deterministic, so republishing
    /// unchanged sources overwrites in place and looks harmless — but the client
    /// stamps the short git sha into its <c>InformationalVersion</c>, so
    /// <i>every commit</i> gives the client assembly (and the AOT runtime linked
    /// against it) a new fingerprint, written beside the old one rather than over
    /// it. In an ordinary edit-commit-test loop that is a fresh generation per
    /// run: measured 2026-08-27 in this fixture's own Debug output, thirteen
    /// distinct <c>BgQuiz_Blazor.Client.&lt;hash&gt;.wasm</c> trios from earlier
    /// runs. The manifest names only the current generation, so the pile is
    /// silent — right up until a scenario reads the directory rather than the
    /// manifest, or a stale asset outlives the change that should have retired
    /// it and the gate green-lights an artifact that no longer matches its
    /// sources. The suite's whole claim is that the thing under test is the
    /// thing that ships, and that claim is only as good as the directory it
    /// publishes into.
    /// </para>
    ///
    /// <para>
    /// <b>The guard.</b> This is a recursive delete, so it refuses any path not
    /// named <see cref="PublishDirectoryName"/> — the fixture's own publish
    /// location. The parameter exists so the reset can be exercised against a
    /// scratch directory instead of the live one; the guard is what keeps that
    /// parameter from ever aiming the delete at a source tree.
    /// </para>
    ///
    /// <para>
    /// Pinned in two places: <see cref="PublishDirectoryResetTests"/> on this
    /// method, over scratch directories; <see cref="PublishOutputHygieneTests"/>
    /// on the finished publish, where a directory that accumulated anyway goes
    /// red no matter how it got that way.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="publishDir"/> is not the fixture's publish directory.
    /// </exception>
    internal static void ResetPublishDirectory(string publishDir)
    {
        string full = Path.GetFullPath(publishDir);
        if (!string.Equals(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(full)),
                PublishDirectoryName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Refusing to recursively delete '{full}': only the fixture's own " +
                $"'{PublishDirectoryName}' directory may be reset.",
                nameof(publishDir));
        }

        try
        {
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Could not clear the publish directory '{full}' — most likely a host " +
                "process from an earlier run is still holding files open there. The " +
                "suite publishes into a clean directory, so it cannot proceed.", ex);
        }

        Directory.CreateDirectory(full);
    }

    private static string PublishLog(StringBuilder log)
    {
        lock (log) return log.ToString();
    }

    /// <summary>
    /// Walks up from the test assembly to the directory holding
    /// <c>BgQuiz_Blazor.slnx</c>, so the fixture works from any build
    /// configuration's output folder without a hardcoded relative depth.
    /// </summary>
    private static string FindSolutionRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BgQuiz_Blazor.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException(
            $"BgQuiz_Blazor.slnx not found above '{AppContext.BaseDirectory}' — " +
            "cannot locate the host project to publish.");
    }

    // -----------------------------------------------------------------------
    //  Spawn + readiness
    // -----------------------------------------------------------------------

    private void SpawnHost(string publishDir)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            // Port 0: the OS assigns a free port; the bound URL is read back from
            // Kestrel's startup line. --contentRoot must name the publish folder
            // (see class docs) — the working directory alone is not enough for
            // MapStaticAssets when the test runner launches from elsewhere.
            ArgumentList =
            {
                Path.Combine(publishDir, HostDllName),
                "--urls", "http://127.0.0.1:0",
                "--contentRoot", publishDir,
            },
            WorkingDirectory = publishDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        _app = new Process { StartInfo = psi };
        _app.OutputDataReceived += (_, e) => CaptureAppOutput(e.Data);
        _app.ErrorDataReceived += (_, e) => CaptureAppOutput(e.Data);
        if (!_app.Start())
            throw new InvalidOperationException("Failed to start the published host process.");
        _app.BeginOutputReadLine();
        _app.BeginErrorReadLine();
    }

    private void CaptureAppOutput(string? line)
    {
        if (line is null) return;
        lock (_appOutput) _appOutput.AppendLine(line);

        var match = Regex.Match(line, @"Now listening on:\s*(http://\S+)");
        if (match.Success)
            _listeningUrl.TrySetResult(match.Groups[1].Value.TrimEnd('/'));
    }

    private async Task<string> ResolveBoundUrlAsync()
    {
        var resolved = await Task.WhenAny(_listeningUrl.Task, Task.Delay(StartupTimeout));
        if (resolved != _listeningUrl.Task)
            throw new InvalidOperationException(
                $"The published host did not report a listening URL within {StartupTimeout.TotalSeconds:0}s. " +
                $"Process output so far:\n{AppOutput()}");
        return await _listeningUrl.Task;
    }

    private async Task ProbeReadinessAsync()
    {
        using var http = new HttpClient();
        var deadline = DateTime.UtcNow + StartupTimeout;
        Exception? lastFailure = null;

        while (DateTime.UtcNow < deadline)
        {
            if (_app is { HasExited: true })
                throw new InvalidOperationException(
                    $"The published host exited (code {_app.ExitCode}) before serving a request. " +
                    $"Process output:\n{AppOutput()}");
            try
            {
                using var response = await http.GetAsync(BaseUrl + "/");
                if (response.IsSuccessStatusCode) return;
                lastFailure = new InvalidOperationException(
                    $"GET / returned {(int)response.StatusCode} {response.StatusCode}.");
            }
            catch (HttpRequestException ex)
            {
                lastFailure = ex;
            }
            await Task.Delay(250);
        }

        throw new InvalidOperationException(
            $"The published host at {BaseUrl} never became ready within {StartupTimeout.TotalSeconds:0}s. " +
            $"Last probe failure: {lastFailure?.Message}\nProcess output:\n{AppOutput()}");
    }

    private string AppOutput()
    {
        lock (_appOutput) return _appOutput.ToString();
    }
}
