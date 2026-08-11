using BgDataTypes_Lib;
using BgFolderAccess_Razor;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="PickedFolderSourceFactory"/> — the production source
/// composition. These call <c>Create</c> itself, so what they pin is what
/// <c>Program.cs</c> registers; they were previously written against a
/// hand-typed copy of the DI lambda, which is exactly the arrangement the named
/// type exists to end.
///
/// <para>
/// Like the corpus tests they assert shape-level invariants over the umbrella's
/// rotating <c>TestData/xg</c> corpus and skip cleanly when it is empty.
/// </para>
/// </summary>
public class PickedFolderSourceFactoryTests
{
    private static string CorpusDirectory =>
        Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "TestData", "xg"));

    /// <summary>Up to <paramref name="take"/> corpus files read into memory as picked files.</summary>
    private static IReadOnlyList<PickedFile> CorpusFiles(int take = 3)
    {
        if (!Directory.Exists(CorpusDirectory)) return [];
        return Directory.EnumerateFiles(CorpusDirectory, "*.xg")
            .Concat(Directory.EnumerateFiles(CorpusDirectory, "*.xgp"))
            .Take(take)
            .Select(p => new PickedFile(Path.GetFileName(p), File.ReadAllBytes(p)))
            .ToList();
    }

    /// <summary>A holder standing on <paramref name="files"/>, as a landed pick would leave it.</summary>
    private static PickedProblemFolder HolderOver(IReadOnlyList<PickedFile> files)
    {
        var picked = new PickedProblemFolder();
        picked.Set("corpus", files, FolderWriteCapability.BrowserUnsupported, []);
        return picked;
    }

    private static ProblemSetSourceFactory FactoryOver(
        PickedProblemFolder picked, ShuffleOption shuffle) =>
        PickedFolderSourceFactory.Create(
            picked, shuffle, NullLoggerFactory.Instance, TimeProvider.System);

    // -----------------------------------------------------------------------
    //  Argument validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_NullPicked_Throws() =>
        Assert.Throws<ArgumentNullException>(() => PickedFolderSourceFactory.Create(
            null!, new ShuffleOption(), NullLoggerFactory.Instance, TimeProvider.System));

    [Fact]
    public void Create_NullShuffle_Throws() =>
        Assert.Throws<ArgumentNullException>(() => PickedFolderSourceFactory.Create(
            new PickedProblemFolder(), null!, NullLoggerFactory.Instance, TimeProvider.System));

    [Fact]
    public void Create_NullLoggerFactory_Throws() =>
        Assert.Throws<ArgumentNullException>(() => PickedFolderSourceFactory.Create(
            new PickedProblemFolder(), new ShuffleOption(), null!, TimeProvider.System));

    [Fact]
    public void Create_NullClock_Throws() =>
        Assert.Throws<ArgumentNullException>(() => PickedFolderSourceFactory.Create(
            new PickedProblemFolder(), new ShuffleOption(), NullLoggerFactory.Instance, null!));

    // -----------------------------------------------------------------------
    //  Factory -> source -> controller wire
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FactoryShape_FeedsControllerStart()
    {
        // The whole path Program.cs wires: the registered composition over the
        // picked set drives QuizController.StartAsync to a first problem.
        var files = CorpusFiles();
        if (files.Count == 0) return; // corpus may be empty in CI

        var factory = FactoryOver(HolderOver(files), new ShuffleOption());
        var controller = new QuizController(factory, new FakeDecisionStatsSink(), TimeProvider.System);

        await controller.StartAsync(new FilterConfig(), QuizMix.Empty);

        Assert.True(controller.HasStarted);
        // A real corpus yields at least one non-pass decision; if every decision
        // happened to be a pass the controller would finish, which is still a
        // valid started state.
        Assert.True(controller.Current is not null || controller.IsFinished);
    }

    [Fact]
    public async Task FactoryShape_ActiveMix_SuppressesShuffleWrap()
    {
        // Pins the arbitration rule the composition carries: an active mix owns
        // presentation order (its RandomOrder toggle), so the shuffle decorator
        // must not wrap under it — a shuffled inner would silently break
        // RandomOrder:false's source-order determinism. With shuffle ON but a
        // non-blank mix, the factory hands back the unshuffled stack:
        // enumeration order equals the shuffle-OFF order.
        var files = CorpusFiles();
        if (files.Count == 0) return;

        var picked = HolderOver(files);
        var shuffle = new ShuffleOption();
        var factory = FactoryOver(picked, shuffle);
        var activeMix = new QuizMix([new QuizMixEntry(QuizCategory.EverythingElse, 100)]);

        // The baseline is the factory's own shuffle-OFF order rather than a bare
        // parse, so the comparison isolates the shuffle decorator from every
        // other layer in the stack: a difference introduced further down would
        // otherwise read as a shuffle that wasn't suppressed.
        var plainOrder = await CollectAllAsync(factory(new DecisionFilterSet(), QuizMix.Empty));
        if (plainOrder.Count < 2) return; // suppression unobservable over <2 items

        shuffle.Set(true);
        var underMixOrder = await CollectAllAsync(factory(new DecisionFilterSet(), activeMix));

        Assert.Equal(plainOrder.Select(d => d.Id), underMixOrder.Select(d => d.Id));
    }

    [Fact]
    public async Task ShuffleDecorator_Enabled_WrapsSourceAndChangesOrder()
    {
        // The one test here that does NOT go through PickedFolderSourceFactory,
        // deliberately: observing a shuffle deterministically needs
        // ShuffledProblemSetSource's *seeded* ctor, and production uses the
        // unseeded one on purpose (reproducibility is a test-only concern) — so
        // pinning the wrap through the real composition could only be done with
        // a random permutation, i.e. a test that flakes when the shuffle lands
        // on the identity. The composition's own arbitration rule is pinned
        // against the real thing by the sibling test above; what this one adds
        // is that a wrapped source reorders at all.
        var files = CorpusFiles();
        if (files.Count == 0) return;

        var picked = HolderOver(files);
        var shuffle = new ShuffleOption();
        ProblemSetSourceFactory seededFactory = (filters, mix) =>
        {
            IProblemSetSource inner = new CachedProblemSetSource(
                picked, filters, NullLoggerFactory.Instance, TimeProvider.System);
            return mix.IsPassthrough && shuffle.Enabled ? new ShuffledProblemSetSource(inner, seed: 42) : inner;
        };

        var unshuffledOrder = await CollectAllAsync(seededFactory(new DecisionFilterSet(), QuizMix.Empty));
        if (unshuffledOrder.Count < 2) return; // can't observe a shuffle over <2 items

        shuffle.Set(true);
        var shuffledOrder = await CollectAllAsync(seededFactory(new DecisionFilterSet(), QuizMix.Empty));

        Assert.Equal(unshuffledOrder.Count, shuffledOrder.Count);
        Assert.NotEqual(unshuffledOrder, shuffledOrder); // order differs (seeded, so deterministic)
    }

    private static async Task<List<BgDecisionData>> CollectAllAsync(IProblemSetSource src)
    {
        var items = new List<BgDecisionData>();
        await foreach (var d in src.EnumerateAsync())
            items.Add(d);
        return items;
    }
}
