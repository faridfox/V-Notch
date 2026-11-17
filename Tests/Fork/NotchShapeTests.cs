using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VNotch.Services;
using VNotch.Tests.Fakes;
using Xunit;

namespace VNotch.Tests.Fork;

// faridfox fork: softer notch ears (Fork/Features/NotchShape).
[Collection("Localization")]
public sealed class NotchShapeTests
{
    [Fact]
    public void TheEarsAreWiderSofterAndMirrored() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        window.ForkAttachNotchShape();
        foreach (var ear in new[] { window.LeftEar, window.RightEar, window.LeftShadowEar, window.RightShadowEar })
        {
            Assert.Equal(MainWindow.ForkEarWidth, ear.Width);
            Assert.Equal(MainWindow.ForkEarHeight, ear.Height);
            Assert.Equal(new Rect(0, 0, MainWindow.ForkEarWidth, MainWindow.ForkEarHeight), ear.Data.Bounds);
        }
        // A concave fillet: filled in the corner between the screen edge and the notch's side, open below the curve.
        double w = MainWindow.ForkEarWidth, h = MainWindow.ForkEarHeight;
        var left = window.LeftEar.Data;     // the notch is on its right
        var right = window.RightEar.Data;   // the notch is on its left
        Assert.True(left.FillContains(new Point(w - 1, 1)));
        Assert.True(left.FillContains(new Point(w - 0.5, h / 2)));
        Assert.False(left.FillContains(new Point(w / 2, h / 2)));
        Assert.False(left.FillContains(new Point(1, h - 1)));
        Assert.True(right.FillContains(new Point(1, 1)));
        Assert.True(right.FillContains(new Point(0.5, h / 2)));
        Assert.False(right.FillContains(new Point(w / 2, h / 2)));
        Assert.False(right.FillContains(new Point(w - 1, h - 1)));
        Assert.Same(window.LeftEar.Data, window.LeftShadowEar.Data);          // shadow layer matches
    });

    private static GreetingAcceptanceTests.MainWindowFixture CreateFixture() =>
        new("en", greeting: false,
            configureServices: services => services.AddSingleton<IMediaDetectionService>(new FakeMediaDetectionService()));
}
