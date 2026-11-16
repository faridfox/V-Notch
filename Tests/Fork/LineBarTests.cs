using System;
using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VNotch.Fork.Features.LineBar;
using VNotch.Models;
using VNotch.Services;
using VNotch.Tests.Fakes;
using Xunit;

namespace VNotch.Tests.Fork;

// faridfox fork: the double-click line bar (Fork/Features/LineBar).
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

    [Fact]
    public void WithoutTheForkUiADoubleClickKeepsUpstreamsBehaviour() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        Assert.False(fixture.Window.ForkHandleNotchClick(2));
        Assert.True(Field<bool>(fixture.Window, "_isNotchVisible"));
    });

    [Fact]
    public void DoubleClickHidesTheNotchIntoTheBarAndABarClickBringsItBack() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        window.ForkAttachLineBar();

        Assert.False(window.ForkHandleNotchClick(1));
        Assert.False(window.ForkLineBarActive);

        Assert.True(window.ForkHandleNotchClick(2));
        Assert.True(window.ForkLineBarActive);
        Assert.True(window.ForkLineBarShown);
        Assert.False(Field<bool>(window, "_isNotchVisible"));

        window.ForkLeaveLineBar();
        Assert.False(window.ForkLineBarActive);
        Assert.False(window.ForkLineBarShown);
        Assert.True(Field<bool>(window, "_isNotchVisible"));
    });

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

    private static void Set(MainWindow window, string name, object? value) => typeof(MainWindow).GetField(name, Private)!.SetValue(window, value);
}
