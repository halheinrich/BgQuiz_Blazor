namespace BgQuiz_Blazor.Client.Quiz;

using XgFilter_Lib.Filtering;

/// <summary>
/// A run that has ended, kept compact: its result, and what a Restart needs to compose
/// again (the filter it ran under and the folder's name). The run's problems are released
/// with it.
/// </summary>
internal sealed class FinishedQuiz
{
    public FinishedQuiz(QuizResult result, FilterConfig filters, string name, bool endedByNewSample)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
        Filters = filters ?? throw new ArgumentNullException(nameof(filters));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        EndedByNewSample = endedByNewSample;
    }

    public QuizResult Result { get; }
    public FilterConfig Filters { get; }
    public string Name { get; }

    /// <summary>Whether a new folder or sample ended it, rather than the user or the last problem.</summary>
    public bool EndedByNewSample { get; }

    /// <summary>Whether the user dismissed Home's notice that a new sample ended it; a dismissal outlives the page.</summary>
    public bool EndedNoticeDismissed { get; set; }
}
