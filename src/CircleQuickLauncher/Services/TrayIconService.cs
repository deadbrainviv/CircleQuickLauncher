using CircleQuickLauncher.Interop;

namespace CircleQuickLauncher.Services;

/// <summary>
/// Notification-area (tray) icon with a Win32 context menu.
/// The menu is rebuilt on every open so the autorun checkmark reflects live state.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    public const uint CallbackMessage = NativeMethods.WM_APP + 0x41;

    public const int CmdShowSettings = 0x1001;
    public const int CmdAddApp = 0x1002;
    public const int CmdToggleAutorun = 0x1003;
    public const int CmdExit = 0x1004;
    public const int CmdOpenDataFolder = 0x1005;

    private IntPtr _hwnd;
    private IntPtr _hIcon = IntPtr.Zero;
    private bool _added;
    private bool _disposed;

    public event EventHandler? ShowSettingsRequested;

    public event EventHandler? AddAppRequested;

    public event EventHandler? ToggleAutorunRequested;

    public event EventHandler? OpenDataFolderRequested;

    public event EventHandler? ExitRequested;

    public void Attach(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _hIcon = LoadAppIcon();

        var data = BuildNotifyData();
        _added = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
        if (_added)
        {
            // Show the tooltip even if the shell hides icon text by default policy.
            data.uFlags = NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP;
            NativeMethods.Shell_NotifyIcon(0x00000004 /* NIM_MODIFY */, ref data);
        }
    }

    /// <summary>Called from the launcher window's WndProc. Returns true when the message was consumed.</summary>
    public bool HandleMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == CallbackMessage)
        {
            int eventCode = lParam.ToInt32() & 0xFFFF;
            switch (eventCode)
            {
                case NativeMethods.WM_LBUTTONUP:
                case 0x0206 /* NIN_SELECT */:
                case 0x0207 /* NIN_KEYSELECT */:
                    ShowSettingsRequested?.Invoke(this, EventArgs.Empty);
                    return true;

                case NativeMethods.WM_RBUTTONUP:
                    ShowContextMenu();
                    return true;
            }

            return false;
        }

        if (msg == NativeMethods.WM_COMMAND)
        {
            int id = wParam.ToInt32() & 0xFFFF;
            switch (id)
            {
                case CmdShowSettings:
                    ShowSettingsRequested?.Invoke(this, EventArgs.Empty);
                    return true;
                case CmdAddApp:
                    AddAppRequested?.Invoke(this, EventArgs.Empty);
                    return true;
                case CmdToggleAutorun:
                    ToggleAutorunRequested?.Invoke(this, EventArgs.Empty);
                    return true;
                case CmdOpenDataFolder:
                    OpenDataFolderRequested?.Invoke(this, EventArgs.Empty);
                    return true;
                case CmdExit:
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    return true;
            }
        }

        return false;
    }

    private void ShowContextMenu()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        IntPtr menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            bool autorun;
            try
            {
                autorun = AutorunService.IsEnabled();
            }
            catch (Exception)
            {
                autorun = false;
            }

            NativeMethods.AppendMenu(menu, 0x00000000 /* MF_STRING */, new IntPtr(CmdShowSettings), "Settings");
            NativeMethods.AppendMenu(menu, 0x00000000 /* MF_STRING */, new IntPtr(CmdAddApp), "Add app...");
            NativeMethods.AppendMenu(menu, 0x00000000 /* MF_STRING */, new IntPtr(CmdOpenDataFolder), "Open data folder");
            NativeMethods.AppendMenu(menu, 0x00000800 /* MF_SEPARATOR */, IntPtr.Zero, string.Empty);
            NativeMethods.AppendMenu(
                menu,
                0x00000000 | (autorun ? 0x00000008u : 0u) /* MF_STRING | MF_CHECKED */,
                new IntPtr(CmdToggleAutorun),
                "Start with Windows");
            NativeMethods.AppendMenu(menu, 0x00000800 /* MF_SEPARATOR */, IntPtr.Zero, string.Empty);
            NativeMethods.AppendMenu(menu, 0x00000000 /* MF_STRING */, new IntPtr(CmdExit), "Exit");

            if (!NativeMethods.GetCursorPos(out NativeMethods.POINT pt))
            {
                return;
            }

            // Required so the menu dismisses correctly when the owner window is not foreground.
            NativeMethods.SetForegroundWindow(_hwnd);
            NativeMethods.SendMessageTimeout(
                _hwnd,
                NativeMethods.WM_NULL,
                IntPtr.Zero,
                IntPtr.Zero,
                0x00000002 /* SMTO_ABORTIFHUNG */,
                100,
                out _);

            // TrackPopupMenu posts WM_COMMAND to _hwnd when a menu item is chosen.
            NativeMethods.TrackPopupMenu(
                menu,
                0x00000001 /* TPM_RIGHTBUTTON */,
                pt.X,
                pt.Y,
                0,
                _hwnd,
                IntPtr.Zero);

            NativeMethods.PostMessage(_hwnd, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private NativeMethods.NOTIFYICONDATA BuildNotifyData()
    {
        return new NativeMethods.NOTIFYICONDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = CallbackMessage,
            hIcon = _hIcon,
            szTip = "CircleQuickLauncher",
        };
    }

    private static IntPtr LoadAppIcon()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var shfi = new NativeMethods.SHFILEINFO();
                IntPtr result = NativeMethods.SHGetFileInfo(
                    exe,
                    0,
                    ref shfi,
                    (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
                    NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);
                if (result != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                {
                    return shfi.hIcon;
                }

                if (shfi.hIcon != IntPtr.Zero)
                {
                    NativeMethods.DestroyIcon(shfi.hIcon);
                }
            }
        }
        catch (Exception)
        {
            // fall through to default
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_added && _hwnd != IntPtr.Zero)
        {
            var data = BuildNotifyData();
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
            _added = false;
        }

        if (_hIcon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }
}
