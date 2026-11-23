#nullable enable
using System;
using System.Diagnostics;
using Microsoft.UI.Xaml.Media;

namespace VK_UI3.Controls.BetterLyric;

/// <summary>
/// Цикл кадров для <c>CanvasControl</c>.
/// <para>
/// <c>CanvasAnimatedControl</c> сам крутил поток обновления/отрисовки, но его
/// composition-поверхность непрозрачна. <c>CanvasControl</c> прозрачен, зато
/// не имеет собственного цикла: кадры двигает <see cref="CompositionTarget.Rendering"/>,
/// а перерисовка запрашивается через <c>Invalidate()</c>.
/// </para>
/// <para>
/// Заодно решена проблема потоков: раньше <c>OnUpdate</c>/<c>OnDraw</c> выполнялись
/// на фоновом потоке Win2D (нельзя было читать DependencyProperty). Теперь всё идёт
/// на UI-потоке, поэтому чтение свойств контрола безопасно.
/// </para>
/// </summary>
public partial class BetterLyric
{
    private bool _isRenderLoopRunning;
    private bool _isRenderingSubscribed;
    private long _lastFrameTimestamp;
    private readonly Stopwatch _renderClock = new();

    /// <summary>
    /// Контрол подключён к визуальному дереву. Между <c>Unloaded</c> и повторным
    /// <c>Loaded</c> <c>CanvasControl</c> уже уничтожен — обращаться к нему нельзя.
    /// </summary>
    private bool _isAttachedToTree;

    /// <summary>
    /// Перезапускает кадровый цикл, если он уже был инициализирован, но остановлен.
    /// Нужно после <c>LyricsPage.Disable()/Enable()</c> (переключение вкладок):
    /// подписка на <see cref="CompositionTarget.Rendering"/> к этому моменту снята,
    /// а повторного <c>Loaded</c>/<c>OnApplyTemplate</c> не будет — без этого вызова
    /// текст навсегда оставался «замороженным» после возврата на вкладку.
    /// </summary>
    internal void ResumeRenderLoop()
    {
        // ВАЖНО: проверять canvas == null недостаточно. После Unloaded поле обнуляется,
        // но если контрол ещё не перезагрузился, ссылка могла остаться на уже уничтоженный
        // Native-объект Win2D — обращение к нему даёт AccessViolation (0xC0000005).
        // Перезапускаем цикл только когда контрол реально в дереве и canvas переподключён.
        if (!_isAttachedToTree || canvas == null)
            return;

        StartRenderLoop();

        // Сбрасываем отметку времени: после паузы дельта кадра не должна быть огромной.
        _lastFrameTimestamp = 0;

        // Поверхность Win2D после смены вкладки могла остаться без валидного кадра:
        // содержимое не пересоздано, а старый кадр не показывается. Помечаем контент
        // «грязным» и пересчитываем раскладку, затем принудительно запрашиваем отрисовку.
        _isContentDirty = true;
        _layoutSignature = null;
        _isLayoutChanged = true;

        RequestRelayout();

        // Даём новому CanvasControl пройти measure/arrange, затем рисуем.
        if (DispatcherQueue != null)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (!_isAttachedToTree || canvas == null)
                    return;

                TriggerRelayout();
                canvas.Invalidate();
            });
        }
    }

    /// <summary>
    /// <c>CanvasControl</c> создаёт <c>CanvasDevice</c> лениво — только при первом
    /// <c>Draw</c>/<c>CreateResources</c>. До этого любое создание Win2D-ресурсов
    /// (CanvasCommandList, CanvasTextLayout и т. п.) бросает ArgumentException
    /// «The control does not currently have a CanvasDevice associated with it».
    /// </summary>
    private bool _isCanvasDeviceReady;

    /// <summary>Предел одного шага (сек): защита от скачка после сна/подвисания.</summary>
    private const double MaxFrameSeconds = 0.1;

    private void StartRenderLoop()
    {
        _isRenderLoopRunning = true;

        if (_isRenderingSubscribed)
            return;

        _renderClock.Restart();
        _lastFrameTimestamp = 0;

        CompositionTarget.Rendering -= OnCompositionRendering;
        CompositionTarget.Rendering += OnCompositionRendering;
        _isRenderingSubscribed = true;
    }

    private void StopRenderLoop()
    {
        _isRenderLoopRunning = false;

        if (!_isRenderingSubscribed)
            return;

        CompositionTarget.Rendering -= OnCompositionRendering;
        _isRenderingSubscribed = false;

        _renderClock.Stop();
        _lastFrameTimestamp = 0;
    }

    private void OnCompositionRendering(object? sender, object e)
    {
        if (!_isRenderLoopRunning || canvas == null)
            return;

        // Текст выключен (панель закрыта / страница неактивна) — кадры не нужны.
        // Цикл не отписывается, чтобы не терять подписку; просто ничего не делает.
        if (!isLyricsVisible)
            return;

        // Device ещё не создан — кадр обновлять нечем: раскладка/эффекты требуют
        // CanvasDevice. Ждём первого CreateResources/Draw, он придёт сам.
        if (!_isCanvasDeviceReady)
            return;

        long timestamp = _renderClock.ElapsedTicks;
        double deltaSeconds = _lastFrameTimestamp == 0
            ? 1.0 / 60.0
            : (double)(timestamp - _lastFrameTimestamp) / Stopwatch.Frequency;

        _lastFrameTimestamp = timestamp;

        if (deltaSeconds <= 0)
            deltaSeconds = 1.0 / 60.0;
        else if (deltaSeconds > MaxFrameSeconds)
            deltaSeconds = MaxFrameSeconds;

        UpdateFrame(TimeSpan.FromSeconds(deltaSeconds));
    }
}
