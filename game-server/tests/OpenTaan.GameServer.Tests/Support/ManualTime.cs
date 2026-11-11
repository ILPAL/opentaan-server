namespace OpenTaan.Tests.GameServer;

/// <summary>
/// A clock that moves only when a test moves it, firing the server's timers as it passes
/// them, so that a test of a wait need not sleep through it.
/// </summary>
/// <remarks>
/// Timers fire on the thread that moves the clock, in the order they fall due, and never
/// while the clock's own lock is held, so a timer's work may take the server's locks.
/// Timers may be made and changed from any thread.
/// </remarks>
internal sealed class ManualTime : TimeProvider
{
    private readonly Lock _lock = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>How many timers are waiting to fire.</summary>
    public int Pending
    {
        get
        {
            lock (_lock)
            {
                return _timers.Count;
            }
        }
    }

    /// <summary>Moves the clock on by <paramref name="time"/>, firing every timer due by then.</summary>
    public void Advance(TimeSpan time)
    {
        DateTimeOffset until;
        lock (_lock)
        {
            until = _now + time;
        }

        while (true)
        {
            ManualTimer? next;
            lock (_lock)
            {
                next = _timers.Where(timer => timer.Due <= until).MinBy(timer => timer.Due);
                if (next is null)
                {
                    _now = until;
                    return;
                }

                _now = next.Due;
                next.Fall();
            }

            next.Fire();
        }
    }

    private sealed class ManualTimer(ManualTime clock, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public DateTimeOffset Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._lock)
            {
                clock._timers.Remove(this);
                _period = period;
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    Due = clock._now + dueTime;
                    clock._timers.Add(this);
                }

                return true;
            }
        }

        // Called with the clock's lock held, as the timer falls due: it waits again for a
        // period of its own, or is done.
        public void Fall()
        {
            clock._timers.Remove(this);
            if (_period != Timeout.InfiniteTimeSpan && _period > TimeSpan.Zero)
            {
                Due += _period;
                clock._timers.Add(this);
            }
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (clock._lock)
            {
                clock._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
