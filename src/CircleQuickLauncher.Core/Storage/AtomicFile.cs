namespace CircleQuickLauncher.Core.Storage;

/// <summary>
/// Crash-safe file replacement: writes to a temp file in the same directory,
/// then atomically swaps it over the destination.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(contents);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllText(tempPath, contents);

            if (File.Exists(path))
            {
                // File.Replace keeps a backup and is atomic on NTFS.
                string backupPath = path + ".bak";
                File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
                try
                {
                    File.Delete(backupPath);
                }
                catch (IOException)
                {
                    // Backup cleanup is best-effort.
                }
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
