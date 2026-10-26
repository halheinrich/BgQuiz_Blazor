using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Showing a play on the solution view, in a real browser
/// (<c>SPEC-quiz-view.md</c> §1's 2026-10-25 amendment,
/// halheinrich/backgammon#194): a click on a candidate row draws the position
/// that play leaves and highlights the row; any other click on the quiz page
/// restores the position as played; the dice still Continue; a cube decision
/// has no rows. That a transition discards the shown play is pinned in bUnit
/// (<c>PageTests</c>), apart from any click — here every navigation is a click,
/// which reaches the page's restore handler too.
///
/// <para>
/// <b>The board is read by its checkers.</b> The fixture's candidates
/// (<see cref="SyntheticXgMatch.ShownPlayCandidates"/>) leave equal pip
/// counts by design, so a pip count could not tell the right candidate's
/// board from a wrong one's. Each expected arrangement is written below by
/// hand, from the moves, not read from the app or the converter; the board's
/// checkers are counted per point off the rendered SVG, each checker circle
/// assigned to the point whose hit region holds its centre.
/// </para>
/// </summary>
public sealed class ShownPlayTests : E2eTestBase
{
    public ShownPlayTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright, output) { }

    // The candidates' stored indices, as SyntheticXgMatch.ShownPlayCandidates
    // declares them: the rows draw them in the reverse order.
    private const int Run24To15 = 0;
    private const int Make4 = 1;
    private const int Played = 2;
    private const int Best = 3;

    /// <summary>
    /// The opponent's checkers in the decision's frame (the mover's point
    /// numbers): one back checker left on the 1-point after 24/18/13, six on
    /// the mid-point. No candidate hits, so every board below shares them.
    /// </summary>
    private static readonly Dictionary<int, int> Opponent = new() { [1] = 1, [12] = 6, [17] = 3, [19] = 5 };

    /// <summary>The mover's checkers as played to the decision: 3-1 made the 5-point.</summary>
    private static readonly Dictionary<int, int> AsPlayed = new() { [24] = 2, [13] = 5, [8] = 2, [6] = 4, [5] = 2 };

    /// <summary>The mover's checkers after each candidate's 5-4, by stored index.</summary>
    private static readonly Dictionary<int, Dictionary<int, int>> After = new()
    {
        [Run24To15] = new() { [24] = 1, [15] = 1, [13] = 5, [8] = 2, [6] = 4, [5] = 2 },
        [Make4] = new() { [24] = 2, [13] = 4, [8] = 2, [6] = 4, [5] = 2, [4] = 1 },
        [Played] = new() { [24] = 2, [13] = 3, [9] = 1, [8] = 3, [6] = 4, [5] = 2 },
        [Best] = new() { [24] = 1, [20] = 1, [13] = 4, [8] = 3, [6] = 4, [5] = 2 },
    };

    private ILocator ContinueButton => Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ContinueButton });

    private ILocator CandidateRows => Page.Locator(".board-container .bg-diagram > svg > rect[data-candidate]");

    private ILocator CandidateRow(int index) =>
        Page.Locator($".board-container .bg-diagram > svg > rect[data-candidate='{index}']");

    /// <summary>The drawn board: the producer's SVG, beside the hit-region overlay.</summary>
    private ILocator DrawnBoard => Page.Locator(".board-container .bg-diagram > div");

    /// <summary>The checker play's review: answered with the played move, 13/8 13/9.</summary>
    private async Task ReachTheCheckerPlayReviewAsync()
    {
        await BootHomeAsync();
        await DisableMaximizeAsync();   // the score panel names the problem while answering
        await BootHomeAsync();
        await PickSynthesizedFileAsync(SyntheticXgMatch.ShownPlayStagedFileName, SyntheticXgMatch.ShownPlayBytes());
        await ExpectFilterInEffectAsync();
        await StartQuizAsync();
        await ExpectProblemNumberAsync(1);
        await Expect(Page.Locator(".board-container .bg-play-entry")).ToBeVisibleAsync();

        // Each click on the 13-point moves a checker by the next die, so two
        // make 13/8 13/9 whichever die the entry takes first.
        await ClickBoardPointAsync(13);
        await ClickBoardPointAsync(13);
        await Expect(SubmitButton).ToBeEnabledAsync();
        await SubmitButton.ClickAsync();
        await Expect(ContinueButton).ToBeVisibleAsync();
        await Expect(CandidateRows).ToHaveCountAsync(After.Count);
    }

    /// <summary>
    /// The drawn board's checkers, per point and side: each checker circle
    /// (the producer strokes checkers, never dice pips) is assigned to the
    /// point whose hit region holds its centre. The mover's side is the one
    /// on its own 6-point, which the opponent never reaches in this fixture.
    /// No stack here is taller than the producer draws in full, so a circle is
    /// a checker.
    /// </summary>
    private async Task<(Dictionary<int, int> Mover, Dictionary<int, int> Opponent)> ReadCheckersAsync()
    {
        var json = await Page.EvaluateAsync<string>("""
            () => {
                const points = [...document.querySelectorAll('.board-container .bg-diagram > svg > rect[data-point]')]
                    .map(r => ({ point: +r.dataset.point, box: r.getBoundingClientRect() }));
                const found = [];
                for (const c of document.querySelectorAll('.board-container .bg-diagram > div circle[stroke="#888888"]')) {
                    const b = c.getBoundingClientRect();
                    const x = b.x + b.width / 2, y = b.y + b.height / 2;
                    const hit = points.find(p => x >= p.box.left && x <= p.box.right && y >= p.box.top && y <= p.box.bottom);
                    found.push({ point: hit ? hit.point : 0, fill: c.getAttribute('fill') });
                }
                return JSON.stringify(found);
            }
            """);
        var checkers = JsonSerializer.Deserialize<List<Checker>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.DoesNotContain(checkers, c => c.Point == 0);   // every checker sits on a point
        var moverFill = checkers.Where(c => c.Point == 6).Select(c => c.Fill).Distinct().Single();
        Dictionary<int, int> Count(bool mover) => checkers
            .Where(c => (c.Fill == moverFill) == mover)
            .GroupBy(c => c.Point)
            .ToDictionary(g => g.Key, g => g.Count());
        return (Count(mover: true), Count(mover: false));
    }

    private sealed record Checker(int Point, string Fill);

    private async Task ExpectCheckersAsync(Dictionary<int, int> mover, string what) =>
        await ExpectToPassAsync(async () =>
        {
            var board = await ReadCheckersAsync();
            Assert.True(Same(mover, board.Mover), $"{what}: the mover's checkers read {Show(board.Mover)}, expected {Show(mover)}");
            Assert.True(Same(Opponent, board.Opponent), $"{what}: the opponent's checkers read {Show(board.Opponent)}");
        });

    private static bool Same(Dictionary<int, int> a, Dictionary<int, int> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var n) && n == kv.Value);

    private static string Show(Dictionary<int, int> d) =>
        string.Join(" ", d.OrderByDescending(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));

    /// <summary>
    /// The candidates whose row is highlighted: those with a drawn rect
    /// covering exactly their hit region. The panel's own background spans
    /// the whole panel, so only a row highlight matches a row.
    /// </summary>
    private Task<int[]> HighlightedRowsAsync() =>
        Page.EvaluateAsync<int[]>("""
            () => {
                const drawn = [...document.querySelectorAll('.board-container .bg-diagram > div rect')]
                    .map(r => r.getBoundingClientRect());
                const near = (a, b) => Math.abs(a - b) < 0.5;
                return [...document.querySelectorAll('.board-container .bg-diagram > svg > rect[data-candidate]')]
                    .filter(row => {
                        const h = row.getBoundingClientRect();
                        return drawn.some(d => near(d.x, h.x) && near(d.y, h.y) && near(d.width, h.width) && near(d.height, h.height));
                    })
                    .map(row => +row.dataset.candidate);
            }
            """);

    private async Task ExpectHighlightedAsync(params int[] rows) =>
        await ExpectToPassAsync(async () => Assert.Equal(rows, await HighlightedRowsAsync()));

    /// <summary>What stays the record's while a play is shown: the title's roll, the XGID and the status strip.</summary>
    private async Task<(string Roll, string Xgid, string Strip)> ReadRecordFactsAsync()
    {
        var roll = await DrawnBoard.Locator("text").Filter(new() { HasTextRegex = new("to play$") }).TextContentAsync();
        var xgid = await Page.Locator(".action-row .xgid-label-text").TextContentAsync();
        var strip = await Page.Locator(".status-strip").InnerTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(roll));
        Assert.False(string.IsNullOrWhiteSpace(xgid));
        Assert.False(string.IsNullOrWhiteSpace(strip));
        return (roll!, xgid!, strip);
    }

    [Fact]
    public async Task ClickingACandidateRow_ShowsItsPlay_AndLeavesTheRecordsFacts()
    {
        await ReachTheCheckerPlayReviewAsync();
        await ExpectCheckersAsync(AsPlayed, "the review as it opens");
        await ExpectHighlightedAsync();
        var facts = await ReadRecordFactsAsync();

        await CandidateRow(Best).ClickAsync();

        await ExpectCheckersAsync(After[Best], "24/20 13/8 shown");
        await ExpectHighlightedAsync(Best);
        Assert.Equal(facts, await ReadRecordFactsAsync());
    }

    [Fact]
    public async Task ClickingAnotherRow_ShowsThatPlayInstead_AndTheShownRowChangesNothing()
    {
        await ReachTheCheckerPlayReviewAsync();
        await CandidateRow(Best).ClickAsync();
        await ExpectCheckersAsync(After[Best], "24/20 13/8 shown");

        // Another row: its play, applied to the decision's own position. With a
        // play shown the page's restore handler is attached, so this is also
        // the row's stopped click end to end — had it reached the page, the
        // board would be back as played.
        await CandidateRow(Make4).ClickAsync();
        await ExpectCheckersAsync(After[Make4], "13/4 shown");
        await ExpectHighlightedAsync(Make4);

        // The shown row again: nothing changes.
        var drawn = await DrawnBoard.InnerHTMLAsync();
        await CandidateRow(Make4).ClickAsync();
        await ExpectCheckersAsync(After[Make4], "13/4 clicked again");
        await ExpectHighlightedAsync(Make4);
        Assert.Equal(drawn, await DrawnBoard.InnerHTMLAsync());
    }

    [Fact]
    public async Task AClickElsewhereOnThePage_RestoresThePositionAsPlayed()
    {
        await ReachTheCheckerPlayReviewAsync();
        var asPlayed = await DrawnBoard.InnerHTMLAsync();

        // A board point: the board is on the page, outside the list.
        await CandidateRow(Run24To15).ClickAsync();
        await ExpectCheckersAsync(After[Run24To15], "24/15 shown");
        Assert.NotEqual(asPlayed, await DrawnBoard.InnerHTMLAsync());
        await ClickBoardPointAsync(3);
        await ExpectToPassAsync(async () => Assert.Equal(asPlayed, await DrawnBoard.InnerHTMLAsync()));
        await ExpectHighlightedAsync();

        // The status strip.
        await CandidateRow(Played).ClickAsync();
        await ExpectCheckersAsync(After[Played], "13/8 13/9 shown");
        await Page.Locator(".status-strip").ClickAsync();
        await ExpectToPassAsync(async () => Assert.Equal(asPlayed, await DrawnBoard.InnerHTMLAsync()));
        await Expect(ContinueButton).ToBeVisibleAsync();
    }

    [Fact]
    public async Task WithAPlayShown_TheDiceStillContinue()
    {
        await ReachTheCheckerPlayReviewAsync();
        await CandidateRow(Best).ClickAsync();
        await ExpectCheckersAsync(After[Best], "24/20 13/8 shown");

        // The dice are the overlay's last region wherever dice are drawn.
        await HitRects.Last.ClickAsync();

        await ExpectCubeProblemAsync(2);
    }

    [Fact]
    public async Task ACubeDecisionsReview_HasNoCandidateRows()
    {
        await ReachTheCheckerPlayReviewAsync();
        await ContinueButton.ClickAsync();
        await ExpectCubeProblemAsync(2);
        await AnswerCubeAsync(ExpectedText.DoubleTakePill);
        await Expect(ContinueButton).ToBeVisibleAsync();

        // The review's board is drawn (its points are there), and has no rows.
        await Expect(Page.Locator(".board-container .bg-diagram > svg > rect[data-point]")).ToHaveCountAsync(24);
        await Expect(CandidateRows).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task TheCandidateRows_ShowAPointerCursor_AndTheBoardDoesNot()
    {
        await ReachTheCheckerPlayReviewAsync();

        for (int i = 0; i < After.Count; i++)
            Assert.Equal("pointer", await CandidateRow(i).EvaluateAsync<string>("r => getComputedStyle(r).cursor"));
        Assert.NotEqual("pointer",
            await Page.Locator(".board-container .bg-diagram > svg > rect[data-point='13']")
                .EvaluateAsync<string>("r => getComputedStyle(r).cursor"));
    }
}
