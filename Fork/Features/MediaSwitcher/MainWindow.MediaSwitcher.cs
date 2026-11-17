using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VNotch.Fork;
using VNotch.Fork.Features.MediaSwitcher;
using VNotch.Models;
using VNotch.Services;

namespace VNotch;

// faridfox fork: media source switcher (Fork/README.md ledger), after Laptop Notch. A small pill on the album art of the
// expanded music view shows which app is playing (its icon). When several apps or tabs hold media, arrows appear:
// ‹ › show the previous / next one. Clicking the icon while a source was picked returns to automatic choice.
//
// Switching is upstream's own pin (right-click on the album art): the picked session is pinned, so upstream shows it
// and its controls act on it; upstream's pin badge marks the manual choice. Sessions are listed by a fork-owned part of
// MediaDetectionService (MediaDetectionService.Sessions.cs). Fork-owned part of upstream's partial MainWindow; attached
// by Fork/Core/ForkUi.cs. No upstream file changes.
public partial class MainWindow
{
    private static readonly TimeSpan ForkSessionsRefreshEvery = TimeSpan.FromSeconds(2);

    private Border? _forkSourcePill;
    private Image? _forkSourceIcon;
    private FrameworkElement? _forkSourcePrev;
    private FrameworkElement? _forkSourceNext;
    private List<ForkMediaSession> _forkSessions = [];
    private readonly Dictionary<string, string> _forkSourceLabels = new(StringComparer.Ordinal);
    private DateTime _forkSessionsListedUtc = DateTime.MinValue;
    private bool _forkSessionsListing;
    private Task _forkSessionsTask = Task.CompletedTask;
    private string _forkSourceIconAppId = "";
    private string _forkSwitcherShownKey = "";

    /// <summary>Where the session list comes from (upstream's service in the app; a stub in tests).</summary>
    internal Func<IForkMediaSessions?>? ForkSessionSource;

    internal Border? ForkSourcePill => _forkSourcePill;
    internal IReadOnlyList<ForkMediaSession> ForkSessions => _forkSessions;
    internal bool ForkSourceArrowsShown => _forkSourceNext?.Visibility == Visibility.Visible;

    internal void ForkAttachMediaSwitcher()
    {
        if (_forkSourcePill != null || _cleanedUp || ThumbnailBorder.Child is not Grid art) return;
        ForkSessionSource ??= () => _mediaService as IForkMediaSessions;

        _forkSourcePrev = ForkSourceArrow(previous: true);
        _forkSourceNext = ForkSourceArrow(previous: false);
        _forkSourceIcon = new Image { Width = 16, Height = 16, Margin = new Thickness(3, 2, 3, 2), Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(_forkSourceIcon, BitmapScalingMode.HighQuality);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_forkSourcePrev);
        row.Children.Add(_forkSourceIcon);
        row.Children.Add(_forkSourceNext);
        _forkSourcePill = new Border
        {
            Child = row,
            CornerRadius = new CornerRadius(10),
            Background = ForkFrozenBrush(Color.FromArgb(0xC8, 0x00, 0x00, 0x00)),
            BorderBrush = ForkFrozenBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0.8),
            Padding = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(5),
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
        };
        // A click on the icon while a source was picked: back to automatic. Otherwise the click falls through to the
        // album art (upstream opens the playing app).
        _forkSourceIcon.MouseLeftButtonDown += (_, e) =>
        {
            if (ForkReturnToAutomaticSource()) e.Handled = true;
        };
        art.Children.Add(_forkSourcePill);

        _viewModel.MediaInfoUpdated += ForkOnSwitcherMedia;
        ThumbnailBorder.IsVisibleChanged += ForkOnArtVisibilityChanged;
        Closed += (_, _) =>
        {
            _viewModel.MediaInfoUpdated -= ForkOnSwitcherMedia;
            ThumbnailBorder.IsVisibleChanged -= ForkOnArtVisibilityChanged;
        };
        if (_currentMediaInfo != null) ForkOnSwitcherMedia(this, _currentMediaInfo);
    }

    private FrameworkElement ForkSourceArrow(bool previous)
    {
        var chevron = new Path
        {
            Data = Geometry.Parse(previous ? "M 4.5,1 L 1,4.5 L 4.5,8" : "M 1,1 L 4.5,4.5 L 1,8"),
            Stroke = ForkFrozenBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var hit = new Border
        {
            Width = 14,
            Background = ForkFrozenBrush(Color.FromArgb(0x01, 0x00, 0x00, 0x00)),
            Child = chevron,
            Visibility = Visibility.Collapsed,
        };
        // Handled: otherwise the album art's own click opens the playing app.
        hit.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            ForkStepSource(previous ? -1 : 1);
        };
        return hit;
    }

    private void ForkOnArtVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (ThumbnailBorder.IsVisible) ForkRefreshSessionsAsync(force: true).SafeFireAndForget("FORK-MEDIA-SWITCHER");
    }

    private void ForkOnSwitcherMedia(object? sender, MediaInfo info)
    {
        if (_forkSourcePill == null) return;
        string key = info.SessionInstanceKey ?? "";
        // Remember what upstream itself called each session: the most precise label for a later pin.
        if (!info.IsThumbnailOnlyUpdate && key.Length > 0 && !string.IsNullOrWhiteSpace(info.MediaSource) &&
            info.Platform != MediaPlatform.Browser)
            _forkSourceLabels[key] = info.MediaSource;
        ForkShowSourceIcon(info.SourceAppId ?? "");
        bool newSession = key != _forkSwitcherShownKey;
        _forkSwitcherShownKey = key;
        ForkUpdateSwitcherVisibility();
        ForkRefreshSessionsAsync(force: newSession).SafeFireAndForget("FORK-MEDIA-SWITCHER");
    }

    /// <summary>Re-lists the sessions (at most every 2 s unless forced) and updates the arrows.</summary>
    internal Task ForkRefreshSessionsAsync(bool force)
    {
        if (_cleanedUp) return Task.CompletedTask;
        if (_forkSessionsListing) return _forkSessionsTask;   // one listing at a time; callers wait for it
        if (!force && DateTime.UtcNow - _forkSessionsListedUtc < ForkSessionsRefreshEvery) return Task.CompletedTask;
        var source = ForkSessionSource?.Invoke();
        if (source == null)
        {
            _forkSessions = [];
            ForkUpdateSwitcherVisibility();
            return Task.CompletedTask;
        }
        _forkSessionsListing = true;
        return _forkSessionsTask = ForkListSessionsAsync(source);
    }

    private async Task ForkListSessionsAsync(IForkMediaSessions source)
    {
        try
        {
            var listed = await Task.Run(source.ForkListSessionsAsync);
            _forkSessions = MediaSwitcherLogic.Order(listed);
        }
        finally
        {
            _forkSessionsListing = false;
            _forkSessionsListedUtc = DateTime.UtcNow;
            ForkUpdateSwitcherVisibility();
        }
    }

    private void ForkUpdateSwitcherVisibility()
    {
        if (_forkSourcePill == null) return;
        var info = _currentMediaInfo;
        bool hasSource = info != null && !string.IsNullOrEmpty(info.SessionInstanceKey) &&
                         !string.IsNullOrWhiteSpace(info.CurrentTrack) && !info.IsMetadataPending;
        _forkSourcePill.Visibility = hasSource ? Visibility.Visible : Visibility.Collapsed;
        var arrows = hasSource && _forkSessions.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        _forkSourcePrev!.Visibility = arrows;
        _forkSourceNext!.Visibility = arrows;
    }

    internal void ForkStepSource(int direction)
    {
        var target = MediaSwitcherLogic.Step(_forkSessions, _currentMediaInfo?.SessionInstanceKey, direction);
        if (target == null || _mediaService.IsSessionPinned(target.Key)) return;
        string label = MediaSwitcherLogic.Label(
            _forkSourceLabels.GetValueOrDefault(target.Key), MediaSwitcherLogic.Classify(target));
        var pick = new MediaInfo { SessionInstanceKey = target.Key, SourceAppId = target.AppId, MediaSource = label, CurrentTrack = target.Title };
        if (!_mediaService.ToggleSessionPin(pick)) return;
        RefreshMediaPinBadges(animate: true);
        ForkShowSourceIcon(target.AppId);   // at once; upstream's media update follows
    }

    /// <summary>True when a picked source was released back to upstream's automatic choice.</summary>
    internal bool ForkReturnToAutomaticSource()
    {
        var info = _currentMediaInfo;
        if (info == null || !_mediaService.IsSessionPinned(info.SessionInstanceKey)) return false;
        if (_mediaService.ToggleSessionPin(info)) RefreshMediaPinBadges(animate: true);
        return true;
    }

    private void ForkShowSourceIcon(string appId)
    {
        if (_forkSourceIcon == null || appId == _forkSourceIconAppId) return;
        _forkSourceIconAppId = appId;
        _forkSourceIcon.Source = ForkPlatformLogo(appId);
        ForkLoadSourceIconAsync(appId).SafeFireAndForget("FORK-MEDIA-SWITCHER");
    }

    private async Task ForkLoadSourceIconAsync(string appId)
    {
        var icon = await ForkAppIcons.ForAppIdAsync(appId);
        if (icon != null && _forkSourceIcon != null && appId == _forkSourceIconAppId) _forkSourceIcon.Source = icon;
    }

    // Until (or unless) the real app icon resolves: upstream's own platform logos.
    private ImageSource? ForkPlatformLogo(string appId)
    {
        var platform = PlatformDetector.DetectFromAppId(appId);
        (string geometryKey, Brush brush) = platform switch
        {
            MediaPlatform.Spotify => ("SpotifyLogo", TryFindResource("SpotifyGreen") as Brush ?? Brushes.White),
            MediaPlatform.YouTube => ("YouTubeLogo", TryFindResource("YouTubeRed") as Brush ?? Brushes.White),
            MediaPlatform.SoundCloud => ("SoundCloudLogo", TryFindResource("SoundCloudOrange") as Brush ?? Brushes.White),
            MediaPlatform.Twitch => ("TwitchLogo", Brushes.White),
            MediaPlatform.Discord => ("DiscordLogo", Brushes.White),
            _ => ("BrowserLogo", Brushes.White),
        };
        if (TryFindResource(geometryKey) is not Geometry geometry) return null;
        var image = new DrawingImage(new GeometryDrawing(brush, null, geometry));
        image.Freeze();
        return image;
    }
}
