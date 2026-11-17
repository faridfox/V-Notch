using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VNotch.Services;

namespace VNotch.Fork.Features.CompactLyrics;

// faridfox fork: per-word fade for the collapsed-notch line. Each word starts dim and brightens with a soft edge
// that sweeps through it in reading direction while it is sung (left to right, or right to left for Arabic/Hebrew).
//
// Like upstream's KaraokeLyricsPresenter, a whole line is scheduled at once as WPF animations (smooth at the frame
// rate, independent of the 100 ms refresh), and rescheduled only on play/pause, seeks or drift. Differences: whole
// words only (Arabic letters stay joined); RTL lines get a right-to-left paragraph (word order: the first word sung is
// the rightmost), and each RTL word gets a right-to-left gradient (the sweep starts at its right edge). Upstream's
// presenter does neither. Proved on rendered pixels by Tests/Fork/CompactKaraokeRenderingTests.
internal sealed class CompactKaraokeText
{
    /// <summary>Half-width of the glow band that travels through a word, as a fraction of the word.</summary>
    internal const double SoftEdge = 0.45;
    /// <summary>Opacity of words not sung yet: the dim level of upstream's "Searching for lyrics..." shimmer.</summary>
    internal const byte PendingAlpha = 0x69;
    /// <summary>Opacity of words already sung (the glow itself is fully white).</summary>
    internal const byte LitAlpha = 0xE6;
    private static readonly TimeSpan ResyncTolerance = TimeSpan.FromMilliseconds(120);

    // Upstream's shimmer look applied to singing: a bright glow with soft edges travels through each word while it is
    // sung, leaving it lit. Three stops per word: lit (behind the glow), glow, dim (ahead of it).
    private sealed class Word(Run run, WordSpan span, GradientStop lit, GradientStop glow, GradientStop dim)
    {
        internal Run Run { get; } = run;
        internal WordSpan Span { get; } = span;
        internal GradientStop Lit { get; } = lit;
        internal GradientStop Glow { get; } = glow;
        internal GradientStop Dim { get; } = dim;

        internal void Hold()
        {
            foreach (var stop in new[] { Lit, Glow, Dim })
            {
                double offset = stop.Offset;
                stop.BeginAnimation(GradientStop.OffsetProperty, null);
                stop.Offset = offset;
            }
        }

        /// <summary>The glow's centre for a progress: before the word at 0, past it at 1.</summary>
        internal static double Centre(double progress) => -SoftEdge + (1 + 2 * SoftEdge) * progress;

        internal void Set(double progress)
        {
            double centre = Centre(progress);
            Lit.Offset = centre - SoftEdge;
            Glow.Offset = centre;
            Dim.Offset = centre + SoftEdge;
        }
    }

    private readonly TextBlock _block;
    private readonly Color _sung;
    private readonly Color _lit;
    private readonly Color _glow;
    private readonly Color _pending;
    private readonly SolidColorBrush _sungBrush;
    private readonly SolidColorBrush _pendingBrush;
    private Word[] _words = [];
    private readonly CompactLineScroller? _scroller;
    private bool _scheduled, _playing, _reduceMotion;
    private TimeSpan _scheduledPosition;
    private long _scheduledTick;

    internal CompactKaraokeText(TextBlock block, Color sung, CompactLineScroller? scroller = null)
    {
        _block = block;
        _scroller = scroller;
        _sung = sung;
        _pending = Color.FromArgb(PendingAlpha, sung.R, sung.G, sung.B);
        _lit = Color.FromArgb(LitAlpha, sung.R, sung.G, sung.B);
        _glow = Color.FromArgb(0xFF, sung.R, sung.G, sung.B);
        _sungBrush = Frozen(_sung);
        _pendingBrush = Frozen(_pending);
    }

    internal bool HasWords => _words.Length > 0;

    /// <summary>Plain text at full brightness (title and artist, the instrumental mark).</summary>
    internal void SetText(string text)
    {
        bool rtl = CompactKaraokeTiming.IsRightToLeft(text);
        Reset(rtl);
        _block.Inlines.Add(new Run(text) { Foreground = _sungBrush });
        _scroller?.SetContent([], rtl, plain: true);
    }

    /// <summary>A lyric line with every word dim; call <see cref="Update"/> to start the fade.</summary>
    internal void SetLine(KaraokeLine line)
    {
        Reset(line.RightToLeft);
        var words = new Word[line.Words.Length];
        int offset = 0;
        for (int i = 0; i < words.Length; i++)
        {
            var span = line.Words[i];
            if (span.Offset > offset) _block.Inlines.Add(new Run(line.Text[offset..span.Offset]) { Foreground = _pendingBrush });
            string text = line.Text.Substring(span.Offset, span.Length);
            var lit = new GradientStop(_lit, -2 * SoftEdge);
            var glow = new GradientStop(_glow, -SoftEdge);
            var dim = new GradientStop(_pending, 0);
            // A run's brush is laid out over its glyphs left to right even inside a mirrored right-to-left paragraph,
            // so an Arabic/Hebrew word needs a right-to-left gradient of its own; a Latin word or number inside an
            // Arabic line still reads left to right. (Tests/Fork/CompactKaraokeRenderingTests checks the pixels.)
            bool rtlWord = CompactKaraokeTiming.IsRightToLeft(text);
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(rtlWord ? 1 : 0, 0.5),
                EndPoint = new Point(rtlWord ? 0 : 1, 0.5),
            };
            brush.GradientStops.Add(lit);
            brush.GradientStops.Add(glow);
            brush.GradientStops.Add(dim);
            var run = new Run(text) { Foreground = brush };
            _block.Inlines.Add(run);
            words[i] = new Word(run, span, lit, glow, dim);
            offset = span.Offset + span.Length;
        }
        if (offset < line.Text.Length) _block.Inlines.Add(new Run(line.Text[offset..]) { Foreground = _pendingBrush });
        _words = words;
        _scroller?.SetContent(Array.ConvertAll(words, w => w.Run), line.RightToLeft, plain: false);
    }

    /// <summary>Bring the fade to <paramref name="position"/>; cheap when nothing changed since the last schedule.</summary>
    internal void Update(TimeSpan position, bool playing)
    {
        if (_words.Length == 0) return;
        long tick = Stopwatch.GetTimestamp();
        bool reduceMotion = AnimationConfig.ReduceMotion;
        double elapsed = _scheduledTick == 0 ? 0 : (tick - _scheduledTick) / (double)Stopwatch.Frequency;
        TimeSpan expected = _scheduledPosition + (_playing ? TimeSpan.FromSeconds(elapsed) : TimeSpan.Zero);
        bool resync = !_scheduled || playing != _playing || reduceMotion != _reduceMotion ||
            ((!playing || reduceMotion) && position != _scheduledPosition) ||
            (position - expected).Duration() > ResyncTolerance;
        if (!resync) return;

        _scheduled = true;
        _playing = playing;
        _reduceMotion = reduceMotion;
        _scheduledTick = tick;
        _scheduledPosition = position;
        int fps = AnimationConfig.TargetFps;
        foreach (var word in _words)
        {
            word.Hold();
            double progress = CompactKaraokeTiming.Progress(word.Span, position);
            word.Set(progress);
            if (!playing || reduceMotion || progress >= 1) continue;
            TimeSpan delay = word.Span.Start > position ? word.Span.Start - position : TimeSpan.Zero;
            TimeSpan remaining = word.Span.End - (word.Span.Start > position ? word.Span.Start : position);
            if (remaining <= TimeSpan.Zero) { word.Set(1); continue; }
            double end = Word.Centre(1);
            Animate(word.Lit, end - SoftEdge, remaining, delay, fps);
            Animate(word.Glow, end, remaining, delay, fps);
            Animate(word.Dim, end + SoftEdge, remaining, delay, fps);
        }
        _scroller?.Follow(Array.ConvertAll(_words, w => w.Span), position, playing, reduceMotion);
    }

    /// <summary>The viewport changed size: measure again, and re-plan the scroll at the next update.</summary>
    internal void Relayout()
    {
        _scroller?.Refresh();
        _scheduled = false;
    }

    /// <summary>Freeze the fade where it is (the line is leaving or the notch is hidden).</summary>
    internal void Hold()
    {
        foreach (var word in _words) word.Hold();
        _scroller?.Hold();
        _scheduled = false;
    }

    private void Reset(bool rightToLeft)
    {
        Hold();
        _words = [];
        _scheduledTick = 0;
        _block.FlowDirection = rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _block.Inlines.Clear();
    }

    private static void Animate(GradientStop stop, double to, TimeSpan duration, TimeSpan delay, int fps)
    {
        var animation = new DoubleAnimation(stop.Offset, to, duration) { BeginTime = delay };
        Timeline.SetDesiredFrameRate(animation, fps);
        stop.BeginAnimation(GradientStop.OffsetProperty, animation);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
