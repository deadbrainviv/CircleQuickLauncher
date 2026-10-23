using CircleQuickLauncher.Core.Models;
using CircleQuickLauncher.Core.Storage;

namespace CircleQuickLauncher.Services;

/// <summary>
/// Central application state: owns the config store, settings and pinned apps.
/// Persistence happens on every mutation (crash-safe), UI listens to change events.
/// </summary>
public sealed class AppState
{
    public static AppState Instance { get; } = new();

    private readonly object _gate = new();
    private readonly List<AppEntry> _apps = new();

    private AppState()
    {
        Store = new JsonConfigStore(AppDataDirectory);
        Settings = Store.LoadSettings();
        foreach (AppEntry entry in Store.LoadApps())
        {
            _apps.Add(entry);
        }
    }

    public JsonConfigStore Store { get; }

    public LauncherSettings Settings { get; private set; }

    public IReadOnlyList<AppEntry> Apps
    {
        get
        {
            lock (_gate)
            {
                return _apps.Select(a => a.Clone()).ToList();
            }
        }
    }

    public event EventHandler? SettingsChanged;

    public event EventHandler? AppsChanged;

    /// <summary>%AppData%\CircleQuickLauncher</summary>
    public static string AppDataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
            "CircleQuickLauncher");

    public static string LogsDirectory => Path.Combine(AppDataDirectory, "logs");

    public string IconsDirectory => Store.IconsPath;

    public void UpdateSettings(Action<LauncherSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        lock (_gate)
        {
            mutate(Settings);
            Settings.Sanitize();
            Store.SaveSettings(Settings);
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds an entry. Returns false when the same target is already pinned.</summary>
    public bool AddApp(AppEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        string key = (entry.TargetPath.Trim() + "|" + entry.Arguments.Trim()).ToUpperInvariant();

        lock (_gate)
        {
            bool duplicate = _apps.Any(a =>
                string.Equals(
                    (a.TargetPath.Trim() + "|" + a.Arguments.Trim()).ToUpperInvariant(),
                    key,
                    StringComparison.Ordinal));
            if (duplicate)
            {
                return false;
            }

            _apps.RemoveAll(a => string.Equals(a.Id, entry.Id, StringComparison.Ordinal));
            _apps.Add(entry);
            Store.SaveApps(_apps);
        }

        AppsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool RemoveApp(string id)
    {
        bool removed;

        lock (_gate)
        {
            removed = _apps.RemoveAll(a => string.Equals(a.Id, id, StringComparison.Ordinal)) > 0;
            if (removed)
            {
                Store.SaveApps(_apps);
            }
        }

        if (removed)
        {
            AppsChanged?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    public AppEntry? FindApp(string id)
    {
        lock (_gate)
        {
            return _apps.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal))?.Clone();
        }
    }

    /// <summary>Persists changes to an existing entry (icon updates, renames).</summary>
    public bool UpdateApp(AppEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        bool updated;

        lock (_gate)
        {
            int index = _apps.FindIndex(a => string.Equals(a.Id, entry.Id, StringComparison.Ordinal));
            if (index >= 0)
            {
                _apps[index] = entry.Clone();
                Store.SaveApps(_apps);
                updated = true;
            }
            else
            {
                updated = false;
            }
        }

        if (updated)
        {
            AppsChanged?.Invoke(this, EventArgs.Empty);
        }

        return updated;
    }
}
