using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VK_UI3.DB;
using Microsoft.UI.Xaml.Automation;

namespace VK_UI3.Views.Settings
{
    public sealed class DisableNavigationAnimationsSetting : CheckBox
    {
        public DisableNavigationAnimationsSetting()
        {
            try
            {
                this.Content = "Отключить анимации переходов";
                this.Checked += DisableNavigationAnimationsSetting_Checked;
                this.Unchecked += DisableNavigationAnimationsSetting_Unchecked;
                this.Loaded += DisableNavigationAnimationsSetting_Loaded;

                // Получение стиля из ресурсов
                Style style = Application.Current.Resources["DefaultCheckBoxStyle"] as Style;

                // Установка стиля
                this.Style = style;

                // Добавляем свойства доступности для экранного диктера
                AutomationProperties.SetName(this, "Отключить анимации переходов");
                AutomationProperties.SetHelpText(this, "Включает или выключает анимации при переходе между разделами. Отключение анимаций может ускорить восприятие загрузки.");
            }
            catch { }
        }

        private void DisableNavigationAnimationsSetting_Loaded(object sender, RoutedEventArgs e)
        {
            this.DispatcherQueue.TryEnqueue(async () =>
            {
                var setting = SettingsTable.GetSetting("disableNavigationAnimations");
                // По умолчанию анимации включены (значение "0")
                this.IsChecked = setting != null && setting.settingValue.Equals("1");
            });
        }

        private void DisableNavigationAnimationsSetting_Unchecked(object sender, RoutedEventArgs e)
        {
            SettingsTable.SetSetting("disableNavigationAnimations", "0");
        }

        private void DisableNavigationAnimationsSetting_Checked(object sender, RoutedEventArgs e)
        {
            SettingsTable.SetSetting("disableNavigationAnimations", "1");
        }
    }
}