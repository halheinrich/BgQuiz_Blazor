using System.Runtime.CompilerServices;
using BgDataTypes_Lib;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XgFilter_Razor;
using XgFilter_Razor.Components;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="MatchCount"/> — Home's match count, keyed by the
/// selection, the filter in effect and the ranking (halheinrich/backgammon#374,
/// Arc 2 item 3). <c>PageTests</c> pins what a page shows across navigation;
/// this suite pins the holder's own rules where a page cannot see them: a
/// superseded count's completion changes nothing, so there is nothing on
/// screen to wait for, and the only proof its continuation ran is awaiting the
/// request itself.
///
/// <para>
/// <b>Driven as the page drives it.</b> Every request is started on the
/// renderer's dispatcher, as Home's are, so each continuation resumes there
/// and the holder sees one thread. The inputs are real: read off the real
/// <see cref="FilterSetup"/> owner, registered as the app registers it, after
/// a mounted <see cref="FilterSurface"/> has settled the boot's restoration —
/// so the ready empty selection is in effect for the reported source. Two
/// requests differ by ranking, the one input a page changes without touching
/// the filter. Storage is incidental (Loose: nothing stored).
/// </para>
/// </summary>
public class MatchCountTests : BunitContext
{
    private static readonly FilterSourceToken Selection = FilterSourceToken.FromGeneration(1);

    private readonly HeldCountSource _equity = new(2);
    private readonly HeldCountSource _depthFirst = new(1);
    private readonly RecordingLogger<MatchCount> _log = new();
    private readonly MatchCount _count;
    private int _built;

    public MatchCountTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped<BrowserStorageCondition>();
        Services.AddScoped<FilterStorageRefusalSink>();
        Services.AddFilterSurface<FilterStorageRefusalSink>();

        // One stack per count, by the ranking it draws under, and every build
        // counted — so a reuse is visible as a count that was never built.
        var controller = new QuizController(
            (_, ranking, _) =>
            {
                _built++;
                return TestFixtures.Composed(ranking == PlayRanking.Equity ? _equity : _depthFirst);
            },
            new FakeProblemStatsSink(), TimeProvider.System);
        _count = new MatchCount(controller, _log);
    }

    /// <summary>The inputs for <paramref name="ranking"/>, read off the owner once the boot's restoration has settled.</summary>
    private MatchCountInputs InputsFor(PlayRanking ranking)
    {
        var setup = Services.GetRequiredService<FilterSetup>();
        if (setup.Current.Restoration == FilterRestoration.Pending)
        {
            setup.ReportSource(Selection);
            var surface = Render<FilterSurface>();
            surface.WaitForAssertion(() => Assert.NotEqual(FilterRestoration.Pending, setup.Current.Restoration));
        }

        return MatchCountInputs.For(setup.Current, Selection, ranking)
            ?? throw new InvalidOperationException("The ready empty selection should be in effect.");
    }

    /// <summary>
    /// Start a request on the renderer's dispatcher, as Home does, and hand
    /// back its task without waiting for it. A statement lambda on purpose: an
    /// expression lambda yielding the task binds the <c>Func&lt;Task&gt;</c>
    /// overload, which waits for the count — forever, for a held one.
    /// </summary>
    private async Task<Task> StartAsync(MatchCountInputs inputs)
    {
        Task request = Task.CompletedTask;
        await Renderer.Dispatcher.InvokeAsync(() => { request = _count.EnsureAsync(inputs); });
        return request;
    }

    private static Task Settled(Task request) => request.WaitAsync(DefaultWaitTimeout);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnOlderCount_CompletingWhileANewerOneIsPending_ChangesNothing(bool olderSucceeds)
    {
        var olderInputs = InputsFor(PlayRanking.Equity);
        var older = await StartAsync(olderInputs);
        _equity.WaitUntilReached();
        var newerInputs = InputsFor(PlayRanking.DepthFirst);
        var newer = await StartAsync(newerInputs);
        _depthFirst.WaitUntilReached();

        _equity.Answer(olderSucceeds);
        await Settled(older); // the older continuation has run

        Assert.Same(newerInputs, _count.Inputs);
        Assert.Same(MatchCountReading.Counting, _count.ReadingFor(newerInputs)); // the newer request's busy state stands, with no result
        Assert.Same(MatchCountReading.Unknown, _count.ReadingFor(olderInputs));  // and the older inputs read as nothing

        _depthFirst.Answer(succeed: true);
        await Settled(newer);

        var reading = _count.ReadingFor(newerInputs);
        Assert.False(reading.IsCounting);
        Assert.Equal(1, reading.Summary!.AnswerTypes.Total); // the newer count's, not the older's 2
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnOlderCount_CompletingAfterTheNewerOnePublished_ChangesNothing(bool olderSucceeds)
    {
        var older = await StartAsync(InputsFor(PlayRanking.Equity));
        _equity.WaitUntilReached();
        var newerInputs = InputsFor(PlayRanking.DepthFirst);
        var newer = await StartAsync(newerInputs);
        _depthFirst.WaitUntilReached();

        _depthFirst.Answer(succeed: true);
        await Settled(newer);
        var published = _count.ReadingFor(newerInputs);
        Assert.NotNull(published.Summary);

        _equity.Answer(olderSucceeds);
        await Settled(older);

        Assert.Same(newerInputs, _count.Inputs);
        Assert.Same(published, _count.ReadingFor(newerInputs));
    }

    [Fact]
    public async Task ACurrentCountThatFails_IsUnknown_NeverZero_AndIsLogged()
    {
        var inputs = InputsFor(PlayRanking.Equity);
        var request = await StartAsync(inputs);
        _equity.WaitUntilReached();

        _equity.Answer(succeed: false);
        await Settled(request);

        Assert.Same(MatchCountReading.Unknown, _count.ReadingFor(inputs)); // not counting, and no result — never zero
        var entry = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
    }

    [Fact]
    public async Task EqualInputs_ReuseTheCount_PendingOrSettled_ByValue()
    {
        var first = await StartAsync(InputsFor(PlayRanking.Equity));
        _equity.WaitUntilReached();

        // Read again off the owner: a different instance, holding a different
        // config instance, equal by value — what a remount reads.
        var again = InputsFor(PlayRanking.Equity);
        Assert.Equal(_count.Inputs, again);
        Assert.NotSame(_count.Inputs, again);

        var whilePending = await StartAsync(again);
        Assert.True(whilePending.IsCompleted); // reused: nothing started
        Assert.Same(MatchCountReading.Counting, _count.ReadingFor(again));

        _equity.Answer(succeed: true);
        await Settled(first);
        var settled = _count.ReadingFor(again);
        Assert.NotNull(settled.Summary);

        var afterSettling = await StartAsync(InputsFor(PlayRanking.Equity));
        Assert.True(afterSettling.IsCompleted);
        Assert.Same(settled, _count.ReadingFor(InputsFor(PlayRanking.Equity)));
        Assert.Equal(1, _built);
    }

    [Fact]
    public async Task DifferentInputs_Recount()
    {
        var firstInputs = InputsFor(PlayRanking.Equity);
        var first = await StartAsync(firstInputs);
        _equity.Answer(succeed: true);
        await Settled(first);
        Assert.Equal(2, _count.ReadingFor(firstInputs).Summary!.AnswerTypes.Total);

        var secondInputs = InputsFor(PlayRanking.DepthFirst);
        var second = await StartAsync(secondInputs);
        Assert.Same(MatchCountReading.Counting, _count.ReadingFor(secondInputs)); // the held count is not these inputs'
        Assert.Same(MatchCountReading.Unknown, _count.ReadingFor(firstInputs));   // nor still the first's
        _depthFirst.Answer(succeed: true);
        await Settled(second);

        Assert.Equal(1, _count.ReadingFor(secondInputs).Summary!.AnswerTypes.Total);
        Assert.Equal(2, _built);
    }

    /// <summary>
    /// A source whose one enumeration waits for the test's answer, then yields
    /// its decisions or fails — a count held open, finished in the order the
    /// test chooses. Continuations run asynchronously, so an answer never runs
    /// the counting code inside the test's own call.
    /// </summary>
    private sealed class HeldCountSource(int decisions) : IProblemSetSource
    {
        private readonly TaskCompletionSource<bool> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim _reached = new(0);

        public string Name => "Held";

        public int? Count => decisions;

        /// <summary>Block until a count has asked this source for its decisions.</summary>
        public void WaitUntilReached() =>
            Assert.True(_reached.Wait(TimeSpan.FromSeconds(10)), "No count reached the held source.");

        /// <summary>Let the held count finish: with its decisions, or by failing.</summary>
        public void Answer(bool succeed) => _answer.SetResult(succeed);

        public async IAsyncEnumerable<BgDecisionData> EnumerateAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _reached.Release();
            if (!await _answer.Task) throw new InvalidOperationException("count failed");
            for (var i = 0; i < decisions; i++)
                yield return TestFixtures.TwoChoiceDecision(TestFixtures.OpeningBest(), TestFixtures.OpeningAlternative());
        }
    }
}
