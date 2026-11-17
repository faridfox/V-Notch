using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using VNotch.Fork.Features.PlaybackControls;
using VNotch.Models;
using VNotch.Services;
using VNotch.Tests.Fakes;
using Xunit;

namespace VNotch.Tests.Fork;

// faridfox fork: Spotify-style controls with shuffle and repeat (Fork/Features/PlaybackControls).
[Collection("Localization")]
public sealed class PlaybackControlsTests
{
    [Fact]
    public void RepeatCyclesLikeSpotify()
    {
        Assert.Equal(ForkRepeat.All, PlaybackModesLogic.NextRepeat(ForkRepeat.Off));
        Assert.Equal(ForkRepeat.One, PlaybackModesLogic.NextRepeat(ForkRepeat.All));
        Assert.Equal(ForkRepeat.Off, PlaybackModesLogic.NextRepeat(ForkRepeat.One));
    }

    [Fact]
    public void FiveEvenSlotsWithARoundPlayButton() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        var view = window.MediaPlayback;
        window.ForkModesSource = () => new StubModes(new ForkPlaybackModes(true, false, true, ForkRepeat.Off));
        window.ForkAttachPlaybackControls();

        Assert.Equal(5, view.MediaControls.ColumnDefinitions.Count);
        Assert.Equal(0, Grid.GetColumn(window.ForkShuffleButton!));
        Assert.Equal(1, Grid.GetColumn(view.PrevButton));
        Assert.Equal(2, Grid.GetColumn(view.PlayPauseButton));
        Assert.Equal(3, Grid.GetColumn(view.NextButton));
        Assert.Equal(4, Grid.GetColumn(window.ForkRepeatButton!));

        Assert.Equal(MainWindow.ForkPlayButtonSize, view.PlayPauseButton.Width);
        Assert.Equal(MainWindow.ForkPlayButtonSize / 2, view.PlayPauseButton.CornerRadius.TopLeft);
        Assert.Equal(Visibility.Hidden, view.PauseIconPath.Visibility);     // upstream recolours these per song
        Assert.Equal(Visibility.Hidden, view.PlayIconPath.Visibility);
        Assert.Contains(view.PauseIcon.Children.OfType<System.Windows.Shapes.Path>(), p => ReferenceEquals(p.Data, PlaybackIcons.Pause));
        Assert.All(new[] { view.NextArrow0, view.NextArrow1, view.NextArrow2 }, p => Assert.Same(PlaybackIcons.Next, p.Data));
    });

    [Fact]
    public void PlayersWithoutShuffleOrRepeatHideTheButtons() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        window.ForkAttachPlaybackControls();                // upstream's fake: no modes (like a browser)
        Assert.Equal(Visibility.Hidden, window.ForkShuffleButton!.Visibility);
        Assert.Equal(Visibility.Hidden, window.ForkRepeatButton!.Visibility);
    });

    [Fact]
    public void ShuffleTogglesAndRepeatCycles() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        var player = new StubModes(new ForkPlaybackModes(true, false, true, ForkRepeat.Off));
        window.ForkModesSource = () => player;
        window.ForkAttachPlaybackControls();
        Assert.Equal(Visibility.Visible, window.ForkShuffleButton!.Visibility);

        await window.ForkToggleShuffleAsync();
        Assert.Equal([true], player.ShuffleCalls);
        Assert.True(window.ForkModes.Shuffle);

        await window.ForkCycleRepeatAsync();
        await window.ForkCycleRepeatAsync();
        await window.ForkCycleRepeatAsync();
        Assert.Equal([ForkRepeat.All, ForkRepeat.One, ForkRepeat.Off], player.RepeatCalls);
    });

    [Fact]
    public void AFailedCommandShowsWhatThePlayerReallyHas() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        var player = new StubModes(new ForkPlaybackModes(true, false, true, ForkRepeat.Off)) { Accept = false };
        window.ForkModesSource = () => player;
        window.ForkAttachPlaybackControls();
        await window.ForkToggleShuffleAsync();
        Assert.False(window.ForkModes.Shuffle);
    });

    [Fact]
    public void TheWholeMusicViewStillFitsTheCard() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        var view = window.MediaPlayback;
        window.ForkModesSource = () => new StubModes(new ForkPlaybackModes(true, true, true, ForkRepeat.All));
        window.ForkAttachNowPlaying();
        window.ForkAttachPlaybackControls();
        window.ForkUpdateAlbumLine(new MediaInfo { CurrentTrack = "Zayaha Meen", CurrentAlbum = "Ya Fatenny", MediaSource = "Spotify" });
        view.UpdateProgressSectionLayout();
        Invalidate(view);
        view.Measure(new Size(400, double.PositiveInfinity));
        Assert.True(view.DesiredSize.Height <= 117, $"the music view must fit the 147 px notch's card (117 px), got {view.DesiredSize.Height}");
    });

    private static void Invalidate(DependencyObject node)
    {
        if (node is UIElement element) element.InvalidateMeasure();
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Invalidate(VisualTreeHelper.GetChild(node, i));
    }

    private sealed class StubModes(ForkPlaybackModes modes) : IForkPlaybackModes
    {
        public bool Accept { get; init; } = true;
        public List<bool> ShuffleCalls { get; } = [];
        public List<ForkRepeat> RepeatCalls { get; } = [];
        private ForkPlaybackModes _modes = modes;

        public ForkPlaybackModes ForkGetModes() => _modes;

        public Task<bool> ForkSetShuffleAsync(bool on)
        {
            ShuffleCalls.Add(on);
            if (Accept) _modes = _modes with { Shuffle = on };
            return Task.FromResult(Accept);
        }

        public Task<bool> ForkSetRepeatAsync(ForkRepeat mode)
        {
            RepeatCalls.Add(mode);
            if (Accept) _modes = _modes with { Repeat = mode };
            return Task.FromResult(Accept);
        }
    }

    private static GreetingAcceptanceTests.MainWindowFixture CreateFixture() =>
        new("en", greeting: false,
            configureServices: services => services.AddSingleton<IMediaDetectionService>(new FakeMediaDetectionService()));
}
