using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VNotch.Services;
using Xunit;

namespace VNotch.Tests.Fork;

// faridfox fork: the collapsed-notch lyric line must never crop Arabic. Renders a deep-descender Arabic line in a mock of
// the 34 px pill (upstream's MusicCompactContent layout) with the line's clip on and off, and compares where the ink
// reaches (top, bottom, left, right). Pixel-for-pixel equality is not usable: a clip changes text antialiasing slightly.
// The old layout is rendered the same way and must lose ink, so this test can actually see a crop.
public sealed class CompactLyricsClipTests
{
    // Letters that reach far below the line (ي ج ع م ح ن) and marks above and below (kasratan, shadda).
    private const string DeepArabic = "يجيء حينٍ مع عينٍ مُرَّب";

    [Theory]
    [InlineData(34)]   // notch mode, default height
    [InlineData(40)]   // Dynamic Island, default height
    public void TheLyricLineNeverCropsArabic(double pillHeight) => SharedStaTestRunner.RunAsync(async ct =>
    {
        var clipped = Ink(RenderPill(pillHeight, clip: true, current: true), pillHeight);
        var free = Ink(RenderPill(pillHeight, clip: false, current: true), pillHeight);
        Assert.True(free.Bottom > free.Top, "the line must render");
        Assert.Equal(free, clipped);
        await System.Threading.Tasks.Task.CompletedTask;
    });

    [Fact]
    public void TheOldLayoutDidCropArabic() => SharedStaTestRunner.RunAsync(async ct =>
    {
        var clipped = Ink(RenderPill(34, clip: true, current: false), 34);
        var free = Ink(RenderPill(34, clip: false, current: false), 34);
        Assert.True(clipped.Bottom < free.Bottom || clipped.Top > free.Top,
            $"the old 11 px layout is expected to crop (clipped {clipped}, free {free}): otherwise this test cannot see a crop");
        await System.Threading.Tasks.Task.CompletedTask;
    });

    private readonly record struct InkBox(int Left, int Top, int Right, int Bottom);

    // Rows and columns that hold clearly visible ink (white text on black).
    private static InkBox Ink(byte[] pixels, double height)
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (int y = 0; y < (int)height; y++)
        for (int x = 0; x < Width; x++)
        {
            int i = (y * Width + x) * 4;
            if (Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])) < 60) continue;
            left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        return new InkBox(left, top, right, bottom);
    }

    [Fact]
    public void TheHostDoesNotGrowTheAlbumArtRow() => SharedStaTestRunner.RunAsync(async ct =>
    {
        var (pill, row, _) = BuildPill(34, clip: true, current: true);
        Layout(pill, 34);
        Assert.Equal(22, row.RowDefinitions[0].ActualHeight, 2);
        await System.Threading.Tasks.Task.CompletedTask;
    });

    private static byte[] RenderPill(double height, bool clip, bool current)
    {
        var (pill, _, _) = BuildPill(height, clip, current);
        Layout(pill, height);
        var bitmap = new RenderTargetBitmap(Width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(pill);
        var pixels = new byte[Width * (int)height * 4];
        bitmap.CopyPixels(pixels, Width * 4, 0);
        return pixels;
    }

    private const int Width = 420;

    // Upstream's collapsed music row: Margin 8,4; rows Auto/Auto; columns: 22 px album art | text | 20 px visualizer.
    private static (Grid Pill, Grid Row, Grid Host) BuildPill(double height, bool clip, bool current)
    {
        var font = AppFonts.Get("Nirmala UI, Segoe UI");   // upstream's MainSystemFont: SF Pro first, Segoe UI for Arabic
        TextBlock text;
        Grid host;
        if (current)
        {
            text = MainWindow.ForkCreateCompactTextBlock(null);
            host = MainWindow.ForkCreateCompactTextHost(text);
        }
        else
        {
            text = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
            host = new Grid { ClipToBounds = true, Margin = new Thickness(8, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            host.Children.Add(text);
            Grid.SetColumn(host, 1);
        }
        text.FontFamily = font;
        text.Foreground = Brushes.White;
        text.FlowDirection = FlowDirection.RightToLeft;
        text.Text = DeepArabic;
        TextOptions.SetTextFormattingMode(text, TextFormattingMode.Ideal);
        host.ClipToBounds = clip;

        var row = new Grid { Margin = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Top };
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var art = new Border { Width = 22, Height = 22 };
        var viz = new Border { Width = 20, Height = 17, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(viz, 2);
        row.Children.Add(art);
        row.Children.Add(host);
        row.Children.Add(viz);

        var pill = new Grid { Width = Width, Height = height, Background = Brushes.Black };
        pill.Children.Add(row);
        return (pill, row, host);
    }

    private static void Layout(FrameworkElement pill, double height)
    {
        pill.Measure(new Size(Width, height));
        pill.Arrange(new Rect(0, 0, Width, height));
        pill.UpdateLayout();
    }
}
