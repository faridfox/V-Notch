using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using VNotch.Fork;
using VNotch.Models;
using VNotch.Services;
using VNotch.Tests.Fakes;
using Xunit;

namespace VNotch.Tests.Fork;

// faridfox fork: tabs position and size (Fork/Features/NavTabs).
[Collection("Localization")]
public sealed class TabsPillTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "vnotch-fork-tabs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void PositionAndSizeAreSavedWithTheForkSettings()
    {
        var settings = new ForkSettingsService(_folder);
        Assert.Equal(ForkTabsPosition.Top, settings.TabsPosition);
        Assert.Equal(ForkTabsSize.Default, settings.TabsSize);
        settings.PreviewTabs(ForkTabsPosition.Bottom, ForkTabsSize.Larger);
        settings.CommitPreview();
        var reloaded = new ForkSettingsService(_folder);
        Assert.Equal(ForkTabsPosition.Bottom, reloaded.TabsPosition);
        Assert.Equal(ForkTabsSize.Larger, reloaded.TabsSize);
        Assert.Contains("\"Bottom\"", File.ReadAllText(reloaded.FilePath));   // readable names, not numbers
        Assert.True(reloaded.ShowLyricsInCompactNotch);                        // other settings untouched
    }

    [Fact]
    public void TheRealTabRowMovesIntoThePillAndBack() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        var settings = new ForkSettingsService(_folder);
        var home = (Panel)window.NavIconsPanel.Parent;
        int index = home.Children.IndexOf(window.NavIconsPanel);
        window.ForkAttachTabsPill(settings);
        Assert.Same(home, window.NavIconsPanel.Parent);                      // Top: upstream's place

        settings.PreviewTabs(ForkTabsPosition.Bottom, ForkTabsSize.Default);
        Assert.Same(window.ForkTabsPill, window.NavIconsPanel.Parent);
        Assert.Same(window.NotchWrapper, ((FrameworkElement)window.ForkTabsPill!.Parent).Parent);   // clicks reach it
        Assert.Equal(1, Grid.GetColumn((UIElement)window.ForkTabsPill.Parent));                   // under the notch
        Assert.True(window.NavTabsStackPanel.LayoutTransform.Value.IsIdentity);                   // a row

        settings.PreviewTabs(ForkTabsPosition.Left, ForkTabsSize.Large);
        Assert.Equal(90, ((RotateTransform)window.NavTabsStackPanel.LayoutTransform).Angle);      // upright column
        var glyph = (FrameworkElement)((Border)window.NavTabsStackPanel.Children[0]).Child;
        Assert.Equal(-90, ((RotateTransform)glyph.LayoutTransform).Angle);                        // icons stay upright
        Assert.Equal(1.2, ((ScaleTransform)window.ForkTabsPill.LayoutTransform).ScaleX, 3);

        settings.RevertPreview();                                                                  // back to Top
        Assert.Same(home, window.NavIconsPanel.Parent);
        Assert.Equal(index, home.Children.IndexOf(window.NavIconsPanel));
        Assert.True(window.NavTabsStackPanel.LayoutTransform.Value.IsIdentity);
        Assert.True(glyph.LayoutTransform.Value.IsIdentity);
        Assert.Equal(new Thickness(0), window.NavIconsPanel.Margin);
    });

    [Fact]
    public void TheHiddenPillNeverCountsAsPartOfTheNotch() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        var settings = new ForkSettingsService(_folder);
        window.ForkAttachTabsPill(settings);
        settings.PreviewTabs(ForkTabsPosition.Bottom, ForkTabsSize.Default);
        Assert.False(window.ForkTabsPillShown);                               // not hovered
        Assert.False(window.ForkTabsPillContains(new InputMonitorService.POINT { x = 0, y = 0 }));
    });

    [Fact]
    public void TheAppsBarMakesRoomForTabsOnTheRight() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        var settings = new ForkSettingsService(_folder);
        window.ForkAttachAppDock();
        window.ForkAttachTabsPill(settings);
        window.NavIconsPanel.Visibility = Visibility.Visible;               // as upstream shows it in the open notch
        double before = window.ForkDockHost!.Width;
        settings.PreviewTabs(ForkTabsPosition.Right, ForkTabsSize.Default);
        Assert.True(window.ForkTabsRightReserve() > 20);
        Assert.Equal(before + window.ForkTabsRightReserve(), window.ForkDockHost.Width, 1);
        Assert.Equal(-window.ForkDockHost.Width, window.ForkDockHost.Margin.Right, 1);    // still zero layout width
        settings.PreviewTabs(ForkTabsPosition.Bottom, ForkTabsSize.Default);
        Assert.Equal(before, window.ForkDockHost.Width, 1);
    });

    [Fact]
    public void SettingsRowsSitAfterNavigationTabsAndPreview() => SharedStaTestRunner.RunAsync(async ct =>
    {
        BackgroundTestWindows.EnsureApplicationResources();
        var settings = new ForkSettingsService(_folder);
        var window = new SettingsWindow(new NotchSettings(), new NullSettingsService()) { ShowActivated = false };
        try
        {
            window.ForkAttachTabsOptions(settings);
            var anchor = (Border)((FrameworkElement)window.NavTabsLabel.Parent).Parent;
            var panel = (Panel)anchor.Parent;
            int at = panel.Children.IndexOf(anchor);
            Assert.Contains(window.ForkTabsPositionCombo!, ((StackPanel)((Border)panel.Children[at + 1]).Child).Children.OfType<ComboBox>());
            Assert.Contains(window.ForkTabsSizeCombo!, ((StackPanel)((Border)panel.Children[at + 2]).Child).Children.OfType<ComboBox>());
            Assert.Equal(4, window.ForkTabsPositionCombo!.Items.Count);
            Assert.False(window.ForkTabsSizeCombo!.IsEnabled);                // size only matters for the pill

            window.ForkTabsPositionCombo.SelectedIndex = (int)ForkTabsPosition.Bottom;
            Assert.Equal(ForkTabsPosition.Bottom, settings.TabsPosition);    // live preview
            Assert.True(window.ForkTabsSizeCombo.IsEnabled);
            Assert.Equal("Bottom", ((ComboBoxItem)window.ForkTabsPositionCombo.SelectedItem).Content);
        }
        finally
        {
            window.Close();
        }
        await Task.CompletedTask;
    });

    private sealed class NullSettingsService : ISettingsApplicationService
    {
        public NotchSettings Load() => new();
        public Task ApplyAsync(NotchSettings settings, System.Threading.CancellationToken ct = default)
        {
            SettingsApplied?.Invoke(this, settings);
            return Task.CompletedTask;
        }
        public Task<(NotchSettings Settings, bool RequiresRestart)> ImportAsync(string filePath, NotchSettings? currentSettings = null,
            System.Threading.CancellationToken ct = default) => Task.FromResult((new NotchSettings(), false));
        public void Export(string filePath, NotchSettings settings) { }
        public bool IsAutoStartEnabled() => false;
        public event EventHandler<NotchSettings>? SettingsApplied;
    }

    private static GreetingAcceptanceTests.MainWindowFixture CreateFixture() =>
        new("en", greeting: false,
            configureServices: services => services.AddSingleton<IMediaDetectionService>(new FakeMediaDetectionService()));
}
