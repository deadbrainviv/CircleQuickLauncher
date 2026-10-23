namespace CircleQuickLauncher.Core.Layout;

/// <summary>A point in device-independent pixels.</summary>
public readonly record struct PointF(double X, double Y);

/// <summary>
/// Computes where circles are placed on the radial arc. Pure math so it can be unit tested.
/// Coordinate space: origin at the anchor circle's center, x grows right, y grows down,
/// angle measured in degrees clockwise-positive in screen space (0 = right, 90 = down).
/// </summary>
public static class ArcLayoutCalculator
{
    /// <summary>
    /// Returns center positions for <paramref name="count"/> circles on an arc from
    /// <paramref name="startAngleDeg"/> to <paramref name="endAngleDeg"/> inclusive.
    /// One item is placed at the arc midpoint.
    /// </summary>
    public static IReadOnlyList<PointF> ComputeArc(
        int count,
        double radius,
        double startAngleDeg,
        double endAngleDeg)
    {
        if (count <= 0)
        {
            return Array.Empty<PointF>();
        }

        if (radius < 0 || double.IsNaN(radius))
        {
            throw new ArgumentOutOfRangeException(nameof(radius));
        }

        if (count == 1)
        {
            double mid = (startAngleDeg + endAngleDeg) / 2.0;
            return new[] { ToPoint(mid, radius) };
        }

        // A full turn distributes items exclusively so 0 and 360 degrees are not duplicated.
        if (endAngleDeg - startAngleDeg >= 360.0)
        {
            double step = 360.0 / count;
            var loop = new PointF[count];
            for (int i = 0; i < count; i++)
            {
                loop[i] = ToPoint(startAngleDeg + step * i, radius);
            }

            return loop;
        }

        double span = (endAngleDeg - startAngleDeg) / (count - 1);
        var points = new PointF[count];
        for (int i = 0; i < count; i++)
        {
            points[i] = ToPoint(startAngleDeg + span * i, radius);
        }

        return points;
    }

    /// <summary>
    /// Returns the angular range (degrees) of the semicircle that opens toward the screen
    /// interior for an anchored edge. The angle convention matches <see cref="ComputeArc"/>.
    /// </summary>
    public static (double StartDeg, double EndDeg) GetInwardArcRange(Models.EdgePosition edge) => edge switch
    {
        // Anchor on the right edge -> menu opens to the left (toward 180 degrees).
        Models.EdgePosition.Right => (90, 270),
        // Anchor on the left edge -> menu opens to the right (toward 0 degrees).
        Models.EdgePosition.Left => (270, 450),
        // Anchor on the bottom edge -> menu opens upward (toward 270 degrees).
        Models.EdgePosition.Bottom => (180, 360),
        // Floating anchor -> full circle around the anchor.
        Models.EdgePosition.FreeFloating => (0, 360),
        _ => (90, 270),
    };

    /// <summary>
    /// Guarantees enough room between circles: if the requested radius is too small for the
    /// item count and circle size, a larger radius that satisfies minimum spacing is returned.
    /// </summary>
    public static double EnsureMinimumRadius(int count, double requestedRadius, double circleSize, bool fullCircle)
    {
        if (count <= 1 || circleSize <= 0 || double.IsNaN(circleSize))
        {
            return Math.Max(requestedRadius, circleSize);
        }

        double arcLengthFactor = fullCircle ? 2 * Math.PI : Math.PI; // semicircle by default
        const double minSpacingRatio = 1.18; // gap between circle centers vs circle diameter

        // Arc length available = factor * r; required length = (count - 1) * size * ratio along the arc.
        double required = (count - 1) * circleSize * minSpacingRatio;
        double minimumRadius = required / arcLengthFactor;

        return Math.Max(requestedRadius, minimumRadius);
    }

    private static PointF ToPoint(double angleDeg, double radius)
    {
        double radians = angleDeg * Math.PI / 180.0;
        return new PointF(radius * Math.Cos(radians), radius * Math.Sin(radians));
    }
}
