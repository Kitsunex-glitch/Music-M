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
// https://github.com/jayfunc/BetterLyrics

using Windows.UI;

namespace VK_UI3.Controls.BetterLyric;

/// <summary>
/// Минимальная копия DevWinUI.Base.ColorHelper — используется только интерполяция цвета
/// в переходах ValueTransition<Color>.
/// </summary>
internal static class ColorHelper
{
    public static Color GetInterpolatedColor(double progress, Color startColor, Color targetColor)
    {
        byte Lerp(byte a, byte b) => (byte)(a + (progress * (b - a)));

        return Color.FromArgb(
            Lerp(startColor.A, targetColor.A),
            Lerp(startColor.R, targetColor.R),
            Lerp(startColor.G, targetColor.G),
            Lerp(startColor.B, targetColor.B)
        );
    }

    /// <summary>Копия DevWinUI.ColorHelper.WithAlpha — используется рендерером строк.</summary>
    public static Color WithAlpha(this Color color, byte a)
    {
        return Color.FromArgb(a, color.R, color.G, color.B);
    }

    /// <summary>Копия DevWinUI.ColorHelper.WithAlpha — доля 0..1.</summary>
    public static Color WithAlpha(this Color color, double a)
    {
        return Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), color.R, color.G, color.B);
    }
}
