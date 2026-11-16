using System;
using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VNotch.Controllers;
using VNotch.Fork.Features.LineBar;
using VNotch.Models;
using VNotch.Services;
using VNotch.Tests.Fakes;
using Xunit;

namespace VNotch.Tests.Fork;

// faridfox fork: the press-and-hold line bar (Fork/Features/LineBar).
[Collection("Localization")]
public sealed class LineBarTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData("Song", true, 25, 100, 0.25)]
    [InlineData("Song", true, 0, 100, 0.0)]
    [InlineData("Song", true, 150, 100, 1.0)]
    public void ProgressIsThePlayedFraction(string track, bool timeline, double position, double duration, double expected) =>
        Assert.Equal(expected, LineBarProgress.Fraction(track, timeline, TimeSpan.FromSeconds(position), TimeSpan.FromSeconds(duration))!.Value, 3);

    [Theory]
    [InlineData(null, true, 100)]
    [InlineData("  ", true, 100)]
    [InlineData("Live stream", false, 100)]
    [InlineData("Song", true, 0)]
    public void NothingLoadedOrNoTimelineIsAPlainLine(string? track, bool timeline, double duration) =>
        Assert.Null(LineBarProgress.Fraction(track, timeline, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(duration)));

    // ---- the hold rules (pure) ----------------------------------------------------------------------------------

    private static readonly Point Origin = new(100, 100);
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Theory]
    [InlineData(true, 200, true, 0, "Waiting")]
    [InlineData(true, 449, true, 0, "Waiting")]
    [InlineData(true, 450, true, 0, "Hold")]
    [InlineData(true, 450, true, 6, "Hold")]      // a tiny wobble is still a hold
    [InlineData(true, 100, false, 0, "Click")]    // released early: the deferred click is given back
    [InlineData(true, 100, true, 7, "Click")]     // moved: a drag, not a hold
    [InlineData(false, 100, false, 0, "Cancel")]  // upstream's gesture handles its own release
    [InlineData(false, 600, true, 0, "Hold")]
    public void AHoldIs450MsWithoutMoving(bool deferred, double elapsedMs, bool buttonDown, double moved, string expectedStep)
    {
        var expected = Enum.Parse<HoldStep>(expectedStep);
        var hold = new LineBarHold();
        hold.Begin(Ms(1000), Origin, deferred, 1);
        Assert.Equal(expected, hold.Step(Ms(1000 + elapsedMs), buttonDown, new Point(Origin.X + moved, Origin.Y)));
        Assert.Equal(expected == HoldStep.Waiting, hold.IsPending);
    }

    [Fact]
    public void ADecidedPressStaysDecided()
    {
        var hold = new LineBarHold();
        hold.Begin(Ms(0), Origin, deferred: true, 1);
        Assert.Equal(HoldStep.Hold, hold.Step(Ms(500), true, Origin));
        Assert.Equal(HoldStep.Cancel, hold.Step(Ms(900), false, Origin));
    }

    // ---- the hold on the window ---------------------------------------------------------------------------------

    [Fact]
    public void WithoutTheForkUiAClickKeepsUpstreamsBehaviour() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        Assert.False(fixture.Window.ForkHandleNotchClick(1));
        Assert.True(Field<bool>(fixture.Window, "_isNotchVisible"));
    });

    [Fact]
    public void HoldingTheCollapsedNotchTurnsItIntoTheBarAndABarClickBringsItBack() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        var clock = Attach(window, buttonDown: true);

        Assert.True(window.ForkHandleNotchClick(1));     // the press is taken while it may become a hold
        Assert.True(window.ForkHoldPending);
        clock.Now += Ms(200);
        window.ForkHoldTick();
        Assert.True(window.ForkHoldPending);
        clock.Now += Ms(300);
        window.ForkHoldTick();

        Assert.False(window.ForkHoldPending);
        Assert.True(window.ForkLineBarActive);
        Assert.True(window.ForkLineBarShown);
        Assert.False(Field<bool>(window, "_isNotchVisible"));
        Assert.False(Field<bool>(window, "_isExpanded"));

        window.ForkLeaveLineBar();
        Assert.False(window.ForkLineBarActive);
        Assert.False(window.ForkLineBarShown);
        Assert.True(Field<bool>(window, "_isNotchVisible"));
        Assert.False(Field<bool>(window, "_isExpanded"));
    });

    [Fact]
    public void AShortClickStillOpensTheNotchOnRelease() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        bool down = true;
        var clock = Attach(window, buttonDown: true, isDown: () => down);

        Assert.True(window.ForkHandleNotchClick(1));
        Assert.Equal(NotchView.Compact, Coordinator(window).TargetView);  // not on the press any more
        clock.Now += Ms(120);
        down = false;
        window.ForkHoldTick();

        Assert.False(window.ForkHoldPending);
        Assert.False(window.ForkLineBarActive);
        Assert.NotEqual(NotchView.Compact, Coordinator(window).TargetView);  // upstream's expand was requested
    });

    [Fact]
    public void AReleaseReportedByUpstreamIsNeverSwallowed() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        Attach(window, buttonDown: false);
        Assert.False(window.ForkHandleNotchClick(1));    // button already up: upstream's own click goes ahead
        Assert.False(window.ForkHoldPending);
    });

    [Fact]
    public void HoldingDuringUpstreamsSwipeTrackingEndsTheGestureAndShowsTheBar() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        var clock = Attach(window, buttonDown: true);
        var gesture = typeof(MainWindow).GetField("_gestureController", Private)!.GetValue(window)!;
        gesture.GetType().GetMethod("BeginTracking")!.Invoke(gesture, [new Point(20, 10)]);
        typeof(MainWindow).GetProperty("_isGestureActive", Private)!.SetValue(window, true);

        window.ForkArmGestureHold(1);
        Assert.True(window.ForkHoldPending);
        clock.Now += Ms(460);
        window.ForkHoldTick();

        Assert.False(Field<bool>(window, "_isGestureActive"));
        Assert.False((bool)gesture.GetType().GetProperty("IsTracking")!.GetValue(gesture)!);
        Assert.True(window.ForkLineBarActive);
    });

    [Fact]
    public void TheExpandedNotchIsNotHeld() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        Attach(window, buttonDown: true);
        Coordinator(window).ForceState(NotchView.Media, NotchShapeState.Expanded, "test");
        Assert.True(Field<bool>(window, "_isExpanded"));
        Assert.False(window.ForkHandleNotchClick(1));    // upstream collapses on the press, as before
        Assert.False(window.ForkHoldPending);
    });

    [Fact]
    public void TheBarHasFullyRoundedEnds() => SharedStaTestRunner.RunAsync(async ct =>
    {
        var bar = MainWindow.ForkCreateLineBar(out _, out _);
        bar.Width = 120;
        bar.Height = 20;
        bar.Measure(new Size(200, 50));
        bar.Arrange(new Rect(0, 0, 120, 20));
        bar.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(120, 20, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(bar);
        var pixels = new byte[120 * 20 * 4];
        bitmap.CopyPixels(pixels, 120 * 4, 0);
        byte Alpha(int x, int y) => pixels[(y * 120 + x) * 4 + 3];

        Assert.Equal(0, Alpha(0, 0));          // all four corners are cut away
        Assert.Equal(0, Alpha(119, 0));
        Assert.Equal(0, Alpha(0, 19));
        Assert.Equal(0, Alpha(119, 19));
        Assert.True(Alpha(60, 10) > 200);      // the middle is solid
        Assert.True(Alpha(10, 10) > 200);      // and so is the middle of each rounded end
        Assert.True(Alpha(110, 10) > 200);
        await Task.CompletedTask;
    });

    private sealed class TestClock { public TimeSpan Now = TimeSpan.FromSeconds(100); }

    private static VNotch.Controllers.NotchTransitionCoordinator Coordinator(MainWindow window) =>
        (VNotch.Controllers.NotchTransitionCoordinator)typeof(MainWindow).GetField("_transitionCoordinator", Private)!.GetValue(window)!;

    private static TestClock Attach(MainWindow window, bool buttonDown, Func<bool>? isDown = null)
    {
        var clock = new TestClock();
        window.ForkAttachLineBar();
        window.ForkButtonDown = isDown ?? (() => buttonDown);
        window.ForkCursor = () => new Point(500, 5);
        window.ForkClock = () => clock.Now;
        return clock;
    }

    [Fact]
    public void ShowingTheNotchFromTheTrayEndsTheBar() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        window.ForkAttachLineBar();
        window.ForkEnterLineBar();
        Assert.True(window.ForkLineBarShown);

        typeof(MainWindow).GetMethod("ToggleNotch_Click", Private)!.Invoke(window, [window, new RoutedEventArgs()]);
        Assert.True(Field<bool>(window, "_isNotchVisible"));
        typeof(MainWindow).GetMethod("ForkUpdateLineBar", Private)!.Invoke(window, null);
        Assert.False(window.ForkLineBarActive);
        Assert.False(window.ForkLineBarShown);
    });

    [Fact]
    public void TheBarShowsTrackProgressOrAPlainLine() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        window.ForkAttachLineBar();

        window.ForkEnterLineBar();
        Assert.Equal(-1, window.ForkLineBarProgress);   // nothing loaded: plain line

        Set(window, "_currentMediaInfo", new MediaInfo
        {
            CurrentTrack = "Song",
            CurrentArtist = "Singer",
            MediaSource = "Spotify",
            Duration = TimeSpan.FromSeconds(100),
            Position = TimeSpan.FromSeconds(25),
            IsPlaying = false,
        });
        typeof(MainWindow).GetMethod("ForkUpdateLineBar", Private)!.Invoke(window, null);
        Assert.Equal(0.25, window.ForkLineBarProgress, 2);
    });

    private static GreetingAcceptanceTests.MainWindowFixture CreateFixture() =>
        new("en", greeting: false,
            configureServices: services => services.AddSingleton<IMediaDetectionService>(new FakeMediaDetectionService()));

    private static T Field<T>(MainWindow window, string name)
    {
        var field = typeof(MainWindow).GetField(name, Private);
        if (field != null) return (T)field.GetValue(window)!;
        return (T)typeof(MainWindow).GetProperty(name, Private)!.GetValue(window)!;
    }

    private static void Set(MainWindow window, string name, object? value)
    {
        var field = typeof(MainWindow).GetField(name, Private);
        if (field != null) field.SetValue(window, value);
        else typeof(MainWindow).GetProperty(name, Private)!.SetValue(window, value);
    }
}
