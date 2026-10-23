using System.Runtime.InteropServices;
using CircleQuickLauncher.Interop;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace CircleQuickLauncher.Services;

/// <summary>
/// Applies the launcher's window chrome: borderless, tool-window (hidden from
/// taskbar and Alt+Tab), topmost, click-through on empty areas, and placement math.
/// </summary>
public static class WindowService
{
    private const int HWND_TOPMOST = -1;

    public static IntPtr GetHwnd(Window window) =>
        WinRT.Interop.WindowNative.GetWindowHandle(window);

    public static AppWindow GetAppWindow(Window window)
    {
        IntPtr hwnd = GetHwnd(window);
        Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    /// <summary>Makes the window borderless, floating, and out of the taskbar/Alt+Tab lists.</summary>
    public static void ConfigureLauncherWindow(Window window)
    {
        AppWindow appWindow = GetAppWindow(window);
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        IntPtr hwnd = GetHwnd(window);
        long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        style |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_NOREDIRECTIONBITMAP;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));

        // Use the WinUI acrylic backdrop so the desktop shows through (frosted/translucent),
        // which is the reliable way to get a non-opaque overlay in WinUI 3.
        try
        {
            window.SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
        }
        catch (Exception)
        {
            // Backdrop is optional; the DWM frame extension below is the fallback path.
        }

        ApplyTransparentFrame(hwnd);
    }

    /// <summary>
    /// Turns the window frame into a fully transparent DWM frame so only XAML content draws.
    /// Combined with the acrylic SystemBackdrop this yields a translucent overlay.
    /// Re-applied after activation because WinUI may reset styles during window setup.
    /// </summary>
    public static void ApplyTransparentFrame(IntPtr hwnd)
    {
        long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        style |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_NOREDIRECTIONBITMAP;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));

        var margins = new NativeMethods.MARGINS
        {
            cxLeftWidth = -1,
            cxRightWidth = -1,
            cyTopHeight = -1,
            cyBottomHeight = -1,
        };
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    /// <summary>
    /// Applies the DWM accent policy that renders the window background fully transparent
    /// while letting XAML content (the circles) draw on top. This is the modern Windows 10/11
    /// mechanism used by the Start menu and other overlay surfaces.
    /// </summary>
    public static void ApplyTransparentAccent(IntPtr hwnd)
    {
        try
        {
            var accent = new NativeMethods.ACCENT_POLICY
            {
                AccentState = (int)NativeMethods.ACCENT_STATE.ACCENT_ENABLE_TRANSPARENTGRADIENT,
                AccentFlags = 2,
                GradientColor = 0x00000000, // fully transparent (AABBGGRR)
            };

            int size = Marshal.SizeOf<NativeMethods.ACCENT_POLICY>();
            var data = new NativeMethods.WINDOWCOMPOSITIONATTRIBDATA
            {
                Attrib = NativeMethods.WCA_ACCENT_POLICY,
                pvData = Marshal.AllocHGlobal(size),
                cbData = size,
            };

            try
            {
                Marshal.StructureToPtr(accent, data.pvData, false);
                NativeMethods.SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(data.pvData);
            }
        }
        catch (Exception)
        {
            // Accent policy is best-effort; the DWM frame extension is the primary path.
        }
    }

    /// <summary>Centers a window on the primary monitor's work area and resizes it.</summary>
    public static void CenterOnScreen(Window window, int widthPx, int heightPx)
    {
        AppWindow appWindow = GetAppWindow(window);
        appWindow.Resize(new SizeInt32(widthPx, heightPx));

        DisplayArea displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        RectInt32 work = displayArea.WorkArea;
        int x = work.X + ((work.Width - widthPx) / 2);
        int y = work.Y + ((work.Height - heightPx) / 2);
        appWindow.Move(new PointInt32(x, y));
    }

    /// <summary>Gets the primary monitor work area in physical pixels.</summary>
    public static RectInt32 GetPrimaryWorkAreaPx()
    {
        // Resolve the primary monitor via a point at the virtual-screen origin.
        IntPtr monitor = NativeMethods.MonitorFromPoint(new NativeMethods.POINT { X = 0, Y = 0 }, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfo(monitor, ref info);
        return new RectInt32(
            info.rcWork.Left,
            info.rcWork.Top,
            info.rcWork.Right - info.rcWork.Left,
            info.rcWork.Bottom - info.rcWork.Top);
    }

    public static void ReassertTopmost(IntPtr hwnd)
    {
        NativeMethods.SetWindowPos(
            hwnd,
            new IntPtr(HWND_TOPMOST),
            0, 0, 0, 0,
            (uint)(NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE));
    }

    public static void Move(IntPtr hwnd, int xPx, int yPx) =>
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, xPx, yPx, 0, 0, (uint)NativeMethods.SWP_NOSIZE);

    public static void Resize(IntPtr hwnd, int widthPx, int heightPx) =>
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, widthPx, heightPx, (uint)NativeMethods.SWP_NOMOVE);

    public static void MoveAndResize(IntPtr hwnd, int xPx, int yPx, int widthPx, int heightPx) =>
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, xPx, yPx, widthPx, heightPx, 0);

    /// <summary>Work area (DIPs) of the primary monitor.</summary>
    public static Core.Layout.WorkArea GetPrimaryWorkAreaDip()
    {
        IntPtr monitor = NativeMethods.MonitorFromPoint(new NativeMethods.POINT { X = 0, Y = 0 }, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        return GetWorkAreaDip(monitor);
    }

    public static Core.Layout.WorkArea GetWorkAreaDip(IntPtr monitor)
    {
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfo(monitor, ref info);

        double scale = GetDpiForMonitor(monitor) / 96.0;
        if (scale <= 0)
        {
            scale = 1.0;
        }

        return new Core.Layout.WorkArea(
            info.rcWork.Left / scale,
            info.rcWork.Top / scale,
            (info.rcWork.Right - info.rcWork.Left) / scale,
            (info.rcWork.Bottom - info.rcWork.Top) / scale);
    }

    public static double GetScaleForPoint(int xPx, int yPx)
    {
        IntPtr monitor = NativeMethods.MonitorFromPoint(new NativeMethods.POINT { X = xPx, Y = yPx }, NativeMethods.MONITOR_DEFAULTTONEAREST);
        double dpi = GetDpiForMonitor(monitor);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    public static double GetDpiForMonitor(IntPtr monitor)
    {
        try
        {
            int hr = GetDpiForMonitorRaw(monitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dpiX, out _);
            return hr == 0 && dpiX > 0 ? dpiX : 96.0;
        }
        catch (DllNotFoundException)
        {
            return 96.0;
        }
        catch (EntryPointNotFoundException)
        {
            return 96.0;
        }
    }

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitorRaw(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    public static IntPtr GetMonitorAt(int xPx, int yPx) =>
        NativeMethods.MonitorFromPoint(new NativeMethods.POINT { X = xPx, Y = yPx }, NativeMethods.MONITOR_DEFAULTTONEAREST);
}
