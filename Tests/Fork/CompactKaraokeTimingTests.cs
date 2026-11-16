using System;
using System.Collections.Generic;
using System.Linq;
using VNotch.Fork.Features.CompactLyrics;
using VNotch.Services;
using Xunit;

namespace VNotch.Tests.Fork;

// faridfox fork: word timing for the per-word fade (Fork/Features/CompactLyrics/CompactKaraokeTiming.cs).
public sealed class CompactKaraokeTimingTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void ProviderSyllablesAreMergedIntoWholeWords()
    {
        // "hello world": hel|lo world, offsets in the untrimmed text (one leading space).
        var line = new LyricLine(S(10), " hello world", [
            new LyricWord(S(10.0), S(10.3), 1, 3),
            new LyricWord(S(10.3), S(10.6), 4, 2),
            new LyricWord(S(10.8), S(11.5), 7, 5),
        ]);
        var built = CompactKaraokeTiming.Build([line], 0, S(200))!;

        Assert.Equal("hello world", built.Text);
        Assert.Equal([new WordSpan(0, 5, S(10.0), S(10.6)), new WordSpan(6, 5, S(10.8), S(11.5))], built.Words);
        Assert.False(built.RightToLeft);
    }

    [Fact]
    public void LineOnlyLyricsGetWordsInReadingOrderInsideTheLine()
    {
        var lines = new List<LyricLine> { new(S(10), "one two three"), new(S(30), "next") };
        var words = CompactKaraokeTiming.Build(lines, 0, S(200))!.Words;

        Assert.Equal(["one", "two", "three"], words.Select(w => "one two three".Substring(w.Offset, w.Length)));
        Assert.Equal(S(10), words[0].Start);
        for (int i = 1; i < words.Length; i++) Assert.True(words[i].Start >= words[i - 1].End);
        // A long gap before the next line is a pause, not a slow word: singing time is estimated from the text.
        TimeSpan estimate = CompactKaraokeTiming.EstimateBase + CompactKaraokeTiming.EstimatePerCharacter * "one two three".Length;
        Assert.True(words[^1].End <= S(10) + estimate);
        Assert.True(words[2].End - words[2].Start > words[0].End - words[0].Start, "longer words take longer");
    }

    [Fact]
    public void FastLinesAreSqueezedIntoTheTimeBeforeTheNextLine()
    {
        var lines = new List<LyricLine> { new(S(10), "a rather long line sung very quickly"), new(S(11.2), "next") };
        var words = CompactKaraokeTiming.Build(lines, 0, S(200))!.Words;
        Assert.True(words[^1].End <= S(11.2));
    }

    [Fact]
    public void TheLastLineEndsWithTheTrackOrAfterAFewSeconds()
    {
        var lines = new List<LyricLine> { new(S(100), "the very last line of the song we sing") };
        Assert.True(CompactKaraokeTiming.Build(lines, 0, S(101))!.Words[^1].End <= S(101));
        Assert.True(CompactKaraokeTiming.Build(lines, 0, S(500))!.Words[^1].End <= S(100) + CompactKaraokeTiming.LastLineSpan);
    }

    [Fact]
    public void ProviderTimingThatMissesAWordFallsBackToTheEstimate()
    {
        var line = new LyricLine(S(10), "hello world", [new LyricWord(S(10), S(10.5), 0, 5)]);
        var words = CompactKaraokeTiming.Build([line, new LyricLine(S(20), "x")], 0, S(200))!.Words;
        Assert.Equal(2, words.Length);
        Assert.True(words[1].Start > words[0].Start);
    }

    [Fact]
    public void EmptyLinesHaveNoWords() =>
        Assert.Null(CompactKaraokeTiming.Build([new LyricLine(S(1), "   ")], 0, S(10)));

    [Fact]
    public void ArabicLinesKeepWholeWordsInReadingOrder()
    {
        string text = "مرحبا بكم"; // مرحبا بكم
        var built = CompactKaraokeTiming.Build([new LyricLine(S(1), text), new LyricLine(S(5), "x")], 0, S(10))!;
        Assert.True(built.RightToLeft);
        Assert.Equal(2, built.Words.Length);
        Assert.Equal(0, built.Words[0].Offset);   // logical order: the first word read (rightmost) is sung first
        Assert.True(built.Words[1].Start >= built.Words[0].End);
    }

    [Theory]
    [InlineData(0.5, 0)]
    [InlineData(1, 0)]
    [InlineData(1.5, 0.5)]
    [InlineData(2, 1)]
    [InlineData(9, 1)]
    public void ProgressIsLinearInsideTheWord(double seconds, double expected) =>
        Assert.Equal(expected, CompactKaraokeTiming.Progress(new WordSpan(0, 1, S(1), S(2)), S(seconds)), 3);
}
