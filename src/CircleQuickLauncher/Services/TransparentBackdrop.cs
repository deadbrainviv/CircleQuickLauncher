using System.Runtime.InteropServices;
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
/// </summary>
public sealed class TransparentBackdrop : SystemBackdrop
{
    // Both the dispatcher-queue controller and the compositor are held in static fields:
    // releasing either one tears down the queue / brush and transparency silently breaks.
    private static IntPtr _dispatcherQueueController = IntPtr.Zero;
    private static Windows.UI.Composition.Compositor? _compositor;

    private static Windows.UI.Composition.Compositor Compositor
    {
        get
        {
            if (_compositor is null)
            {
                EnsureDispatcherQueueController();
                _compositor = new Windows.UI.Composition.Compositor();
            }

            return _compositor;
        }
    }

    /// <summary>
    /// Windows.UI.Composition activation fails with E_ACCESSDENIED unless the thread has a
    /// Windows.System.DispatcherQueue. A WinUI 3 thread only has the Microsoft.UI dispatcher,
    /// so create the WinRT controller explicitly (same approach WinUIEx uses).
    /// </summary>
    private static void EnsureDispatcherQueueController()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is null && _dispatcherQueueController == IntPtr.Zero)
        {
            var options = new DispatcherQueueOptions
            {
                dwSize = Marshal.SizeOf(typeof(DispatcherQueueOptions)),
                threadType = 2,    // DQTYPE_THREAD_CURRENT
                apartmentType = 2, // DQTAT_COM_STA
            };

            CreateDispatcherQueueController(options, out _dispatcherQueueController);
        }
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        CrashLog.Info("TransparentBackdrop.OnTargetConnected fired.");

        try
        {
            // Assign before base so the fill is already transparent once the framework
            // wires up the target.
            connectedTarget.SystemBackdrop = Compositor.CreateColorBrush(Microsoft.UI.Colors.Transparent);
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

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        internal int dwSize;
        internal int threadType;
        internal int apartmentType;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(
        [In] DispatcherQueueOptions options,
        out IntPtr dispatcherQueueController);
}
