using CircleQuickLauncher.Core.Layout;
using CircleQuickLauncher.Core.Models;
using Xunit;

namespace CircleQuickLauncher.Tests;

public class ArcLayoutCalculatorTests
{
    [Fact]
    public void ComputeArc_ZeroCount_ReturnsEmpty()
    {
        IReadOnlyList<PointF> points = ArcLayoutCalculator.ComputeArc(0, 100, 0, 180);
        Assert.Empty(points);
    }

    [Fact]
    public void ComputeArc_SingleItem_PlacedAtMidpoint()
    {
        IReadOnlyList<PointF> points = ArcLayoutCalculator.ComputeArc(1, 100, 90, 270);

        Assert.Single(points);
        Assert.Equal(-100, points[0].X, 3); // cos(180 deg) * 100 (screen coords: left)
        Assert.Equal(0, points[0].Y, 3);    // sin(180 deg) * 100
    }

    [Fact]
    public void ComputeArc_TwoItems_UsesEndpoints()
    {
        IReadOnlyList<PointF> points = ArcLayoutCalculator.ComputeArc(2, 100, 0, 180);

        Assert.Equal(2, points.Count);
        Assert.Equal(100, points[0].X, 3);   // angle 0
        Assert.Equal(0, points[0].Y, 3);
        Assert.Equal(-100, points[1].X, 3);  // angle 180
        Assert.Equal(0, points[1].Y, 3);
    }

    [Fact]
    public void ComputeArc_MultipleItems_AllOnRadius()
    {
        IReadOnlyList<PointF> points = ArcLayoutCalculator.ComputeArc(8, 140, 90, 270);

        Assert.Equal(8, points.Count);
        foreach (PointF p in points)
        {
            double distance = Math.Sqrt(p.X * p.X + p.Y * p.Y);
            Assert.Equal(140, distance, 3);
        }
    }

    [Fact]
    public void ComputeArc_NegativeRadius_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ArcLayoutCalculator.ComputeArc(3, -1, 0, 180));
    }

    [Fact]
    public void ComputeArc_MonotonicAngles_AreDistinct()
    {
        IReadOnlyList<PointF> points = ArcLayoutCalculator.ComputeArc(12, 150, 90, 270);

        var distinct = points.Distinct().ToList();
        Assert.Equal(12, distinct.Count);
    }

    [Theory]
    [InlineData(EdgePosition.Right, 90, 270)]
    [InlineData(EdgePosition.Left, 270, 450)]
    [InlineData(EdgePosition.Bottom, 180, 360)]
    [InlineData(EdgePosition.FreeFloating, 0, 360)]
    public void GetInwardArcRange_ReturnsExpectedRange(EdgePosition edge, double start, double end)
    {
        (double s, double e) = ArcLayoutCalculator.GetInwardArcRange(edge);
        Assert.Equal(start, s);
        Assert.Equal(end, e);
    }

    [Theory]
    [InlineData(1, 140, 48)]       // radius sufficient for a single item
    [InlineData(12, 10, 48)]       // too small -> grows
    [InlineData(0, 10, 48)]        // degenerate count still returns >= circle size
    public void EnsureMinimumRadius_GrowsWhenTooTight(int count, double requested, double size)
    {
        double radius = ArcLayoutCalculator.EnsureMinimumRadius(count, requested, size, fullCircle: false);
        Assert.True(radius >= requested);
        Assert.True(radius > 0);
    }

    [Fact]
    public void EnsureMinimumRadius_SufficientRadius_Unchanged()
    {
        double radius = ArcLayoutCalculator.EnsureMinimumRadius(4, 200, 48, false);
        Assert.Equal(200, radius);
    }

    [Fact]
    public void EnsureMinimumRadius_Count1_ReturnsAtLeastCircleSize()
    {
        double radius = ArcLayoutCalculator.EnsureMinimumRadius(1, 10, 48, false);
        Assert.Equal(48, radius);
    }

    [Fact]
    public void EnsureMinimumRadius_TooTight_EnoughSpacingBetweenAdjacentCircles()
    {
        const int count = 12;
        const double size = 48;
        double radius = ArcLayoutCalculator.EnsureMinimumRadius(count, 10, size, fullCircle: false);

        IReadOnlyList<PointF> points = ArcLayoutCalculator.ComputeArc(count, radius, 90, 270);
        for (int i = 1; i < points.Count; i++)
        {
            double dx = points[i].X - points[i - 1].X;
            double dy = points[i].Y - points[i - 1].Y;
            double spacing = Math.Sqrt(dx * dx + dy * dy);
            Assert.True(spacing >= size, $"spacing {spacing:F1} < circle size {size}");
        }
    }
}
