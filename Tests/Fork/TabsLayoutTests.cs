using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using VNotch.Fork;
using VNotch.Models;
using VNotch.Services;
using VNotch.ViewModels;
using VNotch.Tests.Fakes;
using Xunit;

namespace VNotch.Tests.Fork;

// faridfox fork: the open notch without its top row while the tabs are outside it (Fork/Features/NavTabs/
// MainWindow.TabsLayout.cs).
[Collection("Localization")]
public sealed class TabsLayoutTests : IDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "vnotch-fork-tabslayout-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void TabsOutsideMoveTheGearIntoThePillHideTheTopRowAndShortenTheNotch() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        var settings = new ForkSettingsService(_folder);
        double upstreamHeight = ExpandedHeight(window);
        var upstreamPadding = window.MediaWidget.Padding;
        double upstreamLyrics = window.LyricsWidget.Height;
        window.ForkAttachTabsPill(settings);

        // Top: upstream's layout, untouched.
        Assert.Equal(upstreamHeight, ExpandedHeight(window));
        Assert.Equal(Visibility.Collapsed, window.ForkTabsGear!.Visibility);
        Assert.Equal(1, window.SharedSettingsHost.Opacity);
        Assert.True(window.ExpandedStatusBarHost.IsHitTestVisible);

        settings.PreviewTabs(ForkTabsPosition.Bottom, ForkTabsSize.Default);
        Assert.Equal(upstreamHeight - MainWindow.ForkTabsOutsideTrim, ExpandedHeight(window));
        // The gear follows the tabs, inside the pill.
        var row = (StackPanel)window.NavIconsPanel.Parent;
        Assert.Same(window.ForkTabsPill, row.Parent);
        Assert.Equal(row.Children.IndexOf(window.NavIconsPanel) + 1, row.Children.IndexOf(window.ForkTabsGear));
        Assert.Equal(Visibility.Visible, window.ForkTabsGear.Visibility);
        Assert.Equal(Orientation.Horizontal, row.Orientation);
        // Battery and upstream's gear: hidden, and presses go through.
        foreach (FrameworkElement host in new FrameworkElement[] { window.SharedSettingsHost, window.ExpandedStatusBarHost })
        {
            Assert.Equal(0, host.Opacity);
            Assert.False(host.IsHitTestVisible);
        }
        // The space the top row needed goes; the music card keeps its own height.
        Assert.Equal(MainWindow.ForkMediaTopPadOutside, window.MediaWidget.Padding.Top);
        Assert.Equal(upstreamLyrics - MainWindow.ForkTabsOutsideTrim, window.LyricsWidget.Height);

        settings.PreviewTabs(ForkTabsPosition.Left, ForkTabsSize.Default);
        Assert.Equal(Orientation.Vertical, row.Orientation);                  // gear below the column of tabs
        Assert.Equal(new Thickness(0, 1, 0, 0), window.ForkTabsGear.BorderThickness);

        settings.RevertPreview();                                             // back to Top: everything restored
        Assert.Equal(upstreamHeight, ExpandedHeight(window));
        Assert.Equal(upstreamPadding, window.MediaWidget.Padding);
        Assert.Equal(upstreamLyrics, window.LyricsWidget.Height);
        Assert.Equal(Visibility.Collapsed, window.ForkTabsGear.Visibility);
        Assert.Equal(1, window.ExpandedStatusBarHost.Opacity);
        Assert.True(window.SharedSettingsHost.IsHitTestVisible);
    });

    // In the real window: the shorter open notch still holds the whole music card, without clipping it.
    [Theory]
    [InlineData("The Fame")]
    [InlineData("عيش معاك حكايات")]   // a name in a fallback font must not make the card taller
    public void TheShorterOpenNotchStillHoldsTheWholeMusicCard(string album) => SharedStaTestRunner.Run(() =>
    {
        using var fixture = CreateFixture();
        var window = fixture.Window;
        window.SetDebugViewLock(true);
        window.ShowActivated = false;
        window.Show();
        PumpUntil(() => (bool)typeof(MainWindow).GetField("_isStartupLayoutReady", Private)!.GetValue(window)!);
        var settings = new ForkSettingsService(_folder);
        window.ForkAttachNowPlaying();
        window.ForkAttachPlaybackControls();
        window.ForkAttachTabsPill(settings);
        settings.PreviewTabs(ForkTabsPosition.Bottom, ForkTabsSize.Default);
        var info = new MediaInfo
        {
            CurrentTrack = "LoveGame", CurrentArtist = "Lady Gaga", CurrentAlbum = album, MediaSource = "Spotify",
            IsAnyMediaPlaying = true, IsPlaying = true, Duration = TimeSpan.FromMinutes(3.6), Position = TimeSpan.FromSeconds(200),
        };
        typeof(ShellViewModel).GetMethod("ApplyMediaUpdate", Private)!.Invoke(window.DataContext, new object?[] { info });
        PumpFor(TimeSpan.FromMilliseconds(150));
        typeof(MainWindow).GetMethod("ExpandNotch", Private)!.Invoke(window, new object?[] { null, NotchView.Media, NotchView.Compact });
        PumpFor(TimeSpan.FromMilliseconds(1200));

        Assert.Equal(147 - MainWindow.ForkTabsOutsideTrim, window.NotchBorder.ActualHeight, 1);
        Assert.Equal(Visibility.Visible, window.ForkAlbumLine!.Visibility);
        var card = window.MediaPlayback;
        Assert.True(card.DesiredSize.Height <= card.ActualHeight + 0.01,
            $"the music card needs {card.DesiredSize.Height:F1} px but has {card.ActualHeight:F1} px (it would be cut)");
        double Top(FrameworkElement e) => e.TransformToAncestor(window.NotchBorder).Transform(new Point(0, 0)).Y;
        double bottom = Top(card) + card.ActualHeight;
        Assert.True(Top(card) >= 8, $"the title keeps room above it (top {Top(card):F1})");
        Assert.True(bottom <= window.NotchBorder.ActualHeight - 4, $"the controls keep room below them (bottom {bottom:F1})");
    }, timeoutSeconds: 60);

    private static double ExpandedHeight(MainWindow window) =>
        (double)typeof(MainWindow).GetProperty("_expandedHeight", Private)!.GetValue(window)!;

    private static void PumpFor(TimeSpan duration)
    {
        var end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            System.Threading.Thread.Sleep(10);
        }
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < end) PumpFor(TimeSpan.FromMilliseconds(20));
    }

    private static GreetingAcceptanceTests.MainWindowFixture CreateFixture() =>
        new("en", greeting: false, o => { o.EnableLocalOnlyMode = true; },
            services => services.AddSingleton<IMediaDetectionService>(new FakeMediaDetectionService()));
}
