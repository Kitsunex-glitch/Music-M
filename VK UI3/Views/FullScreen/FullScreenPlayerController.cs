using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using VK_UI3.Views.ModalsPages;
using VK_UI3.Views.Tabs;

namespace VK_UI3.Views.FullScreen
{
    /// <summary>
    /// Владелец отдельного окна полноэкранного плеера.
    /// Отвечает за создание/закрытие <see cref="FullScreenPlayerWindow"/> и за переключение
    /// активной оболочки вкладок: пока окно открыто, навигация приложения идёт во вкладки
    /// этого окна, а вкладки главного окна замораживаются. При выходе вкладки плеера
    /// переносятся в главное окно.
    /// </summary>
    public sealed class FullScreenPlayerController
    {
        private FullScreenPlayerWindow _window;
        private bool _isActive;

        /// <summary>Окно полноэкранного плеера открыто.</summary>
        public bool IsActive => _isActive;

        /// <summary>Вызывается после фактического закрытия окна.</summary>
        public event EventHandler Exited;

        #region Enter / Exit

        /// <summary>Открывает окно полноэкранного плеера (или активирует уже открытое).</summary>
        public void Enter()
        {
            if (_isActive && _window != null)
            {
                _window.Activate();
                return;
            }

            _isActive = true;

            _window = new FullScreenPlayerWindow();
            _window.CloseRequested += OnWindowCloseRequested;

            // Все Open*-хелперы (Ctrl+клик по карточкам, поиск, настройки) с этого момента
            // открывают контент во вкладках окна плеера.
            TabShellRegistry.FullScreen = _window.TabShellHost;
            ShellState.Set(ShellKind.FullScreen);

            _window.ActivateFullScreen();
        }

        /// <summary>Закрывает окно полноэкранного плеера (идемпотентно).</summary>
        public void Exit()
        {
            if (!_isActive)
                return;

            _isActive = false;

            var window = _window;
            _window = null;

            // Сначала переносим вкладки в главное окно (пока окно ещё живо),
            // затем возвращаем оболочку главного окна.
            TransferTabsToMainWindow();

            TabShellRegistry.FullScreen = null;
            ShellState.ResetToMain();

            if (window != null)
            {
                window.CloseRequested -= OnWindowCloseRequested;
                // Закрытие окна вызовет Closed → Cleanup.
                window.Close();
            }

            Exited?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Переносит вкладки окна плеера (кроме закреплённой «Текст и очередь») в главное окно.
        /// Содержимое пересоздаётся по ключу вкладки — элементы одного окна нельзя
        /// переиспользовать в другом.
        /// </summary>
        private static void TransferTabsToMainWindow()
        {
            try
            {
                TabShellRegistry.TransferFullScreenTabsToMain();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FullScreenPlayerController] Transfer tabs failed: {ex.Message}");
            }
        }

        private void OnWindowCloseRequested(object sender, EventArgs e)
        {
            // Пользователь закрыл окно (кнопка/Esc/Alt+F4).
            Exit();
        }

        #endregion

        #region Lyrics reparenting

        /// <summary>
        /// Переносит существующий <see cref="LyricsPage"/> из MainView в хост полноэкранного окна.
        /// Второй экземпляр BetterLyric не создаётся — переиспользуется тот же контрол и его таймер.
        /// </summary>
        public bool AttachLyrics(ContentControl host)
        {
            if (host == null)
                return false;

            var lyricsPage = MainView.mainView?.DetachLyricsPageForFullScreen();
            if (lyricsPage == null)
                return false;

            host.Content = lyricsPage;
            return true;
        }

        /// <summary>Возвращает <see cref="LyricsPage"/> обратно в MainView.</summary>
        public void DetachLyrics(ContentControl host)
        {
            if (host?.Content is LyricsPage page)
            {
                host.Content = null;
                MainView.mainView?.AttachLyricsPageBack(page);
            }
        }

        #endregion
    }
}
