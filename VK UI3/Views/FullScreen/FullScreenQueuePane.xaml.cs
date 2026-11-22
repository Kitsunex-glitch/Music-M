using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System;
using VK_UI3.DB;
using VK_UI3.Views;
using static VK_UI3.Views.SectionView;

namespace VK_UI3.Views.FullScreen
{
    /// <summary>
    /// Встроенная панель очереди полноэкранного плеера.
    /// Переиспользует существующий <see cref="WaitView"/> с типом <see cref="SectionType.CustomIVKGetAudio"/>,
    /// как это делает MainView.ExpandePlayNowPanel — отдельная реализация списка не нужна.
    /// </summary>
    public sealed partial class FullScreenQueuePane : UserControl
    {
        public FullScreenQueuePane()
        {
            this.InitializeComponent();
            QueueFrame.Navigated += (s, e) => QueueLoaded?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Содержимое очереди загружено (список треков доступен для прокрутки).</summary>
        public event EventHandler QueueLoaded;

        /// <summary>Загружает очередь текущего источника воспроизведения в <see cref="QueueFrame"/>.</summary>
        public void LoadQueue()
        {
            var source = Services.MediaPlayerService.iVKGetAudio;
            if (source == null)
            {
                ShowEmpty(true);
                QueueLoaded?.Invoke(this, EventArgs.Empty);
                return;
            }

            var waitParameters = new WaitParameters
            {
                sectionType = SectionType.CustomIVKGetAudio,
                iVKGetAudio = source
            };

            var setting = SettingsTable.GetSetting("disableNavigationAnimations");
            bool animationsDisabled = setting != null && setting.settingValue == "1";

            ShowEmpty(false);

            QueueFrame.Navigate(
                typeof(WaitView),
                waitParameters,
                animationsDisabled ? null : new DrillInNavigationTransitionInfo());
        }

        /// <summary>Очищает содержимое (при закрытии раздела).</summary>
        public void Clear()
        {
            QueueFrame.Content = null;
            QueueFrame.BackStack.Clear();
            QueueFrame.ForwardStack.Clear();
        }

        private void ShowEmpty(bool isEmpty)
        {
            EmptyHint.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            QueueFrame.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
