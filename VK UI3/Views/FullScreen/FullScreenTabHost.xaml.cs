using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VK_UI3.Views.Tabs;
using static VK_UI3.Views.SectionView;
using SelectionChangedEventArgs = Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs;

namespace VK_UI3.Views.FullScreen
{
    /// <summary>
    /// Лента вкладок окна полноэкранного плеера.
    /// Первая вкладка — закреплённая «Текст и очередь»: её нельзя закрыть и нельзя
    /// заменить другим содержимым. Остальные вкладки создаются обычной Ctrl-логикой
    /// (плейлист, артист, поиск, настройки) и живут по тем же правилам, что в главном окне.
    /// </summary>
    public sealed partial class FullScreenTabHost : UserControl, ITabShell
    {
        /// <summary>Ключ закреплённой вкладки «Текст и очередь».</summary>
        public static readonly TabKey LyricsQueueKey = new TabKey(SectionType.None, "fullscreen:lyrics-queue");

        private const string LyricsQueueHeader = "Текст и очередь";
        private const string LyricsQueueGlyph = "\uE8FD";

        private TabManager _manager;
        private TabEntry _pinnedTab;

        /// <summary>
        /// СКОЛЬКО СДВИГАТЬ ЛЕНТУ ВКЛАДОК ПРИ СКРЫТИИ (в пикселях, вверх).
        /// Значение задаётся здесь — подберите его так, чтобы лента полностью уезжала
        /// за верхнюю границу окна. Текущее значение — стандартная высота шапки TabView
        /// с запасом: строка вкладок (~40) + небольшой отступ.
        /// </summary>
        private const double TabStripShift = 56;

        private readonly TimeSpan _collapseDuration = TimeSpan.FromSeconds(0.25);
        private CancellationTokenSource _stripCts;
        private bool _stripCollapsed;

        /// <summary>Оверлей окна плеера сейчас виден (лента следует за ним).</summary>
        private bool _isOverlayVisible = true;

        public FullScreenTabHost()
        {
            this.InitializeComponent();

            // Стартовое состояние: лента сразу за верхней границей. Окно плеера
            // открывается с единственной вкладкой «Текст и очередь», поэтому полоса
            // вкладок не должна быть видна ни одного кадра — выставляем без анимации.
            Tabs.Margin = new Thickness(0, -TabStripShift, 0, 0);
            _stripCollapsed = true;

            _manager = new TabManager(Tabs);
            _manager.HookTabViewEvents();
            _manager.ActiveTabChanged += Manager_ActiveTabChanged;
            _manager.TabCreated += Manager_TabCreated;
            _manager.CloseRequested += Manager_CloseRequested;

            CreatePinnedTab();
        }

        #region Tab strip collapse (одна вкладка)

        /// <summary>
        /// Пересчитывает видимость ленты вкладок при изменении их количества.
        /// <para>
        /// Одна вкладка — лента не нужна вовсе (её нечем переключать): она остаётся
        /// скрытой всегда, движения мыши её не выдвигают.
        /// </para>
        /// <para>
        /// Несколько вкладок — состоянием управляет общий механизм автоскрытия оверлея
        /// (<see cref="FullScreenAutoHideBehavior"/>): лента уезжает и возвращается
        /// одновременно с панелью управления (см. <see cref="SetOverlayVisible"/>).
        /// </para>
        /// </summary>
        public void UpdateTabStripVisibility()
        {
            if (_manager == null)
                return;

            // Одна вкладка — лента скрыта принудительно, даже если оверлей виден.
            if (_manager.Tabs.Count == 1)
            {
                SetStripCollapsed(true);
                return;
            }

            // Несколько вкладок — возвращаем ленту к состоянию оверлея.
            SetStripCollapsed(!_isOverlayVisible);
        }

        /// <summary>
        /// Оверлей (панель управления, шапка вкладки) показан или скрыт.
        /// Лента вкладок следует за ним — отдельного таймера у неё нет.
        /// </summary>
        public void SetOverlayVisible(bool visible)
        {
            _isOverlayVisible = visible;

            // Единственная вкладка: лента не должна выезжать никогда.
            if (_manager == null || _manager.Tabs.Count <= 1)
            {
                SetStripCollapsed(true);
                return;
            }

            SetStripCollapsed(!visible);
        }

        /// <summary>
        /// Сдвигает ленту вкладок вверх/вниз с анимацией. Содержимое закреплённой вкладки
        /// сдвигается вместе с ней, чтобы не оставалось пустой полосы сверху.
        /// </summary>
        private void SetStripCollapsed(bool collapsed)
        {
            if (collapsed == _stripCollapsed)
                return;

            _stripCollapsed = collapsed;

            // Сдвиг задаётся константой TabStripShift (см. объявление выше).
            var target = collapsed
                ? new Thickness(0, -TabStripShift, 0, 0)
                : new Thickness(0);

            // Компенсируем сдвиг у содержимого вкладки: уезжает только полоса вкладок,
            // контент остаётся на месте и не оставляет пустую полосу сверху.
            var contentMargin = collapsed
                ? new Thickness(0, -TabStripShift, 0, 0)
                : new Thickness(0);

            _ = AnimateTabsMargin(target, _collapseDuration);
            _ = AnimateContentMargin(contentMargin, _collapseDuration);
        }

        /// <summary>
        /// Плавно поднимает содержимое закреплённой вкладки вместе с лентой,
        /// чтобы при скрытии полосы вкладок не появлялось пустого отступа.
        /// </summary>
        private async Task AnimateContentMargin(Thickness newMargin, TimeSpan duration)
        {
            var content = PinnedContent as FrameworkElement;
            if (content == null || content.Margin == newMargin)
                return;

            var current = content.Margin;
            double topStep = (newMargin.Top - current.Top) / duration.TotalMilliseconds;
            double bottomStep = (newMargin.Bottom - current.Bottom) / duration.TotalMilliseconds;
            double top = current.Top;
            double bottom = current.Bottom;

            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < duration)
            {
                double elapsed = stopwatch.Elapsed.TotalMilliseconds;
                content.Margin = new Thickness(current.Left, top + topStep * elapsed, current.Right, bottom + bottomStep * elapsed);
                await Task.Delay(1);
            }

            content.Margin = newMargin;
            stopwatch.Stop();
        }

        /// <summary>
        /// Анимация Margin ленты вкладок — тот же приём, что
        /// <c>AnimatedButton.AnimateMargin</c> / <c>MainView.AnimateTabsHostMargin</c>.
        /// </summary>
        private async Task AnimateTabsMargin(Thickness newMargin, TimeSpan duration)
        {
            if (Tabs == null || Tabs.Margin == newMargin)
                return;

            _stripCts?.Cancel();
            _stripCts = new CancellationTokenSource();
            var token = _stripCts.Token;

            var current = Tabs.Margin;
            double leftStep = (newMargin.Left - current.Left) / duration.TotalMilliseconds;
            double topStep = (newMargin.Top - current.Top) / duration.TotalMilliseconds;
            double rightStep = (newMargin.Right - current.Right) / duration.TotalMilliseconds;
            double bottomStep = (newMargin.Bottom - current.Bottom) / duration.TotalMilliseconds;

            var left = current.Left;
            var top = current.Top;
            var right = current.Right;
            var bottom = current.Bottom;

            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < duration)
            {
                if (token.IsCancellationRequested)
                    return;

                double elapsed = stopwatch.Elapsed.TotalMilliseconds;
                Tabs.Margin = new Thickness(
                    left + leftStep * elapsed,
                    top + topStep * elapsed,
                    right + rightStep * elapsed,
                    bottom + bottomStep * elapsed);

                await Task.Delay(1);
            }

            Tabs.Margin = newMargin;
            stopwatch.Stop();
        }

        #endregion

        #region Public API

        /// <summary>Стек навигации закреплённой вкладки «Текст и очередь».</summary>
        public FrameworkElement PinnedContent => _pinnedTab?.ContentFrame;

        /// <summary>Закреплённая вкладка активна.</summary>
        public bool IsPinnedActive => ReferenceEquals(_manager.ActiveTab, _pinnedTab);

        /// <summary>Реестр вкладок окна плеера.</summary>
        public TabManager Manager => _manager;

        /// <summary>Активная вкладка сменилась.</summary>
        public event EventHandler<TabEntry> ActiveTabChanged;

        /// <summary>Активирует закреплённую вкладку «Текст и очередь».</summary>
        public void ActivatePinned()
        {
            if (_pinnedTab != null)
                _manager.Activate(_pinnedTab);
        }

        /// <summary>Вкладка принадлежит этому окну и не является закреплённой.</summary>
        public bool IsOwnedRegularTab(TabEntry tab)
            => tab != null && !tab.IsPinned && _manager.Tabs.Contains(tab);

        /// <summary>Закрывает обычную вкладку (Esc). После закрытия возвращается «Текст и очередь».</summary>
        public void CloseRegularTab(TabEntry tab)
        {
            if (tab == null || tab.IsPinned)
                return;

            _manager.Close(tab);
            UpdateTabStripVisibility();

            if (_manager.ActiveTab == null)
                ActivatePinned();
        }

        #endregion

        #region ITabShell

        ShellKind ITabShell.Kind => ShellKind.FullScreen;

        TabManager ITabShell.Tabs => _manager;

        bool ITabShell.IsReady => true;

        bool ITabShell.IsTabStripEnabled => true;

        bool ITabShell.OpenInTab(TabContent content, bool forceNew) => OpenInTab(content, forceNew);

        bool ITabShell.Activate(TabKey key)
        {
            var entry = _manager.Find(key);
            if (entry == null)
                return false;

            _manager.Activate(entry);
            return true;
        }

        bool ITabShell.Close(TabEntry entry) => _manager.Close(entry);

        IEnumerable<TabEntry> ITabShell.GetTransferableTabs()
            => _manager.Tabs.Where(t => !t.IsPinned);

        #endregion

        #region Tab management

        /// <summary>Создаёт закреплённую вкладку с содержимым «Текст и очередь».</summary>
        private void CreatePinnedTab()
        {
            _pinnedTab = _manager.Create(
                LyricsQueueKey,
                LyricsQueueHeader,
                LyricsQueueGlyph,
                isClosable: false,
                isPinned: true);

            _manager.Activate(_pinnedTab);
        }

        /// <summary>
        /// Открывает контент: при Ctrl (или если закреплённая вкладка активна) — в новой
        /// вкладке, иначе переиспользует активную обычную вкладку / уже открытую с тем же ключом.
        /// </summary>
        private bool OpenInTab(TabContent content, bool forceNew)
        {
            if (content == null)
                return false;

            if (!forceNew && content.Key != null)
            {
                var existing = _manager.Find(content.Key);
                if (existing != null)
                {
                    _manager.Activate(existing);

                    if (existing.ContentFrame != null && existing.ContentFrame.Children.Count == 0)
                        content.Navigate(existing);

                    return true;
                }
            }

            var active = _manager.ActiveTab;

            // Закреплённую вкладку нельзя переиспользовать под другой контент.
            if (!forceNew && active != null && !active.IsPinned)
            {
                active.Key = content.Key;
                active.Header = content.Header;
                active.Glyph = content.Glyph;
                active.ApplyHeader();

                // Новое содержимое в той же вкладке: очищаем прежний стек навигации.
                active.ContentFrame.ClearBackStack();
                active.ContentFrame.Children.Clear();

                content.Navigate(active);
                return true;
            }

            var tab = _manager.CreateAndActivate(content.Key, content.Header, content.Glyph);
            if (tab == null)
                return false;

            content.Navigate(tab);
            return true;
        }

        private void Manager_ActiveTabChanged(object sender, TabEntry e)
        {
            UpdateTabStripVisibility();
            ActiveTabChanged?.Invoke(this, e);
        }

        private void Manager_TabCreated(object sender, TabEntry e)
        {
            UpdateTabStripVisibility();

            if (e?.ContentFrame == null)
                return;

            // Стек вкладки должен занимать всю площадь, а фон — быть прозрачным,
            // чтобы сквозь него просвечивала обложка окна плеера.
            e.ContentFrame.HorizontalAlignment = HorizontalAlignment.Stretch;
            e.ContentFrame.VerticalAlignment = VerticalAlignment.Stretch;
            e.ContentFrame.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

            // Навигация внутри вкладки (плейлист → артист и т.п.) обновляет её заголовок,
            // как это делает главное окно через SyncMenuSelection.
            e.ContentFrame.Navigated += (s, _) => SyncHeaderFromFrame(e);
        }

        /// <summary>Выравнивает заголовок вкладки по текущей странице её стека навигации.</summary>
        private static void SyncHeaderFromFrame(TabEntry tab)
        {
            if (tab?.ContentFrame == null || tab.IsPinned)
                return;

            var sectionType = tab.ContentFrame.frameCurrent?.sectionType;
            if (sectionType == null)
                return;

            // «Плейлист»/«Артист»/«Поиск» — заголовок зависит только от типа раздела.
            switch (sectionType.Value)
            {
                case SectionType.Search:
                    tab.Header = "Поиск";
                    tab.Glyph = "\uE11A";
                    tab.ApplyHeader();
                    break;
                case SectionType.Artist:
                    tab.Header = "Артист";
                    tab.Glyph = "\uE77B";
                    tab.ApplyHeader();
                    break;
            }
        }

        private void Manager_CloseRequested(object sender, TabEntry e)
        {
            // Закреплённая вкладка не закрывается никогда.
            if (e == null || e.IsPinned)
                return;

            CloseAndRefresh(e);
        }

        /// <summary>Закрывает вкладку и пересчитывает видимость ленты (осталась одна — прячем).</summary>
        private void CloseAndRefresh(TabEntry e)
        {
            _manager.Close(e);
            UpdateTabStripVisibility();
        }

        private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Смена выбора обрабатывается в TabManager (ActiveTabChanged).
        }

        private void Tabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            var entry = _manager?.FromItem(args.Tab);
            if (entry == null || entry.IsPinned)
                return;

            CloseAndRefresh(entry);
        }

        #endregion
    }
}
