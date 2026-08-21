using BgDataTypes_Lib;
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
/// <b>Display only.</b> Every string it renders is read straight off the
/// record's <c>DescriptiveData</c> as the converter stamped it; nothing is
/// re-derived from the XGID or the <c>DecisionId</c>, and none of these facts
/// enters <c>ProblemKey</c>, the dedupe, or the stats document —
/// <c>SPEC-stats-identity.md</c> keys by content and dropped file position
/// deliberately. There is no money-versus-match branch here either, by the
/// same ruling: a locator that appeared only for money would be a second
/// place encoding what counts as money, on a display surface.
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
    /// <see cref="MaxVisibleNameLength"/> is the arithmetic it looks like.
    /// </summary>
    private const char Ellipsis = '…';

    /// <summary>
    /// The longest file name shown in full, and — because a truncated name is
    /// cut to exactly this — the visible name's length in every truncated
    /// state.
    ///
    /// <para>
    /// This is the chip's <b>widest</b> state, not its only one. What keeps
    /// the action row one line is the shrink order in <c>app.css</c> (§4
    /// ruling (i)): the XGID badge gives up its text first, then this name
    /// narrows under CSS, and the game/move numbers never move. So the cap
    /// governs how much name a reader gets when there <i>is</i> room; it is
    /// not what the fixed-height contract rests on.
    /// </para>
    /// </summary>
    private const int MaxVisibleNameLength = HeadLength + 1 + TailLength;

    /// <summary>Separates the two coordinates; the app's own separator idiom.</summary>
    private const string CoordinateSeparator = " · ";

    /// <summary>Names the game number in the reader's terms, not the record's.</summary>
    private const string GameLabel = "Game";

    /// <summary>Names the move number in the reader's terms, not the record's.</summary>
    private const string MoveLabel = "Move";

    /// <summary>
    /// The originating file name including its extension, as
    /// <c>DescriptiveData.SourceFile</c> stamps it (no directory). Null or
    /// blank — a source that recorded no name — hides the name half; callers
    /// need not branch.
    /// </summary>
    [Parameter, EditorRequired]
    public string? SourceFile { get; set; }

    /// <summary>
    /// The 1-based game number within the source, from
    /// <c>DescriptiveData.Game</c>. Below 1 means unstamped, and hides the
    /// coordinates half together with <see cref="MoveNumber"/>.
    /// </summary>
    [Parameter, EditorRequired]
    public int Game { get; set; }

    /// <summary>
    /// The 1-based move number within the game, from
    /// <c>DescriptiveData.MoveNumber</c>. A cube decision carries the number
    /// of the play it precedes, which is the number eXtreme Gammon shows for
    /// that cube — verified against XG's own
    /// <c>match_game_move.xgp</c> export naming rather than against the
    /// converter that stamps it (see <c>INSTRUCTIONS.md</c>).
    /// </summary>
    [Parameter, EditorRequired]
    public int MoveNumber { get; set; }

    /// <summary>
    /// The record's stamped identity. Only its <b>kind</b> is read, and only
    /// to answer one question: does this source have within-file coordinates
    /// at all? See <see cref="SourceIsOnePosition"/>. Nothing is ever parsed
    /// out of it, and none of the displayed strings come from it.
    /// </summary>
    [Parameter, EditorRequired]
    public DecisionId? Source { get; set; }

    /// <summary>Whether the record names a file to show.</summary>
    private bool HasFileName => !string.IsNullOrWhiteSpace(SourceFile);

    /// <summary>
    /// Whether the source is a <b>standalone position file</b> — one position,
    /// exported by eXtreme Gammon on its own — in which case the file <i>is</i>
    /// the locator and there is nothing within it to number
    /// (<c>SPEC-quiz-view.md</c> §4 ruling (ii), issue
    /// <c>halheinrich/backgammon#115</c>).
    ///
    /// <para>
    /// This is not cosmetic. An <c>.xgp</c> carries a synthetic single-game
    /// header, so the converter stamps <i>every</i> such record
    /// <c>Game 1 · Move 1</c> — true of the file and false of the position:
    /// XG's own export names them <c>match_2_37.xgp</c> and the position
    /// really is game 2, move 37 of its match. Showing 1 · 1 would mislead by
    /// implicature on the one surface whose whole job is to locate the
    /// problem. (The wire's synthetic numbers are a producer wart, booked
    /// separately; nothing here touches the producer.)
    /// </para>
    ///
    /// <para>
    /// The discriminant is the identity's <b>type</b>, read as a fact:
    /// <see cref="XgpDecisionId"/> keys on a bare filename precisely
    /// <i>because</i> there are no within-file coordinates to key on, whereas
    /// <see cref="XgDecisionId"/> carries the game/move tuple. Sniffing the
    /// file name's extension would be a second place encoding what an
    /// <c>.xgp</c> is, and parsing the id's canonical string would be a second
    /// reader of a grammar the producer owns. A source of any other shape —
    /// including none supplied — is treated as carrying coordinates, and the
    /// unstamped rung below still hides them when the record has none.
    /// </para>
    /// </summary>
    private bool SourceIsOnePosition => Source is XgpDecisionId;

    /// <summary>
    /// Whether the record carries both coordinates, and they mean something.
    /// Both or neither: half a pair locates nothing, and a lone "Game 3" would
    /// read as a move number to anyone scanning the row.
    /// </summary>
    private bool HasCoordinates =>
        !SourceIsOnePosition && Game >= 1 && MoveNumber >= 1;

    /// <summary>The coordinates, in the reader's terms.</summary>
    private string WhereText =>
        $"{GameLabel} {Game}{CoordinateSeparator}{MoveLabel} {MoveNumber}";

    /// <summary>
    /// The visible file name: the record's name with its last extension
    /// dropped, then middle-truncated to <see cref="MaxVisibleNameLength"/>.
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
    /// The truncation is done here rather than in CSS because CSS can only
    /// elide an <i>end</i>, and the end of a match file name is usually the
    /// half that tells two of them apart.
    /// </para>
    /// </summary>
    private string DisplayFileName => Shorten(StripLastExtension(SourceFile!));

    /// <summary>See <see cref="DisplayFileName"/> for the rule this states.</summary>
    private static string StripLastExtension(string fileName)
    {
        int dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }

    /// <summary>
    /// Middle-truncates <paramref name="name"/> to
    /// <see cref="MaxVisibleNameLength"/> characters. A name already that long
    /// or shorter comes back untouched — the cap is a ceiling, not a width to
    /// pad to.
    /// </summary>
    private static string Shorten(string name) =>
        name.Length <= MaxVisibleNameLength
            ? name
            : $"{name[..HeadLength]}{Ellipsis}{name[^TailLength..]}";
}
