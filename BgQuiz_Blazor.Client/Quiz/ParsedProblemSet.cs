namespace BgQuiz_Blazor.Client.Quiz;

using System.Collections.Immutable;
using BgDataTypes_Lib;
using XgFilter_Lib;

/// <summary>
/// One completed, unfiltered parse of a picked folder: every decision its
/// readable files yielded, together with the <see cref="SourceReport"/> of the
/// very walk that produced them — which files were attempted, which were
/// rejected and why (halheinrich/backgammon#368).
///
/// <para>
/// <b>Why one value.</b> The rejection facts exist exactly once per pick, at
/// the parse, and nowhere else: every later enumeration under any filter reads
/// the cached decisions and never walks the files again. A report kept apart
/// from its decisions could be stored without them, replaced without them, or
/// paired with another parse's decisions; a parse result that carries both
/// makes those states unrepresentable — the holder's <c>StoreParsed</c> takes
/// this type, so nothing can store one half, and pick lifecycle is the
/// diagnostics' lifecycle by construction.
/// </para>
///
/// <para>
/// <b>Only a finished walk is a parse result.</b> The report's own contract
/// draws its one conclusion, <see cref="SourceReport.AllRejected"/>, only from
/// a completed walk, and a partial walk's rejections say nothing about the
/// sources it never reached — so an incomplete report is refused here rather
/// than stored and later read as if it were whole. The constructor is the one
/// place that rule is spelled; a cancelled or thrown parse never reaches it.
/// </para>
///
/// <para>
/// A class rather than a record on purpose: the cache's identity is the parse,
/// not a copy of it — a source that adopted the holder's result holds the
/// same object, and tests pin that with reference equality.
/// </para>
/// </summary>
internal sealed class ParsedProblemSet
{
    /// <summary>
    /// Pair <paramref name="decisions"/> with the completed
    /// <paramref name="report"/> of the walk that produced them.
    /// </summary>
    /// <param name="decisions">Every decision the walk's readable files yielded, with no filters applied.</param>
    /// <param name="report">The walk's own report, complete — it reached the end of its sources.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="decisions"/> is a default array, which holds nothing at
    /// all (none is an empty one); or <paramref name="report"/> is not complete.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> is null.</exception>
    public ParsedProblemSet(ImmutableArray<BgDecisionData> decisions, SourceReport report)
    {
        if (decisions.IsDefault)
            throw new ArgumentException("A default array holds nothing; none is an empty one.", nameof(decisions));
        ArgumentNullException.ThrowIfNull(report);
        if (!report.IsComplete)
        {
            throw new ArgumentException(
                "Only a completed walk is a parse result: a report whose walk stopped early or threw " +
                "says nothing about the sources it never reached.",
                nameof(report));
        }

        Decisions = decisions;
        Report = report;
    }

    /// <summary>
    /// Every decision the readable files yielded, with <b>no filters
    /// applied</b>, in file order — so any filter config can be re-applied
    /// over it per enumeration.
    /// </summary>
    public ImmutableArray<BgDecisionData> Decisions { get; }

    /// <summary>
    /// What the walk did with its sources: attempted and readable counts, the
    /// rejected files with the exception each read raised, and
    /// <see cref="SourceReport.AllRejected"/>. Always complete (the
    /// constructor's contract), and never written again — the producer's
    /// one-walk rule means nothing can clear, append to or replace it.
    /// </summary>
    public SourceReport Report { get; }
}
