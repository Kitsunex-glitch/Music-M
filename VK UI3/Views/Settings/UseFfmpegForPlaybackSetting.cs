using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VK_UI3.DB;
using Microsoft.UI.Xaml.Automation;

namespace VK_UI3.Views.Settings
{
    public sealed class UseFfmpegForPlaybackSetting : CheckBox
    {
        public UseFfmpegForPlaybackSetting()
        {
            try
            {
                this.Content = "Использовать FFMPEG для воспроизведения";

                this.Checked += UseFfmpegForPlaybackSetting_Checked;
                this.Unchecked += UseFfmpegForPlaybackSetting_Unchecked;
                this.Loaded += UseFfmpegForPlaybackSetting_Loaded;

                // Получение стиля из ресурсов
                Style style = Application.Current.Resources["DefaultCheckBoxStyle"] as Style;

                // Установка стиля
                this.Style = style;

                // Добавляем свойства доступности для экранного диктера
                AutomationProperties.SetName(this, "Использовать FFMPEG для воспроизведения");
                AutomationProperties.SetHelpText(this, "Включает или выключает использование FFMPEG для декодирования аудио. При отключении будет использоваться встроенный медиаплеер Windows.");
            }
            catch { }
        }

        private void UseFfmpegForPlaybackSetting_Loaded(object sender, RoutedEventArgs e)
        {
            this.DispatcherQueue.TryEnqueue(async () =>
            {
                var setting = SettingsTable.GetSetting("useFfmpegForPlayback");
                this.IsChecked = setting == null || setting.settingValue.Equals("1");
            });
        }

        private void UseFfmpegForPlaybackSetting_Unchecked(object sender, RoutedEventArgs e)
        {
            SettingsTable.SetSetting("useFfmpegForPlayback", "0");
        }

        private void UseFfmpegForPlaybackSetting_Checked(object sender, RoutedEventArgs e)
        {
            SettingsTable.SetSetting("useFfmpegForPlayback", "1");
        }
    }
}