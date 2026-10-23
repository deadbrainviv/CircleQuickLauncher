namespace CircleQuickLauncher.Core.Models;

/// <summary>An application discovered in the Windows Start Menu.</summary>
public sealed class StartMenuAppInfo
{
    public string Name { get; set; } = string.Empty;

    public string ShortcutPath { get; set; } = string.Empty;

    public string TargetPath { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;

    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>Deterministic dedupe key: target path + arguments.</summary>
    public string DedupeKey => (TargetPath + "|" + Arguments).ToUpperInvariant();
}
