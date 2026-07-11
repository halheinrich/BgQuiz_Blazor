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
/// <b>Fail loud, never skip.</b> A publish failure, a missing entry-point dll,
/// a dead process, or a failed readiness probe each throw with the captured
/// process output. Nothing here (or anywhere in this suite) turns a broken
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

    private const string HostDllName = "BgQuiz_Blazor.dll";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);

    private Process? _app;
    private readonly StringBuilder _appOutput = new();
    private readonly TaskCompletionSource<string> _listeningUrl =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Target of every test in the run, without a trailing slash.</summary>
    public string BaseUrl { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable(BaseUrlVariable) is { Length: > 0 } external)
        {
            BaseUrl = external.TrimEnd('/');
            return;
        }

        string publishDir = Path.Combine(AppContext.BaseDirectory, "host-publish");
        PublishHost(publishDir);
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

    private static void PublishHost(string publishDir)
    {
        string hostProject = Path.Combine(
            FindSolutionRoot(), "BgQuiz_Blazor", "BgQuiz_Blazor.csproj");
        if (!File.Exists(hostProject))
            throw new InvalidOperationException(
                $"Host project not found at '{hostProject}' — cannot publish the artifact under test.");

        var psi = new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "publish", hostProject, "-c", "Release", "-o", publishDir, "--nologo" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var publish = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start 'dotnet publish'.");
        // Drain both pipes before waiting, or a full pipe buffer deadlocks the build.
        string output = publish.StandardOutput.ReadToEnd();
        string errors = publish.StandardError.ReadToEnd();
        publish.WaitForExit();

        if (publish.ExitCode != 0)
            throw new InvalidOperationException(
                $"'dotnet publish' of the host failed (exit {publish.ExitCode}). " +
                $"The suite tests the publish output, so it cannot proceed.\n{output}\n{errors}");

        string hostDll = Path.Combine(publishDir, HostDllName);
        if (!File.Exists(hostDll))
            throw new InvalidOperationException(
                $"Publish succeeded but the entry point '{hostDll}' is missing — " +
                "the publish layout is not what this fixture expects.");
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
