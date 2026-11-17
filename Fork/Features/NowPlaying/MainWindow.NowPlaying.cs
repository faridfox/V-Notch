using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using System.Windows.Shapes;
using VNotch.Fork;
using VNotch.Fork.Features.NowPlaying;
using VNotch.Models;
using VNotch.Services;
using static VNotch.Services.AnimationPrimitives;

namespace VNotch;

// faridfox fork: "now playing" additions to the expanded music view (Fork/README.md ledger), after NotchBar's and
// Spotify's layout:
// - an album line between title and artist ("Album: <name>", the name in the song's colour), evenly spaced;
// - the time labels on the progress bar's own row ("1:05 ---o-- 3:24"), which frees the height the album line needs;
// - Spotify-style hover on the bar: it stays slim and a round knob appears on the end of the played part (upstream's
//   hover thickens the bar, zooms the time labels, blurs and dims the controls).
//
// All of it is applied at runtime to upstream's MediaPlaybackView, so its XAML stays untouched. The song colour is
// upstream's: the album name shares the time label's brush, which upstream animates to the album-art colour
// (Windows/MainWindow.MediaBackground.cs). Fork-owned part of upstream's partial MainWindow; attached by
// Fork/Core/ForkUi.cs.
public partial class MainWindow
{
    /// <summary>Title, album and artist evenly spaced: the title row ends with headroom, so the album line moves up
    /// into it and leaves the same space below.</summary>
    internal static readonly Thickness ForkAlbumLineMargin = new(0, -2, 0, 4);
    internal const double ForkKnobSize = 12;
    /// <summary>Bar thickness while hovered (upstream: 10 px; Spotify keeps it slim).</summary>
    internal const double ForkHoverBarHeight = 6;
    /// <summary>Lines the bar and the knob up with the time labels' text (see ForkAttachNowPlaying).</summary>
    internal static readonly Thickness ForkBarRowOffset = new(0, 4, 0, 0);

    private TextBlock? _forkAlbumLine;
    private Run? _forkAlbumName;
    private Ellipse? _forkProgressKnob;
    private ScaleTransform? _forkKnobPop;
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
            Margin = ForkAlbumLineMargin,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        if (view.TryFindResource("SubtitleText") is Style subtitle) line.Style = subtitle;
        // The row keeps the main font's line height when a name falls back to a taller font (Arabic): the card has
        // only a few pixels to spare, more so in the shorter notch (MainWindow.TabsLayout.cs). Ink is not clipped.
        line.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        line.LineHeight = Math.Round(line.FontFamily.LineSpacing * line.FontSize, 2);
        line.Inlines.Add(label);
        line.Inlines.Add(name);
        line.SetBinding(MaxWidthProperty, new Binding(nameof(ActualWidth)) { Source = view.TitleScrollContainer });
        textPanel.Children.Insert(textPanel.Children.IndexOf(view.ArtistScrollContainer), line);
        _forkAlbumLine = line;
        _forkAlbumName = name;

        // ---- time labels beside the bar ----
        // Same row as the bar (Grid placement is never touched by upstream code). The labels keep the margins upstream
        // gives them on every resize: re-setting them on resize made upstream and the fork reset each other in a loop,
        // which shook the whole view. Upstream's 4 px top margin moves the labels' text 2 px below the row's centre,
        // so the bar and the knob (never touched by upstream) move 2 px down as well, with a 4 px top margin of their own.
        Grid.SetRow(view.CurrentTimeText, 0);
        Grid.SetRow(view.RemainingTimeText, 0);
        Grid.SetColumn(view.ProgressBarContainer, 1);
        Grid.SetColumnSpan(view.ProgressBarContainer, 1);
        view.ProgressBarBg.Margin = ForkBarRowOffset;
        view.IndeterminateProgress.Margin = ForkBarRowOffset;

        // ---- progress knob (on hover) ----
        var knob = new Ellipse
        {
            Fill = ForkFrozenBrush(Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Width = ForkKnobSize,
            Height = ForkKnobSize,
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var shift = new TranslateTransform();
        var x = new MultiBinding { Converter = ProgressKnobConverter.Instance };
        x.Bindings.Add(new Binding(nameof(ScaleTransform.ScaleX)) { Source = view.ProgressBarScale });
        x.Bindings.Add(new Binding(nameof(ActualWidth)) { Source = view.ProgressBarBg });
        x.Bindings.Add(new Binding(nameof(ActualWidth)) { Source = knob });
        BindingOperations.SetBinding(shift, TranslateTransform.XProperty, x);
        var pop = new ScaleTransform(0.5, 0.5);
        var transforms = new TransformGroup();
        transforms.Children.Add(pop);
        transforms.Children.Add(shift);
        knob.RenderTransform = transforms;
        // In the bar's grid cell, not inside the bar's own 10 px area: that would clip a 12 px dot into a flat pill.
        knob.Margin = ForkBarRowOffset;
        Grid.SetRow(knob, 0);
        Grid.SetColumn(knob, 1);
        view.ProgressSection.Children.Add(knob);
        _forkProgressKnob = knob;
        _forkKnobPop = pop;
        // These run after upstream's own handlers on the same element, so they replace upstream's hover look in the
        // same frame. Upstream's leave and drag-end code already returns everything to rest.
        view.ProgressBarContainer.MouseEnter += (_, _) => ForkProgressHover(true);
        view.ProgressBarContainer.MouseLeave += (_, _) =>
        {
            if (Mouse.Captured != view.ProgressBarContainer) ForkProgressHover(false);   // keep it while dragging
        };
        view.ProgressBarContainer.LostMouseCapture += (_, _) =>
        {
            if (!view.ProgressBarContainer.IsMouseOver) ForkProgressHover(false);
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

    /// <summary>Spotify-style hover: slim bar, knob fades in; upstream's stretch, label zoom, blur and dim undone.</summary>
    internal void ForkProgressHover(bool hovered)
    {
        var view = MediaPlayback;
        int fps = AnimationConfig.TargetFps;
        var duration = new Duration(TimeSpan.FromMilliseconds(hovered ? 180 : 140));
        if (hovered)
        {
            var height = MakeAnim(ForkHoverBarHeight, duration, _easeExpOut6, fps);
            view.ProgressBarBg.BeginAnimation(HeightProperty, height);
            view.ProgressBar.BeginAnimation(HeightProperty, height);
            var radius = MakeAnim(ForkHoverBarHeight / 2, duration, _easeExpOut6, fps);
            view.ProgressBarClip.BeginAnimation(RectangleGeometry.RadiusXProperty, radius);
            view.ProgressBarClip.BeginAnimation(RectangleGeometry.RadiusYProperty, radius);
            view.ProgressBarMainScale.BeginAnimation(ScaleTransform.ScaleXProperty, MakeAnim(1, duration, _easeExpOut6, fps));
            foreach (var label in new[] { view.CurrentTimeText, view.RemainingTimeText })
                ForkResetHoverTransform(label.RenderTransform, duration, fps);
            if (view.MediaControls.Effect is System.Windows.Media.Effects.BlurEffect blur)
                blur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, MakeAnim(0, duration, _easeExpOut6, fps));
            view.MediaControls.BeginAnimation(OpacityProperty, MakeAnim(1, duration, _easeExpOut6, fps));
        }
        if (_forkProgressKnob == null || _forkKnobPop == null) return;
        _forkProgressKnob.BeginAnimation(OpacityProperty, MakeAnim(hovered ? 1 : 0, duration, _easeQuadOut, fps));
        var pop = MakeAnim(hovered ? 1 : 0.5, duration, hovered ? _easeExpOut6 : _easeQuadOut, fps);
        _forkKnobPop.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        _forkKnobPop.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    // Upstream zooms and lifts the time labels on hover through a scale + translate group it creates on first hover.
    private static void ForkResetHoverTransform(Transform? transform, Duration duration, int fps)
    {
        if (transform is not TransformGroup group) return;
        foreach (var child in group.Children)
        {
            if (child is ScaleTransform scale)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, MakeAnim(1, duration, _easeExpOut6, fps));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, MakeAnim(1, duration, _easeExpOut6, fps));
            }
            else if (child is TranslateTransform translate)
            {
                translate.BeginAnimation(TranslateTransform.YProperty, MakeAnim(0, duration, _easeExpOut6, fps));
            }
        }
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
