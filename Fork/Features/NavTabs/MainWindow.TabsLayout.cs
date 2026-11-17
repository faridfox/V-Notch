using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VNotch.Fork;
using VNotch.Services;
using static VNotch.Services.AnimationPrimitives;

namespace VNotch;

// faridfox fork: the open notch without its top row (Fork/README.md ledger, tabs-position). While the tabs sit in the
// pill outside the notch, the open notch's top row would only hold the battery and the settings gear: the gear moves
// into the pill, the battery is hidden, and the open notch gets shorter by that row.
//
// The height goes through one marked line in upstream's _expandedHeight getter (Windows/MainWindow.xaml.cs), which every
// user of the open-notch height reads (Media page and its lyrics, greeting). Pages with a height of their own
// (clipboard, timer, camera, mixer) do not change. The battery and gear are hidden through their hosts' opacity and
// hit testing, which upstream never sets (it only moves the button between hosts and animates the button itself).
// Fork-owned part of upstream's partial MainWindow; applied from ForkApplyTabsSettings (MainWindow.TabsPill.cs).
public partial class MainWindow
{
    /// <summary>How much shorter the open notch is while the tabs are outside it (DIPs).</summary>
    internal const double ForkTabsOutsideTrim = 10;
    /// <summary>Space above the media card's content once the top row is gone (upstream: 12).</summary>
    internal const double ForkMediaTopPadOutside = 4;
    private const double ForkGearRestOpacity = 0.45;   // as upstream's inactive tabs

    private Thickness? _forkMediaPadding;
    private double _forkLyricsHeight = double.NaN;
    private Border? _forkTabsGear;
    private RotateTransform? _forkTabsGearRotate;

    internal Border? ForkTabsGear => _forkTabsGear;

    /// <summary>Read by upstream's _expandedHeight getter: the open notch is shorter while the tabs are outside it.</summary>
    internal double ForkExpandedHeightTrim() =>
        _forkTabsHost != null && !_cleanedUp && _forkTabsPosition != ForkTabsPosition.Top ? ForkTabsOutsideTrim : 0;

    private void ForkApplyTabsLayout()
    {
        bool outside = ForkExpandedHeightTrim() > 0;
        _forkMediaPadding ??= MediaWidget.Padding;
        if (double.IsNaN(_forkLyricsHeight)) _forkLyricsHeight = LyricsWidget.Height;

        var pad = _forkMediaPadding.Value;
        MediaWidget.Padding = outside ? new Thickness(pad.Left, Math.Min(pad.Top, ForkMediaTopPadOutside), pad.Right, pad.Bottom) : pad;
        LyricsWidget.Height = outside ? _forkLyricsHeight - ForkTabsOutsideTrim : _forkLyricsHeight;
        foreach (var host in new FrameworkElement[] { SharedSettingsHost, ExpandedStatusBarHost })
        {
            host.Opacity = outside ? 0 : 1;
            host.IsHitTestVisible = !outside;
            KeyboardNavigation.SetTabNavigation(host, outside ? KeyboardNavigationMode.None : KeyboardNavigationMode.Continue);
        }
        // The new height applies from the next opening (settings are changed while the notch is closed).
        if (_forkTabsGear != null) _forkTabsGear.Visibility = outside ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The settings gear at the end of the tabs pill (upstream's own gear sits in the hidden top row).</summary>
    private Border ForkCreateTabsGear()
    {
        Geometry? shape = null;
        if (SettingsButton.Child is Viewbox { Child: Canvas canvas })
            foreach (var child in canvas.Children)
                if (child is Path { Data: { } data }) { shape = data.CloneCurrentValue(); break; }
        shape ??= Geometry.Parse("M 28,4 A 24,24 0 1 1 27.9,4 Z M 28,18 A 10,10 0 1 0 28.1,18 Z");
        if (shape.CanFreeze) shape.Freeze();

        _forkTabsGearRotate = new RotateTransform(45);
        var glyph = new Viewbox
        {
            Width = 15,
            Height = 15,
            Stretch = Stretch.Uniform,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _forkTabsGearRotate,
            Child = new Canvas
            {
                Width = 56,
                Height = 56,
                Children = { new Path { Data = shape, Fill = UiPalette.PrimaryBrush } },
            },
        };
        var button = new Border
        {
            Width = 22,
            Height = 22,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Opacity = ForkGearRestOpacity,
            Child = glyph,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, System.Windows.Automation.AutomationProperties.GetName(SettingsButton));
        button.MouseEnter += (_, _) => ForkAnimateTabsGear(true);
        button.MouseLeave += (_, _) => ForkAnimateTabsGear(false);
        button.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            OpenAppSettings();
        };
        // A hairline divides the gear from the tabs (left of it in a row, above it in a column).
        var gear = new Border
        {
            BorderBrush = ForkFrozenBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(4, 0, 0, 0),
            Margin = new Thickness(2, 3, 0, 3),
            Child = button,
            Visibility = Visibility.Collapsed,
        };
        // Fades in and out with upstream's reveal of the tab row.
        BindingOperations.SetBinding(gear, OpacityProperty, new Binding(nameof(Opacity)) { Source = NavIconsPanel });
        return gear;
    }

    // The gear's place in a row (bottom) or a column (left, right).
    private void ForkOrientTabsGear(bool vertical)
    {
        if (_forkTabsGear == null) return;
        _forkTabsGear.BorderThickness = vertical ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
        _forkTabsGear.Padding = vertical ? new Thickness(0, 4, 0, 0) : new Thickness(4, 0, 0, 0);
        _forkTabsGear.Margin = vertical ? new Thickness(3, 2, 3, 0) : new Thickness(2, 3, 0, 3);
    }

    private void ForkAnimateTabsGear(bool hover)
    {
        if (_forkTabsGear?.Child is not Border button || _forkTabsGearRotate == null) return;
        int fps = AnimationConfig.TargetFps;
        button.BeginAnimation(OpacityProperty,
            MakeAnim(hover ? 1 : ForkGearRestOpacity, new Duration(TimeSpan.FromMilliseconds(hover ? 180 : 260)), _easeQuadOut, fps));
        _forkTabsGearRotate.BeginAnimation(RotateTransform.AngleProperty,
            MakeAnim(hover ? 105 : 45, new Duration(TimeSpan.FromMilliseconds(hover ? 520 : 360)), _easeExpOut5, fps));
    }
}
