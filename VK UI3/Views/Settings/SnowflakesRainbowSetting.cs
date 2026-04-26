using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VK_UI3.DB;
using Microsoft.UI.Xaml.Automation;

namespace VK_UI3.Views.Settings
{
    public sealed class SnowflakesRainbowSetting : CheckBox
    {
        public SnowflakesRainbowSetting()
        {
            this.Content = "Разноцветные снежинки";

            this.Checked += SnowflakesRainbowSetting_Checked;
            this.Unchecked += SnowflakesRainbowSetting_Unchecked;
            this.Loaded += SnowflakesRainbowSetting_Loaded;

            // Получение стиля из ресурсов
            Style style = Application.Current.Resources["DefaultCheckBoxStyle"] as Style;
            
            // Установка стиля
            this.Style = style;
            
            // Добавляем свойства доступности для экранного диктера
            AutomationProperties.SetName(this, "Разноцветные снежинки");
            AutomationProperties.SetHelpText(this, "Включает или выключает разноцветный режим снежинок. При включении снежинки будут окрашены в радужные цвета.");
        }

        private void SnowflakesRainbowSetting_Loaded(object sender, RoutedEventArgs e)
        {
            this.DispatcherQueue.TryEnqueue(async () =>
            {
                var setting = SettingsTable.GetSetting("snowflakesUseRainbowColors");
                this.IsChecked = setting != null && setting.settingValue.Equals("1");
            });
        }

        private void SnowflakesRainbowSetting_Unchecked(object sender, RoutedEventArgs e)
        {
            SettingsTable.SetSetting("snowflakesUseRainbowColors", "0");
            if (MainWindow.mainWindow?.Snow != null)
                MainWindow.mainWindow.Snow.UseRainbowColors = false;
        }

        private void SnowflakesRainbowSetting_Checked(object sender, RoutedEventArgs e)
        {
            SettingsTable.SetSetting("snowflakesUseRainbowColors", "1");
            if (MainWindow.mainWindow?.Snow != null)
                MainWindow.mainWindow.Snow.UseRainbowColors = true;
        }
    }
}