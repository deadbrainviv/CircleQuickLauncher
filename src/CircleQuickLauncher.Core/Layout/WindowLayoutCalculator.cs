using CircleQuickLauncher.Core.Models;

namespace CircleQuickLauncher.Core.Layout;

/// <summary>Monitor work area expressed in device-independent pixels.</summary>
public readonly record struct WorkArea(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>Placement math for the launcher window. Pure functions, unit-testable.</summary>
public static class WindowLayoutCalculator
{
    /// <summary>Window side length in DIPs: room for the arc on both sides of the anchor.</summary>
    public static (double Width, double Height) GetWindowSize(
        double arcRadius, double childCircleSize, double margin)
    {
        double half = arcRadius + (childCircleSize / 2.0) + margin;
        double size = Math.Max(half * 2.0, childCircleSize + 2 * margin);
        return (size, size);
    }

    /// <summary>Anchor circle center in DIPs (virtual screen coordinates) for the collapsed peek state.</summary>
    public static (double X, double Y) GetAnchorCenter(
        LauncherSettings settings,
        WorkArea work,
        double windowWidth,
        double windowHeight)
    {
        double main = settings.MainCircleSize;
        double half = main / 2.0;
        double fraction = settings.PeekFraction;

        switch (settings.Edge)
        {
            case EdgePosition.Right:
            {
                // Circle center sits beyond the right edge so only `fraction` of it is visible.
                double cx = work.Right + half * (1.0 - 2.0 * fraction);
                double cy = ClampAxis(
                    work.Y + (work.Height / 2.0),
                    work.Y, work.Bottom, windowHeight);
                return (cx, cy);
            }

            case EdgePosition.Left:
            {
                double cx = work.Left - half * (1.0 - 2.0 * fraction);
                double cy = ClampAxis(
                    work.Y + (work.Height / 2.0),
                    work.Y, work.Bottom, windowHeight);
                return (cx, cy);
            }

            case EdgePosition.Bottom:
            {
                double cx = ClampAxis(
                    work.X + (work.Width / 2.0),
                    work.X, work.Right, windowWidth);
                double cy = work.Bottom + half * (1.0 - 2.0 * fraction);
                return (cx, cy);
            }

            case EdgePosition.FreeFloating:
            default:
                return (settings.FloatingX + windowWidth / 2.0, settings.FloatingY + windowHeight / 2.0);
        }
    }

    /// <summary>Window origin in DIPs given an anchor center.</summary>
    public static (double X, double Y) GetWindowOrigin(double anchorX, double anchorY, double windowWidth, double windowHeight) =>
        (anchorX - windowWidth / 2.0, anchorY - windowHeight / 2.0);

    /// <summary>Anchor circle center inside the window (always centered).</summary>
    public static (double X, double Y) GetAnchorCenterInWindow(double windowWidth, double windowHeight) =>
        (windowWidth / 2.0, windowHeight / 2.0);

    /// <summary>True when the point (DIPs) lies within the circle centered at (cx, cy).</summary>
    public static bool IsPointInsideCircle(double px, double py, double cx, double cy, double diameter)
    {
        double r = diameter / 2.0;
        double dx = px - cx;
        double dy = py - cy;
        return (dx * dx + dy * dy) <= r * r;
    }

    private static double ClampAxis(double value, double min, double max, double size)
    {
        double lo = min + size / 2.0;
        double hi = max - size / 2.0;
        return hi >= lo ? Math.Clamp(value, lo, hi) : min + (max - min) / 2.0;
    }
}
