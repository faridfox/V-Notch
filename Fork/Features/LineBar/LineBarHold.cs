using System;
using System.Windows;

namespace VNotch.Fork.Features.LineBar;

/// <summary>What a pending press on the notch turned into.</summary>
internal enum HoldStep
{
    /// <summary>Still pressed, not long enough yet.</summary>
    Waiting,
    /// <summary>Released (or moved) early: a deferred press becomes the normal click it would have been.</summary>
    Click,
    /// <summary>Released (or moved) early, and nothing was deferred: nothing to do.</summary>
    Cancel,
    /// <summary>Held long enough without moving: turn the notch into the line bar.</summary>
    Hold,
}

// faridfox fork: press-and-hold detection for the line bar. Pure: the window polls the real mouse button, cursor and
// clock and feeds them in, so this is unit-tested without a mouse (Tests/Fork/LineBarTests).
internal sealed class LineBarHold
{
    /// <summary>How long the notch must be held.</summary>
    internal static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(450);
    /// <summary>Moving further than this (screen pixels) is a drag or a swipe, not a hold.</summary>
    internal const double MoveTolerance = 6;

    private TimeSpan _start;
    private Point _origin;

    public bool IsPending { get; private set; }
    /// <summary>The press was taken from upstream's click (notch collapsed, no gesture): replay it on an early release.</summary>
    public bool Deferred { get; private set; }
    public int ClickCount { get; private set; }

    public void Begin(TimeSpan now, Point cursor, bool deferred, int clickCount)
    {
        _start = now;
        _origin = cursor;
        Deferred = deferred;
        ClickCount = clickCount;
        IsPending = true;
    }

    public HoldStep Step(TimeSpan now, bool buttonDown, Point cursor)
    {
        if (!IsPending) return HoldStep.Cancel;
        double dx = cursor.X - _origin.X, dy = cursor.Y - _origin.Y;
        if (!buttonDown || dx * dx + dy * dy > MoveTolerance * MoveTolerance)
        {
            IsPending = false;
            return Deferred ? HoldStep.Click : HoldStep.Cancel;
        }
        if (now - _start < HoldTime) return HoldStep.Waiting;
        IsPending = false;
        return HoldStep.Hold;
    }

    public void Reset() => IsPending = false;
}
