using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace CircleQuickLauncher.Services;

/// <summary>
/// A SystemBackdrop that paints nothing, replacing WinUI's default opaque window fill
/// (black on dark theme, white on light theme) with a fully transparent composition brush
/// so the desktop shows through the launcher.
/// WinUI 3 has no first-class transparency API: overriding <see cref="OnTargetConnected"/>
/// and assigning a transparent brush to the backdrop target is the supported hook.
/// The brush must be built from the <c>Windows.UI.Composition</c> compositor, which is the
/// type the backdrop target expects.
/// </summary>
public sealed class TransparentBackdrop : SystemBackdrop
{
    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        CrashLog.Info("TransparentBackdrop.OnTargetConnected fired.");

        // Assign before base so the fill is already transparent once the framework
        // wires up the target (mirrors WinUIEx's CompositionBrushBackdrop).
        try
        {
            var compositor = new Windows.UI.Composition.Compositor();
            connectedTarget.SystemBackdrop = compositor.CreateColorBrush(Microsoft.UI.Colors.Transparent);
            CrashLog.Info("TransparentBackdrop: transparent brush assigned.");
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
        }

        base.OnTargetConnected(connectedTarget, xamlRoot);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        var backdrop = disconnectedTarget.SystemBackdrop;
        disconnectedTarget.SystemBackdrop = null;
        (backdrop as IDisposable)?.Dispose();

        base.OnTargetDisconnected(disconnectedTarget);
    }
}
