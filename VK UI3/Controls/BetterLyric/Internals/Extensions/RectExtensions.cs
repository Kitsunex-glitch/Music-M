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
using Windows.UI;
// https://github.com/jayfunc/BetterLyrics

using Windows.Foundation;

namespace VK_UI3.Controls.BetterLyric;

internal static partial class RectExtensions
{
    public static Rect AddX(this Rect rect, double x) => new(
        rect.X + x,
        rect.Y,
        rect.Width,
        rect.Height
    );

    public static Rect AddY(this Rect rect, double y) => new(
        rect.X,
        rect.Y + y,
        rect.Width,
        rect.Height
    );

    public static Rect Extend(this Rect rect, double left, double top, double right, double bottom) => new(
        rect.X - left,
        rect.Y - top,
        rect.Width + left + right,
        rect.Height + top + bottom
    );

    public static Rect Extend(this Rect rect, double padding) => rect.Extend(padding, padding, padding, padding);

    public static Rect Extend(this Rect rect, double horizontalPadding, double verticalPadding)
        => rect.Extend(horizontalPadding, verticalPadding, horizontalPadding, verticalPadding);

    public static Rect Scale(this Rect rect, double scale)
    {
        double originalWidth = rect.Width;
        double originalHeight = rect.Height;

        double scaledWidth = originalWidth * scale;
        double scaledHeight = originalHeight * scale;

        double scaleOffsetX = (scaledWidth - originalWidth) / 2;
        double scaleOffsetY = (scaledHeight - originalHeight) / 2;

        return new Rect(
            rect.X - scaleOffsetX,
            rect.Y - scaleOffsetY,
            scaledWidth,
            scaledHeight
        );
    }
}
