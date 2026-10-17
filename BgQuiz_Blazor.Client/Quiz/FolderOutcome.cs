namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>What a folder operation came to, for the page that asked to report.</summary>
internal enum FolderOutcomeKind
{
    /// <summary>A new setup landed.</summary>
    Landed,

    /// <summary>Nothing began: another operation was in flight, or there was no folder to draw again.</summary>
    Refused,

    /// <summary>The picker was dismissed, or the required grant declined; the setup in hand is unchanged.</summary>
    Cancelled,

    /// <summary>The folder holds no problem files.</summary>
    NoFiles,

    /// <summary>The folder could not be read (<see cref="FolderOutcome.Failure"/>).</summary>
    Failed,

    /// <summary>The fallback picker is open; its selection arrives through the input's change event.</summary>
    AwaitingSelection,
}

/// <summary>What a folder operation came to: its kind, and the failure where the kind carries one.</summary>
internal sealed class FolderOutcome
{
    private FolderOutcome(FolderOutcomeKind kind, Exception? failure = null)
    {
        Kind = kind;
        Failure = failure;
    }

    public static FolderOutcome Landed { get; } = new(FolderOutcomeKind.Landed);
    public static FolderOutcome Refused { get; } = new(FolderOutcomeKind.Refused);
    public static FolderOutcome Cancelled { get; } = new(FolderOutcomeKind.Cancelled);
    public static FolderOutcome NoFiles { get; } = new(FolderOutcomeKind.NoFiles);
    public static FolderOutcome AwaitingSelection { get; } = new(FolderOutcomeKind.AwaitingSelection);

    public static FolderOutcome Failed(Exception failure) =>
        new(FolderOutcomeKind.Failed, failure ?? throw new ArgumentNullException(nameof(failure)));

    public FolderOutcomeKind Kind { get; }

    /// <summary>The failure, for <see cref="FolderOutcomeKind.Failed"/>; otherwise null.</summary>
    public Exception? Failure { get; }
}

/// <summary>What Done's resample-and-restart came to: the draw, and the restart where the draw landed.</summary>
internal readonly record struct ResampleRestartOutcome(FolderOutcome Draw, StartOutcome? Restart);
