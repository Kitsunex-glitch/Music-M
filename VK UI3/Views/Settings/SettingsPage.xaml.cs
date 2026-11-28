using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VK_UI3.Helpers;
using System;
using System.IO;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace VK_UI3.Views.Settings
{
    /// <summary>
    /// Страница настроек приложения.
    /// Инициализация защищена try/catch: сбой одного контрола настроек не должен
    /// приводить к тому, что страница вообще не открывается.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            try
            {
                this.InitializeComponent();
            }
            catch (Exception ex)
            {
                // Логируем и показываем заглушку вместо падения всей страницы.
                LogSettingsError("InitializeComponent", ex);
                BuildFallbackContent(ex);
            }

            this.Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Место для будущей инициализации страницы.
            }
            catch (Exception ex)
            {
                LogSettingsError("Loaded", ex);
            }
        }

        /// <summary>
        /// Если XAML-дерево настроек не создалось, показываем хотя бы сообщение об ошибке
        /// и кнопку открытия папки приложения (для диагностики).
        /// </summary>
        private void BuildFallbackContent(Exception ex)
        {
            try
            {
                var panel = new StackPanel
                {
                    Padding = new Thickness(24),
                    Spacing = 12
                };

                panel.Children.Add(new TextBlock
                {
                    Text = "Не удалось полностью загрузить настройки",
                    Style = Application.Current.Resources["SubtitleTextBlockStyle"] as Style
                });

                panel.Children.Add(new TextBlock
                {
                    Text = ex.Message,
                    TextWrapping = TextWrapping.Wrap
                });

                panel.Children.Add(new OpenAppFolder());

                this.Content = panel;
            }
            catch
            {
                // Если даже заглушка не создалась — оставляем пустую страницу.
            }
        }

        private static readonly object _logLock = new();
        private static void LogSettingsError(string where, Exception ex)
        {
            try
            {
                var logPath = AppDataPaths.GetFile("settings-error.log");
                lock (_logLock)
                {
                    File.AppendAllText(logPath,
                        $"[{DateTime.Now:HH:mm:ss}] {where}: {ex}\n\n");
                }
            }
            catch
            {
            }

            System.Diagnostics.Debug.WriteLine($"[SettingsPage] {where}: {ex}");
        }
    }
}
