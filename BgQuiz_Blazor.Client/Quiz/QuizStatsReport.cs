namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>The condition of a run's lifetime-stats recording.</summary>
internal enum QuizStatsStatus
{
    /// <summary>No stats for this run: the folder cannot be written. Not a failure; no notice shows.</summary>
    Disabled,

    /// <summary>The document is loaded (or seeded) and every answer of record writes it back.</summary>
    Ready,

    /// <summary>The existing file could not be read; nothing records and the file is never written.</summary>
    LoadFailed,

    /// <summary>A write-back failed; folds continue in memory but no further write is attempted this run.</summary>
    WriteFailed,
}

/// <summary>
/// What a run's stats recording came to, for the Quiz and Done pages: its condition, the
/// name a retired document was set aside under at the bind, and the dismissals of the two
/// notices about them. It is the run's, and outlives it on the finished quiz, so a notice
/// dismissed on the quiz page stays dismissed on Done and the next run's report shows fresh
/// (SPEC-notices.md §2: the owner of the occurrence holds the dismissal).
/// </summary>
internal sealed class QuizStatsReport
{
    internal QuizStatsReport(QuizStatsStatus status, string? setAsideFileName)
    {
        Status = status;
        SetAsideFileName = setAsideFileName;
    }

    public QuizStatsStatus Status { get; private set; }

    /// <summary>The name the run set a retired document aside under, or null when it retired nothing.</summary>
    public string? SetAsideFileName { get; }

    public bool StatusNoticeDismissed { get; set; }

    public bool RetirementNoticeDismissed { get; set; }

    /// <summary>A write-back failed: the condition moves once, and its notice shows whatever was dismissed before.</summary>
    internal void MarkWriteFailed()
    {
        Status = QuizStatsStatus.WriteFailed;
        StatusNoticeDismissed = false;
    }
}
