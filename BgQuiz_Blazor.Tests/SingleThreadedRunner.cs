using System.Collections.Concurrent;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Runs async work on the calling thread alone, as Blazor WebAssembly runs the
/// app: a synchronization context whose every continuation is queued back to
/// one pump on this thread, so no thread-pool thread ever runs, or keeps a
/// reference to, the state machines the work creates.
///
/// <para>
/// <b>Why a test needs it.</b> A retention test proves an object unreachable by
/// collecting and reading a <see cref="WeakReference"/>. Run on the thread pool,
/// the controller's continuations leave the last async state machine a worker
/// ran — with every local it hoisted, a run and its decisions included —
/// reachable from that idle worker until it runs something else, so the
/// object stays alive for a reason that exists only in the test host and the
/// pin fails at random (measured: different objects survived on different
/// runs). On one thread, once the work has completed and the pump has
/// returned, nothing but the code under test can hold what it made.
/// </para>
/// </summary>
internal static class SingleThreadedRunner
{
    /// <summary>Run <paramref name="work"/> to completion on this thread, rethrowing what it throws.</summary>
    public static void Run(Func<Task> work)
    {
        var previous = SynchronizationContext.Current;
        var context = new PumpContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var task = work();
            task.ContinueWith(_ => context.Complete(), TaskScheduler.Default);
            context.Pump();
            task.GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class PumpContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new NotSupportedException("The pump runs posted work only.");

        public void Complete() => _queue.CompleteAdding();

        public void Pump()
        {
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                callback(state);
        }
    }
}
