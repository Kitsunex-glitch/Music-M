using System;

namespace VK_UI3.Views.Tabs
{
    /// <summary>
    /// Оболочка, которой сейчас принадлежит пользователь.
    /// <see cref="Main"/> — главное окно (лента вкладок в MainView),
    /// <see cref="FullScreen"/> — отдельное окно полноэкранного плеера (своя лента вкладок).
    /// </summary>
    public enum ShellKind
    {
        Main,
        FullScreen
    }

    /// <summary>
    /// Единая точка определения активной оболочки.
    /// Пока открыт полноэкранный плеер, все статические хелперы навигации
    /// (<c>MainView.OpenPlayList</c>, <c>MainView.OpenSection</c> и т.п.) должны открывать
    /// содержимое в окне плеера, а вкладки главного окна — замораживаться.
    /// </summary>
    public static class ShellState
    {
        private static ShellKind _current = ShellKind.Main;

        /// <summary>Текущая активная оболочка.</summary>
        public static ShellKind Current => _current;

        /// <summary>Пользователь находится в полноэкранном плеере.</summary>
        public static bool IsFullScreen => _current == ShellKind.FullScreen;

        /// <summary>Пользователь находится в главном окне.</summary>
        public static bool IsMain => _current == ShellKind.Main;

        /// <summary>
        /// В текущей оболочке разрешено создавать новые вкладки по Ctrl.
        /// В главном окне при открытом полноэкранном плеере вкладки замораживаются.
        /// </summary>
        public static bool CanOpenTabs => _current == ShellKind.Main;

        /// <summary>Оболочка сменилась.</summary>
        public static event EventHandler<ShellKind> ShellChanged;

        /// <summary>Переключает активную оболочку (идемпотентно).</summary>
        public static void Set(ShellKind kind)
        {
            if (_current == kind)
                return;

            _current = kind;
            ShellChanged?.Invoke(null, kind);
        }

        /// <summary>Возвращает оболочку в состояние «главное окно».</summary>
        public static void ResetToMain() => Set(ShellKind.Main);
    }
}
