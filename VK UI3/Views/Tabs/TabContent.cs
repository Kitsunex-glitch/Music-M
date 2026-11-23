using System;

namespace VK_UI3.Views.Tabs
{
    /// <summary>
    /// Описание содержимого вкладки: ключ (для дедупликации), шапка и действие,
    /// которое наполняет <see cref="TabEntry.ContentFrame"/>.
    /// Создаётся <see cref="TabContentFactory"/>, исполняется оболочкой
    /// (<see cref="ITabShell.OpenInTab"/>) — поэтому одинаково работает
    /// и в главном окне, и в окне полноэкранного плеера.
    /// </summary>
    public sealed class TabContent
    {
        public TabContent(TabKey key, string header, string glyph, Action<TabEntry> navigate)
        {
            Key = key;
            Header = header;
            Glyph = glyph;
            Navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));
        }

        /// <summary>Ключ содержимого (дедупликация вкладок).</summary>
        public TabKey Key { get; }

        /// <summary>Заголовок вкладки.</summary>
        public string Header { get; }

        /// <summary>Глиф иконки вкладки.</summary>
        public string Glyph { get; }

        /// <summary>Наполняет стек навигации вкладки.</summary>
        public Action<TabEntry> Navigate { get; }

        /// <summary>Вкладку нужно пересоздать при активации (после восстановления сессии).</summary>
        public bool NeedsRestore { get; set; }

        public override string ToString() => $"{Key}|{Header}";
    }
}
