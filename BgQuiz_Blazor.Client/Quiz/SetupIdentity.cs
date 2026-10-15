namespace BgQuiz_Blazor.Client.Quiz;

using XgFilter_Razor;

/// <summary>
/// The identity of one explicit folder pick — the <b>setup's</b> identity
/// (<c>SPEC-filtering.md</c> §4, "The sample, the limits and Resample"). The
/// filter's applied-ness and the mix follow it: it is kept across resamples,
/// begun afresh by every explicit pick, even of the same folder, and never
/// the folder's leaf name.
///
/// <para>
/// <b>Opaque, and equal only to itself.</b> Its one use beyond identity is the
/// token the filter setup's owner is told (<see cref="FilterSource"/>), so no
/// consumer can compare two picks by anything but reference, and none can
/// mistake it for a sample's identity (<see cref="SampleIdentity"/>), which
/// keys different things: the two used to be one generation counter, and a
/// resample could not keep the one without keeping the other.
/// </para>
///
/// <para>
/// Minted by <see cref="PickedProblemFolder"/> alone, from its own monotonic
/// sequence, so the token's value is unique among the picks that holder has
/// held.
/// </para>
/// </summary>
internal sealed class SetupIdentity
{
    private readonly int _sequence;

    /// <summary>The identity of the holder's <paramref name="sequence"/>th pick.</summary>
    internal SetupIdentity(int sequence) => _sequence = sequence;

    /// <summary>
    /// The source this setup is reported to <see cref="FilterSetup"/> as —
    /// the one spelling of a pick in the filter owner's vocabulary, so the
    /// report and every read of the in-effect filter agree by construction.
    /// </summary>
    public FilterSourceToken FilterSource => FilterSourceToken.FromGeneration(_sequence);

    /// <summary>A description for diagnostics — logs and test failure messages.</summary>
    public override string ToString() => $"pick {_sequence}";
}
