using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VNotch.Services;

namespace VNotch.Fork.Features.CompactLyrics;

// faridfox fork: where the collapsed-notch line sits horizontally. Pure, unit-tested in Tests/Fork.
// X is the line's offset inside the viewport (0 = its left edge at the viewport's left edge; negative = scrolled left).
internal static class CompactScroll
{
    /// <summary>The word being sung is kept this far into the viewport, from the side where reading starts.</summary>
    internal const double Focus = 0.4;

    public static bool Overflows(double view, double text) => view > 0 && text > view + 0.5;

    public static double Centered(double view, double text) => (view - text) / 2;

    public static double Clamp(double x, double view, double text) => Math.Clamp(x, Math.Min(0, view - text), 0);

    /// <summary>Where a line starts: its beginning in view (left end for left-to-right, right end for right-to-left).</summary>
    public static double Start(double view, double text, bool rtl) =>
        !Overflows(view, text) ? Centered(view, text) : rtl ? view - text : 0;

    /// <summary>The other end, for scrolling plain text that is too long.</summary>
    public static double End(double view, double text, bool rtl) =>
        !Overflows(view, text) ? Centered(view, text) : rtl ? 0 : view - text;

    /// <summary>X that puts the word spanning [left, right] (line coordinates) at the focus point, within the line's ends.</summary>
    public static double ForWord(double view, double text, double left, double right, bool rtl)
    {
        if (!Overflows(view, text)) return Centered(view, text);
        double focus = view * (rtl ? 1 - Focus : Focus);
        return Clamp(focus - (left + right) / 2, view, text);
    }
}

// faridfox fork: scrolls the collapsed-notch line inside its fixed-width viewport. A lyric line glides so the word
// being sung stays in view (scheduled as one key-frame animation per line, smooth at the frame rate); a plain line
// that is too long (title and artist) drifts end to end; a line that fits is centred. Right-to-left lines scroll the
// other way. The viewport's edges fade only while the line overflows.
//
// Layout: viewport (clips) > Canvas (measures the line at full width: no "...") > wrapper (left-to-right, carries the
// scroll transform) > TextBlock (may be right-to-left). Word positions are read in the wrapper's coordinates with
// TransformToAncestor, which handles a mirrored right-to-left TextBlock.
internal sealed class CompactLineScroller
{
    private const double FadeWidth = 14;
    private const double PlainSpeed = 34;                                  // px per second
    private static readonly TimeSpan PlainHold = TimeSpan.FromSeconds(1.4);
    private static readonly TimeSpan CatchUp = TimeSpan.FromMilliseconds(320);

    private readonly FrameworkElement _viewport;
    private readonly FrameworkElement _wrapper;
    private readonly TextBlock _block;
    private readonly TranslateTransform _scroll;
    private Run[] _runs = [];
    private (double Left, double Right)[] _wordX = [];
    private bool _rtl;
    private bool _plain;

    public CompactLineScroller(FrameworkElement viewport, FrameworkElement wrapper, TextBlock block, TranslateTransform scroll)
    {
        _viewport = viewport;
        _wrapper = wrapper;
        _block = block;
        _scroll = scroll;
    }

    public double ViewWidth { get; private set; }
    public double TextWidth { get; private set; }
    public bool Overflowing => CompactScroll.Overflows(ViewWidth, TextWidth);
    internal IReadOnlyList<(double Left, double Right)> WordPositions => _wordX;
    internal double Offset => _scroll.X;

    /// <summary>A new line: its word runs (empty for plain text) and its reading direction.</summary>
    public void SetContent(Run[] wordRuns, bool rtl, bool plain)
    {
        _runs = wordRuns;
        _rtl = rtl;
        _plain = plain;
        _wordX = [];
        _scroll.BeginAnimation(TranslateTransform.XProperty, null);
        Layout();
        _scroll.X = CompactScroll.Start(ViewWidth, TextWidth, _rtl);
        if (_plain) Drift();
    }

    /// <summary>Re-measures (after a size change or a new line). False while the viewport has no size yet.</summary>
    public bool Layout()
    {
        ViewWidth = _viewport.ActualWidth;
        _block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        TextWidth = _block.DesiredSize.Width;
        _viewport.OpacityMask = Overflowing ? EdgeFade(ViewWidth) : null;
        _wordX = [];
        if (ViewWidth <= 0) return false;
        if (Overflowing && _runs.Length > 0) _wordX = MeasureWords();
        return true;
    }

    private (double Left, double Right)[] MeasureWords()
    {
        _wrapper.UpdateLayout();
        var positions = new (double, double)[_runs.Length];
        var toWrapper = _block.TransformToAncestor(_wrapper);
        for (int i = 0; i < _runs.Length; i++)
        {
            var start = _runs[i].ContentStart.GetCharacterRect(LogicalDirection.Forward);
            var end = _runs[i].ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            if (start.IsEmpty || end.IsEmpty) return [];
            start.Union(end);
            var bounds = toWrapper.TransformBounds(start);
            positions[i] = (bounds.Left, bounds.Right);
        }
        return positions;
    }

    /// <summary>Glide with the singing from <paramref name="position"/> on (or jump there when paused or with reduced motion).</summary>
    public void Follow(IReadOnlyList<WordSpan> words, TimeSpan position, bool playing, bool reduceMotion)
    {
        if (_plain) return;
        if (!Overflowing)
        {
            _scroll.BeginAnimation(TranslateTransform.XProperty, null);
            _scroll.X = CompactScroll.Centered(ViewWidth, TextWidth);
            return;
        }
        if (_wordX.Length != words.Count && !Layout()) return;
        if (_wordX.Length != words.Count) return;

        int current = -1;
        for (int i = 0; i < words.Count; i++)
            if (words[i].Start <= position) current = i;
        double Target(int i) => i < 0
            ? CompactScroll.Start(ViewWidth, TextWidth, _rtl)
            : CompactScroll.ForWord(ViewWidth, TextWidth, _wordX[i].Left, _wordX[i].Right, _rtl);

        double from = _scroll.X;
        _scroll.BeginAnimation(TranslateTransform.XProperty, null);
        _scroll.X = from;
        if (!playing || reduceMotion)
        {
            _scroll.X = Target(current);
            return;
        }

        var frames = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
        frames.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(Target(current), KeyTime.FromTimeSpan(CatchUp),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        TimeSpan last = CatchUp;
        for (int i = current + 1; i < words.Count; i++)
        {
            TimeSpan at = words[i].Start - position;
            if (at <= last) at = last + TimeSpan.FromMilliseconds(1);
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(Target(i), KeyTime.FromTimeSpan(at)));
            last = at;
        }
        Timeline.SetDesiredFrameRate(frames, AnimationConfig.TargetFps);
        _scroll.BeginAnimation(TranslateTransform.XProperty, frames);
    }

    // Plain text too long for the viewport: drift to the other end and back, pausing at each end.
    private void Drift()
    {
        _scroll.BeginAnimation(TranslateTransform.XProperty, null);
        double start = CompactScroll.Start(ViewWidth, TextWidth, _rtl);
        _scroll.X = start;
        if (!Overflowing || AnimationConfig.ReduceMotion) return;
        double end = CompactScroll.End(ViewWidth, TextWidth, _rtl);
        var travel = TimeSpan.FromSeconds(Math.Abs(end - start) / PlainSpeed);
        var frames = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        var t = PlainHold;
        frames.KeyFrames.Add(new LinearDoubleKeyFrame(start, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        frames.KeyFrames.Add(new LinearDoubleKeyFrame(start, KeyTime.FromTimeSpan(t)));
        t += travel;
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(end, KeyTime.FromTimeSpan(t), new SineEase { EasingMode = EasingMode.EaseInOut }));
        t += PlainHold;
        frames.KeyFrames.Add(new LinearDoubleKeyFrame(end, KeyTime.FromTimeSpan(t)));
        t += travel;
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(start, KeyTime.FromTimeSpan(t), new SineEase { EasingMode = EasingMode.EaseInOut }));
        Timeline.SetDesiredFrameRate(frames, AnimationConfig.TargetFps);
        _scroll.BeginAnimation(TranslateTransform.XProperty, frames);
    }

    /// <summary>Re-applies the current content after the viewport changed size.</summary>
    public void Refresh()
    {
        if (!Layout()) return;
        if (_plain) Drift();
        else if (!Overflowing) _scroll.X = CompactScroll.Centered(ViewWidth, TextWidth);
    }

    /// <summary>Freeze where it is (the notch is hidden or the line is leaving).</summary>
    public void Hold()
    {
        double x = _scroll.X;
        _scroll.BeginAnimation(TranslateTransform.XProperty, null);
        _scroll.X = x;
    }

    private static Brush EdgeFade(double width)
    {
        double edge = Math.Min(0.2, FadeWidth / Math.Max(1, width));
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        brush.GradientStops.Add(new GradientStop(Colors.Black, edge));
        brush.GradientStops.Add(new GradientStop(Colors.Black, 1 - edge));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        brush.Freeze();
        return brush;
    }
}
