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
    /// <summary>
    /// 重排歌词，Y 轴从 0 刻度开始算
    /// </summary>
    /// <param name="resourceCreator"></param>
    /// <param name="lyricsData"></param>
    /// <param name="status"></param>
    /// <param name="appSettings"></param>
    /// <param name="canvasWidth"></param>
    /// <param name="canvasHeight"></param>
    /// <param name="lyricsWidth"></param>
    /// <param name="lyricsHeight"></param>
    internal void MeasureAndArrange(
        ICanvasResourceCreator resourceCreator,
        IList<RenderLyricsLine>? lines,
        double canvasWidth,
        double canvasHeight,
        double lyricsWidth,
        double lyricsHeight)
    {
        if (lines == null || resourceCreator == null)
        {
            BetterLyricDebugLog.Write(
                BetterLyricLogCategory.Layout,
                $"MeasureAndArrange: пропуск (lines={(lines == null ? "null" : "ok")}, creator={(resourceCreator == null ? "null" : "ok")})");
            return;
        }

        bool isLogLayout = BetterLyricDebugLog.IsEnabled(BetterLyricLogCategory.Layout);

        // 计算字体大小
        int originalFontSize, phoneticFontSize, translatedFontSize;

        if (isDynamicLyricsFontSize)
        {
            var lyricsLayoutMetrics = LyricsLayoutHelper.CalculateLayout(canvasWidth, canvasHeight);

            phoneticFontSize = (int)lyricsLayoutMetrics.TransliterationSize;
            originalFontSize = (int)lyricsLayoutMetrics.MainLyricsSize;
            translatedFontSize = (int)lyricsLayoutMetrics.TranslationSize;
        }
        else
        {
            phoneticFontSize = phoneticLyricsFontSize;
            originalFontSize = originalLyricsFontSize;
            translatedFontSize = translatedLyricsFontSize;
        }

        var fontWeight = lyricsFontWeight;

        if (isLogLayout)
        {
            BetterLyricDebugLog.Write(
                BetterLyricLogCategory.Layout,
                $"MeasureAndArrange: canvas={canvasWidth:0.#}x{canvasHeight:0.#}, area={lyricsWidth:0.#}x{lyricsHeight:0.#}, " +
                $"font={originalFontSize}/{translatedFontSize}/{phoneticFontSize}, weight={fontWeight}, " +
                $"phonetic={isPhoneticEnabled}, translation={isTranslationEnabled}, wrap={autoWrap}, align={lyricsAlignmentType}, orient={lyricsLineContentOrientation}");
        }

        // 排版
        double currentX = 0;
        double currentY = 0;

        int emptyLayouts = 0;

        foreach (var line in lines)
        {
            if (line == null) continue;

            double actualWidth = 0;

            line.RecreateTextLayout(
                resourceCreator,
                isPhoneticEnabled,
                isTranslationEnabled,
                phoneticFontSize, originalFontSize, translatedFontSize,
                fontWeight,
                lyricsCJKFontFamily, lyricsWesternFontFamily,
                lyricsWidth, lyricsHeight,
                lyricsAlignmentType, autoWrap, lyricsLineContentOrientation
            );

            line.RecreateTextGeometry();

            line.DisposeCaches();

            // 左上角坐标
            line.TopLeftPosition = new Vector2((float)currentX, (float)currentY);
            // 注音层
            line.TertiaryPosition = line.TopLeftPosition;
            if (line.TertiaryTextLayout != null)
            {
                currentY += line.TertiaryTextLayout.LayoutBounds.Height;
                currentY += (line.TertiaryTextLayout.LayoutBounds.Height / Math.Max(1, line.TertiaryTextLayout.LineCount)) * lyricsLineInnerSpacingFactor; // 间距
                actualWidth = Math.Max(actualWidth, line.TertiaryTextLayout.LayoutBounds.Width);
            }

            // 原文层
            line.PrimaryPosition = new Vector2((float)currentX, (float)currentY);
            if (line.PrimaryTextLayout != null)
            {
                currentY += line.PrimaryTextLayout.LayoutBounds.Height;
                actualWidth = Math.Max(actualWidth, line.PrimaryTextLayout.LayoutBounds.Width);
            }

            switch (lyricsLineContentOrientation)
            {
                case LyricsContentOrientation.Horizontal:
                    // 翻译层
                    line.SecondaryPosition = new Vector2(
                        (float)(currentX + lyricsWidth / 2),
                        (float)(line.TertiaryPosition.Y + (currentY - line.TertiaryPosition.Y) / 2 - (line.SecondaryTextLayout?.LayoutBounds.Height ?? 0) / 2));
                    if (line.SecondaryTextLayout != null)
                    {
                        currentY = Math.Max(line.SecondaryPosition.Y + line.SecondaryTextLayout.LayoutBounds.Height, currentY);
                        actualWidth += line.SecondaryTextLayout.LayoutBounds.Width;
                    }
                    break;
                case LyricsContentOrientation.Vertical:
                    // 翻译层
                    currentY += (line.SecondaryTextLayout?.LayoutBounds.Height ?? 0) / Math.Max(1, line.SecondaryTextLayout?.LineCount ?? 1) * lyricsLineInnerSpacingFactor; // 间距
                    line.SecondaryPosition = new Vector2((float)currentX, (float)currentY);
                    if (line.SecondaryTextLayout != null)
                    {
                        currentY += line.SecondaryTextLayout.LayoutBounds.Height;
                        actualWidth = Math.Max(actualWidth, line.SecondaryTextLayout.LayoutBounds.Width);
                    }
                    break;
                default:
                    break;
            }

            // 右下角坐标
            line.BottomRightPosition = new Vector2((float)currentX + (float)actualWidth, (float)currentY);

            // 行间距
            if (line.PrimaryTextLayout != null)
            {
                // LineCount может быть 0 для пустого текста — защита от деления на ноль,
                // из-за которой межстрочный интервал не добавлялся вовсе.
                int lineCount = Math.Max(1, line.PrimaryTextLayout.LineCount);
                currentY += (line.PrimaryTextLayout.LayoutBounds.Height / lineCount) * lyricsLineOverallSpacingFactor;
            }

            line.TopLeftPosition = line.PrimaryTextLayout?.HorizontalAlignment switch
            {
                CanvasHorizontalAlignment.Left => line.TopLeftPosition,
                CanvasHorizontalAlignment.Center => line.TopLeftPosition.AddX((float)((lyricsWidth - actualWidth) / 2)),
                CanvasHorizontalAlignment.Right => line.TopLeftPosition.AddX((float)(lyricsWidth - actualWidth)),
                _ => line.TopLeftPosition
            };

            line.BottomRightPosition = line.PrimaryTextLayout?.HorizontalAlignment switch
            {
                CanvasHorizontalAlignment.Left => line.BottomRightPosition,
                CanvasHorizontalAlignment.Center => line.BottomRightPosition.AddX((float)((lyricsWidth - actualWidth) / 2)),
                CanvasHorizontalAlignment.Right => line.BottomRightPosition.AddX((float)(lyricsWidth - actualWidth)),
                _ => line.BottomRightPosition
            };

            // 更新中心点
            double centerY = (line.TopLeftPosition.Y + line.BottomRightPosition.Y) / 2;

            line.CenterPosition = line.PrimaryTextLayout?.HorizontalAlignment switch
            {
                CanvasHorizontalAlignment.Left => new Vector2(0, (float)centerY),
                CanvasHorizontalAlignment.Center => new Vector2((float)(lyricsWidth / 2), (float)centerY),
                CanvasHorizontalAlignment.Right => new Vector2((float)(lyricsWidth), (float)centerY),
                _ => line.CenterPosition,
            };

            line.RecreateRenderChars(lyricsFontStrokeWidth);

            if (line.PrimaryTextLayout == null || line.PrimaryTextLayout.LayoutBounds.Width <= 0)
            {
                emptyLayouts++;

                if (isLogLayout)
                {
                    BetterLyricDebugLog.Write(
                        BetterLyricLogCategory.Layout,
                        $"  Строка '{TruncateForLog(line.PrimaryText, 30)}' (lane={line.LaneIndex}) не имеет валидного Layout: " +
                        $"layout={(line.PrimaryTextLayout == null ? "null" : $"{line.PrimaryTextLayout.LayoutBounds.Width:0.##}x{line.PrimaryTextLayout.LayoutBounds.Height:0.##}")}, " +
                        $"chars={line.PrimaryRenderChars.Count}, syllables={line.PrimaryRenderSyllables.Count}");
                }
            }
        }

        if (isLogLayout)
        {
            // Проверка монотонности: центр каждой строки должен быть ниже предыдущего.
            // Нарушение означает, что CenterPosition рассинхронизирован с раскладкой —
            // тогда CalculateTargetScrollOffset даёт неверную цель и прилипание «прыгает».
            int monotonicBreaks = 0;
            double previousCenter = double.NegativeInfinity;
            int firstBreakIndex = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                if (l?.PrimaryTextLayout == null) continue;

                if (l.CenterPosition.Y <= previousCenter)
                {
                    if (firstBreakIndex < 0) firstBreakIndex = i;
                    monotonicBreaks++;
                }

                previousCenter = l.CenterPosition.Y;
            }

            BetterLyricDebugLog.WriteAlways(
                BetterLyricLogCategory.Layout,
                $"MeasureAndArrange готово: строк={lines.Count}, высота={currentY:0.##}, пустых Layout={emptyLayouts}, " +
                $"нарушений порядка CenterPosition={monotonicBreaks}" +
                (firstBreakIndex >= 0 ? $" (первое на строке {firstBreakIndex})" : ""));

            if (monotonicBreaks > 0)
            {
                for (int i = 0; i < Math.Min(lines.Count, 8); i++)
                {
                    var l = lines[i];
                    BetterLyricDebugLog.Write(
                        BetterLyricLogCategory.Layout,
                        $"  [{i}] top={l.TopLeftPosition.Y:0.#} bottom={l.BottomRightPosition.Y:0.#} " +
                        $"center={l.CenterPosition.Y:0.#} h={(l.PrimaryTextLayout?.LayoutBounds.Height ?? 0):0.#} " +
                        $"lines={l.PrimaryTextLayout?.LineCount ?? 0} lane={l.LaneIndex}");
                }
            }
        }
    }

    private static string TruncateForLog(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "…";
    }

    /// <summary>
    /// 计算为了让当前歌词行的竖直几何中心点对齐到 0（原点），画布应该移动的距离（从画布最初始状态计算的值）
    /// </summary>
    internal static double? CalculateTargetScrollOffset(
        IList<RenderLyricsLine>? lines,
        int playingLineIndex)
    {
        if (lines == null || lines.Count == 0) return null;

        var currentLine = lines.ElementAtOrDefault(playingLineIndex);

        if (currentLine?.PrimaryTextLayout == null) return null;

        return -currentLine.CenterPosition.Y;
    }

    /// <summary>
    /// 计算当前屏幕可见的行范围
    /// 返回值: (StartVisibleIndex, EndVisibleIndex)
    /// </summary>
    internal static (int Start, int End) CalculateVisibleRange(
        IList<RenderLyricsLine>? lines,
        double currentScrollOffset,
        double lyricsY,
        double lyricsHeight,
        double canvasHeight,
        double playingLineTopOffsetFactor
    )
    {
        if (lines == null || lines.Count == 0) return (-1, -1);

        double offset = currentScrollOffset + lyricsY + lyricsHeight * playingLineTopOffsetFactor;

        int start = FindFirstVisibleLine(lines, offset, lyricsY);
        int end = FindLastVisibleLine(lines, offset, lyricsY, lyricsHeight, canvasHeight);

        // 修正边界情况
        if (start != -1 && end == -1)
        {
            end = lines.Count - 1;
        }

        return (start, end);
    }

    internal static (int Start, int End) CalculateMaxRange(IList<RenderLyricsLine>? lines)
    {
        if (lines == null || lines.Count == 0) return (-1, -1);

        return (0, lines.Count - 1);
    }

    internal static double CalculateActualHeight(IList<RenderLyricsLine>? lines)
    {
        if (lines == null || lines.Count == 0) return 0;

        return lines.Last().BottomRightPosition.Y;
    }

    internal static void CalculateLanes(IList<RenderLyricsLine>? lines, int toleranceMs = 50)
    {
        if (lines == null) return;
        var lanesEndMs = new List<int> { 0 };

        foreach (var line in lines)
        {
            var start = line.StartMs;
            var end = line.EndMs;

            int assignedLane = -1;
            for (int i = 0; i < lanesEndMs.Count; i++)
            {
                if (lanesEndMs[i] <= start + toleranceMs)
                {
                    assignedLane = i;
                    break;
                }
            }

            if (assignedLane == -1)
            {
                assignedLane = lanesEndMs.Count;
                lanesEndMs.Add(0);
            }

            lanesEndMs[assignedLane] = end ?? 0;
            line.LaneIndex = assignedLane;
        }
    }

    internal static int FindMouseHoverLineIndex(
        IList<RenderLyricsLine>? lines,
        bool isMouseInLyricsArea,
        Point mousePosition,
        double currentScrollOffset,
        double lyricsHeight,
        double playingLineTopOffsetFactor
    )
    {
        if (!isMouseInLyricsArea) return -1;

        if (lines == null || lines.Count == 0) return -1;

        double yOffset = currentScrollOffset + lyricsHeight * playingLineTopOffsetFactor;

        int left = 0, right = lines.Count - 1, result = -1;
        while (left <= right)
        {
            int mid = (left + right) / 2;
            var line = lines[mid];
            if (line.PrimaryTextLayout == null) break;
            double lineBottomY = yOffset + line.BottomRightPosition.Y;
            if (lineBottomY >= mousePosition.Y)
            {
                result = mid;
                right = mid - 1;
            }
            else { left = mid + 1; }
        }

        if (result != -1)
        {
            var line = lines[result];
            double lineLeftX = line.TopLeftPosition.X;
            double lineRightX = line.BottomRightPosition.X;
            double lineTopY = yOffset + line.TopLeftPosition.Y;
            if (mousePosition.X < lineLeftX || mousePosition.X > lineRightX || mousePosition.Y < lineTopY)
            {
                result = -1;
            }
        }

        return result;
    }

    /// <summary>
    /// Первая строка, которая ещё не полностью ушла выше экрана
    /// (её низ ниже верхней границы видимой области).
    /// </summary>
    private static int FindFirstVisibleLine(IList<RenderLyricsLine> lines, double offset, double lyricsY)
    {
        int left = 0, right = lines.Count - 1, result = -1;

        while (left <= right)
        {
            int mid = (left + right) / 2;
            var line = lines[mid];
            if (line.PrimaryTextLayout == null) break;

            // Экранная Y нижней границы строки.
            double bottomOnScreen = offset + line.BottomRightPosition.Y;

            if (bottomOnScreen >= 0)
            {
                result = mid;
                right = mid - 1;
            }
            else
            {
                left = mid + 1;
            }
        }

        return result;
    }

    /// <summary>
    /// Последняя строка, которая ещё не полностью ушла ниже экрана
    /// (её верх выше нижней границы видимой области).
    /// <para>
    /// Раньше здесь искалась ПЕРВАЯ строка за нижней границей — из-за этого диапазон
    /// обрывался на первой же невидимой строке и часть текста не рисовалась.
    /// </para>
    /// </summary>
    private static int FindLastVisibleLine(IList<RenderLyricsLine> lines, double offset, double lyricsY, double lyricsHeight, double canvasHeight)
    {
        int left = 0, right = lines.Count - 1, result = -1;

        while (left <= right)
        {
            int mid = (left + right) / 2;
            var line = lines[mid];
            if (line.PrimaryTextLayout == null) break;

            // Экранная Y верхней границы строки.
            double topOnScreen = offset + line.TopLeftPosition.Y;

            if (topOnScreen <= canvasHeight)
            {
                result = mid;
                left = mid + 1;
            }
            else
            {
                right = mid - 1;
            }
        }

        return result;
    }
}
