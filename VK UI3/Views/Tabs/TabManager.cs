using KrotosNavigationFrame;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VK_UI3.Views.Tabs
{
    /// <summary>
    /// Реестр вкладок приложения поверх <see cref="TabView"/>.
    /// Не знает о содержимом страниц: только создаёт/ищет/закрывает вкладки
    /// и хранит активную. Навигация внутри вкладки выполняется её собственным
    /// <see cref="NavigateFrame"/>.
    /// </summary>
    public sealed class TabManager
    {
        private readonly TabView _tabView;
        private readonly List<TabEntry> _tabs = new();
        private bool _suspendSelectionSync;

        public TabManager(TabView tabView)
        {
            _tabView = tabView ?? throw new ArgumentNullException(nameof(tabView));
            _tabView.SelectionChanged += TabView_SelectionChanged;
        }

        /// <summary>Все вкладки в порядке ленты.</summary>
        public IReadOnlyList<TabEntry> Tabs => _tabs;

        /// <summary>Активная вкладка.</summary>
        public TabEntry ActiveTab { get; private set; }

        /// <summary>Пользователь переключил вкладку.</summary>
        public event EventHandler<TabEntry> ActiveTabChanged;

        /// <summary>Вкладка создана.</summary>
        public event EventHandler<TabEntry> TabCreated;

        /// <summary>Вкладка закрыта.</summary>
        public event EventHandler<TabEntry> TabClosed;

        /// <summary>Пользователь запросил закрытие вкладки через TabView (кнопка / Ctrl+F4).</summary>
        public event EventHandler<TabEntry> CloseRequested;

        /// <summary>Запрос создания новой пустой вкладки (кнопка «+»).</summary>
        public event EventHandler AddTabRequested;

        #region Creation

        /// <summary>
        /// Создаёт вкладку и добавляет её в ленту. Контент создаётся вызывающим кодом.
        /// </summary>
        public TabEntry Create(TabKey key, string header, string glyph, bool isClosable = true)
        {
            var entry = new TabEntry(key, header, glyph) { IsClosable = isClosable };

            var frame = new NavigateFrame();
            // Контент вкладки должен занимать всю её площадь: иначе страницы
            // получают только desired size и центрированные элементы уезжают вверх.
            frame.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
            frame.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
            entry.ContentFrame = frame;

            var item = new TabViewItem
            {
                Content = frame,
                IsClosable = isClosable,
                HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                VerticalContentAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch,
                IconSource = string.IsNullOrWhiteSpace(glyph)
                    ? null
                    : new FontIconSource { Glyph = glyph }
            };
            item.Header = header;
            entry.Item = item;

            _tabs.Add(entry);
            _tabView.TabItems.Add(item);

            TabCreated?.Invoke(this, entry);
            return entry;
        }

        /// <summary>Создаёт вкладку и сразу делает её активной.</summary>
        public TabEntry CreateAndActivate(TabKey key, string header, string glyph, bool isClosable = true)
        {
            var entry = Create(key, header, glyph, isClosable);
            Activate(entry);
            return entry;
        }

        #endregion

        #region Lookup / activation

        public TabEntry Find(TabKey key)
        {
            if (key == null)
                return null;

            return _tabs.FirstOrDefault(t => t.Key != null && t.Key.Equals(key));
        }

        public TabEntry FromItem(TabViewItem item) => _tabs.FirstOrDefault(t => ReferenceEquals(t.Item, item));

        public void Activate(TabEntry entry)
        {
            if (entry?.Item == null)
                return;

            if (!ReferenceEquals(ActiveTab, entry))
                ActiveTab = entry;

            if (!ReferenceEquals(_tabView.SelectedItem, entry.Item))
            {
                _suspendSelectionSync = true;
                try
                {
                    _tabView.SelectedItem = entry.Item;
                }
                finally
                {
                    _suspendSelectionSync = false;
                }
            }

            ActiveTabChanged?.Invoke(this, entry);
        }

        /// <summary>Обновляет заголовок/иконку вкладки.</summary>
        public void UpdateHeader(TabEntry entry, string header, string glyph = null)
        {
            if (entry == null)
                return;

            if (header != null)
                entry.Header = header;
            if (glyph != null)
                entry.Glyph = glyph;

            entry.ApplyHeader();
        }

        #endregion

        #region Closing

        /// <summary>
        /// Закрывает вкладку (включая последнюю — тогда вкладок не остаётся).
        /// Возвращает true, если вкладка действительно закрыта.
        /// </summary>
        public bool Close(TabEntry entry, bool allowLast = true)
        {
            if (entry == null || !_tabs.Contains(entry))
                return false;

            var index = _tabs.IndexOf(entry);
            _tabs.Remove(entry);

            _suspendSelectionSync = true;
            try
            {
                _tabView.TabItems.Remove(entry.Item);
            }
            finally
            {
                _suspendSelectionSync = false;
            }

            entry.Item.Content = null;
            entry.ContentFrame?.Children.Clear();

            if (ReferenceEquals(ActiveTab, entry))
            {
                var next = _tabs.Count == 0
                    ? null
                    : _tabs[Math.Min(index, _tabs.Count - 1)];

                ActiveTab = null;
                if (next != null)
                    Activate(next);
            }

            TabClosed?.Invoke(this, entry);
            return true;
        }

        /// <summary>Закрывает все вкладки, кроме указанной (или кроме всех неактивных, если null).</summary>
        public void CloseAllExcept(TabEntry keep)
        {
            foreach (var tab in _tabs.Where(t => !ReferenceEquals(t, keep)).ToList())
            {
                Close(tab, allowLast: true);
            }
        }

        #endregion

        #region TabView events

        internal void HookTabViewEvents()
        {
            _tabView.TabCloseRequested += TabView_TabCloseRequested;
            _tabView.AddTabButtonClick += TabView_AddTabButtonClick;
        }

        private void TabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suspendSelectionSync)
                return;

            if (_tabView.SelectedItem is TabViewItem item)
            {
                var entry = FromItem(item);
                if (entry != null && !ReferenceEquals(ActiveTab, entry))
                {
                    ActiveTab = entry;
                    ActiveTabChanged?.Invoke(this, entry);
                }
            }
        }

        private void TabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            var entry = args.Tab != null ? FromItem(args.Tab) : FindByItem(args.Item as TabViewItem);
            if (entry == null)
                return;

            CloseRequested?.Invoke(this, entry);
        }

        private void TabView_AddTabButtonClick(TabView sender, object args)
        {
            AddTabRequested?.Invoke(this, EventArgs.Empty);
        }

        private TabEntry FindByItem(TabViewItem item)
        {
            if (item == null)
                return null;

            return _tabs.FirstOrDefault(t => ReferenceEquals(t.Item, item));
        }

        #endregion
    }
}
