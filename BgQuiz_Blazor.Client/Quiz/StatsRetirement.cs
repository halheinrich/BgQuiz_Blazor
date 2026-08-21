namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// What one run's stats retirement did, and the occurrence token its notice is
/// keyed on: a stats document in a retired schema version was found at bind,
/// copied aside under <see cref="SetAsideFileName"/> unparsed, and replaced by a
/// fresh current-version document (SPEC-stats-identity.md §3).
///
/// <para>
/// One object rather than a bare token beside a nullable name: the notice needs
/// both "did this run retire anything?" and "what name did the old file go to?",
/// and two nullable members could come apart. Minted only where the set-aside
/// has actually happened — see
/// <see cref="QuizStatsStore.StatsRetiredOccurrence"/>, which holds the single
/// instance and is null when this run retired nothing.
/// </para>
///
/// <para>
/// <b>A class, not a record, and deliberately.</b> The dismissal holder compares
/// occurrence tokens by <see cref="object.ReferenceEquals"/> precisely so a
/// later, value-identical occurrence still shows (see
/// <see cref="QuizNoticeDismissal"/>). Value equality here would mean a second
/// retirement that set aside the same name arrived pre-dismissed.
/// </para>
/// </summary>
internal sealed class StatsRetirement
{
    /// <summary>
    /// Construct the report for a set-aside that has already been written.
    /// </summary>
    /// <param name="setAsideFileName">
    /// The name the retired document was copied to — derived from the version it
    /// declared, via <see cref="QuizStatsFile.RetiredNameFor"/>.
    /// </param>
    internal StatsRetirement(string setAsideFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setAsideFileName);
        SetAsideFileName = setAsideFileName;
    }

    /// <summary>
    /// The name the user's previous stats document now lives under, in the same
    /// picked folder. The notices render this rather than a name of their own:
    /// what a run says it set aside has to be what that run actually wrote.
    /// </summary>
    internal string SetAsideFileName { get; }
}
