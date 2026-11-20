using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using MusicX.Services.Player;
using MusicX.Services.Player.Sources;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VK_UI3.DB;
using VK_UI3.DownloadTrack;
using VK_UI3.Helpers;
using VK_UI3.Models;
using VK_UI3.Services.Player;
using VK_UI3.VKs;
using VK_UI3.VKs.IVK;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace VK_UI3.Services
{
    public class MediaPlayerService
    {
        #region Constants and Static Fields

        public const int MEDIA_KEY_DEBOUNCE_MS = 250;

        /// <summary>Таймаут ожидания списка треков от источника.</summary>
        private static readonly TimeSpan TrackListLoadTimeout = TimeSpan.FromSeconds(15);

        /// <summary>Таймаут получения данных трека от VK-клиента.</summary>
        private static readonly TimeSpan TrackDataTimeout = TimeSpan.FromSeconds(15);

        /// <summary>Таймаут открытия одного источника в LoadWithMediaSources.</summary>
        private static readonly TimeSpan SourceOpenTimeout = TimeSpan.FromSeconds(20);

        /// <summary>Таймаут ожидания освобождения семафора загрузки.</summary>
        private static readonly TimeSpan PlayTrackSemaphoreTimeout = TimeSpan.FromSeconds(30);

        public static readonly IEnumerable<ITrackMediaSource> _mediaSources = App._host.Services.GetRequiredService<IEnumerable<ITrackMediaSource>>();
        public static MediaPlayer _mediaPlayer = new MediaPlayer();
        public static IVKGetAudio _iVKGetAudio = null;
        public static AudioEqualizer _equalizer = null;
        public static ExtendedAudio _trackDataThis;
        public static ExtendedAudio _playingTrack = null;
        public static ExtendedAudio _nextTrack = null;
        public static CancellationTokenSource? _tokenSource;
        public static CancellationTokenSource? _nextTrackTokenSource;

        private static bool _isInitialized = false;
        private static Window _mainWindow;

        // Защита статических CTS и _loadGeneration от гонок между PlayTrackCore и LoadWithMediaSources.
        // Заменяет прежний _playTrackSemaphore на весь PlayTrackCore: теперь семафор не держится
        // на время сетевых вызовов, а критическая секция — только вокруг смены поколения и CTS.
        private static readonly object _tokenLock = new object();

        #endregion

        #region Events

        public static event EventHandler oniVKUpdate;
        public static event EventHandler onClickonTrack;
        public static event EventHandler AudioPlayedChangeEvent;
        public static event EventHandler<VolumeChangedEventArgs> VolumeChanged;
        public static event EventHandler<TimeSpan> PositionChanged;

        /// <summary>
        /// Трек не удалось воспроизвести (нет доступного потока / ошибка открытия).
        /// Передаёт трек, на котором произошла ошибка.
        /// </summary>
        public static event EventHandler PlaybackFailed;

        #endregion

        #region Properties

        /// <summary>
        /// Фактически использованный режим воспроизведения текущего трека.
        /// Может отличаться от выбранного в настройках, если нужного формата нет у трека.
        /// </summary>
        public static ResolvedStream CurrentPlaybackStream { get; private set; }

        /// <summary>
        /// Человекочитаемое описание фактического режима, например «AAC (откат от «FLAC»)».
        /// </summary>
        public static string CurrentPlaybackStreamName => CurrentPlaybackStream?.DisplayName;

        public static Windows.Media.Playback.MediaPlayer MediaPlayer
        {
            get { return _mediaPlayer; }
            set { _mediaPlayer = value; }
        }

        public static IVKGetAudio iVKGetAudio
        {
            get { return _iVKGetAudio; }
            set
            {
                if (_iVKGetAudio == value) return;
                _iVKGetAudio = value;
                NotifyoniVKUpdate();
            }
        }

        public static AudioEqualizer Equalizer
        {
            get { return _equalizer; }
            set
            {
                _equalizer = value;
                // Note: PlayTrack(position: mediaPlayer.Position); will need to be handled elsewhere
            }
        }

        public static ExtendedAudio PlayingTrack
        {
            get { return _playingTrack; }
            set { _playingTrack = value; }
        }

        private static double _volume = 1.0;
        public static double Volume
        {
            get => _volume;
            set
            {
                if (_volume != value)
                {
                    _volume = Math.Clamp(value, 0.0, 1.0);
                    _mediaPlayer.Volume = _volume;
                    RaiseOnUi(() => VolumeChanged?.Invoke(null, new VolumeChangedEventArgs(_volume)));
                }
            }
        }

        private static bool _isMuted = false;
        public static bool IsMuted
        {
            get => _isMuted;
            set
            {
                if (_isMuted != value)
                {
                    _isMuted = value;
                    _mediaPlayer.IsMuted = _isMuted;
                    RaiseOnUi(() => VolumeChanged?.Invoke(null, new VolumeChangedEventArgs(_volume, _isMuted)));
                }
            }
        }

        #endregion

        #region UI marshalling helpers

        /// <summary>
        /// Выполняет действие в UI-потоке главного окна (если окно доступно).
        /// Если мы уже в UI-потоке — выполняет синхронно.
        /// </summary>
        private static void RaiseOnUi(Action action)
        {
            if (action == null) return;

            var queue = MainWindow.mainWindow?.DispatcherQueue;
            if (queue == null)
            {
                // Окно ещё не создано (или уже закрыто) — выполняем как есть.
                action();
                return;
            }

            if (queue.HasThreadAccess)
            {
                action();
            }
            else
            {
                queue.TryEnqueue(() => action());
            }
        }

        #endregion

        #region Initialization and Cleanup

        public static void Initialize(Window window)
        {
            if (_isInitialized) return;
            _mainWindow = window;

            InitializeMediaPlayer();
            SetupSystemMediaTransportControls();

            _isInitialized = true;
        }

        private static void InitializeMediaPlayer()
        {
            try
            {
                _mediaPlayer.AudioCategory = MediaPlayerAudioCategory.Media;

                // CommandManager нужен для глобальной обработки медиа-клавиш через SMTC.
                _mediaPlayer.CommandManager.IsEnabled = true;

                _mediaPlayer.CommandManager.NextBehavior.EnablingRule = MediaCommandEnablingRule.Always;
                _mediaPlayer.CommandManager.PreviousBehavior.EnablingRule = MediaCommandEnablingRule.Always;
                _mediaPlayer.CommandManager.PlayBehavior.EnablingRule = MediaCommandEnablingRule.Always;
                _mediaPlayer.CommandManager.PauseBehavior.EnablingRule = MediaCommandEnablingRule.Always;

                _mediaPlayer.CommandManager.NextReceived += CommandManager_NextReceived;
                _mediaPlayer.CommandManager.PreviousReceived += CommandManager_PreviousReceived;
                _mediaPlayer.CommandManager.PauseReceived += CommandManager_PauseReceived;
                _mediaPlayer.CommandManager.PlayReceived += CommandManager_PlayReceived;

                _mediaPlayer.VolumeChanged += OnMediaPlayerVolumeChanged;
                _mediaPlayer.IsMutedChanged += OnMediaPlayerMuteChanged;
                _mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
                _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
                _mediaPlayer.CurrentStateChanged += MediaPlayer_CurrentStateChanged;
                _mediaPlayer.SourceChanged += MediaPlayer_SourceChanged;

                _mediaPlayer.PlaybackSession.PositionChanged += PlaybackSession_PositionChanged;
                _mediaPlayer.PlaybackSession.PlaybackStateChanged += PlaybackSession_PlaybackStateChanged;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing media player: {ex.Message}");
            }
        }

        private static void SetupSystemMediaTransportControls()
        {
            try
            {
                var systemControls = _mediaPlayer.SystemMediaTransportControls;

                systemControls.IsPlayEnabled = true;
                systemControls.IsPauseEnabled = true;
                systemControls.IsNextEnabled = true;
                systemControls.IsPreviousEnabled = true;
                systemControls.IsStopEnabled = true;

                systemControls.ButtonPressed += SystemControls_ButtonPressed;

                systemControls.DisplayUpdater.Type = MediaPlaybackType.Music;
                systemControls.DisplayUpdater.AppMediaId = "MusicM";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting up system media controls: {ex.Message}");
            }
        }

        private static void SystemControls_ButtonPressed(SystemMediaTransportControls sender,
            SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            RaiseOnUi(() =>
            {
                switch (args.Button)
                {
                    case SystemMediaTransportControlsButton.Play:
                        _mediaPlayer.Play();
                        break;
                    case SystemMediaTransportControlsButton.Pause:
                        _mediaPlayer.Pause();
                        break;
                    case SystemMediaTransportControlsButton.Next:
                        PlayNextTrack();
                        break;
                    case SystemMediaTransportControlsButton.Previous:
                        HandlePreviousTrack();
                        break;
                    case SystemMediaTransportControlsButton.Stop:
                        _mediaPlayer.Pause();
                        _mediaPlayer.Position = TimeSpan.Zero;
                        break;
                }
            });
        }

        #endregion

        #region Media Player Event Handlers

        private static void MediaPlayer_MediaOpened(Windows.Media.Playback.MediaPlayer sender, object args)
        {
            Debug.WriteLine(
                $"[PlayerDiag] MediaOpened gen={Volatile.Read(ref _loadGeneration)}, track='{PlayingTrack?.audio?.Title}', " +
                $"source={(sender.Source == null ? "null" : sender.Source.GetType().Name)}");

            if (PlayingTrack?.audio?.Duration > 0)
            {
                NotifyAudioPlayedChange(PlayingTrack);
            }
        }

        private static void MediaPlayer_MediaEnded(Windows.Media.Playback.MediaPlayer sender, object args)
        {
            var repeatMode = SettingsTable.GetSetting("playNext")?.settingValue ?? "RepeatAll";

            if (repeatMode == "RepeatOne")
            {
                // MediaEnded приходит в MTA-потоке медиаплеера.
                // PlayTrack сам уходит на пул, но инициируем явно через Task.Run,
                // чтобы не зависеть от контекста события.
                if (PlayingTrack != null)
                {
                    _ = Task.Run(() => PlayTrack());
                }
            }
            else
            {
                PlayNextTrack();
            }
        }

        private static void MediaPlayer_CurrentStateChanged(Windows.Media.Playback.MediaPlayer sender, object args)
        {
            // При необходимости — обновить UI состояния воспроизведения.
        }

        private static void MediaPlayer_SourceChanged(Windows.Media.Playback.MediaPlayer sender, object args)
        {
            Debug.WriteLine(
                $"[PlayerDiag] SourceChanged gen={Volatile.Read(ref _loadGeneration)}, " +
                $"source={(sender.Source == null ? "null" : sender.Source.GetType().Name)}, " +
                $"track='{PlayingTrack?.audio?.Title}'");

            if (PlayingTrack != null)
            {
                NotifyAudioPlayedChange(PlayingTrack);
            }
        }

        private static void PlaybackSession_PositionChanged(MediaPlaybackSession sender, object args)
        {
            var pos = sender.Position;
            RaiseOnUi(() => PositionChanged?.Invoke(null, pos));
        }

        private static void PlaybackSession_PlaybackStateChanged(MediaPlaybackSession sender, object args)
        {
            // При необходимости — обновить статус воспроизведения.
        }

        #endregion

        #region Command Manager Event Handlers

        private static void CommandManager_NextReceived(MediaPlaybackCommandManager sender,
            MediaPlaybackCommandManagerNextReceivedEventArgs args)
        {
            // GetForCurrentThread() в MTA-потоке CommandManager вернёт null —
            // раньше это давало NRE и команда «следующий трек» из SMTC терялась.
            RaiseOnUi(() => PlayNextTrack());
        }

        private static void CommandManager_PreviousReceived(MediaPlaybackCommandManager sender,
            MediaPlaybackCommandManagerPreviousReceivedEventArgs args)
        {
            RaiseOnUi(() => HandlePreviousTrack());
        }

        private static void CommandManager_PauseReceived(MediaPlaybackCommandManager sender,
            MediaPlaybackCommandManagerPauseReceivedEventArgs args)
        {
            RaiseOnUi(() => _mediaPlayer.Pause());
        }

        private static void CommandManager_PlayReceived(MediaPlaybackCommandManager sender,
            MediaPlaybackCommandManagerPlayReceivedEventArgs args)
        {
            RaiseOnUi(() => _mediaPlayer.Play());
        }

        #endregion

        #region Cleanup

        public static void Cleanup()
        {
            Debug.WriteLine("[MemoryLeakDebug] Cleanup started");
            if (!_isInitialized) return;

            if (_mediaPlayer != null)
            {
                _mediaPlayer.CommandManager.NextReceived -= CommandManager_NextReceived;
                _mediaPlayer.CommandManager.PreviousReceived -= CommandManager_PreviousReceived;
                _mediaPlayer.CommandManager.PauseReceived -= CommandManager_PauseReceived;
                _mediaPlayer.CommandManager.PlayReceived -= CommandManager_PlayReceived;

                var systemControls = _mediaPlayer.SystemMediaTransportControls;
                if (systemControls != null)
                {
                    systemControls.ButtonPressed -= SystemControls_ButtonPressed;
                }

                _mediaPlayer.VolumeChanged -= OnMediaPlayerVolumeChanged;
                _mediaPlayer.IsMutedChanged -= OnMediaPlayerMuteChanged;
                _mediaPlayer.MediaOpened -= MediaPlayer_MediaOpened;
                _mediaPlayer.MediaEnded -= MediaPlayer_MediaEnded;
                _mediaPlayer.CurrentStateChanged -= MediaPlayer_CurrentStateChanged;
                _mediaPlayer.SourceChanged -= MediaPlayer_SourceChanged;

                _mediaPlayer.PlaybackSession.PositionChanged -= PlaybackSession_PositionChanged;
                _mediaPlayer.PlaybackSession.PlaybackStateChanged -= PlaybackSession_PlaybackStateChanged;

                _mediaPlayer.Dispose();
                Debug.WriteLine("[MemoryLeakDebug] MediaPlayer disposed");
            }

            _isInitialized = false;
            Debug.WriteLine("[MemoryLeakDebug] Cleanup completed");
        }

        private static void OnMediaPlayerVolumeChanged(Windows.Media.Playback.MediaPlayer sender, object args)
        {
            _volume = sender.Volume;
            _isMuted = sender.IsMuted;
            RaiseOnUi(() => VolumeChanged?.Invoke(null, new VolumeChangedEventArgs(_volume, _isMuted)));
        }

        private static void OnMediaPlayerMuteChanged(Windows.Media.Playback.MediaPlayer sender, object args)
        {
            _isMuted = sender.IsMuted;
            RaiseOnUi(() => VolumeChanged?.Invoke(null, new VolumeChangedEventArgs(_volume, _isMuted)));
        }

        #endregion

        #region Media Key Handling

        private static async void HandleMediaKey(MediaKey key)
        {
            try
            {
                switch (key)
                {
                    case MediaKey.PlayPause:
                        await TogglePlayPause();
                        break;
                    case MediaKey.Next:
                        PlayNextTrack();
                        break;
                    case MediaKey.Previous:
                        HandlePreviousTrack();
                        break;
                    case MediaKey.Stop:
                        StopPlayback();
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaKey] HandleMediaKey error: {ex.Message}");
            }
            finally
            {
                try { await Task.Delay(MEDIA_KEY_DEBOUNCE_MS); }
                catch { /* ignore */ }
            }
        }

        public static async Task TogglePlayPause()
        {
            if (_mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            {
                _mediaPlayer.Pause();
            }
            else
            {
                if (_mediaPlayer.Source == null && iVKGetAudio?.currentTrack != null)
                {
                    _ = PlayTrack();
                }
                else
                {
                    _mediaPlayer.Play();
                }
            }
        }

        public static void StopPlayback()
        {
            _mediaPlayer.Pause();
            _mediaPlayer.PlaybackSession.Position = TimeSpan.Zero;
        }

        /// <summary>
        /// Запускает воспроизведение (или подгружает текущий трек, если ничего не загружено).
        /// </summary>
        public static void Play()
        {
            if (_mediaPlayer.Source == null && iVKGetAudio?.currentTrack != null)
            {
                _ = PlayTrack();
            }
            else
            {
                _mediaPlayer.Play();
            }
        }

        /// <summary>
        /// Ставит воспроизведение на паузу.
        /// </summary>
        public static void Pause()
        {
            _mediaPlayer.Pause();
        }

        /// <summary>
        /// Перематывает трек на указанное количество секунд (положительное — вперёд, отрицательное — назад).
        /// Позиция ограничивается диапазоном [0, длительность трека].
        /// </summary>
        public static void SeekBy(double seconds)
        {
            try
            {
                var session = _mediaPlayer.PlaybackSession;
                if (session == null) return;

                var target = session.Position + TimeSpan.FromSeconds(seconds);
                if (target < TimeSpan.Zero)
                    target = TimeSpan.Zero;

                var duration = session.NaturalDuration;
                if (duration > TimeSpan.Zero && target > duration)
                    target = duration;

                session.Position = target;
                Debug.WriteLine($"[MediaPlayerService] SeekBy {seconds:+0;-0}с -> {target}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaPlayerService] SeekBy error: {ex.Message}");
            }
        }

        /// <summary>
        /// Шаг перемотки в секундах из настроек (по умолчанию 10 секунд).
        /// </summary>
        public static int SeekStepSeconds
        {
            get
            {
                var setting = SettingsTable.GetSetting("SeekStepSeconds");
                if (setting != null && int.TryParse(setting.settingValue, out var value) && value > 0)
                    return value;
                return 10;
            }
        }

        #endregion

        #region Public Methods

        public static void NotifyoniVKUpdate()
        {
            RaiseOnUi(() => oniVKUpdate?.Invoke(null, EventArgs.Empty));
        }

        public static void NotifyonClickonTrack()
        {
            RaiseOnUi(() =>
            {
                if (onClickonTrack != null && PlayingTrack != null)
                {
                    onClickonTrack.Invoke(PlayingTrack, EventArgs.Empty);
                }
            });
        }

        public static void NotifyAudioPlayedChange(ExtendedAudio trackdata)
        {
            RaiseOnUi(() => AudioPlayedChangeEvent?.Invoke(trackdata, EventArgs.Empty));
        }

        private static readonly object _trackSwitchLock = new object();
        private static DateTime _lastTrackSwitchTime = DateTime.MinValue;

        public static void PlayNextTrack()
        {
            if (!Monitor.TryEnter(_trackSwitchLock))
            {
                Debug.WriteLine("[TrackSwitch] PlayNextTrack blocked by lock");
                return;
            }

            try
            {
                var now = DateTime.Now;
                if ((now - _lastTrackSwitchTime).TotalMilliseconds < MEDIA_KEY_DEBOUNCE_MS)
                {
                    Debug.WriteLine("[TrackSwitch] PlayNextTrack debounced");
                    return;
                }

                _lastTrackSwitchTime = now;

                // Смена индекса — в UI-потоке (setNextTrackForPlay меняет currentTrack,
                // который читается UI для подсветки). PlayTrack уходит на пул.
                RaiseOnUi(() => iVKGetAudio?.setNextTrackForPlay());
                _ = PlayTrack();
            }
            finally
            {
                Monitor.Exit(_trackSwitchLock);
            }
        }

        public static void PlayPreviousTrack()
        {
            if (!Monitor.TryEnter(_trackSwitchLock))
            {
                Debug.WriteLine("[TrackSwitch] PlayPreviousTrack blocked by lock");
                return;
            }

            try
            {
                var now = DateTime.Now;
                if ((now - _lastTrackSwitchTime).TotalMilliseconds < MEDIA_KEY_DEBOUNCE_MS)
                {
                    Debug.WriteLine("[TrackSwitch] PlayPreviousTrack debounced");
                    return;
                }

                _lastTrackSwitchTime = now;

                RaiseOnUi(() => iVKGetAudio?.setPreviusTrackForPlay());
                _ = PlayTrack();
            }
            finally
            {
                Monitor.Exit(_trackSwitchLock);
            }
        }

        public static async void PlayList(IVKGetAudio userAudio)
        {
            // PlayList вызывается из обработчика клика по треку на UI-потоке.
            // Ожидание списка уводим на пул, чтобы UI не подвисал.
            try
            {
                if (userAudio.listAudio.Count == 0)
                {
                    await Task.Run(async () =>
                    {
                        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        EventHandler handler = null;
                        handler = (sender, args) =>
                        {
                            userAudio.onListUpdate -= handler;
                            tcs.TrySetResult(true);
                        };
                        userAudio.onListUpdate += handler;

                        userAudio.GetTracks();

                        using var cts = new CancellationTokenSource(TrackListLoadTimeout);
                        using var reg = cts.Token.Register(() =>
                        {
                            userAudio.onListUpdate -= handler;
                            tcs.TrySetResult(false);
                        });

                        await tcs.Task;
                    });
                }

                if (userAudio.listAudio.Count == 0) return;

                iVKGetAudio = userAudio;
                _ = PlayTrack();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrackSwitch] PlayList error: {ex.Message}");
            }
        }

        #endregion

        #region Private Methods

        private static readonly object _previousTrackLock = new object();
        private static DateTime _lastPreviousTrackTime = DateTime.MinValue;

        /// <summary>
        /// Handles the previous track logic consistently across all input methods.
        /// </summary>
        public static void HandlePreviousTrack()
        {
            bool shouldGoPrevious = false;

            lock (_previousTrackLock)
            {
                var now = DateTime.Now;

                if ((now - _lastPreviousTrackTime).TotalMilliseconds < MEDIA_KEY_DEBOUNCE_MS)
                {
                    return;
                }

                _lastPreviousTrackTime = now;

                if (_mediaPlayer.Position.TotalSeconds >= 3)
                {
                    _mediaPlayer.Position = TimeSpan.Zero;
                    if (PlayingTrack != null)
                    {
                        _ = PlayTrack();
                    }
                }
                else
                {
                    shouldGoPrevious = true;
                }
            }

            if (shouldGoPrevious)
            {
                PlayPreviousTrack();
            }
        }

        /// <summary>
        /// Номер текущей попытки загрузки трека. Каждый новый PlayTrack увеличивает его;
        /// устаревшие загрузки видят чужое поколение и молча завершаются.
        /// </summary>
        private static int _loadGeneration;

        /// <summary>
        /// Запускает загрузку трека на пуле потоков. Прежний вариант держал
        /// <c>_playTrackSemaphore</c> на весь PlayTrackCore, и если внутри
        /// застревал сетевой вызов (VK, открытие источника), следующая попытка
        /// навсегда вставала на <c>WaitAsync()</c>. Теперь семафор убран:
        /// критическая секция — только вокруг смены поколения и CTS.
        /// </summary>
        private static Task PlayTrack(long? v = 0, TimeSpan? position = null)
        {
            return Task.Run(async () =>
            {
                try
                {
                    await PlayTrackCore(v, position);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[TrackSwitch] Unhandled error in PlayTrack: {ex.Message}");
                }
            });
        }

        private async static Task PlayTrackCore(long? v, TimeSpan? position)
        {
            if (iVKGetAudio == null)
                return;

            var sw = Stopwatch.StartNew();
            int generation;
            CancellationToken token;
            CancellationTokenSource oldCts;
            CancellationTokenSource oldNextCts;

            // Критическая секция — только смена поколения и CTS.
            // Семафор на весь PlayTrackCore больше не держим: если внутри
            // застрянет сетевой вызов, следующий PlayTrack не будет ждать вечно,
            // а увидит чужое поколение и выйдет.
            //
            // ВАЖНО: Cancel() вызывается ВНЕ lock. Отмена CTS синхронно прогоняет
            // все зарегистрированные callbacks (HTTP-отмена в VK-клиенте,
            // OpenWithMediaPlayerAsync и т.п.), и это может занять сотни мс
            // (в логе был случай critical section = 355ms). Под lock такие вызовы
            // заставляли бы следующий PlayTrack ждать на lock столько же.
            lock (_tokenLock)
            {
                generation = Interlocked.Increment(ref _loadGeneration);

                oldCts = _tokenSource;
                _tokenSource = new CancellationTokenSource();
                token = _tokenSource.Token;

                oldNextCts = _nextTrackTokenSource;
                _nextTrackTokenSource = null;
                _nextTrack = null;
            }
            Debug.WriteLine($"[SemDiag] gen={generation} critical section done ({sw.ElapsedMilliseconds}ms)");

            // Отмена старых CTS — после выхода из критической секции.
            var swCancel = Stopwatch.StartNew();
            try { oldCts?.Cancel(); } catch { }
            Debug.WriteLine($"[SemDiag] gen={generation} oldCts.Cancel took {swCancel.ElapsedMilliseconds}ms");

            swCancel.Restart();
            try { oldNextCts?.Cancel(); } catch { }
            Debug.WriteLine($"[SemDiag] gen={generation} oldNextCts.Cancel took {swCancel.ElapsedMilliseconds}ms");

            try { oldCts?.Dispose(); } catch { }
            try { oldNextCts?.Dispose(); } catch { }

            _mediaPlayer.Position = new TimeSpan(0);
            _mediaPlayer.Source = null;

            sw.Restart();
            await EnsureTrackListLoaded();
            Debug.WriteLine($"[SemDiag] gen={generation} EnsureTrackListLoaded done ({sw.ElapsedMilliseconds}ms)");
            if (generation != Volatile.Read(ref _loadGeneration)) { Debug.WriteLine($"[SemDiag] gen={generation} superseded after list"); return; }

            if (iVKGetAudio.listAudio.Count == 0)
            {
                Debug.WriteLine($"[SemDiag] gen={generation} list empty, exit");
                return;
            }

            if (v != null && iVKGetAudio.currentTrack == null)
            {
                long vv = (long)v;
                RaiseOnUi(() => iVKGetAudio.currentTrack = vv);
            }

            sw.Restart();
            ExtendedAudio trackdata;
            try
            {
                var dataTask = _TrackDataThisGet(true);
                var completed = await Task.WhenAny(dataTask, Task.Delay(TrackDataTimeout, token));
                if (completed != dataTask)
                {
                    Debug.WriteLine($"[SemDiag] gen={generation} _TrackDataThisGet TIMEOUT after {sw.ElapsedMilliseconds}ms");
                    return;
                }
                trackdata = await dataTask;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[SemDiag] gen={generation} _TrackDataThisGet cancelled");
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SemDiag] gen={generation} _TrackDataThisGet error: {ex.Message}");
                return;
            }
            Debug.WriteLine($"[SemDiag] gen={generation} _TrackDataThisGet done ({sw.ElapsedMilliseconds}ms)");

            if (trackdata == null) { Debug.WriteLine($"[SemDiag] gen={generation} trackdata null"); return; }
            if (generation != Volatile.Read(ref _loadGeneration)) { Debug.WriteLine($"[SemDiag] gen={generation} superseded after data"); return; }

            SendPlaybackStatistics(trackdata);

            // SendVKStartEvent — синхронный VK-вызов. Уводим на пул,
            // чтобы даже зависший VK-клиент не блокировал загрузку трека.
            sw.Restart();
            _ = Task.Run(() => SendVKStartEvent(trackdata));
            Debug.WriteLine($"[SemDiag] gen={generation} SendVKStartEvent scheduled ({sw.ElapsedMilliseconds}ms)");

            _ = SetVKStatusAsync(trackdata);
            UpdateSystemMediaDisplay(trackdata);

            sw.Restart();
            bool skip;
            try
            {
                skip = ShouldSkipTrack(trackdata);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SemDiag] gen={generation} ShouldSkipTrack error: {ex.Message}");
                skip = false;
            }
            Debug.WriteLine($"[SemDiag] gen={generation} ShouldSkipTrack done ({sw.ElapsedMilliseconds}ms, skip={skip})");

            if (skip)
            {
                PlayNextTrack();
                return;
            }

            sw.Restart();
            try
            {
                await LoadAndPlayTrack(trackdata, position, generation, token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrackSwitch] Error in PlayTrack loading track: {ex.Message}");
            }
            Debug.WriteLine($"[SemDiag] gen={generation} LoadAndPlayTrack returned ({sw.ElapsedMilliseconds}ms)");
        }

        private static async Task SetVKStatusAsync(ExtendedAudio trackdata)
        {
            try
            {
                var share = SettingsTable.GetSetting("shareFriend");
                if (share?.settingValue == "true" && trackdata != null)
                {
                    await VK.api.Audio.SetBroadcastAsync(
                        trackdata.audio.OwnerId + "_" + trackdata.audio.Id);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrackSwitch] SetVKStatus error: {ex.Message}");
            }
        }

        /// <summary>
        /// Ждёт загрузку списка треков, если он ещё пуст.
        /// <para>
        /// ВАЖНО: раньше здесь был «голый» <c>await tcs.Task</c> без таймаута.
        /// Если <c>GetTracks()</c> падал до <c>onListUpdate</c> или не поднимал
        /// событие, ожидание висело вечно, а вместе с ним — держатель семафора.
        /// Теперь ждём не дольше <see cref="TrackListLoadTimeout"/>, после чего
        /// отписываемся и выходим.
        /// </para>
        /// </summary>
        private static async Task EnsureTrackListLoaded()
        {
            if (iVKGetAudio.listAudio.Count == 0)
            {
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler handler = null;
                handler = (sender, args) =>
                {
                    iVKGetAudio.onListUpdate -= handler;
                    tcs.TrySetResult(true);
                };
                iVKGetAudio.onListUpdate += handler;

                try
                {
                    iVKGetAudio.GetTracks();
                }
                catch (Exception ex)
                {
                    iVKGetAudio.onListUpdate -= handler;
                    Debug.WriteLine($"[TrackSwitch] GetTracks error: {ex.Message}");
                    return;
                }

                using var cts = new CancellationTokenSource(TrackListLoadTimeout);
                using var reg = cts.Token.Register(() =>
                {
                    iVKGetAudio.onListUpdate -= handler;
                    tcs.TrySetResult(false);
                });

                bool ok = await tcs.Task;
                if (!ok)
                {
                    Debug.WriteLine($"[TrackSwitch] EnsureTrackListLoaded timeout after {TrackListLoadTimeout.TotalSeconds}s");
                }
            }
        }

        private static void SendPlaybackStatistics(ExtendedAudio trackdata)
        {
            ExtendedAudio previousTrack = PlayingTrack;
            int? previousTrackPlayedSeconds = null;

            if (previousTrack != null)
            {
                previousTrackPlayedSeconds = (int)_mediaPlayer.Position.TotalSeconds;
            }

            _ = KrotosVK.sendVKAudioPlayStat(
                trackdata,
                previousTrack,
                previousTrackPlayedSeconds
            );
        }

        private static void SendVKStartEvent(ExtendedAudio trackdata)
        {
            try
            {
                if (iVKGetAudio is PlayListVK playlistVK)
                {
                    VK.sendStartEvent((long)trackdata.audio.Id,
                        (long)trackdata.audio.OwnerId,
                        playlistVK.playlist.Id);
                }
                else
                {
                    VK.sendStartEvent((long)trackdata.audio.Id,
                        (long)trackdata.audio.OwnerId);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrackSwitch] SendVKStartEvent error: {ex.Message}");
            }
        }

        private static bool ShouldSkipTrack(ExtendedAudio trackdata)
        {
            return trackdata?.audio == null ||
                   !AudioPlaybackResolver.HasPlayableSource(trackdata.audio) ||
                   new DB.SkipPerformerDB().skipIsSet(trackdata.audio.Artist);
        }

        private static async Task LoadAndPlayTrack(ExtendedAudio trackdata, TimeSpan? position, int generation, CancellationToken token)
        {
            Debug.WriteLine($"[TrackSwitch] LoadAndPlayTrack started gen={generation} for track: {trackdata.audio.Title}");

            // Освобождаем предыдущие ресурсы перед загрузкой нового трека
            if (_mediaPlayer.Source is MediaPlaybackItem oldItem)
            {
                oldItem.AudioTracksChanged -= MediaPlaybackItem_AudioTracksChanged;
                oldItem.TimedMetadataTracksChanged -= MediaPlaybackItem_TimedMetadataTracksChanged;
            }

            try
            {
                var resolved = MediaSourceBase.Resolve(trackdata.audio);

                // Последний резерв: если резолвер не нашёл поток, но у трека есть прямой URL —
                // играем его.
                if (!resolved.IsValid && trackdata.audio?.Url != null)
                {
                    resolved = new ResolvedStream
                    {
                        Url = trackdata.audio.Url,
                        StreamType = "mp3",
                        RequestedType = PlaybackTypeHelper.GetSelected(),
                        ActualType = PlaybackType.Mp3,
                        IsFallback = true,
                        FallbackReason = "резолвер не нашёл поток, использован прямой URL трека"
                    };
                }

                if (!resolved.IsValid)
                {
                    Debug.WriteLine($"[TrackSwitch] No playable stream gen={generation}: {resolved.FallbackReason}");
                    NotifyPlaybackFailed(trackdata);
                    PlayNextTrack();
                    return;
                }

                CurrentPlaybackStream = resolved;

                // Сообщаем UI о новом треке СРАЗУ, до сетевой загрузки источника.
                PlayingTrack = trackdata;
                _mediaPlayer.Pause();

                RaiseOnUi(() => iVKGetAudio?.ChangePlayAudio(trackdata));
                NotifyAudioPlayedChange(trackdata);
                UpdateSystemMediaDisplay(trackdata);

                if (generation != Volatile.Read(ref _loadGeneration)) { Debug.WriteLine($"[SemDiag] gen={generation} superseded before load"); return; }

                var mediaSource = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(resolved.Url.ToString()));
                var mediaPlaybackItem = new Windows.Media.Playback.MediaPlaybackItem(mediaSource);

                mediaPlaybackItem.AudioTracksChanged += MediaPlaybackItem_AudioTracksChanged;
                mediaPlaybackItem.TimedMetadataTracksChanged += MediaPlaybackItem_TimedMetadataTracksChanged;

                Debug.WriteLine($"[TrackSwitch] Before LoadWithMediaSources/LoadBasicMediaItem gen={generation}");

                bool ffmpegExists = MediaSourceBase.IsFfmpegAvailable();
                bool useFfmpeg = true;
                var setting = SettingsTable.GetSetting("useFfmpegForPlayback");
                if (setting != null && setting.settingValue == "0")
                {
                    useFfmpeg = false;
                }

                if (ffmpegExists && useFfmpeg)
                {
                    Debug.WriteLine($"[TrackSwitch] Using FFmpeg path gen={generation}");
                    await LoadWithMediaSources(trackdata, generation, token);
                }
                else
                {
                    Debug.WriteLine($"[TrackSwitch] Using basic media item path gen={generation}");
                    LoadBasicMediaItem(trackdata, mediaPlaybackItem);
                }

                Debug.WriteLine($"[TrackSwitch] After LoadWithMediaSources/LoadBasicMediaItem gen={generation}");

                if (generation != Volatile.Read(ref _loadGeneration))
                {
                    Debug.WriteLine($"[SemDiag] gen={generation} superseded before Play()");
                    return;
                }

                if (position != null)
                {
                    _mediaPlayer.Position = new TimeSpan(0);
                }

                _mediaPlayer.Play();

                Debug.WriteLine($"[TrackSwitch] LoadAndPlayTrack completed gen={generation} for track: {trackdata.audio.Title}");

                // Начинаем предзагрузку следующего трека
                //_ = PreloadNextTrack();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrackSwitch] Error in LoadAndPlayTrack gen={generation}: {ex.Message}");
                NotifyPlaybackFailed(trackdata);
            }
        }

        /// <summary>
        /// Сообщает UI, что текущий трек не удалось воспроизвести.
        /// </summary>
        private static void NotifyPlaybackFailed(ExtendedAudio trackdata)
        {
            RaiseOnUi(() =>
            {
                try
                {
                    PlaybackFailed?.Invoke(trackdata, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[TrackSwitch] PlaybackFailed handler error: {ex.Message}");
                }
            });
        }

        private static void UpdateSystemMediaDisplay(ExtendedAudio trackdata)
        {
            if (trackdata is null) return;

            // SMTC DisplayUpdater/PlaybackStatus требуют UI-поток.
            RaiseOnUi(() =>
            {
                try
                {
                    var systemControls = _mediaPlayer.SystemMediaTransportControls;
                    var updater = systemControls.DisplayUpdater;

                    updater.ClearAll();
                    updater.Type = MediaPlaybackType.Music;
                    updater.MusicProperties.Title = trackdata.audio.Title ?? "Unknown Title";
                    updater.MusicProperties.Artist = trackdata.audio.Artist ?? "Unknown Artist";

                    if (trackdata.audio.Album != null)
                    {
                        updater.MusicProperties.AlbumTitle = trackdata.audio.Album.Title ?? string.Empty;
                    }

                    if (trackdata.audio.Album?.Thumb != null)
                    {
                        var thumbnailUrl = trackdata.audio.Album.Thumb.Photo600 ??
                                         trackdata.audio.Album.Thumb.Photo300 ??
                                         trackdata.audio.Album.Thumb.Photo270;

                        if (thumbnailUrl != null)
                        {
                            try
                            {
                                RandomAccessStreamReference imageStreamRef =
                                    RandomAccessStreamReference.CreateFromUri(new Uri(thumbnailUrl));
                                updater.Thumbnail = imageStreamRef;
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error setting thumbnail: {ex.Message}");
                            }
                        }
                    }

                    updater.Update();

                    systemControls.PlaybackStatus = _mediaPlayer.PlaybackSession.PlaybackState switch
                    {
                        MediaPlaybackState.Playing => MediaPlaybackStatus.Playing,
                        MediaPlaybackState.Paused => MediaPlaybackStatus.Paused,
                        MediaPlaybackState.Buffering => MediaPlaybackStatus.Changing,
                        _ => MediaPlaybackStatus.Stopped
                    };

                    AudioPlayedChangeEvent?.Invoke(trackdata, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error updating system media display: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Открывает трек через первый рабочий источник.
        /// <para>
        /// Источники пробуются ПОСЛЕДОВАТЕЛЬНО (раньше — параллельно через Task.WhenAll,
        /// из-за чего побеждал последний, а объекты освобождались обработчиком другого).
        /// Каждый <c>OpenWithMediaPlayerAsync</c> ограничен <see cref="SourceOpenTimeout"/>.
        /// </para>
        /// </summary>
        private static async Task LoadWithMediaSources(ExtendedAudio trackdata, int generation, CancellationToken token)
        {
            Debug.WriteLine($"[MemoryLeakDebug] LoadWithMediaSources started gen={generation} for track: {trackdata.audio.Title}");

            bool anySucceeded = false;
            string lastError = null;

            foreach (var source in _mediaSources)
            {
                if (token.IsCancellationRequested || generation != Volatile.Read(ref _loadGeneration))
                {
                    Debug.WriteLine($"[MemoryLeakDebug] LoadWithMediaSources cancelled gen={generation} for track: {trackdata.audio.Title}");
                    return;
                }

                var sw = Stopwatch.StartNew();
                try
                {
                    var openTask = source.OpenWithMediaPlayerAsync(
                        _mediaPlayer, trackdata.audio, token, equalizer: _equalizer);

                    var completed = await Task.WhenAny(openTask, Task.Delay(SourceOpenTimeout, token));
                    if (completed != openTask)
                    {
                        Debug.WriteLine($"[MemoryLeakDebug] Source {source.GetType().Name} TIMEOUT after {sw.ElapsedMilliseconds}ms gen={generation}");
                        continue;   // пробуем следующий источник
                    }

                    var opened = await openTask;
                    if (opened)
                    {
                        anySucceeded = true;
                        Debug.WriteLine($"[MemoryLeakDebug] Source {source.GetType().Name} opened in {sw.ElapsedMilliseconds}ms gen={generation}");
                        break;
                    }

                    Debug.WriteLine($"[MemoryLeakDebug] Source {source.GetType().Name} returned false gen={generation}");
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine($"[MemoryLeakDebug] Source {source.GetType().Name} cancelled gen={generation}");
                    return;
                }
                catch (Exception ex)
                {
                    lastError = $"{source.GetType().Name}: {ex.Message}";
                    Debug.WriteLine($"[MemoryLeakDebug] Source {source.GetType().Name} failed gen={generation} ({sw.ElapsedMilliseconds}ms): {ex.Message}");
                }
            }

            if (anySucceeded)
            {
                Debug.WriteLine($"[MemoryLeakDebug] LoadWithMediaSources completed gen={generation} for track: {trackdata.audio.Title}");
                return;
            }

            if (generation != Volatile.Read(ref _loadGeneration) || token.IsCancellationRequested)
                return;

            Debug.WriteLine($"[MemoryLeakDebug] No sources succeeded gen={generation} for track: {trackdata.audio.Title} ({lastError})");

            PlayNextTrack();
        }

        private static void LoadBasicMediaItem(ExtendedAudio trackdata, MediaPlaybackItem mediaPlaybackItem)
        {
            Debug.WriteLine($"[MemoryLeakDebug] LoadBasicMediaItem started for track: {trackdata.audio.Title}");

            MediaItemDisplayProperties props = mediaPlaybackItem.GetDisplayProperties();
            props.Type = Windows.Media.MediaPlaybackType.Music;
            props.MusicProperties.Title = trackdata.audio.Title;
            props.MusicProperties.AlbumArtist = trackdata.audio.Artist;

            if (trackdata.audio.Album?.Thumb != null)
            {
                var thumbnailUrl = trackdata.audio.Album.Thumb.Photo600 ??
                                 trackdata.audio.Album.Thumb.Photo270 ??
                                 trackdata.audio.Album.Thumb.Photo300;

                if (thumbnailUrl != null)
                {
                    RandomAccessStreamReference imageStreamRef =
                        RandomAccessStreamReference.CreateFromUri(new Uri(thumbnailUrl));
                    props.Thumbnail = imageStreamRef;
                }
            }

            mediaPlaybackItem.ApplyDisplayProperties(props);
            _mediaPlayer.PlaybackSession.Position = TimeSpan.FromMilliseconds(1);
            // Note: MainWindow.mainWindow.requstDownloadFFMpegAsync(); will need to be handled elsewhere
            _mediaPlayer.Source = mediaPlaybackItem;

            Debug.WriteLine($"[MemoryLeakDebug] LoadBasicMediaItem completed for track: {trackdata.audio.Title}");
        }

        public static async Task<ExtendedAudio> _TrackDataThisGet(bool forced = false)
        {
            if (iVKGetAudio != null && iVKGetAudio.countTracks != 0)
            {
                return await iVKGetAudio.GetTrackPlay(forced);
            }
            return _trackDataThis;
        }

        #endregion

        private static void MediaPlaybackItem_AudioTracksChanged(MediaPlaybackItem sender, Windows.Foundation.Collections.IVectorChangedEventArgs args)
        {
            Debug.WriteLine($"[TrackSwitch] Audio tracks changed: {args.CollectionChange}");
        }

        private static void MediaPlaybackItem_TimedMetadataTracksChanged(MediaPlaybackItem sender, Windows.Foundation.Collections.IVectorChangedEventArgs args)
        {
            Debug.WriteLine($"[TrackSwitch] Metadata tracks changed: {args.CollectionChange}");
        }

        // Debounce для предотвращения дублирования WM_APPCOMMAND
        // между глобальным хуком (GlobalMediaKeyHook) и оконной процедурой (NewWindowProc).
        private static readonly object _appCommandLock = new object();
        private static DateTime _lastAppCommandTime = DateTime.MinValue;

        /// <summary>
        /// Обрабатывает WM_APPCOMMAND (медиа-клавиши с клавиатуры).
        /// Вызывается из MainWindow.NewWindowProc и GlobalMediaKeyHook.
        /// </summary>
        public static void HandleAppCommand(int cmd)
        {
            lock (_appCommandLock)
            {
                var now = DateTime.Now;
                if ((now - _lastAppCommandTime).TotalMilliseconds < MEDIA_KEY_DEBOUNCE_MS)
                {
                    Debug.WriteLine($"[MediaPlayerService] WM_APPCOMMAND cmd={cmd} debounced (too frequent)");
                    return;
                }
                _lastAppCommandTime = now;
            }

            if (HotkeyService.Instance != null)
            {
                int vkCode = AppCommandToVkCode(cmd);
                if (vkCode != 0 && HotkeyService.Instance.IsMediaKeyRegistered(vkCode))
                {
                    Debug.WriteLine($"[MediaPlayerService] WM_APPCOMMAND cmd={cmd} skipped - handled by HotkeyService (VK=0x{vkCode:X2})");
                    return;
                }
            }

            MediaKey? key = null;

            switch (cmd)
            {
                case 8:  // APPCOMMAND_VOLUME_MUTE
                    IsMuted = !IsMuted;
                    return;
                case 9:  // APPCOMMAND_VOLUME_DOWN
                    Volume = Math.Max(0.0, Volume - 0.05);
                    return;
                case 10: // APPCOMMAND_VOLUME_UP
                    Volume = Math.Min(1.0, Volume + 0.05);
                    return;
                case 11: // APPCOMMAND_MEDIA_NEXTTRACK
                    key = MediaKey.Next;
                    break;
                case 12: // APPCOMMAND_MEDIA_PREVIOUSTRACK
                    key = MediaKey.Previous;
                    break;
                case 13: // APPCOMMAND_MEDIA_STOP
                    key = MediaKey.Stop;
                    break;
                case 14: // APPCOMMAND_MEDIA_PLAY_PAUSE
                case 46: // APPCOMMAND_MEDIA_PLAY
                case 47: // APPCOMMAND_MEDIA_PAUSE
                    key = MediaKey.PlayPause;
                    break;
            }

            if (key.HasValue)
            {
                HandleMediaKey(key.Value);
            }
        }

        /// <summary>
        /// Преобразует APPCOMMAND код в Win32 VirtualKey код.
        /// </summary>
        private static int AppCommandToVkCode(int cmd)
        {
            return cmd switch
            {
                8 => 0xB4, // APPCOMMAND_VOLUME_MUTE → VK_VOLUME_MUTE
                9 => 0xB6, // APPCOMMAND_VOLUME_DOWN → VK_VOLUME_DOWN
                10 => 0xB5, // APPCOMMAND_VOLUME_UP → VK_VOLUME_UP
                11 => 0xB2, // APPCOMMAND_MEDIA_NEXTTRACK → VK_MEDIA_NEXT_TRACK
                12 => 0xB1, // APPCOMMAND_MEDIA_PREVIOUSTRACK → VK_MEDIA_PREV_TRACK
                13 => 0xB3, // APPCOMMAND_MEDIA_STOP → VK_MEDIA_STOP
                14 or 46 or 47 => 0xB0, // APPCOMMAND_MEDIA_PLAY_PAUSE/PLAY/PAUSE → VK_MEDIA_PLAY_PAUSE
                _ => 0
            };
        }

        /// <summary>
        /// Выполняет указанное действие плеера. Используется горячими клавишами.
        /// </summary>
        public static void ExecuteAction(PlayerAction action)
        {
            switch (action)
            {
                case PlayerAction.PlayPause:
                    _ = TogglePlayPause();
                    break;

                case PlayerAction.Play:
                    Play();
                    break;

                case PlayerAction.Pause:
                    Pause();
                    break;

                case PlayerAction.NextTrack:
                    PlayNextTrack();
                    break;

                case PlayerAction.PreviousTrack:
                    HandlePreviousTrack();
                    break;

                case PlayerAction.SeekForward:
                    SeekBy(SeekStepSeconds);
                    break;

                case PlayerAction.SeekBackward:
                    SeekBy(-SeekStepSeconds);
                    break;

                case PlayerAction.VolumeUp:
                    Volume = Math.Min(1.0, Volume + 0.05);
                    break;

                case PlayerAction.VolumeDown:
                    Volume = Math.Max(0.0, Volume - 0.05);
                    break;

                case PlayerAction.Mute:
                    IsMuted = !IsMuted;
                    break;

                case PlayerAction.Shuffle:
                    PlaybackModeService.CycleMode();
                    break;

                case PlayerAction.Stop:
                    StopPlayback();
                    break;
            }
        }

        private static async Task PreloadNextTrack()
        {
            try
            {
                _nextTrackTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                var nextTrack = await iVKGetAudio?.GetTrackPlay(false);
                if (nextTrack == null || !AudioPlaybackResolver.HasPlayableSource(nextTrack.audio))
                {
                    return;
                }

                if (_nextTrackTokenSource.Token.IsCancellationRequested)
                {
                    return;
                }

                _nextTrack = nextTrack;
                Debug.WriteLine($"[Preload] Next track preloaded: {nextTrack.audio.Title}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Preload] Error preloading next track: {ex.Message}");
            }
        }
    }
}