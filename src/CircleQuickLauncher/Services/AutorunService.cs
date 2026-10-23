using CircleQuickLauncher.Interop;
using Microsoft.Win32;

namespace CircleQuickLauncher.Services;

/// <summary>
/// "Start with Windows" registration.
/// Unpackaged (portable/Inno): HKCU\...\Run with a quoted path.
/// Packaged (MSIX): declared windows.startupTask, toggled via the StartupTask API.
/// </summary>
public static class AutorunService
{
    public const string RunValueName = "CircleQuickLauncher";
    public const string StartupTaskId = "CircleQuickLauncherStartup";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsPackaged
    {
        get
        {
            try
            {
                return Windows.ApplicationModel.Package.Current is not null;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public static bool IsEnabled()
    {
        if (IsPackaged)
        {
            try
            {
                var task = Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
                return task.State is Windows.ApplicationModel.StartupTaskState.Enabled
                    or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
            }
            catch (Exception)
            {
                return false;
            }
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(RunValueName) is not null;
    }

    public static void SetEnabled(bool enabled)
    {
        if (IsPackaged)
        {
            try
            {
                var task = Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
                if (enabled)
                {
                    var state = task.RequestEnableAsync().AsTask().GetAwaiter().GetResult();
                    if (state == Windows.ApplicationModel.StartupTaskState.DisabledByPolicy)
                    {
                        throw new InvalidOperationException("Startup is disabled by policy on this device.");
                    }
                }
                else
                {
                    task.Disable();
                }

                return;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Could not change the startup registration: " + ex.Message, ex);
            }
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            throw new InvalidOperationException("Could not open the startup registry key.");
        }

        if (enabled)
        {
            string exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot determine the executable path.");
            key.SetValue(RunValueName, "\"" + exePath + "\"");
        }
        else
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Removes our startup registration if it points at a non-existent binary.</summary>
    public static void RepairIfBroken()
    {
        if (IsPackaged)
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (key?.GetValue(RunValueName) is string value)
            {
                string path = value.Trim().Trim('"');
                if (!File.Exists(path))
                {
                    SetEnabled(false);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
        }
    }
}
