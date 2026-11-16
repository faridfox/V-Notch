using System;
using System.Collections.Generic;
using VNotch.Fork.Features.CompactLyrics;
using VNotch.Services;
using Xunit;

namespace VNotch.Tests.Fork;

// faridfox fork: what the collapsed notch shows (Fork/Features/CompactLyrics).
public sealed class CompactLyricsTextTests
{
    [Theory]
    [InlineData("Song", "Singer", "Song — Singer")]
    [InlineData("  Song ", " Singer ", "Song — Singer")]
    [InlineData("Song", "", "Song")]
    [InlineData("Song", null, "Song")]
    [InlineData("Song", "Singer - Topic", "Song — Singer")]
    [InlineData("Song", "YouTube", "Song")]
    [InlineData("Song", "Spotify", "Song")]
    [InlineData("", "Singer", "Singer")]
    [InlineData(null, null, "")]
    public void TitleAndArtistNamesTheSongAndARealArtistOnly(string? title, string? artist, string expected) =>
        Assert.Equal(expected, CompactLyricsText.TitleAndArtist(title, artist));

    private static readonly List<LyricLine> Lines =
    [
        new(TimeSpan.FromSeconds(5), "first line"),
        new(TimeSpan.FromSeconds(10), "second line"),
        new(TimeSpan.FromSeconds(15), "   "),
        new(TimeSpan.FromSeconds(20), "last line"),
    ];

    [Theory]
    [InlineData(0, -1)]
    [InlineData(4.99, -1)]
    [InlineData(5, 0)]
    [InlineData(9.99, 0)]
    [InlineData(10, 1)]
    [InlineData(17, 2)]
    [InlineData(20, 3)]
    [InlineData(500, 3)]
    public void FindLineIndexPicksTheLineThatHasStarted(double seconds, int expected) =>
        Assert.Equal(expected, CompactLyricsText.FindLineIndex(Lines, TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void LineAtReturnsNullBeforeTheFirstLineSoTheTitleShows() =>
        Assert.Null(CompactLyricsText.LineAt(Lines, TimeSpan.FromSeconds(1)));

    [Fact]
    public void LineAtShowsAMusicNoteForAnEmptyLine() =>
        Assert.Equal(CompactLyricsText.InstrumentalMark, CompactLyricsText.LineAt(Lines, TimeSpan.FromSeconds(16)));

    [Fact]
    public void LineAtReturnsTheCurrentLineText() =>
        Assert.Equal("second line", CompactLyricsText.LineAt(Lines, TimeSpan.FromSeconds(12)));

    [Fact]
    public void NoLinesMeansNoLine()
    {
        Assert.Equal(-1, CompactLyricsText.FindLineIndex([], TimeSpan.FromSeconds(3)));
        Assert.Null(CompactLyricsText.LineAt([], TimeSpan.FromSeconds(3)));
    }
}
