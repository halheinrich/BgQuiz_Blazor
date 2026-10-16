namespace BgQuiz_Blazor.Client.Quiz;

using System.Collections.Immutable;
using BgDataTypes_Lib;
using BgFolderAccess_Razor;
using BgGame_Lib;
using BgMoveGen;
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
    /// Parse <paramref name="files"/> unfiltered, set forced plays aside, and report progress
    /// per file. Yields to the browser on the clock's budget so the page can paint the
    /// progress line; the parse itself is the one long operation the app has.
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
        var forced = 0;
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

        foreach (var decision in iterator.IterateXgStreamDiagrams(Streams(), report))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasNoPlayChoice(decision))
                forced++;
            else
                problems.Add(decision);
            await yielder.YieldIfDueAsync();
        }

        progress?.Invoke(new LoadProgress(files.Length, files.Length));
        return new LoadedProblems(problems.ToImmutable(), report, forced);
    }

    /// <summary>A checker play whose roll leaves exactly one legal play asks nothing.</summary>
    internal static bool HasNoPlayChoice(BgDecisionData decision) =>
        decision is CheckerPlayDecision checkerPlay
        && MoveGenerator.GeneratePlays(
            new BoardState(checkerPlay.Board), checkerPlay.Dice.High, checkerPlay.Dice.Low).Count == 1;
}
