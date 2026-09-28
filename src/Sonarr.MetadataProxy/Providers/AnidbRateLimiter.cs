namespace Sonarr.MetadataProxy.Providers;

/// <summary>
/// Throttles requests to the AniDB HTTP API. AniDB asks clients to issue no
/// more than one request every two seconds and never to re-request identical
/// data within a day.
/// </summary>
public class AnidbRateLimiter
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(110);
    private const int MaxRequestsPerWindow = 55;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<DateTime> _requestedAt = new();
    private DateTime _lastRequest = DateTime.MinValue;

    public virtual async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;
            while (_requestedAt.Count > 0 && now - _requestedAt.Peek() >= Window)
            {
                _requestedAt.Dequeue();
            }

            var delay = TimeSpan.Zero;
            if (_requestedAt.Count >= MaxRequestsPerWindow)
            {
                delay = Window - (now - _requestedAt.Peek());
            }

            if (_lastRequest != DateTime.MinValue)
            {
                var sinceLast = now - _lastRequest;
                if (sinceLast < MinInterval)
                {
                    var gap = MinInterval - sinceLast;
                    if (gap > delay)
                    {
                        delay = gap;
                    }
                }
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            _requestedAt.Enqueue(DateTime.UtcNow);
            _lastRequest = DateTime.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }
}