using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using VNotch.Services;

namespace VNotch.Fork;

// faridfox fork: app icons for fork UI (media source switcher, open-apps dock), resolved with upstream's own helpers on
// the thread pool (upstream does the same for notifications), frozen so any thread can draw them, cached per app.
internal static class ForkAppIcons
{
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Icon of the app behind a media session (its AppUserModelId, e.g. "Spotify.exe" or "Chrome").</summary>
    public static Task<ImageSource?> ForAppIdAsync(string appId, string? appName = null) =>
        string.IsNullOrWhiteSpace(appId)
            ? Task.FromResult<ImageSource?>(null)
            : Cache.GetOrAdd("aumid:" + appId, _ => Task.Run(() => Frozen(AppNotificationService.TryResolveAppIcon(appId, appName))));

    /// <summary>Icon of an executable (large, as the taskbar shows it).</summary>
    public static Task<ImageSource?> ForExecutableAsync(string exePath) =>
        string.IsNullOrWhiteSpace(exePath)
            ? Task.FromResult<ImageSource?>(null)
            : Cache.GetOrAdd("exe:" + exePath, _ => Task.Run(() => Frozen(FileIconProvider.GetAppIcon(exePath, small: false))));

    private static ImageSource? Frozen(ImageSource? icon)
    {
        try
        {
            if (icon is Freezable { IsFrozen: false } freezable)
            {
                if (!freezable.CanFreeze) return null;   // could not be shared with the UI thread
                freezable.Freeze();
            }
            return icon;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}
