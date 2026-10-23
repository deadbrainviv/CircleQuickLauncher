using System.Runtime.InteropServices;
using CircleQuickLauncher.Core.Models;

namespace CircleQuickLauncher.Services;

[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPath, uint dwReserved);
    void Resolve(IntPtr hwnd, uint fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport]
[Guid("0000010b-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistFile
{
    void GetClassID(out Guid pClassID);

    [PreserveSig]
    int IsDirty();

    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);

    void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);

    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

    void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
}

/// <summary>
/// Reads applications from the per-user and all-users Start Menus.
/// Shortcuts are resolved with IShellLink and heavily filtered (uninstallers, caps,
/// inaccessible locations) before they reach the UI.
/// </summary>
public static class StartMenuCatalogService
{
    public const int MaxShortcuts = 1500;
    public const int MaxDepth = 8;

    private static readonly string[] UninstallPathSegments =
    {
        "uninstall",
    };

    public static IReadOnlyList<StartMenuAppInfo> GetStartMenuApps()
    {
        var results = new List<StartMenuAppInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string root in GetRootFolders())
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            CollectFromRoot(root, results, seen, visitedFiles);
            if (results.Count >= MaxShortcuts)
            {
                break;
            }
        }

        return results
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static StartMenuAppInfo? ResolveShortcut(string shortcutPath)
    {
        if (string.IsNullOrWhiteSpace(shortcutPath) || !File.Exists(shortcutPath))
        {
            return null;
        }

        object? comObject = null;
        try
        {
            Type? linkType = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"));
            if (linkType is null)
            {
                return null;
            }

            comObject = Activator.CreateInstance(linkType);
            if (comObject is not IShellLinkW link)
            {
                return null;
            }

            var file = (IPersistFile)comObject;
            file.Load(shortcutPath, 0);

            var target = new System.Text.StringBuilder(1024);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0);

            var arguments = new System.Text.StringBuilder(1024);
            link.GetArguments(arguments, arguments.Capacity);

            var workingDir = new System.Text.StringBuilder(1024);
            link.GetWorkingDirectory(workingDir, workingDir.Capacity);

            string targetPath = target.ToString().Trim();
            if (string.IsNullOrEmpty(targetPath))
            {
                // Shell-only shortcuts (shell:::, GUID targets) have no resolvable file path;
                // they are skipped in v1.
                return null;
            }

            return new StartMenuAppInfo
            {
                Name = Path.GetFileNameWithoutExtension(shortcutPath),
                ShortcutPath = shortcutPath,
                TargetPath = targetPath,
                Arguments = arguments.ToString(),
                WorkingDirectory = workingDir.ToString(),
            };
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        finally
        {
            if (comObject is not null)
            {
                try
                {
                    Marshal.FinalReleaseComObject(comObject);
                }
                catch (ArgumentException)
                {
                }
            }
        }
    }

    private static string[] GetRootFolders() => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
    };

    private static void CollectFromRoot(
        string root,
        List<StartMenuAppInfo> results,
        HashSet<string> seen,
        HashSet<string> visitedFiles)
    {
        // Explicit depth-bounded walk: no symlink following, no unbounded recursion.
        var pending = new Stack<(string Directory, int Depth)>();
        pending.Push((root, 0));

        while (pending.Count > 0)
        {
            (string currentDir, int depth) = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(currentDir, "*.lnk", SearchOption.TopDirectoryOnly);
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string shortcut in files)
            {
                if (results.Count >= MaxShortcuts)
                {
                    return;
                }

                if (!visitedFiles.Add(shortcut))
                {
                    continue;
                }

                if (LooksLikeUninstaller(shortcut))
                {
                    continue;
                }

                StartMenuAppInfo? info = ResolveShortcut(shortcut);
                if (info is null || string.IsNullOrWhiteSpace(info.Name))
                {
                    continue;
                }

                if (LooksLikeUninstaller(info.Name) || LooksLikeUninstaller(info.TargetPath))
                {
                    continue;
                }

                if (info.TargetPath.EndsWith("unins000.exe", StringComparison.OrdinalIgnoreCase) ||
                    info.TargetPath.EndsWith("uninst.exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (seen.Add(info.DedupeKey))
                {
                    results.Add(info);
                }
            }

            if (depth >= MaxDepth)
            {
                continue;
            }

            string[] subdirs;
            try
            {
                subdirs = Directory.GetDirectories(currentDir);
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string subdir in subdirs)
            {
                try
                {
                    FileAttributes attributes = File.GetAttributes(subdir);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                pending.Push((subdir, depth + 1));
            }
        }
    }

    private static bool LooksLikeUninstaller(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string v = value;
        if (v.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
            v.Contains("uninstaller", StringComparison.OrdinalIgnoreCase) ||
            v.Contains("unins000", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (string segment in UninstallPathSegments)
        {
            if (v.Contains("\\" + segment + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
