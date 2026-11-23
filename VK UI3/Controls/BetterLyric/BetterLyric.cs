#nullable enable
using System.Threading;
using System.Threading.Tasks;
using System.Numerics;
using System.Collections.ObjectModel;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.DirectX;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.UI;
// https://github.com/jayfunc/BetterLyrics

using Microsoft.UI.Xaml.Input;

namespace VK_UI3.Controls.BetterLyric;

public partial class BetterLyric : Control
{
    private const string PART_Canvas = "PART_Canvas";
    private const string PART_ScrollViewer = "PART_ScrollViewer";
    private const string PART_UntimedBadge = "PART_UntimedBadge";
    private const string PART_Root = "PART_Root";

    // CanvasControl (не CanvasAnimatedControl): его composition-поверхность
    // поддерживает альфа-канал, поэтому канвас прозрачен и фон под ним виден.
    // Кадры двигает CompositionTarget.Rendering (см. BetterLyric.RenderLoop.cs).
    private CanvasControl canvas;
    private ScrollViewer _scrollViewer;

    /// <summary>
    /// Корневая панель шаблона. Нужна, чтобы вернуть <c>CanvasControl</c> в дерево:
    /// <c>RemoveFromVisualTree()</c> (вызывается в <c>OnUnloaded</c>) отвязывает
    /// элемент от Grid шаблона, и после повторного <c>Loaded</c> он остаётся вне
    /// дерева — Win2D тогда не создаёт device и не рисует ни строки.
    /// </summary>
    private Panel? _templateRoot;

    /// <summary>Плашка-подсказка «текст без синхронизации».</summary>
    private FrameworkElement? _untimedBadge;

    public event EventHandler<int>? LineClicked;

    private readonly LyricsSynchronizer _synchronizer = new();
    private readonly LyricsRenderer _lyricsRenderer = new();
    private readonly EdgeFadeMaskRenderer _edgeFadeMaskRenderer = new();

    private readonly ValueTransition<Color> _immersiveBgColorTransition = new(
        initialValue: Colors.Black,
        defaultTotalDuration: 0.3f,
        interpolator: (from, to, progress) => ColorHelper.GetInterpolatedColor(progress, from, to)
    );
    private readonly ValueTransition<double> _immersiveBgOpacityTransition = new(
        initialValue: 1f,
        AnimationEasingHelper.GetInterpolatorByEasingType<double>(AnimationEasingType.Sine),
        defaultTotalDuration: 0.3f
    );
    
    private readonly ValueTransition<double> _canvasYScrollTransition = new(
        initialValue: 0f,
        AnimationEasingHelper.GetInterpolatorByEasingType<double>(AnimationEasingType.Sine),
        defaultTotalDuration: 0.3f
    );
    private readonly ValueTransition<double> _mouseYScrollTransition = new(
        initialValue: 0f,
        AnimationEasingHelper.GetInterpolatorByEasingType<double>(AnimationEasingType.Sine),
        defaultTotalDuration: 0.3f
    );


    private TimeSpan _songPositionWithOffset;
    private TimeSpan _songPosition;

    
    private int _mouseHoverLineIndex = -1;

    private bool _isSnapToLineRequested = false;

    private List<RenderLyricsLine>? _renderLyricsLines = null;

    /// <summary>Данные, из которых построены текущие строки.</summary>
    private LyricData? _renderLyricsData = null;

    /// <summary>Признаки раскладки (шрифт/выравнивание/размеры), для которых построены строки.</summary>
    private LayoutSignature? _layoutSignature = null;

    /// <summary>Признаки, влияющие на содержимое геометрии и кэшей строк.</summary>
    private (int StrokeWidth, bool Phonetic, bool Translation)? _contentSignature = null;

    /// <summary>
    /// Свойства раскладки/содержимого изменились до того, как появился Canvas
    /// (например, настройки применены в конструкторе страницы, до загрузки шаблона).
    /// Такие изменения не теряются: при появлении Canvas выполняется полный пересчёт.
    /// </summary>
    private bool _isRelayoutPendingBeforeTemplate = false;

    private readonly Debouncer _layoutDebouncer = new();
    private readonly Debouncer _scrollWheelDebounceTimer =new();
    private bool _isLayoutChanged = false;
    private bool _isNowPlayingPaletteChanged = false;

    /// <summary>
    /// Требуется пересоздать строки, потому что изменились данные или их содержимое.
    /// В отличие от <see cref="_isLayoutChanged"/> сбрасывается только после реальной перестройки.
    /// </summary>
    private bool _isContentDirty = false;

    private int _primaryPlayingLineIndex;

    /// <summary>
    /// Текущее время попало в разрыв (интро, пауза, инструментал): полотно уже
    /// ведётся к следующей строке, но она ещё не звучит — подсветку и масштаб
    /// включать нельзя, иначе строка «горит» до своего начала.
    /// </summary>
    private bool _isPlayingLinePending;
    private (int Start, int End) _visibleRange;
    private double _canvasTargetScrollOffset;

    /// <summary>
    /// Пользователь закончил ручную прокрутку, и полотно должно вернуться
    /// к активной строке песни. Живёт один кадр.
    /// </summary>
    private bool isReturningFromScroll;

    /// <summary>
    /// Свободный скролл заблокирован: идёт примагничивание к строке, выбранной кликом.
    /// Снимается, когда плеер подтвердил перемотку (или по дедлайну).
    /// </summary>
    private bool isScrollLockedBySnap;

    /// <summary>
    /// Индекс строки, к которой идёт примагничивание по клику.
    /// Пока плеер не перемотан на её начало, синхронизатор не должен
    /// возвращать старую строку — иначе текст сначала прыгает назад.
    /// </summary>
    private int _pendingSnapLineIndex = -1;

    /// <summary>
    /// Момент (в мс «внутренних часов»), после которого ожидание перемотки
    /// прекращается даже если плеер так и не догнал позицию.
    /// </summary>
    private double _pendingSnapDeadlineMs;

    /// <summary>
    /// Плеер подтвердил перемотку: его <c>Position</c> оказалась рядом с началом
    /// выбранной строки. Только это (или дедлайн) снимает <see cref="_pendingSnapLineIndex"/>.
    /// <para>
    /// Локальное совпадение синхронизатора — НЕ подтверждение: <c>SnapToLine</c>
    /// сам подставляет <c>_songPosition = line.StartMs</c>, поэтому синхронизатор
    /// соглашается мгновенно, а плеер ещё отдаёт старую позицию. Если снять флаг
    /// в этот момент, старая позиция принимается, строка откатывается назад
    /// и полотно прыгает к ней.
    /// </para>
    /// </summary>
    private bool _snapConfirmedByPlayer;

    /// <summary>Снимок последнего отрисованного диапазона — для диагностики пустых кадров.</summary>
    private int _lastDrawnLineCount = -1;
    private bool _isEmptyFrameReported = false;

    /// <summary>
    /// Текст без таймингов (см. <see cref="IsLyricsUntimed"/>): позиция плеера
    /// не используется для привязки строк, подсветка не включается.
    /// </summary>
    private bool isLyricsUntimed = false;

    /// <summary>
    /// Текущий текст статичный: свойство задано явно либо данные сами помечены
    /// как <see cref="LyricData.IsUntimed"/> (страховка, если страница забыла свойство).
    /// </summary>
    private bool IsUntimedNow => isLyricsUntimed || currentLyricsData?.IsUntimed == true;

    public TimeSpan SongPosition => _songPosition;
    public double CurrentCanvasYScroll => _canvasYScrollTransition.Value;
    public double ActualLyricsHeight => CalculateActualHeight(_renderLyricsLines);
    public int CurrentHoveringLineIndex => _mouseHoverLineIndex;

    public BetterLyric()
    {
        DefaultStyleKey = typeof(BetterLyric);
    }
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Корень шаблона: через него канвас возвращается в дерево после RemoveFromVisualTree().
        _templateRoot = GetTemplateChild(PART_Root) as Panel;

        canvas = GetTemplateChild(PART_Canvas) as CanvasControl;
        if (canvas == null)
        {
            BetterLyricDebugLog.WriteAlways(BetterLyricLogCategory.Lifecycle, "OnApplyTemplate: PART_Canvas не найден!");
            return;
        }

        // Канвас мог остаться вне дерева после предыдущего Unloaded — возвращаем его
        // в Grid шаблона, иначе Win2D не создаст device и не нарисует ни строки.
        EnsureCanvasInTemplateRoot();

        // Канвас прозрачен: очистка полностью прозрачным цветом (альфа = 0).
        canvas.ClearColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

        canvas.CreateResources -= OnCreateResources;
        canvas.CreateResources += OnCreateResources;
        canvas.Draw -= OnDraw;
        canvas.Draw += OnDraw;

        StartRenderLoop();

        _untimedBadge = GetTemplateChild(PART_UntimedBadge) as FrameworkElement;
        UpdateUntimedBadge();

        _scrollViewer = GetTemplateChild(PART_ScrollViewer) as ScrollViewer;
        if (_scrollViewer != null)
        {
            _scrollViewer.PointerEntered -= OnScrollViewerPointerEntered;
            _scrollViewer.PointerEntered += OnScrollViewerPointerEntered;

            _scrollViewer.PointerExited -= OnScrollViewerPointerExited;
            _scrollViewer.PointerExited += OnScrollViewerPointerExited;
            
            _scrollViewer.PointerMoved -= OnScrollViewerPointerMoved;
            _scrollViewer.PointerMoved += OnScrollViewerPointerMoved;
            
            _scrollViewer.PointerPressed -= OnScrollViewerPointerPressed;
            _scrollViewer.PointerPressed += OnScrollViewerPointerPressed;
            
            _scrollViewer.PointerReleased -= OnScrollViewerPointerReleased;
            _scrollViewer.PointerReleased += OnScrollViewerPointerReleased;
            
            _scrollViewer.PointerWheelChanged -= OnScrollViewerPointerWheelChanged;
            _scrollViewer.PointerWheelChanged += OnScrollViewerPointerWheelChanged;
        }
        else
        {
            BetterLyricDebugLog.WriteAlways(BetterLyricLogCategory.Lifecycle, "OnApplyTemplate: PART_ScrollViewer не найден — мышь и колесо не работают");
        }

        SizeChanged -= OnSizeChanged;
        SizeChanged += OnSizeChanged;

        Unloaded -= OnUnloaded;
        Unloaded += OnUnloaded;

        // Признак «контрол в дереве»: после Unloaded canvas уничтожается, и до повторного
        // Loaded обращаться к нему нельзя (иначе AccessViolation в нативном Win2D).
        _isAttachedToTree = true;

        // Контрол может быть перенесён в другое окно (репарент в полноэкранный плеер).
        // При этом приходит Unloaded (canvas освобождается), а затем повторный Loaded —
        // OnApplyTemplate больше не вызывается, поэтому canvas нужно переподключить здесь.
        Loaded -= OnLoaded;
        Loaded += OnLoaded;

        // Повторное применение шаблона (перезагрузка стиля) требует новой раскладки:
        // старые CanvasTextLayout привязаны к прежнему CanvasControl.
        // Настройки, применённые до появления Canvas, тоже попадают сюда — их запрос
        // откладывался (см. RequestRelayout) и сбрасывается только сейчас.
        _isContentDirty = true;
        _layoutSignature = null;
        ConsumePendingRelayoutBeforeTemplate();
        RequestRelayout();

        BetterLyricDebugLog.WriteAlways(
            BetterLyricLogCategory.Lifecycle,
            $"OnApplyTemplate: canvas={canvas.Size.Width}x{canvas.Size.Height}, scrollViewer={( _scrollViewer != null ? "ok" : "null")}");
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        BetterLyricDebugLog.Write(
            BetterLyricLogCategory.Layout,
            $"OnSizeChanged: {e.PreviousSize.Width:0.#}x{e.PreviousSize.Height:0.#} -> {e.NewSize.Width:0.#}x{e.NewSize.Height:0.#}, autoLayout={isAutoLayoutEnabled}");

        if (isAutoLayoutEnabled)
        {
            renderLyricsStartX = 0;
            renderLyricsStartY = 0;
            renderLyricsWidth = ActualWidth;
            renderLyricsHeight = ActualHeight;

            if (lyrics3DAutoFitLayout)
            {
                (renderLyricsHeight, renderLyricsWidth) = (renderLyricsWidth, renderLyricsHeight);
                renderLyricsStartX += (renderLyricsHeight - renderLyricsWidth) / 2;
                renderLyricsStartY += (renderLyricsWidth - renderLyricsHeight) / 2;
            }

            RequestRelayout();
        }
    }

    private void OnScrollViewerPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (isScrollViewerEnabled)
        {
            isMouseInLyricsArea = true;
        }
    }

    private void OnScrollViewerPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (isScrollViewerEnabled)
        {
            isMouseInLyricsArea = false;
        }
    }

    private void OnScrollViewerPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (isScrollViewerEnabled)
        {
            mousePosition = e.GetCurrentPoint(_scrollViewer).Position;
        }
    } 

    private void OnScrollViewerPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (isScrollViewerEnabled)
        {
            isMousePressing = true;
        }
    }

    private void OnScrollViewerPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!isScrollViewerEnabled) return;

        isMousePressing = false;

        // Снимок состояния на момент клика. Всё читается ОДИН раз и в локальные
        // переменные: OnUpdate пересчитывает эти поля в потоке игрового цикла
        // параллельно с обработчиком, а SnapToLine ниже меняет скролл.
        var linesSnapshot = _renderLyricsLines;
        double scrollSnapshot = _canvasYScrollTransition.Value + _mouseYScrollTransition.Value;
        var clickPoint = e.GetCurrentPoint(_scrollViewer).Position;

        // Индекс считаем по точке отпускания и текущему суммарному скроллу,
        // а не берём _mouseHoverLineIndex с прошлого кадра: между кадром и кликом
        // колесо могло сдвинуть полотно, и индекс соответствовал бы другому смещению.
        int clickedLineIndex = FindMouseHoverLineIndex(
            linesSnapshot,
            true,
            clickPoint,
            scrollSnapshot,
            renderLyricsHeight,
            playingLineTopOffset / 100.0);

        if (clickedLineIndex < 0)
        {
            BetterLyricDebugLog.WriteAlways(
                BetterLyricLogCategory.Scroll,
                $"КЛИК: строка под курсором не определена (точка={clickPoint.X:0.#},{clickPoint.Y:0.#}, " +
                $"scroll={scrollSnapshot:0.#}) — перемотки нет");
            return;
        }

        var clickedLine = linesSnapshot != null && clickedLineIndex < linesSnapshot.Count
            ? linesSnapshot[clickedLineIndex]
            : null;

        BetterLyricDebugLog.WriteAlways(
            BetterLyricLogCategory.Scroll,
            $"КЛИК: строка {clickedLineIndex} '{Truncate(clickedLine?.PrimaryText, 45)}' | " +
            $"её начало={clickedLine?.StartMs} мс ({FormatTime(TimeSpan.FromMilliseconds(clickedLine?.StartMs ?? 0))}) | " +
            $"\n    ДО: текущая строка={_primaryPlayingLineIndex}, pos={_songPositionWithOffset.TotalMilliseconds:0} мс ({FormatTime(_songPositionWithOffset)}), " +
            $"canvas={_canvasYScrollTransition.Value:0.#}, mouse={_mouseYScrollTransition.Value:0.#}, " +
            $"сумма={scrollSnapshot:0.#} (снимок на клик), точка={clickPoint.X:0.#},{clickPoint.Y:0.#}, " +
            $"scrolling={isMouseScrolling}, snapPending={_pendingSnapLineIndex}");

        // Клик по строке: колесо игнорируется до завершения перемотки,
        // иначе пользовательский скролл перебьёт примагничивание.
        isScrollLockedBySnap = true;

        // Статичный текст: перематывать некуда, но подсветка/выбор строки кликом
        // допустимы. Примагничивание к строке включаем только для синхронизированного
        // текста — иначе полотно прыгало бы к «условной» строке.
        if (!IsUntimedNow)
        {
            // Примагничиваемся строго к зафиксированной строке.
            SnapToLine(clickedLineIndex);
        }
        else
        {
            isScrollLockedBySnap = false;
        }

        LineClicked?.Invoke(this, clickedLineIndex);

        BetterLyricDebugLog.WriteAlways(
            BetterLyricLogCategory.Scroll,
            $"КЛИК: после снапа — текущая строка={_primaryPlayingLineIndex}, " +
            $"scroll={_canvasYScrollTransition.Value:0.#} -> {_canvasTargetScrollOffset:0.#} (анимация), " +
            $"mouseScroll={_mouseYScrollTransition.Value:0.#} -> 0, scrollLocked={isScrollLockedBySnap}");
    }

    /// <summary>
    /// Примагничивает текст к указанной строке тем же механизмом, что используется
    /// при автоматическом возврате к текущей строке песни.
    /// </summary>
    private void SnapToLine(int lineIndex)
    {
        // Локальная ссылка: TriggerRelayout может подменить _renderLyricsLines
        // в потоке игрового цикла, и тогда line и lines разъедутся.
        var lines = _renderLyricsLines;

        if (lines == null || lineIndex < 0 || lineIndex >= lines.Count)
        {
            BetterLyricDebugLog.Write(
                BetterLyricLogCategory.Scroll,
                $"SnapToLine: пропуск, index={lineIndex}, lines={lines?.Count.ToString() ?? "null"}");
            return;
        }

        var line = lines[lineIndex];

        // Отменяем отложенный возврат к песне после прокрутки колесом.
        _scrollWheelDebounceTimer.Cancel();

        // Остаток пользовательского скролла. Его нельзя сбрасывать через JumpTo(0):
        // если пользователь проскроллил от активной строки, полотно сначала дёрнется
        // на эту величину, и только потом поедет к выбранной строке.
        // Вместо этого «съедаем» остаток анимацией вместе с основным переходом.
        double wheelOffset = _mouseYScrollTransition.Value;
        bool hadWheelOffset = Math.Abs(wheelOffset) > 0.5;

        isMouseScrolling = false;
        isReturningFromScroll = false;

        // ВАЖНО: не выставляем isMouseScrollingChanged. Этот флаг заставляет аниматор
        // перезапустить YOffsetTransition у всех видимых строк с нулевым делэем —
        // то есть второе движение поверх уже запущенного ниже. Один переход на клик.
        isMouseScrollingChanged = false;

        // Ключевой момент: локальная позиция синхронизируется с началом выбранной строки
        // СРАЗУ, до фактической перемотки плеера. Иначе следующий кадр OnUpdate получит
        // из синхронизатора старый индекс и перезапустит скролл назад к прежней строке.
        _songPosition = TimeSpan.FromMilliseconds(line.StartMs);
        _songPositionWithOffset = _songPosition + TimeSpan.FromMilliseconds(positionOffset);

        _synchronizer.Reset();
        _primaryPlayingLineIndex = lineIndex;

        // Пользователь выбрал строку — она считается активной сразу,
        // режим «полотно едет, но не подсвечиваем» снимаем.
        _isPlayingLinePending = false;

        // Блокируем автоподстройку индекса, пока плеер не подтвердит перемотку.
        // Ограничение по времени защищает от «залипания», если плеер не перемотается
        // (например, трек закончился или перемотка не поддерживается).
        _pendingSnapLineIndex = lineIndex;
        _snapConfirmedByPlayer = false;

        // Страховка только от «вечного» ожидания: 3 секунды с запасом покрывают
        // seek в плеере и длительность анимации скролла.
        _pendingSnapDeadlineMs = _songPositionWithOffset.TotalMilliseconds + Math.Max(lyricsScrollDuration, 300) + 3000;

        var targetScroll = CalculateTargetScrollOffset(lines, lineIndex);
        if (targetScroll.HasValue)
        {
            // ВАЖНО: без компенсации wheelOffset.
            // Экранное положение строки = canvas-скролл + UserScrollOffset (mouse-скролл).
            // Раньше здесь вычитался wheelOffset, чтобы «учесть остаток» — но остаток
            // в это же время гасится к нулю отдельной анимацией. В сумме получалось
            // (targetScroll - wheelOffset) + 0 != targetScroll, то есть строка вставала
            // не в якорь, а на величину колеса ниже. Теперь обе цели согласованы:
            // canvas → targetScroll, mouse → 0, сумма сходится ровно к targetScroll.
            _canvasTargetScrollOffset = targetScroll.Value;

            _canvasYScrollTransition.SetDurationMs(lyricsScrollDuration);
            _canvasYScrollTransition.SetInterpolator(AnimationEasingHelper.GetInterpolatorByEasingType<double>(lyricsScrollEasingType, lyricsScrollEasingMode));
            _canvasYScrollTransition.Start(_canvasTargetScrollOffset);
        }

        // Остаток колеса гасим той же анимацией (одинаковые длительность и кривая),
        // чтобы сумма canvas + mouse шла к цели монотонно, без рывка.
        if (hadWheelOffset)
        {
            _mouseYScrollTransition.SetDurationMs(lyricsScrollDuration);
            _mouseYScrollTransition.SetInterpolator(AnimationEasingHelper.GetInterpolatorByEasingType<double>(lyricsScrollEasingType, lyricsScrollEasingMode));
            _mouseYScrollTransition.Start(0);
        }
        else
        {
            _mouseYScrollTransition.JumpTo(0);
        }

        _isSnapToLineRequested = true;

        // Остаток колеса должен догаснуть к нулю, canvas — дойти до targetScroll.
        // Сумма этих двух переходов в конце даёт ровно targetScroll.
        double previousLineCenterY = _primaryPlayingLineIndex >= 0 && _primaryPlayingLineIndex < lines.Count
            ? lines[_primaryPlayingLineIndex].CenterPosition.Y
            : double.NaN;

        BetterLyricDebugLog.WriteAlways(
            BetterLyricLogCategory.Scroll,
            $"SnapToLine: index={lineIndex}, startMs={line.StartMs}, centerY={line.CenterPosition.Y:0.##}, " +
            $"targetScroll={_canvasTargetScrollOffset:0.##}, hasTarget={targetScroll.HasValue}, " +
            $"fromCanvas={_canvasYScrollTransition.Value:0.##}, fromMouse={wheelOffset:0.##} " +
            $"({(hadWheelOffset ? "гасим анимацией" : "нет")}), " +
            $"сумма на старте={_canvasYScrollTransition.Value + wheelOffset:0.##}, " +
            $"pendingUntil={_pendingSnapDeadlineMs:0} мс | " +
            $"сдвиг от прежней строки {_primaryPlayingLineIndex} (centerY={previousLineCenterY:0.##}) = " +
            $"{Math.Abs(line.CenterPosition.Y - previousLineCenterY):0.##} px");
    }

    private void OnScrollViewerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        // Пока идёт примагничивание после клика, свободный скролл выключен.
        if (isScrollLockedBySnap)
        {
            BetterLyricDebugLog.Write(
                BetterLyricLogCategory.Scroll,
                "Wheel: проигнорирован — идёт примагничивание к выбранной строке");
            return;
        }

        if (isScrollViewerEnabled)
        {
            isMouseScrolling = true;
            isMouseScrollingChanged = true;

            var delta = e.GetCurrentPoint(_scrollViewer).Properties.MouseWheelDelta;
            var value = _mouseYScrollTransition.TargetValue + delta;
            var actualHeight = CalculateActualHeight(_renderLyricsLines);
            var minValue = -_canvasYScrollTransition.Value - actualHeight;
            var maxValue = -_canvasYScrollTransition.Value;
            value = Math.Clamp(value, minValue, maxValue);
            _mouseYScrollTransition.Start(value);

            // Какая строка окажется в якоре при текущем офсете — чтобы понимать,
            // куда пользователь уводит текст.
            int wheelPreviewLine = FindMouseHoverLineIndex(
                _renderLyricsLines,
                true,
                new Point(renderLyricsWidth / 2, renderLyricsStartY + renderLyricsHeight * playingLineTopOffset / 100.0),
                _canvasYScrollTransition.Value + value,
                renderLyricsHeight,
                playingLineTopOffset / 100.0);

            var previewText = wheelPreviewLine >= 0 && _renderLyricsLines != null && wheelPreviewLine < _renderLyricsLines.Count
                ? _renderLyricsLines[wheelPreviewLine].PrimaryText
                : null;

            BetterLyricDebugLog.WriteAlways(
                BetterLyricLogCategory.Scroll,
                $"КОЛЕСО: delta={delta}, офсет={_mouseYScrollTransition.Value:0.#} -> {value:0.#}, " +
                $"границы=[{minValue:0.#};{maxValue:0.#}], высота текста={actualHeight:0.#} | " +
                $"песня: строка={_primaryPlayingLineIndex}, pos={_songPositionWithOffset.TotalMilliseconds:0} мс ({FormatTime(_songPositionWithOffset)}) | " +
                $"в якоре будет строка={wheelPreviewLine} '{Truncate(previewText, 35)}'");

            // Текст без таймингов: автоматически возвращать полотно некуда —
            // пользователь листает его свободно, офсет остаётся на месте.
            if (IsUntimedNow)
                return;

           _ = _scrollWheelDebounceTimer.RunAsync(() =>
            {
                BetterLyricDebugLog.WriteAlways(
                    BetterLyricLogCategory.Scroll,
                    $"КОЛЕСО: пауза 3 с — возврат к песне. Офсет={_mouseYScrollTransition.Value:0.#} -> 0, " +
                    $"песня: строка={_primaryPlayingLineIndex}, pos={_songPositionWithOffset.TotalMilliseconds:0} мс ({FormatTime(_songPositionWithOffset)}), " +
                    $"scroll={_canvasYScrollTransition.Value:0.#}");

                _mouseYScrollTransition.Start(0);
                isMouseScrolling = false;

                // Пользовательский офсет гасится, но активная строка не менялась —
                // нужно заново привязать полотно к текущей строке песни.
                isReturningFromScroll = true;
                isMouseScrollingChanged = true;
            }, (int?)TimeSpan.FromSeconds(3).TotalMilliseconds);
        }
    }

    /// <summary>
    /// Повторная инициализация после репарентa (перенос в другое окно): при Unloaded
    /// canvas освобождается, а шаблон заново не применяется — переподключаем ресурсы здесь.
    /// </summary>
    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _isAttachedToTree = true;

        // Шаблон мог ещё не примениться — берём корень и канвас из него.
        _templateRoot ??= GetTemplateChild(PART_Root) as Panel;

        // ВАЖНО: раньше здесь был досрочный выход при canvas != null. Это ломало
        // возврат на вкладку: OnUnloaded делает RemoveFromVisualTree(), но поле
        // canvas не всегда обнуляется до повторного Loaded, а если канвас остался
        // вне дерева — Win2D не создаёт device и не рисует ни строки.
        // Поэтому сначала гарантируем, что канвас снова в шаблоне.
        canvas ??= GetTemplateChild(PART_Canvas) as CanvasControl;
        if (canvas == null)
        {
            BetterLyricDebugLog.WriteAlways(BetterLyricLogCategory.Lifecycle, "OnLoaded: PART_Canvas не найден!");
            return;
        }

        EnsureCanvasInTemplateRoot();

        BetterLyricDebugLog.WriteAlways(BetterLyricLogCategory.Lifecycle, "OnLoaded: переподключение канваса после загрузки");

        canvas.ClearColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

        canvas.CreateResources -= OnCreateResources;
        canvas.CreateResources += OnCreateResources;
        canvas.Draw -= OnDraw;
        canvas.Draw += OnDraw;

        StartRenderLoop();

        _untimedBadge = GetTemplateChild(PART_UntimedBadge) as FrameworkElement;
        UpdateUntimedBadge();

        _scrollViewer = GetTemplateChild(PART_ScrollViewer) as ScrollViewer;
        if (_scrollViewer != null)
        {
            _scrollViewer.PointerEntered -= OnScrollViewerPointerEntered;
            _scrollViewer.PointerEntered += OnScrollViewerPointerEntered;

            _scrollViewer.PointerExited -= OnScrollViewerPointerExited;
            _scrollViewer.PointerExited += OnScrollViewerPointerExited;

            _scrollViewer.PointerMoved -= OnScrollViewerPointerMoved;
            _scrollViewer.PointerMoved += OnScrollViewerPointerMoved;

            _scrollViewer.PointerPressed -= OnScrollViewerPointerPressed;
            _scrollViewer.PointerPressed += OnScrollViewerPointerPressed;

            _scrollViewer.PointerReleased -= OnScrollViewerPointerReleased;
            _scrollViewer.PointerReleased += OnScrollViewerPointerReleased;

            _scrollViewer.PointerWheelChanged -= OnScrollViewerPointerWheelChanged;
            _scrollViewer.PointerWheelChanged += OnScrollViewerPointerWheelChanged;
        }

        // Новый device/дерево — строки и раскладку нужно построить заново.
        _isContentDirty = true;
        _layoutSignature = null;
        _isLayoutChanged = false;
        ConsumePendingRelayoutBeforeTemplate();
        RequestRelayout();

        // Раскладку строим на следующем тике: при репарентe размеры ещё нулевые
        // в момент Loaded, а к следующему тику контрол уже измерен.
        DispatcherQueue.TryEnqueue(TriggerRelayout);

        // После возврата в дерево CanvasControl создаёт device не сразу: первый Draw
        // приходит только при Invalidate(). Просим кадр, иначе поверхность остаётся пустой.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!_isAttachedToTree || canvas == null)
                return;

            TriggerRelayout();
            canvas.Invalidate();
        });
    }

    /// <summary>
    /// Возвращает <c>CanvasControl</c> в Grid шаблона, если он был отвязан
    /// (RemoveFromVisualTree в <see cref="OnUnloaded"/>). Без этого Win2D не создаёт
    /// device, и после повторного Loaded текст не рисуется вовсе.
    /// </summary>
    private void EnsureCanvasInTemplateRoot()
    {
        if (canvas == null)
            return;

        // Корень шаблона мог быть пересоздан — перечитываем при необходимости.
        if (_templateRoot == null || !ReferenceEquals(_templateRoot.Parent, this))
            _templateRoot = GetTemplateChild(PART_Root) as Panel;

        if (_templateRoot == null)
            return;

        if (ReferenceEquals(canvas.Parent, _templateRoot))
            return;

        // Элемент мог остаться у прежнего родителя (например, у уничтоженного Grid).
        if (canvas.Parent is Panel oldPanel)
        {
            try
            {
                oldPanel.Children.Remove(canvas);
            }
            catch (System.Exception ex)
            {
                BetterLyricDebugLog.WriteAlways(
                    BetterLyricLogCategory.Lifecycle,
                    $"EnsureCanvasInTemplateRoot: не удалось отвязать канвас ({ex.GetType().Name})");
            }
        }

        try
        {
            // Канвас должен быть под ScrollViewer'ом и плашкой — вставляем первым.
            _templateRoot.Children.Insert(0, canvas);
            BetterLyricDebugLog.WriteAlways(BetterLyricLogCategory.Lifecycle, "EnsureCanvasInTemplateRoot: канвас возвращён в дерево");
        }
        catch (System.Exception ex)
        {
            BetterLyricDebugLog.WriteAlways(
                BetterLyricLogCategory.Lifecycle,
                $"EnsureCanvasInTemplateRoot: не удалось вернуть канвас ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private void OnUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        BetterLyricDebugLog.WriteAlways(BetterLyricLogCategory.Lifecycle, "OnUnloaded: отписка и освобождение ресурсов");

        // Контрол выходит из дерева: canvas будет уничтожен ниже.
        _isAttachedToTree = false;

        StopRenderLoop();
        _isCanvasDeviceReady = false;

        // Null-safe: OnUnloaded может прийти повторно или после неудачного OnApplyTemplate.
        if (canvas != null)
        {
            canvas.Draw -= OnDraw;
            canvas.CreateResources -= OnCreateResources;

            canvas.RemoveFromVisualTree();
            canvas = null;
        }

        if (_scrollViewer != null)
        {
            _scrollViewer.PointerEntered -= OnScrollViewerPointerEntered;
            _scrollViewer.PointerExited -= OnScrollViewerPointerExited;
            _scrollViewer.PointerMoved -= OnScrollViewerPointerMoved;
            _scrollViewer.PointerPressed -= OnScrollViewerPointerPressed;
            _scrollViewer.PointerReleased -= OnScrollViewerPointerReleased;
            _scrollViewer.PointerWheelChanged -= OnScrollViewerPointerWheelChanged;
            _scrollViewer = null;
        }

        SizeChanged -= OnSizeChanged;
        Unloaded -= OnUnloaded;

        _untimedBadge = null;

        _lyricsRenderer.Dispose();

        // Сбрасываем всё, что привязано к уничтоженному CanvasControl,
        // иначе после повторной загрузки строки будут ссылаться на мёртвый device.
        DisposeRenderLyricsLines();
        _renderLyricsData = null;
        _contentSignature = null;
        _layoutSignature = null;
        _isContentDirty = true;
        _isLayoutChanged = false;
        _isSnapToLineRequested = false;
        _pendingSnapLineIndex = -1;
        _snapConfirmedByPlayer = false;
        isScrollLockedBySnap = false;
        isReturningFromScroll = false;
        _isPlayingLinePending = false;
        _visibleRange = (-1, -1);
        _lastDrawnLineCount = -1;
        _isEmptyFrameReported = false;

        _scrollWheelDebounceTimer.Dispose();
        _layoutDebouncer.Dispose();
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;

        // Страховка: Draw гарантированно идёт после создания device.
        _isCanvasDeviceReady = true;

        // CanvasControl поддерживает альфа-канал: очищаем прозрачным цветом,
        // и сквозь канвас виден фон контрола/окна (обложка, акрил, что угодно).
        // CanvasBackgroundColor остаётся опциональным: если он непрозрачный —
        // заливаем им (например, для непрозрачной панели), иначе — прозрачно.
        // Читаем кэш поля, а не DP (безопасно при любом потоке отрисовки).
        var bg = canvasBackgroundColorCache;
        ds.Clear(bg.A > 0 ? bg : Windows.UI.Color.FromArgb(0, 0, 0, 0));

        DrawCoreWithEdgeFeatheringHandled(sender, ds);
    }
    private void DrawCoreWithEdgeFeatheringHandled(ICanvasResourceCreator sender, CanvasDrawingSession ds)
    {
        DrawCore(sender, ds);
    }
    private void DrawCore(ICanvasResourceCreator sender, CanvasDrawingSession ds)
    {

        if (isLyricsVisible)
        {
            // Статичный текст: ни одна строка не считается «играющей» —
            // подсветка, масштаб и посимвольные эффекты не включаются.
            if (IsUntimedNow)
            {
                _lyricsRenderer.PendingLineIndex = -1;
                if (_renderLyricsLines != null)
                {
                    foreach (var line in _renderLyricsLines)
                        line.IsPlayingLastFrame = false;
                }
            }

            int drawn = _lyricsRenderer.Draw(sender, ds);

            // Диагностика «пустого кадра»: текст видим, но на экран не попало ни строки.
            if (BetterLyricDebugLog.IsEnabled(BetterLyricLogCategory.Draw))
            {
                if (drawn == 0 && !_isEmptyFrameReported)
                {
                    _isEmptyFrameReported = true;
                    BetterLyricDebugLog.WriteAlways(
                        BetterLyricLogCategory.Draw,
                        $"Пустой кадр! lines={_renderLyricsLines?.Count.ToString() ?? "null"}, " +
                        $"range={_visibleRange.Start}..{_visibleRange.End}, " +
                        $"yScroll={_canvasYScrollTransition.Value:0.##}, userScroll={_mouseYScrollTransition.Value:0.##}, " +
                        $"layout={renderLyricsWidth:0.#}x{renderLyricsHeight:0.#}@{renderLyricsStartX:0.#},{renderLyricsStartY:0.#}, " +
                        $"canvas={ActualWidth:0.#}x{ActualHeight:0.#}");
                }
                else if (drawn > 0)
                {
                    _isEmptyFrameReported = false;
                }

                if (drawn != _lastDrawnLineCount)
                {
                    _lastDrawnLineCount = drawn;
                    BetterLyricDebugLog.WriteThrottled(
                        BetterLyricLogCategory.Draw,
                        $"Отрисовано строк: {drawn} (диапазон {_visibleRange.Start}..{_visibleRange.End})");
                }
            }
        }
    }

    private void OnCreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
    {
        BetterLyricDebugLog.WriteAlways(
            BetterLyricLogCategory.Lifecycle,
            $"OnCreateResources: reason={args.Reason}, device={sender.Device?.ToString() ?? "null"}");

        // CanvasDevice создан — с этого момента кадры можно обновлять (см. RenderLoop).
        _isCanvasDeviceReady = true;

        // Пересоздание device может сбросить ClearColor — задаём прозрачную очистку заново,
        // чтобы канвас не заливал фон чёрным (критично в прозрачном окне плеера).
        sender.ClearColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

        // Новый device — все CanvasTextLayout/кэши невалидны, строки нужно построить заново.
        _isContentDirty = true;
        _layoutSignature = null;
        ConsumePendingRelayoutBeforeTemplate();

        RequestRelayout();
        TriggerRelayout();
    }

    /// <summary>
    /// Логика кадра: раньше вызывалась из <c>CanvasAnimatedControl.Update</c>,
    /// теперь — из цикла CompositionTarget.Rendering (см. BetterLyric.RenderLoop.cs).
    /// </summary>
    private void UpdateFrame(TimeSpan elapsedTime)
    {
        var lyricsData = currentLyricsData;

        BetterLyricDebugLog.NextFrame();

        // Статичный текст: кадровый цикл продолжает работать (чтобы при переходе
        // на песню с таймингами слежение включилось само), но позиция воспроизведения
        // не учитывается — полотно стоит, подсветки нет.
        if (IsUntimedNow)
        {
            UpdateUntimedFrame(elapsedTime);
            return;
        }

        _immersiveBgOpacityTransition.Update(elapsedTime);
        _immersiveBgColorTransition.Update(elapsedTime);

        UpdatePlaybackState(elapsedTime);

        TriggerRelayout();

        #region UpdatePlayingLineIndex

        var lineHit = _synchronizer.GetCurrentLine(_songPositionWithOffset.TotalMilliseconds, _renderLyricsLines);
        int primaryPlayingIndex = lineHit.Index;

        // Строка ещё не начала звучать (время в разрыве): полотно ведём к ней,
        // но подсветку/масштаб не включаем.
        bool isPlayingLinePending = !lineHit.IsActive;
        bool isPlayingLinePendingChanged = isPlayingLinePending != _isPlayingLinePending;
        _isPlayingLinePending = isPlayingLinePending;

        bool isPrimaryPlayingLineChanged = primaryPlayingIndex != _primaryPlayingLineIndex;

        // Ожидание перемотки после клика по строке: держим выбранную строку активной,
        // пока плеер не догонит её начало. Снимаем блокировку при совпадении индекса
        // или по истечении дедлайна (страховка от «залипания», если плеер не перемотался).
        if (_pendingSnapLineIndex >= 0)
        {
            // Снимаем ожидание ТОЛЬКО когда плеер реально перемотался (_snapConfirmedByPlayer
            // выставляется в UpdateCurrentPosition) либо истёк дедлайн-страховка.
            // Совпадение индекса синхронизатора подтверждением не считается: SnapToLine
            // сам подставил _songPosition = line.StartMs, поэтому синхронизатор соглашается
            // мгновенно, а плеер ещё отдаёт старую позицию. Раньше флаг снимался здесь,
            // старая позиция принималась, строка откатывалась назад — и полотно прыгало.
            bool isSnapResolved = _snapConfirmedByPlayer
                || _songPositionWithOffset.TotalMilliseconds >= _pendingSnapDeadlineMs;

            if (isSnapResolved)
            {
                bool isResolvedByTimeout = !_snapConfirmedByPlayer;

                BetterLyricDebugLog.WriteAlways(
                    BetterLyricLogCategory.Scroll,
                    $"СНАП: ожидание завершено ({(isResolvedByTimeout ? "по таймауту" : "плеер подтвердил перемотку")}) — " +
                    $"было target={_pendingSnapLineIndex}, синхронизатор вернул={primaryPlayingIndex}, " +
                    $"pos={_songPositionWithOffset.TotalMilliseconds:0} мс ({FormatTime(_songPositionWithOffset)}), " +
                    $"deadline={_pendingSnapDeadlineMs:0} мс");

                _pendingSnapLineIndex = -1;
                _snapConfirmedByPlayer = false;
                _primaryPlayingLineIndex = primaryPlayingIndex;

                // Перемотка подтверждена — возвращаем свободный скролл.
                isScrollLockedBySnap = false;
            }
            else
            {
                // Плеер ещё не перемотан — не даём синхронизатору вернуть старую строку,
                // иначе блок ниже перезапустит скролл назад.
                _primaryPlayingLineIndex = _pendingSnapLineIndex;
                isPrimaryPlayingLineChanged = false;

                BetterLyricDebugLog.WriteThrottled(
                    BetterLyricLogCategory.Scroll,
                    $"СНАП: ждём подтверждения от плеера — target={_pendingSnapLineIndex}, " +
                    $"синхронизатор вернул={primaryPlayingIndex}, " +
                    $"pos={_songPositionWithOffset.TotalMilliseconds:0} мс ({FormatTime(_songPositionWithOffset)}), " +
                    $"scrollLocked={isScrollLockedBySnap}");
            }
        }

        // Пока пользователь листает строки, текст отмагничен: не пересчитываем привязку
        // к текущей строке песни, иначе она перебивает пользовательский скролл.
        if (isMouseScrolling)
        {
            _synchronizer.Reset();
            isPrimaryPlayingLineChanged = false;
        }
        else if (_pendingSnapLineIndex < 0)
        {
            _primaryPlayingLineIndex = primaryPlayingIndex;
        }

        if (isPlayingLinePendingChanged)
        {
            var pendingLine = _renderLyricsLines != null && primaryPlayingIndex >= 0 && primaryPlayingIndex < _renderLyricsLines.Count
                ? _renderLyricsLines[primaryPlayingIndex]
                : null;

            BetterLyricDebugLog.WriteAlways(
                BetterLyricLogCategory.Scroll,
                (isPlayingLinePending
                    ? $"ПАУЗА: строка {primaryPlayingIndex} ещё не началась (старт={pendingLine?.StartMs} мс, " +
                      $"pos={_songPositionWithOffset.TotalMilliseconds:0} мс) — полотно едет к ней без подсветки"
                    : $"ПАУЗА: закончилась — строка {primaryPlayingIndex} зазвучала " +
                      $"(pos={_songPositionWithOffset.TotalMilliseconds:0} мс)"));
        }

        if (isPrimaryPlayingLineChanged)
        {
            var newLine = _renderLyricsLines != null && primaryPlayingIndex >= 0 && primaryPlayingIndex < _renderLyricsLines.Count
                ? _renderLyricsLines[primaryPlayingIndex]
                : null;

            BetterLyricDebugLog.WriteAlways(
                BetterLyricLogCategory.Scroll,
                $"СТРОКА: {_primaryPlayingLineIndex} -> {primaryPlayingIndex} | " +
                $"pos={_songPositionWithOffset.TotalMilliseconds:0} мс ({FormatTime(_songPositionWithOffset)}) | " +
                $"текст='{Truncate(newLine?.PrimaryText, 45)}' | " +
                $"интервал={(newLine != null ? $"{newLine.StartMs}-{newLine.EndMs} мс" : "нет")} | " +
                $"scroll={_canvasYScrollTransition.Value:0.#} | mouseScroll={_mouseYScrollTransition.Value:0.#} | " +
                $"layoutChanged={_isLayoutChanged} | snapPending={_pendingSnapLineIndex} | pending={isPlayingLinePending}");
        }

        #endregion

        #region UpdateTargetScrollOffset

        // Пока пользователь листает строки, штатное прилипание к текущей строке песни
        // временно отключено — иначе оно перебивает пользовательский скролл.
        // Ожидание перемотки (_pendingSnapLineIndex) тоже исключает этот блок:
        // переход к выбранной строке уже запущен в SnapToLine, повторный старт
        // сбивал бы анимацию (визуально — рывок).
        // _snapConfirmedByPlayer тоже исключает блок: в кадре подтверждения цель уже
        // выставлена в SnapToLine, повторный Start сбил бы идущую анимацию.
        bool isSnapPending = _pendingSnapLineIndex >= 0 || _snapConfirmedByPlayer;

        // Возврат после ручной прокрутки: пользовательский офсет гаснет к нулю,
        // но активная строка при этом не менялась. Раньше этот случай не обрабатывался,
        // и полотно оставалось смещённым — «примагничивание работает криво после скролла».
        bool isReturningFromUserScroll = isReturningFromScroll;

        if (((isPrimaryPlayingLineChanged && !isMouseScrolling) || _isLayoutChanged || isReturningFromUserScroll) && !isSnapPending)
        {
            var targetScroll = CalculateTargetScrollOffset(_renderLyricsLines, _primaryPlayingLineIndex);
            if (targetScroll.HasValue) _canvasTargetScrollOffset = targetScroll.Value;

            // Цель не изменилась — анимация не нужна. Запуск 500-мс перехода «в то же место»
            // вреден: видимый диапазон считается по анимируемому значению и расходится
            // с реальным положением строк, из-за чего текст визуально «уезжает».
            bool isTargetUnchanged = Math.Abs(_canvasYScrollTransition.TargetValue - _canvasTargetScrollOffset) < 0.5
                && Math.Abs(_canvasYScrollTransition.Value - _canvasTargetScrollOffset) < 0.5;

            if (_isLayoutChanged || isTargetUnchanged)
            {
                _canvasYScrollTransition.JumpTo(_canvasTargetScrollOffset);

                BetterLyricDebugLog.Write(
                    BetterLyricLogCategory.Scroll,
                    $"Скролл JumpTo ({(isTargetUnchanged ? "цель не изменилась" : "layout changed")}): " +
                    $"target={_canvasTargetScrollOffset:0.##}");
            }
            else
            {
                _canvasYScrollTransition.SetDurationMs(lyricsScrollDuration);
                _canvasYScrollTransition.SetInterpolator(AnimationEasingHelper.GetInterpolatorByEasingType<double>(lyricsScrollEasingType, lyricsScrollEasingMode));
                _canvasYScrollTransition.Start(_canvasTargetScrollOffset);

                BetterLyricDebugLog.Write(
                    BetterLyricLogCategory.Scroll,
                    $"Скролл Start ({(isReturningFromUserScroll ? "возврат после колеса" : "смена строки")}): " +
                    $"{_canvasYScrollTransition.Value:0.##} -> {_canvasTargetScrollOffset:0.##}, dur={lyricsScrollDuration} мс");
            }
        }

        // Примагничивание к выбранной строке уже запустило _canvasYScrollTransition в SnapToLine,
        // здесь только сообщаем аниматору, что офсеты строк нужно пересчитать.
        bool isSnapToLineRequested = _isSnapToLineRequested;
        _isSnapToLineRequested = false;
        _canvasYScrollTransition.Update(elapsedTime);

        #endregion

        _mouseYScrollTransition.Update(elapsedTime);

        _mouseHoverLineIndex = FindMouseHoverLineIndex(
            _renderLyricsLines,
            isMouseInLyricsArea,
            mousePosition,
            _canvasYScrollTransition.Value + _mouseYScrollTransition.Value,
            renderLyricsHeight,
            playingLineTopOffset / 100.0
        );

        var previousVisibleRange = _visibleRange;

        _visibleRange = CalculateVisibleRange(
            _renderLyricsLines,
            _canvasYScrollTransition.Value + _mouseYScrollTransition.Value,
            renderLyricsStartY,
            renderLyricsHeight,
            ActualHeight,
            playingLineTopOffset / 100.0
        );

        if (previousVisibleRange != _visibleRange)
        {
            BetterLyricDebugLog.Write(
                BetterLyricLogCategory.Draw,
                $"Видимый диапазон: {previousVisibleRange.Start}..{previousVisibleRange.End} -> {_visibleRange.Start}..{_visibleRange.End}");
        }

        var maxRange = CalculateMaxRange(_renderLyricsLines);

        // ВАЖНО: сюда идёт только база прилипания (_canvasTargetScrollOffset).
        // Пользовательская прокрутка колесом (_mouseYScrollTransition) НЕ прибавляется —
        // она уже учитывается при отрисовке через UserScrollOffset в LyricsRenderer.
        // Сложение давало двойной учёт и ломало скролл.
        UpdateLines(
            _renderLyricsLines,
            isMouseScrolling ? maxRange.Start : _visibleRange.Start,
            isMouseScrolling ? maxRange.End : _visibleRange.End,
            _primaryPlayingLineIndex,
            renderLyricsWidth,
            renderLyricsHeight,
            _canvasTargetScrollOffset,
            playingLineTopOffset / 100.0,
            _canvasYScrollTransition,
            elapsedTime,
            isMouseScrolling,
            _isLayoutChanged,
            isPrimaryPlayingLineChanged,
            isMouseScrollingChanged,
            _isNowPlayingPaletteChanged,
            _songPositionWithOffset.TotalMilliseconds,
            isSnapToLineRequested,
            _isPlayingLinePending
        );

        isMouseScrollingChanged = false;
        isReturningFromScroll = false;
        _isNowPlayingPaletteChanged = false;

        if (isLyricsVisible)
        {
            _lyricsRenderer.CalculateLyrics3DMatrix(_isLayoutChanged);
        }

        _isLayoutChanged = false;
        
        if (isLyricsVisible)
        {
            _lyricsRenderer.MouseHoverLineIndex = _mouseHoverLineIndex;
            _lyricsRenderer.IsMousePressing = isMousePressing;
            _lyricsRenderer.StartVisibleLineIndex = _visibleRange.Start;
            _lyricsRenderer.EndVisibleLineIndex = _visibleRange.End;
            _lyricsRenderer.UserScrollOffset = _mouseYScrollTransition.Value;
            _lyricsRenderer.LyricsX = renderLyricsStartX;
            _lyricsRenderer.LyricsY = renderLyricsStartY;
            _lyricsRenderer.LyricsWidth = renderLyricsWidth;
            _lyricsRenderer.LyricsHeight = renderLyricsHeight;
            _lyricsRenderer.IsLyricsVisible = isLyricsVisible;
            _lyricsRenderer.PlayingLineTopOffsetFactor = playingLineTopOffset / 100.0;
            _lyricsRenderer.CurrentProgressMs = _songPositionWithOffset.TotalMilliseconds;
            _lyricsRenderer.PendingLineIndex = _isPlayingLinePending ? _primaryPlayingLineIndex : -1;
            _lyricsRenderer.RenderConfig = new LyricsRenderConfig(wordByWordEffectMode, lyricsLineContentOrientation, is3DLyricsEnabled, autoWrap, isLyricsBrethingEffectEnabled, isLyricsFloatAnimationEnabled, isLyricsGlowEffectEnabled, isLyricsScaleEffectEnabled, isRightToLeftLyric, isFanLyricsEnabled, fanLyricsAngle, lyricsFontStrokeWidth, playingLineTopOffset, lyrics3DXAngle, lyrics3DYAngle, lyrics3DZAngle, lyrics3DDepth, lyricsGlowColor);
            _lyricsRenderer.RenderLyricsLines = _renderLyricsLines;
            _lyricsRenderer.Update(canvas, 0, lyricsBreathingIntensity);
        }

        // Запрашиваем кадр отрисовки: CanvasControl рисует только по Invalidate().
        if (canvas != null && isLyricsVisible)
        {
            canvas.Invalidate();
        }
    }

    /// <summary>
    /// Кадр для текста без таймингов: строки не привязаны к позиции плеера.
    /// Полотно остаётся на месте (скролл — только пользовательский), раскладка
    /// поддерживается актуальной, кадры продолжают запрашиваться — благодаря
    /// этому при следующей песне с таймингами слежение включается сразу.
    /// </summary>
    private void UpdateUntimedFrame(TimeSpan elapsedTime)
    {
        _immersiveBgOpacityTransition.Update(elapsedTime);
        _immersiveBgColorTransition.Update(elapsedTime);

        TriggerRelayout();

        if (_renderLyricsLines != null)
        {
            // Позиция плеера не используется: прогресс и подсветка сняты.
            _primaryPlayingLineIndex = 0;
            _isPlayingLinePending = false;

            foreach (var line in _renderLyricsLines)
            {
                line.IsPlayingLastFrame = false;
            }

            UpdateLines(
                _renderLyricsLines,
                isMouseScrolling ? 0 : _visibleRange.Start,
                isMouseScrolling ? _renderLyricsLines.Count - 1 : _visibleRange.End,
                0,
                renderLyricsWidth,
                renderLyricsHeight,
                0,
                playingLineTopOffset / 100.0,
                _canvasYScrollTransition,
                elapsedTime,
                isMouseScrolling,
                _isLayoutChanged,
                false,
                isMouseScrollingChanged,
                false,
                // Прогресс заведомо до начала первой строки: символы не «проиграны».
                double.NegativeInfinity,
                false,
                false);
        }

        _mouseYScrollTransition.Update(elapsedTime);

        _mouseHoverLineIndex = FindMouseHoverLineIndex(
            _renderLyricsLines,
            isMouseInLyricsArea,
            mousePosition,
            _mouseYScrollTransition.Value,
            renderLyricsHeight,
            playingLineTopOffset / 100.0);

        _visibleRange = CalculateVisibleRange(
            _renderLyricsLines,
            _mouseYScrollTransition.Value,
            renderLyricsStartY,
            renderLyricsHeight,
            ActualHeight,
            playingLineTopOffset / 100.0);

        isMouseScrollingChanged = false;
        isReturningFromScroll = false;
        _isNowPlayingPaletteChanged = false;
        _isLayoutChanged = false;

        if (isLyricsVisible)
        {
            _lyricsRenderer.CalculateLyrics3DMatrix(false);
            _lyricsRenderer.MouseHoverLineIndex = _mouseHoverLineIndex;
            _lyricsRenderer.IsMousePressing = isMousePressing;
            _lyricsRenderer.StartVisibleLineIndex = _visibleRange.Start;
            _lyricsRenderer.EndVisibleLineIndex = _visibleRange.End;
            _lyricsRenderer.UserScrollOffset = _mouseYScrollTransition.Value;
            _lyricsRenderer.LyricsX = renderLyricsStartX;
            _lyricsRenderer.LyricsY = renderLyricsStartY;
            _lyricsRenderer.LyricsWidth = renderLyricsWidth;
            _lyricsRenderer.LyricsHeight = renderLyricsHeight;
            _lyricsRenderer.IsLyricsVisible = isLyricsVisible;
            _lyricsRenderer.PlayingLineTopOffsetFactor = playingLineTopOffset / 100.0;
            // Время за пределами текста — ни одна строка не подсвечена.
            _lyricsRenderer.CurrentProgressMs = double.NegativeInfinity;
            _lyricsRenderer.PendingLineIndex = -1;
            _lyricsRenderer.RenderConfig = new LyricsRenderConfig(wordByWordEffectMode, lyricsLineContentOrientation, is3DLyricsEnabled, autoWrap, isLyricsBrethingEffectEnabled, isLyricsFloatAnimationEnabled, isLyricsGlowEffectEnabled, isLyricsScaleEffectEnabled, isRightToLeftLyric, isFanLyricsEnabled, fanLyricsAngle, lyricsFontStrokeWidth, playingLineTopOffset, lyrics3DXAngle, lyrics3DYAngle, lyrics3DZAngle, lyrics3DDepth, lyricsGlowColor);
            _lyricsRenderer.RenderLyricsLines = _renderLyricsLines;
            _lyricsRenderer.Update(canvas, 0, lyricsBreathingIntensity);
        }

        if (canvas != null && isLyricsVisible)
        {
            canvas.Invalidate();
        }
    }

    /// <summary>
    /// Показывает плашку-подсказку, когда у песни нет таймингов текста:
    /// пользователь видит, почему строки не подсвечиваются.
    /// </summary>
    internal void UpdateUntimedBadge()
    {
        if (_untimedBadge == null) return;

        _untimedBadge.Visibility = IsUntimedNow ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePlaybackState(TimeSpan elapsedTime)
    {
        // Пока ждём подтверждения перемотки от плеера, локальные часы стоят:
        // SnapToLine уже выставил их на начало выбранной строки. Тиканье от этой базы
        // сдвигало бы цель и делало момент снятия ожидания непредсказуемым.
        if (_pendingSnapLineIndex >= 0) return;

        if (currentIsPlaying)
        {
            _songPosition += elapsedTime;
            _songPositionWithOffset = _songPosition + TimeSpan.FromMilliseconds(positionOffset);
        }
    }

    private void TriggerRelayout()
    {
        if (!_isLayoutChanged) return;

        if (canvas == null || currentLyricsData == null)
        {
            BetterLyricDebugLog.Write(
                BetterLyricLogCategory.Layout,
                $"TriggerRelayout: нет ресурсов (canvas={(canvas == null ? "null" : "ok")}, data={(currentLyricsData == null ? "null" : "ok")}) — строки сброшены");

            DisposeRenderLyricsLines();
            _renderLyricsData = null;
            _layoutSignature = null;
            return;
        }

        var lyricsData = currentLyricsData;
        var contentSignature = (lyricsFontStrokeWidth, isPhoneticEnabled, isTranslationEnabled);
        var layoutSignature = LayoutSignature.From(this, ActualWidth, ActualHeight);

        // 1. Текст песни или содержимое строк изменилось — строки нужно построить заново.
        bool isDataReplaced = !ReferenceEquals(_renderLyricsData, lyricsData);
        bool isContentSignatureChanged = _contentSignature != contentSignature;
        bool isLineCountMismatch = _renderLyricsLines != null && _renderLyricsLines.Count != lyricsData.LyricsLines.Count;
        bool isLinesMissing = _renderLyricsLines == null;

        bool isContentChanged = _isContentDirty
            || isDataReplaced
            || isContentSignatureChanged
            || isLineCountMismatch
            || isLinesMissing;

        if (isContentChanged)
        {
            BetterLyricDebugLog.Write(
                BetterLyricLogCategory.Layout,
                $"Пересоздание строк: dirty={_isContentDirty}, dataReplaced={isDataReplaced}, " +
                $"contentSigChanged={isContentSignatureChanged}, countMismatch={isLineCountMismatch}, linesMissing={isLinesMissing}, " +
                $"newCount={lyricsData.LyricsLines.Count}");

            // Данные сменились — подсказка «без синхронизации» должна соответствовать новому тексту.
            UpdateUntimedBadge();

            DisposeRenderLyricsLines();

            _renderLyricsData = lyricsData;
            _contentSignature = contentSignature;
            _layoutSignature = null;
            _isContentDirty = false;

            _renderLyricsLines = lyricsData.LyricsLines.Select(x => new RenderLyricsLine(x)).ToList();

            EnsureRenderLyricsLinesPreservedAnimation();

            if (_renderLyricsLines == null) return;

            CalculateLanes(_renderLyricsLines);

            // Новая композиция текста — обязательно пересчитать матрицу 3D и сбросить видимость.
            _lastDrawnLineCount = -1;
            _isEmptyFrameReported = false;
        }

        // 2. Раскладка не изменилась — сохраняем готовые CanvasTextLayout, геометрию и кэши.
        if (_layoutSignature == layoutSignature)
        {
            BetterLyricDebugLog.Write(
                BetterLyricLogCategory.Layout,
                "Раскладка не изменилась — CanvasTextLayout и кэши сохранены");
            return;
        }

        var stopwatch = BetterLyricDebugLog.IsEnabled(BetterLyricLogCategory.Layout)
            ? System.Diagnostics.Stopwatch.StartNew()
            : null;

        MeasureAndArrange(
            resourceCreator: canvas,
            lines: _renderLyricsLines,
            canvasWidth: ActualWidth,
            canvasHeight: ActualHeight,
            lyricsWidth: renderLyricsWidth,
            lyricsHeight: renderLyricsHeight
        );

        _layoutSignature = layoutSignature;

        if (stopwatch != null)
        {
            stopwatch.Stop();
            BetterLyricDebugLog.Write(
                BetterLyricLogCategory.Layout,
                $"MeasureAndArrange за {stopwatch.Elapsed.TotalMilliseconds:0.##} мс, " +
                $"строк={_renderLyricsLines?.Count ?? 0}, высота текста={CalculateActualHeight(_renderLyricsLines):0.##}");
        }
    }

    /// <summary>
    /// Планирует пересчёт раскладки, только если признаки раскладки действительно изменились.
    /// Свойства, влияющие лишь на анимацию (скорости, тайминги, эффекты), сюда не относятся.
    /// </summary>
    private void RequestRelayout()
    {
        if (canvas == null || currentLyricsData == null)
        {
            // Настройки могли быть применены до загрузки шаблона (страница создаёт контрол
            // и сразу раскладывает по нему сохранённые значения). Раньше такой запрос
            // просто терялся, и значения «доезжали» до текста только при открытии панели.
            if (canvas == null)
                _isRelayoutPendingBeforeTemplate = true;

            BetterLyricDebugLog.Write(
                BetterLyricLogCategory.Layout,
                $"RequestRelayout: отложен до появления ресурсов, canvas={(canvas == null ? "null" : "ok")}, data={(currentLyricsData == null ? "null" : "ok")}");
            return;
        }

        var layoutSignature = LayoutSignature.From(this, ActualWidth, ActualHeight);

        // ВАЖНО: досрочный выход допустим только когда данные и содержимое строк
        // точно актуальны. Иначе смена CurrentLyricsData на объект с тем же числом строк
        // и теми же настройками раскладки никогда не доедет до TriggerRelayout.
        bool isContentFresh = !_isContentDirty
            && ReferenceEquals(_renderLyricsData, currentLyricsData)
            && _renderLyricsLines != null;

        if (isContentFresh && _layoutSignature == layoutSignature)
        {
            BetterLyricDebugLog.Write(
                BetterLyricLogCategory.Layout,
                "RequestRelayout: пропуск, сигнатура раскладки и содержимое совпадают");
            return;
        }

        BetterLyricDebugLog.Write(
            BetterLyricLogCategory.Layout,
            $"RequestRelayout: запланирован пересчёт (contentFresh={isContentFresh}, " +
            $"layoutSigMatch={_layoutSignature == layoutSignature})");

        _ = _layoutDebouncer.RunAsync(() =>
        {
            _isLayoutChanged = true;
        });
    }

    /// <summary>
    /// Забирает отложенные до появления Canvas изменения: возвращает true и сбрасывает флаг,
    /// если свойства раскладки/содержимого менялись, пока шаблон ещё не был применён.
    /// </summary>
    private bool ConsumePendingRelayoutBeforeTemplate()
    {
        if (!_isRelayoutPendingBeforeTemplate) return false;

        _isRelayoutPendingBeforeTemplate = false;
        return true;
    }

    private void ResetPlaybackState()
    {
        _songPosition = TimeSpan.Zero;
    }

    private void DisposeRenderLyricsLines()
    {
        if (_renderLyricsLines != null)
        {
            foreach (var item in _renderLyricsLines)
            {
                item.DisposeTextGeometry();
                item.DisposeTextLayout();
                item.DisposeCaches();
            }
            _renderLyricsLines = null;
        }
    }

    private void UpdateCurrentPosition()
    {
        // Во время ожидания перемотки локальная позиция уже синхронизирована с выбранной
        // строкой. Плеер какое-то время ещё отдаёт старое значение — если принять его,
        // синхронизатор вернёт прежнюю строку и текст прыгнет назад.
        if (_pendingSnapLineIndex >= 0)
        {
            // Единственное настоящее подтверждение перемотки — позиция САМОГО плеера
            // рядом с началом выбранной строки. Локальное совпадение синхронизатора
            // таким подтверждением не является: SnapToLine сам выставил _songPosition.
            var pendingLine = _renderLyricsLines != null
                && _pendingSnapLineIndex < _renderLyricsLines.Count
                ? _renderLyricsLines[_pendingSnapLineIndex]
                : null;

            if (pendingLine != null)
            {
                double distanceToLineStart = Math.Abs(currentPosition.TotalMilliseconds - pendingLine.StartMs);

                if (distanceToLineStart < Math.Max(timelineSyncThreshold, 300))
                {
                    _snapConfirmedByPlayer = true;

                    // Принимаем внешнее время как базу локальных часов: дальше
                    // UpdatePlaybackState тикает уже от позиции плеера, а не от StartMs.
                    _songPosition = currentPosition;
                    _songPositionWithOffset = _songPosition + TimeSpan.FromMilliseconds(positionOffset);

                    BetterLyricDebugLog.WriteAlways(
                        BetterLyricLogCategory.Scroll,
                        $"СНАП: плеер подтвердил перемотку — его позиция {currentPosition.TotalMilliseconds:0} мс " +
                        $"рядом с началом строки {_pendingSnapLineIndex} ({pendingLine.StartMs} мс), " +
                        $"расхождение {distanceToLineStart:0} мс");
                }
                else
                {
                    BetterLyricDebugLog.Write(
                        BetterLyricLogCategory.Scroll,
                        $"Позиция {currentPosition.TotalMilliseconds:0} мс проигнорирована — идёт ожидание перемотки " +
                        $"к строке {_pendingSnapLineIndex} (плеер ещё в {currentPosition.TotalMilliseconds:0} мс, " +
                        $"до цели {distanceToLineStart:0} мс)");
                }
            }

            return;
        }

        var diff = Math.Abs(_songPosition.TotalMilliseconds - currentPosition.TotalMilliseconds);

        if (diff >= timelineSyncThreshold)
        {
            BetterLyricDebugLog.Write(
                BetterLyricLogCategory.Scroll,
                $"Синхронизация позиции: расхождение {diff:0} мс >= порога {timelineSyncThreshold} мс, " +
                $"local={_songPosition.TotalMilliseconds:0} -> {currentPosition.TotalMilliseconds:0}");

            _songPosition = currentPosition;
        }

        if (diff >= timelineSyncThreshold + 5000)
        {
            BetterLyricDebugLog.Write(
                BetterLyricLogCategory.Scroll,
                $"Большое расхождение позиции ({diff:0} мс) — запрошен пересчёт раскладки");

            RequestRelayout();
        }
    }

    public void ApplyLayoutFromContainer(FrameworkElement lyricsContainer, FrameworkElement root)
    {
        var transform = lyricsContainer.TransformToVisual(root);
        var localRect = new Windows.Foundation.Rect(0, 0, this.ActualWidth, this.ActualHeight);
        var relativeRect = transform.TransformBounds(localRect);

        renderLyricsStartX = relativeRect.X;
        renderLyricsStartY = relativeRect.Y;
        renderLyricsWidth = lyricsContainer.ActualWidth;
        renderLyricsHeight = lyricsContainer.ActualHeight;

        if (lyrics3DAutoFitLayout)
        {
            (renderLyricsHeight, renderLyricsWidth) = (renderLyricsWidth, renderLyricsHeight);
            renderLyricsStartX += (renderLyricsHeight - renderLyricsWidth) / 2;
            renderLyricsStartY += (renderLyricsWidth - renderLyricsHeight) / 2;
        }

        BetterLyricDebugLog.Write(
            BetterLyricLogCategory.Layout,
            $"ApplyLayoutFromContainer: {renderLyricsWidth:0.#}x{renderLyricsHeight:0.#} @ {renderLyricsStartX:0.#},{renderLyricsStartY:0.#}");

        RequestRelayout();
    }

    private void EnsureRenderLyricsLinesPreservedAnimation()
    {
        if (_renderLyricsLines == null) return;

        if (!isLyricsScaleEffectEnabled && !isLyricsGlowEffectEnabled) return;

        int animationPadding = (int)BetterLyricTimeSpanHelper.AnimationDuration.TotalMilliseconds;
        int longSyllableThreshold = Math.Max(
            lyricsScaleEffectLongSyllableDuration,
            lyricsGlowEffectLongSyllableDuration
        );

        var lines = _renderLyricsLines;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line == null) continue;

            bool isLastLine = i + 1 >= lines.Count;
            var nextLineStartMs = isLastLine ? (int)currentLyricsData.Duration.TotalMilliseconds : lines[i + 1].StartMs;

            bool isLongSyllable = line.PrimaryRenderSyllables.LastOrDefault()?.DurationMs > longSyllableThreshold;

            if (line.EndMs.HasValue && isLongSyllable)
            {
                if (isLastLine || line.EndMs > nextLineStartMs)
                {
                    line.EndMs += animationPadding;
                }
                else
                {
                    int targetEndMs = line.EndMs.Value + animationPadding;

                    line.EndMs = Math.Max(line.EndMs.Value, Math.Min(targetEndMs, nextLineStartMs));
                }
            }
        }
    }

    /// <summary>
    /// Признаки, от которых зависит геометрия строк (шрифты, размеры, выравнивание, обёртка).
    /// Если они не изменились — пересоздавать CanvasTextLayout и кэши не нужно.
    /// </summary>
    private readonly record struct LayoutSignature(
        double CanvasWidth,
        double CanvasHeight,
        double LyricsWidth,
        double LyricsHeight,
        bool IsDynamicFontSize,
        int OriginalFontSize,
        int TranslatedFontSize,
        int PhoneticFontSize,
        LyricFontWeight FontWeight,
        string? WesternFontFamily,
        string? CjkFontFamily,
        LyricsTextAlignmentType Alignment,
        bool AutoWrap,
        LyricsContentOrientation Orientation,
        double InnerSpacing,
        double OverallSpacing
    )
    {
        public static LayoutSignature From(BetterLyric ctl, double canvasWidth, double canvasHeight) => new(
            canvasWidth,
            canvasHeight,
            ctl.renderLyricsWidth,
            ctl.renderLyricsHeight,
            ctl.isDynamicLyricsFontSize,
            ctl.originalLyricsFontSize,
            ctl.translatedLyricsFontSize,
            ctl.phoneticLyricsFontSize,
            ctl.lyricsFontWeight,
            ctl.lyricsWesternFontFamily,
            ctl.lyricsCJKFontFamily,
            ctl.lyricsAlignmentType,
            ctl.autoWrap,
            ctl.lyricsLineContentOrientation,
            ctl.lyricsLineInnerSpacingFactor,
            ctl.lyricsLineOverallSpacingFactor
        );

        /// <summary>
        /// Человекочитаемый список изменившихся полей — для отладочных логов.
        /// </summary>
        public string DescribeChanges(in LayoutSignature other)
        {
            var changes = new List<string>();

            if (CanvasWidth != other.CanvasWidth) changes.Add($"CanvasWidth {CanvasWidth:0.#}->{other.CanvasWidth:0.#}");
            if (CanvasHeight != other.CanvasHeight) changes.Add($"CanvasHeight {CanvasHeight:0.#}->{other.CanvasHeight:0.#}");
            if (LyricsWidth != other.LyricsWidth) changes.Add($"LyricsWidth {LyricsWidth:0.#}->{other.LyricsWidth:0.#}");
            if (LyricsHeight != other.LyricsHeight) changes.Add($"LyricsHeight {LyricsHeight:0.#}->{other.LyricsHeight:0.#}");
            if (IsDynamicFontSize != other.IsDynamicFontSize) changes.Add($"IsDynamicFontSize {IsDynamicFontSize}->{other.IsDynamicFontSize}");
            if (OriginalFontSize != other.OriginalFontSize) changes.Add($"OriginalFontSize {OriginalFontSize}->{other.OriginalFontSize}");
            if (TranslatedFontSize != other.TranslatedFontSize) changes.Add($"TranslatedFontSize {TranslatedFontSize}->{other.TranslatedFontSize}");
            if (PhoneticFontSize != other.PhoneticFontSize) changes.Add($"PhoneticFontSize {PhoneticFontSize}->{other.PhoneticFontSize}");
            if (FontWeight != other.FontWeight) changes.Add($"FontWeight {FontWeight}->{other.FontWeight}");
            if (WesternFontFamily != other.WesternFontFamily) changes.Add($"WesternFontFamily '{WesternFontFamily}'->'{other.WesternFontFamily}'");
            if (CjkFontFamily != other.CjkFontFamily) changes.Add($"CjkFontFamily '{CjkFontFamily}'->'{other.CjkFontFamily}'");
            if (Alignment != other.Alignment) changes.Add($"Alignment {Alignment}->{other.Alignment}");
            if (AutoWrap != other.AutoWrap) changes.Add($"AutoWrap {AutoWrap}->{other.AutoWrap}");
            if (Orientation != other.Orientation) changes.Add($"Orientation {Orientation}->{other.Orientation}");
            if (InnerSpacing != other.InnerSpacing) changes.Add($"InnerSpacing {InnerSpacing:0.##}->{other.InnerSpacing:0.##}");
            if (OverallSpacing != other.OverallSpacing) changes.Add($"OverallSpacing {OverallSpacing:0.##}->{other.OverallSpacing:0.##}");

            return changes.Count == 0 ? "нет" : string.Join(", ", changes);
        }
    }

    /// <summary>
    /// Лёгкий путь для свойств, влияющих только на анимацию и прозрачность:
    /// пересоздавать строки не нужно, достаточно пересчитать текущее состояние.
    /// </summary>
    private void RequestAnimationRefresh()
    {
        if (_renderLyricsLines == null)
        {
            // До загрузки шаблона строк ещё нет — запоминаем, что настройки менялись,
            // чтобы после появления Canvas выполнить полный пересчёт.
            if (canvas == null)
                _isRelayoutPendingBeforeTemplate = true;

            return;
        }

        if (isMouseScrolling)
        {
            isMouseScrollingChanged = true;
        }
        else
        {
            // Заставляем аниматор заново разложить целевые значения для видимых строк.
            _isLayoutChanged = true;
        }
    }

    /// <summary>
    /// Пометить, что данные текста/содержимое строк изменились — строки будут пересозданы
    /// при ближайшем <see cref="TriggerRelayout"/>, независимо от сигнатур.
    /// </summary>
    private void MarkContentDirty(string reason)
    {
        _isContentDirty = true;

        // Пометка тоже могла прийти до появления Canvas — её нужно пронести через
        // OnApplyTemplate/OnCreateResources, иначе строки останутся с прежним содержимым.
        if (canvas == null)
            _isRelayoutPendingBeforeTemplate = true;

        BetterLyricDebugLog.Write(BetterLyricLogCategory.Layout, $"MarkContentDirty: {reason}");
    }

    /// <summary>
    /// Признак смены цветовой палитры «сейчас играет» — заставляет аниматор
    /// заново разложить цвета/прозрачности для видимых строк.
    /// </summary>
    internal void NotifyNowPlayingPaletteChanged()
    {
        _isNowPlayingPaletteChanged = true;
        BetterLyricDebugLog.Write(BetterLyricLogCategory.Anim, "Палитра «сейчас играет» изменена — пересчёт цветов строк");
    }

    /// <summary>
    /// Снимок состояния контрола для диагностики. Выводится в лог и может быть
    /// показан пользователю (например, скопирован в буфер обмена).
    /// </summary>
    public string DumpState()
    {
        var lines = _renderLyricsLines;

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("=== BetterLyric DumpState ===");
        builder.AppendLine($"Frame: {BetterLyricDebugLog.Frame}");
        builder.AppendLine($"Canvas: {(canvas == null ? "null" : $"{canvas.Size.Width:0.#}x{canvas.Size.Height:0.#}")}");
        builder.AppendLine($"ActualSize: {ActualWidth:0.#}x{ActualHeight:0.#}");
        builder.AppendLine($"LyricsRect: {renderLyricsWidth:0.#}x{renderLyricsHeight:0.#} @ {renderLyricsStartX:0.#},{renderLyricsStartY:0.#}");
        builder.AppendLine($"IsLyricsVisible: {isLyricsVisible}");
        builder.AppendLine($"AutoLayout: {isAutoLayoutEnabled}, AutoWrap: {autoWrap}, RTL: {isRightToLeftLyric}");
        builder.AppendLine($"IsPlaying: {currentIsPlaying}, Position: {currentPosition.TotalMilliseconds:0} мс, Local: {_songPosition.TotalMilliseconds:0} мс, Offset: {positionOffset} мс");
        builder.AppendLine($"Untimed: {IsUntimedNow} (property={isLyricsUntimed}, data={currentLyricsData?.IsUntimed}), Badge: {(_untimedBadge?.Visibility.ToString() ?? "null")}");
        builder.AppendLine($"SyncThreshold: {timelineSyncThreshold} мс");
        builder.AppendLine($"Data: {(currentLyricsData == null ? "null" : $"{currentLyricsData.LyricsLines.Count} строк, wordByWord={currentLyricsData.IsWordByWord}, duration={currentLyricsData.Duration.TotalSeconds:0.##} с")}");
        builder.AppendLine($"RenderLines: {(lines == null ? "null" : lines.Count.ToString())}, contentDirty={_isContentDirty}, layoutChanged={_isLayoutChanged}");
        builder.AppendLine($"LayoutSignature: {(_layoutSignature?.ToString() ?? "null")}");
        builder.AppendLine($"ContentSignature: {(_contentSignature?.ToString() ?? "null")}");
        builder.AppendLine($"PlayingLineIndex: {_primaryPlayingLineIndex}, HoverIndex: {_mouseHoverLineIndex}");
        builder.AppendLine($"VisibleRange: {_visibleRange.Start}..{_visibleRange.End}, LastDrawn: {_lastDrawnLineCount}");
        builder.AppendLine($"Scroll: canvas={_canvasYScrollTransition.Value:0.##} (target={_canvasTargetScrollOffset:0.##}, transitioning={_canvasYScrollTransition.IsTransitioning}), mouse={_mouseYScrollTransition.Value:0.##}");
        builder.AppendLine($"Mouse: inArea={isMouseInLyricsArea}, scrolling={isMouseScrolling}, pressing={isMousePressing}, pos={mousePosition.X:0.#},{mousePosition.Y:0.#}");
        builder.AppendLine($"ActualLyricsHeight: {CalculateActualHeight(lines):0.##}");

        if (lines != null && lines.Count > 0)
        {
            int show = Math.Min(lines.Count, 5);
            for (int i = 0; i < show; i++)
            {
                var line = lines[i];
                builder.AppendLine(
                    $"  [{i}] '{Truncate(line.PrimaryText, 40)}' {line.StartMs}-{line.EndMs} мс, lane={line.LaneIndex}, " +
                    $"layout={(line.PrimaryTextLayout == null ? "null" : $"{line.PrimaryTextLayout.LayoutBounds.Width:0.#}x{line.PrimaryTextLayout.LayoutBounds.Height:0.#}")}, " +
                    $"top={line.TopLeftPosition.Y:0.#}, center={line.CenterPosition.Y:0.#}, yOff={line.YOffsetTransition.Value:0.##}, " +
                    $"scale={line.ScaleTransition.Value:0.##}, blur={line.BlurAmountTransition.Value:0.##}, " +
                    $"playedOpacity={line.PlayedPrimaryOpacityTransition.Value:0.##}, unplayedOpacity={line.UnplayedPrimaryOpacityTransition.Value:0.##}, " +
                    $"caches={(line.CachedFill == null ? "нет" : "ok")}");
            }

            if (lines.Count > show)
                builder.AppendLine($"  ... ещё {lines.Count - show} строк");
        }

        var dump = builder.ToString();
        BetterLyricDebugLog.WriteAlways(BetterLyricLogCategory.Lifecycle, "DumpState:\n" + dump);
        return dump;
    }

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "…";
    }

    /// <summary>Время в формате mm:ss.fff — для читаемых отладочных логов.</summary>
    private static string FormatTime(TimeSpan time)
        => $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
}
