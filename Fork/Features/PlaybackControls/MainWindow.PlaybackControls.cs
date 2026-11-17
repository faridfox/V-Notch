using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using VNotch.Controls;
using VNotch.Fork;
using VNotch.Fork.Features.PlaybackControls;
using VNotch.Models;
using VNotch.Services;
using static VNotch.Services.AnimationPrimitives;

namespace VNotch;

// faridfox fork: Spotify-style playback controls in the expanded music view (Fork/README.md ledger):
//   [shuffle]  [|<]  ( > )  [>|]  [repeat]
// - shuffle and repeat buttons for players that support them (Spotify does; browsers do not, so they stay hidden
//   there). The active state is drawn in the song's colour with a dot underneath, like Spotify; repeat cycles off ->
//   all -> one. Smart Shuffle is Spotify-only and not reachable from outside Spotify;
// - skip icons with a bar, and a light round play/pause button with a dark glyph.
//
// Applied at runtime to upstream's MediaPlaybackView (its controls row gets five columns). Upstream keeps all its own
// behaviour: clicks, hover, skip animations and the play/pause swap. Upstream recolours its play/pause paths for each
// song, so those are hidden and the fork draws its own dark glyphs in the same canvases. Shuffle and repeat go through
// Windows' media controls on the session upstream controls (MediaDetectionService.Modes.cs).
public partial class MainWindow
{
    internal const double ForkPlayButtonSize = 30;
    private static readonly SolidColorBrush ForkPlayCircle = ForkFrozenBrush(Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush ForkPlayGlyph = ForkFrozenBrush(Color.FromRgb(0x12, 0x12, 0x12));

    private ActionBorder? _forkShuffleButton;
    private ActionBorder? _forkRepeatButton;
    private Path? _forkShuffleIcon;
    private Path? _forkRepeatIcon;
    private Path? _forkRepeatOneMark;
    private FrameworkElement? _forkShuffleDot;
    private FrameworkElement? _forkRepeatDot;
    private ForkPlaybackModes _forkModes = ForkPlaybackModes.None;

    /// <summary>Where shuffle/repeat come from (upstream's media service in the app; a stub in tests).</summary>
    internal Func<IForkPlaybackModes?>? ForkModesSource;

    internal ActionBorder? ForkShuffleButton => _forkShuffleButton;
    internal ActionBorder? ForkRepeatButton => _forkRepeatButton;
    internal ForkPlaybackModes ForkModes => _forkModes;

    internal void ForkAttachPlaybackControls()
    {
        if (_forkShuffleButton != null || _cleanedUp) return;
        var view = MediaPlayback;
        var row = view.MediaControls;
        ForkModesSource ??= () => _mediaService as IForkPlaybackModes;

        // Five evenly spaced slots: shuffle | previous | play | next | repeat.
        row.ColumnDefinitions.Clear();
        foreach (var width in new[] { 1.0, 1.0, 0, 1.0, 1.0 })
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = width > 0 ? new GridLength(width, GridUnitType.Star) : GridLength.Auto });
        Grid.SetColumn(view.PrevButton, 1);
        Grid.SetColumn(view.PlayPauseButton, 2);
        Grid.SetColumn(view.NextButton, 3);
        view.PrevButton.HorizontalAlignment = HorizontalAlignment.Center;
        view.NextButton.HorizontalAlignment = HorizontalAlignment.Center;

        // Skip icons with a bar (all three copies: upstream's skip animation slides them in and out).
        foreach (var path in new[] { view.PrevArrow0, view.PrevArrow1, view.PrevArrow2 }) path.Data = PlaybackIcons.Previous;
        foreach (var path in new[] { view.NextArrow0, view.NextArrow1, view.NextArrow2 }) path.Data = PlaybackIcons.Next;

        // Light round play/pause button with a dark glyph.
        view.PlayPauseButton.Width = ForkPlayButtonSize;
        view.PlayPauseButton.Height = ForkPlayButtonSize;
        view.PlayPauseButton.CornerRadius = new CornerRadius(ForkPlayButtonSize / 2);
        view.PlayPauseButton.Background = ForkPlayCircle;
        view.PauseIconPath.Visibility = Visibility.Hidden;
        view.PlayIconPath.Visibility = Visibility.Hidden;
        view.PauseIcon.Children.Add(new Path { Data = PlaybackIcons.Pause, Fill = ForkPlayGlyph });
        view.PlayIcon.Children.Add(new Path { Data = PlaybackIcons.Play, Fill = ForkPlayGlyph });

        // Shuffle and repeat.
        _forkShuffleButton = ForkModeButton(PlaybackIcons.Shuffle, out _forkShuffleIcon, out _forkShuffleDot, out _);
        _forkRepeatButton = ForkModeButton(PlaybackIcons.Repeat, out _forkRepeatIcon, out _forkRepeatDot, out _forkRepeatOneMark);
        Grid.SetColumn(_forkShuffleButton, 0);
        Grid.SetColumn(_forkRepeatButton, 4);
        AutomationProperties.SetName(_forkShuffleButton, ForkText.T("playback.shuffle"));
        AutomationProperties.SetName(_forkRepeatButton, ForkText.T("playback.repeat"));
        _forkShuffleButton.Click += (_, _) => ForkToggleShuffleAsync().SafeFireAndForget("FORK-PLAYBACK-MODES");
        _forkRepeatButton.Click += (_, _) => ForkCycleRepeatAsync().SafeFireAndForget("FORK-PLAYBACK-MODES");
        row.Children.Add(_forkShuffleButton);
        row.Children.Add(_forkRepeatButton);

        _viewModel.MediaInfoUpdated += ForkOnModesMedia;
        Closed += (_, _) => _viewModel.MediaInfoUpdated -= ForkOnModesMedia;
        ForkRefreshModes();
    }

    private ActionBorder ForkModeButton(Geometry icon, out Path glyph, out FrameworkElement dot, out Path? oneMark)
    {
        var canvas = new Canvas { Width = 16, Height = 16 };
        glyph = new Path
        {
            Data = icon,
            Stroke = UiPalette.PrimaryBrush,
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        canvas.Children.Add(glyph);
        oneMark = null;
        if (ReferenceEquals(icon, PlaybackIcons.Repeat))
        {
            oneMark = new Path
            {
                Data = PlaybackIcons.RepeatOneMark,
                StrokeThickness = 1.3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Visibility = Visibility.Collapsed,
            };
            canvas.Children.Add(oneMark);
        }
        var box = new Viewbox { Width = 15, Height = 15, Child = canvas, Margin = new Thickness(0, 0, 0, 3) };
        var activeDot = new Border
        {
            Width = 3.5,
            Height = 3.5,
            CornerRadius = new CornerRadius(1.75),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 1),
            Visibility = Visibility.Hidden,
        };
        // The song's colour: the brush upstream animates to the album art (shared with the time labels).
        BindingOperations.SetBinding(activeDot, Border.BackgroundProperty,
            new Binding(nameof(TextBlock.Foreground)) { Source = MediaPlayback.CurrentTimeText });
        dot = activeDot;
        var content = new Grid();
        content.Children.Add(box);
        content.Children.Add(activeDot);
        var scale = new ScaleTransform(1, 1);
        var button = new ActionBorder
        {
            Width = 28,
            Height = 26,
            Background = ForkFrozenBrush(Color.FromArgb(0x01, 0x00, 0x00, 0x00)),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = content,
            RenderTransform = scale,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Visibility = Visibility.Hidden,
        };
        // Same hover as upstream's transport buttons; follow their fade when the notch opens or closes.
        button.MouseEnter += (_, _) => ForkScaleTo(scale, 1.15);
        button.MouseLeave += (_, _) => ForkScaleTo(scale, 1);
        BindingOperations.SetBinding(button, OpacityProperty, new Binding(nameof(Opacity)) { Source = MediaPlayback.PrevButton });
        return button;
    }

    private static void ForkScaleTo(ScaleTransform scale, double to)
    {
        var anim = MakeAnim(to, new Duration(TimeSpan.FromMilliseconds(220)), _easeExpOut6, AnimationConfig.TargetFps);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private void ForkOnModesMedia(object? sender, MediaInfo info)
    {
        if (!info.IsThumbnailOnlyUpdate) ForkRefreshModes();
    }

    internal void ForkRefreshModes() => ForkShowModes(ForkModesSource?.Invoke()?.ForkGetModes() ?? ForkPlaybackModes.None);

    private void ForkShowModes(ForkPlaybackModes modes)
    {
        _forkModes = modes;
        if (_forkShuffleButton == null || _forkRepeatButton == null) return;
        _forkShuffleButton.Visibility = modes.ShuffleSupported ? Visibility.Visible : Visibility.Hidden;
        _forkRepeatButton.Visibility = modes.RepeatSupported ? Visibility.Visible : Visibility.Hidden;
        ForkStyleMode(_forkShuffleIcon!, _forkShuffleDot!, modes.Shuffle);
        ForkStyleMode(_forkRepeatIcon!, _forkRepeatDot!, modes.Repeat != ForkRepeat.Off);
        if (_forkRepeatOneMark != null)
        {
            _forkRepeatOneMark.Visibility = modes.Repeat == ForkRepeat.One ? Visibility.Visible : Visibility.Collapsed;
            _forkRepeatOneMark.Stroke = _forkRepeatIcon!.Stroke;
        }
        TooltipHelper.SetTooltip(_forkShuffleButton, ForkText.T(modes.Shuffle ? "playback.shuffle.off" : "playback.shuffle.on"));
        TooltipHelper.SetTooltip(_forkRepeatButton, ForkText.T(modes.Repeat switch
        {
            ForkRepeat.Off => "playback.repeat.all",
            ForkRepeat.All => "playback.repeat.one",
            _ => "playback.repeat.off",
        }));
    }

    private void ForkStyleMode(Path glyph, FrameworkElement dot, bool active)
    {
        if (active)
            BindingOperations.SetBinding(glyph, Shape.StrokeProperty,
                new Binding(nameof(TextBlock.Foreground)) { Source = MediaPlayback.CurrentTimeText });
        else
        {
            BindingOperations.ClearBinding(glyph, Shape.StrokeProperty);
            glyph.Stroke = UiPalette.PrimaryBrush;
        }
        glyph.Opacity = active ? 1 : 0.72;
        dot.Visibility = active ? Visibility.Visible : Visibility.Hidden;
    }

    internal async Task ForkToggleShuffleAsync()
    {
        var source = ForkModesSource?.Invoke();
        if (source == null || !_forkModes.ShuffleSupported) return;
        bool target = !_forkModes.Shuffle;
        ForkShowModes(_forkModes with { Shuffle = target });           // at once; the player confirms right after
        if (!await source.ForkSetShuffleAsync(target)) ForkRefreshModes();
    }

    internal async Task ForkCycleRepeatAsync()
    {
        var source = ForkModesSource?.Invoke();
        if (source == null || !_forkModes.RepeatSupported) return;
        var target = PlaybackModesLogic.NextRepeat(_forkModes.Repeat);
        ForkShowModes(_forkModes with { Repeat = target });
        if (!await source.ForkSetRepeatAsync(target)) ForkRefreshModes();
    }
}
