using BgDataTypes_Lib;
using BgQuiz_Blazor.Client.Components;
using Bunit;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="ProblemLocator"/>'s own contract, away from the page that hosts
/// it (<c>SPEC-quiz-view.md</c> §4, issue <c>halheinrich/backgammon#115</c>):
/// the file-name derivation, the middle-truncation rule at its boundaries, the
/// accessible name, and the states in which the chip renders nothing.
///
/// <para>
/// The derivation is pinned here rather than only through the page because it
/// is a <b>second statement of a rule stated elsewhere</b> — the producer's
/// baked title strip drops a file extension by the same rule, and
/// <c>DiagramRenderer.StripLastExtension</c> is private to that library, so
/// nothing but these pins can catch the two surfaces drifting apart on how
/// they name one file.
/// </para>
/// </summary>
public class ProblemLocatorTests : BunitContext
{
    /// <summary>
    /// A multi-decision <c>.xg</c> source — the shape that <i>has</i> within-file
    /// coordinates. Spelled with the same file name the <c>SourceFile</c>
    /// argument carries, because a record whose identity and provenance
    /// disagreed about the file would not be a record this app can produce.
    /// </summary>
    private static DecisionId FromMatch(string sourceFile) =>
        new XgDecisionId(sourceFile, Game: 3, MoveNumber: 12, IsCube: false);

    /// <summary>A standalone <c>.xgp</c> position — no coordinates to have.</summary>
    private static DecisionId FromOnePosition(string sourceFile) =>
        new XgpDecisionId(sourceFile);

    private IRenderedComponent<ProblemLocator> Locator(
        string? sourceFile, int game = 3, int moveNumber = 12, DecisionId? source = null) =>
        Render<ProblemLocator>(p => p
            .Add(c => c.SourceFile, sourceFile)
            .Add(c => c.Game, game)
            .Add(c => c.MoveNumber, moveNumber)
            .Add(c => c.Source, source ?? FromMatch(sourceFile ?? "match.xg")));

    /// <summary>The visible, shortened name — the aria-hidden twin.</summary>
    private static string VisibleName(IRenderedComponent<ProblemLocator> cut) =>
        cut.Find(".problem-locator-file").TextContent;

    [Theory]
    // The extension rule, at each of its three branches. "The last dot, and
    // only if it isn't the first character" is exactly what separates these.
    [InlineData("match.xg", "match")]
    [InlineData("match.xgp", "match")]
    [InlineData("m.2026.xg", "m.2026")]      // earlier dots survive
    [InlineData("noextension", "noextension")]
    [InlineData(".xg", ".xg")]               // leading-dot-only: not an extension
    public void FileName_DropsTheLastExtensionOnly(string sourceFile, string expected)
    {
        Assert.Equal(expected, VisibleName(Locator(sourceFile)));
    }

    [Fact]
    public void ShortName_IsShownWhole()
    {
        // 17 characters after the extension goes — the longest name the cap
        // lets through untouched. One shorter would pass under any cap at or
        // above it, which is what makes a boundary pin worth writing.
        const string stem = "abcdefghijklmnopq";
        Assert.Equal(17, stem.Length);

        Assert.Equal(stem, VisibleName(Locator(stem + ".xg")));
    }

    [Fact]
    public void LongName_IsTruncatedInTheMiddle_ToTheSameWidth()
    {
        // One character longer than the case above: the first name the rule
        // touches. Head and tail survive, the middle goes, and the result is
        // the same length as the longest untruncated name — the property the
        // row's width contract actually rests on.
        const string stem = "abcdefghijklmnopqr";
        Assert.Equal(18, stem.Length);

        string visible = VisibleName(Locator(stem + ".xg"));

        Assert.Equal("abcdefgh…klmnopqr", visible);
        Assert.Equal(17, visible.Length);
    }

    [Fact]
    public void TruncatedName_KeepsTheFullNameAsTheAccessibleName()
    {
        // The whole point of truncating: nothing is lost, it is only hidden.
        // The untruncated name — extension and all, because that is the file
        // the reader goes and looks for — is in the accessibility tree, and
        // the shortened twin is out of it, so neither is announced twice.
        const string sourceFile = "a-very-long-match-file-name-indeed.xg";

        var cut = Locator(sourceFile);

        var full = cut.Find(".problem-locator .visually-hidden");
        Assert.Equal(sourceFile, full.TextContent);
        Assert.Null(full.GetAttribute("aria-hidden"));

        var shortened = cut.Find(".problem-locator-file");
        Assert.Equal("true", shortened.GetAttribute("aria-hidden"));
        Assert.NotEqual(sourceFile, shortened.TextContent);

        // And on hover, for a sighted mouse user, the same full name.
        Assert.Equal(sourceFile, shortened.GetAttribute("title"));
    }

    [Fact]
    public void Chip_NamesItselfToAScreenReader()
    {
        var chip = Locator("match.xg").Find(".problem-locator");

        Assert.Equal("group", chip.GetAttribute("role"));
        Assert.Equal("Problem location", chip.GetAttribute("aria-label"));
    }

    [Fact]
    public void Coordinates_AreShownInTheReadersTerms()
    {
        Assert.Equal(
            "Game 3 · Move 12",
            Locator("match.xg", game: 3, moveNumber: 12).Find(".problem-locator-where").TextContent);
    }

    [Fact]
    public void NoCopyButton_TheBadgeNextDoorOwnsThatAffordance()
    {
        // A ruled choice, not an oversight (see the component's remarks): the
        // row's width is board budget, and a file name is read rather than
        // pasted. If a copy control is ever wanted here it arrives with a
        // width measurement, and this pin is where it announces itself.
        Assert.Empty(Locator("match.xg").FindAll("button"));
    }

    [Fact]
    public void NoFileName_ShowsTheCoordinatesAlone()
    {
        var cut = Locator(sourceFile: null);

        Assert.Empty(cut.FindAll(".problem-locator-file"));
        Assert.Equal("Game 3 · Move 12", cut.Find(".problem-locator-where").TextContent);
    }

    [Theory]
    [InlineData(0, 12)]   // unstamped game
    [InlineData(3, 0)]    // unstamped move
    public void PartialCoordinates_ShowNeither(int game, int moveNumber)
    {
        // Both or neither: half a pair locates nothing, and a lone "Game 3"
        // would read as a move number to anyone scanning the row.
        var cut = Locator("match.xg", game, moveNumber);

        Assert.Empty(cut.FindAll(".problem-locator-where"));
        Assert.Equal("match", VisibleName(cut));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingToLocate_RendersNothingAtAll(string? sourceFile)
    {
        // The XgidLabel contract, restated for this chip: a record that locates
        // nothing produces no element, so a host may bind it unconditionally —
        // and so the cluster's ms-auto may not live on it.
        var cut = Locator(sourceFile, game: 0, moveNumber: 0);

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public void MatchSource_ShowsTheCoordinates()
    {
        // The .xg branch of SPEC-quiz-view.md §4's ruling (ii): a multi-decision
        // source has real within-file coordinates, verified against XG's own
        // <match>_<game>_<move>.xgp export naming, so the chip shows them.
        var cut = Locator("match.xg", source: FromMatch("match.xg"));

        Assert.Equal("match", VisibleName(cut));
        Assert.Equal("Game 3 · Move 12", cut.Find(".problem-locator-where").TextContent);
    }

    [Fact]
    public void OnePositionSource_ShowsTheFileNameAlone()
    {
        // The .xgp branch. The record still CARRIES coordinates — the converter
        // stamps every standalone position Game 1 · Move 1 off a synthetic game
        // header — and they are exactly what must not be shown: XG's own export
        // names such a file "match_2_37.xgp", so 1 · 1 would contradict the file
        // name sitting beside it. The file is the locator; there is nothing
        // within it to number.
        //
        // Note the coordinates passed in are the app's real ones, not zeroes:
        // this pin fails if the suppression is quietly resting on the unstamped
        // rung rather than on the source's kind.
        var cut = Locator("match_2_37.xgp", game: 1, moveNumber: 1,
                          source: FromOnePosition("match_2_37.xgp"));

        Assert.Equal("match_2_37", VisibleName(cut));
        Assert.Empty(cut.FindAll(".problem-locator-where"));
    }

    [Fact]
    public void OnePositionSource_IsDiscriminatedByKind_NotByExtension()
    {
        // The ruled discriminant, pinned as such. These two records disagree on
        // ONLY the identity's type — same name, same numbers — so a component
        // that sniffed the ".xgp" in the file name, or parsed the id's canonical
        // string, would answer the same way for both and fail here. (The pairing
        // is deliberately impossible in production; that is what makes it a
        // clean instrument for the claim.)
        const string name = "ambiguous.xgp";

        Assert.NotEmpty(Locator(name, source: FromMatch(name)).FindAll(".problem-locator-where"));
        Assert.Empty(Locator(name, source: FromOnePosition(name)).FindAll(".problem-locator-where"));
    }

    [Fact]
    public void OnePositionSource_WithNoFileName_RendersNothingAtAll()
    {
        // The two suppressions compose: an .xgp shows its name, so an .xgp with
        // no name has nothing left to show — and must not fall back to the
        // synthetic numbers it is still carrying.
        var cut = Locator(null, game: 1, moveNumber: 1,
                          source: FromOnePosition("unnamed.xgp"));

        Assert.Empty(cut.Markup.Trim());
    }
}
