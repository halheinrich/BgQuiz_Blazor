namespace BgQuiz_Blazor.Client.Quiz;

using BgDataTypes_Lib;
using BgGame_Lib;
using XgFilter_Lib.Filtering;

/// <summary>
/// The problems one setup offers under one filter and one ranking: filtered, then
/// deduplicated by position, with the count the page shows. It is valid for exactly the
/// setup, config and ranking it was computed for (<see cref="Describes"/>): a resample
/// changes the setup, an Apply the config, Settings the ranking, and each makes a new one.
/// </summary>
internal sealed class Selection
{
    private readonly DecisionFilterSet _filters;

    private Selection(
        QuizSetup setup, FilterConfig config, PlayRanking ranking, DecisionFilterSet filters,
        IReadOnlyList<BgDecisionData> pool, AnswerTypeDistribution answerTypes, int duplicatesCollapsed)
    {
        _filters = filters;
        Setup = setup;
        Config = config;
        Ranking = ranking;
        Pool = pool;
        AnswerTypes = answerTypes;
        DuplicatesCollapsed = duplicatesCollapsed;
    }

    public QuizSetup Setup { get; }
    public FilterConfig Config { get; }
    public PlayRanking Ranking { get; }

    /// <summary>The problems a quiz draws from, in file order.</summary>
    public IReadOnlyList<BgDecisionData> Pool { get; }

    public AnswerTypeDistribution AnswerTypes { get; }
    public int DuplicatesCollapsed { get; }

    public bool Describes(QuizSetup setup, FilterConfig config, PlayRanking ranking) =>
        ReferenceEquals(Setup, setup) && Config.Equals(config) && Ranking == ranking;

    /// <summary>
    /// XG's error for <paramref name="decision"/> as this selection's filter reads it, under its
    /// ranking: the listed player's half under a name facet, the doubler's otherwise
    /// (SPEC-filtering.md §8). The by-error order reads it.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="decision"/> is null.</exception>
    public double? RecordedError(BgDecisionData decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return _filters.ErrorRead(decision.ViewFor(Ranking));
    }

    /// <summary>Filter the setup's problems under the ranking, then deduplicate by position.</summary>
    public static async Task<Selection> ComputeAsync(
        QuizSetup setup, FilterConfig config, PlayRanking ranking,
        CooperativeYielder yielder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(yielder);

        var filters = config.Build();
        var matched = new List<BgDecisionData>();
        foreach (var decision in setup.Loaded.Problems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (filters.Matches(decision.ViewFor(ranking)))
                matched.Add(decision);
            await yielder.YieldIfDueAsync();
        }

        var distinct = await DistinctProblems.OfAsync(matched, yielder, cancellationToken);
        var distribution = AnswerTypeDistribution.Empty;
        foreach (var problem in distinct.Problems)
            distribution = distribution.Add(problem);
        return new Selection(setup, config, ranking, filters, distinct.Problems, distribution, distinct.DuplicatesCollapsed);
    }
}
