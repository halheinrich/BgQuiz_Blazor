namespace BgQuiz_Blazor.Client.Quiz;

using BgFolderAccess_Razor;
using XgFilter_Razor;

/// <summary>
/// The folder in hand and what it parsed to: the setup a quiz is composed from, with the
/// folder's context (the stats destination a Start activates with its run), what its stats
/// file held at the pick, and one filter source token, so the shared filter owner's "in
/// effect" answers are about this folder and no earlier one.
/// </summary>
internal sealed class QuizSetup
{
    public QuizSetup(
        IFolderContext folder,
        FolderSample sample,
        LoadedProblems loaded,
        StatsProbe stats,
        FilterSourceToken filterSource)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(stats);
        if (sample.Files.IsEmpty)
            throw new ArgumentException("A setup holds a folder with at least one problem file.", nameof(sample));
        Folder = folder;
        Sample = sample;
        Loaded = loaded;
        Stats = stats;
        FilterSource = filterSource;
        // Saved filters are read and written in the folder where the browser holds a handle for it.
        Documents = folder.Capability == FolderWriteCapability.BrowserUnsupported ? null : new FolderDocumentStorage(folder);
    }

    /// <summary>The folder, held for as long as this setup or a run started from it exists.</summary>
    public IFolderContext Folder { get; }

    public string FolderName => Folder.Name;
    public FolderWriteCapability Capability => Folder.Capability;
    public FolderSample Sample { get; }
    public LoadedProblems Loaded { get; }

    /// <summary>
    /// What the folder's stats file holds — read at the pick, then as the last run from this
    /// setup left it: the mix's visibility fact and Home's forecasts.
    /// </summary>
    public StatsProbe Stats { get; private set; }

    /// <summary>A run from this setup has ended: what its recording learned of the file supersedes the reading in hand.</summary>
    public void NoteRunEnded(QuizStats stats) => Stats = Stats.AfterRun(stats);

    public FilterSourceToken FilterSource { get; }

    /// <summary>The folder as the filter surface keeps its saved filters in it; null when the browser holds no handle.</summary>
    public IDocumentStorage? Documents { get; }

    // Dismissals of the notices about this setup; each outlives the page (SPEC-notices.md §2).
    public bool TruncationNoticeDismissed { get; set; }
    public bool StatsCapabilityNoticeDismissed { get; set; }
    public bool StatsUnreadableNoticeDismissed { get; set; }
    public bool StatsUnwritableNoticeDismissed { get; set; }
    public bool StatsRetirementNoticeDismissed { get; set; }

    public string DisplayName => $"'{FolderName}'";

    /// <summary>The set's name, as the score panel shows it: the one file's name for a one-file pick, else the folder's.</summary>
    public string Name => Sample.Files.Length == 1 ? Sample.Files[0].FileName : FolderName;

    public string Summary => Sample.Files.Length == 1
        ? $"{DisplayName} — 1 problem file"
        : $"{DisplayName} — {Sample.Files.Length} problem files";
}
