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

internal partial class LyricsRenderer : BreathingRendererBase, IDisposable
{
    private Matrix4x4 _threeDimMatrix = Matrix4x4.Identity;
    private EdgeFadeMaskRenderer _edgeFadeMaskRenderer = new();

    public int MouseHoverLineIndex { get; set; } = -1;
    public bool IsMousePressing { get; set; } = false;

    public int StartVisibleLineIndex { get; set; } = 0;
    public int EndVisibleLineIndex { get; set; } = 0;

    public double UserScrollOffset { get; set; } = 0;

    public double LyricsX { get; set; }
    public double LyricsY { get; set; }

    public double LyricsWidth { get; set; }

    public double LyricsHeight { get; set; }

    public bool IsLyricsVisible { get; set; } = true;
    public double PlayingLineTopOffsetFactor { get; set; }

    public double CurrentProgressMs { get; set; }

    /// <summary>
    /// Индекс строки, к которой ведётся полотно, но которая ещё не начала звучать
    /// (время в разрыве). Для неё подсветка «играющей» строки не применяется.
    /// </summary>
    public int PendingLineIndex { get; set; } = -1;

    public LyricsRenderConfig RenderConfig { get; set; }

    public IList<RenderLyricsLine>? RenderLyricsLines { get; set; }

    /// <summary>
    /// Рисует текст и возвращает количество фактически отрисованных строк
    /// (для диагностики «пустых кадров»).
    /// </summary>
    public int Draw(ICanvasResourceCreator control, CanvasDrawingSession ds)
    {
        if (IsLyricsVisible == false)
        {
            BetterLyricDebugLog.WriteThrottled(
                BetterLyricLogCategory.Draw,
                "Draw: пропуск — IsLyricsVisible=false");
            return 0;
        }

        if (RenderConfig == null)
        {
            BetterLyricDebugLog.WriteThrottled(
                BetterLyricLogCategory.Draw,
                "Draw: пропуск — RenderConfig=null");
            return 0;
        }

        if (RenderConfig.Is3DLyricsEnabled)
        {
            using var layer = new CanvasCommandList(control);
            int layerDrawn;
            using (var layerDs = layer.CreateDrawingSession())
            {
                layerDrawn = DrawLyricsWithEdgeFadeHandled(control, layerDs);
            }

            ds.DrawImage(new Transform3DEffect
            {
                Source = layer,
                TransformMatrix = _threeDimMatrix
            });

            return layerDrawn;
        }

        return DrawLyricsWithEdgeFadeHandled(control, ds);
    }

    private int DrawLyricsWithEdgeFadeHandled(ICanvasResourceCreator control, CanvasDrawingSession ds)
    {
        if (RenderConfig == null) return 0;
        if (_edgeFadeMaskRenderer.Brush != null && !RenderConfig.AutoWrap)
        {
            using (ds.CreateLayer(_edgeFadeMaskRenderer.Brush))
            {
                return DrawLyrics(control, ds);
            }
        }

        return DrawLyrics(control, ds);
    }

    private int DrawLyrics(ICanvasResourceCreator control, CanvasDrawingSession ds)
    {
        if (RenderLyricsLines == null)
        {
            BetterLyricDebugLog.WriteThrottled(
                BetterLyricLogCategory.Draw,
                "DrawLyrics: пропуск — RenderLyricsLines=null");
            return 0;
        }

        if (RenderConfig == null) return 0;

        var isBreathingEnabled = RenderConfig.IsLyricsBrethingEffectEnabled;

        var rotationX = RenderConfig.FanLyricsAngle < 0 ? LyricsWidth : 0;
        rotationX += LyricsWidth / 2 * (RenderConfig.FanLyricsAngle < 0 ? 1 : -1);

        var yOffsetBase = UserScrollOffset + LyricsY + LyricsHeight * PlayingLineTopOffsetFactor;

        int drawnCount = 0;
        int skippedNullLayout = 0;
        int skippedZeroWidth = 0;
        int skippedCaches = 0;

        for (int i = StartVisibleLineIndex; i <= EndVisibleLineIndex; i++)
        {
            if (i < 0 || i >= RenderLyricsLines.Count) continue;
            var line = RenderLyricsLines[i];

            if (line == null) continue;
            if (line.PrimaryTextLayout == null) { skippedNullLayout++; continue; }
            if (line.PrimaryTextLayout.LayoutBounds.Width <= 0) { skippedZeroWidth++; continue; }

            double xOffset = LyricsX;
            double yOffset = line.YOffsetTransition.Value + yOffsetBase;

            // Для строки, к которой полотно только едет (время в разрыве),
            // подсветка не включается — она ещё не звучит.
            bool isPlaying = line.GetIsPlaying(CurrentProgressMs) && i != PendingLineIndex;

            LyricsLineRenderer lineRenderer = new()
            {
                IsPlaying = isPlaying,
                StrokeWidth = RenderConfig.LyricsFontStrokeWidth,
                CurrentProgressMs = CurrentProgressMs,
                LyricsWidth = LyricsWidth,
                LyricsHeight = LyricsHeight,
                Line = line,
                AutoWrap = RenderConfig.AutoWrap,
                IsLyricsFloatAnimationEnabled = RenderConfig.IsLyricsFloatAnimationEnabled,
                IsLyricsGlowEffectEnabled = RenderConfig.IsLyricsGlowEffectEnabled,
                LyricsGlowColor = RenderConfig.LyricsGlowColor,
                IsLyricsScaleEffectEnabled = RenderConfig.IsLyricsScaleEffectEnabled,
                WordByWordEffectMode = RenderConfig.WordByWordEffectMode,
                IsRightToLeft = RenderConfig.IsRightToLeftLyric
            };

            if (isPlaying)
            {
                ApplyBreathingTransform(ds, line.CenterPosition, isBreathingEnabled);
            }

            ds.Transform *= Matrix3x2.CreateScale((float)line.ScaleTransition.Value, line.CenterPosition);

            if (RenderConfig.IsFanLyricsEnabled)
            {
                xOffset += Math.Abs(line.AngleTransition.Value) / (Math.PI / 2) * LyricsWidth / 2 * (RenderConfig.FanLyricsAngle < 0 ? 1 : -1);
                var rotationY = line.CenterPosition.Y;
                ds.Transform *= Matrix3x2.CreateRotation((float)line.AngleTransition.Value, new Vector2((float)rotationX, rotationY));
            }

            ds.Transform *= Matrix3x2.CreateTranslation((float)xOffset, (float)yOffset);

            line.EnsureCaches(control, RenderConfig.LyricsFontStrokeWidth);
            if (line.CachedStroke == null || line.CachedFill == null) { skippedCaches++; ds.Transform = Matrix3x2.Identity; continue; }
            if (line.UnplayedFillTint == null || line.UnplayedStrokeTint == null || line.UnplayedComposite == null) { skippedCaches++; ds.Transform = Matrix3x2.Identity; continue; }

            line.UnplayedFillTint.Color = line.UnplayedFillColorTransition.Value;
            line.UnplayedStrokeTint.Color = line.UnplayedStrokeColorTransition.Value;

            lineRenderer.Draw(control, ds);
            drawnCount++;

            if (i == MouseHoverLineIndex)
            {
                byte opacity = IsMousePressing ? (byte)32 : (byte)16;
                double scale = IsMousePressing ? 1.09 : 1.10;
                ds.FillRoundedRectangle(
                    new Windows.Foundation.Rect(line.TopLeftPosition.ToPoint(), line.BottomRightPosition.ToPoint()).Scale(scale),
                    8, 8, Color.FromArgb(opacity, 255, 255, 255));
            }

            ds.Transform = Matrix3x2.Identity;
        }

        if (drawnCount == 0 && BetterLyricDebugLog.IsEnabled(BetterLyricLogCategory.Draw))
        {
            BetterLyricDebugLog.WriteAlways(
                BetterLyricLogCategory.Draw,
                $"DrawLyrics: 0 строк. range={StartVisibleLineIndex}..{EndVisibleLineIndex}, " +
                $"nullLayout={skippedNullLayout}, zeroWidth={skippedZeroWidth}, noCaches={skippedCaches}, " +
                $"yOffsetBase={yOffsetBase:0.##} (user={UserScrollOffset:0.##}, y={LyricsY:0.##}, h={LyricsHeight:0.##}, factor={PlayingLineTopOffsetFactor:0.##}), " +
                $"progress={CurrentProgressMs:0} мс");
        }

        return drawnCount;
    }

    public void CalculateLyrics3DMatrix(bool isLayoutChanged)
    {
        if (!isLayoutChanged) return;

        if (RenderConfig == null) return;

        if (!RenderConfig.Is3DLyricsEnabled) return;

        var playingLineTopOffsetFactor = RenderConfig.PlayingLineTopOffset / 100.0;

        Vector3 center = new(
            (float)(LyricsX + LyricsWidth / 2),
            (float)(LyricsY + LyricsHeight * playingLineTopOffsetFactor),
            0);

        float rotationX = (float)(Math.PI * RenderConfig.Lyrics3DXAngle / 180.0);
        float rotationY = (float)(Math.PI * RenderConfig.Lyrics3DYAngle / 180.0);
        float rotationZ = (float)(Math.PI * RenderConfig.Lyrics3DZAngle / 180.0);

        Matrix4x4 rotation =
            Matrix4x4.CreateRotationX(rotationX) *
            Matrix4x4.CreateRotationY(rotationY) *
            Matrix4x4.CreateRotationZ(rotationZ);
        Matrix4x4 perspective = Matrix4x4.Identity;
        perspective.M34 = 1.0f / RenderConfig.Lyrics3DDepth;

        // 组合变换：
        // 1. 将中心移到原点
        // 2. 旋转
        // 3. 应用透视
        // 4. 将中心移回原位
        _threeDimMatrix =
            Matrix4x4.CreateTranslation(-center) *
            rotation *
            perspective *
            Matrix4x4.CreateTranslation(center);
    }

    public void Update(ICanvasResourceCreator sender, float bassEnergy, int breathingIntensity)
    {
        base.UpdateBreathing(bassEnergy, breathingIntensity);
        switch (RenderConfig?.LyricsLineContentOrientation)
        {
            case LyricsContentOrientation.Horizontal:
                var stops = new CanvasGradientStop[]
                {
                    new() { Position = 0.00f, Color = Colors.Transparent },

                    new() { Position = 0.05f, Color = Colors.White },
                    new() { Position = 0.45f, Color = Colors.White },

                    new() { Position = 0.50f, Color = Colors.Transparent },

                    new() { Position = 0.55f, Color = Colors.White },
                    new() { Position = 0.95f, Color = Colors.White },

                    new() { Position = 1.00f, Color = Colors.Transparent }
                };
                _edgeFadeMaskRenderer.Update(
                    sender,
                    new Windows.Foundation.Rect(LyricsX - 16, LyricsY, LyricsWidth + 32, LyricsHeight),
                    stops,
                    false
                );
                break;
            case LyricsContentOrientation.Vertical:
                _edgeFadeMaskRenderer.Update(
                    sender,
                    new Windows.Foundation.Rect(LyricsX - 16, LyricsY, LyricsWidth + 32, LyricsHeight),
                    16, 0, 16, 0
                );
                break;
            default:
                break;
        }
    }

    public void Dispose()
    {
        _edgeFadeMaskRenderer.Dispose();
    }

}
