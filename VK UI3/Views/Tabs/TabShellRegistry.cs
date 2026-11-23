using System;

namespace VK_UI3.Views.Tabs
{
    /// <summary>
    /// Реестр оболочек с вкладками: главное окно и окно полноэкранного плеера.
    /// Статические хелперы навигации (<c>MainView.Open*</c>) через <see cref="Resolve"/>
    /// попадают в активную оболочку, поэтому контент никогда не «улетает» в главное окно,
    /// пока пользователь находится в полноэкранном плеере.
    /// </summary>
    public static class TabShellRegistry
    {
        private static ITabShell _main;
        private static ITabShell _fullScreen;

        /// <summary>Оболочка главного окна.</summary>
        public static ITabShell Main
        {
            get => _main;
            set => _main = value;
        }

        /// <summary>Оболочка окна полноэкранного плеера (null, когда окно закрыто).</summary>
        public static ITabShell FullScreen
        {
            get => _fullScreen;
            set => _fullScreen = value;
        }

        /// <summary>
        /// Оболочка, в которую нужно открывать контент.
        /// В полноэкранном режиме — окно плеера (если оно готово), иначе главное окно.
        /// </summary>
        public static ITabShell Resolve()
        {
            if (ShellState.IsFullScreen && _fullScreen != null && _fullScreen.IsReady)
                return _fullScreen;

            if (_main != null && _main.IsReady)
                return _main;

            return _main;
        }

        /// <summary>
        /// Переносит вкладки из окна плеера в главное окно.
        /// Возвращает количество перенесённых вкладок.
        /// </summary>
        public static int TransferFullScreenTabsToMain()
        {
            var source = _fullScreen;
            var target = _main;

            if (source?.Tabs == null || target == null || !target.IsReady)
                return 0;

            var active = source.Tabs.ActiveTab;
            var count = 0;

            foreach (var tab in source.GetTransferableTabs())
            {
                // Пиннутая вкладка «Текст и очередь» остаётся в окне плеера.
                if (tab?.Key == null)
                    continue;

                if (target.OpenInTab(TabContentFrom(tab), forceNew: true))
                    count++;
            }

            // В главном окне активируем ту вкладку, которая была активна в плеере.
            if (active != null && !active.IsPinned && active.Key != null)
                target.Activate(active.Key);

            return count;
        }

        /// <summary>Описание вкладки по уже открытой вкладке (для пере-хостинга в другое окно).</summary>
        private static TabContent TabContentFrom(TabEntry tab)
        {
            var content = new TabContent(
                tab.Key,
                tab.Header,
                tab.Glyph,
                entry => TabContentFactory.RestoreInto(entry))
            {
                // Вкладка переносится без содержимого: контент пересоздаётся по ключу.
                NeedsRestore = true
            };

            return content;
        }
    }
}
