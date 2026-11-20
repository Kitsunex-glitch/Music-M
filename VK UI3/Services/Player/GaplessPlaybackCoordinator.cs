дusing MusicX.Services.Player.Sources;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Playback;

namespace VK_UI3.Services.Player
{
    /// <summary>
    /// Координатор бесшовного перехода: предзагружает следующий трек в отдельный
    /// плеер и за N секунд до конца текущего плавно гасит его, одновременно
    /// вводя следующий (кроссфейд без провала громкости).
    /// <para>
    /// Работает только для «естественного» окончания трека. Любое ручное действие
    /// (next/prev, перемотка, пауза, смена трека) вызывает <see cref="Cancel"/>.
    /// </para>
    /// </summary>
    public sealed class GaplessPlaybackCoordinator : IDisposable
    {
        private readonly object _lock = new object();
        private readonly MediaPlayer _currentPlayer;

        private CrossfadeAudioSource _next;
        private ExtendedAudio _nextTrack;
        private bool _nextIndexAdvanced;
        private Timer _tickTimer;
        private Timer _fadeTimer;
        private DateTime _fadeStartUtc;
        private double _fadeDurationSeconds = 5;
        private bool _isFading;
        private bool _isDisposed;
        private CancellationTokenSource _preloadCts;

        /// <summary>Вызывается, когда нужно атомарно переключить основной плеер на второй.</summary>
        public event Action<MediaPlayer> SwapRequested;

        /// <summary>Вызывается при фактическом старте следующего трека (для UI/статистики).</summary>
        public event Action<ExtendedAudio> NextTrackStarted;

        public GaplessPlaybackCoordinator(MediaPlayer currentPlayer)
        {
            _currentPlayer = currentPlayer;
        }

        /// <summary>
        /// Можно ли запускать предзагрузку при текущих настройках и состоянии.
        /// </summary>
        public static bool IsAvailable()
        {
            if (!CrossfadeSettings.IsEnabled)
                return false;

            if (!MediaSourceBase.IsFfmpegAvailable())
                return false;

            var useFfmpeg = DB.SettingsTable.GetSetting("useFfmpegForPlayback");
            if (useFfmpeg != null && useFfmpeg.settingValue == "0")
                return false;

            // Повтор одного трека — кроссфейд не нужен.
            var repeat = DB.SettingsTable.GetSetting("playNext")?.settingValue;
            if (repeat == PlaybackModeService.RepeatOne)
                return false;

            return true;
        }

        /// <summary>
        /// Планирует предзагрузку следующего трека после открытия текущего.
        /// </summary>
        public void Arm()
        {
            if (_isDisposed || !IsAvailable())
                return;

            var iVKGetAudio = MediaPlayerService.iVKGetAudio;
            if (iVKGetAudio == null)
                return;

            Cancel();

            _preloadCts = new CancellationTokenSource();
            var token = _preloadCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    var next = await iVKGetAudio.GetNextTrackPlay();
                    if (token.IsCancellationRequested || next == null || next.audio == null)
                        return;

                    if (!AudioPlaybackResolver.HasPlayableSource(next.audio))
                        return;

                    var resolved = MediaSourceBase.Resolve(next.audio);
                    if (!resolved.IsValid)
                        return;

                    var source = await CrossfadeAudioSource.CreateAsync(
                        resolved.Url.ToString(), MediaPlayerService.Equalizer, token);

                    if (token.IsCancellationRequested)
                    {
                        source.Dispose();
                        return;
                    }

                    lock (_lock)
                    {
                        if (_isDisposed)
                        {
                            source.Dispose();
                            return;
                        }

                        _next?.Dispose();
                        _next = source;
                        _nextTrack = next;
                        _nextIndexAdvanced = false;
                        _fadeDurationSeconds = CrossfadeSettings.DurationSeconds;

                        StartTick();
                        Debug.WriteLine($"[Gapless] Preloaded next track: {next.audio.Title}");
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Gapless] Preload failed: {ex.Message}");
                }
            });
        }

        private void StartTick()
        {
            _tickTimer?.Dispose();
            _tickTimer = new Timer(_ => OnTick(), null, 200, 200);
        }

        private void OnTick()
        {
            try
            {
                lock (_lock)
                {
                    if (_isDisposed || _next == null || _isFading)
                        return;

                    var session = _currentPlayer?.PlaybackSession;
                    if (session == null)
                        return;

                    var duration = session.NaturalDuration;
                    var position = session.Position;
                    if (duration <= TimeSpan.Zero)
                        return;

                    var remaining = duration - position;
                    if (remaining <= TimeSpan.FromSeconds(_fadeDurationSeconds))
                    {
                        BeginFade();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Gapless] Tick error: {ex.Message}");
            }
        }

        private void BeginFade()
        {
            if (_next == null)
                return;

            _isFading = true;
            _tickTimer?.Dispose();
            _tickTimer = null;

            // Сдвигаем индекс и стартуем следующий трек одновременно.
            var iVKGetAudio = MediaPlayerService.iVKGetAudio;
            if (iVKGetAudio != null && !_nextIndexAdvanced)
            {
                iVKGetAudio.AdvanceToNextTrack();
                _nextIndexAdvanced = true;
            }

            _next.Gain = 0;
            _next.Start();

            try
            {
                NextTrackStarted?.Invoke(_nextTrack);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Gapless] NextTrackStarted handler error: {ex.Message}");
            }

            _fadeStartUtc = DateTime.UtcNow;
            _fadeTimer?.Dispose();
            _fadeTimer = new Timer(_ => OnFadeTick(), null, 0, 50);
            Debug.WriteLine("[Gapless] Crossfade started");
        }

        private void OnFadeTick()
        {
            try
            {
                CrossfadeAudioSource next;
                double t;

                lock (_lock)
                {
                    if (_isDisposed)
                        return;

                    next = _next;
                    if (next == null)
                        return;

                    t = (DateTime.UtcNow - _fadeStartUtc).TotalSeconds / Math.Max(0.5, _fadeDurationSeconds);
                    if (t >= 1.0)
                        t = 1.0;

                    MediaPlayerService.SetCurrentTrackGain(CrossfadeSettings.FadeOutGain(t));
                    next.Gain = CrossfadeSettings.FadeInGain(t, CrossfadeSettings.Curve);

                    if (t < 1.0)
                        return;
                }

                // Кроссфейд завершён — переключаем основной плеер на второй.
                CompleteFade(next);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Gapless] Fade tick error: {ex.Message}");
            }
        }

        private void CompleteFade(CrossfadeAudioSource next)
        {
            _fadeTimer?.Dispose();
            _fadeTimer = null;

            MediaPlayer player;
            lock (_lock)
            {
                if (_isDisposed)
                    return;

                _isFading = false;
                player = _next?.Player;
                _next = null;
            }

            if (player == null)
                return;

            try
            {
                // Восстанавливаем gain основного источника на случай, если он останется.
                MediaPlayerService.SetCurrentTrackGain(1.0);

                // Атомарно меняем основной плеер на второй (событие переподпишет UI).
                SwapRequested?.Invoke(player);
                Debug.WriteLine("[Gapless] Swapped to next player");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Gapless] Swap error: {ex.Message}");
            }
        }

        /// <summary>
        /// Отменяет предзагрузку/кроссфейд и освобождает второй плеер.
        /// Безопасно вызывать в любой момент и многократно.
        /// </summary>
        public void Cancel()
        {
            CrossfadeAudioSource next = null;

            lock (_lock)
            {
                try { _preloadCts?.Cancel(); } catch { }
                _preloadCts = null;

                _tickTimer?.Dispose();
                _tickTimer = null;

                _fadeTimer?.Dispose();
                _fadeTimer = null;

                _isFading = false;
                next = _next;
                _next = null;
                _nextTrack = null;
                _nextIndexAdvanced = false;
            }

            // Возвращаем громкость текущего трека, если кроссфейд успел её убавить.
            MediaPlayerService.SetCurrentTrackGain(1.0);

            if (next != null)
            {
                try { next.Dispose(); } catch { }
                Debug.WriteLine("[Gapless] Crossfade cancelled");
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            Cancel();
            _isDisposed = true;
        }
    }
}
