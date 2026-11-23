using System.Collections.Generic;

namespace VK_UI3.Views.Tabs
{
    /// <summary>
    /// Оболочка, владеющая лентой вкладок. Реализуется главным окном (<see cref="VK_UI3.Views.MainView"/>)
    /// и окном полноэкранного плеера (<see cref="VK_UI3.Views.FullScreen.FullScreenTabHost"/>).
    /// Позволяет общим статическим хелперам навигации не знать, в каком окне открывается контент.
    /// </summary>
    public interface ITabShell
    {
        /// <summary>Тип оболочки.</summary>
        ShellKind Kind { get; }

        /// <summary>Реестр вкладок этой оболочки.</summary>
        TabManager Tabs { get; }

        /// <summary>Оболочка готова принимать контент (например, главное окно уже загружено).</summary>
        bool IsReady { get; }

        /// <summary>Можно ли сейчас создавать новые вкладки по Ctrl.</summary>
        bool IsTabStripEnabled { get; }

        /// <summary>Открывает контент по параметрам навигации (новая вкладка при <paramref name="forceNew"/>).</summary>
        bool OpenInTab(TabContent content, bool forceNew);

        /// <summary>Активирует вкладку с указанным ключом, если она открыта.</summary>
        bool Activate(TabKey key);

        /// <summary>Закрывает вкладку (pinned-вкладки не закрываются).</summary>
        bool Close(TabEntry entry);

        /// <summary>Вкладки, которые можно перенести в другую оболочку (без pinned).</summary>
        IEnumerable<TabEntry> GetTransferableTabs();
    }
}
