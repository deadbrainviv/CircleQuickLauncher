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

    /// <summary>GDI stock object index for a null (no-fill) brush.</summary>
    private const int NULL_BRUSH_INDEX = 5;

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
            // hasBorder: true + explicit bit stripping below avoids the WS_DLGFRAME
            // artifact DWM injects when hasBorder is false.
            presenter.SetBorderAndTitleBar(true, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        try
        {
            // Let XAML content fill the whole HWND so no GDI stripe shows above it.
            window.ExtendsContentIntoTitleBar = true;
        }
        catch (Exception)
        {
        }

        // No SystemBackdrop — the surface is made see-through by removing the GDI
        // class brush and extending the DWM frame (see ApplyWindowStyles).
        try
        {
            window.SystemBackdrop = null;
        }
        catch (Exception)
        {
        }

        IntPtr hwnd = GetHwnd(window);
        long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        style |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));

        ApplyWindowStyles(hwnd);
    }

    /// <summary>
    /// Applies every Win32/DWM tweak required for a per-pixel-alpha transparent WinUI 3
    /// window. WinUI 3 offers no first-class API for this; each call here removes a
    /// specific opaque artifact, and most must be re-applied after activation because
    /// WinUI silently resets the styles.
    /// </summary>
    public static void ApplyWindowStyles(IntPtr hwnd)
    {
        // 1. Drop the window class background brush. Left in place, GDI fills the client
        //    area with an opaque rectangle before DirectComposition draws — that is the
        //    black backdrop behind the circles.
        NativeMethods.SetClassLongPtr(
            hwnd,
            NativeMethods.GCLP_HBRBACKGROUND,
            NativeMethods.GetStockObject(NULL_BRUSH_INDEX));

        // Flush pending presenter changes before touching styles.
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_FRAMECHANGED);

        // 2. Strip frame bits so DWM shows no border.
        uint style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE,
            style & ~(NativeMethods.WS_BORDER | NativeMethods.WS_DLGFRAME | NativeMethods.WS_THICKFRAME));

        // 3. WS_EX_NOREDIRECTIONBITMAP makes DWM composite from the DirectComposition
        //    visual tree instead of a GDI redirection bitmap, enabling per-pixel alpha.
        uint exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            (uint)((exStyle | NativeMethods.WS_EX_NOREDIRECTIONBITMAP | (uint)NativeMethods.WS_EX_TOOLWINDOW | (uint)NativeMethods.WS_EX_NOACTIVATE)
                   & ~NativeMethods.WS_EX_WINDOWEDGE));

        // 4. Windows 11: no DWM border highlight, no rounded corners.
        try
        {
            if (Environment.OSVersion.Version is { Major: > 10 } or { Major: 10, Build: >= 22000 })
            {
                int none = NativeMethods.DWMWA_COLOR_NONE;
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref none, sizeof(uint));

                int noRound = NativeMethods.DWMWCP_DONOTROUND;
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref noRound, sizeof(uint));
            }
        }
        catch (Exception)
        {
        }

        // 5. Reset any previously-extended DWM frame to zero. Extending it across the
        //    whole client area (margins -1) makes DWM paint a black glass fill that shows
        //    through the transparent XAML content, which reads as an opaque black square.
        var margins = new NativeMethods.MARGINS
        {
            cxLeftWidth = 0,
            cxRightWidth = 0,
            cyTopHeight = 0,
            cyBottomHeight = 0,
        };
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);

        // 6. Tell DWM to draw no background of its own. A degenerate blur region means
        //    "blur everywhere is empty", so the window body becomes see-through while the
        //    XAML content still composites on top. Without this the frame fills black.
        IntPtr blurRegion = NativeMethods.CreateRectRgn(-2, -2, -1, -1);
        try
        {
            var blur = new NativeMethods.DWM_BLURBEHIND
            {
                dwFlags = NativeMethods.DWM_BB_ENABLE | NativeMethods.DWM_BB_BLURREGION,
                fEnable = true,
                hRgnBlur = blurRegion,
            };
            NativeMethods.DwmEnableBlurBehindWindow(hwnd, ref blur);
        }
        finally
        {
            if (blurRegion != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(blurRegion);
            }
        }
    }

    /// <summary>
    /// The XAML island creates several internal child HWNDs during startup. Each inherits
    /// the class background brush and paints its own opaque fill on top of ours, so they
    /// must be cleared individually. Only available after the first Activated event.
    /// </summary>
    public static void PatchXamlIslandChildWindows(IntPtr hwnd)
    {
        // Keep the delegate alive for the duration of the enumeration.
        NativeMethods.EnumChildProc proc = (childHwnd, _) =>
        {
            NativeMethods.SetClassLongPtr(
                childHwnd,
                NativeMethods.GCLP_HBRBACKGROUND,
                NativeMethods.GetStockObject(NULL_BRUSH_INDEX));
            return true;
        };

        NativeMethods.EnumChildWindows(hwnd, proc, IntPtr.Zero);
        GC.KeepAlive(proc);
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
