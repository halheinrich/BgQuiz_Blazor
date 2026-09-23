namespace BgQuiz_Blazor.Client.Components;

/// <summary>
/// The app's one cut for a name too long for the line it sits on: keep the
/// front and the back, elide the middle. Called by the problem locator chip
/// (<see cref="ProblemLocator"/>, <c>SPEC-quiz-view.md</c> §4, issue
/// <c>halheinrich/backgammon#115</c>) and by the score panel's folder name
/// (§2, issue <c>halheinrich/backgammon#111</c>) — two surfaces, one rule, so
/// a name can never be shortened one way in the row and another way in the
/// line beneath it.
///
/// <para>
/// <b>Why a middle cut, and why here rather than in CSS.</b> CSS can only
/// elide an <i>end</i>, and the end of a match file or folder name is usually
/// the half that tells two of them apart (a date, a match number). Each
/// surface still caps how wide the cut name may draw with its own CSS
/// <c>max-width</c>, whose end-ellipsis takes over only when the line has
/// less room than even the cut name needs.
/// </para>
/// </summary>
internal static class NameTruncation
{
    /// <summary>Characters kept from the front of a truncated name.</summary>
    private const int HeadLength = 8;

    /// <summary>
    /// Characters kept from the end of a truncated name. Equal to
    /// <see cref="HeadLength"/> deliberately: the two halves of a real match
    /// file name carry different things — the front names the source (an
    /// opponent, a tournament), the back disambiguates it (a date, a match
    /// number) — and neither is the one worth favouring.
    /// </summary>
    private const int TailLength = 8;

    /// <summary>
    /// The ellipsis standing in for the elided middle. One character, so
    /// <see cref="MaxVisibleLength"/> is the arithmetic it looks like.
    /// </summary>
    private const char Ellipsis = '…';

    /// <summary>
    /// The longest name shown in full, and — because a truncated name is cut
    /// to exactly this — the visible name's length in every truncated state.
    /// A ceiling, not a width to pad to.
    /// </summary>
    internal const int MaxVisibleLength = HeadLength + 1 + TailLength;

    /// <summary>
    /// Middle-truncates <paramref name="name"/> to <see cref="MaxVisibleLength"/>
    /// characters. A name already that long or shorter comes back untouched.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    internal static string MiddleTruncate(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Length <= MaxVisibleLength
            ? name
            : $"{name[..HeadLength]}{Ellipsis}{name[^TailLength..]}";
    }
}
