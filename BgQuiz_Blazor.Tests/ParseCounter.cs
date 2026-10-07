using Microsoft.Extensions.Logging;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// A logger factory that counts the parse's per-file skip warnings. The pins
/// hand a source files the producer refuses — unparseable bytes, or a
/// truncated match (<see cref="TestFixtures.DamagedXg"/>) — which the parse
/// logs once per file per walk and skips, so a file's warning count is how
/// many times it was walked. The warning's state carries the file name as its
/// <c>File</c> value (the iterator's own structured argument), which is what
/// lets a pin tell two picks' parses apart. Parse counting rides this seam
/// because the holder keeps the pick's immutable array, which no test can
/// instrument.
/// </summary>
internal sealed class ParseCounter : ILoggerFactory
{
    private readonly List<string?> _files = [];

    /// <summary>Every parse counted, whichever file.</summary>
    public int Count => _files.Count;

    /// <summary>How many times <paramref name="fileName"/> was parsed.</summary>
    public int CountFor(string fileName) => _files.Count(f => f == fileName);

    public ILogger CreateLogger(string categoryName) => new Counting(this);

    public void AddProvider(ILoggerProvider provider) { }

    public void Dispose() { }

    private sealed class Counting(ParseCounter owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel != LogLevel.Warning) return;
            var file = state is IReadOnlyList<KeyValuePair<string, object?>> values
                ? values.FirstOrDefault(v => v.Key == "File").Value as string
                : null;
            owner._files.Add(file);
        }
    }
}
