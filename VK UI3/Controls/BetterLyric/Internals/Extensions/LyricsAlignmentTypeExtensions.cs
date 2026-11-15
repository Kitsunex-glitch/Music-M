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

internal static partial class LyricsAlignmentTypeExtensions
{
    public static HorizontalAlignment ToHorizontalAlignment(this LyricsTextAlignmentType alignmentType)
    {
        return alignmentType switch
        {
            LyricsTextAlignmentType.Left => HorizontalAlignment.Left,
            LyricsTextAlignmentType.Center => HorizontalAlignment.Center,
            LyricsTextAlignmentType.Right => HorizontalAlignment.Right,
            _ => throw new ArgumentOutOfRangeException(nameof(alignmentType), alignmentType, null),
        };
    }

    public static CanvasHorizontalAlignment ToCanvasHorizontalAlignment(this LyricsTextAlignmentType alignmentType)
    {
        return alignmentType switch
        {
            LyricsTextAlignmentType.Left => CanvasHorizontalAlignment.Left,
            LyricsTextAlignmentType.Center => CanvasHorizontalAlignment.Center,
            LyricsTextAlignmentType.Right => CanvasHorizontalAlignment.Right,
            _ => throw new ArgumentOutOfRangeException(nameof(alignmentType), alignmentType, null),
        };
    }
}
