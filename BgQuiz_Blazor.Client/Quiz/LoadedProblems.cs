namespace BgQuiz_Blazor.Client.Quiz;

using System.Collections.Immutable;
using BgDataTypes_Lib;
using XgFilter_Lib;

/// <summary>
/// What a folder's files parsed to: every readable decision that leaves the player a
/// choice, the report of the walk (files attempted and rejected), and how many checker
/// plays were set aside because the roll left exactly one legal play.
/// </summary>
internal sealed class LoadedProblems
{
    public LoadedProblems(ImmutableArray<BgDecisionData> problems, SourceReport report, int forcedPlaysSetAside)
    {
        if (problems.IsDefault)
            throw new ArgumentException("A default array holds nothing; none is an empty one.", nameof(problems));
        ArgumentNullException.ThrowIfNull(report);
        if (!report.IsComplete)
            throw new ArgumentException("Only a completed walk is a load: a walk that stopped early says nothing about the files it never reached.", nameof(report));
        ArgumentOutOfRangeException.ThrowIfNegative(forcedPlaysSetAside);
        Problems = problems;
        Report = report;
        ForcedPlaysSetAside = forcedPlaysSetAside;
    }

    /// <summary>The problems a quiz can ask, in file order.</summary>
    public ImmutableArray<BgDecisionData> Problems { get; }

    /// <summary>The walk that produced them: files attempted, files rejected and why.</summary>
    public SourceReport Report { get; }

    /// <summary>Checker plays with one legal play, which no quiz asks.</summary>
    public int ForcedPlaysSetAside { get; }
}
