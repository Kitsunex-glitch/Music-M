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

using Microsoft.UI.Text;
using Windows.UI.Text;

namespace VK_UI3.Controls.BetterLyric;

internal static partial class LyricsFontWeightExtensions
{
    public static FontWeight ToFontWeight(this LyricFontWeight weight)
    {
        return weight switch
        {
            LyricFontWeight.Thin => FontWeights.Thin,
            LyricFontWeight.ExtraLight => FontWeights.ExtraLight,
            LyricFontWeight.Light => FontWeights.Light,
            LyricFontWeight.SemiLight => FontWeights.SemiLight,
            LyricFontWeight.Normal => FontWeights.Normal,
            LyricFontWeight.Medium => FontWeights.Medium,
            LyricFontWeight.SemiBold => FontWeights.SemiBold,
            LyricFontWeight.Bold => FontWeights.Bold,
            LyricFontWeight.ExtraBold => FontWeights.ExtraBold,
            LyricFontWeight.Black => FontWeights.Black,
            LyricFontWeight.ExtraBlack => FontWeights.ExtraBlack,
            LyricFontWeight _ => throw new ArgumentOutOfRangeException(nameof(weight), weight, null),
        };
    }
}
