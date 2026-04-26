namespace VkNet.Extensions.DependencyInjection;

public class AsyncRateLimiter : IAsyncRateLimiter
{
    private readonly SemaphoreSlim _semaphore;
    private readonly TimeSpan _window;
    private int _requestsInWindow;
    private DateTime _windowStart;
    private readonly int _maxRequestsPerWindow;
    private readonly object _lock = new();

    public AsyncRateLimiter(TimeSpan window, int maxRequestsPerWindow)
    {
        _window = window;
        _maxRequestsPerWindow = maxRequestsPerWindow;
        _semaphore = new SemaphoreSlim(1, 1);
        _windowStart = DateTime.UtcNow;
    }

    public TimeSpan Window => _window;
    public int MaxRequestsPerWindow => _maxRequestsPerWindow;

    // ⚡ Быстрый путь - без ожидания
    public bool TryGetNext()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;

            // Сброс окна
            if (now - _windowStart >= _window)
            {
                _windowStart = now;
                _requestsInWindow = 0;
            }

            // Проверяем лимит
            if (_requestsInWindow < _maxRequestsPerWindow)
            {
                _requestsInWindow++;
                return true;
            }

            return false;
        }
    }

    public async ValueTask WaitNextAsync(CancellationToken cancellationToken = default)
    {
        // Используем семафор для всей логики
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                var now = DateTime.UtcNow;
                lock (_lock)
                {
                    // Правильный сброс окна
                    if (now - _windowStart >= _window)
                    {
                        var excess = now - (_windowStart + _window);
                        _windowStart = excess > TimeSpan.Zero
                            ? now
                            : _windowStart + _window;
                        _requestsInWindow = 0;
                    }

                    if (_requestsInWindow < _maxRequestsPerWindow)
                    {
                        _requestsInWindow++;
                        return;
                    }
                }

                // Рассчитываем точное время ожидания
                var waitTime = _windowStart + _window - now;
                await Task.Delay(waitTime > TimeSpan.Zero ? waitTime : TimeSpan.FromMilliseconds(1), cancellationToken);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public ValueTask WaitNextAsync(int timeout) =>
        WaitNextAsync(TimeSpan.FromMilliseconds(timeout));

    public async ValueTask WaitNextAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        await WaitNextAsync(cts.Token);
    }

    public void Dispose() => _semaphore.Dispose();
}