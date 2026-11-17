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

// faridfox fork: open-apps bar (Fork/README.md ledger), after DeskNotch's apps bar. While the notch is open on its Media
// page, a slim black panel beside it shows the open apps, most recently used first, two at a time: the mouse wheel
// pages through them two by two with a slide, hovering an icon shows the app's name in a pill beside it, and a click
// brings that app to the front. Icons and names are the ones the taskbar uses (Store apps included: AppIdentity).
//
// The bar is a child of upstream's NotchWrapper with zero layout width (it draws beside the notch without moving
// anything): only elements under NotchWrapper receive clicks (MainWindow.IsPointInteractive), and the pointer stays
// "inside the notch" on its way to the bar, so upstream's hover-collapse does not close the notch. A click on the bar
// counts as outside the notch for upstream, which closes it after the app comes to the front.
// Fork-owned part of upstream's partial MainWindow; attached by Fork/Core/ForkUi.cs. No upstream file changes.
public partial class MainWindow
{
    internal const int ForkDockVisible = 2;
    private const double ForkDockIcon = 40;
    private const double ForkDockCell = 44;
    private const double ForkDockSpacing = 6;
    private const double ForkDockPadding = 7;
    private const double ForkDockPanelWidth = 54;
    /// <summary>Gap between the notch and the bar, bridged by a nearly invisible strip so the pointer never leaves the notch.</summary>
    private const double ForkDockGap = 8;
    private static readonly TimeSpan ForkDockRefreshEvery = TimeSpan.FromSeconds(2);
    private static readonly Duration ForkDockPageSlide = new(TimeSpan.FromMilliseconds(460));

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
        _forkDockList = new StackPanel { Orientation = Orientation.Vertical, RenderTransform = _forkDockListShift };
        var viewport = new Grid
        {
            Height = ForkDockVisible * ForkDockCell + (ForkDockVisible - 1) * ForkDockSpacing,
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Top,
        };
        viewport.Children.Add(_forkDockList);
        _forkDockPill = new Border
        {
            Width = ForkDockPanelWidth,
            Padding = new Thickness(0, ForkDockPadding, 0, ForkDockPadding),
            Background = ForkFrozenBrush(Color.FromArgb(0xF7, 0x00, 0x00, 0x00)),
            BorderBrush = ForkFrozenBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0.8, 0, 0.8, 0.8),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Child = viewport,
        };

        // The name pill, beside the bar (outside the host's own box; never takes the mouse).
        _forkDockTipText = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = ForkFrozenBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 180,
        };
        TextOptions.SetTextFormattingMode(_forkDockTipText, TextFormattingMode.Ideal);
        _forkDockTipShift = new TranslateTransform();
        _forkDockTip = new Border
        {
            Child = _forkDockTipText,
            Background = ForkFrozenBrush(Color.FromArgb(0xF2, 0x24, 0x24, 0x26)),
            BorderBrush = ForkFrozenBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0.8),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9, 4, 9, 5),
            IsHitTestVisible = false,
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            RenderTransform = _forkDockTipShift,
        };
        var tipLayer = new Canvas { IsHitTestVisible = false };
        Canvas.SetLeft(_forkDockTip, ForkDockGap + ForkDockPanelWidth + 6);   // ForkPlaceDockForTabs moves it
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
            _forkDockPill.CornerRadius = island ? new CornerRadius(14) : new CornerRadius(0, 0, 14, 14);
            _forkDockPill.BorderThickness = island ? new Thickness(0.8) : new Thickness(0.8, 0, 0.8, 0.8);
            ForkSetDockPage(0, animate: false);
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
        Canvas.SetLeft(_forkDockTip, width + 6);
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
        var scale = new ScaleTransform(1, 1);
        var cell = new Border
        {
            Width = ForkDockCell,
            Height = ForkDockCell,
            Margin = new Thickness(0, 0, 0, last ? 0 : ForkDockSpacing),
            CornerRadius = new CornerRadius(11),
            Background = ForkFrozenBrush(Color.FromArgb(0x01, 0x00, 0x00, 0x00)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand,
            Child = image,
            Tag = app,
            RenderTransform = scale,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        int fps = AnimationConfig.TargetFps;
        cell.MouseEnter += (_, _) =>
        {
            var grow = MakeAnim(1.08, new Duration(TimeSpan.FromMilliseconds(200)), _easeExpOut6, fps);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            ForkShowDockTip(app, cell);
        };
        cell.MouseLeave += (_, _) =>
        {
            var rest = MakeAnim(1, new Duration(TimeSpan.FromMilliseconds(180)), _easeExpOut6, fps);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, rest);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, rest);
        };
        // On the press: upstream's outside-click handling closes the notch right after, so the release may never come.
        cell.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            ForkActivateDockApp(app);
        };
        ForkLoadDockIconAsync(image, app).SafeFireAndForget("FORK-APP-DOCK");
        return cell;
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
        // The incoming pair settles in: a short fade and grow, one after the other.
        for (int i = 0; i < ForkDockVisible; i++)
        {
            int index = page * ForkDockVisible + i;
            if (index >= _forkDockList.Children.Count || _forkDockList.Children[index] is not Border cell) continue;
            var delay = TimeSpan.FromMilliseconds(60 + 50 * i);
            cell.BeginAnimation(OpacityProperty, MakeAnim(0.25, 1, new Duration(TimeSpan.FromMilliseconds(320)), _easeQuadOut, delay));
            if (cell.RenderTransform is ScaleTransform scale)
            {
                var pop = MakeAnim(0.82, 1, new Duration(TimeSpan.FromMilliseconds(420)), _easeExpOut6, delay);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
        }
    }

    // ---- the name pill ----

    private void ForkShowDockTip(DockApp? app, FrameworkElement? cell)
    {
        if (_forkDockTip == null || _forkDockTipText == null || _forkDockTipShift == null || _forkDockHost == null) return;
        int fps = AnimationConfig.TargetFps;
        if (app == null || cell == null || app.Label.Length == 0)
        {
            if (_forkDockTip.Visibility != Visibility.Visible) return;
            var fade = MakeAnim(0, new Duration(TimeSpan.FromMilliseconds(110)), _easePowerOut3, fps);
            fade.Completed += (_, _) =>
            {
                if (_forkDockTip.Opacity < 0.01) _forkDockTip.Visibility = Visibility.Collapsed;
            };
            _forkDockTip.BeginAnimation(OpacityProperty, fade);
            return;
        }
        _forkDockTipText.Text = app.Label;
        _forkDockTip.Visibility = Visibility.Visible;
        _forkDockTip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double centre = cell.TranslatePoint(new Point(0, cell.ActualHeight / 2), _forkDockHost).Y;
        Canvas.SetTop(_forkDockTip, Math.Round(centre - _forkDockTip.DesiredSize.Height / 2));
        _forkDockTip.BeginAnimation(OpacityProperty, MakeAnim(1, new Duration(TimeSpan.FromMilliseconds(140)), _easeQuadOut, fps));
        _forkDockTipShift.BeginAnimation(TranslateTransform.XProperty, MakeAnim(-4, 0, new Duration(TimeSpan.FromMilliseconds(200)), _easeExpOut6, fps));
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
}
