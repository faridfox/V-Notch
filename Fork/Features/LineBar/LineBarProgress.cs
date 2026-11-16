using System;

namespace VNotch.Fork.Features.LineBar;

// faridfox fork: what the line bar shows. Pure, unit-tested in Tests/Fork.
internal static class LineBarProgress
{
    /// <summary>
    /// Played fraction (0..1) of the loaded track, or null for a plain line: no track, no timeline (live streams,
    /// some browser media) or an unknown duration. A paused track keeps its progress.
    /// </summary>
    public static double? Fraction(string? track, bool hasTimeline, TimeSpan position, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(track) || !hasTimeline || duration <= TimeSpan.Zero) return null;
        return Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
    }
}
