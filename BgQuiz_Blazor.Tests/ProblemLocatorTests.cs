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
    /// The chip over a record's three facts. The defaults are a decision in a
    /// game — the shape with coordinates; a standalone position passes
    /// <see langword="null"/> for both numbers, as the producer states them
    /// (halheinrich/backgammon#124).
    /// </summary>
    private IRenderedComponent<ProblemLocator> Locator(
        string? sourceFile, int? game = 3, int? moveNumber = 12) =>
        Render<ProblemLocator>(p => p
            .Add(c => c.SourceFile, sourceFile)
            .Add(c => c.Game, game)
            .Add(c => c.MoveNumber, moveNumber));

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
    public void Coordinates_ShowInTheShortForm_TheirFullWordingTheAccessibleTextAndTooltip()
    {
        // SPEC-quiz-view.md §4, halheinrich/backgammon#264's ruling of
        // 2026-10-03: "The locator takes a short form such as "G3 · M12". Its
        // numbers stay whole, and its accessible name keeps the full wording."
        // The short form is what shows, hidden from the accessibility tree so
        // the numbers are not announced twice; the full wording, in the
        // reader's terms, is the visually hidden text and the tooltip — the
        // split the file name already makes.
        var cut = Locator("match.xg", game: 3, moveNumber: 12);
        var shown = cut.Find(".problem-locator-where");

        Assert.Equal("G3 · M12", shown.TextContent);
        Assert.Equal("true", shown.GetAttribute("aria-hidden"));
        Assert.Equal("Game 3 · Move 12", shown.GetAttribute("title"));
        Assert.Equal(
            ["match.xg", "Game 3 · Move 12"],
            cut.FindAll(".problem-locator .visually-hidden").Select(e => e.TextContent));
    }

    [Fact]
    public void Coordinates_KeepTheirNumbersWhole_HoweverLong()
    {
        // Only the words shorten: the numbers are the record's, every digit.
        Assert.Equal(
            "G104 · M1203",
            Locator("match.xg", game: 104, moveNumber: 1203).Find(".problem-locator-where").TextContent);
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
        Assert.Equal("G3 · M12", cut.Find(".problem-locator-where").TextContent);
    }

    [Theory]
    [InlineData(null, 12)]   // no game
    [InlineData(3, null)]    // no move
    public void PartialCoordinates_ShowNeither(int? game, int? moveNumber)
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
        var cut = Locator(sourceFile, game: null, moveNumber: null);

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public void DecisionInAGame_ShowsTheFileAndItsCoordinates()
    {
        // The .xg branch of SPEC-quiz-view.md §4's ruling (ii): a decision in a
        // match has real within-file coordinates, verified against XG's own
        // <match>_<game>_<move>.xgp export naming, so the chip shows them.
        var cut = Locator("match.xg", game: 3, moveNumber: 12);

        Assert.Equal("match", VisibleName(cut));
        Assert.Equal("G3 · M12", cut.Find(".problem-locator-where").TextContent);
    }

    [Fact]
    public void StandalonePosition_ShowsTheFileNameAlone()
    {
        // The .xgp branch. A standalone position belongs to no game, so the
        // producer states its game and move as "not applicable" — null — and
        // the chip says nothing in their place (halheinrich/backgammon#124): the
        // file is the locator, and XG's own export names such a file
        // "match_2_37.xgp", so no number could be true beside it.
        var cut = Locator("match_2_37.xgp", game: null, moveNumber: null);

        Assert.Equal("match_2_37", VisibleName(cut));
        Assert.Empty(cut.FindAll(".problem-locator-where"));
    }

    [Fact]
    public void Coordinates_FollowTheRecordsNumbers_NotTheFileExtension()
    {
        // What decides whether coordinates show is whether the record states
        // them, and nothing else: the chip reads no identity kind any more, and
        // it never sniffs the extension. These two disagree on ONLY the numbers
        // — same name — so a component that answered from the ".xgp" in the
        // name would answer the same way for both and fail here. (The first
        // pairing is impossible in production; that is what makes it a clean
        // instrument for the claim.)
        const string name = "ambiguous.xgp";

        Assert.NotEmpty(Locator(name, game: 3, moveNumber: 12).FindAll(".problem-locator-where"));
        Assert.Empty(Locator(name, game: null, moveNumber: null).FindAll(".problem-locator-where"));
    }

    /// <summary>The locator in its line form, as the action row's "⋯" list renders it.</summary>
    private IRenderedComponent<ProblemLocator> Line(string? sourceFile, int? game = 3, int? moveNumber = 12) =>
        Render<ProblemLocator>(p => p
            .Add(c => c.SourceFile, sourceFile)
            .Add(c => c.Game, game)
            .Add(c => c.MoveNumber, moveNumber)
            .Add(c => c.Form, ProblemLocatorForm.Line));

    [Theory]
    // The full wording, each half only where the record states it, as the
    // chip: a decision in a game, a standalone position, and — impossible in
    // production, but the halves are independent — coordinates alone. The
    // file name is whole: no extension dropped, no middle cut, whatever its
    // length, because the line has no width cap to serve.
    [InlineData("synthetic-match-2026-04-12.xg", 2, 4, "synthetic-match-2026-04-12.xg · Game 2 · Move 4")]
    [InlineData("a-very-long-match-file-name-beyond-any-cap.xg", 3, 12, "a-very-long-match-file-name-beyond-any-cap.xg · Game 3 · Move 12")]
    [InlineData("BothAnalysis.xgp", null, null, "BothAnalysis.xgp")]
    [InlineData(null, 3, 12, "Game 3 · Move 12")]
    public void LineForm_IsTheFullWording_AsOneLineOfText(string? sourceFile, int? game, int? moveNumber, string expected)
    {
        var cut = Line(sourceFile, game, moveNumber);

        var line = cut.Find(".problem-locator-line");
        Assert.Equal(expected, line.TextContent);
        // What shows is what a screen reader reads: no hidden twin, no
        // aria-hidden half, no tooltip standing in for the text.
        Assert.Empty(cut.FindAll(".visually-hidden, [aria-hidden], [title]"));
        Assert.Empty(cut.FindAll(".problem-locator"));
    }

    [Fact]
    public void LineForm_LocatingNothing_RendersNothing()
    {
        Assert.Equal(string.Empty, Line(null, null, null).Markup.Trim());
    }

    [Fact]
    public void TheChip_IsTheDefaultForm()
    {
        Assert.Equal(ProblemLocatorForm.Chip, new ProblemLocator().Form);
        Assert.Empty(Locator("match.xg").FindAll(".problem-locator-line"));
    }
}
