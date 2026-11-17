using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VNotch.Controllers;
using VNotch.Fork.Features.AppDock;
using VNotch.Services;
using static VNotch.Services.AnimationPrimitives;

namespace VNotch;

// faridfox fork: open-apps bar (Fork/README.md ledger), in the design of DeskNotch's apps bar: a slim near-black panel
// beside the open notch (Media page) with the open apps, most recently used first, two at a time. 32 px icons in 34 px
// slots; an icon floats up 2 px on hover and gives under the press; apps arrive with a short staggered rise; the wheel
// pages two by two; hovering shows the app's name in a small round pill beside the bar; a click brings that app to the
// front. Icons and names are the ones the taskbar uses (Store apps included: AppIdentity).
//
// The bar is a child of upstream's NotchWrapper with zero layout width (it draws beside the notch without moving
// anything): only elements under NotchWrapper receive clicks (MainWindow.IsPointInteractive), and the pointer stays
// "inside the notch" on its way to the bar, so upstream's hover-collapse does not close the notch. A click on the bar
// counts as outside the notch for upstream, which closes it after the app comes to the front.
// Fork-owned part of upstream's partial MainWindow; attached by Fork/Core/ForkUi.cs. No upstream file changes.
public partial class MainWindow
{
    internal const int ForkDockVisible = 2;
    // DeskNotch's apps bar: 34 px slots 8 px apart holding 32 px icons; panel padding 4 / 8 / 4 / 6; bottom corners 12.
    internal const double ForkDockIcon = 32;
    internal const double ForkDockCell = 34;
    private const double ForkDockSpacing = 8;
    private static readonly Thickness ForkDockPadding = new(4, 8, 4, 6);
    internal const double ForkDockPanelWidth = 42;
    private const double ForkDockCorner = 12;
    /// <summary>Gap between the notch and the bar, bridged by a nearly invisible strip so the pointer never leaves the notch.</summary>
    private const double ForkDockGap = 8;
    private const double ForkDockTipGap = 10;
    private static readonly TimeSpan ForkDockRefreshEvery = TimeSpan.FromSeconds(2);
    private static readonly Duration ForkDockPageSlide = new(TimeSpan.FromMilliseconds(420));
    // A spring's settle (DeskNotch: stiffness 420, damping 26 - a little overshoot).
    private static readonly IEasingFunction ForkDockSpring = ForkFrozenEase(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 });

    private Grid? _forkDockHost;
    private Border? _forkDockPill;
    private StackPanel? _forkDockList;
    private TranslateTransform? _forkDockListShift;
    private Border? _forkDockTip;
    private TextBlock? _forkDockTipText;
    private TranslateTransform? _forkDockTipShift;
    private UiScheduledTimer? _forkDockTimer;
    private bool _forkDockShown;
    private bool _forkDockListing;
    private Task _forkDockTask = Task.CompletedTask;
    private IReadOnlyList<DockApp> _forkDockApps = [];
    private int _forkDockPage;
    private double _forkDockWheel;

    // Test seams: the window list and the activation.
    internal Func<IReadOnlyList<DockApp>> ForkDockSource = AppDockWindows.List;
    internal Func<IntPtr, bool> ForkDockActivate = MediaWindowActivator.TryActivateWindow;

    internal Grid? ForkDockHost => _forkDockHost;
    internal bool ForkDockShown => _forkDockShown;
    internal IReadOnlyList<DockApp> ForkDockApps => _forkDockApps;
    internal int ForkDockPage => _forkDockPage;
    internal int ForkDockPageCount => Math.Max(1, (_forkDockApps.Count + ForkDockVisible - 1) / ForkDockVisible);
    internal StackPanel? ForkDockList => _forkDockList;
    internal string ForkDockTipText => _forkDockTip?.Visibility == Visibility.Visible ? _forkDockTipText?.Text ?? "" : "";

    private static double ForkDockPageStep => ForkDockVisible * (ForkDockCell + ForkDockSpacing);

    internal void ForkAttachAppDock()
    {
        if (_forkDockHost != null || _cleanedUp) return;

        _forkDockListShift = new TranslateTransform();
        _forkDockList = new StackPanel { Orientation = Orientation.Vertical, RenderTransform = _forkDockListShift, Width = ForkDockCell };
        // The list sits on a Canvas: inside a Grid it would be clipped to the visible two apps before it scrolls, and the
        // next pages would come up empty.
        var listLayer = new Canvas();
        listLayer.Children.Add(_forkDockList);
        var viewport = new Grid
        {
            Width = ForkDockCell,
            Height = ForkDockVisible * ForkDockCell + (ForkDockVisible - 1) * ForkDockSpacing,
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Top,
        };
        viewport.Children.Add(listLayer);
        _forkDockPill = new Border
        {
            Width = ForkDockPanelWidth,
            Padding = ForkDockPadding,
            Background = ForkFrozenBrush(Color.FromRgb(0x0B, 0x0B, 0x0D)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Child = viewport,
        };

        // The name pill, beside the bar (outside the host's own box; never takes the mouse).
        _forkDockTipText = new TextBlock
        {
            FontSize = 10.5,
            FontWeight = FontWeights.Medium,
            Foreground = ForkFrozenBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 180,
        };
        TextOptions.SetTextFormattingMode(_forkDockTipText, TextFormattingMode.Ideal);
        _forkDockTipShift = new TranslateTransform();
        _forkDockTip = new Border
        {
            Child = _forkDockTipText,
            Background = ForkFrozenBrush(Color.FromRgb(0x1C, 0x1C, 0x1F)),
            BorderBrush = ForkFrozenBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 3, 8, 3),
            IsHitTestVisible = false,
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            RenderTransform = _forkDockTipShift,
        };
        var tipLayer = new Canvas { IsHitTestVisible = false };
        Canvas.SetLeft(_forkDockTip, ForkDockGap + ForkDockPanelWidth + ForkDockTipGap);   // ForkPlaceDockForTabs moves it
        tipLayer.Children.Add(_forkDockTip);

        double hostWidth = ForkDockGap + ForkDockPanelWidth;
        _forkDockHost = new Grid
        {
            Width = hostWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            // Zero layout width: column 2 and the notch's centring stay exactly as upstream laid them out.
            Margin = new Thickness(0, 0, -hostWidth, 0),
            Background = ForkFrozenBrush(Color.FromArgb(0x01, 0x00, 0x00, 0x00)),
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _forkDockHost.Children.Add(_forkDockPill);
        _forkDockHost.Children.Add(tipLayer);
        Grid.SetColumn(_forkDockHost, 2);
        // The wheel always stays in the bar (upstream's wheel handler on the notch would switch pages).
        _forkDockHost.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            ForkDockWheel(e.Delta);
        };
        // A press that misses the icons must not reach the notch (it would close it or start a gesture).
        _forkDockHost.MouseLeftButtonDown += (_, e) => e.Handled = true;
        _forkDockHost.MouseLeave += (_, _) => ForkShowDockTip(null, null);
        NotchWrapper.Children.Add(_forkDockHost);

        _forkDockTimer = new UiScheduledTimer(DispatcherPriority.Background) { Interval = ForkDockRefreshEvery };
        _forkDockTimer.Tick += (_, _) => ForkRefreshDockAsync().SafeFireAndForget("FORK-APP-DOCK");
        _transitionCoordinator.StateChanged += ForkOnDockState;
        Closed += (_, _) =>
        {
            _transitionCoordinator.StateChanged -= ForkOnDockState;
            _forkDockTimer?.Stop();
        };
    }

    private void ForkOnDockState(object? sender, NotchTransitionSnapshot state)
    {
        if (Dispatcher.CheckAccess()) ForkUpdateDock(state);
        else Dispatcher.BeginInvoke(new Action(() => ForkUpdateDock(_transitionCoordinator.Snapshot)));
    }

    internal void ForkUpdateDock(NotchTransitionSnapshot state)
    {
        if (_forkDockHost == null || _forkDockPill == null || _forkDockTimer == null) return;
        bool show = !_cleanedUp && AppDockWindows.ShouldShow(state) && ForkDockFits();
        if (show == _forkDockShown) return;
        _forkDockShown = show;
        int fps = AnimationConfig.TargetFps;
        var host = _forkDockHost;
        if (show)
        {
            ForkPlaceDockForTabs();
            bool island = _settings.EnableDynamicIslandMode;
            _forkDockPill.CornerRadius = island
                ? new CornerRadius(ForkDockCorner)
                : new CornerRadius(0, 0, ForkDockCorner, ForkDockCorner);
            ForkSetDockPage(0, animate: false);
            ForkDockArrive(0);
            host.Visibility = Visibility.Visible;
            host.IsHitTestVisible = true;
            host.BeginAnimation(OpacityProperty, MakeAnim(host.Opacity, 1, new Duration(TimeSpan.FromMilliseconds(220)), _easeQuadOut, TimeSpan.FromMilliseconds(60)));
            ForkRefreshDockAsync().SafeFireAndForget("FORK-APP-DOCK");
            _forkDockTimer.Start();
        }
        else
        {
            _forkDockTimer.Stop();
            ForkShowDockTip(null, null);
            var fade = MakeAnim(0, new Duration(TimeSpan.FromMilliseconds(150)), _easePowerOut3, fps);
            // Still clickable while it fades: a click just as the notch starts closing must not fall through.
            fade.Completed += (_, _) =>
            {
                if (_forkDockShown) return;
                host.IsHitTestVisible = false;
                host.Visibility = Visibility.Collapsed;
            };
            host.BeginAnimation(OpacityProperty, fade);
        }
    }

    // Beside the notch inside the window (the window is wider than the Media page; never for the wide clipboard page).
    private bool ForkDockFits()
    {
        double free = (ActualWidth - NotchBorder.ActualWidth) / 2;
        double needed = (_forkDockHost?.Width ?? ForkDockGap + ForkDockPanelWidth) + 4;
        return !double.IsFinite(free) || ActualWidth <= 0 || free >= needed;
    }

    /// <summary>Moves the bar out past a tabs pill on the right of the notch (Fork/Features/NavTabs), if there is one.</summary>
    internal void ForkPlaceDockForTabs()
    {
        if (_forkDockHost == null || _forkDockTip == null) return;
        double width = ForkTabsRightReserve() + ForkDockGap + ForkDockPanelWidth;
        _forkDockHost.Width = width;
        _forkDockHost.Margin = new Thickness(0, 0, -width, 0);
        Canvas.SetLeft(_forkDockTip, width + ForkDockTipGap);
    }

    internal Task ForkRefreshDockAsync()
    {
        if (_cleanedUp) return Task.CompletedTask;
        if (_forkDockListing) return _forkDockTask;   // one listing at a time; callers wait for it
        _forkDockListing = true;
        return _forkDockTask = ForkListDockAsync();
    }

    private async Task ForkListDockAsync()
    {
        try
        {
            var source = ForkDockSource;
            var apps = await Task.Run(source);
            if (!SameApps(apps, _forkDockApps)) ForkShowDockApps(apps);
        }
        finally
        {
            _forkDockListing = false;
        }
    }

    private static bool SameApps(IReadOnlyList<DockApp> a, IReadOnlyList<DockApp> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i].Window != b[i].Window || a[i].Key != b[i].Key || a[i].Label != b[i].Label) return false;
        return true;
    }

    private void ForkShowDockApps(IReadOnlyList<DockApp> apps)
    {
        _forkDockApps = apps;
        var list = _forkDockList!;
        list.Children.Clear();
        for (int i = 0; i < apps.Count; i++)
            list.Children.Add(ForkDockCellFor(apps[i], last: i == apps.Count - 1));
        ForkSetDockPage(Math.Min(_forkDockPage, ForkDockPageCount - 1), animate: false);
    }

    private FrameworkElement ForkDockCellFor(DockApp app, bool last)
    {
        var image = new Image { Width = ForkDockIcon, Height = ForkDockIcon, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var press = new ScaleTransform(1, 1);
        var lift = new TranslateTransform();
        var motion = new TransformGroup();
        motion.Children.Add(press);
        motion.Children.Add(lift);
        var cell = new Border
        {
            Width = ForkDockCell,
            Height = ForkDockCell,
            Margin = new Thickness(0, 0, 0, last ? 0 : ForkDockSpacing),
            Background = ForkFrozenBrush(Color.FromArgb(0x01, 0x00, 0x00, 0x00)),
            Cursor = Cursors.Hand,
            Child = image,
            Tag = app,
            RenderTransform = motion,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        int fps = AnimationConfig.TargetFps;
        cell.MouseEnter += (_, _) =>
        {
            lift.BeginAnimation(TranslateTransform.YProperty, MakeAnim(-2, new Duration(TimeSpan.FromMilliseconds(260)), ForkDockSpring, fps));
            ForkShowDockTip(app, cell);
        };
        cell.MouseLeave += (_, _) =>
        {
            lift.BeginAnimation(TranslateTransform.YProperty, MakeAnim(0, new Duration(TimeSpan.FromMilliseconds(240)), ForkDockSpring, fps));
            var release = MakeAnim(1, new Duration(TimeSpan.FromMilliseconds(200)), ForkDockSpring, fps);
            press.BeginAnimation(ScaleTransform.ScaleXProperty, release);
            press.BeginAnimation(ScaleTransform.ScaleYProperty, release);
        };
        // On the press: upstream's outside-click handling closes the notch right after, so the release may never come.
        cell.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            var give = MakeAnim(0.88, new Duration(TimeSpan.FromMilliseconds(90)), _easeQuadOut, fps);
            press.BeginAnimation(ScaleTransform.ScaleXProperty, give);
            press.BeginAnimation(ScaleTransform.ScaleYProperty, give);
            ForkActivateDockApp(app);
        };
        ForkLoadDockIconAsync(image, app).SafeFireAndForget("FORK-APP-DOCK");
        return cell;
    }

    // DeskNotch's arrival: each visible app rises 6 px from 80 % size and fades in, one after the other.
    private void ForkDockArrive(int page)
    {
        if (_forkDockList == null || AnimationConfig.ReduceMotion) return;
        int fps = AnimationConfig.TargetFps;
        for (int i = 0; i < ForkDockVisible; i++)
        {
            int index = page * ForkDockVisible + i;
            if (index >= _forkDockList.Children.Count || _forkDockList.Children[index] is not Border cell) continue;
            if (cell.RenderTransform is not TransformGroup { Children: [ScaleTransform scale, TranslateTransform lift] }) continue;
            var delay = TimeSpan.FromMilliseconds(35 * i);
            var duration = new Duration(TimeSpan.FromMilliseconds(380));
            cell.BeginAnimation(OpacityProperty, MakeAnim(0, 1, new Duration(TimeSpan.FromMilliseconds(220)), _easeQuadOut, delay));
            var grow = MakeAnim(0.8, 1, duration, ForkDockSpring, delay);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            lift.BeginAnimation(TranslateTransform.YProperty, MakeAnim(6, 0, duration, ForkDockSpring, delay));
        }
    }

    // ---- paging: two apps at a time ----

    private void ForkDockWheel(int delta)
    {
        // A wheel notch is 120; touchpads send many small steps. One page per notch.
        _forkDockWheel += delta;
        if (Math.Abs(_forkDockWheel) < 120) return;
        int direction = _forkDockWheel < 0 ? 1 : -1;
        _forkDockWheel = 0;
        ForkSetDockPage(_forkDockPage + direction, animate: true);
    }

    internal void ForkSetDockPage(int page, bool animate)
    {
        page = Math.Clamp(page, 0, ForkDockPageCount - 1);
        bool changed = page != _forkDockPage;
        _forkDockPage = page;
        ForkShowDockTip(null, null);
        if (_forkDockListShift == null || _forkDockList == null) return;
        double target = -page * ForkDockPageStep;
        if (!animate || AnimationConfig.ReduceMotion)
        {
            _forkDockListShift.BeginAnimation(TranslateTransform.YProperty, null);
            _forkDockListShift.Y = target;
            return;
        }
        if (!changed) return;
        int fps = AnimationConfig.TargetFps;
        _forkDockListShift.BeginAnimation(TranslateTransform.YProperty, MakeAnim(target, ForkDockPageSlide, _easeExpOut6, fps));
        ForkDockArrive(page);
    }

    // ---- the name pill ----

    private void ForkShowDockTip(DockApp? app, FrameworkElement? cell)
    {
        if (_forkDockTip == null || _forkDockTipText == null || _forkDockTipShift == null || _forkDockHost == null) return;
        int fps = AnimationConfig.TargetFps;
        if (app == null || cell == null || app.Label.Length == 0)
        {
            if (_forkDockTip.Visibility != Visibility.Visible) return;
            var fade = MakeAnim(0, new Duration(TimeSpan.FromMilliseconds(80)), _easePowerOut3, fps);
            fade.Completed += (_, _) =>
            {
                if (_forkDockTip.Opacity < 0.01) _forkDockTip.Visibility = Visibility.Collapsed;
            };
            _forkDockTip.BeginAnimation(OpacityProperty, fade);
            return;
        }
        _forkDockTipText.Text = app.Label;
        _forkDockTip.CornerRadius = new CornerRadius(10);   // fully round at 10.5 px text
        _forkDockTip.Visibility = Visibility.Visible;
        _forkDockTip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double centre = cell.TranslatePoint(new Point(0, cell.ActualHeight / 2), _forkDockHost).Y;
        Canvas.SetTop(_forkDockTip, Math.Round(centre - _forkDockTip.DesiredSize.Height / 2));
        _forkDockTip.BeginAnimation(OpacityProperty, MakeAnim(1, new Duration(TimeSpan.FromMilliseconds(120)), _easeQuadOut, fps));
        _forkDockTipShift.BeginAnimation(TranslateTransform.XProperty, MakeAnim(-3, 0, new Duration(TimeSpan.FromMilliseconds(160)), _easeExpOut6, fps));
    }

    internal bool ForkActivateDockApp(DockApp app)
    {
        try { return ForkDockActivate(app.Window); }
        catch (Exception ex)
        {
            RuntimeLog.Warn("FORK-APP-DOCK", $"Could not switch to the window: {ex.Message}");
            return false;
        }
    }

    private static async Task ForkLoadDockIconAsync(Image image, DockApp app)
    {
        var icon = await AppIdentity.IconAsync(app);
        if (icon != null) image.Source = icon;
    }

    private static IEasingFunction ForkFrozenEase(EasingFunctionBase ease)
    {
        ease.Freeze();
        return ease;
    }
}
