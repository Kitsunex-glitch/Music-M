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
    private readonly double _defaultScale = 0.75f;
    private readonly double _highlightedScale = 1.0f;

    internal void UpdateLines(
        IList<RenderLyricsLine>? lines,
        int startIndex,
        int endIndex,
        int primaryPlayingLineIndex,
        double lyricsWidth,
        double lyricsHeight,
        double targetYScrollOffset,
        double playingLineTopOffsetFactor,
        ValueTransition<double> canvasYScrollTransition,
        TimeSpan elapsedTime,
        bool isMouseScrolling,
        bool isLayoutChanged,
        bool isPrimaryPlayingLineChanged,
        bool isMouseScrollingChanged,
        bool isArtThemeColorsChanged,
        double currentPositionMs,
        bool isSnapToLineRequested,
        bool isPlayingLinePending
    )
    {
        if (lines == null || lines.Count == 0)
        {
            BetterLyricDebugLog.WriteThrottled(
                BetterLyricLogCategory.Anim,
                "UpdateLines: пропуск — строки отсутствуют");
            return;
        }

        if (primaryPlayingLineIndex < 0 || primaryPlayingLineIndex >= lines.Count)
        {
            BetterLyricDebugLog.WriteThrottled(
                BetterLyricLogCategory.Anim,
                $"UpdateLines: пропуск — primaryPlayingLineIndex={primaryPlayingLineIndex} вне диапазона 0..{lines.Count - 1}");
            return;
        }

        var primaryPlayingLine = lines[primaryPlayingLineIndex];

        bool isLogAnimations = BetterLyricDebugLog.IsEnabled(BetterLyricLogCategory.Anim);
        if (isLogAnimations)
        {
            BetterLyricDebugLog.WriteThrottled(
                BetterLyricLogCategory.Anim,
                $"UpdateLines: range={startIndex}..{endIndex}, playing={primaryPlayingLineIndex}, " +
                $"layoutChanged={isLayoutChanged}, lineChanged={isPrimaryPlayingLineChanged}, " +
                $"mouseScroll={isMouseScrolling}/{isMouseScrollingChanged}, snap={isSnapToLineRequested}, palette={isArtThemeColorsChanged}, " +
                $"pending={isPlayingLinePending}, pos={currentPositionMs:0} мс, " +
                $"scroll={canvasYScrollTransition.Value:0.##}->{canvasYScrollTransition.TargetValue:0.##}, " +
                $"target={targetYScrollOffset:0.##}");
        }


        var phoneticOpacity = phoneticLyricsOpacity / 100.0;
        var originalOpacity = unplayedOriginalLyricsOpacity / 100.0;
        var translatedOpacity = translatedLyricsOpacity / 100.0;

        double topHeightFactor = lyricsHeight * playingLineTopOffsetFactor;
        double bottomHeightFactor = lyricsHeight * (1 - playingLineTopOffsetFactor);

        double scrollTopDurationSec = lyricsScrollTopDuration / 1000.0;
        double scrollTopDelaySec = lyricsScrollTopDelay / 1000.0;
        double scrollBottomDurationSec = lyricsScrollBottomDuration / 1000.0;
        double scrollBottomDelaySec = lyricsScrollBottomDelay / 1000.0;
        double canvasTransDuration = canvasYScrollTransition.DurationSeconds;

        bool isBlurEnabled = isLyricsBlurEffectEnabled;
        bool isOutOfSightEnabled = isLyricsOutOfSightEffectEnabled;
        bool isFanEnabled = isFanLyricsEnabled;
        double fanAngleRad = Math.PI * (fanLyricsAngle / 180.0);
        bool isGlowEnabled = isLyricsGlowEffectEnabled;
        bool isFloatEnabled = isLyricsFloatAnimationEnabled;
        bool isScaleEnabled = isLyricsScaleEffectEnabled;

        int safeStart = Math.Max(0, startIndex);
        int safeEnd = Math.Min(lines.Count - 1, endIndex + 1);

        for (int i = safeStart; i <= safeEnd; i++)
        {
            var line = lines[i];
            var lineHeight = line.PrimaryLineHeight;
            if (lineHeight == null || lineHeight <= 0)
            {
                if (isLogAnimations)
                {
                    BetterLyricDebugLog.WriteThrottled(
                        BetterLyricLogCategory.Anim,
                        $"  [{i}] пропуск: PrimaryLineHeight={(lineHeight.HasValue ? lineHeight.Value.ToString("0.##") : "null")} " +
                        $"(RenderChars={line.PrimaryRenderChars.Count})");
                }
                continue;
            }

            bool isWordAnimationEnabled = wordByWordEffectMode switch
            {
                LyricsWordByWordEffectMode.Auto => line.IsPrimaryHasRealSyllableInfo,
                LyricsWordByWordEffectMode.Always => true,
                LyricsWordByWordEffectMode.Never => false,
                _ => line.IsPrimaryHasRealSyllableInfo
            };

            double targetCharFloat = isLyricsFloatAnimationAmountAutoAdjust
                ? lineHeight.Value * 0.1
                : lyricsFloatAnimationAmount;
            double targetCharGlow = isLyricsGlowEffectAmountAutoAdjust
                ? lineHeight.Value * 0.2
                : lyricsGlowEffectAmount;
            double targetCharScale = isLyricsScaleEffectAmountAutoAdjust
                ? 1.15
                : lyricsScaleEffectAmount / 100.0;

            var maxAnimationDurationMs = Math.Max((line.EndMs ?? 0) - currentPositionMs, 0);

            bool isSecondaryLinePlaying = line.GetIsPlaying(currentPositionMs);

            // Строка ещё не началась (время в разрыве): полотно к ней едет,
            // но подсветка, масштаб и цвет «играющей» строки не включаются —
            // визуально она остаётся обычной непропетой строкой.
            bool isLineHighlighted = isSecondaryLinePlaying
                && !(isPlayingLinePending && i == primaryPlayingLineIndex);

            bool isSecondaryLinePlayingChanged = line.IsPlayingLastFrame != isLineHighlighted;
            line.IsPlayingLastFrame = isLineHighlighted;

            var playProgress = line.GetPlayProgress(currentPositionMs);

            // 行动画（примагничивание по клику пересчитывает офсеты так же, как смена играющей строки）
            bool isLineOffsetRefreshRequested = isPrimaryPlayingLineChanged || isSnapToLineRequested;

            if (isLayoutChanged || isLineOffsetRefreshRequested || isMouseScrollingChanged || isSecondaryLinePlayingChanged || isArtThemeColorsChanged)
            {
                if (isLogAnimations)
                {
                    BetterLyricDebugLog.WriteThrottled(
                        BetterLyricLogCategory.Anim,
                        $"  [{i}] старт переходов (playing={isSecondaryLinePlaying}, highlighted={isLineHighlighted}, " +
                        $"playingChanged={isSecondaryLinePlayingChanged}, " +
                        $"progress={playProgress:0.###}, layoutChanged={isLayoutChanged}, offsetRefresh={isLineOffsetRefreshRequested})");
                }
                int lineCountDelta = i - primaryPlayingLineIndex;
                double distanceFromPlayingLine = Math.Abs(line.TopLeftPosition.Y - primaryPlayingLine.TopLeftPosition.Y);

                double distanceFactor;
                if (lineCountDelta < 0)
                {
                    distanceFactor = Math.Clamp(distanceFromPlayingLine / topHeightFactor, 0, 1);
                }
                else
                {
                    distanceFactor = Math.Clamp(distanceFromPlayingLine / bottomHeightFactor, 0, 1);
                }

                double yScrollDuration;
                double yScrollDelay;

                if (lineCountDelta < 0)
                {
                    yScrollDuration =
                        canvasTransDuration +
                        distanceFactor * (scrollTopDurationSec - canvasTransDuration);
                    yScrollDelay = distanceFactor * scrollTopDelaySec;
                }
                else if (lineCountDelta == 0)
                {
                    yScrollDuration = canvasTransDuration;
                    yScrollDelay = 0;
                }
                else
                {
                    yScrollDuration =
                        canvasTransDuration +
                        distanceFactor * (scrollBottomDurationSec - canvasTransDuration);
                    yScrollDelay = distanceFactor * scrollBottomDelaySec;
                }

                line.BlurAmountTransition.SetDuration(yScrollDuration);
                line.BlurAmountTransition.SetDelay(yScrollDelay);
                line.BlurAmountTransition.Start(
                    (isMouseScrolling || isLineHighlighted) ? 0 :
                    (isBlurEnabled ? (5 * distanceFactor) : 0));

                line.ScaleTransition.SetDuration(yScrollDuration);
                line.ScaleTransition.SetDelay(yScrollDelay);
                line.ScaleTransition.Start(
                    isLineHighlighted ? _highlightedScale :
                    (isOutOfSightEnabled ?
                    (_highlightedScale - distanceFactor * (_highlightedScale - _defaultScale)) :
                    _highlightedScale));

                line.TertiaryOpacityTransition.SetDuration(yScrollDuration);
                line.TertiaryOpacityTransition.SetDelay(yScrollDelay);
                line.TertiaryOpacityTransition.Start(
                    isLineHighlighted ? phoneticOpacity :
                    CalculateTargetOpacity(phoneticOpacity, phoneticOpacity, distanceFactor, isMouseScrolling));

                // 原文不透明度（已播放）
                line.PlayedPrimaryOpacityTransition.SetDuration(yScrollDuration);
                line.PlayedPrimaryOpacityTransition.SetDelay(yScrollDelay);
                line.PlayedPrimaryOpacityTransition.Start(
                    isLineHighlighted ? 1.0 :
                    CalculateTargetOpacity(originalOpacity, 1.0, distanceFactor, isMouseScrolling));
                // 原文不透明度（未播放）
                line.UnplayedPrimaryOpacityTransition.SetDuration(yScrollDuration);
                line.UnplayedPrimaryOpacityTransition.SetDelay(yScrollDelay);
                line.UnplayedPrimaryOpacityTransition.Start(
                    isLineHighlighted ? originalOpacity :
                    CalculateTargetOpacity(originalOpacity, originalOpacity, distanceFactor, isMouseScrolling));

                line.SecondaryOpacityTransition.SetDuration(yScrollDuration);
                line.SecondaryOpacityTransition.SetDelay(yScrollDelay);
                line.SecondaryOpacityTransition.Start(
                    isLineHighlighted ? translatedOpacity :
                    CalculateTargetOpacity(translatedOpacity, translatedOpacity, distanceFactor, isMouseScrolling));

                line.PlayedFillColorTransition.SetDuration(yScrollDuration);
                line.PlayedFillColorTransition.SetDelay(yScrollDelay);
                line.PlayedFillColorTransition.Start(isLineHighlighted ? playedCurrentLineFillColor : nonCurrentLineFillColor);

                line.UnplayedFillColorTransition.SetDuration(yScrollDuration);
                line.UnplayedFillColorTransition.SetDelay(yScrollDelay);
                line.UnplayedFillColorTransition.Start(isLineHighlighted ? unplayedCurrentLineFillColor : nonCurrentLineFillColor);

                line.PlayedStrokeColorTransition.SetDuration(yScrollDuration);
                line.PlayedStrokeColorTransition.SetDelay(yScrollDelay);
                line.PlayedStrokeColorTransition.Start(isLineHighlighted ? playedTextStrokeColor : unplayedTextStrokeColor);

                line.UnplayedStrokeColorTransition.SetDuration(yScrollDuration);
                line.UnplayedStrokeColorTransition.SetDelay(yScrollDelay);
                line.UnplayedStrokeColorTransition.Start(unplayedTextStrokeColor);

                line.AngleTransition.SetInterpolator(canvasYScrollTransition.Interpolator);
                line.AngleTransition.SetDuration(yScrollDuration);
                line.AngleTransition.SetDelay(yScrollDelay);
                line.AngleTransition.Start(
                    (isFanEnabled && !isMouseScrolling) ?
                    fanAngleRad * distanceFactor * (i > primaryPlayingLineIndex ? 1 : -1) :
                    0);

                // ВАЖНО: targetYScrollOffset — это уже ГОТОВЫЙ абсолютный Y-офсет строки,
                // посчитанный как -(CenterPosition.Y активной строки) в CalculateTargetScrollOffset.
                // Прибавлять сюда CenterPosition.Y НЕЛЬЗЯ: получался двойной учёт,
                // из-за которого при прокрутке колесом текст уезжал вниз за пределы экрана
                // («окно как будто уменьшается сверху»).
                double lineTarget = targetYScrollOffset;

                if (isLayoutChanged)
                {
                    // Пересчёт раскладки — мгновенно, без анимации.
                    line.YOffsetTransition.SetInterpolator(canvasYScrollTransition.Interpolator);
                    line.YOffsetTransition.JumpTo(lineTarget);
                }
                else if (isSnapToLineRequested || isLineOffsetRefreshRequested || canvasYScrollTransition.IsTransitioning)
                {
                    // Смена играющей строки, клик по строке или анимация общего скролла:
                    // все строки идут к цели синхронно с canvas-переходом (без задержек
                    // и с его длительностью), иначе между полотном и строками
                    // появляется рассинхрон — визуально «сначала прыжок к старой строке».
                    line.YOffsetTransition.SetInterpolator(canvasYScrollTransition.Interpolator);
                    line.YOffsetTransition.SetDuration(canvasYScrollTransition.DurationSeconds);
                    line.YOffsetTransition.SetDelay(0);
                    line.YOffsetTransition.Start(lineTarget);
                }
                else if (isMouseScrollingChanged)
                {
                    line.YOffsetTransition.SetInterpolator(canvasYScrollTransition.Interpolator);
                    line.YOffsetTransition.SetDuration(yScrollDuration);
                    line.YOffsetTransition.SetDelay(yScrollDelay);
                    line.YOffsetTransition.Start(lineTarget);
                }
            }

            if (isWordAnimationEnabled)
            {
                if (isSecondaryLinePlayingChanged)
                {
                    // 辉光动画（从行首开始到当前）
                    if (isGlowEnabled && lyricsGlowEffectScope == LyricEffectScope.LineStartToCurrentChar
                         && isLineHighlighted)
                    {
                        foreach (var renderChar in line.PrimaryRenderChars)
                        {
                            var stepInOutDuration = Math.Min(BetterLyricTimeSpanHelper.AnimationDuration.TotalMilliseconds, maxAnimationDurationMs) / 2.0 / 1000.0;
                            var stepLastingDuration = Math.Max(maxAnimationDurationMs / 1000.0 - stepInOutDuration * 2, 0);
                            renderChar.GlowTransition.Start(
                                new BetterLyricKeyframe<double>(targetCharGlow, stepInOutDuration),
                                new BetterLyricKeyframe<double>(targetCharGlow, stepLastingDuration),
                                new BetterLyricKeyframe<double>(0, stepInOutDuration)
                            );
                        }
                    }

                    // 浮动动画（控制整体）
                    if (isFloatEnabled)
                    {
                        foreach (var renderChar in line.PrimaryRenderChars)
                        {
                            if (isLineHighlighted)
                            {
                                if (renderChar.EndMs < currentPositionMs)
                                {
                                    // 确保已播放的部分恢复原位
                                    renderChar.FloatTransition.JumpTo(0);
                                }
                                else
                                {
                                    // 下沉（以便后续上浮）
                                    renderChar.FloatTransition.Start(targetCharFloat);
                                }
                            }
                            else
                            {
                                // 恢复初始状态（相当于上浮）
                                renderChar.FloatTransition.Start(0);
                            }
                        }
                    }
                }

                // 浮动动画（控制单个）
                foreach (var renderChar in line.PrimaryRenderChars)
                {
                    renderChar.ProgressPlayed = renderChar.GetPlayProgress(currentPositionMs);

                    bool isCharPlaying = renderChar.GetIsPlaying(currentPositionMs);
                    bool isCharPlayingChanged = renderChar.IsPlayingLastFrame != isCharPlaying;

                    if (isCharPlayingChanged)
                    {
                        if (isFloatEnabled)
                        {
                            renderChar.FloatTransition.SetDurationMs(Math.Min(lyricsFloatAnimationDuration, maxAnimationDurationMs));
                            renderChar.FloatTransition.Start(0);
                        }

                        renderChar.IsPlayingLastFrame = isCharPlaying;
                    }
                    else
                    {
                        if (!isCharPlaying && currentPositionMs > renderChar.EndMs && renderChar.FloatTransition.Value != 0)
                        {
                            renderChar.FloatTransition.SetDurationMs(Math.Min(lyricsFloatAnimationDuration, maxAnimationDurationMs));
                            renderChar.FloatTransition.Start(0);
                        }
                    }
                }

                foreach (var syllable in line.PrimaryRenderSyllables)
                {
                    bool isSyllablePlaying = syllable.GetIsPlaying(currentPositionMs);
                    bool isSyllablePlayingChanged = syllable.IsPlayingLastFrame != isSyllablePlaying;

                    var desiredAnimationDurationMs = Math.Max((syllable.EndMs ?? 0) - currentPositionMs, 0);

                    if (isSyllablePlayingChanged)
                    {
                        // 缩放
                        if (isScaleEnabled && isSyllablePlaying)
                        {
                            foreach (var renderChar in syllable.ChildrenRenderLyricsChars)
                            {
                                if (syllable.DurationMs >= lyricsScaleEffectLongSyllableDuration)
                                {
                                    var (inDuration, outDuration) = CalculateSegmentDuration(desiredAnimationDurationMs / 1000.0, maxAnimationDurationMs / 1000.0);
                                    renderChar.ScaleTransition.Start(
                                        new BetterLyricKeyframe<double>(targetCharScale, inDuration),
                                        new BetterLyricKeyframe<double>(1.0, outDuration)
                                    );
                                }
                            }
                        }

                        // 辉光（长音节）
                        if (isGlowEnabled && isSyllablePlaying && lyricsGlowEffectScope == LyricEffectScope.LongDurationSyllable
                            && syllable.DurationMs >= lyricsGlowEffectLongSyllableDuration)
                        {
                            foreach (var renderChar in syllable.ChildrenRenderLyricsChars)
                            {
                                var (inDuration, outDuration) = CalculateSegmentDuration(desiredAnimationDurationMs / 1000.0, maxAnimationDurationMs / 1000.0);
                                renderChar.GlowTransition.Start(
                                    new BetterLyricKeyframe<double>(targetCharGlow, inDuration),
                                    new BetterLyricKeyframe<double>(0, outDuration)
                                );
                            }
                        }

                        syllable.IsPlayingLastFrame = isSyllablePlaying;
                    }
                }

                foreach (var renderChar in line.PrimaryRenderChars)
                {
                    renderChar.Update(elapsedTime);
                }
            }

            if (!autoWrap)
            {
                if (isSecondaryLinePlaying)
                {
                    // Держим X-скролл по прогрессу строки. playProgress пересчитывается
                    // каждый кадр выше, поэтому сброса в 0 при смене строки не требуется:
                    // для неиграющей строки прогресс и так 0.
                    line.PrimaryXOffsetTransition.JumpTo(CalculateTargetXOffset(lyricsAlignmentType, line.PrimaryTextLayout?.LayoutBounds.Width ?? 0, lyricsWidth, playProgress));
                    line.SecondaryXOffsetTransition.JumpTo(CalculateTargetXOffset(lyricsAlignmentType, line.SecondaryTextLayout?.LayoutBounds.Width ?? 0, lyricsWidth, playProgress));
                    line.TertiaryXOffsetTransition.JumpTo(CalculateTargetXOffset(lyricsAlignmentType, line.TertiaryTextLayout?.LayoutBounds.Width ?? 0, lyricsWidth, playProgress));
                }
                if (isSecondaryLinePlayingChanged)
                {
                    if (!isSecondaryLinePlaying)
                    {
                        line.PrimaryXOffsetTransition.Start(CalculateTargetXOffset(lyricsAlignmentType, line.PrimaryTextLayout?.LayoutBounds.Width ?? 0, lyricsWidth, 0));
                        line.SecondaryXOffsetTransition.Start(CalculateTargetXOffset(lyricsAlignmentType, line.SecondaryTextLayout?.LayoutBounds.Width ?? 0, lyricsWidth, 0));
                        line.TertiaryXOffsetTransition.Start(CalculateTargetXOffset(lyricsAlignmentType, line.TertiaryTextLayout?.LayoutBounds.Width ?? 0, lyricsWidth, 0));
                    }
                }
            }

            line.Update(elapsedTime);
        }
    }

    private double CalculateTargetOpacity(double baseOpacity, double baseOpacityWhenZeroDistanceFactor, double distanceFactor, bool isMouseScrolling)
    {
        double targetOpacity;
        if (distanceFactor == 0)
        {
            targetOpacity = baseOpacityWhenZeroDistanceFactor;
        }
        else
        {
            if (isMouseScrolling)
            {
                targetOpacity = baseOpacity;
            }
            else
            {
                if (isLyricsFadeOutEffectEnabled)
                {
                    targetOpacity = (1 - distanceFactor) * baseOpacity;
                }
                else
                {
                    targetOpacity = baseOpacity;
                }
            }
        }
        return targetOpacity;
    }

    private static double CalculateTargetXOffset(LyricsTextAlignmentType textAlignmentType, double actualWidth, double lyricsWidth, double progress)
    {
        var offset = textAlignmentType switch
        {
            LyricsTextAlignmentType.Center => (lyricsWidth - actualWidth) / 2,
            LyricsTextAlignmentType.Right => lyricsWidth - actualWidth,
            _ => 0,
        };
        offset = -Math.Min(0, offset);
        var progressStartToScroll = lyricsWidth * 0.5 / actualWidth;
        var progressEndToScroll = 1 - progressStartToScroll;
        return -Math.Max((Math.Min(progress, progressEndToScroll) - progressStartToScroll), 0) * actualWidth + offset;
    }

    private static (double InDuration, double OutDuration) CalculateSegmentDuration(double desiredDuration, double maxDuration)
    {
        // 缓入动画时长尽量接近 desiredDuration
        var inDuration = Math.Min(desiredDuration, maxDuration);
        // 缓出动画时长保证有合理的时长
        var outDuration = Math.Min(maxDuration - inDuration, BetterLyricTimeSpanHelper.AnimationDuration.TotalSeconds);
        //// 缓出动画时长过短时调整缓入动画时长以保证有合理的缓出动画
        if (outDuration == 0)
        {
            outDuration = inDuration / 3 * 1;
            inDuration = outDuration * 2;
        }
        return (inDuration, outDuration);
    }
}
