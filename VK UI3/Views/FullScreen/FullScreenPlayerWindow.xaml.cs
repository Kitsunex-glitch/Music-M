using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Numerics;
using VK_UI3.Helpers;
using VK_UI3.Helpers.Animations;
using VK_UI3.Services;
using VK_UI3.Views.ModalsPages;
using Windows.Graphics;
using Windows.System;

namespace VK_UI3.Views.FullScreen
{
    /// <summary>
    /// Отдельное окно полноэкранного плеера.
    /// Левая половина: обложка + метаданные + управление.
    /// Правая половина: переключение между текстом песни и очередью треков с анимацией.
    /// Состояние воспроизведения общее — используется статический <see cref="MediaPlayerService"/>.
    /// </summary>
    public sealed partial class FullScreenPlayerWindow : Window
    {
        private readonly FullScreenPlayerViewModel _viewModel = new FullScreenPlayerViewModel();
        private readonly FullScreenAutoHideBehavior _autoHide = new FullScreenAutoHideBehavior();

        private AnimationsChangeImage _backgroundImageAnimator;
        private AnimationsChangeImage _coverImageAnimator;
        // Анимация смены текста — как changeText/changeText2 в AudioPlayer.xaml.cs.
        private AnimationsChangeText _titleAnimator;
        private AnimationsChangeText _artistAnimator;

        private bool _isQueueShown;
        private bool _isClosing;
        // Защита от рекурсии: синхронизация SourceCombo с самой страницей текста.
        private bool _syncingSourceCombo;
        // Собственный экземпляр страницы текста для этого окна (не переносим из MainView).
        private LyricsPage _ownLyricsPage;
        // Защита от рекурсии: SetSelectedSection меняет choosen, что снова вызывает onChangeSelected.
        private bool _syncingSwitcher;
        // Composition-анимации переключения разделов (Storyboard в коде без TargetName в WinUI 3
        // бросает "Animation target not specified"). Активный batch держим, чтобы прервать
        // предыдущую анимацию при быстром переключении вкладок.
        private CompositionScopedBatch _activeSwapBatch;

        public FullScreenPlayerWindow()
        {
            this.InitializeComponent();

            // Кастомная шапка не нужна — окно целиком контент (полноэкранный режим).
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(null);

            HookUpControls();

            _viewModel.PropertyChanged += ViewModel_PropertyChanged;

            RootGrid.KeyDown += RootGrid_KeyDown;
            RootGrid.Loaded += RootGrid_Loaded;

            this.Closed += FullScreenPlayerWindow_Closed;
        }

        /// <summary>Окно закрывается пользователем (кнопка, Esc, системное закрытие).</summary>
        public event EventHandler CloseRequested;

        #region Lifecycle

        /// <summary>
        /// Показывает окно в полноэкранном режиме, подписывается на состояние и создаёт
        /// собственную страницу текста. Вызывать в UI-потоке главного окна.
        /// </summary>
        public void ActivateFullScreen()
        {
            var appWindow = this.AppWindow;
            if (appWindow != null)
            {
                // Показываем на мониторе, где находится главное окно, и разворачиваем на весь экран.
                TryMoveToMainDisplay(appWindow);
                appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            }

            this.Activate();

            _viewModel.Attach();

            _backgroundImageAnimator ??= new AnimationsChangeImage(WindowBackgroundImage, DispatcherQueue);
            _coverImageAnimator ??= new AnimationsChangeImage(CoverImage, DispatcherQueue);
            _titleAnimator ??= new AnimationsChangeText(TrackTitle, DispatcherQueue);
            _artistAnimator ??= new AnimationsChangeText(TrackArtist, DispatcherQueue);

            ApplyTrackVisuals();
            UpdateTexts();

            // ВАЖНО: НЕ переносим LyricsPage из MainView. BetterLyric построен на Win2D
            // CanvasAnimatedControl, который нельзя перенести в другое окно — он теряет
            // device и перестаёт рисовать (текст пропадал и в MainView, и здесь).
            // Создаём собственный экземпляр страницы для этого окна; состояние берётся
            // из MediaPlayerService (PlayingTrack + позиция), поэтому синхронизация общая.
            EnsureOwnLyricsPage();
            LyricsPlaceholder.Visibility = Visibility.Collapsed;
            SyncSourceCombo();

            // Текст песни активен только пока открыт раздел «Текст» — иначе таймер
            // и загрузка текста не должны работать.
            if (_ownLyricsPage != null)
            {
                if (_isQueueShown)
                    _ownLyricsPage.Disable();
                else
                    _ownLyricsPage.Enable();
            }

            _autoHide.Initialize(RootGrid, Controls);

            // Панель действий над текстом и переключатель разделов живут вместе
            // с остальным оверлеем: появляются при движении мыши, гаснут по таймауту.
            // (Кнопка выхода теперь только в панели управления внизу.)
            _autoHide.AddTarget(LyricsActionsPanel);
            _autoHide.AddTarget(SectionsSwitcher);

            // Шапка правой панели (переключатель + действия над текстом) не просто
            // гаснет, а плавно «схлопывается» по высоте и отступам.
            _autoHide.AddCollapsible(RightPaneHeader);

            _autoHide.Start();

            RootGrid.Focus(FocusState.Programmatic);
        }

        /// <summary>Освобождает ресурсы окна. Экземпляр LyricsPage этого окна уничтожается вместе с ним.</summary>
        public void Cleanup()
        {
            _autoHide.Detach();
            _viewModel.Detach();

            // Останавливаем синхронизацию собственной страницы и отпускаем хост.
            if (_ownLyricsPage != null)
            {
                _ownLyricsPage.Disable();
                _ownLyricsPage = null;
            }

            LyricsHost.Content = null;
        }

        /// <summary>
        /// Создаёт собственный <see cref="LyricsPage"/> для полноэкранного окна (один раз),
        /// переводит его в полноэкранный вид и помещает в <see cref="LyricsHost"/>.
        /// </summary>
        private void EnsureOwnLyricsPage()
        {
            if (_ownLyricsPage != null)
            {
                if (!ReferenceEquals(LyricsHost.Content, _ownLyricsPage))
                    LyricsHost.Content = _ownLyricsPage;
                return;
            }

            _ownLyricsPage = new LyricsPage();
            _ownLyricsPage.SetFullScreenMode(true);
            LyricsHost.Content = _ownLyricsPage;
        }

        /// <summary>
        /// Перемещает окно на монитор главного окна перед разворотом в полный экран.
        /// </summary>
        private void TryMoveToMainDisplay(AppWindow appWindow)
        {
            try
            {
                var mainWindow = MainWindow.mainWindow;
                if (mainWindow?.AppWindow == null)
                    return;

                var displayArea = DisplayArea.GetFromWindowId(mainWindow.AppWindow.Id, DisplayAreaFallback.Primary);
                if (displayArea != null)
                    appWindow.MoveAndResize(displayArea.WorkArea);
            }
            catch
            {
                // Геометрия не критична — окно всё равно развернётся в FullScreen.
            }
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            RootGrid.Focus(FocusState.Programmatic);
        }

        private void FullScreenPlayerWindow_Closed(object sender, WindowEventArgs args)
        {
            if (_isClosing)
                return;

            _isClosing = true;
            Cleanup();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Lyrics actions (source / settings)

        /// <summary>Заполняет список источников и выравнивает его по текущему выбору страницы.</summary>
        private void SyncSourceCombo()
        {
            if (SourceCombo == null)
                return;

            _syncingSourceCombo = true;
            try
            {
                if (SourceCombo.Items.Count == 0)
                {
                    foreach (var name in LyricsPage.LyricsSourceNames)
                        SourceCombo.Items.Add(name);
                }

                SourceCombo.SelectedIndex = _ownLyricsPage?.LyricsSourceIndex ?? 0;
            }
            finally
            {
                _syncingSourceCombo = false;
            }
        }

        private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingSourceCombo || SourceCombo.SelectedIndex < 0)
                return;

            // Страница сама перезагрузит текст и сохранит выбор в настройках.
            _ownLyricsPage?.SetLyricsSource(SourceCombo.SelectedIndex);
        }

        private void LyricsSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Настройки живут внутри страницы текста — открываем их на весь экран.
            ShowLyricsTab();
            _ownLyricsPage?.OpenSettingsPane();
        }

        #endregion

        #region Controls wiring

        private void HookUpControls()
        {
            Controls.Initialize(_viewModel);
            Controls.LyricsRequested += (s, e) => ShowLyricsTab();
            Controls.MoreRequested += (s, e) => ShowTrackContextMenu(Controls);
            Controls.CloseRequested += (s, e) => RequestClose();

            QueuePane.QueueLoaded += (s, e) => ScrollQueueToPlayingTrack();

            // Переключатель разделов: индекс 0 — «Текст песни», 1 — «Очередь».
            SectionsSwitcher.onChangeSelected += SectionsSwitcher_OnChangeSelected;

            FlyOutControl.Opened += FlyOutControl_Opened;
            FlyOutControl.Closed += FlyOutControl_Closed;
        }

        private void SectionsSwitcher_OnChangeSelected(object sender, EventArgs e)
        {
            if (SectionsSwitcher.choosen == 1)
                ShowQueueTab();
            else
                ShowLyricsTab();
        }

        /// <summary>Запрос на закрытие окна (обрабатывается владельцем — контроллером).</summary>
        private void RequestClose()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Input

        private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.Escape:
                    if (_isQueueShown)
                        ShowLyricsTab();
                    else
                        RequestClose();
                    e.Handled = true;
                    break;

                case VirtualKey.Space:
                    _ = MediaPlayerService.TogglePlayPause();
                    _autoHide.WakeUp();
                    e.Handled = true;
                    break;

                case VirtualKey.Left:
                    MediaPlayerService.SeekBy(-5);
                    _autoHide.WakeUp();
                    e.Handled = true;
                    break;

                case VirtualKey.Right:
                    MediaPlayerService.SeekBy(5);
                    _autoHide.WakeUp();
                    e.Handled = true;
                    break;

                default:
                    _autoHide.WakeUp();
                    break;
            }
        }

        #endregion

        #region ViewModel sync

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(FullScreenPlayerViewModel.Title):
                case nameof(FullScreenPlayerViewModel.Artist):
                    UpdateTexts();
                    break;
                case nameof(FullScreenPlayerViewModel.Thumbnail):
                    ApplyTrackVisuals();
                    break;
            }

            // Смена трека: если открыт раздел очереди — прокручиваем список к играющему треку.
            if (e.PropertyName is nameof(FullScreenPlayerViewModel.Title)
                or nameof(FullScreenPlayerViewModel.Artist)
                or nameof(FullScreenPlayerViewModel.Thumbnail))
            {
                ScrollQueueToPlayingTrack();
            }
        }

        /// <summary>
        /// Прокручивает очередь к текущему треку. Использует ту же логику, что и PlayListPage:
        /// ContainerFromIndex + ChangeView по вертикальному смещению контейнера.
        /// </summary>
        private void ScrollQueueToPlayingTrack()
        {
            if (!_isQueueShown)
                return;

            // Отложенная прокрутка — контейнеры списка должны успеть виртуализироваться.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (!_isQueueShown)
                    return;

                var listView = FindDescendant<ListView>(QueuePane);
                var source = MediaPlayerService.iVKGetAudio;
                var track = MediaPlayerService.PlayingTrack;

                if (listView == null || source?.listAudio == null || track?.audio == null)
                    return;

                var index = -1;
                for (int i = 0; i < source.listAudio.Count; i++)
                {
                    var a = source.listAudio[i]?.audio;
                    if (a != null && a.Id == track.audio.Id && a.OwnerId == track.audio.OwnerId)
                    {
                        index = i;
                        break;
                    }
                }

                if (index < 0)
                    index = (int)(source.currentTrack ?? -1);

                if (index < 0)
                    return;

                try
                {
                    if (listView.ContainerFromIndex(index) is ListViewItem container)
                    {
                        var scrollViewer = FindDescendant<ScrollViewer>(listView);
                        var transform = container.TransformToVisual(listView);
                        var position = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

                        if (scrollViewer != null)
                        {
                            scrollViewer.ChangeView(
                                null,
                                scrollViewer.VerticalOffset + position.Y - scrollViewer.ViewportHeight / 3,
                                null);
                        }
                    }
                    else
                    {
                        // Контейнер ещё не создан — используем ScrollIntoView.
                        listView.ScrollIntoView(source.listAudio[index], ScrollIntoViewAlignment.Leading);
                    }
                }
                catch
                {
                    // Прокрутка не критична для работы плеера.
                }
            });
        }

        private static T FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null)
                return null;

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    return match;

                var nested = FindDescendant<T>(child);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private void UpdateTexts()
        {
            // Смена текста с анимацией (fade), как changeText2/changeText в AudioPlayer.
            if (_titleAnimator != null)
                _titleAnimator.ChangeTextWithAnimation(_viewModel.Title ?? string.Empty);
            else
                TrackTitle.Text = _viewModel.Title;

            if (_artistAnimator != null)
                _artistAnimator.ChangeTextWithAnimation(_viewModel.Artist ?? string.Empty);
            else
                TrackArtist.Text = _viewModel.Artist;

            var name = string.IsNullOrEmpty(_viewModel.Artist)
                ? _viewModel.Title
                : $"{_viewModel.Artist} — {_viewModel.Title}";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(RootGrid, name);
        }

        private void ApplyTrackVisuals()
        {
            var thumb = _viewModel.Thumbnail;
            _backgroundImageAnimator?.ChangeImageWithAnimation(thumb);
            _coverImageAnimator?.ChangeImageWithAnimation(thumb);
        }

        #endregion

        #region Context menu

        private void ShowTrackContextMenu(FrameworkElement anchor)
        {
            if (MediaPlayerService.PlayingTrack == null)
                return;

            FlyOutControl.dataTrack = MediaPlayerService.PlayingTrack;
            FlyOutControl.ShowAt(anchor);
        }

        private void FlyOutControl_Opened(object sender, object e) => _autoHide.BlockAutoHide();

        private void FlyOutControl_Closed(object sender, object e) => _autoHide.UnblockAutoHide();

        #endregion

        #region Right pane: Lyrics <-> Queue

        /// <summary>
        /// Показывает раздел «Текст песни». Если раздел уже активен — просто
        /// выравниваем состояние переключателя (без анимации, чтобы не было «пустого» swap).
        /// </summary>
        public void ShowLyricsTab()
        {
            SetSelectedSection(queueIndex: 0);

            // Включаем синхронизацию и загрузку текста — раздел действительно виден.
            _ownLyricsPage?.Enable();

            if (!_isQueueShown)
                return; // уже открыт текст

            _isQueueShown = false;

            // Входящий раздел показываем до старта анимации — иначе Composition её не отрисует.
            // Opacity НЕ сбрасываем здесь: начальный кадр задаётся в AnimateSwap (0f → 1f),
            // иначе при первом показе элемент, только что ставший Visible, мог не анимироваться.
            LyricsSection.Visibility = Visibility.Visible;

            RunSwapAnimation(showQueue: false);
        }

        /// <summary>
        /// Показывает раздел «Очередь». Если раздел уже активен — только выравниваем
        /// состояние вкладок, без повторной навигации.
        /// </summary>
        public void ShowQueueTab()
        {
            SetSelectedSection(queueIndex: 1);

            if (_isQueueShown)
                return; // уже открыта очередь

            _isQueueShown = true;

            // Раздел текста скрыт — останавливаем его таймер/загрузку.
            _ownLyricsPage?.Disable();

            QueuePane.LoadQueue();

            // Входящий раздел показываем до старта анимации — иначе Composition её не отрисует.
            // Opacity НЕ сбрасываем: начальный кадр задаётся в AnimateSwap (0f → 1f).
            QueueSection.Visibility = Visibility.Visible;

            RunSwapAnimation(showQueue: true);

            // После анимации список готов — прокручиваем к играющему треку.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ScrollQueueToPlayingTrack);
        }

        /// <summary>
        /// Выравнивает выбранный раздел в <see cref="SectionsSwitcher"/>.
        /// Свойство <c>choosen</c> само запускает анимацию «бегунка» и вызывает
        /// <c>onChangeSelected</c>, поэтому защищаемся от рекурсии флагом.
        /// </summary>
        private void SetSelectedSection(int queueIndex)
        {
            if (_syncingSwitcher || SectionsSwitcher.choosen == queueIndex)
                return;

            _syncingSwitcher = true;
            try
            {
                SectionsSwitcher.choosen = queueIndex;
            }
            finally
            {
                _syncingSwitcher = false;
            }
        }

        private const int SwapDurationMs = 260;

        /// <summary>
        /// Запускает Composition-анимацию переключения разделов — только кросс-фейд
        /// (без сдвигов по X/Y): уходящий раздел гаснет, приходящий проявляется.
        /// Таргеты задаются явно через ElementCompositionPreview — никаких
        /// Storyboard/TargetName, поэтому ошибка "Animation target not specified" невозможна.
        /// </summary>
        private void RunSwapAnimation(bool showQueue)
        {
            // Отменяем предыдущую анимацию (если пользователь быстро переключил вкладки).
            _activeSwapBatch?.Dispose();
            _activeSwapBatch = null;

            var incoming = showQueue ? QueueSection : LyricsSection;
            var outgoing = showQueue ? LyricsSection : QueueSection;

            var duration = TimeSpan.FromMilliseconds(SwapDurationMs);
            var compositor = ElementCompositionPreview.GetElementVisual(incoming).Compositor;
            var easing = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.4f, 0f),
                new Vector2(0.2f, 1f));

            // Один scoped batch — оба фейда стартуют одновременно и завершаются вместе.
            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            _activeSwapBatch = batch;

            AnimateSwap(incoming, 0f, 1f, duration, easing);
            AnimateSwap(outgoing, 1f, 0f, duration, easing);

            batch.Completed += (s, e) =>
            {
                if (_activeSwapBatch != batch)
                    return; // анимацию уже заменили новой

                // По завершении прячем погасший раздел, чтобы он не перехватывал ввод.
                outgoing.Visibility = Visibility.Collapsed;
                outgoing.Opacity = 1;
                ResetOffset(outgoing);

                if (!showQueue)
                    QueuePane.Clear();

                _activeSwapBatch = null;
                batch.Dispose();
            };

            batch.End();
        }

        private static void AnimateSwap(FrameworkElement element, float fromOpacity, float toOpacity,
            TimeSpan duration, CompositionEasingFunction easing)
        {
            if (element == null)
                return;

            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;

            // Сбрасываем ранее наложенные Composition-анимации (Dispose() у batch этого не делает).
            TryStopAnimation(visual, "Opacity");
            ResetOffset(element);

            // Явный стартовый кадр: если элемент только что стал Visible (первое открытие
            // раздела), visual может не иметь корректной начальной Opacity — без keyframe 0f
            // анимация не запускается и раздел остаётся невидимым.
            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, fromOpacity);
            fade.InsertKeyFrame(1f, toOpacity, easing);
            fade.Duration = duration;
            visual.StartAnimation("Opacity", fade);
        }

        /// <summary>
        /// Страховка от «залипшего» сдвига: анимация сдвига больше не используется,
        /// но если Translation когда-то остался выставленным — обнуляем его.
        /// </summary>
        private static void ResetOffset(FrameworkElement element)
        {
            if (element == null)
                return;

            var visual = ElementCompositionPreview.GetElementVisual(element);
            TryStopAnimation(visual, "Translation");
            visual.Offset = Vector3.Zero;
            element.Translation = Vector3.Zero;
        }

        /// <summary>
        /// Безопасно останавливает compositor-анимацию свойства. StopAnimation бросает
        /// ArgumentException (0x80070057), если для свойства не запущено ни одной анимации.
        /// </summary>
        private static void TryStopAnimation(Visual visual, string propertyName)
        {
            if (visual == null)
                return;

            try
            {
                visual.StopAnimation(propertyName);
            }
            catch (ArgumentException)
            {
                // Анимации для этого свойства нет — останавливать нечего.
            }
            catch (COMException)
            {
                // Визуал уже отключён/не готов — игнорируем.
            }
        }

        #endregion
    }
}
