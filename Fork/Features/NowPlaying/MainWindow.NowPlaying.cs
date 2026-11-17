using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.ComponentModel;
using System.Windows.Shapes;
using VNotch.Fork;
using VNotch.Fork.Features.NowPlaying;
using VNotch.Models;
using VNotch.Services;

namespace VNotch;

// faridfox fork: "now playing" additions to the expanded music view (Fork/README.md ledger), after NotchBar's layout:
// - an album line between title and artist ("Album: <name>", the name in the song's colour);
// - a round knob on the end of the played part of the progress bar.
//
// Both are inserted at runtime into upstream's MediaPlaybackView, so its XAML stays untouched. The song colour is
// upstream's: the album name shares the time label's brush, which upstream animates to the album-art colour
// (Windows/MainWindow.MediaBackground.cs). Fork-owned part of upstream's partial MainWindow; attached by
// Fork/Core/ForkUi.cs.
public partial class MainWindow
{
    /// <summary>The album line tucks this far into the title row's empty bottom, so the card still fits its height.</summary>
    internal const double ForkAlbumLineTuck = 5;
    /// <summary>The knob is this much taller than the bar (4 px at rest, 10 px while upstream's bar is hovered).</summary>
    private const double ForkKnobOversize = 6;

    private TextBlock? _forkAlbumLine;
    private Run? _forkAlbumName;
    private Ellipse? _forkProgressKnob;
    private string _forkAlbumShown = "";

    internal TextBlock? ForkAlbumLine => _forkAlbumLine;
    internal Ellipse? ForkProgressKnob => _forkProgressKnob;

    internal void ForkAttachNowPlaying()
    {
        if (_forkAlbumLine != null || _cleanedUp) return;
        var view = MediaPlayback;
        if (view.TitleScrollContainer.Parent is not Panel textPanel) return;

        // ---- album line ----
        var label = new Run(ForkText.T("nowPlaying.album") + " ") { Foreground = UiPalette.SecondaryBrush };
        var name = new Run();
        BindingOperations.SetBinding(name, TextElement.ForegroundProperty,
            new Binding(nameof(TextBlock.Foreground)) { Source = view.CurrentTimeText });
        var line = new TextBlock
        {
            Margin = new Thickness(0, -ForkAlbumLineTuck, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        if (view.TryFindResource("SubtitleText") is Style subtitle) line.Style = subtitle;
        line.Inlines.Add(label);
        line.Inlines.Add(name);
        line.SetBinding(MaxWidthProperty, new Binding(nameof(ActualWidth)) { Source = view.TitleScrollContainer });
        textPanel.Children.Insert(textPanel.Children.IndexOf(view.ArtistScrollContainer), line);
        _forkAlbumLine = line;
        _forkAlbumName = name;

        // ---- progress knob ----
        var knob = new Ellipse
        {
            Fill = ForkFrozenBrush(Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Width = view.ProgressBarBg.Height + ForkKnobOversize,
            Height = view.ProgressBarBg.Height + ForkKnobOversize,
        };
        var shift = new TranslateTransform();
        var x = new MultiBinding { Converter = ProgressKnobConverter.Instance };
        x.Bindings.Add(new Binding(nameof(ScaleTransform.ScaleX)) { Source = view.ProgressBarScale });
        x.Bindings.Add(new Binding(nameof(ActualWidth)) { Source = view.ProgressBarBg });
        x.Bindings.Add(new Binding(nameof(ActualWidth)) { Source = knob });
        BindingOperations.SetBinding(shift, TranslateTransform.XProperty, x);
        knob.RenderTransform = shift;
        view.ProgressBarContainer.Children.Add(knob);
        _forkProgressKnob = knob;
        // Grow with the bar (upstream thickens it on hover).
        view.ProgressBarBg.SizeChanged += (_, e) =>
        {
            if (!e.HeightChanged) return;
            knob.Width = knob.Height = e.NewSize.Height + ForkKnobOversize;
        };
        // Only where upstream shows a real timeline (not for live streams or while loading). Watches the elements' own
        // Visibility, which upstream sets even while the notch is closed.
        var visibility = DependencyPropertyDescriptor.FromProperty(VisibilityProperty, typeof(UIElement));
        visibility.AddValueChanged(view.ProgressBarBg, ForkOnTimelineVisibility);
        visibility.AddValueChanged(view.IndeterminateProgress, ForkOnTimelineVisibility);
        Closed += (_, _) =>
        {
            visibility.RemoveValueChanged(view.ProgressBarBg, ForkOnTimelineVisibility);
            visibility.RemoveValueChanged(view.IndeterminateProgress, ForkOnTimelineVisibility);
        };
        ForkUpdateKnobVisibility();

        _viewModel.MediaInfoUpdated += ForkOnNowPlayingMedia;
        Closed += (_, _) => _viewModel.MediaInfoUpdated -= ForkOnNowPlayingMedia;
        ForkUpdateAlbumLine(_currentMediaInfo);
    }

    private void ForkOnNowPlayingMedia(object? sender, MediaInfo info)
    {
        if (!info.IsThumbnailOnlyUpdate) ForkUpdateAlbumLine(info);
    }

    internal void ForkUpdateAlbumLine(MediaInfo? info)
    {
        if (_forkAlbumLine == null || _forkAlbumName == null) return;
        string album = NowPlayingText.Album(info?.CurrentTrack, info?.CurrentAlbum, info?.IsMetadataPending == true);
        if (album == _forkAlbumShown) return;
        _forkAlbumShown = album;
        _forkAlbumName.Text = album;
        _forkAlbumLine.Visibility = album.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ForkOnTimelineVisibility(object? sender, EventArgs e) => ForkUpdateKnobVisibility();

    private void ForkUpdateKnobVisibility()
    {
        if (_forkProgressKnob == null) return;
        var view = MediaPlayback;
        bool timeline = view.ProgressBarBg.Visibility == Visibility.Visible &&
                        view.IndeterminateProgress.Visibility != Visibility.Visible;
        _forkProgressKnob.Visibility = timeline ? Visibility.Visible : Visibility.Collapsed;
    }
}
