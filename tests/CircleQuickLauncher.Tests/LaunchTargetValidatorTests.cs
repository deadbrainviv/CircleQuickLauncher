using CircleQuickLauncher.Core.Validation;
using Xunit;

namespace CircleQuickLauncher.Tests;

public class LaunchTargetValidatorTests
{
    [Fact]
    public void Validate_AbsoluteWindowsPath_IsAccepted()
    {
        LaunchTargetResult result = LaunchTargetValidator.ValidateTarget(@"C:\Program Files\App\app.exe");

        Assert.True(result.IsAcceptable);
    }

    [Fact]
    public void Validate_NullOrEmpty_IsRejected()
    {
        Assert.Equal(LaunchTargetIssue.Empty, LaunchTargetValidator.ValidateTarget(null).Issue);
        Assert.Equal(LaunchTargetIssue.Empty, LaunchTargetValidator.ValidateTarget("").Issue);
        Assert.Equal(LaunchTargetIssue.Empty, LaunchTargetValidator.ValidateTarget("   ").Issue);
    }

    [Fact]
    public void Validate_RelativePath_IsRejected()
    {
        LaunchTargetResult result = LaunchTargetValidator.ValidateTarget(@"relative\path.exe");

        Assert.False(result.IsAcceptable);
        Assert.Equal(LaunchTargetIssue.NotRooted, result.Issue);
    }

    [Fact]
    public void Validate_NewlineInjection_IsRejected()
    {
        LaunchTargetResult result = LaunchTargetValidator.ValidateTarget("C:\\app.exe\r\nmalicious");

        Assert.False(result.IsAcceptable);
        Assert.Equal(LaunchTargetIssue.ControlCharacters, result.Issue);
    }

    [Fact]
    public void Validate_EmbeddedNull_IsRejected()
    {
        LaunchTargetResult result = LaunchTargetValidator.ValidateTarget("C:\\app.exe\0evil");

        Assert.False(result.IsAcceptable);
        Assert.Equal(LaunchTargetIssue.ControlCharacters, result.Issue);
    }

    [Fact]
    public void Validate_TooLong_IsRejected()
    {
        string longPath = @"C:\" + new string('a', LaunchTargetValidator.MaxTargetLength);

        LaunchTargetResult result = LaunchTargetValidator.ValidateTarget(longPath);

        Assert.False(result.IsAcceptable);
        Assert.Equal(LaunchTargetIssue.TooLong, result.Issue);
    }

    [Fact]
    public void Validate_KnownUriScheme_IsAccepted()
    {
        LaunchTargetResult result = LaunchTargetValidator.ValidateTarget("ms-settings:display");

        Assert.True(result.IsAcceptable);
    }

    [Fact]
    public void Validate_HttpUri_IsAccepted()
    {
        Assert.True(LaunchTargetValidator.ValidateTarget("https://example.com/x").IsAcceptable);
    }

    [Fact]
    public void Validate_HostileShellMetacharactersInUri_IsRejected()
    {
        LaunchTargetResult result = LaunchTargetValidator.ValidateTarget("http://x.com/&&calc.exe");

        Assert.False(result.IsAcceptable);
        Assert.Equal(LaunchTargetIssue.SuspiciousShellInvocation, result.Issue);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe")]
    public void Validate_ShellExecutables_AreStillValidTargets(string path)
    {
        // Shells are legitimate launch targets when the user pins them; launch still
        // goes through ShellExecute with discrete argument fields, never string building.
        Assert.True(LaunchTargetValidator.ValidateTarget(path).IsAcceptable);
    }

    [Fact]
    public void IsNetworkLocation_DetectsUnc()
    {
        Assert.True(LaunchTargetValidator.IsNetworkLocation(@"\\server\share\app.exe"));
        Assert.False(LaunchTargetValidator.IsNetworkLocation(@"C:\local\app.exe"));
        Assert.False(LaunchTargetValidator.IsNetworkLocation(null));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("--flag value", true)]
    [InlineData("arg\0evil", false)]
    public void IsSafeArguments_ChecksContent(string? args, bool expected)
    {
        Assert.Equal(expected, LaunchTargetValidator.IsSafeArguments(args));
    }

    [Fact]
    public void IsSafeArguments_TooLong_IsRejected()
    {
        string longArgs = new string('a', LaunchTargetValidator.MaxArgumentsLength + 1);
        Assert.False(LaunchTargetValidator.IsSafeArguments(longArgs));
    }
}
