namespace BgQuiz_Blazor.Client.Quiz;

using System.Runtime.CompilerServices;
using BgDataTypes_Lib;
using BgGame_Lib;

/// <summary>
/// The pool-composition guard: every money record that reaches the quiz states
/// which Jacoby rule was in force, or the folder does not load at all. Sits
/// innermost in <see cref="PickedFolderSourceFactory"/>'s stack, directly over
/// the parse-once layer, and either yields the pool unchanged or throws before
/// the first problem leaves it.
///
/// <para>
/// <b>The ruling it enforces</b> (<c>../SPEC-stats-identity.md</c> §2,
/// amended 2026-08-24; issue <c>halheinrich/backgammon#142</c>). Content
/// identity is <c>ProblemKey</c>, and a money key <i>spells</i> the Jacoby
/// fact — with a centered cube the rule voids undoubled gammons and shifts
/// the doubling window outright, so two money positions alike in every other
/// fact are two different problems. A money record that does not carry the
/// fact therefore has no key: <see cref="ProblemKey.TryDerive"/> refuses to
/// guess "off", dedupe passes the item through unmerged, and the stats
/// document declines to record what the user answered. All of that is
/// <i>silent</i> — the position quizzes normally and simply never counts.
/// </para>
///
/// <para>
/// <b>Why this one rung fails loud while its siblings still degrade.</b> The
/// other no-key shapes (unstamped dice, an empty board, a missing
/// <c>Xgid</c>) describe data a producer can plausibly emit, so silence
/// there is robustness. This shape is different in kind: the in-tree
/// converter stamps the fact onto every money record it writes, so no
/// producer in the ecosystem can emit it. Silence here therefore tolerates
/// exactly one thing — a converter defect — and reports nothing about it,
/// while the user loses lifetime stats for every money position in the
/// folder and is never told. Failing the load converts that into a named
/// error against a named file.
/// </para>
///
/// <para>
/// <b>Boundary-only.</b> The enforcement lives here and nowhere else: the
/// wire stays tolerant (<see cref="PositionData.IsJacoby"/> remains
/// <c>bool?</c> — a data type cannot name a file), and every rung below this
/// boundary keeps its degrade behaviour as defensive robustness. Nothing
/// about keying, dedupe or the stats fold changes; a folder that loads
/// composes exactly the pool it composed before.
/// </para>
///
/// <para>
/// <b>Beneath the dedupe, so every offending file is visible.</b> The layer
/// above collapses content-equal copies to one survivor, which would hide the
/// other files those copies came from — and naming files is this guard's
/// whole product. Sitting under it also means the pool is checked before any
/// layer reorders or drops from it.
/// </para>
///
/// <para>
/// <b>One pass, then the pool.</b> The inner enumeration is drained into a
/// buffer first, so every violating file is known before anything is yielded:
/// the error can say how many files are affected, and a quiz never starts on
/// a folder that is about to fail. The buffer holds references to records the
/// parse-once layer already has in memory, and the drain is the same single
/// filter pass the enumeration would have made lazily — it happens under
/// Start's busy affordance rather than between problems. Cancellation and
/// the inner's cooperative yielding are unaffected.
/// </para>
/// </summary>
internal sealed class JacobyStampedProblemSetSource : IProblemSetSource
{
    private readonly IProblemSetSource _inner;

    /// <summary>
    /// Guard <paramref name="inner"/>'s pool.
    /// </summary>
    /// <param name="inner">The source whose records form the quiz pool.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is null.</exception>
    public JacobyStampedProblemSetSource(IProblemSetSource inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc />
    public string Name => _inner.Name;

    /// <inheritdoc />
    /// <remarks>
    /// Forwarded unchanged: this layer admits every record or none, so it can
    /// never make a known total wrong.
    /// </remarks>
    public int? Count => _inner.Count;

    /// <summary>
    /// Yield <see cref="Name"/>'s pool once every money record in it carries
    /// the Jacoby fact.
    /// </summary>
    /// <param name="cancellationToken">Cancels the drain and the replay.</param>
    /// <returns>The inner pool, in order, unchanged.</returns>
    /// <exception cref="InvalidOperationException">
    /// A money record in the pool has no <see cref="PositionData.IsJacoby"/>.
    /// The message is
    /// <see cref="FolderPickDisplay.MalformedForQuizzing(string, int)"/> over
    /// the first offending file and the number of others — user-facing copy,
    /// because the existing folder-load error surface renders this message
    /// verbatim (Home's start-error banner).
    /// </exception>
    public async IAsyncEnumerable<BgDecisionData> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var pool = new List<BgDecisionData>();
        string? firstOffendingFile = null;
        HashSet<string>? offendingFiles = null;

        await foreach (var decision in _inner.EnumerateAsync(cancellationToken))
        {
            if (IsMoneyWithoutJacoby(decision))
            {
                // The file name comes from the record's Id, not from
                // Descriptive.SourceFile. Both carry the same bare filename by
                // producer contract, but Id is `required` and its Filename is a
                // validated non-null invariant, while SourceFile is optional
                // metadata that can be null — and an error whose whole job is
                // to name a file must never be the one that has no name to
                // give. Case-sensitive, matching DecisionId's own filename
                // equality.
                offendingFiles ??= new HashSet<string>(StringComparer.Ordinal);
                offendingFiles.Add(decision.Id.Filename);
                firstOffendingFile ??= decision.Id.Filename;
            }

            pool.Add(decision);
        }

        if (firstOffendingFile is not null)
        {
            throw new InvalidOperationException(
                FolderPickDisplay.MalformedForQuizzing(
                    firstOffendingFile, offendingFiles!.Count - 1));
        }

        foreach (var decision in pool)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return decision;
        }
    }

    /// <summary>
    /// The ruled shape, and only it: a money record — away scores
    /// <c>0</c>/<c>0</c>, the identity model's single source of that truth —
    /// whose Jacoby fact is not supplied.
    ///
    /// <para>
    /// Money is read off the away scores rather than
    /// <c>IDecisionFilterData.IsMoneyGame</c>'s match length, because this
    /// predicate has to fire on exactly the records
    /// <see cref="ProblemKey.TryDerive"/>'s money rung would silently drop,
    /// and that rung reads the away scores. A null
    /// <see cref="BgDecisionData.Position"/> — reachable only through lenient
    /// JSON — is not this shape and keeps its own degrade rung.
    /// </para>
    /// </summary>
    private static bool IsMoneyWithoutJacoby(BgDecisionData decision) =>
        decision.Position is { OnRollNeeds: 0, OpponentNeeds: 0, IsJacoby: null };
}
