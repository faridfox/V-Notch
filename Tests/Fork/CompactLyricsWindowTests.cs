using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using VNotch.Fork;
using VNotch.Models;
using VNotch.Services;
using VNotch.Tests.Fakes;
using Xunit;

namespace VNotch.Tests.Fork;

// faridfox fork: the collapsed-notch lyrics feature wired into upstream's windows. These fail the gate when a sync
// drops the hook line in GetCollapsedWidth or moves the surfaces the feature attaches to.
[Collection("Localization")]
public sealed class CompactLyricsWindowTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void CollapsedWidthFollowsTheShownLineOnlyWhileMusicShows() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        using var folder = new TempFolder();
        double baseWidth = Field<NotchSettings>(window, "_settings").Width;

        Set(window, "_isMusicCompactMode", true);
        Assert.Equal(baseWidth, CollapsedWidth(window));
        window.ForkAttachCompactText(new ForkSettingsService(folder.Path));
        Assert.True(window.ForkCompactTextAttached);

        double Show(string title)
        {
            Set(window, "_currentMediaInfo", Media(title, "", "Chrome"));
            window.ForkRefreshCompactText();
            return CollapsedWidth(window);
        }

        double tiny = Show("A");
        double medium = Show("A somewhat longer song title, then more words");
        double longer = Show("A somewhat longer song title, then more words and even more words to sing");
        double huge = Show(new string('W', 400));

        Assert.Equal(MainWindow.ForkCompactMinWidth, tiny);
        Assert.InRange(medium, MainWindow.ForkCompactMinWidth + 1, MainWindow.ForkCompactMaxWidth - 1);
        Assert.True(longer > medium + 50, $"a longer line must widen the pill ({medium} -> {longer})");
        Assert.Equal(MainWindow.ForkCompactMaxWidth, huge);
        Assert.Equal(window.ForkFittedCompactWidth(window.ForkCompactTextShown), huge);

        // Leaving the music view restores upstream's width, whatever line was shown last.
        Set(window, "_isMusicCompactMode", false);
        Assert.Equal(baseWidth, CollapsedWidth(window));
    });

    [Fact]
    public void ALyricLineIsShownWordByWordWhenLyricsAreAvailable() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        using var folder = new TempFolder();
        window.ForkAttachCompactText(new ForkSettingsService(folder.Path));
        var settings = Field<NotchSettings>(window, "_settings");
        settings.EnableOnlineLyrics = true;
        settings.EnableLocalOnlyMode = false;

        var media = Media("Song", "Singer", "Spotify");
        media.IsPlaying = false;
        media.Position = TimeSpan.FromSeconds(6);
        Set(window, "_currentMediaInfo", media);
        // Lyrics as if upstream's LyricsService had answered (no network in tests).
        Set(window, "_forkLyricsKey", (media.CurrentTrack, media.CurrentArtist, (int)media.Duration.TotalSeconds, settings.LyricsProviderPriority));
        Set(window, "_forkLyricsLines", new List<LyricLine>
        {
            new(TimeSpan.FromSeconds(5), " first line here "),
            new(TimeSpan.FromSeconds(9), ""),
        });

        window.ForkRefreshCompactText();
        Assert.Equal("first line here", window.ForkCompactTextShown);
        var block = (TextBlock)window.MusicCompactContent.Children.OfType<Grid>().Single(grid => Grid.GetColumn(grid) == 1).Children[0];
        Assert.Equal(3, block.Inlines.OfType<System.Windows.Documents.Run>().Count(run => run.Foreground is System.Windows.Media.LinearGradientBrush));

        media.Position = TimeSpan.FromSeconds(10);
        window.ForkRefreshCompactText();
        Assert.Equal(VNotch.Fork.Features.CompactLyrics.CompactLyricsText.InstrumentalMark, window.ForkCompactTextShown);
    });

    [Fact]
    public void TheTextSitsBetweenTheAlbumArtAndTheVisualizerAndFollowsTheArt() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        using var folder = new TempFolder();
        window.ForkAttachCompactText(new ForkSettingsService(folder.Path));

        var host = window.MusicCompactContent.Children.OfType<Grid>()
            .Single(grid => grid.Children.OfType<TextBlock>().Any() && Grid.GetColumn(grid) == 1);
        Assert.Equal(0, Grid.GetRow(host));
        Assert.False(host.IsHitTestVisible);
        Assert.Equal(0, Grid.GetColumn(window.CompactThumbnailBorder));
        Assert.Equal(2, Grid.GetColumn(window.MusicViz));

        window.CompactThumbnailBorder.Visibility = Visibility.Collapsed;
        Assert.Equal(Visibility.Collapsed, host.Visibility);
        window.CompactThumbnailBorder.Visibility = Visibility.Visible;
        window.CompactThumbnailBorder.Opacity = 0.4;
        Assert.Equal(0.4, host.Opacity, 3);
    });

    [Fact]
    public void ShowsTitleAndArtistWhenLyricsAreOffOrNotAvailable() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        using var folder = new TempFolder();
        var forkSettings = new ForkSettingsService(folder.Path);
        window.ForkAttachCompactText(forkSettings);

        // Another player: never a lyrics lookup.
        Set(window, "_currentMediaInfo", Media("Some Video Song", "Some Channel", "Chrome"));
        window.ForkRefreshCompactText();
        Assert.Equal("Some Video Song — Some Channel", window.ForkCompactTextShown);

        // Spotify, but local-only mode blocks online lyrics: title and artist, no network.
        Field<NotchSettings>(window, "_settings").EnableLocalOnlyMode = true;
        Set(window, "_currentMediaInfo", Media("Song", "Singer", "Spotify"));
        window.ForkRefreshCompactText();
        Assert.Equal("Song — Singer", window.ForkCompactTextShown);

        // Switch off: title and artist.
        forkSettings.PreviewShowLyricsInCompactNotch(false);
        Assert.Equal("Song — Singer", window.ForkCompactTextShown);
    });

    [Fact]
    public void SettingsSwitchSitsBelowSpotifyLyricsPreviewsAndSavesWithTheWindow() => SharedStaTestRunner.RunAsync(async ct =>
    {
        BackgroundTestWindows.EnsureApplicationResources();
        using var folder = new TempFolder();
        var forkSettings = new ForkSettingsService(folder.Path);
        var service = new RecordingSettingsService();
        var window = new SettingsWindow(new NotchSettings(), service) { ShowActivated = false };
        try
        {
            window.ForkAttachCompactLyricsRow(forkSettings);
            var check = window.ForkCompactLyricsCheck!;
            var panel = (Panel)window.SpotifyLyricsRow.Parent;
            Assert.Same(check, ((StackPanel)((Border)panel.Children[panel.Children.IndexOf(window.SpotifyLyricsRow) + 1]).Child).Children[0]);
            Assert.Equal(ForkText.T("compactLyrics.setting"), check.Content);
            Assert.True(check.IsChecked);

            check.IsChecked = false;
            Assert.False(forkSettings.ShowLyricsInCompactNotch);
            Assert.False(File.Exists(forkSettings.FilePath));

            await service.ApplyAsync(new NotchSettings(), ct);
            Assert.False(new ForkSettingsService(folder.Path).ShowLyricsInCompactNotch);

            // A later change that is not saved is reverted when the window closes.
            check.IsChecked = true;
            Assert.True(forkSettings.ShowLyricsInCompactNotch);
            window.Show();
            window.Close();
            Assert.False(forkSettings.ShowLyricsInCompactNotch);
        }
        finally
        {
            if (window.IsLoaded) window.Close();
        }
    });

    private static MediaInfo Media(string track, string artist, string source) => new()
    {
        CurrentTrack = track,
        CurrentArtist = artist,
        MediaSource = source,
        Duration = TimeSpan.FromMinutes(3),
        IsPlaying = true,
    };

    private static GreetingAcceptanceTests.MainWindowFixture CreateFixture() =>
        new("en", greeting: false,
            configureServices: services => services.AddSingleton<IMediaDetectionService>(new FakeMediaDetectionService()));

    private static double CollapsedWidth(MainWindow window) =>
        (double)typeof(MainWindow).GetMethod("GetCollapsedWidth", Private)!.Invoke(window, null)!;

    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, Private)!.GetValue(window)!;

    private static void Set(MainWindow window, string name, object? value) => typeof(MainWindow).GetField(name, Private)!.SetValue(window, value);

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vnotch-fork-ui-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class RecordingSettingsService : ISettingsApplicationService
    {
        public NotchSettings Load() => new();

        public Task ApplyAsync(NotchSettings settings, System.Threading.CancellationToken ct = default)
        {
            SettingsApplied?.Invoke(this, settings.Clone());
            return Task.CompletedTask;
        }

        public Task<(NotchSettings Settings, bool RequiresRestart)> ImportAsync(string filePath, NotchSettings? currentSettings = null,
            System.Threading.CancellationToken ct = default) => Task.FromResult((new NotchSettings(), false));

        public void Export(string filePath, NotchSettings settings) { }

        public bool IsAutoStartEnabled() => false;

        public event EventHandler<NotchSettings>? SettingsApplied;
    }
}
