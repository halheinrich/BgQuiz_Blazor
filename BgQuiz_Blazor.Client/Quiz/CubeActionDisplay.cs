namespace BgQuiz_Blazor.Client.Quiz;

using BgDataTypes_Lib;

/// <summary>
/// The one home for user-facing cube wording on the quiz surfaces — the
/// review verdict line's per-half labels, which name each half for what the
/// user actually submitted: the doubler half by its <see cref="CubeClaim"/>
/// (SPEC-scoring §3's three-valued claim, halheinrich/backgammon#86) and the
/// taker half by its <see cref="CubeAction"/>.
///
/// <para>
/// The labels <b>deliberately mirror</b> the cube-banner wording in
/// <c>BackgammonDiagram_Lib</c>'s <c>DiagramRenderer</c> (its inline literals
/// "No Double" / "Double" / "Take" / "Pass", and its pair-level "Too Good"),
/// so the verdict line reads in the same terms as the solution diagram beside
/// it. The duplication across the submodule boundary is bounded (five
/// strings) and visible — both sides are test-pinned — rather than reached
/// for by extending the data types or promoting the renderer's private
/// labels. Note the case split with <c>BgDiag_Razor</c>'s answer-row
/// captions ("No double" / "Too good", sentence case): the row is what the
/// user clicks, this is what the review says back, and today the two spell
/// the claim differently. Putting display wording on <see cref="CubeClaim"/>
/// / <see cref="CubeAction"/> at the producer is the arc's standing charter
/// question (recorded at halheinrich/backgammon#11's closure); this leg
/// inventories the spellings and leaves the move to the umbrella.
/// </para>
///
/// <para>
/// Kept as its own small class rather than folded into <c>MixDisplay</c> or
/// <see cref="AnswerTypeDisplay"/>: those are the homes for stats-weighted-mix
/// wording and corpus-composition vocabulary respectively, and neither is
/// overloaded with unrelated cube strings.
/// </para>
/// </summary>
internal static class CubeActionDisplay
{
    /// <summary>
    /// The display label for a doubler claim, matching the solution diagram's
    /// banner wording — "Too Good" is the renderer's own pair-level label.
    /// Exhaustive over <see cref="CubeClaim"/>; an unrecognized value is a
    /// caller/enum-evolution bug rather than a display fallback.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="claim"/> is not a defined <see cref="CubeClaim"/>.
    /// </exception>
    public static string Label(CubeClaim claim) => claim switch
    {
        CubeClaim.NoDouble => "No Double",
        CubeClaim.Double => "Double",
        CubeClaim.TooGood => "Too Good",
        _ => throw new ArgumentOutOfRangeException(
            nameof(claim), claim, "Unknown cube claim."),
    };

    /// <summary>
    /// The display label for a single cube action, matching the solution
    /// diagram's banner wording. Exhaustive over <see cref="CubeAction"/>;
    /// an unrecognized value is a caller/enum-evolution bug rather than a
    /// display fallback.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="action"/> is not a defined <see cref="CubeAction"/>.
    /// </exception>
    public static string Label(CubeAction action) => action switch
    {
        CubeAction.NoDouble => "No Double",
        CubeAction.Double => "Double",
        CubeAction.Take => "Take",
        CubeAction.Pass => "Pass",
        _ => throw new ArgumentOutOfRangeException(
            nameof(action), action, "Unknown cube action."),
    };
}
