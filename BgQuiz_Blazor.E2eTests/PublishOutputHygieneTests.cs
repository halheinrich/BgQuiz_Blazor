using System.Text.RegularExpressions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The standing half of the clean-publish rule: the artifact this run is
/// driving carries <b>one</b> generation of every fingerprinted asset.
///
/// <para>
/// <see cref="PublishDirectoryResetTests"/> pins the reset in isolation; this
/// pins that the publish actually got it. It asserts against the directory the
/// collection fixture has already produced, so it costs no second publish and
/// no browser — it is the fixture's own output, read after the fact.
/// </para>
///
/// <para>
/// <b>Where it bites.</b> A fresh CI runner publishes once into an empty
/// directory and passes trivially. That is the point: accumulation only ever
/// happens where the directory is <i>reused</i>, which is every local run, and
/// this is the assertion that turns a silent pile into a red test there. Before
/// the reset landed, this fixture's Debug output held fourteen generations of
/// <c>BgQuiz_Blazor.Client</c>.
/// </para>
/// </summary>
[Collection(E2eCollection.Name)]
public sealed class PublishOutputHygieneTests
{
    private readonly PublishedAppFixture _app;

    public PublishOutputHygieneTests(PublishedAppFixture app) => _app = app;

    /// <summary>
    /// A published asset name, fingerprint carried in the second-to-last segment
    /// before the extension: <c>Microsoft.AspNetCore.Components.Web</c> +
    /// <c>x6etprh71k</c> + <c>wasm</c>. The stem is greedy so a name that is
    /// itself dotted keeps all of it, and the pre-compression suffix is matched
    /// separately because <c>.br</c> / <c>.gz</c> sit outside the fingerprint.
    /// </summary>
    private static readonly Regex Fingerprinted = new(
        @"^(?<stem>.+)\.(?<fingerprint>[a-z0-9]{10})\.(?<extension>[^.]+(?:\.(?:br|gz))?)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void PublishedArtifact_CarriesOneGenerationOfEachFingerprintedAsset()
    {
        // Null only under BGQUIZ_E2E_BASE_URL, where the suite drives a URL it did
        // not publish: there is no local publish directory to have a rule about.
        // Not a skipped precondition — the subject does not exist in that mode.
        if (_app.PublishDirectory is not { } publishDir) return;

        // Keyed by what the asset *is* — directory, name, extension — so the
        // fingerprints of one logical asset gather in one place. A file whose name
        // merely looks fingerprinted keys to itself and stays a set of one.
        var generations = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(publishDir, "*", SearchOption.AllDirectories))
        {
            var match = Fingerprinted.Match(Path.GetFileName(file));
            if (!match.Success) continue;

            string key = Path.Combine(
                Path.GetRelativePath(publishDir, Path.GetDirectoryName(file)!),
                match.Groups["stem"].Value + ".*." + match.Groups["extension"].Value);
            if (!generations.TryGetValue(key, out var fingerprints))
                generations[key] = fingerprints = new SortedSet<string>(StringComparer.Ordinal);
            fingerprints.Add(match.Groups["fingerprint"].Value);
        }

        // The publish must have produced fingerprinted assets at all — an empty
        // dictionary would make the assertion below vacuous, and this suite's own
        // rule is that a pin proving nothing is worse than no pin.
        Assert.NotEmpty(generations);

        var accumulated = generations
            .Where(entry => entry.Value.Count > 1)
            .Select(entry => $"{entry.Key} -> {string.Join(", ", entry.Value)}")
            .ToList();
        Assert.True(accumulated.Count == 0,
            $"The publish under test at '{publishDir}' carries more than one generation of " +
            $"{accumulated.Count} asset(s) — output from an earlier run survived into it, so " +
            "what the browser boots is no longer only what this publish produced:\n  " +
            string.Join("\n  ", accumulated));
    }
}
