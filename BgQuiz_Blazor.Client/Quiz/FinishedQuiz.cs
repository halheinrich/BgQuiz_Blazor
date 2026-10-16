namespace BgQuiz_Blazor.Client.Quiz;

using BgGame_Lib;
using XgFilter_Lib.Filtering;

/// <summary>
/// A run that has ended, kept compact: its result, what a Restart composes again from (the
/// filter and the mix it ran under, and the folder's name), and its stats report. The run's
/// problems and its folder context are released with it.
/// </summary>
internal sealed class FinishedQuiz
{
    public FinishedQuiz(
        QuizResult result, FilterConfig filters, QuizMix mix, string name, QuizStatsReport stats, bool endedByNewSample)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
        Filters = filters ?? throw new ArgumentNullException(nameof(filters));
        Mix = mix ?? throw new ArgumentNullException(nameof(mix));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Stats = stats ?? throw new ArgumentNullException(nameof(stats));
        EndedByNewSample = endedByNewSample;
    }

    public QuizResult Result { get; }
    public FilterConfig Filters { get; }

    /// <summary>The mix the run was composed with; blank when it was not weighted.</summary>
    public QuizMix Mix { get; }

    public string Name { get; }

    /// <summary>What the run's stats recording came to, for Done's notices.</summary>
    public QuizStatsReport Stats { get; }

    /// <summary>Whether a new folder or sample ended it, rather than the user or the last problem.</summary>
    public bool EndedByNewSample { get; }

    /// <summary>Whether the user dismissed Home's notice that a new sample ended it; a dismissal outlives the page.</summary>
    public bool EndedNoticeDismissed { get; set; }
}
