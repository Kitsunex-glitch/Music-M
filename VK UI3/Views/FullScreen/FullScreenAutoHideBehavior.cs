using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace VK_UI3.Views.FullScreen
{
    /// <summary>
    /// Управляет автоскрытием оверлея полноэкранного плеера.
    /// После <see cref="IdleTimeout"/> без движения мыши/нажатий клавиш элементы плавно угасают (fade-out),
    /// при любом вводе — снова появляются (fade-in). Скрытие блокируется, если:
    /// курсор находится над панелью управления, открыт Flyout/контекстное меню или очередь.
    /// Анимации — Composition (без Storyboard/TargetName), поэтому не зависят от XAML-скоупа
    /// и не бросают "Animation target not specified".
    /// </summary>
    public sealed class FullScreenAutoHideBehavior
    {
        private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(300);

        private readonly DispatcherTimer _timer;
        private readonly List<FrameworkElement> _targets = new List<FrameworkElement>();

        // Элементы, которые при скрытии не только гаснут, но и «схлопываются»:
        // высота и внутренние отступы уходят в 0 (и возвращаются обратно). Нужно для
        // шапки правой панели, чтобы при появлении/скрытии оверлея контент плавно
        // переезжал, а не прыгал.
        //
        // Thickness не анимируется compositor-ом, ThicknessAnimation в WinUI 3 нет,
        // а Storyboard без TargetName не работает. Поэтому интерполируем значения
        // вручную через async-цикл с Task.Delay(1) — тот же приём, что в
        // VK_UI3.Helpers.Animations.AnimatedButton (AnimateSize/AnimateMargin).
        private readonly List<CollapsibleEntry> _collapsibles = new List<CollapsibleEntry>();

        private sealed class CollapsibleEntry
        {
            public Border Element { get; init; }
            public double Height { get; init; }
            public Thickness Padding { get; init; }

            // Токен отмены текущей анимации этого блока.
            public CancellationTokenSource Cancellation { get; set; }
        }

        private bool _isRunning;
        private bool _isHidden;
        private bool _isPointerOverControls;
        private bool _isBlocked;

        public FullScreenAutoHideBehavior()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _timer.Tick += OnTimerTick;
        }

        /// <summary>Сколько ждать бездействия перед скрытием (по умолчанию 4 секунды).</summary>
        public TimeSpan IdleTimeout
        {
            get => _timer.Interval;
            set => _timer.Interval = value;
        }

        /// <summary>Оверлей сейчас скрыт (только текст на экране).</summary>
        public bool IsHidden => _isHidden;

        /// <summary>Цель fade-анимаций (панель управления).</summary>
        public FrameworkElement Target { get; private set; }

        /// <summary>
        /// Настраивает поведение: цель анимации и обработчики ввода на корневом элементе.
        /// Вызывать один раз после загрузки XAML.
        /// </summary>
        public void Initialize(FrameworkElement root, FrameworkElement target)
        {
            if (root == null || target == null)
                return;

            Target = target;

            root.PointerMoved += OnPointerMoved;
            root.PointerEntered += OnPointerMoved;

            target.PointerEntered += OnControlsPointerEntered;
            target.PointerExited += OnControlsPointerExited;
        }

        /// <summary>Добавляет элемент, который скрывается вместе с панелью (например, подписи/кнопку закрытия).</summary>
        public void AddTarget(FrameworkElement element)
        {
            if (element == null || _targets.Contains(element))
                return;

            _targets.Add(element);
        }

        /// <summary>
        /// Регистрирует блок, который при скрытии дополнительно «схлопывается»:
        /// высота и внутренние отступы плавно уходят в 0, а при показе возвращаются
        /// к исходным. Исходные значения запоминаются на момент вызова —
        /// вызывать после загрузки XAML.
        /// </summary>
        public void AddCollapsible(Border element)
        {
            if (element == null || _collapsibles.Exists(entry => ReferenceEquals(entry.Element, element)))
                return;

            _collapsibles.Add(new CollapsibleEntry
            {
                Element = element,
                Height = element.Height,
                Padding = element.Padding
            });
        }

        /// <summary>Старт автоскрытия: показать элементы и запустить таймер.</summary>
        public void Start()
        {
            _isRunning = true;
            Show();
            RestartTimer();
        }

        /// <summary>Остановка: элементы остаются в видимом состоянии, таймер снят.</summary>
        public void Stop()
        {
            _isRunning = false;
            _timer.Stop();
            Show();
        }

        /// <summary>Блокировка автоскрытия (открыт Flyout/контекстное меню/очередь).</summary>
        public void BlockAutoHide()
        {
            _isBlocked = true;
            _timer.Stop();
            Show();
        }

        /// <summary>Снятие блокировки с перезапуском таймера.</summary>
        public void UnblockAutoHide()
        {
            _isBlocked = false;
            RestartTimer();
        }

        /// <summary>Немедленно показать элементы и перезапустить таймер (например, по нажатию клавиши).</summary>
        public void WakeUp()
        {
            if (!_isRunning)
                return;

            Show();
            RestartTimer();
        }

        /// <summary>Снять обработчики — вызывается при выходе из полноэкранного режима.</summary>
        public void Detach()
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;

            if (Target != null)
            {
                var root = Target.Parent as FrameworkElement;
                if (root != null)
                {
                    root.PointerMoved -= OnPointerMoved;
                    root.PointerEntered -= OnPointerMoved;
                }

                Target.PointerEntered -= OnControlsPointerEntered;
                Target.PointerExited -= OnControlsPointerExited;
            }

            StopFade();

            // Отменяем незавершённые анимации и возвращаем блоки в исходное
            // состояние — окно может быть показано снова.
            foreach (var entry in _collapsibles)
            {
                entry.Cancellation?.Cancel();
                entry.Cancellation = null;
                ApplyCollapseLayout(entry, hidden: false);
            }

            _isRunning = false;
        }

        #region Input handlers

        private void OnPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            WakeUp();
        }

        private void OnControlsPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _isPointerOverControls = true;
            WakeUp();
        }

        private void OnControlsPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _isPointerOverControls = false;
            RestartTimer();
        }

        #endregion

        #region Timer / animation

        private void RestartTimer()
        {
            if (!_isRunning || _isBlocked)
                return;

            _timer.Stop();
            _timer.Start();
        }

        private void OnTimerTick(object sender, object e)
        {
            _timer.Stop();

            if (!_isRunning || _isBlocked || _isPointerOverControls)
            {
                // Условия для скрытия не выполнены — просто продлеваем ожидание.
                if (_isRunning && !_isBlocked)
                    _timer.Start();
                return;
            }

            Hide();
        }

        private void Show()
        {
            if (Target == null)
                return;

            _isHidden = false;
            Fade(1.0);
            SetHitTestVisible(true);

            // Схлопнутые блоки раскрываются обратно: высота и отступы возвращаются.
            foreach (var entry in _collapsibles)
                AnimateCollapse(entry, hidden: false);
        }

        private void Hide()
        {
            if (Target == null)
                return;

            _isHidden = true;
            Fade(0.0);
            // После fade-out элементы не должны перехватывать мышь.
            SetHitTestVisible(false);

            foreach (var entry in _collapsibles)
            {
                AnimateCollapse(entry, hidden: true);
            }
        }

        /// <summary>
        /// Запускает «схлопывание»/раскрытие блока. Промежуточные значения Height и
        /// Padding выставляются в async-цикле — тот же приём, что в
        /// <c>AnimatedButton.AnimateSize/AnimateMargin</c>.
        /// </summary>
        private void AnimateCollapse(CollapsibleEntry entry, bool hidden)
        {
            var element = entry.Element;
            if (element == null)
                return;

            // Отменяем предыдущую анимацию этого блока (быстрое дёргание мыши).
            entry.Cancellation?.Cancel();
            entry.Cancellation = new CancellationTokenSource();

            _ = AnimateCollapseAsync(entry, hidden, entry.Cancellation.Token);
        }

        private async Task AnimateCollapseAsync(CollapsibleEntry entry, bool hidden, CancellationToken token)
        {
            var element = entry.Element;
            if (element == null)
                return;

            double fromHeight = element.Height > 0 ? element.Height : element.ActualHeight;
            double toHeight = hidden ? 0 : entry.Height;

            Thickness fromPadding = element.Padding;
            Thickness toPadding = hidden ? new Thickness(0) : entry.Padding;

            // Уже в целевом состоянии — анимировать нечего.
            if (Math.Abs(fromHeight - toHeight) < 0.01 && fromPadding.Equals(toPadding))
            {
                ApplyCollapseLayout(entry, hidden);
                return;
            }

            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < FadeDuration)
            {
                if (token.IsCancellationRequested)
                {
                    stopwatch.Stop();
                    return;
                }

                double progress = Math.Clamp(stopwatch.Elapsed.TotalSeconds / FadeDuration.TotalSeconds, 0, 1);

                // EaseInOutCubic — та же кривая, что и у fade оверлея.
                double eased = progress < 0.5
                    ? 4 * progress * progress * progress
                    : 1 - Math.Pow(-2 * progress + 2, 3) / 2;

                element.Height = Lerp(fromHeight, toHeight, eased);
                element.Padding = Lerp(fromPadding, toPadding, eased);

                await Task.Delay(1);
            }

            stopwatch.Stop();

            if (token.IsCancellationRequested)
                return;

            ApplyCollapseLayout(entry, hidden);
        }

        private static double Lerp(double from, double to, double t) => from + (to - from) * t;

        private static Thickness Lerp(Thickness from, Thickness to, double t) => new(
            Lerp(from.Left, to.Left, t),
            Lerp(from.Top, to.Top, t),
            Lerp(from.Right, to.Right, t),
            Lerp(from.Bottom, to.Bottom, t));

        /// <summary>Жёстко выставляет конечное состояние (без анимации).</summary>
        private static void ApplyCollapseLayout(CollapsibleEntry entry, bool hidden)
        {
            var element = entry.Element;
            if (element == null)
                return;

            element.Height = hidden ? 0 : entry.Height;
            element.Padding = hidden ? new Thickness(0) : entry.Padding;
        }

        private void SetHitTestVisible(bool value)
        {
            if (Target != null)
                Target.IsHitTestVisible = value;
        }

        /// <summary>
        /// Плавно меняет прозрачность всех целей до <paramref name="to"/> (1 — показать, 0 — скрыть).
        /// Таргеты задаются явно для каждого визуала — Storyboard и TargetName не используются.
        /// </summary>
        private void Fade(double to)
        {
            foreach (var element in EnumerateTargets())
            {
                var visual = ElementCompositionPreview.GetElementVisual(element);
                var compositor = visual.Compositor;

                var animation = compositor.CreateScalarKeyFrameAnimation();
                animation.InsertKeyFrame(1f, (float)to,
                    compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.2f, 1f)));
                animation.Duration = FadeDuration;

                visual.StartAnimation("Opacity", animation);
            }
        }

        private void StopFade()
        {
            foreach (var element in EnumerateTargets())
                ElementCompositionPreview.GetElementVisual(element).StopAnimation("Opacity");
        }

        private IEnumerable<FrameworkElement> EnumerateTargets()
        {
            if (Target != null)
                yield return Target;

            foreach (var element in _targets)
                yield return element;
        }

        #endregion
    }
}
