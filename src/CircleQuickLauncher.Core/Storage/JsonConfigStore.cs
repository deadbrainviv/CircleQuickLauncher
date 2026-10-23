using System.Text.Json;
using System.Text.Json.Serialization;
using CircleQuickLauncher.Core.Models;

namespace CircleQuickLauncher.Core.Storage;

/// <summary>
/// Loads and saves launcher state as JSON in a per-user directory.
/// All files are size-capped, deserialized with strict, non-polymorphic DTOs,
/// and sanitized before they reach the UI.
/// </summary>
public sealed class JsonConfigStore
{
    public const long MaxConfigBytes = 512 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        NumberHandling = JsonNumberHandling.Strict,
    };

    private readonly string _directory;

    public JsonConfigStore(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        _directory = directory;
    }

    public string DirectoryPath => _directory;

    public string SettingsPath => Path.Combine(_directory, "settings.json");

    public string AppsPath => Path.Combine(_directory, "apps.json");

    public string IconsPath => Path.Combine(_directory, "icons");

    public LauncherSettings LoadSettings()
    {
        LauncherSettings? settings = ReadJsonFile<LauncherSettings>(SettingsPath);
        settings ??= new LauncherSettings();

        bool changed = settings.Sanitize();
        if (changed)
        {
            SaveSettings(settings);
        }

        return settings;
    }

    public void SaveSettings(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Sanitize();
        WriteJsonFile(SettingsPath, settings);
    }

    public IReadOnlyList<AppEntry> LoadApps()
    {
        List<AppEntry>? entries = ReadJsonFile<List<AppEntry>>(AppsPath);
        if (entries is null || entries.Count == 0)
        {
            return Array.Empty<AppEntry>();
        }

        var sanitized = new List<AppEntry>(entries.Count);
        bool changed = false;
        foreach (AppEntry entry in entries)
        {
            if (entry is null)
            {
                changed = true;
                continue;
            }

            if (SanitizeEntry(entry))
            {
                changed = true;
            }

            if (!string.IsNullOrEmpty(entry.TargetPath))
            {
                sanitized.Add(entry);
            }
        }

        // Remove duplicate ids / duplicate targets, preserving order.
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<AppEntry>(sanitized.Count);
        foreach (AppEntry entry in sanitized)
        {
            if (seenIds.Add(entry.Id) && seenTargets.Add(entry.TargetPath + "|" + entry.Arguments))
            {
                deduped.Add(entry);
            }
            else
            {
                changed = true;
            }
        }

        if (changed)
        {
            SaveApps(deduped);
        }

        return deduped;
    }

    public void SaveApps(IReadOnlyList<AppEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        WriteJsonFile(AppsPath, entries);
    }

    private static bool SanitizeEntry(AppEntry entry)
    {
        bool changed = false;

        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            entry.Name = Path.GetFileNameWithoutExtension(entry.TargetPath) ?? "Unknown";
            changed = true;
        }

        if (entry.Name.Length > 128)
        {
            entry.Name = entry.Name[..128];
            changed = true;
        }

        entry.Name = entry.Name.Trim();
        entry.TargetPath = entry.TargetPath?.Trim() ?? string.Empty;
        entry.Arguments = (entry.Arguments ?? string.Empty).Trim();
        entry.WorkingDirectory = (entry.WorkingDirectory ?? string.Empty).Trim();

        if (entry.Arguments.Length > 2048)
        {
            entry.Arguments = entry.Arguments[..2048];
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(entry.Id) || entry.Id.Length > 64)
        {
            entry.Id = Guid.NewGuid().ToString("N");
            changed = true;
        }

        // Custom icons must be bare file names inside the icon folder: no directories, no traversal.
        if (!string.IsNullOrEmpty(entry.CustomIconFileName))
        {
            string name = entry.CustomIconFileName;
            if (name.Contains('/') || name.Contains('\\') || name.Contains("..") ||
                !string.Equals(Path.GetExtension(name), ".png", StringComparison.OrdinalIgnoreCase))
            {
                entry.CustomIconFileName = null;
                changed = true;
            }
            else
            {
                string? dir = Path.GetDirectoryName(name);
                if (!string.IsNullOrEmpty(dir))
                {
                    entry.CustomIconFileName = null;
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static T? ReadJsonFile<T>(string path) where T : class
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxConfigBytes)
            {
                return null;
            }

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
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
    }

    private void WriteJsonFile<T>(string path, T value)
    {
        string json = JsonSerializer.Serialize(value, Options);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxConfigBytes)
        {
            throw new InvalidOperationException("Configuration exceeds the maximum supported size.");
        }

        AtomicFile.WriteAllText(path, json);
    }
}
