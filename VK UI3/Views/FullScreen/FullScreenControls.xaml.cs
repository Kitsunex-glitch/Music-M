using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using VK_UI3.Converters;
using VK_UI3.Helpers.Animations;
using VK_UI3.Services;
using VK_UI3.Services.Player;
using VK_UI3.VKs;

namespace VK_UI3.Views.FullScreen
{
    /// <summary>
    /// Панель управления полноэкранного плеера.
    /// Поведение повторяет VK_UI3/Controllers/AudioPlayer.xaml.cs:
    /// смена иконок (play/pause, repeat, громкость, like) — через
    /// <see cref="AnimationsChangeFontIcon"/> (fade-out → смена глифа → fade-in),
    /// громкость — через Flyout со слайдером, как SoundSlider в мини-плеере.
    /// </summary>
    public sealed partial class FullScreenControls : UserControl
    {
        private static readonly SecondsToTimeConverter TimeConverter = new SecondsToTimeConverter();

        private FullScreenPlayerViewModel _viewModel;
        private bool _suppressSliderEvents;
        private bool _isPositionSliderHovered;

        // Аниматоры иконок — как в AudioPlayer (changeIconPlayBTN / animateFontIcon / statusAnimate).
        private AnimationsChangeFontIcon _playPauseAnimator;
        private AnimationsChangeFontIcon _repeatAnimator;
        private AnimationsChangeFontIcon _likeAnimator;
        private AnimationsChangeFontIcon _muteAnimator;
        private AnimationsChangeFontIcon _statusAnimator;

        // Защита от рекурсии при программном обновлении SoundSlider.
        private bool _suppressVolumeSlider;

        public FullScreenControls()
        {
            this.InitializeComponent();
            InitializeAnimators();
        }

        private void InitializeAnimators()
        {
            _playPauseAnimator = new AnimationsChangeFontIcon(PlayPauseIcon, DispatcherQueue);
            _repeatAnimator = new AnimationsChangeFontIcon(RepeatIcon, DispatcherQueue);
            _muteAnimator = new AnimationsChangeFontIcon(MuteIcon, DispatcherQueue);
            _statusAnimator = new AnimationsChangeFontIcon(StatusIcon, DispatcherQueue);

            // Начальная иконка трансляции в статус (как setStatusIcon в AudioPlayer).
            UpdateStatusIcon();

            // Начальные значения громкости (как InitializeSettings в AudioPlayer).
            var volume = MediaPlayerService.MediaPlayer?.Volume ?? 1.0;
            _suppressVolumeSlider = true;
            SoundSlider.Value = Math.Round(volume * 100);
            _suppressVolumeSlider = false;
        }

        #region Public API

        /// <summary>Показать текст песни на правой панели.</summary>
        public event EventHandler LyricsRequested;

        /// <summary>Открыть контекстное меню трека.</summary>
        public event EventHandler MoreRequested;

        /// <summary>Выход из полноэкранного режима.</summary>
        public event EventHandler CloseRequested;

        /// <summary>Подключает ViewModel и подписывается на изменения свойств.</summary>
        public void Initialize(FullScreenPlayerViewModel viewModel)
        {
            if (_viewModel != null)
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

            _viewModel = viewModel;

            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                ApplyAll();
            }
        }

        /// <summary>Отписывается от ViewModel.</summary>
        public void Detach()
        {
            if (_viewModel != null)
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel = null;
        }

        #endregion

        #region ViewModel sync

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(FullScreenPlayerViewModel.PositionMs):
                case nameof(FullScreenPlayerViewModel.SliderPositionMs):
                    UpdatePositionDisplay();
                    break;
                case nameof(FullScreenPlayerViewModel.DurationMs):
                    UpdateDurationDisplay();
                    break;
                case nameof(FullScreenPlayerViewModel.VolumeGlyph):
                case nameof(FullScreenPlayerViewModel.IsMuted):
                    UpdateMuteIcon();
                    break;
                case nameof(FullScreenPlayerViewModel.VolumePercent):
                    UpdateVolumeSlider();
                    break;
                case nameof(FullScreenPlayerViewModel.IsPlaying):
                case nameof(FullScreenPlayerViewModel.PlayPauseGlyph):
                    UpdatePlayPauseIcon();
                    break;
                case nameof(FullScreenPlayerViewModel.RepeatGlyph):
                    UpdateRepeatIcon();
                    break;
                case nameof(FullScreenPlayerViewModel.RepeatTooltip):
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                        RepeatButton, "Режим воспроизведения: " + _viewModel.RepeatTooltip);
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(
                        RepeatButton,
                        "Переключает режим: повтор одного трека, повтор всех треков, перемешать. Текущий режим: "
                        + _viewModel.RepeatTooltip);
                    ToolTipService.SetToolTip(RepeatButton, _viewModel.RepeatTooltip);
                    break;
                case nameof(FullScreenPlayerViewModel.Title):
                case nameof(FullScreenPlayerViewModel.Artist):
                    break;
            }
        }

        private void ApplyAll()
        {
            UpdatePositionDisplay();
            UpdateDurationDisplay();
            UpdateMuteIcon();
            UpdateVolumeSlider();
            UpdatePlayPauseIcon();
            UpdateRepeatIcon();

        }

        private void UpdatePositionDisplay()
        {
            if (_viewModel == null)
                return;

            var ms = _isPositionSliderHovered ? _viewModel.SliderPositionMs : _viewModel.PositionMs;
            PositionText.Text = (string)TimeConverter.Convert(ms, typeof(string), null, null);

            if (!_isPositionSliderHovered)
            {
                _suppressSliderEvents = true;
                PositionSlider.Value = ms;
                _suppressSliderEvents = false;
            }
        }

        private void UpdateDurationDisplay()
        {
            if (_viewModel == null)
                return;

            DurationText.Text = (string)TimeConverter.Convert(_viewModel.DurationMs, typeof(string), null, null);
            PositionSlider.Maximum = Math.Max(_viewModel.DurationMs, 1);
        }

        private void UpdateMuteIcon()
        {
            if (_viewModel == null)
                return;

            // Иконка меняется с анимацией (fade), как statusAnimate в AudioPlayer.
            _muteAnimator.ChangeFontIconWithAnimation(_viewModel.VolumeGlyph);

            var volume = MediaPlayerService.MediaPlayer?.Volume ?? 1.0;
            var percent = (int)Math.Round(volume * 100);
            var muted = _viewModel.IsMuted;
            var name = muted ? "Громкость, звук отключён" : $"Громкость, {percent} процентов";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MuteButton, name);
            ToolTipService.SetToolTip(MuteButton, name);
        }

        private void UpdateVolumeSlider()
        {
            if (_viewModel == null)
                return;

            _suppressVolumeSlider = true;
            SoundSlider.Value = Math.Round(_viewModel.VolumePercent);
            _suppressVolumeSlider = false;
        }

        private void UpdatePlayPauseIcon()
        {
            if (_viewModel == null)
                return;

            _playPauseAnimator.ChangeFontIconWithAnimation(_viewModel.PlayPauseGlyph);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PlayPauseButton, _viewModel.PlayPauseTooltip);
            ToolTipService.SetToolTip(PlayPauseButton, _viewModel.PlayPauseTooltip);
        }

        private void UpdateRepeatIcon()
        {
            if (_viewModel == null)
                return;

            _repeatAnimator.ChangeFontIconWithAnimation(_viewModel.RepeatGlyph);
        }

    

       

        #endregion

        #region Transport handlers

        private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            await MediaPlayerService.TogglePlayPause();
        }

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            MediaPlayerService.HandlePreviousTrack();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            MediaPlayerService.PlayNextTrack();
        }

        private void RepeatButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.CyclePlaybackMode();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void StatusButton_Click(object sender, RoutedEventArgs e)
        {
            // Как ToggleShareStatus в AudioPlayer: переключаем настройку и значок.
            var share = DB.SettingsTable.GetSetting("shareFriend");
            if (share == null)
            {
                DB.SettingsTable.SetSetting("shareFriend", "false");
                share = DB.SettingsTable.GetSetting("shareFriend");
            }

            if (share?.settingValue == "false")
            {
                DB.SettingsTable.SetSetting("shareFriend", "true");
                MediaPlayerService.iVKGetAudio?.shareToVK();
            }
            else
            {
                DB.SettingsTable.SetSetting("shareFriend", "false");
            }

            UpdateStatusIcon();
        }

        /// <summary>Обновляет иконку трансляции в статус (порт setStatusIcon из AudioPlayer).</summary>
        private void UpdateStatusIcon()
        {
            var share = DB.SettingsTable.GetSetting("shareFriend");
            var enabled = share != null && bool.TryParse(share.settingValue, out var value) && value;

            _statusAnimator.ChangeFontIconWithAnimation(enabled ? "\uE701" : "\uEB5E");
            var text = enabled ? "Остановить трансляцию в статус ВК" : "Транслировать в статус ВК";
            ToolTipService.SetToolTip(StatusButton, text);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(StatusButton, text);
        }

        private void LyricsButton_Click(object sender, RoutedEventArgs e)
        {
            LyricsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            MoreRequested?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Volume / seek handlers

        private void SoundSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_suppressVolumeSlider || _viewModel == null)
                return;

            _viewModel.VolumePercent = e.NewValue;
        }

        private void SoundSlider_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // Заглушка совместимости с разметкой AudioPlayer.
        }

        private void SoundSlider_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // Заглушка совместимости с разметкой AudioPlayer.
        }

        private void PositionSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_suppressSliderEvents || _viewModel == null)
                return;

            // Программные обновления отфильтрованы флагом; сюда попадают только действия
            // пользователя (перетаскивание/клик/клавиши) — перематываем трек.
            _viewModel.SeekTo((long)e.NewValue);
        }

        private void PositionSlider_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // Только помечаем наведение (подпись показывает позицию ползунка).
            // Синхронизацию НЕ останавливаем: иначе таймер позиции «откатывал» бы трек
            // назад после перемотки — перемотка выглядела бы нерабочей.
            _isPositionSliderHovered = true;
        }

        private void PositionSlider_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _isPositionSliderHovered = false;
        }

        #endregion

        #region Like

       
        

        #endregion
    }
}
