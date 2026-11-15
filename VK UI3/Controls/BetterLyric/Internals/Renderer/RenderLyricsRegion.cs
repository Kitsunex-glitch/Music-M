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

internal partial class RenderLyricsRegion : IDisposable
{
    public CanvasGradientStop[] FillStops { get; } = new CanvasGradientStop[4];
    public CanvasGradientStop[] StrokeStops { get; } = new CanvasGradientStop[4];

    public AlphaMaskEffect FinalFillEffect { get; }
    public AlphaMaskEffect? FinalStrokeEffect { get; }
    public CompositeEffect? CombinedEffect { get; }

    public RenderLyricsRegion(ICanvasImage cachedFill, ICanvasImage? cachedStroke)
    {
        FinalFillEffect = new AlphaMaskEffect { AlphaMask = cachedFill };

        if (cachedStroke != null)
        {
            FinalStrokeEffect = new AlphaMaskEffect { AlphaMask = cachedStroke };
            CombinedEffect = new CompositeEffect
            {
                Sources = { FinalStrokeEffect, FinalFillEffect },
                Mode = CanvasComposite.SourceOver
            };
        }
    }

    public void Dispose()
    {
        FinalFillEffect?.Dispose();
        FinalStrokeEffect?.Dispose();
        CombinedEffect?.Dispose();
    }
}
