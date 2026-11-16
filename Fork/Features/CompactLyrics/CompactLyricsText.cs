using System;
using System.Collections.Generic;
using VNotch.Services;

namespace VNotch.Fork.Features.CompactLyrics;

// faridfox fork: what the collapsed notch says. Pure functions, so the rules are unit-tested (Tests/Fork).
internal static class CompactLyricsText
{
    /// <summary>Shown for lyric lines without words (instrumental breaks).</summary>
    internal const string InstrumentalMark = "♪";
    internal const string Separator = " — ";

    /// <summary>"Title — Artist", or just the title when the artist is missing or only names a platform.</summary>
    public static string TitleAndArtist(string? title, string? artist)
    {
        string cleanTitle = title?.Trim() ?? "";
        string cleanArtist = CleanArtist(artist);
        if (cleanTitle.Length == 0) return cleanArtist;
        return cleanArtist.Length == 0 ? cleanTitle : cleanTitle + Separator + cleanArtist;
    }

    // Same rule as upstream's MediaDisplayController.ResolveDisplayText: a platform name is not an artist,
    // and YouTube's auto-generated " - Topic" channels lose that suffix.
    private static string CleanArtist(string? artist)
    {
        string text = artist?.Trim() ?? "";
        if (text.Length == 0) return "";
        if (MediaPlatformExtensions.ParsePlatform(text) is MediaPlatform.YouTube or MediaPlatform.Browser or MediaPlatform.Spotify)
            return "";
        const string topic = " - Topic";
        if (text.EndsWith(topic, StringComparison.OrdinalIgnoreCase)) text = text[..^topic.Length].Trim();
        return text;
    }

    /// <summary>Index of the line playing at <paramref name="position"/>; -1 before the first line.</summary>
    public static int FindLineIndex(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        if (lines.Count == 0 || position < lines[0].Time) return -1;
        int lo = 0, hi = lines.Count - 1, result = 0;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (lines[mid].Time <= position) { result = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return result;
    }

    /// <summary>The lyric line for <paramref name="position"/>, or null before the first line (caller shows the title).</summary>
    public static string? LineAt(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        int index = FindLineIndex(lines, position);
        if (index < 0) return null;
        string text = lines[index].Text?.Trim() ?? "";
        return text.Length == 0 ? InstrumentalMark : text;
    }
}
