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

namespace VK_UI3.Controls.BetterLyric;

public partial class BetterLyric
{
    private double renderLyricsStartX = 0;
    private double renderLyricsStartY = 0;
    private double renderLyricsWidth = 0;
    private double renderLyricsHeight = 0;
    private bool isLyricsVisible = false;
    private Point mousePosition = new(0, 0);
    private bool isMouseInLyricsArea = false;
    private bool isMousePressing = false;
    private bool isMouseScrolling = false;
    private bool isMouseScrollingChanged = false;
    private TimeSpan currentPosition = TimeSpan.Zero;
    private LyricData currentLyricsData = null;
    private bool currentIsPlaying = true;
    private int positionOffset = 0;
    private int timelineSyncThreshold = 0;
    private bool isScrollViewerEnabled = true;
    private bool isAutoLayoutEnabled = true;
    public double LyricsStartX
    {
        get { return (double)GetValue(LyricsStartXProperty); }
        set { SetValue(LyricsStartXProperty, value); }
    }

    public static readonly DependencyProperty LyricsStartXProperty =
        DependencyProperty.Register(nameof(LyricsStartX), typeof(double), typeof(BetterLyric), new PropertyMetadata(0.0, OnLyricsStartXChanged));

    private static void OnLyricsStartXChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        ctl.renderLyricsStartX = Convert.ToDouble(e.NewValue);
        ctl.RequestRelayout();
    }

    public double LyricsStartY
    {
        get { return (double)GetValue(LyricsStartYProperty); }
        set { SetValue(LyricsStartYProperty, value); }
    }

    public static readonly DependencyProperty LyricsStartYProperty =
        DependencyProperty.Register(nameof(LyricsStartY), typeof(double), typeof(BetterLyric), new PropertyMetadata(0.0, OnLyricsStartYChanged));

    private static void OnLyricsStartYChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        ctl.renderLyricsStartY = Convert.ToDouble(e.NewValue);
        ctl.RequestRelayout();
    }

    public double LyricsWidth
    {
        get { return (double)GetValue(LyricsWidthProperty); }
        set { SetValue(LyricsWidthProperty, value); }
    }

    public static readonly DependencyProperty LyricsWidthProperty =
        DependencyProperty.Register(nameof(LyricsWidth), typeof(double), typeof(BetterLyric), new PropertyMetadata(0.0, OnLyricsWidthChanged));

    private static void OnLyricsWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        ctl.renderLyricsWidth = Convert.ToDouble(e.NewValue);
        ctl.RequestRelayout();
    }

    public double LyricsHeight
    {
        get { return (double)GetValue(LyricsHeightProperty); }
        set { SetValue(LyricsHeightProperty, value); }
    }

    public static readonly DependencyProperty LyricsHeightProperty =
        DependencyProperty.Register(nameof(LyricsHeight), typeof(double), typeof(BetterLyric), new PropertyMetadata(0.0, OnLyricsHeightChanged));

    private static void OnLyricsHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        ctl.renderLyricsHeight = Convert.ToDouble(e.NewValue);
        ctl.RequestRelayout();
    }

    public bool IsLyricsVisible
    {
        get { return (bool)GetValue(IsLyricsVisibleProperty); }
        set { SetValue(IsLyricsVisibleProperty, value); }
    }

    public static readonly DependencyProperty IsLyricsVisibleProperty =
        DependencyProperty.Register(nameof(IsLyricsVisible), typeof(bool), typeof(BetterLyric), new PropertyMetadata(false, OnIsLyricsVisibleChanged));

    private static void OnIsLyricsVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        ctl.isLyricsVisible = (bool)e.NewValue;

        BetterLyricDebugLog.Write(
            BetterLyricLogCategory.Lifecycle,
            $"IsLyricsVisible -> {ctl.isLyricsVisible}");

        // Видимость не влияет на геометрию строк — достаточно лёгкого обновления.
        // Полный relayout здесь вызывал лишние пересоздания CanvasTextLayout
        // (а при совпадении сигнатур вообще ничего не делал).
        ctl.RequestAnimationRefresh();
    }

    public double MouseScrollOffset
    {
        get { return (double)GetValue(MouseScrollOffsetProperty); }
        set { SetValue(MouseScrollOffsetProperty, value); }
    }

    public static readonly DependencyProperty MouseScrollOffsetProperty =
        DependencyProperty.Register(nameof(MouseScrollOffset), typeof(double), typeof(BetterLyric), new PropertyMetadata(0.0, OnMouseScrollOffsetChanged));

    private static void OnMouseScrollOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        ctl._mouseYScrollTransition.Start(Convert.ToDouble(e.NewValue));
    }

    public Point MousePosition
    {
        get { return (Point)GetValue(MousePositionProperty); }
        set { SetValue(MousePositionProperty, value); }
    }

    public static readonly DependencyProperty MousePositionProperty =
        DependencyProperty.Register(nameof(MousePosition), typeof(Point), typeof(BetterLyric), new PropertyMetadata(new Point(0, 0), OnMousePositionChanged));

    private static void OnMousePositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        ctl.mousePosition = (Point)e.NewValue;
    }

    public bool IsMouseInLyricsArea
    {
        get { return (bool)GetValue(IsMouseInLyricsAreaProperty); }
        set { SetValue(IsMouseInLyricsAreaProperty, value); }
    }

    public static readonly DependencyProperty IsMouseInLyricsAreaProperty =
        DependencyProperty.Register(nameof(IsMouseInLyricsArea), typeof(bool), typeof(BetterLyric), new PropertyMetadata(false, OnIsMouseInLyricsAreaChanged));

    private static void OnIsMouseInLyricsAreaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        ctl.isMouseInLyricsArea = (bool)e.NewValue;
    }

    public bool IsMousePressing
    {
        get { return (bool)GetValue(IsMousePressingProperty); }
        set { SetValue(IsMousePressingProperty, value); }
    }

    public static readonly DependencyProperty IsMousePressingProperty =
        DependencyProperty.Register(nameof(IsMousePressing), typeof(bool), typeof(BetterLyric), new PropertyMetadata(false, OnIsMousePressingChanged));

    private static void OnIsMousePressingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        ctl.isMousePressing = (bool)e.NewValue;
    }

    public bool IsMouseScrolling
    {
        get { return (bool)GetValue(IsMouseScrollingProperty); }
        set { SetValue(IsMouseScrollingProperty, value); }
    }

    public static readonly DependencyProperty IsMouseScrollingProperty =
        DependencyProperty.Register(nameof(IsMouseScrolling), typeof(bool), typeof(BetterLyric), new PropertyMetadata(false, OnIsMouseScrollingChanged));

    private static void OnIsMouseScrollingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        var newValue = (bool)e.NewValue;
        var oldValue = (bool)e.OldValue;
        ctl.isMouseScrolling = newValue;
        if (newValue != oldValue)
        {
            ctl.isMouseScrollingChanged = true;
        }
    }
    public TimeSpan CurrentPosition
    {
        get { return (TimeSpan)GetValue(CurrentPositionProperty); }
        set { SetValue(CurrentPositionProperty, value); }
    }

    public static readonly DependencyProperty CurrentPositionProperty =
        DependencyProperty.Register(nameof(CurrentPosition), typeof(TimeSpan), typeof(BetterLyric), new PropertyMetadata(TimeSpan.Zero, OnCurrentPositionChanged));

    private static void OnCurrentPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        ctl.currentPosition = (TimeSpan)e.NewValue;
        ctl.UpdateCurrentPosition();
    }

    public LyricData CurrentLyricsData
    {
        get { return (LyricData)GetValue(CurrentLyricsDataProperty); }
        set { SetValue(CurrentLyricsDataProperty, value); }
    }

    public static readonly DependencyProperty CurrentLyricsDataProperty =
        DependencyProperty.Register(nameof(CurrentLyricsData), typeof(LyricData), typeof(BetterLyric), new PropertyMetadata(null, OnCurrentLyricsDataChanged));

    private static void OnCurrentLyricsDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        var oldData = ctl.currentLyricsData;
        var newData = (LyricData)e.NewValue;

        ctl.currentLyricsData = newData;

        BetterLyricDebugLog.WriteAlways(
            BetterLyricLogCategory.Data,
            $"CurrentLyricsData: {(oldData == null ? "null" : oldData.LyricsLines.Count + " строк")} -> " +
            $"{(newData == null ? "null" : newData.LyricsLines.Count + " строк")}");

        // Данные могли смениться на объект с тем же числом строк и той же раскладкой —
        // сигнатуры такое не поймают, поэтому явно помечаем содержимое устаревшим.
        ctl.MarkContentDirty("CurrentLyricsData changed");

        // Новая песня — начинаем с начала, а не с позиции предыдущего трека.
        ctl.ResetPlaybackState();
        ctl._synchronizer.Reset();
        ctl._primaryPlayingLineIndex = 0;

        ctl.RequestRelayout();
    }

    public bool CurrentIsPlaying
    {
        get { return (bool)GetValue(CurrentIsPlayingProperty); }
        set { SetValue(CurrentIsPlayingProperty, value); }
    }

    public static readonly DependencyProperty CurrentIsPlayingProperty =
        DependencyProperty.Register(nameof(CurrentIsPlaying), typeof(bool), typeof(BetterLyric), new PropertyMetadata(true, OnCurrentIsPlayingChanged));

    private static void OnCurrentIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        ctl.currentIsPlaying = (bool)e.NewValue;
    }

    public int PositionOffset
    {
        get { return (int)GetValue(PositionOffsetProperty); }
        set { SetValue(PositionOffsetProperty, value); }
    }

    public static readonly DependencyProperty PositionOffsetProperty =
        DependencyProperty.Register(nameof(PositionOffset), typeof(int), typeof(BetterLyric), new PropertyMetadata(0, OnPositionOffsetChanged));

    private static void OnPositionOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        ctl.positionOffset = (int)e.NewValue;
    }
    public int TimelineSyncThreshold
    {
        get { return (int)GetValue(TimelineSyncThresholdProperty); }
        set { SetValue(TimelineSyncThresholdProperty, value); }
    }

    public static readonly DependencyProperty TimelineSyncThresholdProperty =
        DependencyProperty.Register(nameof(TimelineSyncThreshold), typeof(int), typeof(BetterLyric), new PropertyMetadata(0, OnTimelineSyncThresholdChanged));

    private static void OnTimelineSyncThresholdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        ctl.timelineSyncThreshold = (int)e.NewValue;
    }

    public bool IsAutoLayoutEnabled
    {
        get { return (bool)GetValue(IsAutoLayoutEnabledProperty); }
        set { SetValue(IsAutoLayoutEnabledProperty, value); }
    }

    public static readonly DependencyProperty IsAutoLayoutEnabledProperty =
        DependencyProperty.Register(nameof(IsAutoLayoutEnabled), typeof(bool), typeof(BetterLyric), new PropertyMetadata(true, OnIsAutoLayoutEnabledChanged));

    private static void OnIsAutoLayoutEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        ctl.isAutoLayoutEnabled = (bool)e.NewValue;

        BetterLyricDebugLog.Write(BetterLyricLogCategory.Layout, $"IsAutoLayoutEnabled -> {ctl.isAutoLayoutEnabled}");

        // Переключение режима меняет источник размеров области текста — нужен пересчёт.
        ctl.RequestRelayout();
    }

    public bool IsScrollViewerEnabled
    {
        get { return (bool)GetValue(IsScrollViewerEnabledProperty); }
        set { SetValue(IsScrollViewerEnabledProperty, value); }
    }

    public static readonly DependencyProperty IsScrollViewerEnabledProperty =
        DependencyProperty.Register(nameof(IsScrollViewerEnabled), typeof(bool), typeof(BetterLyric), new PropertyMetadata(true, OnIsScrollViewerEnabledChanged));

    private static void OnIsScrollViewerEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        ctl.isScrollViewerEnabled = (bool)e.NewValue;

        // Отключаем взаимодействие — сбрасываем «залипшее» состояние мыши.
        if (!ctl.isScrollViewerEnabled)
        {
            ctl.isMouseInLyricsArea = false;
            ctl.isMousePressing = false;
            ctl.isMouseScrolling = false;
            ctl._mouseHoverLineIndex = -1;
            ctl._scrollWheelDebounceTimer.Cancel();
            ctl._mouseYScrollTransition.JumpTo(0);
            ctl.isMouseScrollingChanged = true;
        }
    }

    #region Debug / diagnostics

    /// <summary>
    /// Включает отладочные логи отрисовки, раскладки и анимаций текста.
    /// Сообщения пишутся в <see cref="System.Diagnostics.Debug"/> (Output → Debug).
    /// </summary>
    public bool IsDebugLoggingEnabled
    {
        get { return (bool)GetValue(IsDebugLoggingEnabledProperty); }
        set { SetValue(IsDebugLoggingEnabledProperty, value); }
    }

    public static readonly DependencyProperty IsDebugLoggingEnabledProperty =
        DependencyProperty.Register(nameof(IsDebugLoggingEnabled), typeof(bool), typeof(BetterLyric), new PropertyMetadata(false, OnIsDebugLoggingEnabledChanged));

    private static void OnIsDebugLoggingEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        var enabled = (bool)e.NewValue;

        BetterLyricDebugLog.Enabled = enabled;

        if (enabled)
        {
            BetterLyricDebugLog.WriteAlways(
                BetterLyricLogCategory.Lifecycle,
                "IsDebugLoggingEnabled=True — состояние контрола:\n" + ctl.DumpState());
        }
    }

    /// <summary>
    /// Набор категорий отладочных сообщений (по умолчанию все).
    /// </summary>
    public BetterLyricLogCategory DebugLogCategories
    {
        get { return (BetterLyricLogCategory)GetValue(DebugLogCategoriesProperty); }
        set { SetValue(DebugLogCategoriesProperty, value); }
    }

    public static readonly DependencyProperty DebugLogCategoriesProperty =
        DependencyProperty.Register(nameof(DebugLogCategories), typeof(BetterLyricLogCategory), typeof(BetterLyric), new PropertyMetadata(BetterLyricLogCategory.All, OnDebugLogCategoriesChanged));

    private static void OnDebugLogCategoriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        BetterLyricDebugLog.Categories = (BetterLyricLogCategory)e.NewValue;
    }

    /// <summary>
    /// Минимальный интервал между «кадровыми» отладочными сообщениями, мс.
    /// </summary>
    public int DebugLogFrameThrottleMs
    {
        get { return (int)GetValue(DebugLogFrameThrottleMsProperty); }
        set { SetValue(DebugLogFrameThrottleMsProperty, value); }
    }

    public static readonly DependencyProperty DebugLogFrameThrottleMsProperty =
        DependencyProperty.Register(nameof(DebugLogFrameThrottleMs), typeof(int), typeof(BetterLyric), new PropertyMetadata(250, OnDebugLogFrameThrottleMsChanged));

    private static void OnDebugLogFrameThrottleMsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        BetterLyricDebugLog.FrameThrottleMs = (int)e.NewValue;
    }

    #endregion
}
