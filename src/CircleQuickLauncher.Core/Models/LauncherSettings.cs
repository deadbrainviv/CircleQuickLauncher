namespace CircleQuickLauncher.Core.Models;

public enum EdgePosition
{
    Right = 0,
    Left = 1,
    Bottom = 2,
    FreeFloating = 3,
}

/// <summary>User-facing configuration, persisted as versioned JSON.</summary>
public sealed class LauncherSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public EdgePosition Edge { get; set; } = EdgePosition.Right;

    /// <summary>Fraction of the main circle that peeks out from the edge (0.10 - 0.75).</summary>
    public double PeekFraction { get; set; } = 0.25;

    /// <summary>Diameter in device-independent pixels of the anchor circle.</summary>
    public double MainCircleSize { get; set; } = 56;

    /// <summary>Diameter in device-independent pixels of each app circle.</summary>
    public double ChildCircleSize { get; set; } = 48;

    /// <summary>Diameter in device-independent pixels of the logo/icon drawn inside each app circle.</summary>
    public double IconSize { get; set; } = 32;

    /// <summary>Radius of the radial arc in device-independent pixels.</summary>
    public double ArcRadius { get; set; } = 140;

    /// <summary>Maximum number of app circles shown in the arc.</summary>
    public int MaxItems { get; set; } = 12;

    /// <summary>Milliseconds the menu waits after pointer exit before collapsing.</summary>
    public int CollapseDelayMs { get; set; } = 400;

    public bool StartWithWindows { get; set; }

    /// <summary>Free-floating window position (device-independent pixels, virtual screen).</summary>
    public double FloatingX { get; set; } = 120;

    public double FloatingY { get; set; } = 240;

    /// <summary>Clamps values into safe, supported ranges. Returns true if anything changed.</summary>
    public bool Sanitize()
    {
        bool changed = false;

        if (!Enum.IsDefined(Edge))
        {
            Edge = EdgePosition.Right;
            changed = true;
        }

        if (double.IsNaN(PeekFraction) || PeekFraction is < 0.10 or > 0.75)
        {
            PeekFraction = 0.25;
            changed = true;
        }

        if (double.IsNaN(MainCircleSize) || MainCircleSize is < 32 or > 128)
        {
            MainCircleSize = 56;
            changed = true;
        }

        if (double.IsNaN(ChildCircleSize) || ChildCircleSize is < 24 or > 96)
        {
            ChildCircleSize = 48;
            changed = true;
        }

        if (double.IsNaN(IconSize) || IconSize is < 12 or > 80)
        {
            IconSize = 32;
            changed = true;
        }

        if (double.IsNaN(ArcRadius) || ArcRadius is < 70 or > 400)
        {
            ArcRadius = 140;
            changed = true;
        }

        if (MaxItems is < 1 or > 20)
        {
            MaxItems = 12;
            changed = true;
        }

        if (CollapseDelayMs is < 0 or > 5000)
        {
            CollapseDelayMs = 400;
            changed = true;
        }

        if (double.IsNaN(FloatingX))
        {
            FloatingX = 120;
            changed = true;
        }

        if (double.IsNaN(FloatingY))
        {
            FloatingY = 240;
            changed = true;
        }

        if (SchemaVersion != CurrentSchemaVersion)
        {
            SchemaVersion = CurrentSchemaVersion;
            changed = true;
        }

        return changed;
    }
}
