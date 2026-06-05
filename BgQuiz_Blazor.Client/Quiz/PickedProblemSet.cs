namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// One browser-picked XG-format file, fully buffered into memory. The bytes are
/// read out of the <c>IBrowserFile</c> stream once, at pick time, and never
/// leave the browser — this is the in-memory payload the quiz parses locally.
/// </summary>
/// <param name="FileName">
/// The original file name <i>including</i> its extension (<c>.xg</c> /
/// <c>.xgp</c>). The extension is required: the producer's <c>DecisionId</c>
/// stamping discriminates the format from it. See <c>XgFileStream.FileName</c>.
/// </param>
/// <param name="Bytes">
/// The complete file contents. A fresh <c>MemoryStream(Bytes)</c> is minted for
/// each enumeration so the source stays re-iterable (the stream iterator reads a
/// stream exactly once, forward).
/// </param>
public sealed record PickedFile(string FileName, byte[] Bytes);

/// <summary>
/// Per-app holder for the user's browser-picked problem-set files.
///
/// <para>
/// Lifetime: <b>Scoped</b> — in the WebAssembly client that resolves to one
/// instance per loaded app (one tab), the same lifetime as
/// <see cref="QuizController"/>. It shuttles the picked files from
/// <c>Home.razor</c> (the writer) to the <see cref="ProblemSetSourceFactory"/>
/// registered in <c>Program.cs</c> (the reader), the same writer/reader seam the
/// retired server-disk directory holder provided for a filesystem path.
/// </para>
///
/// <para>
/// The files are buffered byte arrays rather than open streams: the
/// <c>IBrowserFile</c> streams are read once at pick time, so the source can
/// re-enumerate (Restart) by minting fresh <c>MemoryStream</c>s from the bytes.
/// </para>
/// </summary>
public sealed class PickedProblemSet
{
    /// <summary>The currently-picked files; empty until the user picks any.</summary>
    public IReadOnlyList<PickedFile> Files { get; private set; } = [];

    /// <summary>True once at least one file has been picked.</summary>
    public bool HasFiles => Files.Count > 0;

    /// <summary>Replace the picked set with <paramref name="files"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="files"/> is null.</exception>
    public void Set(IReadOnlyList<PickedFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        Files = files;
    }

    /// <summary>Clear the picked set (e.g. after a failed read).</summary>
    public void Clear() => Files = [];
}
