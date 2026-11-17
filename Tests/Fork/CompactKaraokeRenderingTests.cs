using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VNotch.Fork.Features.CompactLyrics;
using Xunit;

namespace VNotch.Tests.Fork;

// faridfox fork: proves on real rendered pixels that the per-word fade runs in reading direction:
// from the left for English, from the right for Arabic (inside a word and from word to word).
public sealed class CompactKaraokeRenderingTests
{
    private const string Hello = "Hello";
    private const string World = "World";
    private const string Marhaba = "مرحبا";   // مرحبا
    private const string Salam = "سلام";           // سلام

    [Theory]
    [InlineData(Hello, false)]
    [InlineData(Marhaba, true)]
    public void HalfSungWordIsBrightOnTheSideWhereReadingStarts(string word, bool rightToLeft) => SharedStaTestRunner.RunAsync(async ct =>
    {
        // A quarter of the way in: the glow (brightest) is near the side where reading starts, the rest still dim.
        var line = new KaraokeLine(word, [new WordSpan(0, word.Length, TimeSpan.Zero, TimeSpan.FromSeconds(2))], rightToLeft);
        var (left, right) = RenderHalves(line, TimeSpan.FromSeconds(0.5));
        AssertBrighterAtStart(left, right, rightToLeft);
        await System.Threading.Tasks.Task.CompletedTask;
    });

    [Theory]
    [InlineData(Hello + " " + World, false)]
    [InlineData(Marhaba + " " + Salam, true)]
    public void FirstSungWordIsTheOneWhereReadingStarts(string text, bool rightToLeft) => SharedStaTestRunner.RunAsync(async ct =>
    {
        var segments = CompactKaraokeTiming.Segments(text);
        var line = new KaraokeLine(text,
        [
            new WordSpan(segments[0].Offset, segments[0].Length, TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new WordSpan(segments[1].Offset, segments[1].Length, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)),
        ], rightToLeft);
        var (left, right) = RenderHalves(line, TimeSpan.FromSeconds(1));
        AssertBrighterAtStart(left, right, rightToLeft);
        await System.Threading.Tasks.Task.CompletedTask;
    });

    // Mixed lines: an Arabic word inside an English line fades from its right edge; an English word inside an Arabic
    // line fades from its left edge. Only the mixed-in word is half sung; the other word is not started.
    [Theory]
    [InlineData(Hello, Marhaba, false, true)]
    [InlineData(Marhaba, Hello, true, false)]
    public void EachWordFadesInItsOwnReadingDirection(string first, string second, bool lineRtl, bool secondRtl) =>
        SharedStaTestRunner.RunAsync(async ct =>
        {
            string text = first + " " + second;
            var segments = CompactKaraokeTiming.Segments(text);
            var line = new KaraokeLine(text,
            [
                new WordSpan(segments[0].Offset, segments[0].Length, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(11)),
                new WordSpan(segments[1].Offset, segments[1].Length, TimeSpan.Zero, TimeSpan.FromSeconds(2)),
            ], lineRtl);
            var (left, right) = RenderHalves(line, TimeSpan.FromSeconds(0.5), onlyWord: 1);
            AssertBrighterAtStart(left, right, secondRtl);
            await System.Threading.Tasks.Task.CompletedTask;
        });

    [Fact]
    public void ArabicLinesAreDetectedAsRightToLeft()
    {
        Assert.True(CompactKaraokeTiming.IsRightToLeft(Marhaba + " " + Salam));
        Assert.True(CompactKaraokeTiming.IsRightToLeft("  123 " + Marhaba + " hello"));
        Assert.False(CompactKaraokeTiming.IsRightToLeft("hello " + Marhaba));
        Assert.False(CompactKaraokeTiming.IsRightToLeft("123 ..."));
        Assert.True(CompactKaraokeTiming.IsRightToLeft("שלום")); // Hebrew
    }

    private static void AssertBrighterAtStart(double left, double right, bool rightToLeft)
    {
        double start = rightToLeft ? right : left;
        double end = rightToLeft ? left : right;
        Assert.True(start > end * 1.3, $"reading-start half {start:F0} should be clearly brighter than the other half {end:F0}");
    }

    // Renders the line white-on-black inside a left-to-right parent (as in the notch) and sums brightness per half.
    private static (double Left, double Right) RenderHalves(KaraokeLine line, TimeSpan position, int? onlyWord = null)
    {
        var block = new TextBlock { FontSize = 40, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
        TextOptions.SetTextFormattingMode(block, TextFormattingMode.Ideal);
        var karaoke = new CompactKaraokeText(block, Colors.White);
        karaoke.SetLine(line);
        karaoke.Update(position, playing: false);

        var root = new Grid { Background = Brushes.Black, FlowDirection = FlowDirection.LeftToRight };
        root.Children.Add(block);
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();

        int width = (int)Math.Ceiling(root.ActualWidth), height = (int)Math.Ceiling(root.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        // TransformBounds, not TranslatePoint(0,0): a right-to-left element is mirrored, so its (0,0) is its right edge.
        var bounds = block.TransformToAncestor(root).TransformBounds(new Rect(block.RenderSize));
        if (onlyWord is int index)
        {
            var span = line.Words[index];
            string word = line.Text.Substring(span.Offset, span.Length);
            var run = System.Linq.Enumerable.Single(System.Linq.Enumerable.OfType<System.Windows.Documents.Run>(block.Inlines), r => r.Text == word);
            var caretA = run.ContentStart.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward);
            var caretB = run.ContentEnd.GetCharacterRect(System.Windows.Documents.LogicalDirection.Backward);
            caretA.Union(caretB);
            bounds = block.TransformToAncestor(root).TransformBounds(caretA);
            Assert.True(bounds.Width > 20, "the word must render");
        }
        Assert.True(bounds.Width > 20, "the line must render");
        int x0 = (int)Math.Floor(bounds.Left), x1 = (int)Math.Ceiling(bounds.Right);
        int mid = (x0 + x1) / 2;
        double left = 0, right = 0;
        for (int y = 0; y < height; y++)
        for (int x = Math.Max(0, x0); x < Math.Min(width, x1); x++)
        {
            int i = (y * width + x) * 4;
            double brightness = pixels[i] + pixels[i + 1] + pixels[i + 2];
            if (x < mid) left += brightness; else right += brightness;
        }
        return (left, right);
    }
}
