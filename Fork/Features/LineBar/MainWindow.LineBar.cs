using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VNotch.Fork.Features.LineBar;
using VNotch.Services;
using static VNotch.Services.AnimationPrimitives;

namespace VNotch;

// faridfox fork: "line bar" (Fork/README.md ledger). Press and hold the collapsed notch and it turns into a thin,
// fully rounded bar at the top edge: a music progress bar while a track is loaded, a plain line otherwise. One click
// on the bar brings the notch back.
//
// The bar is upstream's own "Hide notch" (tray menu) plus a fork element where the notch was: hiding collapses the
// notch, slides it away and quiets its pop-ups (notifications, volume, battery... all check _isNotchVisible), so
// nothing has to be suppressed here. Showing the notch by any route (bar click, tray "Show notch", a shortcut that
// opens it) ends the line bar.
//
// Hold detection. Upstream reacts to a press on the collapsed notch in one of two ways:
// - with a media session it tracks a swipe gesture and only clicks on release (MainWindow.Gestures.cs). The fork arms
//   the hold from a handler that runs after upstream's, and on firing ends upstream's gesture so the release is ignored;
// - otherwise it expands on the press itself (ToggleNotchFromClick). The fork's hook there takes the press, and gives
//   it back as the same click when the button is released early. So a plain click expands on release instead of press.
// While a press is pending, a 25 ms timer polls the real button and cursor: no mouse capture, no extra event handlers.
//
// Fork-owned part of upstream's partial MainWindow (compile-checked at each sync). Upstream calls in through one
// marked line in ToggleNotchFromClick (Windows/MainWindow.xaml.cs); Fork/Core/ForkUi.cs attaches the bar on Loaded.
public partial class MainWindow
{
    internal const double ForkLineBarHeight = 5;
    /// <summary>Gap above the bar in notch mode, so both rounded ends show (island mode uses upstream's island margin).</summary>
    private const double ForkLineBarTopGap = 3;
    /// <summary>Invisible but clickable area around the bar (alpha 1/255: a layered window passes alpha 0 through).</summary>
    private const double ForkLineBarHitPadding = 8;
    private static readonly TimeSpan ForkLineBarTick = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ForkHoldPoll = TimeSpan.FromMilliseconds(25);

    private readonly LineBarHold _forkHold = new();
    private UiScheduledTimer? _forkHoldTimer;
    private bool _forkHoldReplay;
    // Test seams: the real left button, cursor (screen pixels) and a monotonic clock.
    internal Func<bool> ForkButtonDown = () => (Win32Interop.GetAsyncKeyState(Win32Interop.VK_LBUTTON) & 0x8000) != 0;
    internal Func<Point> ForkCursor = () => Win32Interop.GetCursorPos(out var p) ? new Point(p.X, p.Y) : default;
    internal Func<TimeSpan> ForkClock = () => TimeSpan.FromMilliseconds(Environment.TickCount64);

    private Grid? _forkLineBarHost;
    private Border? _forkLineBar;
    private Border? _forkLineBarFill;
    private ScaleTransform? _forkLineBarFillScale;
    private TranslateTransform? _forkLineBarShift;
    private UiScheduledTimer? _forkLineBarTimer;
    private bool _forkLineBarActive;
    private bool _forkLineBarShown;
    private bool _forkLineBarPressed;

    internal bool ForkLineBarActive => _forkLineBarActive;
    internal bool ForkLineBarShown => _forkLineBarShown;
    internal bool ForkHoldPending => _forkHold.IsPending;
    internal double ForkLineBarProgress => _forkLineBarFill?.Visibility == Visibility.Visible ? _forkLineBarFillScale?.ScaleX ?? 0 : -1;

    /// <summary>
    /// The bar: a pill with fully rounded ends whose rounded clip follows its size (it grows on hover), and a progress
    /// fill scaled inside that clip. Static so the look can be rendered and checked in tests.
    /// </summary>
    internal static Border ForkCreateLineBar(out Border fill, out ScaleTransform fillScale)
    {
        fillScale = new ScaleTransform(0, 1);
        fill = new Border
        {
            Background = ForkFrozenBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            RenderTransform = fillScale,
            RenderTransformOrigin = new Point(0, 0.5),
            Visibility = Visibility.Collapsed,
        };
        var bar = new Border
        {
            Height = ForkLineBarHeight,
            Background = ForkFrozenBrush(Color.FromArgb(0xF2, 0x00, 0x00, 0x00)),
            BorderBrush = ForkFrozenBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0.5),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = fill,
        };
        bar.SizeChanged += (_, e) =>
        {
            double radius = e.NewSize.Height / 2;
            bar.CornerRadius = new CornerRadius(radius);
            bar.Clip = new RectangleGeometry(new Rect(e.NewSize), radius, radius);
        };
        return bar;
    }

    internal void ForkAttachLineBar()
    {
        if (_forkLineBarHost != null || _cleanedUp || NotchContainer.Parent is not Panel root) return;

        _forkLineBar = ForkCreateLineBar(out var fill, out var fillScale);
        _forkLineBarFill = fill;
        _forkLineBarFillScale = fillScale;
        _forkLineBarShift = new TranslateTransform();
        _forkLineBarHost = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Background = ForkFrozenBrush(Color.FromArgb(0x01, 0x00, 0x00, 0x00)),
            Cursor = Cursors.Hand,
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            RenderTransform = _forkLineBarShift,
        };
        Panel.SetZIndex(_forkLineBarHost, 1000);
        _forkLineBarHost.Children.Add(_forkLineBar);
        // Only a click that also started on the bar: the hold's own release must not end the line bar it just created.
        _forkLineBarHost.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _forkLineBarPressed = _forkLineBarShown;
        };
        _forkLineBarHost.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            bool pressed = _forkLineBarPressed;
            _forkLineBarPressed = false;
            if (pressed && _forkLineBarShown) ForkLeaveLineBar();
        };
        _forkLineBarHost.MouseEnter += (_, _) => ForkAnimateLineBarHover(hovered: true);
        _forkLineBarHost.MouseLeave += (_, _) => ForkAnimateLineBarHover(hovered: false);
        if (root is Grid grid)
        {
            Grid.SetRowSpan(_forkLineBarHost, Math.Max(1, grid.RowDefinitions.Count));
            Grid.SetColumnSpan(_forkLineBarHost, Math.Max(1, grid.ColumnDefinitions.Count));
        }
        root.Children.Add(_forkLineBarHost);

        _forkLineBarTimer = new UiScheduledTimer(DispatcherPriority.Background) { Interval = ForkLineBarTick };
        _forkLineBarTimer.Tick += (_, _) => ForkUpdateLineBar();
        _forkHoldTimer = new UiScheduledTimer(DispatcherPriority.Input) { Interval = ForkHoldPoll };
        _forkHoldTimer.Tick += (_, _) => ForkHoldTick();
        // Runs after upstream's own handler (added later), so upstream has already decided whether a gesture started.
        NotchWrapper.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler((_, e) => ForkArmGestureHold(e.ClickCount)),
            handledEventsToo: true);
        NotchContainer.IsVisibleChanged += (_, _) => ForkUpdateLineBar();
        Closed += (_, _) =>
        {
            _forkLineBarTimer?.Stop();
            _forkHoldTimer?.Stop();
            _forkHold.Reset();
        };
    }

    private bool ForkHoldBlocked =>
        _forkLineBarHost == null || _cleanedUp || !_isNotchVisible || _isExpanded || _isMusicExpanded || _isAnimating ||
        _isDebugViewLocked || IsGreetingInteractionBlocked || IsCountdownCompletionVisualActive || IsAppNotificationBannerActive;

    /// <summary>
    /// Called first in upstream's ToggleNotchFromClick. True when the fork took the press: the collapsed notch was
    /// pressed (button still down), so the click waits for the release, or becomes the line bar if held.
    /// </summary>
    internal bool ForkHandleNotchClick(int clickCount)
    {
        if (_forkHoldReplay || ForkHoldBlocked) return false;
        // Release-time callers (end of upstream's swipe tracking, debug drag): upstream as usual. A pending gesture hold
        // is then cancelled by the next poll, which sees the button up.
        if (!ForkButtonDown()) return false;
        if (_forkHold.IsPending) return _forkHold.Deferred;  // the same press, reported again
        ForkBeginHold(deferred: true, clickCount);
        return true;
    }

    /// <summary>A press that started upstream's swipe tracking (media session present): may become a hold as well.</summary>
    internal void ForkArmGestureHold(int clickCount)
    {
        if (_forkHold.IsPending || clickCount != 1 || ForkHoldBlocked) return;
        if (!_isGestureActive || !_gestureController.IsTracking) return;
        ForkBeginHold(deferred: false, clickCount);
    }

    private void ForkBeginHold(bool deferred, int clickCount)
    {
        _forkHold.Begin(ForkClock(), ForkCursor(), deferred, clickCount);
        if (_forkHoldTimer != null && !_forkHoldTimer.IsEnabled) _forkHoldTimer.Start();
    }

    internal void ForkHoldTick()
    {
        var step = _forkHold.Step(ForkClock(), ForkButtonDown(), ForkCursor());
        if (step == HoldStep.Waiting) return;
        _forkHoldTimer?.Stop();
        switch (step)
        {
            case HoldStep.Click:
                // Give the press back as the click it was, if nothing else opened the notch meanwhile.
                if (_cleanedUp || !_isNotchVisible || _isExpanded || _isMusicExpanded || _isAnimating) return;
                _forkHoldReplay = true;
                try { ToggleNotchFromClick(_forkHold.ClickCount); }
                finally { _forkHoldReplay = false; }
                return;
            case HoldStep.Hold:
                ForkFireHold();
                return;
        }
    }

    private void ForkFireHold()
    {
        if (_isGestureActive)
        {
            // End upstream's swipe tracking now, so its mouse-up (if it still arrives) neither swipes nor clicks.
            _gestureController.CancelTracking();
            _isGestureActive = false;
            NotchWrapper.ReleaseMouseCapture();
            AnimateGestureSnapBack();
        }
        if (ForkHoldBlocked) return;
        if (_isCompactThumbnailHovered) SetCompactThumbnailHover(false);
        ForkEnterLineBar();
    }

    internal void ForkEnterLineBar()
    {
        _forkLineBarActive = true;
        // Upstream's tray "Hide notch": collapses (unless an animation is running), slides the notch away.
        if (_isNotchVisible) ToggleNotch_Click(this, new RoutedEventArgs());
        ForkCollapseHiddenNotch();
        ForkUpdateLineBar();
    }

    internal void ForkLeaveLineBar()
    {
        _forkLineBarActive = false;
        if (!_isNotchVisible) ToggleNotch_Click(this, new RoutedEventArgs()); // upstream's "Show notch"
        ForkUpdateLineBar();
    }

    // If the notch was opening when the bar was requested, upstream's hide skips collapsing while an animation runs.
    // Collapse it behind the bar once it settles, so the notch comes back collapsed.
    private void ForkCollapseHiddenNotch()
    {
        if (!_forkLineBarActive || _isNotchVisible || !(_isExpanded || _isMusicExpanded) || _isAnimating) return;
        CollapseAll();
    }

    private void ForkUpdateLineBar()
    {
        if (_forkLineBarHost == null || _forkLineBar == null || _forkLineBarTimer == null) return;
        // Shown again by any route (bar click, tray "Show notch", a shortcut): the line bar is over.
        if (_forkLineBarActive && _isNotchVisible) _forkLineBarActive = false;

        ForkCollapseHiddenNotch();
        bool show = _forkLineBarActive && !_isNotchVisible && !_isHiddenByFullscreen && !_cleanedUp;
        if (show != _forkLineBarShown) ForkAnimateLineBarVisibility(show);
        if (show) ForkUpdateLineBarContent();

        if (_forkLineBarActive && !_cleanedUp)
        {
            if (!_forkLineBarTimer.IsEnabled) _forkLineBarTimer.Start();
        }
        else
        {
            _forkLineBarTimer.Stop();
        }
    }

    private void ForkUpdateLineBarContent()
    {
        bool island = _settings.EnableDynamicIslandMode;
        double width = island ? _settings.DynamicIslandWidth : _settings.Width;
        _forkLineBar!.Width = width;
        _forkLineBar.Margin = new Thickness(ForkLineBarHitPadding, island ? DynamicIslandTopMargin : ForkLineBarTopGap,
            ForkLineBarHitPadding, ForkLineBarHitPadding);

        var info = _currentMediaInfo;
        var playback = GetLyricsPlayback();
        TimeSpan duration = info?.Duration ?? TimeSpan.Zero;
        double? progress = LineBarProgress.Fraction(info?.CurrentTrack, info?.HasTimeline == true, playback.Position, duration);
        var scale = _forkLineBarFillScale!;
        var fill = _forkLineBarFill!;
        if (progress is not double now)
        {
            fill.Visibility = Visibility.Collapsed;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.ScaleX = 0;
            return;
        }

        fill.Visibility = Visibility.Visible;
        double current = scale.ScaleX;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.ScaleX = current;
        if (!playback.Playing || AnimationConfig.ReduceMotion || Math.Abs(now - current) > 0.05)
        {
            scale.ScaleX = now; // paused, reduced motion, or a seek: jump
            return;
        }
        // Playing: glide to where the track will be at the next refresh, so the bar moves continuously.
        double next = LineBarProgress.Fraction(info?.CurrentTrack, true, playback.Position + ForkLineBarTick, duration) ?? now;
        var glide = new DoubleAnimation(now, next, ForkLineBarTick);
        Timeline.SetDesiredFrameRate(glide, Math.Min(AnimationConfig.TargetFps, 60));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, glide);
    }

    private void ForkAnimateLineBarVisibility(bool show)
    {
        var host = _forkLineBarHost!;
        var shift = _forkLineBarShift!;
        _forkLineBarShown = show;
        host.IsHitTestVisible = show;
        int fps = AnimationConfig.TargetFps;
        if (show)
        {
            host.Visibility = Visibility.Visible;
            // Appear while the notch slides away (upstream: 220-250 ms).
            host.BeginAnimation(OpacityProperty, MakeAnim(host.Opacity, 1, new Duration(TimeSpan.FromMilliseconds(260)), _easeQuadOut, TimeSpan.FromMilliseconds(120)));
            shift.BeginAnimation(TranslateTransform.YProperty, MakeAnim(-ForkLineBarHeight - 2, 0, new Duration(TimeSpan.FromMilliseconds(320)), _easeExpOut6, TimeSpan.FromMilliseconds(120)));
        }
        else
        {
            var fade = MakeAnim(0, new Duration(TimeSpan.FromMilliseconds(160)), _easePowerOut3, fps);
            fade.Completed += (_, _) =>
            {
                if (!_forkLineBarShown) host.Visibility = Visibility.Collapsed;
            };
            host.BeginAnimation(OpacityProperty, fade);
            shift.BeginAnimation(TranslateTransform.YProperty, MakeAnim(-ForkLineBarHeight - 2, new Duration(TimeSpan.FromMilliseconds(160)), _easePowerOut3, fps));
        }
    }

    // A touch taller on hover, so the thin bar is easy to see and hit.
    private void ForkAnimateLineBarHover(bool hovered)
    {
        if (_forkLineBar == null) return;
        _forkLineBar.BeginAnimation(HeightProperty,
            MakeAnim(hovered ? ForkLineBarHeight + 3 : ForkLineBarHeight, new Duration(TimeSpan.FromMilliseconds(180)), _easeExpOut6, AnimationConfig.TargetFps));
    }

    private static SolidColorBrush ForkFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
