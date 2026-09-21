using AngleSharp.Dom;
using BgQuiz_Blazor.Client.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="DecisionNotes"/>'s own contract, away from the page that hosts
/// it (<c>SPEC-quiz-view.md</c> §4's 2026-09-15 amendment, issue
/// <c>halheinrich/backgammon#31</c>): the control renders iff there is a
/// comment; it opens the comment as a dialog and moves focus into it; Esc, the
/// backdrop and the close button each close it and hand focus back to the
/// control; and the text reaches the page as it was given.
///
/// <para>
/// <b>What bUnit cannot see, and who does.</b> The overlay's promises are
/// partly CSS — fixed positioning (nothing reflows, the board never moves),
/// <c>pre-wrap</c> (the author's whitespace and line breaks), the backdrop's
/// reach over the whole viewport — and AngleSharp evaluates none of it. The
/// declaration is pinned in <c>PageTests</c> beside the other <c>AppCss_*</c>
/// pins; the behaviour in a real browser is <c>DecisionNotesTests</c> in the
/// e2e suite. The page's half — review only, after Redo, the record's comment
/// passed through — is pinned in <c>PageTests</c> too.
/// </para>
/// </summary>
public class DecisionNotesTests : BunitContext
{
    private const string Note = "Hit loose here; the gammons are worth it.";

    /// <summary>
    /// The control's element-reference id, read off the first render. Read
    /// then because bUnit prints an element's <c>@ref</c> id only on the
    /// render that created the element — a re-render leaves the attribute
    /// empty (measured) — and every close is a re-render, so this is the one
    /// moment the control can be identified by the reference focus is aimed at.
    /// </summary>
    private string? _controlReferenceId;

    private IRenderedComponent<DecisionNotes> Notes(string? comment = Note)
    {
        var cut = Render<DecisionNotes>(p => p.Add(c => c.Comment, comment));
        _controlReferenceId = cut.FindAll("button.decision-notes-toggle").SingleOrDefault()
            ?.GetAttribute("blazor:elementreference");
        return cut;
    }

    private static IElement Control(IRenderedComponent<DecisionNotes> cut) =>
        cut.Find("button.decision-notes-toggle");

    private static async Task<IRenderedComponent<DecisionNotes>> OpenedAsync(IRenderedComponent<DecisionNotes> cut)
    {
        await Control(cut).ClickAsync(new());
        Assert.Single(cut.FindAll("dialog"));
        return cut;
    }

    /// <summary>
    /// Focus has moved exactly <paramref name="moves"/> times so far, the last
    /// of them to <paramref name="expected"/>. Counted, not merely inspected:
    /// open moves focus once (into the dialog) and each close once more (back
    /// to the control), so a stray extra move is as much a defect as a
    /// missing one.
    /// </summary>
    private void AssertFocusMoved(int moves, IElement expected)
    {
        var focusCalls = JSInterop.VerifyFocusAsyncInvoke(calledTimes: moves);
        focusCalls[^1].Arguments[0].ShouldBeElementReferenceTo(expected);
    }

    /// <summary>
    /// Focus has moved exactly <paramref name="moves"/> times, the last of them
    /// back to the control — identified by the reference id its first render
    /// carried (see <see cref="_controlReferenceId"/>).
    /// </summary>
    private void AssertFocusReturnedToTheControl(int moves)
    {
        Assert.False(string.IsNullOrEmpty(_controlReferenceId), "the control's reference was read on its first render");
        var focusCalls = JSInterop.VerifyFocusAsyncInvoke(calledTimes: moves);
        var target = Assert.IsType<ElementReference>(focusCalls[^1].Arguments[0]);
        Assert.Equal(_controlReferenceId, target.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void NoComment_RendersNothing(string? comment)
    {
        // "A problem without notes has no control": nothing at all, so the
        // host's row carries no trace of it.
        Assert.Equal(string.Empty, Notes(comment).Markup.Trim());
    }

    [Fact]
    public void Comment_RendersTheNotesControl_Closed()
    {
        var cut = Notes();

        var control = Control(cut);
        Assert.Equal("Notes", control.TextContent.Trim());
        Assert.Equal("button", control.GetAttribute("type"));
        Assert.Equal("dialog", control.GetAttribute("aria-haspopup"));
        Assert.Equal("false", control.GetAttribute("aria-expanded"));
        Assert.False(control.HasAttribute("aria-controls"));

        // Nothing opens by itself: no gesture, no overlay.
        Assert.Empty(cut.FindAll("dialog"));
        Assert.Empty(cut.FindAll(".decision-notes-backdrop"));
    }

    [Fact]
    public async Task ClickingTheControl_OpensTheNotesAsANamedDialog_AndMovesFocusIntoIt()
    {
        var cut = await OpenedAsync(Notes());

        var dialog = cut.Find("dialog");
        Assert.True(dialog.HasAttribute("open"));
        // A dialog to assistive technology, named by its heading.
        var heading = cut.Find($"#{dialog.GetAttribute("aria-labelledby")}");
        Assert.Equal("Notes", heading.TextContent.Trim());
        Assert.Equal(Note, cut.Find(".decision-notes-text").TextContent);

        // The control says what it opened and where.
        var control = Control(cut);
        Assert.Equal("true", control.GetAttribute("aria-expanded"));
        Assert.Equal(dialog.Id, control.GetAttribute("aria-controls"));

        // The backdrop that turns a click outside into a close.
        Assert.Single(cut.FindAll(".decision-notes-backdrop"));

        AssertFocusMoved(1, dialog);
    }

    [Fact]
    public async Task Escape_Closes_AndReturnsFocusToTheControl()
    {
        var cut = await OpenedAsync(Notes());

        await cut.Find("dialog").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll("dialog"));
        Assert.Empty(cut.FindAll(".decision-notes-backdrop"));
        Assert.Equal("false", Control(cut).GetAttribute("aria-expanded"));
        AssertFocusReturnedToTheControl(2);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("Enter")]
    [InlineData("Tab")]
    public async Task OtherKeys_LeaveTheNotesOpen(string key)
    {
        // Esc is the one key the overlay claims. Space in particular must not
        // close it: inside the notes it scrolls a long text (and quizKeys.js
        // keeps the page's Space shortcut out of an open dialog).
        var cut = await OpenedAsync(Notes());

        await cut.Find("dialog").KeyDownAsync(new KeyboardEventArgs { Key = key });

        Assert.Single(cut.FindAll("dialog"));
    }

    [Fact]
    public async Task ClickingOutside_OnTheBackdrop_Closes_AndReturnsFocusToTheControl()
    {
        var cut = await OpenedAsync(Notes());

        await cut.Find(".decision-notes-backdrop").ClickAsync(new());

        Assert.Empty(cut.FindAll("dialog"));
        Assert.Empty(cut.FindAll(".decision-notes-backdrop"));
        AssertFocusReturnedToTheControl(2);
    }

    [Fact]
    public async Task NothingInsideTheNotes_ClosesThemOnAClick()
    {
        // A click on the notes themselves must not reach the backdrop's close.
        // It cannot, by structure: the backdrop is the dialog's sibling, never
        // its ancestor, and nothing inside the dialog but the close button
        // carries a click handler. Pinned as that structure because bUnit will
        // not dispatch a click to an element with no handler on itself or any
        // ancestor — which is the very fact being claimed. The real click is the
        // e2e scenario's.
        var cut = await OpenedAsync(Notes());

        var text = cut.Find(".decision-notes-text");
        Assert.Null(text.Closest(".decision-notes-backdrop"));
        Assert.Null(cut.Find("dialog").Closest(".decision-notes-backdrop"));
        Assert.False(text.HasAttribute("blazor:onclick"));
        Assert.False(cut.Find("dialog").HasAttribute("blazor:onclick"));
    }

    [Fact]
    public async Task TheCloseButton_Closes_AndReturnsFocusToTheControl()
    {
        var cut = await OpenedAsync(Notes());

        var close = cut.Find("dialog button.btn-close");
        Assert.Equal("Close notes", close.GetAttribute("aria-label"));
        await close.ClickAsync(new());

        Assert.Empty(cut.FindAll("dialog"));
        AssertFocusReturnedToTheControl(2);
    }

    [Fact]
    public async Task TheControl_WhileOpen_Closes()
    {
        // Reachable only by keyboard (the backdrop covers it for a pointer),
        // and then it does what aria-expanded="true" promises.
        var cut = await OpenedAsync(Notes());

        await Control(cut).ClickAsync(new());

        Assert.Empty(cut.FindAll("dialog"));
        AssertFocusReturnedToTheControl(2);
    }

    [Fact]
    public async Task TheText_ReachesThePageVerbatim_WhitespaceAndAll()
    {
        // Shown as the converter stamped it: leading and trailing whitespace,
        // runs of spaces, indentation, tabs, blank lines — and even text that
        // looks like RTF markup — pass through untouched, as one text node;
        // nothing trims, collapses or interprets them. (Real comments arrive
        // as plain text: the converter reduces XG's RTF before stamping,
        // halheinrich/backgammon#233. Markup-shaped text reaching this
        // component is the author's own, and stays as written.) The edges matter: every real XG comment ends in a
        // line break, so a trim would be a change to real text, not a nicety.
        // (LF here, not CRLF: bUnit round-trips the render through AngleSharp's
        // HTML parser, which normalizes CR LF to LF by specification, so a CRLF
        // could not be told from an LF at this layer. The real browser, where
        // the text is set through the DOM and CRLF survives, is the e2e
        // scenario's.)
        const string laidOut = "  {\\rtf1 CASH\\par\n      S4\n24/18 21/18     .17\n\n\tlast\n";
        var cut = await OpenedAsync(Notes(laidOut));

        var text = cut.Find(".decision-notes-text");
        Assert.Equal(laidOut, text.TextContent);
        Assert.Empty(text.Children); // one text node: nothing was turned into markup
    }

    [Fact]
    public async Task ANewComment_WhileOpen_ClosesTheNotes_WithoutMovingFocus()
    {
        // The overlay only ever shows notes a gesture opened. A host that hands
        // this instance another decision's comment is showing another decision,
        // so the open overlay does not carry over to it.
        var cut = await OpenedAsync(Notes());

        cut.Render(p => p.Add(c => c.Comment, "A different decision's note."));

        Assert.Empty(cut.FindAll("dialog"));
        Assert.Equal("false", Control(cut).GetAttribute("aria-expanded"));
        // Only the opening move: the user did not close anything, so focus is
        // not yanked back to the control.
        JSInterop.VerifyFocusAsyncInvoke(calledTimes: 1);
    }

    [Fact]
    public async Task TheSameComment_ReRendered_LeavesTheNotesOpen()
    {
        // The other half: a host re-rendering for its own reasons (a stats
        // status change mid-review, say) passes the same text, and the reader's
        // open notes stay open.
        var cut = await OpenedAsync(Notes());

        cut.Render(p => p.Add(c => c.Comment, Note));

        Assert.Single(cut.FindAll("dialog"));
    }
}
