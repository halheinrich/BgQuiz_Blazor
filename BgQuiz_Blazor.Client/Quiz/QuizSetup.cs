namespace BgQuiz_Blazor.Client.Quiz;

using BgFolderAccess_Razor;
using XgFilter_Razor;

/// <summary>
/// The folder in hand and what it parsed to: the setup a quiz is composed from. One
/// filter source token per setup, so the shared filter owner's "in effect" answers are
/// about this folder and no earlier one.
/// </summary>
internal sealed class QuizSetup
{
    public QuizSetup(
        string folderName,
        FolderWriteCapability capability,
        FolderSample sample,
        LoadedProblems loaded,
        FilterSourceToken filterSource)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(loaded);
        if (sample.Files.IsEmpty)
            throw new ArgumentException("A setup holds a folder with at least one problem file.", nameof(sample));
        FolderName = folderName;
        Capability = capability;
        Sample = sample;
        Loaded = loaded;
        FilterSource = filterSource;
    }

    public string FolderName { get; }
    public FolderWriteCapability Capability { get; }
    public FolderSample Sample { get; }
    public LoadedProblems Loaded { get; }
    public FilterSourceToken FilterSource { get; }

    public string DisplayName => $"'{FolderName}'";

    public string Summary => Sample.Files.Length == 1
        ? $"{DisplayName} — 1 problem file"
        : $"{DisplayName} — {Sample.Files.Length} problem files";
}
