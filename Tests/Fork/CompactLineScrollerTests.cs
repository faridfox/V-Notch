using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VNotch.Fork.Features.CompactLyrics;
using VNotch.Services;
using Xunit;

namespace VNotch.Tests.Fork;

// faridfox fork: the collapsed-notch line scrolls so the word being sung stays in view (CompactLineScroller).
public sealed class CompactLineScrollerTests
{
    // ---- where to scroll (pure) ---------------------------------------------------------------------------------

    [Fact]
    public void ALineThatFitsIsCentred()
    {
        Assert.False(CompactScroll.Overflows(200, 120));
        Assert.Equal(40, CompactScroll.Start(200, 120, rtl: false));
        Assert.Equal(40, CompactScroll.Start(200, 120, rtl: true));
        Assert.Equal(40, CompactScroll.ForWord(200, 120, 0, 10, rtl: false));
    }

    [Fact]
    public void ALongLineStartsAtTheSideWhereReadingStarts()
    {
        Assert.Equal(0, CompactScroll.Start(200, 500, rtl: false));     // left end in view
        Assert.Equal(-300, CompactScroll.Start(200, 500, rtl: true));   // right end in view
        Assert.Equal(-300, CompactScroll.End(200, 500, rtl: false));
        Assert.Equal(0, CompactScroll.End(200, 500, rtl: true));
    }

    [Theory]
    [InlineData(300, 340, false, -240)]   // word centre 320 kept at 40 % of 200 = 80
    [InlineData(10, 30, false, 0)]        // near the start: cannot scroll past the line's beginning
    [InlineData(470, 500, false, -300)]   // near the end: cannot scroll past the line's end
    [InlineData(300, 340, true, -200)]    // right-to-left: kept at 60 % of 200 = 120
    [InlineData(470, 500, true, -300)]    // right-to-left start (rightmost word)
    public void TheSungWordIsKeptNearTheReadingSide(double left, double right, bool rtl, double expected) =>
        Assert.Equal(expected, CompactScroll.ForWord(200, 500, left, right, rtl), 3);

    // ---- on real layout -----------------------------------------------------------------------------------------

    private const string LongEnglish = "and I said hey what is going on with all of these words in a very long line";
    private const string LongArabic = "برتاح وبطمن وأنا في حضنك أخاف من إيه برتاح وبطمن وأنا في حضنك";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EachSungWordEndsUpInsideTheViewport(bool arabic) => SharedStaTestRunner.RunAsync(async ct =>
    {
        string text = arabic ? LongArabic : LongEnglish;
        var segments = CompactKaraokeTiming.Segments(text);
        var line = new KaraokeLine(text,
            segments.Select((w, i) => new WordSpan(w.Offset, w.Length, TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(i + 0.9))).ToArray(),
            CompactKaraokeTiming.IsRightToLeft(text));

        var block = MainWindow.ForkCreateCompactTextBlock(null);
        block.FontFamily = AppFonts.Get("Nirmala UI, Segoe UI");
        var host = MainWindow.ForkCreateCompactTextHost(block, out var wrapper, out var scroll);
        host.Width = 220;
        host.Height = 30;
        host.Margin = new Thickness(0);
        var root = new Grid { Width = 220, Height = 30 };
        root.Children.Add(host);
        root.Measure(new Size(220, 30));
        root.Arrange(new Rect(0, 0, 220, 30));
        root.UpdateLayout();

        var scroller = new CompactLineScroller(host, wrapper, block, scroll);
        var karaoke = new CompactKaraokeText(block, Colors.White, scroller);
        karaoke.SetLine(line);
        Assert.True(scroller.Overflowing, "the test line must be longer than the viewport");
        Assert.Equal(line.Words.Length, scroller.WordPositions.Count);
        Assert.NotNull(host.OpacityMask);                      // edges fade while the line overflows

        for (int i = 0; i < line.Words.Length; i++)
        {
            karaoke.Update(TimeSpan.FromSeconds(i + 0.1), playing: false);   // paused: the scroll jumps there
            var (left, right) = scroller.WordPositions[i];
            double x = scroll.X;
            Assert.True(left + x >= -0.5 && right + x <= 220.5,
                $"word {i} ({left:F0}..{right:F0}) must be inside the 220 px viewport at offset {x:F0}");
        }

        // The first word is where reading starts: the left end for English, the right end for Arabic.
        karaoke.Update(TimeSpan.FromSeconds(0.1), playing: false);
        Assert.Equal(arabic ? 220 - scroller.TextWidth : 0, scroll.X, 1);
        await System.Threading.Tasks.Task.CompletedTask;
    });

    [Fact]
    public void AShortLineIsCentredWithoutFade() => SharedStaTestRunner.RunAsync(async ct =>
    {
        var block = MainWindow.ForkCreateCompactTextBlock(null);
        var host = MainWindow.ForkCreateCompactTextHost(block, out var wrapper, out var scroll);
        host.Width = 220;
        host.Height = 30;
        var root = new Grid { Width = 240, Height = 30 };
        root.Children.Add(host);
        root.Measure(new Size(240, 30));
        root.Arrange(new Rect(0, 0, 240, 30));
        root.UpdateLayout();
        var scroller = new CompactLineScroller(host, wrapper, block, scroll);
        new CompactKaraokeText(block, Colors.White, scroller).SetText("Short");
        Assert.False(scroller.Overflowing);
        Assert.Null(host.OpacityMask);
        Assert.Equal((scroller.ViewWidth - scroller.TextWidth) / 2, scroll.X, 1);
        await System.Threading.Tasks.Task.CompletedTask;
    });
}
