using BgDataTypes_Lib.TestSupport;
using BgUiPrimitives_Razor;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The one warning <see cref="ReportingBrowserStorage"/> writes for a refusal, as this app's
/// stores are pinned to say it: the shared template, with the store's own consequence phrase
/// and the browser's exception (halheinrich/backgammon#424). The template is BgUiPrimitives_Razor's
/// and pinned in its suite; it is restated once here so each store's sentence is read whole.
/// </summary>
internal static class StorageRefusalLog
{
    /// <summary>The sentence a refused <paramref name="operation"/> of <paramref name="key"/> is logged as.</summary>
    public static string Message(string operation, string key, string consequence) =>
        $"The browser refused a storage {operation} of the key {key}; {consequence}.";

    /// <summary>Exactly one entry: a warning carrying the browser's exception, in the store's words.</summary>
    public static void AssertSaidOnce(
        RecordingLogger<ReportingBrowserStorage> log, string operation, string key, string consequence)
    {
        var entry = Assert.Single(log.Entries);
        AssertSaid(entry, operation, key, consequence);
    }

    /// <summary>One entry as a refused <paramref name="operation"/> of <paramref name="key"/> is said.</summary>
    public static void AssertSaid(LogEntry entry, string operation, string key, string consequence)
    {
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<JSException>(entry.Exception);
        Assert.Equal(Message(operation, key, consequence), entry.Message);
    }
}
