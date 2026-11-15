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
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.UI;
// https://github.com/jayfunc/BetterLyrics

using Microsoft.Graphics.Canvas.Text;

namespace VK_UI3.Controls.BetterLyric;

internal static partial class CanvasTextLayoutExtensions
{
    public static void SetFontFamily(this CanvasTextLayout canvasTextLayout, string text, string cjk, string latin)
    {
        if (canvasTextLayout == null) return;
        if (text == null) return;

        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            bool isCJK = c >= 0x3000 && c <= 0x9FFF   // CJK Unified Ideographs, Hiragana, Katakana
                      || c >= 0xF900 && c <= 0xFAFF    // CJK Compatibility Ideographs
                      || c >= 0xAC00 && c <= 0xD7AF;   // Hangul Syllables

            canvasTextLayout.SetFontFamily(i, 1, isCJK ? cjk : latin);
        }
    }
}
