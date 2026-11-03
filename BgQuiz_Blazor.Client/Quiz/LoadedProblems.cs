namespace BgQuiz_Blazor.Client.Quiz;

using System.Collections.Immutable;
using BgDataTypes_Lib;
using XgFilter_Lib;

/// <summary>
/// What a folder's files parsed to: every decision the walk yielded, unfiltered, and the
/// report of that walk — the files it attempted and rejected, and what it left out of the
/// files it read: the games and plays it omitted and the forced plays it passed by
/// (<see cref="SourceReport"/>, the one owner of each of those facts).
/// </summary>
internal sealed class LoadedProblems
{
    public LoadedProblems(ImmutableArray<BgDecisionData> problems, SourceReport report)
    {
        if (problems.IsDefault)
            throw new ArgumentException("A default array holds nothing; none is an empty one.", nameof(problems));
        ArgumentNullException.ThrowIfNull(report);
        if (!report.IsComplete)
            throw new ArgumentException("Only a completed walk is a load: a walk that stopped early says nothing about the files it never reached.", nameof(report));
        Problems = problems;
        Report = report;
    }

    /// <summary>The problems a quiz can ask, in file order, before any filter: empty when every file was rejected or the files hold nothing to ask.</summary>
    public ImmutableArray<BgDecisionData> Problems { get; }

    /// <summary>The walk that produced them: files attempted, files rejected and why, games and plays omitted and why, forced plays passed by.</summary>
    public SourceReport Report { get; }
}
