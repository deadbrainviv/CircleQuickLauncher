using CircleQuickLauncher.Core.Models;
using CircleQuickLauncher.Core.Storage;
using Xunit;

namespace CircleQuickLauncher.Tests;

public class LauncherSettingsTests
{
    [Fact]
    public void Defaults_AreWithinSafeRanges()
    {
        var settings = new LauncherSettings();

        Assert.False(settings.Sanitize());
        Assert.Equal(EdgePosition.Right, settings.Edge);
        Assert.Equal(0.25, settings.PeekFraction);
        Assert.Equal(LauncherSettings.CurrentSchemaVersion, settings.SchemaVersion);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1)]
    [InlineData(2)]
    public void Sanitize_InvalidPeekFraction_ResetsToDefault(double value)
    {
        var settings = new LauncherSettings { PeekFraction = value };

        Assert.True(settings.Sanitize());
        Assert.Equal(0.25, settings.PeekFraction);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(500)]
    public void Sanitize_InvalidMainCircleSize_Resets(double value)
    {
        var settings = new LauncherSettings { MainCircleSize = value };

        Assert.True(settings.Sanitize());
        Assert.Equal(56, settings.MainCircleSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(-3)]
    public void Sanitize_InvalidMaxItems_Resets(int value)
    {
        var settings = new LauncherSettings { MaxItems = value };

        Assert.True(settings.Sanitize());
        Assert.Equal(12, settings.MaxItems);
    }

    [Fact]
    public void Sanitize_UnknownEdge_ResetsToRight()
    {
        var settings = new LauncherSettings { Edge = (EdgePosition)999 };

        Assert.True(settings.Sanitize());
        Assert.Equal(EdgePosition.Right, settings.Edge);
    }

    [Fact]
    public void Sanitize_OutdatedSchemaVersion_Bumps()
    {
        var settings = new LauncherSettings { SchemaVersion = 0 };

        Assert.True(settings.Sanitize());
        Assert.Equal(LauncherSettings.CurrentSchemaVersion, settings.SchemaVersion);
    }
}

public class JsonConfigStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly JsonConfigStore _store;

    public JsonConfigStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cql-tests-" + Guid.NewGuid().ToString("N"));
        _store = new JsonConfigStore(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Settings_RoundTrip_PreservesValues()
    {
        var settings = new LauncherSettings
        {
            Edge = EdgePosition.Bottom,
            PeekFraction = 0.4,
            MaxItems = 8,
            ArcRadius = 180,
            StartWithWindows = true,
            FloatingX = 33,
            FloatingY = 44,
        };

        _store.SaveSettings(settings);
        LauncherSettings loaded = _store.LoadSettings();

        Assert.Equal(EdgePosition.Bottom, loaded.Edge);
        Assert.Equal(0.4, loaded.PeekFraction);
        Assert.Equal(8, loaded.MaxItems);
        Assert.Equal(180, loaded.ArcRadius);
        Assert.True(loaded.StartWithWindows);
        Assert.Equal(33, loaded.FloatingX);
        Assert.Equal(44, loaded.FloatingY);
    }

    [Fact]
    public void Settings_MissingFile_ReturnsDefaults()
    {
        LauncherSettings loaded = _store.LoadSettings();
        Assert.Equal(EdgePosition.Right, loaded.Edge);
    }

    [Fact]
    public void Settings_CorruptJson_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_store.SettingsPath, "{ this is not json !!!");

        LauncherSettings loaded = _store.LoadSettings();

        Assert.Equal(EdgePosition.Right, loaded.Edge);
    }

    [Fact]
    public void Apps_RoundTrip_PreservesEntries()
    {
        var apps = new List<AppEntry>
        {
            new() { Name = "Notepad", TargetPath = @"C:\Windows\notepad.exe", Arguments = "" },
            new() { Name = "Editor", TargetPath = @"C:\Tools\editor.exe", Arguments = "--new", CustomIconFileName = "a.png" },
        };

        _store.SaveApps(apps);
        IReadOnlyList<AppEntry> loaded = _store.LoadApps();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("Editor", loaded[1].Name);
        Assert.Equal("a.png", loaded[1].CustomIconFileName);
        Assert.Equal("--new", loaded[1].Arguments);
    }

    [Fact]
    public void Apps_DuplicateTargets_Removed()
    {
        var apps = new List<AppEntry>
        {
            new() { Name = "A", TargetPath = @"C:\same.exe" },
            new() { Name = "B", TargetPath = @"C:\same.exe" },
            new() { Name = "C", TargetPath = @"C:\other.exe" },
        };

        _store.SaveApps(apps);
        IReadOnlyList<AppEntry> loaded = _store.LoadApps();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("A", loaded[0].Name);
    }

    [Fact]
    public void Apps_TrajectoryIconPath_Strip()
    {
        var apps = new List<AppEntry>
        {
            new() { Name = "A", TargetPath = @"C:\a.exe", CustomIconFileName = @"..\..\evil.png" },
            new() { Name = "B", TargetPath = @"C:\b.exe", CustomIconFileName = "sub/other.png" },
            new() { Name = "C", TargetPath = @"C:\c.exe", CustomIconFileName = "not-a-png.bmp" },
        };

        _store.SaveApps(apps);
        IReadOnlyList<AppEntry> loaded = _store.LoadApps();

        Assert.All(loaded, a => Assert.Null(a.CustomIconFileName));
    }

    [Fact]
    public void Apps_EntriesWithoutTarget_Dropped()
    {
        var apps = new List<AppEntry>
        {
            new() { Name = "No target", TargetPath = "   " },
            new() { Name = "Valid", TargetPath = @"C:\ok.exe" },
        };

        _store.SaveApps(apps);
        IReadOnlyList<AppEntry> loaded = _store.LoadApps();

        Assert.Single(loaded);
        Assert.Equal("Valid", loaded[0].Name);
    }

    [Fact]
    public void Apps_MissingFile_ReturnsEmpty()
    {
        IReadOnlyList<AppEntry> loaded = _store.LoadApps();
        Assert.Empty(loaded);
    }

    [Fact]
    public void Save_LeavesNoTempFilesBehind()
    {
        _store.SaveSettings(new LauncherSettings());
        _store.SaveApps(new List<AppEntry> { new() { Name = "X", TargetPath = @"C:\x.exe" } });

        string[] temps = Directory.GetFiles(_dir, "*.tmp");
        Assert.Empty(temps);
    }

    [Fact]
    public void Save_OverExistingFile_Works()
    {
        _store.SaveApps(new List<AppEntry> { new() { Name = "One", TargetPath = @"C:\1.exe" } });
        _store.SaveApps(new List<AppEntry> { new() { Name = "Two", TargetPath = @"C:\2.exe" } });

        IReadOnlyList<AppEntry> loaded = _store.LoadApps();
        Assert.Single(loaded);
        Assert.Equal("Two", loaded[0].Name);
    }
}
