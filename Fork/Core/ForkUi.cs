using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VNotch.Services;

namespace VNotch.Fork;

// faridfox fork: attaches fork UI to upstream windows when they load (the mechanism upstream itself uses in
// Services/LocalizedPresentation.cs), so upstream XAML stays untouched. Runs only inside the real app: upstream's
// tests build windows without an App, and then nothing here is registered.
internal static class ForkUi
{
    private static bool _initialized;

    internal static void InitializeOnce()
    {
        if (_initialized || Application.Current is not App) return;
        _initialized = true;
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));
        EventManager.RegisterClassHandler(typeof(SettingsWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnSettingsWindowLoaded));
    }

    private static ForkSettingsService Settings => App.Services.GetRequiredService<ForkSettingsService>();

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        Guard(() => window.ForkAttachCompactText(Settings));
        Guard(window.ForkAttachLineBar);
        Guard(window.ForkAttachNowPlaying);
        Guard(window.ForkAttachMediaSwitcher);
        Guard(window.ForkAttachAppDock);
    }

    private static void OnSettingsWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is SettingsWindow window && ReferenceEquals(e.OriginalSource, window))
            Guard(() => window.ForkAttachCompactLyricsRow(Settings));
    }

    // A fork bug must never take the notch down: log it and leave upstream's UI as it is.
    private static void Guard(Action attach)
    {
        try { attach(); }
        catch (Exception ex) { RuntimeLog.Error("FORK-UI", ex, "Fork UI was not attached."); }
    }
}
