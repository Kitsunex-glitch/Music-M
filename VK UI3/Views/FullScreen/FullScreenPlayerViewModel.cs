using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using VK_UI3.Services;
using VK_UI3.Services.Player;
using Windows.Media.Playback;

namespace VK_UI3.Views.FullScreen
{
    /// <summary>
    /// Состояние полноэкранного плеера: метаданные трека, позиция, громкость и режим воспроизведения.
    /// Реализует <see cref="INotifyPropertyChanged"/> вручную (в проекте нет CommunityToolkit.Mvvm).
    /// Все уведомления приходят из UI-потока: сервис плеера сам маршалит события через DispatcherQueue.
    /// </summary>
    public sealed class FullScreenPlayerViewModel : INotifyPropertyChanged
    {
        #region Fields

        private bool _subscribed;

        private string _title = string.Empty;
        private string _artist = string.Empty;
        private string _thumbnail = string.Empty;

        private long _positionMs;
        private long _durationMs;
        private long _sliderPositionMs;
        private bool _isSliderDragging;

        private double _volumePercent = 100;
        private bool _isMuted;

        private bool _isPlaying;
        private string _repeatGlyph = "\uE8EE"; // RepeatAll по умолчанию
        private string _repeatTooltip = PlaybackModeService.GetModeDisplayName(PlaybackModeService.RepeatAll);

        #endregion

        #region Properties

        public string Title
        {
            get => _title;
            private set => SetProperty(ref _title, value);
        }

        public string Artist
        {
            get => _artist;
            private set => SetProperty(ref _artist, value);
        }

        /// <summary>URL обложки трека (может быть пустым).</summary>
        public string Thumbnail
        {
            get => _thumbnail;
            private set => SetProperty(ref _thumbnail, value);
        }

        /// <summary>Текущая позиция воспроизведения в миллисекундах.</summary>
        public long PositionMs
        {
            get => _positionMs;
            private set => SetProperty(ref _positionMs, value);
        }

        /// <summary>Длительность текущего трека в миллисекундах.</summary>
        public long DurationMs
        {
            get => _durationMs;
            private set => SetProperty(ref _durationMs, value);
        }

        /// <summary>
        /// Позиция ползунка в миллисекундах. Отличается от <see cref="PositionMs"/>,
        /// пока пользователь перетаскивает слайдер (или навёл курсор).
        /// Перемотка выполняется НЕ здесь: сеттер вызывается и из таймера позиции,
        /// поэтому seek делается явно из <see cref="SeekTo"/> при действии пользователя.
        /// </summary>
        public long SliderPositionMs
        {
            get => _sliderPositionMs;
            set => SetProperty(ref _sliderPositionMs, value);
        }

        /// <summary>Громкость в процентах (0..100).</summary>
        public double VolumePercent
        {
            get => _volumePercent;
            set
            {
                if (SetProperty(ref _volumePercent, value))
                {
                    var normalized = Math.Clamp(value / 100.0, 0.0, 1.0);
                    MediaPlayerService.Volume = normalized;
                    DB.SettingsTable.SetSetting("Volume", normalized.ToString());
                }
            }
        }

        public bool IsMuted
        {
            get => _isMuted;
            private set => SetProperty(ref _isMuted, value);
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            private set
            {
                if (SetProperty(ref _isPlaying, value))
                {
                    OnPropertyChanged(nameof(PlayPauseGlyph));
                    OnPropertyChanged(nameof(PlayPauseTooltip));
                }
            }
        }

        /// <summary>Глиф основной кнопки: пауза, если играет, иначе «воспроизвести».</summary>
        public string PlayPauseGlyph => _isPlaying ? "\uE769" : "\uE768";

        public string PlayPauseTooltip => _isPlaying ? "Пауза" : "Воспроизвести";

        /// <summary>Глиф кнопки режима воспроизведения (repeat one / repeat all / shuffle).</summary>
        public string RepeatGlyph
        {
            get => _repeatGlyph;
            private set => SetProperty(ref _repeatGlyph, value);
        }

        public string RepeatTooltip
        {
            get => _repeatTooltip;
            private set => SetProperty(ref _repeatTooltip, value);
        }

        /// <summary>Глиф кнопки громкости (динамик / без звука).</summary>
        public string VolumeGlyph => _isMuted ? "\uE74F" : "\uE767";

        #endregion

        #region Lifecycle

        /// <summary>
        /// Подписывается на события сервисов и синхронизирует начальное состояние.
        /// Вызывать в UI-потоке.
        /// </summary>
        public void Attach()
        {
            if (_subscribed)
                return;
            _subscribed = true;

            MediaPlayerService.AudioPlayedChangeEvent += OnAudioPlayedChange;
            MediaPlayerService.VolumeChanged += OnVolumeChanged;
            MediaPlayerService.PositionChanged += OnPositionChanged;
            PlaybackModeService.ModeChanged += OnModeChanged;

            var player = MediaPlayerService.MediaPlayer;
            if (player != null)
            {
                player.CurrentStateChanged += OnCurrentStateChanged;
            }

            RefreshTrackInfo();
            RefreshVolume();
            RefreshPlayState();
            ApplyMode(PlaybackModeService.CurrentMode);
        }

        /// <summary>Отписывается от всех событий. Вызывать в UI-потоке.</summary>
        public void Detach()
        {
            if (!_subscribed)
                return;
            _subscribed = false;

            MediaPlayerService.AudioPlayedChangeEvent -= OnAudioPlayedChange;
            MediaPlayerService.VolumeChanged -= OnVolumeChanged;
            MediaPlayerService.PositionChanged -= OnPositionChanged;
            PlaybackModeService.ModeChanged -= OnModeChanged;

            var player = MediaPlayerService.MediaPlayer;
            if (player != null)
            {
                player.CurrentStateChanged -= OnCurrentStateChanged;
            }
        }

        #endregion

        #region Public helpers

        /// <summary>Пользователь начал взаимодействие с ползунком — остановить синхронизацию.</summary>
        public void BeginSliderDrag()
        {
            _isSliderDragging = true;
        }

        /// <summary>Пользователь закончил взаимодействие с ползунком.</summary>
        public void EndSliderDrag()
        {
            _isSliderDragging = false;
            SliderPositionMs = PositionMs;
        }

        /// <summary>
        /// Перематывает трек на указанную позицию. Вызывается только из обработчиков
        /// пользовательского ввода слайдера (как в AudioPlayer), чтобы таймер позиции
        /// не «откатывал» трек назад.
        /// </summary>
        public void SeekTo(long positionMs)
        {
            var clamped = Math.Max(0, positionMs);
            SliderPositionMs = clamped;

            try
            {
                var session = MediaPlayerService.MediaPlayer?.PlaybackSession;
                if (session != null)
                    session.Position = TimeSpan.FromMilliseconds(clamped);
            }
            catch
            {
                // Позиция может быть недоступна, пока трек не открыт — игнорируем.
            }
        }

        /// <summary>Переключает режим воспроизведения (повтор одного → повтор всех → перемешать).</summary>
        public void CyclePlaybackMode()
        {
            try
            {
                var mode = PlaybackModeService.CycleMode();
                ApplyMode(mode);
            }
            catch
            {
                // Переключение режима не должно ронять UI.
            }
        }

        #endregion

        #region Event handlers

        private void OnAudioPlayedChange(object sender, EventArgs e)
        {
            RefreshTrackInfo();
            RefreshPlayState();
        }

        private void OnVolumeChanged(object sender, VK_UI3.Services.Player.VolumeChangedEventArgs e)
        {
            RefreshVolume();
        }

        private void OnPositionChanged(object sender, TimeSpan position)
        {
            var ms = (long)position.TotalMilliseconds;
            PositionMs = ms;
            if (!_isSliderDragging)
            {
                // Не создаём анимаций на каждый тик — просто присваиваем значение.
                SliderPositionMs = ms;
            }
        }

        private void OnModeChanged(object sender, string mode)
        {
            ApplyMode(mode);
        }

        private void OnCurrentStateChanged(MediaPlayer sender, object args)
        {
            // Вызывается на потоке MediaPlayer — маршалим в UI.
            var queue = MainWindow.mainWindow?.DispatcherQueue;
            if (queue == null)
            {
                RefreshPlayState();
                return;
            }

            if (queue.HasThreadAccess)
                RefreshPlayState();
            else
                queue.TryEnqueue(RefreshPlayState);
        }

        #endregion

        #region Private refresh helpers

        private void RefreshTrackInfo()
        {
            // ВАЖНО: только PlayingTrack — без TrackDataThis (.Result на UI-потоке).
            var track = MediaPlayerService.PlayingTrack;
            if (track?.audio == null)
            {
                Title = string.Empty;
                Artist = string.Empty;
                Thumbnail = string.Empty;
                DurationMs = 0;
                return;
            }

            Title = track.audio.Title ?? string.Empty;
            Artist = track.audio.Artist ?? string.Empty;
            DurationMs = (long)(track.audio.Duration * 1000);

            var album = track.audio.Album;
            Thumbnail = album?.Thumb?.Photo600
                        ?? album?.Thumb?.Photo300
                        ?? album?.Thumb?.Photo270
                        ?? album?.Thumb?.Photo68
                        ?? album?.Thumb?.Photo34
                        ?? string.Empty;

            OnPropertyChanged(nameof(VolumeGlyph));
        }

        private void RefreshVolume()
        {
            var volume = MediaPlayerService.MediaPlayer?.Volume ?? 1.0;
            _volumePercent = Math.Round(volume * 100);
            IsMuted = MediaPlayerService.IsMuted;
            OnPropertyChanged(nameof(VolumePercent));
            OnPropertyChanged(nameof(VolumeGlyph));
        }

        private void RefreshPlayState()
        {
            var player = MediaPlayerService.MediaPlayer;
            IsPlaying = player?.PlaybackSession?.PlaybackState == MediaPlaybackState.Playing;
        }

        private void ApplyMode(string mode)
        {
            switch (mode)
            {
                case PlaybackModeService.RepeatOne:
                    RepeatGlyph = "\uE8ED";
                    break;
                case PlaybackModeService.Shuffle:
                    RepeatGlyph = "\uE8B1";
                    break;
                default:
                    RepeatGlyph = "\uE8EE";
                    break;
            }

            RepeatTooltip = PlaybackModeService.GetModeDisplayName(mode);
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
