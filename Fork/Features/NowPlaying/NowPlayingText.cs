using System;

namespace VNotch.Fork.Features.NowPlaying;

// faridfox fork: rules for the expanded now-playing additions. Pure, unit-tested in Tests/Fork.
internal static class NowPlayingText
{
    /// <summary>
    /// The album name to show under the title, or "" to hide the line: no track, metadata still loading, no album, or
    /// an album named like the track (singles), which would only repeat the title.
    /// </summary>
    public static string Album(string? track, string? album, bool metadataPending)
    {
        string name = album?.Trim() ?? "";
        string title = track?.Trim() ?? "";
        if (metadataPending || title.Length == 0 || name.Length == 0) return "";
        return string.Equals(name, title, StringComparison.CurrentCultureIgnoreCase) ? "" : name;
    }

    /// <summary>
    /// Left edge of the progress knob, so that its centre sits on the end of the played part of the bar.
    /// </summary>
    public static double KnobLeft(double progress, double barWidth, double knobSize)
    {
        if (!double.IsFinite(barWidth) || barWidth <= 0 || !double.IsFinite(progress)) return -knobSize / 2;
        return Math.Clamp(progress, 0, 1) * barWidth - knobSize / 2;
    }
}
