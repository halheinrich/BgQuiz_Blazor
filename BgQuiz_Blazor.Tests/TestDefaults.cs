using System.Runtime.CompilerServices;
using Bunit;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Suite-wide defaults, applied once when the test assembly loads.
/// </summary>
internal static class TestDefaults
{
    /// <summary>
    /// bUnit's wait budget for <c>WaitForAssertion</c>, <c>WaitForState</c> and an unconfigured
    /// JSInterop handler. The library's default is one second, which is a timing assertion in
    /// disguise on a loaded runner: on CI, right after the client's AOT compile, a copy-control
    /// test timed out with its assertion never checked once ("Check count: 0", run 38952860962,
    /// 2026-10-16) and passed on the rerun. Five seconds is a bound on a stalled test, not a
    /// wait any passing test spends: a wait returns the moment its assertion holds.
    /// </summary>
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [ModuleInitializer]
    internal static void Apply() => BunitContext.DefaultWaitTimeout = WaitTimeout;
}
