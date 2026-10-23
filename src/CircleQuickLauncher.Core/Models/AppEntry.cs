namespace CircleQuickLauncher.Core.Models;

/// <summary>A single application pinned to the circular quick launcher.</summary>
public sealed class AppEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>Fully qualified path to the executable or shortcut target.</summary>
    public string TargetPath { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;

    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>File name (not full path) of a user-selected PNG stored in the app's icon folder.</summary>
    public string? CustomIconFileName { get; set; }

    public DateTimeOffset DateAdded { get; set; } = DateTimeOffset.UtcNow;

    public AppEntry Clone() => new()
    {
        Id = Id,
        Name = Name,
        TargetPath = TargetPath,
        Arguments = Arguments,
        WorkingDirectory = WorkingDirectory,
        CustomIconFileName = CustomIconFileName,
        DateAdded = DateAdded,
    };
}
