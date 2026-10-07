using Microsoft.Extensions.Logging;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// A logger that keeps what it is told — each entry's level, its exception and
/// its rendered message — for the tests that pin that a degrade is said in the
/// log rather than thrown or kept silent.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, exception, formatter(state, exception)));
}
