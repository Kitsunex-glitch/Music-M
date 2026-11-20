using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using VK_UI3.Models;

namespace VK_UI3.Views.Settings
{
    /// <summary>
    /// Настройка типа воспроизведения аудио: выбор режима с описанием, что даёт каждый вариант.
    /// Если выбранного типа нет у трека — используется первый доступный (см. AudioPlaybackResolver).
    /// </summary>
    public sealed class PlaybackModeSetting : StackPanel
    {
        private readonly ComboBox _comboBox;
        private readonly TextBlock _descriptionText;
        private bool _isInitializing;
        private bool _loaded;

        public PlaybackModeSetting()
        {
            try
            {
                Spacing = 4;
                Orientation = Orientation.Vertical;

                var header = new TextBlock
                {
                    Text = "Тип воспроизведения",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                };

                _comboBox = new ComboBox
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    MinWidth = 280
                };

                _descriptionText = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.75,
                    FontSize = 12
                };

                AutomationProperties.SetName(_comboBox, "Тип воспроизведения");
                AutomationProperties.SetHelpText(_comboBox,
                    "Выбор формата потока для воспроизведения. Если выбранный формат отсутствует у трека, " +
                    "будет использован первый доступный (MP3, AAC, FLAC, HLS, DASH).");

                Children.Add(header);
                Children.Add(_comboBox);
                Children.Add(_descriptionText);

                _comboBox.SelectionChanged += ComboBox_SelectionChanged;
                Loaded += PlaybackModeSetting_Loaded;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlaybackModeSetting] Init failed: {ex.Message}");
            }
        }

        private void PlaybackModeSetting_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;

            try
            {
                var selected = PlaybackTypeHelper.GetSelected();
                var items = PlaybackTypeHelper.All.ToList();

                _comboBox.ItemsSource = items;
                _comboBox.SelectedItem = items.FirstOrDefault(x => x.Type == selected) ?? items.First();
                UpdateDescription((PlaybackTypeInfo)_comboBox.SelectedItem);
            }
            finally
            {
                _isInitializing = false;
                _loaded = true;
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_comboBox.SelectedItem is not PlaybackTypeInfo info)
                return;

            UpdateDescription(info);

            // Не пишем настройку, пока контрол не загрузился: SelectionChanged
            // может прилететь до Loaded и перезаписать выбор дефолтом.
            if (_isInitializing || !_loaded)
                return;

            PlaybackTypeHelper.SetSelected(info.Type);
            System.Diagnostics.Debug.WriteLine($"[PlaybackModeSetting] Selected playback type: {info.Type}");
        }

        private void UpdateDescription(PlaybackTypeInfo info)
        {
            if (info == null || _descriptionText == null)
                return;

            var suffix = info.RequiresFFmpeg
                ? " Требуется установленный FFmpeg — без него приложение автоматически выберет другой доступный формат."
                : string.Empty;

            _descriptionText.Text = info.Description + suffix;
        }
    }
}
