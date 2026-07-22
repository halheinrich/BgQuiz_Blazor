namespace BgQuiz_Blazor.Client.Quiz;

using BgDataTypes_Lib;

/// <summary>
/// The one home for user-facing <see cref="CubeAction"/> wording on the quiz
/// surfaces — currently the review verdict line's per-half labels, which name
/// each half for the action the user actually submitted.
///
/// <para>
/// The four labels <b>deliberately mirror</b> the cube-banner row wording in
/// <c>BackgammonDiagram_Lib</c>'s <c>DiagramRenderer</c> (its inline literals
/// "No Double" / "Double" / "Take" / "Pass"), so the verdict line reads in the
/// same terms as the solution diagram beside it. The duplication across the
/// submodule boundary is bounded (four strings) and visible — both sides are
/// test-pinned — rather than reached for by extending the data type or
/// promoting the renderer's private label. Putting display wording on
/// <see cref="CubeAction"/> itself is a charter question for the deferred
/// <c>CubeVerdict</c> arc (see the umbrella <c>INSTRUCTIONS.md</c> Deferred
/// section), which will need verdict-level labels anyway; that is the natural
/// point to consolidate action/verdict wording at the producer.
/// </para>
///
/// <para>
/// Kept as its own small class rather than folded into <c>MixDisplay</c>:
/// that type is the home for stats-weighted-<i>mix</i> wording and is not
/// overloaded with unrelated cube-action strings.
/// </para>
/// </summary>
internal static class CubeActionDisplay
{
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
