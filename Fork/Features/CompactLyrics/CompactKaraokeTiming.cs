using System;
using System.Collections.Generic;
using VNotch.Services;

namespace VNotch.Fork.Features.CompactLyrics;

/// <summary>One word of the displayed line: where it is in the (trimmed) text and when it is sung.</summary>
internal readonly record struct WordSpan(int Offset, int Length, TimeSpan Start, TimeSpan End);

/// <summary>A lyric line as the collapsed notch shows it: trimmed text, its words, its reading direction.</summary>
internal sealed record KaraokeLine(string Text, WordSpan[] Words, bool RightToLeft);

// faridfox fork: word timing for the collapsed-notch karaoke. Pure functions, unit-tested in Tests/Fork.
//
// Works on whole words (whitespace-separated), never syllables: a run boundary inside an Arabic word would break
// letter joining, and the fade is per word anyway. Providers with word or syllable timing (AMLL, lrc mux, NetEase,
// Kugou) are merged into words; line-only lyrics (LRCLIB, QQ) get an estimate spread over the line by word length.
internal static class CompactKaraokeTiming
{
    /// <summary>How long the last line may run when nothing follows it.</summary>
    internal static readonly TimeSpan LastLineSpan = TimeSpan.FromSeconds(8);
    /// <summary>Estimated singing speed for line-only lyrics; the rest of the gap until the next line is a pause.</summary>
    internal static readonly TimeSpan EstimatePerCharacter = TimeSpan.FromMilliseconds(110);
    internal static readonly TimeSpan EstimateBase = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan MinimumLineSpan = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan Breath = TimeSpan.FromMilliseconds(150);

    /// <summary>The line at <paramref name="index"/> with per-word timing, or null for an empty (instrumental) line.</summary>
    public static KaraokeLine? Build(IReadOnlyList<LyricLine> lines, int index, TimeSpan trackDuration)
    {
        var line = lines[index];
        string raw = line.Text ?? "";
        int lead = 0;
        while (lead < raw.Length && char.IsWhiteSpace(raw[lead])) lead++;
        string text = raw.Trim();
        if (text.Length == 0) return null;

        var segments = Segments(text);
        var words = FromProviderTiming(line.Words, segments, lead) ?? Estimate(text, segments, line.Time, LineEnd(lines, index, trackDuration));
        return new KaraokeLine(text, words, IsRightToLeft(text));
    }

    /// <summary>0 before the word starts, 1 once it has been sung, linear in between.</summary>
    public static double Progress(WordSpan word, TimeSpan position)
    {
        if (position <= word.Start) return 0;
        if (position >= word.End) return 1;
        return (position - word.Start).TotalMilliseconds / (word.End - word.Start).TotalMilliseconds;
    }

    /// <summary>True when the first strong character is Hebrew, Arabic, Syriac, Thaana, N'Ko or another RTL script.</summary>
    public static bool IsRightToLeft(string text)
    {
        foreach (char c in text)
        {
            if (IsRtlChar(c)) return true;
            if (char.IsLetter(c)) return false;
        }
        return false;
    }

    private static bool IsRtlChar(char c) =>
        c is >= '֐' and <= 'ࣿ'    // Hebrew, Arabic, Syriac, Arabic Supplement, Thaana, N'Ko, Samaritan, Mandaic, Arabic Extended
          or >= 'יִ' and <= '﷿'    // Hebrew and Arabic presentation forms A
          or >= 'ﹰ' and <= '﻿';   // Arabic presentation forms B

    /// <summary>Whitespace-separated words of <paramref name="text"/> as (offset, length), in logical (reading) order.</summary>
    public static List<(int Offset, int Length)> Segments(string text)
    {
        var segments = new List<(int, int)>();
        int i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
            if (i > start) segments.Add((start, i - start));
        }
        return segments;
    }

    private static TimeSpan LineEnd(IReadOnlyList<LyricLine> lines, int index, TimeSpan trackDuration)
    {
        TimeSpan start = lines[index].Time;
        if (index + 1 < lines.Count && lines[index + 1].Time > start) return lines[index + 1].Time;
        TimeSpan end = start + LastLineSpan;
        return trackDuration > start && trackDuration < end ? trackDuration : end;
    }

    // Merge provider word/syllable timing into whole words. Null when it does not cover every word.
    private static WordSpan[]? FromProviderTiming(LyricWord[]? timed, List<(int Offset, int Length)> segments, int lead)
    {
        if (timed is not { Length: > 0 }) return null;
        var words = new WordSpan[segments.Count];
        for (int s = 0; s < segments.Count; s++)
        {
            var (offset, length) = segments[s];
            int from = offset + lead, to = offset + lead + length;
            TimeSpan? start = null, end = null;
            foreach (var w in timed)
            {
                if (w.Offset >= to || w.Offset + w.Length <= from) continue;
                if (start == null || w.Start < start) start = w.Start;
                if (end == null || w.End > end) end = w.End;
            }
            if (start == null || end == null) return null;
            words[s] = new WordSpan(offset, length, start.Value, end.Value < start.Value ? start.Value : end.Value);
        }
        return words;
    }

    // Line-only lyrics: spread an estimated singing time over the words by length (a trailing space counts as a pause).
    private static WordSpan[] Estimate(string text, List<(int Offset, int Length)> segments, TimeSpan start, TimeSpan lineEnd)
    {
        TimeSpan available = lineEnd - start - Breath;
        TimeSpan estimate = EstimateBase + EstimatePerCharacter * text.Length;
        TimeSpan span = available < estimate ? available : estimate;
        if (span < MinimumLineSpan) span = MinimumLineSpan;

        int weightTotal = 0;
        foreach (var (_, length) in segments) weightTotal += length + 1;
        var words = new WordSpan[segments.Count];
        int weightSoFar = 0;
        for (int s = 0; s < segments.Count; s++)
        {
            var (offset, length) = segments[s];
            TimeSpan wordStart = start + span * ((double)weightSoFar / weightTotal);
            weightSoFar += length + 1;
            TimeSpan wordEnd = start + span * ((double)(weightSoFar - 1) / weightTotal);
            words[s] = new WordSpan(offset, length, wordStart, wordEnd);
        }
        return words;
    }
}
