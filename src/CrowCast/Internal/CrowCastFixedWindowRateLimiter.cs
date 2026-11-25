namespace CrowCast.Internal;

internal sealed class CrowCastFixedWindowRateLimiter
{
    private readonly object _gate = new();
    private readonly int _permitLimit;
    private readonly TimeSpan _window;
    private DateTimeOffset _windowStartedAt;
    private int _permitsUsed;

    public CrowCastFixedWindowRateLimiter(
        int permitLimit,
        TimeSpan window,
        DateTimeOffset startedAt)
    {
        _permitLimit = permitLimit;
        _window = window;
        _windowStartedAt = startedAt;
    }

    public bool TryAcquire(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (now < _windowStartedAt || now - _windowStartedAt >= _window)
            {
                _windowStartedAt = now;
                _permitsUsed = 0;
            }

            if (_permitsUsed >= _permitLimit)
            {
                return false;
            }

            _permitsUsed++;
            return true;
        }
    }
}
