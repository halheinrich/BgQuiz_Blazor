using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Client.Components;

/// <summary>
/// Where a problem came from, as a compact chip: the decision's source file
/// name, its game number and its move number — the three facts
/// <c>SPEC-quiz-view.md</c> §4 rules into the quiz page's bottom row beside
/// the XGID (issue <c>halheinrich/backgammon#115</c>). A problem drawn from a
/// money session is framed by no score, and while answering maximized the
/// producer's title strip — the only other surface naming the file — is
/// dropped by that same section, so without this the reader has no way back
/// to the position in eXtreme Gammon.
///
/// <para>
/// <b>Display only.</b> Every fact it renders is the record's as the producer
/// states it — <c>BgDecisionData.SourceFile</c>, <c>Game</c> and
/// <c>MoveNumber</c>, each derived from the record's identity, the one place
/// they are stored; nothing is re-derived here, and none of these facts
/// enters <c>ProblemKey</c>, the dedupe, or the stats document —
/// <c>SPEC-stats-identity.md</c> keys by content and dropped file position
/// deliberately. There is no money-versus-match branch here either, by the
/// same ruling: a locator that appeared only for money would be a second
/// place encoding what counts as money, on a display surface.
/// </para>
///
/// <para>
/// <b>A standalone position has no coordinates, and the chip says nothing in
/// their place</b> (<c>SPEC-quiz-view.md</c> §4 ruling (ii), issue
/// <c>halheinrich/backgammon#115</c>; the producer's half,
/// <c>halheinrich/backgammon#124</c>). An <c>.xgp</c> file holds one position,
/// which belongs to no game, so the producer states its game and move numbers
/// as "not applicable" — <see langword="null"/> — and the chip shows the file
/// name alone: the file <i>is</i> the locator. The converter used to stamp such
/// a record <c>Game 1 · Move 1</c> off a synthetic single-game header, true of
/// the file and false of the position (XG's own export names one
/// <c>match_2_37.xgp</c>), and this component carried a workaround that read
/// the identity's kind to suppress it. The producer no longer states the
/// numbers, so the workaround went with them.
/// </para>
///
/// <para>
/// <b>It is an in-flow chip and positions nothing</b> (the
/// <c>.problem-locator</c> rules in <c>app.css</c>): it takes the space the
/// host gives it, and gives space back when the row runs short — the file
/// name narrows, the game and move numbers never do (§4 ruling (i); the
/// shrink order lives in the stylesheet, not here). A record that locates
/// nothing — no file name and no coordinates — renders nothing at all,
/// exactly as <see cref="XgidLabel"/> does for an empty XGID, so a host may
/// bind it unconditionally and no layout may hang off its being there.
/// </para>
///
/// <para>
/// <b>No copy button</b>, unlike <see cref="XgidLabel"/>. The badge next door
/// earns one because an XGID is a value you paste <i>into</i> another tool; a
/// file name is a thing you go and look for in a folder listing, where reading
/// it is the whole use — and the row's width is board budget by
/// <c>SPEC-quiz-view.md</c> §2's contract, which a second copy control would
/// spend on an affordance nobody reaches for. The untruncated name stays
/// available two cheaper ways: it is the chip's accessible name, and
/// <c>title</c> reveals it on hover. (Deliberately not a third: the visible
/// text carries no <c>user-select: all</c>, unlike the badge next door, because
/// one-click-selecting an elided string hands the reader something with an
/// ellipsis in the middle of it — worse than nothing to paste anywhere.)
/// </para>
/// </summary>
public partial class ProblemLocator : ComponentBase
{
    /// <summary>
    /// The chip's accessible name — what a screen reader announces before the
    /// facts inside it, so the file name and the numbers arrive with a reason
    /// attached. Mirrors <see cref="XgidLabel"/>'s "Position XGID".
    /// </summary>
    private const string LocatorLabel = "Problem location";

    /// <summary>Separates the two coordinates; the app's own separator idiom.</summary>
    private const string CoordinateSeparator = " · ";

    /// <summary>Names the game number in the reader's terms, not the record's.</summary>
    private const string GameLabel = "Game";

    /// <summary>Names the move number in the reader's terms, not the record's.</summary>
    private const string MoveLabel = "Move";

    /// <summary>
    /// The game number's visible prefix in the short form — "G3" — whose
    /// full word, <see cref="GameLabel"/>, is what a screen reader hears and a
    /// hover shows.
    /// </summary>
    private const string ShortGameLabel = "G";

    /// <summary>The move number's visible prefix in the short form — "M12".</summary>
    private const string ShortMoveLabel = "M";

    /// <summary>
    /// The originating file name including its extension, as
    /// <c>BgDecisionData.SourceFile</c> states it (no directory). Every record
    /// names its file; null or blank — a caller with no name to give — hides
    /// the name half, so callers need not branch.
    /// </summary>
    [Parameter, EditorRequired]
    public string? SourceFile { get; set; }

    /// <summary>
    /// The 1-based game number within the source, from
    /// <c>BgDecisionData.Game</c>; <see langword="null"/> — "not applicable" —
    /// for a standalone position, which belongs to no game. Null hides the
    /// coordinates half together with <see cref="MoveNumber"/>.
    /// </summary>
    [Parameter, EditorRequired]
    public int? Game { get; set; }

    /// <summary>
    /// The 1-based move number within the game, from
    /// <c>BgDecisionData.MoveNumber</c>; <see langword="null"/> for a
    /// standalone position, as <see cref="Game"/> is. A cube decision carries
    /// the number of the play it precedes, which is the number eXtreme Gammon
    /// shows for that cube — verified against XG's own
    /// <c>match_game_move.xgp</c> export naming rather than against the
    /// converter that stamps it (see <c>INSTRUCTIONS.md</c>).
    /// </summary>
    [Parameter, EditorRequired]
    public int? MoveNumber { get; set; }

    /// <summary>Whether the record names a file to show.</summary>
    private bool HasFileName => !string.IsNullOrWhiteSpace(SourceFile);

    /// <summary>
    /// Whether the record states both coordinates. Both or neither: half a
    /// pair locates nothing, and a lone "Game 3" would read as a move number to
    /// anyone scanning the row. The producer states the two together — both
    /// for a decision in a game, neither for a standalone position — so the
    /// "neither" half is how a standalone position shows its file name alone.
    /// </summary>
    private bool HasCoordinates => Game is not null && MoveNumber is not null;

    /// <summary>
    /// The coordinates in full, in the reader's terms — "Game 3 · Move 12":
    /// the chip's accessible text for them and their tooltip.
    /// </summary>
    private string WhereText =>
        $"{GameLabel} {Game}{CoordinateSeparator}{MoveLabel} {MoveNumber}";

    /// <summary>
    /// The coordinates as the chip shows them — "G3 · M12"
    /// (<c>SPEC-quiz-view.md</c> §4, halheinrich/backgammon#264's ruling of
    /// 2026-10-03: "The locator takes a short form … Its numbers stay whole,
    /// and its accessible name keeps the full wording"). Only the words
    /// shorten: the numbers are the record's, whole, and the row's shrink
    /// order never takes from them (<c>app.css</c>).
    /// </summary>
    private string ShortWhereText =>
        $"{ShortGameLabel}{Game}{CoordinateSeparator}{ShortMoveLabel}{MoveNumber}";

    /// <summary>
    /// The visible file name: the record's name with its last extension
    /// dropped, then middle-truncated to
    /// <see cref="NameTruncation.MaxVisibleLength"/>.
    ///
    /// <para>
    /// <b>The extension rule, stated here because it is stated nowhere this
    /// project can reach.</b> Drop everything from the last dot onwards, and
    /// only when that dot is not the first character: "match.xg" becomes
    /// "match", "match.2026.xg" becomes "match.2026", and ".xg" passes
    /// through unchanged rather than degenerating to the empty string. That
    /// is the same rule the producer's baked title strip applies, so the two
    /// surfaces name one file the same way — but
    /// <c>DiagramRenderer.StripLastExtension</c> is private to that library,
    /// so this is a deliberate second statement of a shared rule rather than
    /// a call to it, and <c>ProblemLocatorTests</c> pins it at each boundary.
    /// </para>
    ///
    /// <para>
    /// The cut is the app's one middle truncation,
    /// <see cref="NameTruncation.MiddleTruncate"/>, shared with the score
    /// panel's folder name. The cut length is the chip's <b>widest</b> state,
    /// not its only one: what keeps the action row one line is the shrink
    /// order in <c>app.css</c> (§4 ruling (i)) — the XGID badge gives up its
    /// text first, then this name narrows under CSS, and the game/move numbers
    /// never move. So the cut governs how much name a reader gets when there
    /// <i>is</i> room; it is not what the fixed-height contract rests on.
    /// </para>
    /// </summary>
    private string DisplayFileName =>
        NameTruncation.MiddleTruncate(StripLastExtension(SourceFile!));

    /// <summary>See <see cref="DisplayFileName"/> for the rule this states.</summary>
    private static string StripLastExtension(string fileName)
    {
        int dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }
}
