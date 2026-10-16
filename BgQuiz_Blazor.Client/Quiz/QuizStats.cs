namespace BgQuiz_Blazor.Client.Quiz;

using System.Text.Json;
using BgFolderAccess_Razor;
using BgGame_Lib;
using Microsoft.JSInterop;

/// <summary>What one read of a folder's stats file found.</summary>
internal enum StatsFileKind
{
    /// <summary>No file: a fresh folder.</summary>
    Absent,

    /// <summary>A document in the current schema, parsed.</summary>
    Current,

    /// <summary>A genuine document in a retired schema version, to be set aside unread at the bind.</summary>
    Retired,

    /// <summary>A genuine document in the foldable schema version (the interim v4), to be merged at the bind.</summary>
    Foldable,

    /// <summary>A file that could not be read: the browser failed, or the content would not parse.</summary>
    Unreadable,
}

/// <summary>One read of the stats file: its kind, and the text, document or version that kind carries.</summary>
internal sealed record StatsFileRead(StatsFileKind Kind, string? Json = null, ProblemStatsDocument? Document = null, int SchemaVersion = 0);

/// <summary>
/// A run's lifetime-stats recording into its folder: bound at Start with the folder the
/// setup was picked from, held by the run and by nothing else, and folding each answer of
/// record into the document and writing it back (small file; a lost tab loses nothing already
/// answered). The one place a stats document is read out of a folder and written into one.
///
/// <para>
/// Degrade, never block: no member throws for stats trouble. A file that cannot be read
/// records nothing and is never written (<see cref="QuizStatsStatus.LoadFailed"/>); a write
/// that fails keeps folding in memory and stops writing (<see cref="QuizStatsStatus.WriteFailed"/>).
/// The clock enters through the <see cref="TimeProvider"/>; ambient time is never read.
/// </para>
/// </summary>
internal sealed class QuizStats
{
    private readonly IFolderContext? _folder;
    private readonly TimeProvider _clock;
    private ProblemStatsDocument _document;

    private QuizStats(IFolderContext? folder, TimeProvider clock, ProblemStatsDocument document, QuizStatsReport report)
    {
        _folder = folder;
        _clock = clock;
        _document = document;
        Report = report;
    }

    /// <summary>What the recording came to, for the pages; it outlives the run on the finished quiz.</summary>
    public QuizStatsReport Report { get; }

    /// <summary>
    /// The live document, which a weighted composition reads, or null when this run records
    /// nothing. Non-null after a write failure, since folds continue in memory.
    /// </summary>
    public ProblemStatsDocument? Document =>
        Report.Status is QuizStatsStatus.Ready or QuizStatsStatus.WriteFailed ? _document : null;

    /// <summary>
    /// Bind the recording for a run over <paramref name="folder"/>: load the document, seed
    /// one for a fresh folder, set a retired one aside, or fold the interim one in. A folder
    /// without write capability records nothing (<see cref="QuizStatsStatus.Disabled"/>).
    /// Deliberately not gated on the folder holding stats: a fresh folder's first run is what
    /// creates the record a mix later composes from.
    /// </summary>
    public static async Task<QuizStats> BindAsync(IFolderContext folder, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(clock);
        if (folder.Capability != FolderWriteCapability.Enabled)
            return Inert(clock, QuizStatsStatus.Disabled);

        var read = await ReadFileAsync(folder);
        return read.Kind switch
        {
            StatsFileKind.Absent => Ready(folder, clock, ProblemStatsDocument.Empty, setAsideFileName: null),
            StatsFileKind.Current => Ready(folder, clock, read.Document!, setAsideFileName: null),
            StatsFileKind.Retired => await RetireAsync(folder, clock, read.Json!, read.SchemaVersion),
            StatsFileKind.Foldable => await FoldAsync(folder, clock, read.Json!, read.SchemaVersion),
            _ => Inert(clock, QuizStatsStatus.LoadFailed),
        };
    }

    /// <summary>
    /// Read <paramref name="folder"/>'s stats file and say what it is, without acting on it;
    /// the bind and the pick-time probe both read through here. A retired or foldable
    /// document is recognised by the producer's signals, which derive from
    /// <see cref="JsonException"/> and so are caught first.
    /// </summary>
    internal static async Task<StatsFileRead> ReadFileAsync(IFolderContext folder)
    {
        string? json;
        try
        {
            json = await folder.ReadFileAsync(QuizStatsFile.FileName);
        }
        catch (JSException)
        {
            return new StatsFileRead(StatsFileKind.Unreadable);
        }

        if (json is null) return new StatsFileRead(StatsFileKind.Absent);
        try
        {
            var document = JsonSerializer.Deserialize(json, QuizStatsFile.DocumentTypeInfo)
                ?? throw new JsonException("Stats document deserialized to null.");
            return new StatsFileRead(StatsFileKind.Current, json, document);
        }
        catch (RetiredStatsSchemaException retired)
        {
            return new StatsFileRead(StatsFileKind.Retired, json, SchemaVersion: retired.SchemaVersion);
        }
        catch (FoldableStatsSchemaException foldable)
        {
            return new StatsFileRead(StatsFileKind.Foldable, json, SchemaVersion: foldable.SchemaVersion);
        }
        catch (JsonException)
        {
            // Corrupt, foreign, or a newer schema than this build reads: never rewritten.
            return new StatsFileRead(StatsFileKind.Unreadable, json);
        }
    }

    /// <summary>
    /// Set a retired document aside under its version's name and seed a fresh one
    /// (SPEC-stats-identity.md §3). Set aside first, replace second: a copy that fails leaves
    /// the user's file exactly as it was, and a replace that fails leaves the retired document
    /// under the standard name for the next bind to recognise and retry.
    /// </summary>
    private static async Task<QuizStats> RetireAsync(IFolderContext folder, TimeProvider clock, string retiredJson, int schemaVersion)
    {
        var setAsideName = QuizStatsFile.RetiredNameFor(schemaVersion);
        try
        {
            await folder.WriteFileAsync(setAsideName, retiredJson);
            await folder.WriteFileAsync(QuizStatsFile.FileName, Serialize(ProblemStatsDocument.Empty));
        }
        catch (JSException)
        {
            return Inert(clock, QuizStatsStatus.LoadFailed);
        }

        return Ready(folder, clock, ProblemStatsDocument.Empty, setAsideName);
    }

    /// <summary>
    /// Fold the interim v4 document into the current one (SPEC-stats-identity.md §3, amended
    /// 2026-09-02): read it through the producer's fold reader, merge it into the set-aside
    /// v3 sibling where one exists, copy the v4 bytes aside as merged, then replace the
    /// standard name. Every read comes before any write, and the write order is the
    /// retirement's: a folder this build cannot read whole is not rewritten, and a failed
    /// replace is retried by the next bind. No retirement is reported: the record carries on.
    /// </summary>
    private static async Task<QuizStats> FoldAsync(IFolderContext folder, TimeProvider clock, string foldableJson, int schemaVersion)
    {
        var mergedName = QuizStatsFile.MergedNameFor(schemaVersion);
        var baseName = QuizStatsFile.RetiredNameFor(ProblemStatsDocument.CurrentSchemaVersion);

        ProblemStatsDocument merged;
        try
        {
            var folded = ProblemStatsDocument.ReadFoldable(foldableJson);
            var baseJson = await folder.ReadFileAsync(baseName);
            merged = baseJson is null
                ? folded
                : (JsonSerializer.Deserialize(baseJson, QuizStatsFile.DocumentTypeInfo)
                   ?? throw new JsonException("Stats base document deserialized to null."))
                  .Merge(folded);
        }
        catch (Exception ex) when (ex is JsonException or JSException)
        {
            return Inert(clock, QuizStatsStatus.LoadFailed);
        }

        try
        {
            await folder.WriteFileAsync(mergedName, foldableJson);
            await folder.WriteFileAsync(QuizStatsFile.FileName, Serialize(merged));
        }
        catch (JSException)
        {
            return Inert(clock, QuizStatsStatus.LoadFailed);
        }

        return Ready(folder, clock, merged, setAsideFileName: null);
    }

    private static QuizStats Ready(IFolderContext folder, TimeProvider clock, ProblemStatsDocument document, string? setAsideFileName) =>
        new(folder, clock, document, new QuizStatsReport(QuizStatsStatus.Ready, setAsideFileName));

    private static QuizStats Inert(TimeProvider clock, QuizStatsStatus status) =>
        new(folder: null, clock, ProblemStatsDocument.Empty, new QuizStatsReport(status, setAsideFileName: null));

    /// <summary>
    /// Fold <paramref name="answer"/> into the document and write it back. The pre-write guard
    /// (SPEC-stats-identity.md §5): the file is re-read and the fold applied to the document
    /// as it stands now, so a second recorder over the same folder (another tab, an external
    /// edit) loses only a same-instant race; any trouble reading it degrades to the in-memory
    /// document and changes nothing about the condition. Never throws.
    /// </summary>
    public async Task RecordAsync(AnswerOfRecord answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (Report.Status != QuizStatsStatus.Ready) return;

        var current = await ReadFoldBaseAsync();
        _document = answer.Match(
            play: play => current.Plus(play, _clock),
            cube: cube => current.Plus(cube, _clock));
        try
        {
            await _folder!.WriteFileAsync(QuizStatsFile.FileName, Serialize(_document));
        }
        catch (JSException)
        {
            Report.MarkWriteFailed();
        }
    }

    private async Task<ProblemStatsDocument> ReadFoldBaseAsync()
    {
        var read = await ReadFileAsync(_folder!);
        return read.Kind == StatsFileKind.Current ? read.Document! : _document;
    }

    private static string Serialize(ProblemStatsDocument document) =>
        JsonSerializer.Serialize(document, QuizStatsFile.DocumentTypeInfo);
}
