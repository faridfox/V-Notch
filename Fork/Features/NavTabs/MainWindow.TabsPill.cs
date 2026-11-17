using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using VNotch.Fork;
using VNotch.Services;
using static VNotch.Services.AnimationPrimitives;

namespace VNotch;

// faridfox fork: tabs position and size (Fork/README.md ledger), after DeskNotch. Settings > Appearance can move the
// expanded notch's page tabs out of the notch into a pill below, left or right of it, in three sizes; the pill appears
// while the pointer is over the open notch.
//
// It is upstream's own tab row (NavIconsPanel) that moves, so switching, drag-to-reorder, the active-tab look and
// upstream's reveal animation keep working. The pill is a child of upstream's NotchWrapper with zero layout size
// (clicks reach it; the pointer stays "inside the notch" on its way there) and a nearly invisible bridge over the gap.
// Upstream closes the notch on a press outside NotchWrapper's own box: one marked line in
// IsScreenPointInsideNotchVisual (Windows/MainWindow.Progress.cs) asks ForkTabsPillContains first.
// Beside the notch the row is turned upright (rotated, each glyph turned back), which keeps upstream's drag maths valid.
// Fork-owned part of upstream's partial MainWindow; attached by Fork/Core/ForkUi.cs.
public partial class MainWindow
{
    internal const double ForkTabsGap = 8;
    private static readonly Thickness ForkTabsPadRow = new(8, 5, 4, 5);       // the last tab carries a 4 px right margin
    private static readonly Thickness ForkTabsPadColumn = new(5, 8, 5, 4);

    private ForkSettingsService? _forkTabsSettings;
    private Canvas? _forkTabsHost;
    private Border? _forkTabsPill;
    private Border? _forkTabsBridge;
    private Panel? _forkTabsHome;
    private int _forkTabsHomeIndex;
    private CacheMode? _forkTabsCache;
    private ForkTabsPosition _forkTabsPosition = ForkTabsPosition.Top;
    private ForkTabsSize _forkTabsSize = ForkTabsSize.Default;
    private bool _forkTabsShown;

    internal ForkTabsPosition ForkTabsPositionApplied => _forkTabsPosition;
    internal Border? ForkTabsPill => _forkTabsPill;
    internal bool ForkTabsPillShown => _forkTabsShown;

    internal static double ForkTabsScale(ForkTabsSize size) => size switch
    {
        ForkTabsSize.Large => 1.2,
        ForkTabsSize.Larger => 1.4,
        _ => 1.0,
    };

    internal void ForkAttachTabsPill(ForkSettingsService settings)
    {
        if (_forkTabsHost != null || _cleanedUp || NavIconsPanel.Parent is not Panel home) return;
        _forkTabsSettings = settings;
        _forkTabsHome = home;
        _forkTabsHomeIndex = home.Children.IndexOf(NavIconsPanel);
        _forkTabsCache = NavTabsStackPanel.CacheMode;

        _forkTabsPill = new Border
        {
            Background = ForkFrozenBrush(Color.FromArgb(0xF7, 0x00, 0x00, 0x00)),
            BorderBrush = ForkFrozenBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0.8),
            Padding = ForkTabsPadRow,
        };
        // Presses on the pill's padding must not reach the notch (upstream would close it).
        _forkTabsPill.MouseLeftButtonDown += (_, e) => e.Handled = true;
        _forkTabsPill.SizeChanged += (_, _) => ForkPlaceTabsPill();
        BindingOperations.SetBinding(_forkTabsPill, VisibilityProperty, new Binding(nameof(Visibility)) { Source = NavIconsPanel });
        _forkTabsBridge = new Border { Background = ForkFrozenBrush(Color.FromArgb(0x01, 0x00, 0x00, 0x00)) };
        _forkTabsHost = new Canvas
        {
            Width = 0,
            Height = 0,
            Opacity = 0,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        _forkTabsHost.Children.Add(_forkTabsBridge);
        _forkTabsHost.Children.Add(_forkTabsPill);
        NotchWrapper.Children.Add(_forkTabsHost);

        NotchWrapper.MouseEnter += (_, _) => ForkUpdateTabsHover();
        NotchWrapper.MouseLeave += (_, _) => ForkUpdateTabsHover();
        NotchBorder.SizeChanged += (_, _) => ForkPlaceTabsPill();
        NavIconsPanel.IsVisibleChanged += (_, _) => ForkUpdateTabsHover();
        SecondaryContent.IsVisibleChanged += (_, _) => ForkApplyClipboardHeader();
        settings.Changed += ForkOnTabsSettingsChanged;
        Closed += (_, _) => settings.Changed -= ForkOnTabsSettingsChanged;
        ForkApplyTabsSettings();
    }

    private void ForkOnTabsSettingsChanged()
    {
        if (Dispatcher.CheckAccess()) ForkApplyTabsSettings();
        else Dispatcher.BeginInvoke(new Action(ForkApplyTabsSettings));
    }

    internal void ForkApplyTabsSettings()
    {
        if (_forkTabsSettings == null || _forkTabsPill == null || _forkTabsHome == null || _forkTabsHost == null) return;
        var position = _forkTabsSettings.TabsPosition;
        var size = _forkTabsSettings.TabsSize;
        bool inPill = ReferenceEquals(NavIconsPanel.Parent, _forkTabsPill);

        if (position == ForkTabsPosition.Top)
        {
            if (inPill)
            {
                _forkTabsPill.Child = null;
                _forkTabsHome.Children.Insert(Math.Min(_forkTabsHomeIndex, _forkTabsHome.Children.Count), NavIconsPanel);
            }
            ForkOrientTabs(vertical: false);
            NavIconsPanel.Margin = new Thickness(0);
            NavTabsStackPanel.CacheMode = _forkTabsCache;
            _forkTabsPill.LayoutTransform = Transform.Identity;
        }
        else
        {
            if (!inPill)
            {
                _forkTabsHome.Children.Remove(NavIconsPanel);
                _forkTabsPill.Child = NavIconsPanel;
            }
            bool vertical = position is ForkTabsPosition.Left or ForkTabsPosition.Right;
            ForkOrientTabs(vertical);
            // Upstream re-applies the row's own 14,3 margin on every page change and slides it to 5 px in island mode;
            // cancel both, so the pill's padding alone decides the spacing.
            double restY = _settings.EnableDynamicIslandMode ? 5 : 0;
            NavIconsPanel.Margin = new Thickness(-14, -3 - restY, 0, 0);
            _forkTabsPill.Padding = vertical ? ForkTabsPadColumn : ForkTabsPadRow;
            double scale = ForkTabsScale(size);
            _forkTabsPill.LayoutTransform = scale == 1 ? Transform.Identity : new ScaleTransform(scale, scale);
            // Upstream caches the row as a bitmap at 1x; at a larger size that would blur.
            NavTabsStackPanel.CacheMode = scale == 1 ? _forkTabsCache : new BitmapCache(scale) { SnapsToDevicePixels = true };
        }

        _forkTabsPosition = position;
        _forkTabsSize = size;
        ForkApplyClipboardHeader();
        ForkPlaceTabsPill();
        ForkUpdateTabsHover();
        ForkPlaceDockForTabs();
    }

    // Beside the notch: the row turned upright. The whole row is rotated (upstream's drag-to-reorder keeps measuring
    // along its own axis), and each tab's glyph is turned back.
    private void ForkOrientTabs(bool vertical)
    {
        NavTabsStackPanel.LayoutTransform = vertical ? new RotateTransform(90) : Transform.Identity;
        foreach (var child in NavTabsStackPanel.Children)
            if (child is Border { Child: FrameworkElement glyph })
                glyph.LayoutTransform = vertical ? new RotateTransform(-90) : Transform.Identity;
    }

    // The clipboard page leaves room in its header for the tabs; none is needed while they are in the pill.
    private void ForkApplyClipboardHeader()
    {
        if (_clipboardTrayView is not { } tray) return;
        if (_forkTabsPosition == ForkTabsPosition.Top)
        {
            if (BindingOperations.GetBindingExpression(tray, VNotch.Controls.ClipboardTray.NavigationWidthProperty) == null)
                tray.SetBinding(VNotch.Controls.ClipboardTray.NavigationWidthProperty,
                    new Binding(nameof(ActualWidth)) { Source = NavTabsStackPanel });
        }
        else
        {
            BindingOperations.ClearBinding(tray, VNotch.Controls.ClipboardTray.NavigationWidthProperty);
            tray.NavigationWidth = 0;
        }
    }

    private void ForkPlaceTabsPill()
    {
        if (_forkTabsHost == null || _forkTabsPill == null || _forkTabsBridge == null) return;
        if (_forkTabsPosition == ForkTabsPosition.Top) return;
        _forkTabsPill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = _forkTabsPill.DesiredSize.Width, height = _forkTabsPill.DesiredSize.Height;
        double notchHeight = NotchBorder.ActualHeight > 0 ? NotchBorder.ActualHeight : NotchBorder.Height;
        _forkTabsPill.CornerRadius = new CornerRadius(Math.Min(width, height) / 2 / ForkTabsScale(_forkTabsSize));

        double left, top, bridgeLeft, bridgeTop, bridgeWidth, bridgeHeight;
        switch (_forkTabsPosition)
        {
            case ForkTabsPosition.Bottom:
                Grid.SetColumn(_forkTabsHost, 1);
                _forkTabsHost.HorizontalAlignment = HorizontalAlignment.Center;
                _forkTabsHost.VerticalAlignment = VerticalAlignment.Bottom;
                left = -width / 2; top = ForkTabsGap;
                bridgeLeft = left; bridgeTop = 0; bridgeWidth = width; bridgeHeight = ForkTabsGap;
                break;
            case ForkTabsPosition.Left:
                Grid.SetColumn(_forkTabsHost, 0);
                _forkTabsHost.HorizontalAlignment = HorizontalAlignment.Left;
                _forkTabsHost.VerticalAlignment = VerticalAlignment.Top;
                left = -(ForkTabsGap + width); top = Math.Max(0, (notchHeight - height) / 2);
                bridgeLeft = -ForkTabsGap; bridgeTop = top; bridgeWidth = ForkTabsGap; bridgeHeight = height;
                break;
            default:
                Grid.SetColumn(_forkTabsHost, 2);
                _forkTabsHost.HorizontalAlignment = HorizontalAlignment.Left;
                _forkTabsHost.VerticalAlignment = VerticalAlignment.Top;
                left = ForkTabsGap; top = Math.Max(0, (notchHeight - height) / 2);
                bridgeLeft = 0; bridgeTop = top; bridgeWidth = ForkTabsGap; bridgeHeight = height;
                break;
        }
        Canvas.SetLeft(_forkTabsPill, left);
        Canvas.SetTop(_forkTabsPill, top);
        Canvas.SetLeft(_forkTabsBridge, bridgeLeft);
        Canvas.SetTop(_forkTabsBridge, bridgeTop);
        _forkTabsBridge.Width = bridgeWidth;
        _forkTabsBridge.Height = bridgeHeight;
    }

    /// <summary>Shows the pill while the pointer is over the open notch (or the pill itself).</summary>
    internal void ForkUpdateTabsHover()
    {
        if (_forkTabsHost == null) return;
        bool show = _forkTabsPosition != ForkTabsPosition.Top && !_cleanedUp &&
                    NavIconsPanel.Visibility == Visibility.Visible && NotchWrapper.IsMouseOver;
        if (show == _forkTabsShown) return;
        _forkTabsShown = show;
        var host = _forkTabsHost;
        int fps = AnimationConfig.TargetFps;
        if (show)
        {
            ForkPlaceTabsPill();
            host.Visibility = Visibility.Visible;
            host.IsHitTestVisible = true;
            host.BeginAnimation(OpacityProperty, MakeAnim(1, new Duration(TimeSpan.FromMilliseconds(200)), _easeQuadOut, fps));
        }
        else
        {
            var fade = MakeAnim(0, new Duration(TimeSpan.FromMilliseconds(160)), _easePowerOut3, fps);
            fade.Completed += (_, _) =>
            {
                if (_forkTabsShown) return;
                host.IsHitTestVisible = false;
                host.Visibility = Visibility.Collapsed;
            };
            host.BeginAnimation(OpacityProperty, fade);
        }
    }

    /// <summary>
    /// Called first by upstream's IsScreenPointInsideNotchVisual: a press or the pointer on the visible pill counts as
    /// inside the notch, so upstream neither closes the notch nor treats the pointer as gone.
    /// </summary>
    internal bool ForkTabsPillContains(InputMonitorService.POINT point)
    {
        if (!_forkTabsShown || _forkTabsPill is not { IsVisible: true } pill || PresentationSource.FromVisual(pill) == null) return false;
        try
        {
            var a = pill.PointToScreen(new Point(0, 0));
            var b = pill.PointToScreen(new Point(pill.ActualWidth, pill.ActualHeight));
            const double pad = 6;
            return point.x >= Math.Min(a.X, b.X) - pad && point.x <= Math.Max(a.X, b.X) + pad &&
                   point.y >= Math.Min(a.Y, b.Y) - pad && point.y <= Math.Max(a.Y, b.Y) + pad;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Room the apps bar leaves for a tabs pill on the right of the notch (DIPs).</summary>
    internal double ForkTabsRightReserve()
    {
        if (_forkTabsPosition != ForkTabsPosition.Right || _forkTabsPill == null) return 0;
        _forkTabsPill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _forkTabsPill.DesiredSize.Width + ForkTabsGap;
    }
}
