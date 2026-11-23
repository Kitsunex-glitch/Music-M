using KrotosNavigationFrame;
using Microsoft.UI.Xaml.Controls;
using System;

namespace VK_UI3.Views.Tabs
{
    /// <summary>
    /// Одна вкладка приложения: свой стек навигации (<see cref="NavigateFrame"/>),
    /// ключ содержимого и элементы шапки TabView.
    /// </summary>
    public sealed class TabEntry
    {
        public TabEntry(TabKey key, string header, string glyph)
        {
            Key = key;
            Header = header;
            Glyph = glyph;
        }

        /// <summary>Ключ содержимого для дедупликации вкладок.</summary>
        public TabKey Key { get; set; }

        /// <summary>Заголовок вкладки (отображается в ленте).</summary>
        public string Header { get; set; }

        /// <summary>Глиф иконки вкладки (Segoe Fluent Icons).</summary>
        public string Glyph { get; set; }

        /// <summary>Собственный стек навигации вкладки.</summary>
        public NavigateFrame ContentFrame { get; set; }

        /// <summary>Элемент ленты вкладок TabView.</summary>
        public TabViewItem Item { get; set; }

        /// <summary>Параметр навигации, с которым открыт корневой контент вкладки.</summary>
        public object NavigationParameter { get; set; }

        /// <summary>Можно ли закрыть вкладку пользователю (домашняя — нет).</summary>
        public bool IsClosable { get; set; } = true;

        /// <summary>
        /// Закреплённая вкладка (например, «Текст и очередь» в полноэкранном плеере):
        /// всегда первая, не закрывается и не заменяется другим содержимым.
        /// </summary>
        public bool IsPinned { get; set; }

        /// <summary>Флаг восстановления: содержимое загружается при первой активации.</summary>
        public bool NeedsRestore { get; set; }

        /// <summary>Обновляет заголовок и иконку в ленте.</summary>
        public void ApplyHeader()
        {
            if (Item == null)
                return;

            if (Item.Header is string s && s == Header)
                return;

            Item.Header = Header;
            Item.IconSource = string.IsNullOrWhiteSpace(Glyph)
                ? null
                : new FontIconSource { Glyph = Glyph };
        }

        public override string ToString() => Header;
    }
}
