using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace VK_UI3.Helpers
{
    /// <summary>
    /// Показ модальных окон с учётом вкладок: если зажат Ctrl и для действия
    /// есть «вкладочная» альтернатива (<c>openAsTab</c>), диалог не показывается —
    /// вместо него открывается новая вкладка с этим содержимым.
    /// </summary>
    public static class ModalHelper
    {
        /// <summary>
        /// Показывает диалог или открывает содержимое новой вкладкой при зажатом Ctrl.
        /// </summary>
        /// <param name="dialog">Диалог, который нужно показать, если Ctrl не зажат.</param>
        /// <param name="openAsTab">Альтернатива во вкладке; возвращает true, если открыла вкладку.</param>
        /// <returns>true — показан диалог; false — открыта вкладка.</returns>
        public static async Task<bool> ShowDialogOrTab(ContentDialog dialog, Func<bool> openAsTab)
        {
            if (dialog == null)
                return false;

            if (openAsTab != null && InputState.IsCtrlPressed() && openAsTab())
                return false;

            await dialog.ShowAsync();
            return true;
        }

        /// <summary>
        /// Проверяет, нужно ли вместо диалога открыть новую вкладку.
        /// </summary>
        public static bool ShouldOpenInTab() => InputState.IsCtrlPressed();

        /// <summary>Выставляет XamlRoot диалога по активному элементу окна.</summary>
        public static void AttachXamlRoot(ContentDialog dialog, FrameworkElement anchor)
        {
            if (dialog == null)
                return;

            var root = anchor?.XamlRoot ?? MainWindow.mainWindow?.Content?.XamlRoot;
            if (root != null)
                dialog.XamlRoot = root;
        }
    }
}
