using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VNotch.Controllers;
using VNotch.Fork;
using VNotch.Fork.Features.AppDock;
using VNotch.Services;
using static VNotch.Services.AnimationPrimitives;

namespace VNotch;

// faridfox fork: open-apps dock (Fork/README.md ledger), after DeskNotch. While the notch is open on its Media page, a
// slim vertical dock beside it shows the icons of the open apps (most recently used first). Click an icon to bring that
// app to the front; the mouse wheel scrolls the list.
//
// The dock is a child of upstream's NotchWrapper with zero layout width (it draws beside the notch without moving
// anything): only elements under NotchWrapper receive clicks (MainWindow.IsPointInteractive), and the pointer stays
// "inside the notch" on its way to the dock, so upstream's hover-collapse does not close the notch. A click on the dock
// counts as outside the notch for upstream, which collapses it after the app comes to the front.
// Fork-owned part of upstream's partial MainWindow; attached by Fork/Core/ForkUi.cs. No upstream file changes.
public partial class MainWindow
{
    private const double ForkDockIcon = 24;
    private const double ForkDockCell = 34;
    private const double ForkDockPillWidth = 44;
    /// <summary>Nearly invisible strip between the notch and the dock, so the pointer never leaves the notch on the way.</summary>
    private const double ForkDockBridge = 14;
    private static readonly TimeSpan ForkDockRefreshEvery = TimeSpan.FromSeconds(2);

    private Grid? _forkDockHost;
    private Border? _forkDockPill;
    private ScrollViewer? _forkDockScroll;
    private StackPanel? _forkDockList;
    private UiScheduledTimer? _forkDockTimer;
    private bool _forkDockShown;
    private bool _forkDockListing;
    private Task _forkDockTask = Task.CompletedTask;
    private IReadOnlyList<DockApp> _forkDockApps = [];

    // Test seams: the window list and the activation.
    internal Func<IReadOnlyList<DockApp>> ForkDockSource = AppDockWindows.List;
    internal Func<IntPtr, bool> ForkDockActivate = MediaWindowActivator.TryActivateWindow;

    internal Grid? ForkDockHost => _forkDockHost;
    internal bool ForkDockShown => _forkDockShown;
    internal IReadOnlyList<DockApp> ForkDockApps => _forkDockApps;

    internal void ForkAttachAppDock()
    {
        if (_forkDockHost != null || _cleanedUp) return;

        _forkDockList = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 5, 0, 5) };
        _forkDockScroll = new ScrollViewer
        {
            Content = _forkDockList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Focusable = false,
        };
        _forkDockPill = new Border
        {
            Width = ForkDockPillWidth,
            CornerRadius = new CornerRadius(16),
            Background = ForkFrozenBrush(Color.FromArgb(0xF2, 0x0A, 0x0A, 0x0C)),
            BorderBrush = ForkFrozenBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0.8),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            ClipToBounds = true,
            Child = _forkDockScroll,
        };
        _forkDockHost = new Grid
        {
            Width = ForkDockBridge + ForkDockPillWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            // Zero layout width: column 2 and the notch's centring stay exactly as upstream laid them out.
            Margin = new Thickness(0, 6, -(ForkDockBridge + ForkDockPillWidth), 0),
            Background = ForkFrozenBrush(Color.FromArgb(0x01, 0x00, 0x00, 0x00)),
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _forkDockHost.Children.Add(_forkDockPill);
        Grid.SetColumn(_forkDockHost, 2);
        // The wheel always stays in the dock: upstream's own wheel handler on the notch would switch pages.
        _forkDockHost.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            _forkDockScroll.ScrollToVerticalOffset(_forkDockScroll.VerticalOffset - e.Delta / 3.0);
        };
        // A press that misses the icons must not reach the notch (it would collapse or start a gesture).
        _forkDockHost.MouseLeftButtonDown += (_, e) => e.Handled = true;
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
        if (show)
        {
            _forkDockPill.MaxHeight = Math.Max(ForkDockCell + 10, NotchBorder.ActualHeight - 12);
            _forkDockHost.Visibility = Visibility.Visible;
            _forkDockHost.IsHitTestVisible = true;
            _forkDockHost.BeginAnimation(OpacityProperty, MakeAnim(_forkDockHost.Opacity, 1, new Duration(TimeSpan.FromMilliseconds(220)), _easeQuadOut, TimeSpan.FromMilliseconds(60)));
            ForkRefreshDockAsync().SafeFireAndForget("FORK-APP-DOCK");
            _forkDockTimer.Start();
        }
        else
        {
            _forkDockTimer.Stop();
            var host = _forkDockHost;
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
        return !double.IsFinite(free) || ActualWidth <= 0 || free >= ForkDockBridge + ForkDockPillWidth + 4;
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
            if (a[i].Window != b[i].Window || a[i].Title != b[i].Title) return false;
        return true;
    }

    private void ForkShowDockApps(IReadOnlyList<DockApp> apps)
    {
        _forkDockApps = apps;
        var list = _forkDockList!;
        list.Children.Clear();
        foreach (var app in apps) list.Children.Add(ForkDockCellFor(app));
    }

    private FrameworkElement ForkDockCellFor(DockApp app)
    {
        var image = new Image { Width = ForkDockIcon, Height = ForkDockIcon, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var cell = new Border
        {
            Width = ForkDockCell,
            Height = ForkDockCell,
            CornerRadius = new CornerRadius(10),
            Background = ForkFrozenBrush(Color.FromArgb(0x01, 0x00, 0x00, 0x00)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand,
            Child = image,
            ToolTip = string.IsNullOrWhiteSpace(app.Title) ? null : app.Title,
            Tag = app,
        };
        var hover = ForkFrozenBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        var rest = cell.Background;
        cell.MouseEnter += (_, _) => cell.Background = hover;
        cell.MouseLeave += (_, _) => cell.Background = rest;
        // On the press: upstream's outside-click handling closes the notch right after, so the release may never come.
        cell.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            ForkActivateDockApp(app);
        };
        ForkLoadDockIconAsync(image, app.Executable).SafeFireAndForget("FORK-APP-DOCK");
        return cell;
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

    private static async Task ForkLoadDockIconAsync(Image image, string executable)
    {
        var icon = await ForkAppIcons.ForExecutableAsync(executable);
        if (icon != null) image.Source = icon;
    }
}
