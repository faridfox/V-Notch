using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using VNotch.Fork.Features.NowPlaying;
using VNotch.Models;
using VNotch.Services;
using VNotch.Tests.Fakes;
using Xunit;

namespace VNotch.Tests.Fork;

// faridfox fork: album line and progress knob in the expanded music view (Fork/Features/NowPlaying).
[Collection("Localization")]
public sealed class NowPlayingTests
{
    [Theory]
    [InlineData("Geit Ala Karamty", "Esht Maak Hekayat", false, "Esht Maak Hekayat")]
    [InlineData("Song", "  Album  ", false, "Album")]
    [InlineData("Song", "", false, "")]
    [InlineData("Song", null, false, "")]
    [InlineData("Song", "Album", true, "")]        // metadata still loading
    [InlineData("", "Album", false, "")]           // no track
    [InlineData("Ripe", "ripe", false, "")]        // a single: the album would only repeat the title
    public void AlbumLineShowsARealAlbumOnly(string? track, string? album, bool pending, string expected) =>
        Assert.Equal(expected, NowPlayingText.Album(track, album, pending));

    [Theory]
    [InlineData(0.0, 200, 10, -5)]
    [InlineData(0.5, 200, 10, 95)]
    [InlineData(1.0, 200, 10, 195)]
    [InlineData(1.7, 200, 10, 195)]
    [InlineData(-1, 200, 10, -5)]
    [InlineData(0.5, 0, 10, -5)]
    public void TheKnobIsCentredOnTheEndOfThePlayedPart(double progress, double width, double knob, double left) =>
        Assert.Equal(left, NowPlayingText.KnobLeft(progress, width, knob), 3);

    [Fact]
    public void TheAlbumLineSitsBetweenTitleAndArtistAndFitsTheCard() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        var view = window.MediaPlayback;
        view.UpdateProgressSectionLayout();
        double before = Height(view);

        window.ForkAttachNowPlaying();
        var line = window.ForkAlbumLine!;
        var panel = (Panel)view.TitleScrollContainer.Parent;
        Assert.Equal(panel.Children.IndexOf(view.TitleScrollContainer) + 1, panel.Children.IndexOf(line));
        Assert.Equal(panel.Children.IndexOf(view.ArtistScrollContainer) - 1, panel.Children.IndexOf(line));
        Assert.Equal(Visibility.Collapsed, line.Visibility);
        Assert.Equal(before, Height(view), 1);         // hidden: upstream's layout unchanged

        window.ForkUpdateAlbumLine(Media("Geit Ala Karamty", "Esht Maak Hekayat"));
        Assert.Equal(Visibility.Visible, line.Visibility);
        Assert.Equal("Album: Esht Maak Hekayat", string.Concat(line.Inlines.OfType<Run>().Select(r => r.Text)));
        double after = Height(view);
        Assert.InRange(after - before, 1, 9);          // tucked into the title row's empty bottom
        Assert.True(after <= 117, $"the music view must fit the 147 px notch's card (117 px), got {after}");

        window.ForkUpdateAlbumLine(Media("Ripe", ""));
        Assert.Equal(Visibility.Collapsed, line.Visibility);
    });

    [Fact]
    public void TheAlbumNameUsesTheSongColourBrush() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        window.ForkAttachNowPlaying();
        var name = window.ForkAlbumLine!.Inlines.OfType<Run>().Last();
        var accent = new SolidColorBrush(Colors.OrangeRed);
        window.MediaPlayback.CurrentTimeText.Foreground = accent;   // what upstream does for each song
        Assert.Same(accent, name.Foreground);
    });

    [Fact]
    public void TheKnobFollowsTheBarAndNeverTakesClicks() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        var view = window.MediaPlayback;
        window.ForkAttachNowPlaying();
        var knob = window.ForkProgressKnob!;
        Assert.Same(view.ProgressBarContainer, knob.Parent);
        Assert.False(knob.IsHitTestVisible);               // seeking on the bar keeps working

        view.ProgressBarBg.Visibility = Visibility.Visible;
        view.IndeterminateProgress.Visibility = Visibility.Collapsed;
        view.UpdateProgressSectionLayout();
        Assert.Equal(Visibility.Visible, knob.Visibility);
        Invalidate(view);
        view.Measure(new Size(400, double.PositiveInfinity));
        view.Arrange(new Rect(view.DesiredSize));
        view.UpdateLayout();   // a real layout pass: only it raises the size notifications bindings listen to
        view.ProgressBarScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);   // as upstream does before a jump
        view.ProgressBarScale.ScaleX = 0.25;
        double width = view.ProgressBarBg.ActualWidth;
        Assert.True(width > 50);
        Assert.True(knob.ActualWidth > 0);

        view.IndeterminateProgress.Visibility = Visibility.Visible;               // live stream / loading
        Assert.Equal(Visibility.Collapsed, knob.Visibility);
        view.IndeterminateProgress.Visibility = Visibility.Collapsed;
        Assert.Equal(Visibility.Visible, knob.Visibility);
        Assert.Equal(0.25 * width - knob.ActualWidth / 2, ((TranslateTransform)knob.RenderTransform).X, 1);
    });

    // The window is never shown, so WPF does not pass "measure again" up the tree: invalidate it all, then measure.
    private static double Height(FrameworkElement view)
    {
        Invalidate(view);
        view.Measure(new Size(400, double.PositiveInfinity));
        return view.DesiredSize.Height;
    }

    private static void Invalidate(DependencyObject node)
    {
        if (node is UIElement element) element.InvalidateMeasure();
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Invalidate(VisualTreeHelper.GetChild(node, i));
    }

    private static MediaInfo Media(string track, string album) => new()
    {
        CurrentTrack = track,
        CurrentArtist = "Singer",
        CurrentAlbum = album,
        MediaSource = "Spotify",
        Duration = TimeSpan.FromMinutes(3),
    };

    private static GreetingAcceptanceTests.MainWindowFixture CreateFixture() =>
        new("en", greeting: false,
            configureServices: services => services.AddSingleton<IMediaDetectionService>(new FakeMediaDetectionService()));
}
