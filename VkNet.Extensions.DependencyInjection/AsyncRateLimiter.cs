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

    // ⚡ Максимально быстрый асинхронный метод
    public async ValueTask WaitNextAsync(CancellationToken cancellationToken = default)
    {
        // Быстрая проверка без блокировки
        if (TryGetNext())
            return;

        // Медленный путь - ждем
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            while (true)
            {
                lock (_lock)
                {
                    var now = DateTime.UtcNow;

                    if (now - _windowStart >= _window)
                    {
                        _windowStart = now;
                        _requestsInWindow = 0;
                    }

                    if (_requestsInWindow < _maxRequestsPerWindow)
                    {
                        _requestsInWindow++;
                        return;
                    }
                }

                // Ждем минимально возможное время
                await Task.Delay(10, cancellationToken); // ⚡ всего 10мс вместо полного окна
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