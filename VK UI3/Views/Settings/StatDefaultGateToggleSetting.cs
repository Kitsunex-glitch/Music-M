using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace VK_UI3.Views.Settings
{
    public sealed class StatDefaultGateToggleSetting : CheckBox
    {
        public StatDefaultGateToggleSetting()
        {
            try
            {
                this.Content = "Отправлять анонимную статистику использования";

                this.Checked += StatDefaultGateToggleSetting_Checked;
                this.Unchecked += StatDefaultGateToggleSetting_Unchecked;
                this.Loaded += StatDefaultGateToggleSetting_Loaded;

                // Получение стиля из ресурсов
                Style style = Application.Current.Resources["DefaultCheckBoxStyle"] as Style;

                // Установка стиля
                this.Style = style;

                // Добавляем свойства доступности для экранного диктера
                AutomationProperties.SetName(this, "Отправлять анонимную статистику использования");
                AutomationProperties.SetHelpText(this, "Отправляет анонимную статистику использования и отчёты об ошибках на сервер StatDefaultGate для улучшения приложения. Вы можете отключить сбор в любое время.");
            }
            catch { }
        }

        private void StatDefaultGateToggleSetting_Loaded(object sender, RoutedEventArgs e)
        {
            this.DispatcherQueue.TryEnqueue(async () =>
            {
                var setting = DB.SettingsTable.GetSetting("StatDefaultGateEnabled");

                if (setting == null)
                {
                    // По умолчанию статистика включена (opt-out)
                    this.IsChecked = true;
                    DB.SettingsTable.SetSetting("StatDefaultGateEnabled", "1");
                    return;
                }
                this.IsChecked = setting.settingValue.Equals("1") ? true : false;
            });
        }

        private void StatDefaultGateToggleSetting_Unchecked(object sender, RoutedEventArgs e)
        {
            DB.SettingsTable.SetSetting("StatDefaultGateEnabled", "0");
        }

        private void StatDefaultGateToggleSetting_Checked(object sender, RoutedEventArgs e)
        {
            DB.SettingsTable.SetSetting("StatDefaultGateEnabled", "1");
        }
    }
}