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
    private bool isTranslationEnabled = true;
    private bool isPhoneticEnabled = true;

    public bool IsTranslationEnabled
    {
        get { return (bool)GetValue(IsTranslationEnabledProperty); }
        set { SetValue(IsTranslationEnabledProperty, value); }
    }

    public static readonly DependencyProperty IsTranslationEnabledProperty =
        DependencyProperty.Register(nameof(IsTranslationEnabled), typeof(bool), typeof(BetterLyric), new PropertyMetadata(true, OnIsTranslationEnabledChanged));
    private static void OnIsTranslationEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        ctl.isTranslationEnabled = (bool)e.NewValue;
    }

    public bool IsPhoneticEnabled
    {
        get { return (bool)GetValue(IsPhoneticEnabledProperty); }
        set { SetValue(IsPhoneticEnabledProperty, value); }
    }

    public static readonly DependencyProperty IsPhoneticEnabledProperty =
        DependencyProperty.Register(nameof(IsPhoneticEnabled), typeof(bool), typeof(BetterLyric), new PropertyMetadata(false, OnIsPhoneticEnabledChanged));

    private static void OnIsPhoneticEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (BetterLyric)d;
        ctl.isPhoneticEnabled = (bool)e.NewValue;
    }
}
