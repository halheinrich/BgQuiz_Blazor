namespace BgQuiz_Blazor.Client.Quiz;

using System.Collections.Immutable;
using BgDataTypes_Lib;
using BgFolderAccess_Razor;
using BgGame_Lib;
using Microsoft.Extensions.Logging;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;

/// <summary>How far a load has got: files read so far, of how many.</summary>
internal readonly record struct LoadProgress(int FilesRead, int FileCount);

/// <summary>Parses a picked folder's files into problems; the app's one seam for the parse (<see cref="ProblemLoader.LoadAsync"/> in production).</summary>
internal delegate Task<LoadedProblems> ProblemLoad(
    ImmutableArray<PickedFile> files, Action<LoadProgress>? progress, CancellationToken cancellationToken);

/// <summary>Parses a picked folder's files into <see cref="LoadedProblems"/>, once, at the pick.</summary>
internal static class ProblemLoader
{
    /// <summary>
    /// Parse <paramref name="files"/> unfiltered, keeping every decision the walk yields, and
    /// report progress per file. What the walk leaves out — rejected files, omitted games and
    /// plays, forced plays — is its report's, not this loader's (halheinrich/backgammon#514).
    /// Yields to the browser on the clock's budget so the page can paint the progress line;
    /// the parse itself is the one long operation the app has.
    /// </summary>
    public static async Task<LoadedProblems> LoadAsync(
        ImmutableArray<PickedFile> files,
        ILoggerFactory loggerFactory,
        TimeProvider clock,
        Action<LoadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (files.IsDefault)
            throw new ArgumentException("A default array holds no files; none is an empty one.", nameof(files));
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(clock);

        var report = new SourceReport();
        var iterator = new FilteredDecisionIterator(
            new FilterConfig().Build(), PlayRanking.Equity, loggerFactory.CreateLogger<FilteredDecisionIterator>());
        var yielder = new CooperativeYielder(clock);
        var problems = ImmutableArray.CreateBuilder<BgDecisionData>();
        var read = 0;

        IEnumerable<XgFileStream> Streams()
        {
            foreach (var file in files)
            {
                progress?.Invoke(new LoadProgress(read, files.Length));
                yield return new XgFileStream(file.FileName, file.OpenRead());
                read++;
            }
        }

        // The walk is synchronous, so the yield and the cancellation check can only sit between
        // the decisions it yields: a file's decode, and whatever of a file yields nothing, runs
        // through without either.
        foreach (var decision in iterator.IterateXgStreamDiagrams(Streams(), report))
        {
            cancellationToken.ThrowIfCancellationRequested();
            problems.Add(decision);
            await yielder.YieldIfDueAsync();
        }

        progress?.Invoke(new LoadProgress(files.Length, files.Length));
        return new LoadedProblems(problems.ToImmutable(), report);
    }
}
