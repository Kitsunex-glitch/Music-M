using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VK_UI3.Helpers;

namespace VK_UI3.Views.Notification
{
    public sealed partial class TwitchNotifController : UserControl
    {
        private TwitchStreamStatus _stream;

        public TwitchNotifController()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Заполняет контрол данными о стриме и настраивает отображение.
        /// </summary>
        public void SetStream(TwitchStreamStatus status)
        {
            _stream = status;
            if (_stream == null)
                return;

            NameText.Text = _stream.DisplayName;
            ViewersText.Text = $"Зрителей: {_stream.viewer_count}";

            // Название стрима (если есть)
            if (!string.IsNullOrWhiteSpace(_stream.title))
            {
                TitleText.Text = _stream.title;
                TitleText.Visibility = Visibility.Visible;
            }
            else
            {
                TitleText.Visibility = Visibility.Collapsed;
            }

            // Игра (если есть)
            if (!string.IsNullOrWhiteSpace(_stream.game_name))
            {
                GameText.Text = $"Игра: {_stream.game_name}";
                GameText.Visibility = Visibility.Visible;
            }
            else
            {
                GameText.Visibility = Visibility.Collapsed;
            }
        }

        private void OpenStreamButton_Click(object sender, RoutedEventArgs e)
        {
            if (_stream == null || string.IsNullOrWhiteSpace(_stream.Login))
                return;

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"https://www.twitch.tv/{_stream.Login}")
            {
                UseShellExecute = true
            });

            // Закрыть уведомление после перехода на стрим
            CloseNotification();
        }

        private void CloseNotification()
        {
            // Находим родительский NotifController через визуальное дерево и удаляем уведомление.
            // Проще: DataContext у контрола — Notification (устанавливается ListView),
            // поэтому используем его, если доступен.
            if (DataContext is Notification notification)
            {
                notification.Delete();
            }
        }
    }
}