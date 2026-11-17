using System;
using System.Collections.Generic;
using System.Text;
using VNotch.Controllers;
using VNotch.Models;
using VNotch.Services;

namespace VNotch.Fork.Features.AppDock;

/// <summary>
/// One app in the dock: the window to bring to the front, its program, the window title, the app's AppUserModelID when
/// it is a Store/packaged app, and the app's name (shown in the tooltip).
/// </summary>
internal sealed record DockApp(IntPtr Window, string Executable, string Title, string AppId = "", string Name = "")
{
    /// <summary>What makes two windows the same app: the package app id, else the program file.</summary>
    public string Key => AppId.Length > 0 ? "app:" + AppId : Executable;
    public string Label => Name.Length > 0 ? Name : Title;
}

/// <summary>What the dock needs to know about a top-level window to decide whether Alt+Tab would show it.</summary>
internal readonly record struct WindowFacts(
    IntPtr Handle, bool Visible, bool Cloaked, int ExStyle, IntPtr Owner, int TitleLength, string ClassName, uint ProcessId);

// faridfox fork: the open apps for the dock (Fork/Features/AppDock). Uses upstream's Win32 helpers only; the filter and
// grouping are pure and unit-tested.
internal static class AppDockWindows
{
    /// <summary>At most this many apps (the dock scrolls).</summary>
    internal const int MaxApps = 16;
    private const uint GwOwner = 4;
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
    };

    /// <summary>Alt+Tab's rule: visible, not cloaked (other desktops, suspended UWP), titled, not a tool or owned window.</summary>
    public static bool IsSwitchable(WindowFacts w, uint ownProcessId)
    {
        if (!w.Visible || w.Cloaked || w.TitleLength <= 0 || w.ProcessId == ownProcessId) return false;
        if (ShellClasses.Contains(w.ClassName)) return false;
        bool appWindow = (w.ExStyle & Win32Interop.WS_EX_APPWINDOW) != 0;
        if ((w.ExStyle & Win32Interop.WS_EX_TOOLWINDOW) != 0 && !appWindow) return false;
        return w.Owner == IntPtr.Zero || appWindow;
    }

    /// <summary>
    /// One entry per app, most recently used first (the input is in z-order). Store apps are told apart by their app id
    /// (they all run in ApplicationFrameHost); a frame window without one keeps its own entry.
    /// </summary>
    public static List<DockApp> Group(IEnumerable<DockApp> windows)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var apps = new List<DockApp>();
        foreach (var app in windows)
        {
            if (string.IsNullOrWhiteSpace(app.Executable)) continue;
            bool unidentifiedFrame = app.AppId.Length == 0 &&
                app.Executable.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
            if (!unidentifiedFrame && !seen.Add(app.Key)) continue;
            apps.Add(app);
            if (apps.Count == MaxApps) break;
        }
        return apps;
    }

    /// <summary>The open apps right now (call off the UI thread: it reads every top-level window).</summary>
    public static List<DockApp> List()
    {
        uint own = (uint)Environment.ProcessId;
        var windows = new List<DockApp>();
        var title = new StringBuilder(256);
        var className = new StringBuilder(128);
        Win32Interop.EnumWindows((hwnd, _) =>
        {
            try
            {
                Win32Interop.GetWindowThreadProcessId(hwnd, out uint pid);
                className.Clear();
                Win32Interop.GetClassName(hwnd, className, className.Capacity);
                var facts = new WindowFacts(hwnd, Win32Interop.IsWindowVisible(hwnd), FullscreenDetector.IsWindowCloaked(hwnd),
                    Win32Interop.GetWindowLong(hwnd, Win32Interop.GWL_EXSTYLE), Win32Interop.GetWindow(hwnd, GwOwner),
                    Win32Interop.GetWindowTextLength(hwnd), className.ToString(), pid);
                if (!IsSwitchable(facts, own)) return true;
                title.Clear();
                Win32Interop.GetWindowText(hwnd, title, title.Capacity);
                var (_, executable) = ProcessNameResolver.TryGetNameAndExecutable(pid);
                executable ??= "";
                string appId = AppIdentity.AppUserModelId(hwnd, pid);
                string name;
                if (!AppIdentity.IsPackagedApp(appId, out name))
                {
                    appId = "";
                    name = executable.Length > 0 ? AppIdentity.ProgramName(executable) : "";
                }
                windows.Add(new DockApp(hwnd, executable, title.ToString(), appId, name));
            }
            catch (Exception ex)
            {
                RuntimeLog.Warn("FORK-APP-DOCK", $"Window skipped: {ex.Message}");
            }
            return true;
        }, IntPtr.Zero);
        return Group(windows);
    }

    /// <summary>The dock shows while the notch is open on its Media page and nothing is moving.</summary>
    public static bool ShouldShow(NotchTransitionSnapshot state) =>
        state.CurrentView == NotchView.Media && state.TargetView == NotchView.Media && !state.IsTransitionActive &&
        state.IsEffectivelyVisible && state.Ownership == DisplayOwnership.Notch;
}
