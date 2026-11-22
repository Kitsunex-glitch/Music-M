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

internal partial class RenderLyricsChar : BaseRenderLyrics
{
    public Rect LayoutRect { get; private set; }

    public ValueTransition<double> ScaleTransition { get; set; }
    public ValueTransition<double> GlowTransition { get; set; }
    public ValueTransition<double> FloatTransition { get; set; }

    public CropEffect Crop { get; }
    public GaussianBlurEffect Glow { get; }

    /// <summary>Окрашивает размытие свечения в заданный пользователем цвет.</summary>
    public TintEffect GlowTint { get; }

    public double ProgressPlayed { get; set; } = 0; // 0~1

    public RenderLyricsChar(BaseLyric lyricsChars, Rect layoutRect) : base(lyricsChars)
    {
        ScaleTransition = new(
            initialValue: 1.0,
            AnimationEasingHelper.GetInterpolatorByEasingType<double>(AnimationEasingType.Sine),
            defaultTotalDuration: BetterLyricTimeSpanHelper.AnimationDuration.TotalSeconds
        );
        GlowTransition = new(
            initialValue: 0,
            AnimationEasingHelper.GetInterpolatorByEasingType<double>(AnimationEasingType.Sine),
            defaultTotalDuration: BetterLyricTimeSpanHelper.AnimationDuration.TotalSeconds
        );
        FloatTransition = new(
            initialValue: 0,
            AnimationEasingHelper.GetInterpolatorByEasingType<double>(AnimationEasingType.Sine),
            defaultTotalDuration: BetterLyricTimeSpanHelper.LongAnimationDuration.TotalSeconds
        );
        LayoutRect = layoutRect;
        Crop = new CropEffect { BorderMode = EffectBorderMode.Hard };
        Glow = new GaussianBlurEffect { Source = Crop, BorderMode = EffectBorderMode.Soft };
        GlowTint = new TintEffect { Source = Glow, Color = Colors.White };
    }

    public void Update(TimeSpan elapsedTime)
    {
        ScaleTransition.Update(elapsedTime);
        GlowTransition.Update(elapsedTime);
        FloatTransition.Update(elapsedTime);
    }

    public void DisposeEffects()
    {
        Crop?.Dispose();
        Glow?.Dispose();
        GlowTint?.Dispose();
    }

}
