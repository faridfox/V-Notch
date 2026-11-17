using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using VNotch.Controllers;
using VNotch.Fork.Features.AppDock;
using VNotch.Models;
using VNotch.Services;
using VNotch.Tests.Fakes;
using Xunit;

namespace VNotch.Tests.Fork;

// faridfox fork: the open-apps dock beside the expanded notch (Fork/Features/AppDock).
[Collection("Localization")]
public sealed class AppDockTests
{
    private const uint Own = 42;

    private const int Tool = Win32Interop.WS_EX_TOOLWINDOW;
    private const int App = Win32Interop.WS_EX_APPWINDOW;

    [Theory]
    [InlineData(true, false, 0, 0, 5, "Chrome_WidgetWin_1", 7u, true)]
    [InlineData(false, false, 0, 0, 5, "Chrome_WidgetWin_1", 7u, false)]
    [InlineData(true, true, 0, 0, 5, "Chrome_WidgetWin_1", 7u, false)]          // another desktop, suspended app
    [InlineData(true, false, 0, 0, 0, "Chrome_WidgetWin_1", 7u, false)]         // no title
    [InlineData(true, false, 0, 0, 5, "Chrome_WidgetWin_1", Own, false)]        // V-Notch itself
    [InlineData(true, false, 0, 0, 5, "Progman", 7u, false)]
    [InlineData(true, false, 0, 0, 5, "Shell_TrayWnd", 7u, false)]
    [InlineData(true, false, Tool, 0, 5, "Chrome_WidgetWin_1", 7u, false)]
    [InlineData(true, false, Tool | App, 0, 5, "Chrome_WidgetWin_1", 7u, true)]
    [InlineData(true, false, 0, 55, 5, "Chrome_WidgetWin_1", 7u, false)]        // dialogs belong to their owner
    [InlineData(true, false, App, 55, 5, "Chrome_WidgetWin_1", 7u, true)]
    public void OnlyWindowsAltTabWouldShowAreListed(bool visible, bool cloaked, int exStyle, long owner, int title,
        string cls, uint pid, bool expected) =>
        Assert.Equal(expected, AppDockWindows.IsSwitchable(
            new WindowFacts(new IntPtr(100), visible, cloaked, exStyle, new IntPtr(owner), title, cls, pid), Own));

    [Fact]
    public void OneEntryPerProgramMostRecentFirst()
    {
        var apps = AppDockWindows.Group(
        [
            new DockApp(new IntPtr(1), @"C:\Chrome\chrome.exe", "Tab A"),
            new DockApp(new IntPtr(2), @"C:\Code\Code.exe", "Repo"),
            new DockApp(new IntPtr(3), @"C:\CHROME\CHROME.EXE", "Tab B"),
            new DockApp(new IntPtr(4), @"C:\Windows\System32\ApplicationFrameHost.exe", "Settings"),
            new DockApp(new IntPtr(5), @"C:\Windows\System32\ApplicationFrameHost.exe", "Calculator"),
            new DockApp(new IntPtr(6), "", "No program"),
        ]);
        Assert.Equal([1, 2, 4, 5], apps.Select(a => (int)a.Window));
    }

    [Fact]
    public void TheListIsCapped() =>
        Assert.Equal(AppDockWindows.MaxApps,
            AppDockWindows.Group(Enumerable.Range(1, 40).Select(i => new DockApp(new IntPtr(i), $"app{i}.exe", "x"))).Count);

    [Theory]
    [InlineData(NotchView.Media, NotchView.Media, false, true, true)]
    [InlineData(NotchView.Compact, NotchView.Compact, false, true, false)]
    [InlineData(NotchView.Compact, NotchView.Media, true, true, false)]   // still opening
    [InlineData(NotchView.Media, NotchView.Secondary, true, true, false)] // leaving for another page
    [InlineData(NotchView.Secondary, NotchView.Secondary, false, true, false)]
    [InlineData(NotchView.Media, NotchView.Media, false, false, false)]   // notch hidden (fullscreen, line bar)
    public void TheDockShowsOnlyOnTheOpenMediaPage(NotchView current, NotchView target, bool moving, bool visible, bool expected) =>
        Assert.Equal(expected, AppDockWindows.ShouldShow(State(current, target, moving, visible)));

    [Fact]
    public void TheDockLivesInTheNotchWithoutChangingItsLayout() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        window.NotchWrapper.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double before = window.NotchWrapper.DesiredSize.Width;

        window.ForkAttachAppDock();
        Assert.Same(window.NotchWrapper, window.ForkDockHost!.Parent);   // so upstream lets it receive clicks
        window.ForkDockHost.Visibility = Visibility.Visible;
        window.NotchWrapper.InvalidateMeasure();
        window.NotchWrapper.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(before, window.NotchWrapper.DesiredSize.Width, 2);
    });

    [Fact]
    public void ShownOnTheMediaPageListsAppsAndAClickSwitches() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        var activated = new List<IntPtr>();
        window.ForkDockSource = () =>
        [
            new DockApp(new IntPtr(11), @"C:\Chrome\chrome.exe", "Tab"),
            new DockApp(new IntPtr(22), @"C:\Code\Code.exe", "Repo"),
        ];
        window.ForkDockActivate = hwnd => { activated.Add(hwnd); return true; };
        window.ForkAttachAppDock();

        window.ForkUpdateDock(State(NotchView.Media, NotchView.Media, false, true));
        Assert.True(window.ForkDockShown);
        await window.ForkRefreshDockAsync();
        Assert.Equal(2, window.ForkDockApps.Count);

        var cells = ((StackPanel)((ScrollViewer)((Border)window.ForkDockHost!.Children[0]).Child).Content).Children;
        var press = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
        ((UIElement)cells[1]).RaiseEvent(press);
        Assert.True(press.Handled);                   // never reaches the notch
        Assert.Equal([new IntPtr(22)], activated);

        window.ForkUpdateDock(State(NotchView.Media, NotchView.Secondary, true, true));
        Assert.False(window.ForkDockShown);
    });

    [Fact]
    public void TheWheelScrollsTheDockAndNeverFlipsNotchPages() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = CreateFixture();
        var window = fixture.Window;
        window.ForkAttachAppDock();
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
        window.ForkDockHost!.RaiseEvent(wheel);
        Assert.True(wheel.Handled);
    });

    private static NotchTransitionSnapshot State(NotchView current, NotchView target, bool moving, bool visible) =>
        new(current, target, NotchShapeState.Expanded, 1, moving, DisplayOwnership.Notch, NotchView.Media, false, visible);

    private static GreetingAcceptanceTests.MainWindowFixture CreateFixture() =>
        new("en", greeting: false,
            configureServices: services => services.AddSingleton<IMediaDetectionService>(new FakeMediaDetectionService()));
}
