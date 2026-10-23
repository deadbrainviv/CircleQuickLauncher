using CircleQuickLauncher.Core.Validation;

namespace CircleQuickLauncher.Services;

public enum LaunchOutcome
{
    Started,
    TargetMissing,
    TargetRejected,
    Failed,
}

public sealed record LaunchResult(LaunchOutcome Outcome, string? Error = null);

/// <summary>
/// Launches pinned applications through ShellExecute with discrete parameters.
/// Never builds shell command strings; validates every field before use.
/// </summary>
public static class LaunchService
{
    public static LaunchResult Launch(string targetPath, string? arguments = null, string? workingDirectory = null)
    {
        LaunchTargetResult guard = LaunchTargetValidator.ValidateTarget(targetPath);
        if (!guard.IsAcceptable)
        {
            return new LaunchResult(LaunchOutcome.TargetRejected, $"Launch target rejected ({guard.Issue}).");
        }

        if (!LaunchTargetValidator.IsSafeArguments(arguments))
        {
            return new LaunchResult(LaunchOutcome.TargetRejected, "Launch arguments rejected.");
        }

        string target = targetPath.Trim();

        // Scheme targets (e.g. ms-settings:...) do not exist on disk.
        bool isScheme = IsSchemeTarget(target);
        if (!isScheme && target.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return new LaunchResult(LaunchOutcome.TargetRejected, "Launch target contains invalid characters.");
        }

        if (!isScheme)
        {
            try
            {
                target = Path.GetFullPath(target);
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
            {
                return new LaunchResult(LaunchOutcome.TargetRejected, "Launch target could not be normalized.");
            }

            if (!File.Exists(target))
            {
                return new LaunchResult(LaunchOutcome.TargetMissing, "The application was not found on this computer.");
            }
        }

        string? workingDir = null;
        if (!string.IsNullOrWhiteSpace(workingDirectory) && !isScheme)
        {
            try
            {
                string full = Path.GetFullPath(workingDirectory);
                if (Directory.Exists(full))
                {
                    workingDir = full;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
            {
            }
        }

        try
        {
            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
            };

            if (!string.IsNullOrWhiteSpace(arguments))
            {
                info.Arguments = arguments.Trim();
            }

            if (workingDir is not null)
            {
                info.WorkingDirectory = workingDir;
            }

            System.Diagnostics.Process? process = System.Diagnostics.Process.Start(info);
            if (process is null)
            {
                return new LaunchResult(LaunchOutcome.Failed, "Windows did not start the application.");
            }

            process.Dispose();
            return new LaunchResult(LaunchOutcome.Started);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new LaunchResult(LaunchOutcome.Failed, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return new LaunchResult(LaunchOutcome.Failed, ex.Message);
        }
    }

    private static bool IsSchemeTarget(string target)
    {
        int colon = target.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        // Drive-letter paths such as C:\... are not scheme targets.
        if (colon == 1 && char.IsAsciiLetter(target[0]))
        {
            return false;
        }

        return true;
    }
}
