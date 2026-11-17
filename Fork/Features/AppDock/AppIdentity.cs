using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VNotch.Services;

namespace VNotch.Fork.Features.AppDock;

// faridfox fork: who an open window belongs to, as the taskbar sees it: the app's name and its real icon.
// - Store/packaged apps (Microsoft Store, WhatsApp, Settings...): their AppUserModelID, from the window's own property
//   (Store apps hosted by ApplicationFrameHost carry it there) or from the packaged process; name and logo then come
//   from the package's manifest through upstream's own logo loader (the exe has no usable icon).
// - Classic apps: the program file; name from its version info, icon from the Windows shell at 96 px (crisp at 40 px
//   and high DPI; the system "large icon" is only 32 px).
// Everything here runs off the UI thread; results are frozen and cached.
internal static class AppIdentity
{
    private const int IconPixels = 96;
    private static readonly ConcurrentDictionary<string, (bool Packaged, string Name)> Packages = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> Icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The window's AppUserModelID if it has one ("" for most classic desktop apps).</summary>
    public static string AppUserModelId(IntPtr window, uint processId)
    {
        string id = FromWindowProperty(window);
        return id.Length > 0 ? id : FromPackagedProcess(processId);
    }

    /// <summary>True when the id names an installed package app; its display name is returned too.</summary>
    public static bool IsPackagedApp(string appUserModelId, out string name)
    {
        name = "";
        if (string.IsNullOrWhiteSpace(appUserModelId) || !appUserModelId.Contains('!')) return false;
        var entry = Packages.GetOrAdd(appUserModelId, id =>
        {
            try
            {
                var info = global::Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(id);
                return (true, info?.DisplayInfo?.DisplayName ?? "");
            }
            catch (Exception) { return (false, ""); }   // not a package app (or not installed for this user)
        });
        name = entry.Name;
        return entry.Packaged;
    }

    private static readonly ConcurrentDictionary<string, string> ProgramNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A program's friendly name ("Google Chrome"), else its file name.</summary>
    public static string ProgramName(string executable) => ProgramNames.GetOrAdd(executable, path =>
    {
        try
        {
            string description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim() ?? "";
            if (description.Length > 0) return description;
        }
        catch (Exception) { /* no version info */ }
        return Path.GetFileNameWithoutExtension(path);
    });

    public static Task<ImageSource?> IconAsync(DockApp app) =>
        app.AppId.Length > 0
            ? Icons.GetOrAdd("aumid:" + app.AppId, _ => PackageLogoAsync(app.AppId))
            : Icons.GetOrAdd("exe:" + app.Executable, _ => Task.Run(() => ShellIcon(app.Executable) ?? Frozen(FileIconProvider.GetAppIcon(app.Executable, small: false))));

    private static async Task<ImageSource?> PackageLogoAsync(string appUserModelId)
    {
        try
        {
            var info = global::Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(appUserModelId);
            return await AppNotificationService.TryLoadAppLogoAsync(info.DisplayInfo).ConfigureAwait(false);
        }
        catch (Exception) { return null; }
    }

    // ---- the window's AppUserModelID property (shell32) ----

    private static string FromWindowProperty(IntPtr window)
    {
        var iid = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreForWindow(window, ref iid, out var store) != 0 || store == null) return "";
        try
        {
            var key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };
            if (store.GetValue(ref key, out var value) != 0) return "";
            try { return value.Type == 31 /* VT_LPWSTR */ && value.Pointer != 0 ? Marshal.PtrToStringUni(value.Pointer) ?? "" : ""; }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    // ---- a packaged process's AppUserModelID (kernel32) ----

    private static string FromPackagedProcess(uint processId)
    {
        IntPtr process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, processId);
        if (process == IntPtr.Zero) return "";
        try
        {
            uint length = 256;
            var buffer = new char[length];
            if (GetApplicationUserModelId(process, ref length, buffer) != 0 || length == 0) return "";
            return new string(buffer, 0, (int)length - 1);
        }
        finally { CloseHandle(process); }
    }

    // ---- a crisp program icon from the shell (IShellItemImageFactory through upstream's native helper) ----

    private static ImageSource? ShellIcon(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) return null;
        IntPtr bitmap = Win32Interop.GetShellThumbnailBitmap(executable, IconPixels, 0x4 /* SIIGBF_ICONONLY */);
        if (bitmap == IntPtr.Zero) return null;
        try { return FromDibSection(bitmap); }
        finally { DeleteObject(bitmap); }
    }

    /// <summary>
    /// Copies a 32-bit premultiplied DIB section into a frozen WPF bitmap, keeping its alpha (the usual
    /// CreateBitmapSourceFromHBitmap drops it, which paints icons on black).
    /// </summary>
    internal static BitmapSource? FromDibSection(IntPtr bitmap)
    {
        if (GetObject(bitmap, Marshal.SizeOf<DibSection>(), out var dib) == 0) return null;
        var bm = dib.Bitmap;
        if (bm.BitsPixel != 32 || bm.Bits == IntPtr.Zero || bm.Width <= 0 || bm.Height == 0) return null;
        int width = bm.Width, height = Math.Abs(bm.Height), stride = bm.WidthBytes;
        var pixels = new byte[stride * height];
        Marshal.Copy(bm.Bits, pixels, 0, pixels.Length);
        if (dib.Header.Height > 0)   // bottom-up rows: flip
        {
            var row = new byte[stride];
            for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
            {
                Buffer.BlockCopy(pixels, top * stride, row, 0, stride);
                Buffer.BlockCopy(pixels, bottom * stride, pixels, top * stride, stride);
                Buffer.BlockCopy(row, 0, pixels, bottom * stride, stride);
            }
        }
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        source.Freeze();
        return source;
    }

    private static ImageSource? Frozen(ImageSource? icon)
    {
        if (icon is Freezable { IsFrozen: false, CanFreeze: true } freezable) freezable.Freeze();
        return icon is Freezable { IsFrozen: false } ? null : icon;
    }

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, out IPropertyStore store);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(IntPtr process, ref uint length, [Out] char[] id);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int size, out DibSection dib);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey { public Guid Format; public uint Id; }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public nint Pointer;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DibSection
    {
        public BitmapInfo Bitmap;
        public BitmapInfoHeader Header;
        public uint Bitfield0, Bitfield1, Bitfield2;
        public IntPtr Section;
        public uint Offset;
    }
}
