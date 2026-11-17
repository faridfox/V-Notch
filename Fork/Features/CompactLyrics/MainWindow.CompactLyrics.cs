using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VNotch.Controllers;
using VNotch.Fork;
using VNotch.Fork.Features.CompactLyrics;
using VNotch.Models;
using VNotch.Services;
using static VNotch.Services.AnimationPrimitives;

namespace VNotch;

// faridfox fork: "lyrics in the collapsed notch" (Fork/README.md ledger). While music shows in the collapsed notch,
// one line of text sits between the album art and the visualizer: the current lyric line with a per-word fade
// (Spotify, switch on) or "Title — Artist" (switch off, other players, no lyrics found). The pill has one fixed
// width; a line too long for it scrolls so the word being sung stays in view (CompactLineScroller).
//
// This is a fork-owned part of upstream's partial MainWindow, so every upstream member used below is checked by the
// compiler at each sync. Upstream calls in through one marked line in GetCollapsedWidth
// (Windows/MainWindow.AnimationHelpers.cs); Fork/Core/ForkUi.cs attaches it when the window loads.
public partial class MainWindow
{
    /// <summary>Width of the collapsed notch while it shows music (or the configured width, if that is larger).</summary>
    internal const double ForkCompactWidth = 340;

    private static readonly TimeSpan ForkCompactTickPlaying = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ForkCompactTickPaused = TimeSpan.FromMilliseconds(500);
    private static readonly Duration ForkCompactResizeDuration = new(TimeSpan.FromMilliseconds(420));
    private static readonly Color ForkCompactTextColor = Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF);

    private ForkSettingsService? _forkSettings;
    private Grid? _forkCompactHost;
    private TextBlock? _forkCompactText;
    private TranslateTransform? _forkCompactTextShift;
    private CompactKaraokeText? _forkKaraoke;
    private CompactLineScroller? _forkCompactScroller;
    private UiScheduledTimer? _forkCompactTimer;
    private string _forkCompactShown = "";
    private string _forkCompactShownKey = "";
    private KaraokeLine? _forkCompactShownLine;
    private int _forkCompactTransition;
    private (string Track, string Artist, int Duration, string Priority)? _forkLyricsKey;
    private List<LyricLine>? _forkLyricsLines;
    private int _forkLyricsGeneration;
    private (List<LyricLine> Lines, int Index, KaraokeLine? Line)? _forkBuiltLine;

    private readonly record struct ForkCompactContent(string Key, string Text, KaraokeLine? Line);

    internal bool ForkCompactTextAttached => _forkCompactHost != null;
    internal string ForkCompactTextShown => _forkCompactShown;
    internal CompactLineScroller? ForkCompactScroller => _forkCompactScroller;
    internal Grid? ForkCompactHost => _forkCompactHost;
    internal TextBlock? ForkCompactTextBlock => _forkCompactText;

    internal const double ForkCompactFontSize = 13;
    /// <summary>How far the clipping host reaches above and below the album-art row (it is 22 px tall in a 34 px pill).</summary>
    internal const double ForkCompactHostOverhang = 4;

    /// <summary>
    /// The line's TextBlock: 13 px Bold, upstream's title style and text policy (Ideal formatting, animated hinting).
    /// The padding keeps tall glyphs inside the line's own box: WPF sizes the line from the first font (SF Pro), while
    /// Arabic falls back to Segoe UI, whose letters reach further above and below.
    /// </summary>
    internal static TextBlock ForkCreateCompactTextBlock(Style? titleStyle)
    {
        // Never trimmed ("..."): a long line scrolls instead (CompactLineScroller).
        var text = new TextBlock
        {
            TextTrimming = TextTrimming.None,
            TextWrapping = TextWrapping.NoWrap,
            TextAlignment = TextAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        if (titleStyle != null && titleStyle.TargetType.IsAssignableFrom(typeof(TextBlock))) text.Style = titleStyle;
        text.FontSize = ForkCompactFontSize;
        text.FontWeight = FontWeights.Bold;
        text.Padding = new Thickness(0, 5, 0, 5);
        text.Foreground = new SolidColorBrush(ForkCompactTextColor);
        TextOptions.SetTextFormattingMode(text, TextFormattingMode.Ideal);
        TextOptions.SetTextHintingMode(text, TextHintingMode.Animated);
        return text;
    }

    /// <summary>
    /// The clipping host in the pill's middle column. It stretches over the whole album-art row and reaches
    /// <see cref="ForkCompactHostOverhang"/> px beyond it, so Arabic descenders and marks are never cut, while its desired
    /// height stays below the row's 22 px (the pill's layout is unchanged).
    /// </summary>
    internal static Grid ForkCreateCompactTextHost(TextBlock text) => ForkCreateCompactTextHost(text, out _, out _);

    /// <summary>
    /// The host, plus what scrolls the line: a Canvas (it measures the line at its full width, so nothing is cut with
    /// "...") holding a left-to-right wrapper that carries the horizontal scroll, around the TextBlock (which may be
    /// right-to-left). The wrapper is centred vertically in the host.
    /// </summary>
    internal static Grid ForkCreateCompactTextHost(TextBlock text, out FrameworkElement wrapper, out TranslateTransform scroll)
    {
        scroll = new TranslateTransform();
        var line = new Border { Child = text, FlowDirection = FlowDirection.LeftToRight, RenderTransform = scroll };
        var canvas = new Canvas();
        canvas.Children.Add(line);
        var host = new Grid
        {
            IsHitTestVisible = false,
            ClipToBounds = true,
            Margin = new Thickness(8, -ForkCompactHostOverhang, 6, -ForkCompactHostOverhang),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        host.Children.Add(canvas);
        void Centre() => Canvas.SetTop(line, Math.Round((host.ActualHeight - line.ActualHeight) / 2, 1));
        host.SizeChanged += (_, _) => Centre();
        line.SizeChanged += (_, _) => Centre();
        Grid.SetRow(host, 0);
        Grid.SetColumn(host, 1);
        wrapper = line;
        return host;
    }

    /// <summary>Called from upstream's GetCollapsedWidth: while music shows, the collapsed notch is the fixed fork width.</summary>
    internal double ForkCompactExtraWidth()
    {
        if (_forkCompactHost == null || !_isMusicCompactMode) return 0;
        double upstreamWidth = _settings.EnableDynamicIslandMode ? _settings.DynamicIslandWidth : _settings.Width;
        return Math.Max(0, ForkCompactWidth - upstreamWidth);
    }

    internal void ForkAttachCompactText(ForkSettingsService settings)
    {
        if (_forkCompactHost != null || _cleanedUp) return;
        _forkSettings = settings;

        _forkCompactTextShift = new TranslateTransform();
        var titleStyle = TryFindResource("TitleText") as Style;
        var text = ForkCreateCompactTextBlock(titleStyle);
        text.RenderTransform = _forkCompactTextShift;
        var host = ForkCreateCompactTextHost(text, out var wrapper, out var scroll);
        // Follow the album art. Every upstream surface that takes over the collapsed pill (volume, clipboard,
        // notifications, countdown) hides the art, so the text leaves with it instead of overlapping that surface.
        BindingOperations.SetBinding(host, OpacityProperty, new Binding(nameof(Opacity)) { Source = CompactThumbnailBorder });
        BindingOperations.SetBinding(host, VisibilityProperty, new Binding(nameof(Visibility)) { Source = CompactThumbnailBorder });
        MusicCompactContent.Children.Add(host);

        _forkCompactText = text;
        _forkCompactHost = host;
        _forkCompactScroller = new CompactLineScroller(host, wrapper, text, scroll);
        _forkKaraoke = new CompactKaraokeText(text, ForkCompactTextColor, _forkCompactScroller);
        host.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged) _forkKaraoke.Relayout();
        };

        _forkCompactTimer = new UiScheduledTimer(DispatcherPriority.Background) { Interval = ForkCompactTickPlaying };
        _forkCompactTimer.Tick += (_, _) => ForkRefreshCompactText();
        MusicCompactContent.IsVisibleChanged += (_, _) => ForkUpdateCompactTimer();
        settings.Changed += ForkOnSettingsChanged;
        Closed += (_, _) =>
        {
            settings.Changed -= ForkOnSettingsChanged;
            _forkCompactTimer?.Stop();
            _forkKaraoke?.Hold();
        };

        // Music may already be showing: take the fork's width now.
        _collapsedWidth = GetCollapsedWidth();
        if (_isMusicCompactMode && !_isExpanded && !_isAnimating)
            AnimateCompactWidth(_collapsedWidth, ForkCompactResizeDuration, _easeExpOut6, 0);
        ForkUpdateCompactTimer();
    }

    private void ForkOnSettingsChanged()
    {
        if (Dispatcher.CheckAccess()) ForkRefreshCompactText();
        else Dispatcher.BeginInvoke(new Action(ForkRefreshCompactText));
    }

    private void ForkUpdateCompactTimer()
    {
        if (_forkCompactTimer == null) return;
        if (MusicCompactContent.IsVisible && !_cleanedUp)
        {
            ForkRefreshCompactText();
            if (!_forkCompactTimer.IsEnabled) _forkCompactTimer.Start();
        }
        else
        {
            _forkCompactTimer.Stop();
            _forkKaraoke?.Hold(); // no animations while the pill is not on screen
        }
    }

    internal void ForkRefreshCompactText()
    {
        if (_forkCompactText == null || _forkSettings == null || _cleanedUp) return;
        var info = _currentMediaInfo;
        var playback = GetLyricsPlayback();
        var content = info == null
            ? new ForkCompactContent("", "", null)
            : ForkCompactContentFor(info, _forkSettings.ShowLyricsInCompactNotch, playback.Position);
        ForkShowCompactContent(content, playback.Position, playback.Playing);

        var interval = playback.Playing ? ForkCompactTickPlaying : ForkCompactTickPaused;
        if (_forkCompactTimer != null && _forkCompactTimer.Interval != interval) _forkCompactTimer.Interval = interval;
    }

    private ForkCompactContent ForkCompactContentFor(MediaInfo info, bool showLyrics, TimeSpan position)
    {
        string titleAndArtist = CompactLyricsText.TitleAndArtist(info.CurrentTrack, info.CurrentArtist);
        var plain = new ForkCompactContent("T|" + titleAndArtist, titleAndArtist, null);
        if (!showLyrics || !ForkCanLookUpLyrics(info)) return plain;
        var lines = ForkLyricsFor(info);
        if (lines == null) return plain;
        int index = CompactLyricsText.FindLineIndex(lines, position);
        if (index < 0) return plain;

        KaraokeLine? line;
        if (_forkBuiltLine is { } built && ReferenceEquals(built.Lines, lines) && built.Index == index) line = built.Line;
        else
        {
            line = CompactKaraokeTiming.Build(lines, index, info.Duration);
            _forkBuiltLine = (lines, index, line);
        }
        if (line == null) return new ForkCompactContent("I", CompactLyricsText.InstrumentalMark, null);
        return new ForkCompactContent("L|" + index + "|" + line.Text, line.Text, line);
    }

    // Same conditions as upstream's lyrics (Spotify only) and its privacy switches (online lyrics, local-only mode).
    private bool ForkCanLookUpLyrics(MediaInfo info) =>
        info.Platform == MediaPlatform.Spotify && !info.IsMetadataPending &&
        _settings.EnableOnlineLyrics && !_settings.EnableLocalOnlyMode &&
        !string.IsNullOrWhiteSpace(info.CurrentTrack) && !string.IsNullOrWhiteSpace(info.CurrentArtist) &&
        info.Duration > TimeSpan.Zero;

    private List<LyricLine>? ForkLyricsFor(MediaInfo info)
    {
        var key = (Track: info.CurrentTrack, Artist: info.CurrentArtist, Duration: (int)info.Duration.TotalSeconds,
            Priority: _settings.LyricsProviderPriority);
        if (_forkLyricsKey != key)
        {
            _forkLyricsKey = key;
            _forkLyricsLines = null;
            int generation = ++_forkLyricsGeneration;
            ForkFetchLyricsAsync(key, generation).SafeFireAndForget("FORK-COMPACT-LYRICS");
        }
        return _forkLyricsLines;
    }

    private async Task ForkFetchLyricsAsync((string Track, string Artist, int Duration, string Priority) key, int generation)
    {
        void Take(LyricsResult result)
        {
            if (generation != _forkLyricsGeneration || result.Lines.Count == 0) return;
            _forkLyricsLines = result.Lines;
            ForkRefreshCompactText();
        }

        // Exactly the arguments of upstream's PrefetchSpotifyLyrics / FetchLyricsForTrack. LyricsService keeps one
        // active request and a cache per key, so this joins upstream's request instead of starting (or cancelling)
        // another one: no extra network traffic. A later result with word timing replaces line-only lyrics.
        var result = await _lyricsService.FetchSyncedLyricsAsync(key.Track, key.Artist, key.Duration, key.Priority,
            onLyricsAvailable: available => Dispatcher.BeginInvoke(new Action(() => Take(available))));
        if (result != null) Take(result);
    }

    private void ForkShowCompactContent(ForkCompactContent content, TimeSpan position, bool playing)
    {
        if (_forkCompactText == null || _forkCompactTextShift == null || _forkKaraoke == null) return;
        var karaoke = _forkKaraoke;

        if (content.Key == _forkCompactShownKey)
        {
            // Same line; word timing may have arrived for it since (line-only result first, word timing later).
            if (content.Line != null && !ReferenceEquals(content.Line, _forkCompactShownLine))
            {
                _forkCompactShownLine = content.Line;
                karaoke.SetLine(content.Line);
            }
            if (_forkCompactShownLine != null) karaoke.Update(position, playing);
            return;
        }

        _forkCompactShownKey = content.Key;
        _forkCompactShownLine = content.Line;
        _forkCompactShown = content.Text;
        int version = ++_forkCompactTransition;

        void Apply()
        {
            if (content.Line != null)
            {
                karaoke.SetLine(content.Line);
                var now = GetLyricsPlayback();
                karaoke.Update(now.Position, now.Playing);
            }
            else
            {
                karaoke.SetText(content.Text);
            }
        }

        var block = _forkCompactText;
        var shift = _forkCompactTextShift;
        if (_forkCompactHost?.IsVisible != true || block.Inlines.Count == 0 || AnimationConfig.ReduceMotion)
        {
            block.BeginAnimation(OpacityProperty, null);
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            Apply();
            block.Opacity = 1;
            shift.Y = 0;
            return;
        }

        int fps = AnimationConfig.TargetFps;
        var fadeOut = MakeAnim(0, new Duration(TimeSpan.FromMilliseconds(110)), _easePowerOut3, fps);
        fadeOut.Completed += (_, _) =>
        {
            if (version != _forkCompactTransition) return;
            Apply();
            shift.BeginAnimation(TranslateTransform.YProperty, MakeAnim(5, 0, new Duration(TimeSpan.FromMilliseconds(260)), _easeExpOut6, fps));
            block.BeginAnimation(OpacityProperty, MakeAnim(0, 1, _dur200, _easeQuadOut, fps));
        };
        block.BeginAnimation(OpacityProperty, fadeOut);
    }
}
