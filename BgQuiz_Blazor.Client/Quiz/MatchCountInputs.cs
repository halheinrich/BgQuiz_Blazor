namespace BgQuiz_Blazor.Client.Quiz;

using BgDataTypes_Lib;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;

/// <summary>
/// What a match count is a count <i>of</i>: the selection (the pick, by its
/// filter source token), the filter in effect for it, and the ranking the
/// count draws under (<c>SPEC-scoring.md</c> §2a) — the key
/// <see cref="MatchCount"/> holds its result under, compared by value
/// (XgFilter_Razor's host contract, rule 4; halheinrich/backgammon#374).
///
/// <para>
/// <b>A stable snapshot, not a held config.</b> The owner hands out a new
/// <see cref="FilterConfig"/> on every read, and a config is mutable, so a key
/// that held one would hold something anybody it was given to could change.
/// This keeps the immutable <see cref="FilterSetupSnapshot"/> the inputs were
/// read from instead, and reads the config off it — a new instance each time
/// — for every comparison and for the count itself. The filter's authoritative
/// state stays in <see cref="FilterSetup"/>; nothing here is a copy of it that
/// could drift.
/// </para>
///
/// <para>
/// <b>Equality is the config's value equality</b>
/// (<see cref="FilterConfig.Equals(FilterConfig?)"/>), with the token's and
/// the ranking's. Two snapshots of one in-effect filter — a remount's, or a
/// later snapshot whose change was elsewhere, a notice closed — are the same
/// inputs, which is what lets a navigate-back reuse the count it left.
/// </para>
/// </summary>
internal sealed class MatchCountInputs : IEquatable<MatchCountInputs>
{
    private readonly FilterSetupSnapshot _setup;

    private MatchCountInputs(FilterSetupSnapshot setup, FilterSourceToken selection, PlayRanking ranking)
    {
        _setup = setup;
        Selection = selection;
        Ranking = ranking;
    }

    /// <summary>The selection counted over: the pick, as Home mints its filter source.</summary>
    public FilterSourceToken Selection { get; }

    /// <summary>The ranking the count draws under.</summary>
    public PlayRanking Ranking { get; }

    /// <summary>
    /// The inputs for a count over <paramref name="selection"/>, or
    /// <see langword="null"/> when no filter is in effect for it in
    /// <paramref name="setup"/> — when there is nothing to count.
    /// </summary>
    /// <param name="setup">The filter setup as last published.</param>
    /// <param name="selection">Home's filter source for the pick on screen.</param>
    /// <param name="ranking">The user's ranking.</param>
    /// <returns>The inputs, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="setup"/> is <see langword="null"/>.</exception>
    public static MatchCountInputs? For(FilterSetupSnapshot setup, FilterSourceToken selection, PlayRanking ranking)
    {
        ArgumentNullException.ThrowIfNull(setup);
        return setup.IsInEffectFor(selection) ? new MatchCountInputs(setup, selection, ranking) : null;
    }

    /// <summary>
    /// The filter in effect, as a new instance the caller may hand on — the
    /// count's own config, which nothing else holds.
    /// </summary>
    public FilterConfig NewConfig() =>
        _setup.ConfigInEffectFor(Selection)
        ?? throw new InvalidOperationException("The snapshot these inputs were read from has no filter in effect.");

    /// <inheritdoc />
    public bool Equals(MatchCountInputs? other) =>
        other is not null
        && Selection == other.Selection
        && Ranking == other.Ranking
        && NewConfig().Equals(other.NewConfig());

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as MatchCountInputs);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Selection, Ranking, NewConfig());

    /// <summary>A description for diagnostics — logs and test failure messages.</summary>
    public override string ToString() => $"MatchCountInputs {{ Selection = {Selection}, Ranking = {Ranking} }}";
}
