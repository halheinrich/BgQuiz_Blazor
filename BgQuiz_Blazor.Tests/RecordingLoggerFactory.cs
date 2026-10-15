using Microsoft.Extensions.Logging;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// A logger factory that keeps what every logger it makes is told, with the
/// category it was made for — for a type that takes a factory rather than a
/// logger, where a pin reads what was said in the log and by whom.
/// </summary>
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    public List<(string Category, LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

    /// <summary>The entries logged under <typeparamref name="T"/>'s category.</summary>
    public IReadOnlyList<(string Category, LogLevel Level, Exception? Exception, string Message)> For<T>() =>
        [.. Entries.Where(e => e.Category == typeof(T).FullName)];

    public ILogger CreateLogger(string categoryName) => new Recording(this, categoryName);

    public void AddProvider(ILoggerProvider provider) { }

    public void Dispose() { }

    private sealed class Recording(RecordingLoggerFactory owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            owner.Entries.Add((category, logLevel, exception, formatter(state, exception)));
    }
}
