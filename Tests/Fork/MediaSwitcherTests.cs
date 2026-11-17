using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using VNotch.Fork.Features.MediaSwitcher;
using VNotch.Models;
using VNotch.Services;
using VNotch.Tests.Fakes;
using Xunit;

namespace VNotch.Tests.Fork;

// faridfox fork: the media source switcher on the album art (Fork/Features/MediaSwitcher).
[Collection("Localization")]
public sealed class MediaSwitcherTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly ForkMediaSession Spotify = new("spotify|1", "Spotify.exe", true, "Song", "Singer", "Album");
    private static readonly ForkMediaSession Chrome = new("chrome|2", "Chrome", false, "A video", "Channel", "");
    private static readonly ForkMediaSession Edge = new("msedge|3", "MSEdge", false, "A podcast", "Host", "");

    // ---- rules (pure) -------------------------------------------------------------------------------------------

    [Fact]
    public void SessionsAreCycledInAStableOrder()
    {
        var ordered = MediaSwitcherLogic.Order([Spotify, Edge, Chrome]);
        Assert.Equal(["Chrome", "MSEdge", "Spotify.exe"], ordered.Select(s => s.AppId));
        Assert.Equal(ordered, MediaSwitcherLogic.Order([Chrome, Spotify, Edge]));
    }

    [Theory]
    [InlineData("chrome|2", 1, "msedge|3")]
    [InlineData("msedge|3", 1, "spotify|1")]
    [InlineData("spotify|1", 1, "chrome|2")]      // wraps around
    [InlineData("chrome|2", -1, "spotify|1")]     // wraps around backwards
    [InlineData("unknown", 1, "chrome|2")]
    [InlineData("unknown", -1, "spotify|1")]
    public void ArrowsStepToTheNextOrPreviousSession(string shown, int direction, string expected)
    {
        var ordered = MediaSwitcherLogic.Order([Spotify, Chrome, Edge]);
        Assert.Equal(expected, MediaSwitcherLogic.Step(ordered, shown, direction)!.Key);
    }

    [Fact]
    public void ASingleSessionHasNothingToSwitchTo() =>
        Assert.Null(MediaSwitcherLogic.Step([Spotify], Spotify.Key, 1));

    [Theory]
    [InlineData("YouTube", "Browser", "YouTube")]   // what upstream showed beats the generic guess
    [InlineData("Browser", "Browser", "Browser")]
    [InlineData(null, "Spotify", "Spotify")]
    [InlineData("", "", "Browser")]
    public void ThePinKeepsTheMostPreciseLabel(string? remembered, string classified, string expected) =>
        Assert.Equal(expected, MediaSwitcherLogic.Label(remembered, classified));

    // ---- on the window ------------------------------------------------------------------------------------------

    [Fact]
    public void WithOneSourceOnlyTheBadgeShows() => SharedStaTestRunner.RunAsync(async ct =>
    {
        var media = new FakeMediaDetectionService();
        await using var fixture = CreateFixture(media);
        var window = await Attach(fixture, [Spotify], Show(Spotify));
        Assert.Equal(Visibility.Visible, window.ForkSourcePill!.Visibility);
        Assert.False(window.ForkSourceArrowsShown);
    });

    [Fact]
    public void ArrowsPinTheNextSourceAndTheIconReturnsToAutomatic() => SharedStaTestRunner.RunAsync(async ct =>
    {
        var media = new FakeMediaDetectionService();
        await using var fixture = CreateFixture(media);
        var window = await Attach(fixture, [Spotify, Chrome], Show(Spotify));
        Assert.True(window.ForkSourceArrowsShown);

        window.ForkStepSource(1);
        Assert.Equal(Spotify.Key, MediaSwitcherLogic.Step(window.ForkSessions, Chrome.Key, 1)!.Key);
        Assert.Equal(Chrome.Key, media.PinnedSessionKey);   // upstream's pin now shows Chrome

        Set(window, "_currentMediaInfo", Show(Chrome));    // upstream's update for the pinned session
        Assert.True(window.ForkReturnToAutomaticSource());
        Assert.Null(media.PinnedSessionKey);
        Assert.False(window.ForkReturnToAutomaticSource()); // nothing picked any more
    });

    [Fact]
    public void ArrowClicksNeverReachTheAlbumArt() => SharedStaTestRunner.RunAsync(async ct =>
    {
        var media = new FakeMediaDetectionService();
        await using var fixture = CreateFixture(media);
        var window = await Attach(fixture, [Spotify, Chrome], Show(Spotify));
        var arrow = (FrameworkElement)((StackPanel)window.ForkSourcePill!.Child).Children[2];
        var press = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
        arrow.RaiseEvent(press);
        Assert.True(press.Handled);
        Assert.Equal(Chrome.Key, media.PinnedSessionKey);
    });

    [Fact]
    public void WithoutASessionListTheSwitcherIsInert() => SharedStaTestRunner.RunAsync(async ct =>
    {
        var media = new FakeMediaDetectionService();      // upstream's fake: no fork session list
        await using var fixture = CreateFixture(media);
        var window = fixture.Window;
        Set(window, "_currentMediaInfo", Show(Spotify));
        window.ForkAttachMediaSwitcher();
        await window.ForkRefreshSessionsAsync(force: true);
        Assert.Empty(window.ForkSessions);
        Assert.False(window.ForkSourceArrowsShown);
        window.ForkStepSource(1);
        Assert.Null(media.PinnedSessionKey);
    });

    private static async Task<MainWindow> Attach(GreetingAcceptanceTests.MainWindowFixture fixture,
        IReadOnlyList<ForkMediaSession> sessions, MediaInfo shown)
    {
        var window = fixture.Window;
        Set(window, "_currentMediaInfo", shown);
        window.ForkSessionSource = () => new StubSessions(sessions);
        window.ForkAttachMediaSwitcher();
        await window.ForkRefreshSessionsAsync(force: true);
        return window;
    }

    private static MediaInfo Show(ForkMediaSession session) => new()
    {
        SessionInstanceKey = session.Key,
        SourceAppId = session.AppId,
        MediaSource = session.AppId.StartsWith("Spotify", StringComparison.Ordinal) ? "Spotify" : "Browser",
        CurrentTrack = session.Title,
        CurrentArtist = session.Artist,
        Duration = TimeSpan.FromMinutes(3),
    };

    private sealed class StubSessions(IReadOnlyList<ForkMediaSession> sessions) : IForkMediaSessions
    {
        public Task<IReadOnlyList<ForkMediaSession>> ForkListSessionsAsync() => Task.FromResult(sessions);
    }

    private static GreetingAcceptanceTests.MainWindowFixture CreateFixture(FakeMediaDetectionService media) =>
        new("en", greeting: false, configureServices: services => services.AddSingleton<IMediaDetectionService>(media));

    private static void Set(MainWindow window, string name, object? value) => typeof(MainWindow).GetField(name, Private)!.SetValue(window, value);
}
