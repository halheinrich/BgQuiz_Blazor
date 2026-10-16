namespace BgQuiz_Blazor.Client.Quiz;

using System.Text.Json;
using BgFolderAccess_Razor;
using BgGame_Lib;
using Microsoft.JSInterop;

/// <summary>
/// What the folder's stats file holds, read at the pick, kept on the setup and brought up to
/// date as a recording touches the file (<see cref="AfterRecording"/>): whether there is a document with
/// content (the fact the mix's visibility reads, SPEC-filtering.md §5: missing, empty and
/// unreadable are one answer), and the forecasts Home says before Start — a retired version
/// the next bind will set aside, a file that cannot be read, a file that cannot be written.
/// A probe reads and never writes; the set-aside is the bind's.
/// </summary>
internal sealed record StatsProbe(bool HasStats, int? RetiredSchemaVersion, bool Unreadable, bool Unwritable)
{
    /// <summary>Nothing found and nothing forecast: a folder that cannot hold stats.</summary>
    public static StatsProbe None { get; } = new(HasStats: false, RetiredSchemaVersion: null, Unreadable: false, Unwritable: false);

    /// <summary>The name the next bind will set a retired document aside under, or null when none is in prospect.</summary>
    public string? SetAsideForecast =>
        RetiredSchemaVersion is { } version ? QuizStatsFile.RetiredNameFor(version) : null;

    /// <summary>
    /// This probe once a recording has touched the file — at its bind, after each write. A
    /// recording that resolved the file (read it, set a retired one aside, folded an interim
    /// one in) has made the forecasts moot and knows whether it holds a record now; one that
    /// never did changed nothing, so the reading in hand stands. Writability is the pick's
    /// probe either way: a write that failed is the recording's report.
    /// </summary>
    public StatsProbe AfterRecording(QuizStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return stats.FileHasStats is { } hasStats
            ? new StatsProbe(hasStats, RetiredSchemaVersion: null, Unreadable: false, Unwritable)
            : this;
    }

    /// <summary>
    /// Probe <paramref name="folder"/>'s stats file. A folder without write capability is
    /// not read at all. A file that was found, readable or not, is asked whether it can be
    /// written; a browser failure on that probe forecasts nothing, since the bind and the
    /// writes report whatever is really wrong.
    /// </summary>
    public static async Task<StatsProbe> OfAsync(IFolderContext folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (folder.Capability != FolderWriteCapability.Enabled) return None;

        var read = await QuizStats.ReadFileAsync(folder);
        var hasStats = false;
        int? retired = null;
        var unreadable = false;
        switch (read.Kind)
        {
            case StatsFileKind.Absent:
                return None;
            case StatsFileKind.Current:
                hasStats = read.Document!.Count > 0;
                break;
            case StatsFileKind.Retired:
                retired = read.SchemaVersion;
                break;
            case StatsFileKind.Foldable:
                // Its records carry forward at the bind, so for the mix it is stats; a body
                // the fold reader rejects is unreadable in the bind's sense too.
                try
                {
                    hasStats = ProblemStatsDocument.ReadFoldable(read.Json!).Count > 0;
                }
                catch (JsonException)
                {
                    unreadable = true;
                }

                break;
            case StatsFileKind.Unreadable:
                unreadable = true;
                break;
        }

        var unwritable = false;
        try
        {
            unwritable = await folder.ProbeFileWritabilityAsync(QuizStatsFile.FileName) == FileWritability.NotWritable;
        }
        catch (JSException)
        {
            // Advisory: a probe the browser failed forecasts nothing.
        }

        return new StatsProbe(hasStats, retired, unreadable, unwritable);
    }
}
