using DiscordRPC;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VK_UI3.Helpers;
using Windows.Media.Playback;

namespace VK_UI3.DiscordRPC
{
    /// <summary>
    /// Управляет Rich Presence в Discord.
    /// <para>
    /// ВАЖНО: ВСЕ операции с Discord IPC (Initialize/SetPresence/ClearPresence/Dispose)
    /// выполняются на ОДНОМ выделенном фоновом потоке. Named pipe работает синхронно,
    /// и если Discord не запущен, <c>Connect()</c> уходит в таймаут
    /// (<c>System.TimeoutException</c> в System.IO.Pipes.dll). Раньше эти вызовы
    /// делались из UI-потока (через подписчик AudioPlayedChangeEvent) — каждый
    /// таймаут подвешивал UI и копил задачи.
    /// </para>
    /// <para>
    /// Подключение выполняется ОДИН раз в фоне с backoff (не чаще раза в 30 с);
    /// повторные попытки не запускаются из событий OnError/OnClose
    /// (это давало каскад <c>Initialize()</c>).
    /// </para>
    /// </summary>
    public class DiscordRichPresenceManager : IDisposable
    {
        private const int UpdateInterval = 5000;

        // Ограничение размера очереди: если Discord не запущен и SetTrack
        // дёргается часто, очередь не должна расти бесконечно.
        private const int MaxQueueSize = 10;

        // Backoff между попытками ConnectClient, чтобы не долбить pipe,
        // когда Discord не запущен.
        private static readonly TimeSpan ConnectBackoff = TimeSpan.FromSeconds(30);

        private DiscordRpcClient _client;
        private readonly object _lock = new object();
        private bool _disposed;

        // Очередь операций для выделенного потока Discord IPC.
        private readonly Queue<WorkItem> _queue = new Queue<WorkItem>();
        private readonly object _queueLock = new object();
        private readonly AutoResetEvent _queueSignal = new AutoResetEvent(false);
        private readonly Thread _worker;
        private int _workerStopped;

        private string _currentTrack;
        private string _currentArtist;
        private string image;

        // Кэш текущего MediaPlayer для обновления Timestamps.
        private MediaPlayer media;

        // Флаг: подключение уже в процессе — новые Initialize не ставим.
        private bool _connecting;

        // Время последней попытки подключения — для backoff.
        private DateTime _lastConnectAttempt = DateTime.MinValue;

        private sealed class WorkItem
        {
            public Func<bool> Action;
            public TaskCompletionSource<bool> Completion;
        }

        public DiscordRichPresenceManager()
        {
            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "DiscordRPC-Worker"
            };
            _worker.Start();

            // Первичное подключение — в фоне, не блокируя конструктор (и UI).
            _ = Enqueue(() =>
            {
                ConnectClient();
                return true;
            });
        }

        #region Logging

        private static void Log(string msg)
        {
            // Console.WriteLine в WinUI-приложении не виден — используем Debug.
            System.Diagnostics.Debug.WriteLine(msg);
        }

        #endregion

        #region Worker plumbing

        private void WorkerLoop()
        {
            while (true)
            {
                WorkItem item = null;
                lock (_queueLock)
                {
                    if (_queue.Count > 0)
                        item = _queue.Dequeue();
                }

                if (item == null)
                {
                    if (Volatile.Read(ref _workerStopped) == 1)
                        return;

                    _queueSignal.WaitOne(50);
                    continue;
                }

                bool result = false;
                try
                {
                    result = item.Action != null && item.Action();
                }
                catch (Exception ex)
                {
                    Log($"[DiscordRPC] worker error: {ex.Message}");
                }
                finally
                {
                    item.Completion?.TrySetResult(result);
                }
            }
        }

        /// <summary>
        /// Ставит операцию в очередь на поток Discord IPC и возвращает Task,
        /// который завершится с таймаутом, чтобы вызывающий никогда не ждал вечно.
        /// При переполнении очереди отбрасывается самая старая задача — presence
        /// всё равно обновится следующим SetTrack.
        /// </summary>
        private Task<bool> Enqueue(Func<bool> action)
        {
            if (Volatile.Read(ref _workerStopped) == 1)
                return Task.FromResult(false);

            var item = new WorkItem
            {
                Action = action,
                Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            };

            bool dropped = false;
            lock (_queueLock)
            {
                if (_queue.Count >= MaxQueueSize)
                {
                    _queue.Dequeue();
                    dropped = true;
                }
                _queue.Enqueue(item);
            }

            if (dropped)
                Log("[DiscordRPC] queue overflow, dropped oldest item");

            _queueSignal.Set();

            // Таймаут на стороне вызывающего: даже если pipe «залип», UI не встанет.
            return item.Completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        #endregion

        #region Connection

        /// <summary>
        /// Выполняется на рабочем потоке. Не чаще раза в <see cref="ConnectBackoff"/>,
        /// чтобы не долбить pipe при отсутствующем Discord.
        /// </summary>
        private bool ConnectClient()
        {
            if (_disposed || _connecting)
                return false;

            // Backoff: если Discord не запущен, не пытаемся подключаться на каждый SetTrack.
            if ((DateTime.UtcNow - _lastConnectAttempt) < ConnectBackoff)
                return false;

            _connecting = true;
            _lastConnectAttempt = DateTime.UtcNow;
            try
            {
                try { _client?.Dispose(); } catch { }
                _client = null;

                _client = new DiscordRpcClient("1350750411811983440");

                _client.OnReady += (sender, e) =>
                {
                    Log($"[DiscordRPC] Connected to Discord as {e.User.Username}");
                };

                _client.OnError += (sender, e) =>
                {
                    // НЕ вызываем AttemptReconnect из события — иначе каскад попыток
                    // при отсутствующем Discord. Переподключение делает worker по требованию.
                    Log($"[DiscordRPC] error: {e.Message}");
                };

                _client.OnClose += (sender, e) =>
                {
                    Log("[DiscordRPC] Connection to Discord closed.");
                };

                if (!_client.IsInitialized && !_client.IsDisposed)
                {
                    // Здесь возможен System.TimeoutException (Discord не запущен).
                    _client.Initialize();
                    Log("[DiscordRPC] initialized successfully.");
                    return true;
                }
            }
            catch (TimeoutException)
            {
                // Discord не запущен — это норма. Никаких повторов из событий.
                Log("[DiscordRPC] Discord not running (pipe timeout).");
            }
            catch (Exception ex)
            {
                Log($"[DiscordRPC] init failed: {ex.Message}");
            }
            finally
            {
                _connecting = false;
            }

            return false;
        }

        /// <summary>
        /// Безопасная проверка, что клиент готов к работе.
        /// </summary>
        private bool IsClientReady()
        {
            try
            {
                return _client != null && !_client.IsDisposed && _client.IsInitialized;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Presence

        /// <summary>
        /// Обновляет статус в Discord. Выполняется на рабочем потоке.
        /// </summary>
        private bool UpdatePresenceInternal()
        {
            if (_disposed || !IsClientReady())
                return false;

            var mp = media;
            if (mp == null)
                return false;

            try
            {
                var position = TimeSpan.Zero;
                var duration = TimeSpan.Zero;
                try
                {
                    position = mp.Position;
                    duration = mp.NaturalDuration;
                }
                catch
                {
                    // MediaPlayer мог быть освобождён — не критично.
                }

                // Не даём End уйти в прошлое: если позиция уже за длительностью,
                // оставляем End = null (трек «закончился»).
                DateTime? endTimestamp = null;
                if (duration > TimeSpan.Zero)
                {
                    var remaining = duration - position;
                    if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

                    // Discord не любит End в прошлом — если remaining == 0, не ставим End.
                    if (remaining > TimeSpan.Zero)
                        endTimestamp = DateTime.UtcNow.AddSeconds(remaining.TotalSeconds);
                }

                var presence = new RichPresence()
                {
                    Details = _currentTrack ?? "Неизвестный трек",
                    State = _currentArtist ?? "Неизвестен",
                    Type = ActivityType.Listening,
                    Timestamps = new Timestamps()
                    {
                        Start = DateTime.UtcNow.AddSeconds(-position.TotalSeconds),
                        End = endTimestamp
                    },
                    Assets = new Assets()
                    {
                        LargeImageKey = image,
                        LargeImageText = "Music M",
                        SmallImageKey = null,
                        SmallImageText = "Слушает"
                    },
                    Buttons = new Button[]
                    {
                        new Button()
                        {
                            Label = "Слушать в Music M",
                            Url = "https://github.com/Kitsunex-glitch/Music-M"
                        },
                        new Button()
                        {
                            Label = "ТГ Канал",
                            Url = "https://t.me/music_m_rework"
                        }
                    }
                };

                _client.SetPresence(presence);
                return true;
            }
            catch (Exception ex)
            {
                Log($"[DiscordRPC] presence error: {ex.Message}");
                return false;
            }
        }

        #endregion

        /// <summary>
        /// Останавливает worker и освобождает ресурсы.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            // ВАЖНО: сначала ставим cleanup-задачу в очередь, ПОТОМ выставляем
            // _workerStopped. Раньше было наоборот: Enqueue видел _workerStopped == 1
            // и сразу возвращал Task.FromResult(false), поэтому ClearPresence/Dispose
            // никогда не выполнялись.
            Task<bool> cleanupTask = null;
            try
            {
                cleanupTask = Enqueue(() =>
                {
                    try
                    {
                        if (_client != null && _client.IsInitialized)
                            _client.ClearPresence();
                    }
                    catch (Exception ex)
                    {
                        Log($"[DiscordRPC] clear presence error: {ex.Message}");
                    }

                    try { _client?.Dispose(); } catch { }
                    _client = null;
                    return true;
                });
            }
            catch
            {
                // ignore
            }

            // Не ждём дольше 2 секунд при выходе приложения.
            try { cleanupTask?.Wait(TimeSpan.FromSeconds(2)); }
            catch { }

            // Теперь останавливаем worker.
            Interlocked.Exchange(ref _workerStopped, 1);
            _queueSignal.Set();

            lock (_lock)
            {
                _disposed = true;
                media = null;
            }
        }

        /// <summary>
        /// Задаёт текущий трек. НЕ блокирует вызывающий поток: все операции с IPC
        /// (включая возможное подключение) уходят на рабочий поток Discord RPC.
        /// </summary>
        internal void SetTrack(ExtendedAudio trackDataThis, MediaPlayer mediaPlayer)
        {
            // Читаем поля трека без обращения к IPC — это безопасно на UI-потоке.
            string track = trackDataThis?.audio?.Title ?? "Неизвестный трек";
            string artist = trackDataThis?.audio?.Artist ?? "Неизвестен";

            string img;
            if (trackDataThis?.audio?.Album?.Thumb != null)
            {
                var thumb = trackDataThis.audio.Album.Thumb;
                string photoUrl = thumb.Photo68 ?? thumb.Photo300 ?? thumb.Photo600 ?? thumb.Photo270 ?? thumb.Photo1200;
                img = !string.IsNullOrEmpty(photoUrl) ? $"mp:external/{photoUrl}" : null;
            }
            else
            {
                img = null;
            }

            lock (_lock)
            {
                if (_disposed) return;
                _currentTrack = track;
                _currentArtist = artist;
                image = img;
                media = mediaPlayer;
            }

            // Отправляем обновление на рабочий поток, не блокируя UI.
            _ = Enqueue(() =>
            {
                if (_disposed)
                    return false;

                if (!IsClientReady())
                {
                    // Клиент ещё не подключён (или отвалился) — пробуем подключиться
                    // с учётом backoff, затем сразу обновляем.
                    if (!ConnectClient())
                        return false;
                }

                return UpdatePresenceInternal();
            });
        }
    }
}