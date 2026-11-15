#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace VK_UI3.Controls.BetterLyric;

/// <summary>
/// Категории отладочных сообщений <see cref="BetterLyric"/>.
/// Позволяют включать только нужный слой: например, только отрисовку или только раскладку.
/// </summary>
[Flags]
public enum BetterLyricLogCategory
{
    None = 0,

    /// <summary>Жизненный цикл контрола: ApplyTemplate, CreateResources, Loaded/Unloaded.</summary>
    Lifecycle = 1 << 0,

    /// <summary>Пересоздание строк и раскладка: TriggerRelayout, RequestRelayout, MeasureAndArrange.</summary>
    Layout = 1 << 1,

    /// <summary>Отрисовка: диапазон видимых строк, пропуски, пустые кадры.</summary>
    Draw = 1 << 2,

    /// <summary>Анимации появления/исчезновения: YOffset, Scale, Blur, Opacity.</summary>
    Anim = 1 << 3,

    /// <summary>Прокрутка: прилипание к строке, колесо, дебаунс-возврат.</summary>
    Scroll = 1 << 4,

    /// <summary>Загрузка и применение текста песни (LyricsPage).</summary>
    Data = 1 << 5,

    All = Lifecycle | Layout | Draw | Anim | Scroll | Data
}

/// <summary>
/// Централизованный переключаемый логгер для контроля <see cref="BetterLyric"/>.
/// <para>
/// Выключен по умолчанию — при <see cref="Enabled"/> = <c>false</c> все вызовы
/// сводятся к проверке булевого поля, поэтому оверхед нулевой.
/// </para>
/// <para>
/// Включить можно тремя способами:
/// <list type="bullet">
///   <item>из XAML: <c>IsDebugLoggingEnabled="True"</c> у <c>BetterLyric</c>;</item>
///   <item>из кода: <c>BetterLyricDebugLog.Enabled = true;</c></item>
///   <item>глобально при старте приложения — см. <c>BetterLyricDebugLog</c> в Debug-сборке.</item>
/// </list>
/// </para>
/// </summary>
public static class BetterLyricDebugLog
{
    /// <summary>
    /// Глобальный переключатель. Проверяется в горячем пути, поэтому держим его как volatile-поле.
    /// <para>
    /// В DEBUG-сборке включён по умолчанию: все важные события (смена строки, клики,
    /// прокрутка, раскладка) сразу видны в Output → Debug. В Release — выключен,
    /// включается через <c>IsDebugLoggingEnabled</c> у контрола.
    /// </para>
    /// </summary>
    private static volatile bool _enabled =
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>Активные категории сообщений.</summary>
    private static volatile int _categories = (int)BetterLyricLogCategory.All;

    /// <summary>Минимальный интервал между «кадровыми» сообщениями одной категории, мс.</summary>
    private static volatile int _frameThrottleMs = 250;

    private static long _frameCounter;

    // Время последнего сообщения по каждой категории (Environment.TickCount64).
    private static readonly long[] LastWriteTicks = new long[8];

    /// <summary>Включена ли отладочная запись.</summary>
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (value)
            {
                ResetCounters();
                Write(BetterLyricLogCategory.Lifecycle, "=== Debug logging ENABLED ===");
            }
            else
            {
                Write(BetterLyricLogCategory.Lifecycle, "=== Debug logging DISABLED ===");
            }
        }
    }

    /// <summary>Активные категории.</summary>
    public static BetterLyricLogCategory Categories
    {
        get => (BetterLyricLogCategory)_categories;
        set => _categories = (int)value;
    }

    /// <summary>Троттлинг для сообщений, которые потенциально пишутся каждый кадр.</summary>
    public static int FrameThrottleMs
    {
        get => _frameThrottleMs;
        set => _frameThrottleMs = Math.Max(0, value);
    }

    /// <summary>Номер текущего кадра отрисовки (увеличивается в <see cref="NextFrame"/>).</summary>
    public static long Frame => Interlocked.Read(ref _frameCounter);

    /// <summary>Увеличивает счётчик кадров. Вызывается из OnUpdate/OnDraw.</summary>
    public static void NextFrame() => Interlocked.Increment(ref _frameCounter);

    /// <summary>Сбрасывает счётчик кадров и метки троттлинга.</summary>
    public static void ResetCounters()
    {
        Interlocked.Exchange(ref _frameCounter, 0);
        for (int i = 0; i < LastWriteTicks.Length; i++)
            LastWriteTicks[i] = 0;
    }

    /// <summary>Включена ли указанная категория.</summary>
    public static bool IsEnabled(BetterLyricLogCategory category)
    {
        if (!_enabled) return false;
        return (_categories & (int)category) != 0;
    }

    /// <summary>Записать сообщение, если включены логи и категория.</summary>
    public static void Write(BetterLyricLogCategory category, string message)
    {
        if (!IsEnabled(category)) return;
        Debug.WriteLine(Format(category, message));
    }

    /// <summary>
    /// Записать сообщение с троттлингом по категории.
    /// Возвращает <c>true</c>, если сообщение действительно было записано.
    /// </summary>
    public static bool WriteThrottled(BetterLyricLogCategory category, string message)
    {
        if (!IsEnabled(category)) return false;

        int index = CategoryIndex(category);
        long now = Environment.TickCount64;

        if (index >= 0)
        {
            long last = Interlocked.Read(ref LastWriteTicks[index]);
            if (now - last < _frameThrottleMs) return false;
            Interlocked.Exchange(ref LastWriteTicks[index], now);
        }

        Debug.WriteLine(Format(category, message));
        return true;
    }

    /// <summary>
    /// Записать сообщение, игнорируя троттлинг, но только при включённой категории.
    /// Используется для «важных» событий: пустой кадр, ошибка, смена трека.
    /// </summary>
    public static void WriteAlways(BetterLyricLogCategory category, string message)
    {
        if (!IsEnabled(category)) return;
        Debug.WriteLine(Format(category, message));
    }

    private static int CategoryIndex(BetterLyricLogCategory category)
    {
        for (int i = 0; i < 8; i++)
        {
            if ((int)category == 1 << i) return i;
        }
        return -1;
    }

    private static string Format(BetterLyricLogCategory category, string message)
        => $"[{DateTime.Now:HH:mm:ss.fff}] [BetterLyric/{category}] [f{Interlocked.Read(ref _frameCounter)}] {message}";
}
