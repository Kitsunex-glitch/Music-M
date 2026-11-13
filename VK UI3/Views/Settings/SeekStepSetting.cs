using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using VK_UI3.DB;

namespace VK_UI3.Views.Settings
{
    /// <summary>
    /// Настройка шага перемотки для горячих клавиш «Перемотка вперёд/назад».
    /// Хранится в SettingsTable под ключом SeekStepSeconds.
    /// </summary>
    public sealed class SeekStepSetting : StackPanel
    {
        public const string SettingKey = "SeekStepSeconds";
        private const int DefaultStep = 10;

        private static readonly int[] Steps = { 5, 10, 15, 30 };

        private readonly ComboBox _comboBox;
        private readonly TextBlock _descriptionText;
        private bool _isInitializing;

        public SeekStepSetting()
        {
            try
            {
                Spacing = 4;
                Orientation = Orientation.Vertical;

                var header = new TextBlock
                {
                    Text = "Шаг перемотки",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                };
                AutomationProperties.SetHeadingLevel(header, AutomationHeadingLevel.Level4);

                _comboBox = new ComboBox
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    MinWidth = 280
                };
                _comboBox.ItemsSource = Steps.Select(s => new StepOption(s)).ToList();
                _comboBox.DisplayMemberPath = nameof(StepOption.Label);

                AutomationProperties.SetName(_comboBox, "Шаг перемотки");
                AutomationProperties.SetHelpText(_comboBox,
                    "Выберите, на сколько секунд перематывать трек горячими клавишами «Перемотка вперёд» и «Перемотка назад».");

                _descriptionText = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.75,
                    FontSize = 12
                };

                Children.Add(header);
                Children.Add(_comboBox);
                Children.Add(_descriptionText);

                _comboBox.SelectionChanged += ComboBox_SelectionChanged;
                Loaded += SeekStepSetting_Loaded;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SeekStepSetting] Init failed: {ex.Message}");
            }
        }

        private void SeekStepSetting_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            try
            {
                int current = GetSavedStep();
                var items = (System.Collections.Generic.List<StepOption>)_comboBox.ItemsSource;
                _comboBox.SelectedItem = items.FirstOrDefault(x => x.Seconds == current) ?? items.First(x => x.Seconds == DefaultStep);
                UpdateDescription(current);
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_comboBox.SelectedItem is not StepOption option)
                return;

            UpdateDescription(option.Seconds);

            if (_isInitializing)
                return;

            SettingsTable.SetSetting(SettingKey, option.Seconds.ToString());
            System.Diagnostics.Debug.WriteLine($"[SeekStepSetting] Saved seek step: {option.Seconds}s");
        }

        private void UpdateDescription(int seconds)
        {
            if (_descriptionText == null)
                return;

            _descriptionText.Text = $"Горячие клавиши «Перемотка вперёд» и «Перемотка назад» будут перематывать трек на {seconds} секунд.";
        }

        private static int GetSavedStep()
        {
            var setting = SettingsTable.GetSetting(SettingKey);
            if (setting != null && int.TryParse(setting.settingValue, out var value) && value > 0)
                return value;
            return DefaultStep;
        }

        private sealed class StepOption
        {
            public StepOption(int seconds)
            {
                Seconds = seconds;
            }

            public int Seconds { get; }

            public string Label => $"{Seconds} секунд";
        }
    }
}
