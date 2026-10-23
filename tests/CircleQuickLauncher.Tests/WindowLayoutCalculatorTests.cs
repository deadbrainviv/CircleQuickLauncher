using CircleQuickLauncher.Core.Layout;
using CircleQuickLauncher.Core.Models;
using Xunit;

namespace CircleQuickLauncher.Tests;

public class WindowLayoutCalculatorTests
{
    private static readonly WorkArea Work = new(0, 0, 2880, 1752);

    [Fact]
    public void GetAnchorCenter_RightEdge_PlacesCirclePeekingPastEdge()
    {
        var settings = new LauncherSettings
        {
            Edge = EdgePosition.Right,
            PeekFraction = 0.25,
            MainCircleSize = 56,
        };

        (double x, double y) = WindowLayoutCalculator.GetAnchorCenter(settings, Work, 704, 704);

        // Center sits 14 DIP past the right edge so exactly 25% of the circle is visible.
        Assert.Equal(2894, x, 3);
    }

    [Fact]
    public void GetAnchorCenter_RightEdge_UsesEdgeFractionForVerticalPosition()
    {
        var settings = new LauncherSettings
        {
            Edge = EdgePosition.Right,
            EdgeFraction = 0.75,
            PeekFraction = 0.25,
            MainCircleSize = 56,
        };

        (double x, double y) = WindowLayoutCalculator.GetAnchorCenter(settings, Work, 704, 704);

        // Three-quarters down the edge (1752 * 0.75), inside the window-size clamp.
        Assert.Equal(1314, y, 3);
        Assert.Equal(2894, x, 3);
    }

    [Fact]
    public void GetAnchorCenter_RightEdge_CenteredFraction_YieldsMidEdge()
    {
        var settings = new LauncherSettings
        {
            Edge = EdgePosition.Right,
            EdgeFraction = 0.5,
            PeekFraction = 0.25,
            MainCircleSize = 56,
        };

        (_, double y) = WindowLayoutCalculator.GetAnchorCenter(settings, Work, 704, 704);

        Assert.Equal(876, y, 3);
    }

    [Fact]
    public void GetAnchorCenter_BottomEdge_UsesEdgeFractionForHorizontalPosition()
    {
        var settings = new LauncherSettings
        {
            Edge = EdgePosition.Bottom,
            EdgeFraction = 0.75,
            PeekFraction = 0.25,
            MainCircleSize = 56,
        };

        (double x, double y) = WindowLayoutCalculator.GetAnchorCenter(settings, Work, 704, 704);

        Assert.Equal(2160, x, 3);  // 2880 * 0.75
        Assert.Equal(1766, y, 3);  // 1752 + 14 (peeks past the bottom edge)
    }

    [Fact]
    public void GetAnchorCenter_FreeFloating_UsesStoredOriginPlusHalfWindow()
    {
        var settings = new LauncherSettings
        {
            Edge = EdgePosition.FreeFloating,
            FloatingX = 120,
            FloatingY = 240,
        };

        (double x, double y) = WindowLayoutCalculator.GetAnchorCenter(settings, Work, 704, 704);

        Assert.Equal(472, x, 3);
        Assert.Equal(592, y, 3);
    }

    [Fact]
    public void Sanitize_InvalidPlacementValues_ResetToDefaults()
    {
        var settings = new LauncherSettings
        {
            EdgeFraction = 7.0,
            HoverScalePercent = -5,
            DisplayDevice = null!,
        };

        Assert.True(settings.Sanitize());
        Assert.Equal(0.75, settings.EdgeFraction);
        Assert.Equal(30, settings.HoverScalePercent);
        Assert.Equal("", settings.DisplayDevice);
    }

    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var settings = new LauncherSettings();

        Assert.Equal(0.75, settings.EdgeFraction);
        Assert.Equal(30, settings.HoverScalePercent);
        Assert.Equal("", settings.DisplayDevice);
    }
}
