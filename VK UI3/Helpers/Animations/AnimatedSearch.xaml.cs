using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Threading;
using Windows.Foundation;
using Windows.Foundation.Collections;
using VK_UI3.Views;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace VK_UI3.Helpers.Animations
{
    /// <summary>
    /// Строка поиска с анимацией раскрытия/сворачивания.
    /// Анимация построена по тому же принципу, что <see cref="AnimatedButton"/>:
    /// исходные <see cref="FrameworkElement.Width"/>, <see cref="FrameworkElement.Height"/>
    /// и <see cref="FrameworkElement.Margin"/> запоминаются один раз, а затем пошагово
    /// интерполируются до целевых значений (с отменой предыдущей анимации).
    /// </summary>
    public sealed partial class AnimatedSearch : Grid
    {
        public AnimatedSearch()
        {
            this.InitializeComponent();

            // Скрываем содержимое до первого раскрытия: иначе AutoSuggestBox
            // с фиксированной высотой строки «выпрыгивает» из схлопнутой панели.
            CollapseContent(immediate: true);
        }

        #region Dependency properties

        public static readonly DependencyProperty OriginalHeightProperty = DependencyProperty.Register(
            "OriginalHeight", typeof(double), typeof(AnimatedSearch), new PropertyMetadata(default(double)));

        public double? OriginalHeight
        {
            get { return (double)GetValue(OriginalHeightProperty); }
            set { SetValue(OriginalHeightProperty, value); }
        }

        public static readonly DependencyProperty OriginalMarginProperty = DependencyProperty.Register(
            "OriginalMargin", typeof(Thickness), typeof(AnimatedSearch), new PropertyMetadata(default(Thickness)));

        public Thickness? OriginalMargin
        {
            get { return (Thickness)GetValue(OriginalMarginProperty); }
            set { SetValue(OriginalMarginProperty, value); }
        }

        public string searchtext { get { return searchTXT.Text; } set { searchTXT.Text = value; } }

        #endregion

        private CancellationTokenSource cts;

        /// <summary>Исходные параметры запомнены (панель уже раскрывалась).</summary>
        private bool _originalCaptured;

        /// <summary>
        /// Запоминает исходные размеры и отступ панели один раз — как
        /// <c>AnimatedButton.HideButton</c> фиксирует <c>OriginalWidth/Height/Margin</c>.
        /// </summary>
        private void CaptureOriginal()
        {
            if (_originalCaptured)
                return;

            if (OriginalHeight == null || OriginalHeight <= 0)
                OriginalHeight = Height > 0 ? Height : 50;

            if (OriginalMargin == null)
                OriginalMargin = Margin;

            _originalCaptured = true;
        }

        #region Animation

        /// <summary>
        /// Пошаговая интерполяция высоты и отступа (тот же приём, что
        /// <see cref="AnimatedButton.AnimateSize"/> и <see cref="AnimatedButton.AnimateMargin"/>).
        /// </summary>
        public async Task AnimateSize(double? newHeight, Thickness newMargin, TimeSpan duration)
        {
            // Отменяем предыдущую анимацию ДО проверки «уже в цели»: иначе при быстром
            // повторном вызове (одновременное открытие вкладки и поиска) предыдущая
            // анимация успевает дописать свои конечные значения уже после старта новой
            // и перезаписывает её промежуточные — панель «прыгает» на чужой размер.
            cts?.Cancel();
            cts = new CancellationTokenSource();
            var token = cts.Token;

            if (ActualHeight == newHeight && Margin == newMargin)
                return;

            var startHeight = ActualHeight;
            var startMargin = Margin;

            double targetHeight = newHeight ?? startHeight;

            double heightStep = (targetHeight - startHeight) / duration.TotalMilliseconds;
            double leftStep = (newMargin.Left - startMargin.Left) / duration.TotalMilliseconds;
            double topStep = (newMargin.Top - startMargin.Top) / duration.TotalMilliseconds;
            double rightStep = (newMargin.Right - startMargin.Right) / duration.TotalMilliseconds;
            double bottomStep = (newMargin.Bottom - startMargin.Bottom) / duration.TotalMilliseconds;

            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < duration)
            {
                if (token.IsCancellationRequested)
                {
                    stopwatch.Stop();
                    return;
                }

                double elapsed = stopwatch.Elapsed.TotalMilliseconds;

                this.Height = startHeight + heightStep * elapsed;
                this.Margin = new Thickness(
                    startMargin.Left + leftStep * elapsed,
                    startMargin.Top + topStep * elapsed,
                    startMargin.Right + rightStep * elapsed,
                    startMargin.Bottom + bottomStep * elapsed);

                await Task.Delay(1);
            }

            stopwatch.Stop();

            this.Height = targetHeight;
            this.Margin = newMargin;
        }

        private static readonly TimeSpan AnimationDuration = TimeSpan.FromSeconds(0.25);

        /// <summary>
        /// Скрывает строку поиска: высота и отступ уходят в 0.
        /// Как <c>AnimatedButton.HideButton</c> — сначала размер, затем отступ.
        /// </summary>
        public async Task Hide()
        {
            CaptureOriginal();

            // Убираем содержимое сразу: AutoSuggestBox не должен «висеть» в схлопнутой панели.
            CollapseContent(immediate: false);

            await AnimateSize(0, new Thickness(0), AnimationDuration);
            IsHitTestVisible = false;
        }

        /// <summary>
        /// Раскрывает строку поиска до исходного размера и отступа.
        /// Как <c>AnimatedButton.ShowButton</c> — сначала размер, затем отступ.
        /// </summary>
        public async Task Show()
        {
            CaptureOriginal();

            IsHitTestVisible = true;

            await AnimateSize(OriginalHeight, OriginalMargin ?? new Thickness(0), AnimationDuration);

            // Показываем содержимое только когда панель полностью раскрылась.
            CollapseContent(immediate: false, visible: true);
        }

        /// <summary>Прячет/показывает содержимое панели (сама панель при этом анимируется).</summary>
        private void CollapseContent(bool immediate, bool visible = false)
        {
            if (searchTXT == null)
                return;

            searchTXT.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

            if (immediate)
            {
                this.Height = 0;
                this.Margin = new Thickness(0);
                IsHitTestVisible = false;
            }
        }

        #endregion

        private void AutoSuggestBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            MainView.OpenSearchSection(searchTXT.Text);
        }
    }
}
