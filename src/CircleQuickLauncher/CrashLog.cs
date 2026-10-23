using CircleQuickLauncher.Services;

namespace CircleQuickLauncher;

/// <summary>Append-only diagnostic log in %AppData%\CircleQuickLauncher\logs. Never contains user content.</summary>
public static class CrashLog
{
    private static readonly object Gate = new();

    public static void Write(Exception exception)
    {
        try
        {
            lock (Gate)
            {
                string dir = AppState.LogsDirectory;
                Directory.CreateDirectory(dir);

                string path = Path.Combine(dir, $"error-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                string contents =
                    $"Timestamp: {DateTime.Now:o}{Environment.NewLine}" +
                    $"Version: {typeof(App).Assembly.GetName().Version}{Environment.NewLine}" +
                    $"OS: {Environment.OSVersion}{Environment.NewLine}" +
                    $"CLR: {Environment.Version}{Environment.NewLine}" +
                    $"{exception}{Environment.NewLine}";

                File.AppendAllText(path, contents);

                TrimOldLogs(dir);
            }
        }
        catch (Exception)
        {
            // Logging must never throw into the crash path.
        }
    }

    public static void Info(string message)
    {
        try
        {
            lock (Gate)
            {
                string dir = AppState.LogsDirectory;
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"info-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(path, $"{DateTime.Now:o} {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
        }
    }

    private static void TrimOldLogs(string dir)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-14);
            foreach (string file in Directory.EnumerateFiles(dir, "*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception)
        {
        }
    }
}
