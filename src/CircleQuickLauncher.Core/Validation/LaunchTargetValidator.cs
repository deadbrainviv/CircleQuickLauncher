namespace CircleQuickLauncher.Core.Validation;

public enum LaunchTargetIssue
{
    None = 0,
    Empty,
    TooLong,
    ControlCharacters,
    NotRooted,
    SuspiciousShellInvocation,
}

public sealed record LaunchTargetResult(bool IsAcceptable, LaunchTargetIssue Issue)
{
    public static readonly LaunchTargetResult Ok = new(true, LaunchTargetIssue.None);

    public static LaunchTargetResult Fail(LaunchTargetIssue issue) => new(false, issue);
}

/// <summary>
/// Decides whether a stored launch target is safe to hand to ShellExecute.
/// Targets are never concatenated into shell command strings; this guard only
/// filters malformed or obviously hostile values (e.g. from a tampered config).
/// </summary>
public static class LaunchTargetValidator
{
    public const int MaxTargetLength = 1024;
    public const int MaxArgumentsLength = 2048;

    private static readonly char[] ForbiddenControlChars = { '\r', '\n', '\0', '\t' };

    // Conservative rules for scheme-based non-file targets such as "ms-settings:display".

    public static LaunchTargetResult ValidateTarget(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return LaunchTargetResult.Fail(LaunchTargetIssue.Empty);
        }

        string target = targetPath.Trim();

        if (target.Length > MaxTargetLength)
        {
            return LaunchTargetResult.Fail(LaunchTargetIssue.TooLong);
        }

        if (target.IndexOfAny(ForbiddenControlChars) >= 0 || ContainsOtherControlChars(target))
        {
            return LaunchTargetResult.Fail(LaunchTargetIssue.ControlCharacters);
        }

        // Allow scheme-based targets (shell URIs) only when they are simple and well-formed.
        int schemeColon = target.IndexOf(':');
        bool looksLikeDrive = schemeColon == 1 && char.IsAsciiLetter(target[0]);
        if (schemeColon > 0 && !looksLikeDrive)
        {
            return IsSafeUriScheme(target)
                ? LaunchTargetResult.Ok
                : LaunchTargetResult.Fail(LaunchTargetIssue.SuspiciousShellInvocation);
        }

        if (!Path.IsPathRooted(target))
        {
            return LaunchTargetResult.Fail(LaunchTargetIssue.NotRooted);
        }

        return LaunchTargetResult.Ok;
    }

    public static bool IsSafeArguments(string? arguments) =>
        arguments is null
        || (arguments.Length <= MaxArgumentsLength
            && arguments.IndexOf('\0') < 0
            && !ContainsOtherControlChars(arguments));

    /// <summary>True when the target points at a UNC/network location and needs explicit user confirmation.</summary>
    public static bool IsNetworkLocation(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        try
        {
            return targetPath.StartsWith(@"\\", StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsSafeUriScheme(string target)
    {
        int colon = target.IndexOf(':');
        if (colon <= 0 || colon > 32)
        {
            return false;
        }

        for (int i = 0; i < colon; i++)
        {
            char c = target[i];
            bool ok = char.IsAsciiLetter(c) || (i > 0 && (char.IsAsciiDigit(c) || c == '+' || c == '-' || c == '.'));
            if (!ok)
            {
                return false;
            }
        }

        // No whitespace or shell metacharacters anywhere in a URI target.
        foreach (char c in target)
        {
            if (char.IsControl(c) || c is '&' or '|' or '<' or '>' or '^' or '`' or '$' or ' ')
            {
                return false;
            }
        }

        string rest = target[(colon + 1)..];
        if (rest.Length > 512)
        {
            return false;
        }

        try
        {
            return Uri.UnescapeDataString(rest).Length <= 512;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static bool ContainsOtherControlChars(string value)
    {
        foreach (char c in value)
        {
            if (char.IsControl(c))
            {
                return true;
            }
        }

        return false;
    }
}
