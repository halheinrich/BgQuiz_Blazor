namespace BgQuiz_Blazor.Tests;

/// <summary>
/// A clock that moves only when a test moves it (<see cref="Advance"/>), so a
/// wait on it — <c>Task.Delay(span, clock)</c> — ends exactly when the test
/// says and never on the wall clock. Its timers fire once, on the advance that
/// reaches them, on the thread that advanced; a period is not modelled, since
/// nothing here asks for one.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];

    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _now;

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Move the clock on by <paramref name="by"/>, firing every timer that comes due.</summary>
    public void Advance(TimeSpan by)
    {
        _now += by;
        foreach (var timer in _timers.Where(t => t.DueAt <= _now).ToList())
        {
            _timers.Remove(timer);
            timer.Fire();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            owner._timers.Remove(this);
            if (dueTime == Timeout.InfiniteTimeSpan) return true;
            DueAt = owner._now + dueTime;
            owner._timers.Add(this);
            return true;
        }

        public void Fire() => callback(state);

        public void Dispose() => owner._timers.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
