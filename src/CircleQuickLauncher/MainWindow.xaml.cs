using System.Numerics;
using System.Runtime.InteropServices;
using CircleQuickLauncher.Core.Layout;
using CircleQuickLauncher.Core.Models;
using CircleQuickLauncher.Interop;
using CircleQuickLauncher.Services;
using CircleQuickLauncher.Views;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Windows.Foundation;
using Windows.Storage.Pickers;

namespace CircleQuickLauncher;

public sealed partial class MainWindow : Window
{
    private const double WindowMargin = 14.0;
    private const double CircleHitPadding = 8.0;

    private readonly AppState _state = AppState.Instance;
    private readonly TrayIconService _tray = new();
    private readonly List<(Button Button, AppEntry? Entry, double CenterXDip, double CenterYDip)> _circles = new();
    private readonly List<AppEntry> _allEntries;

    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _topmostTimer;

    private IntPtr _hwnd;
    private IntPtr _originalWndProc;
    private IntPtr _ourWndProcPtr;
    private NativeMethods.WndProcDelegate? _wndProcDelegate;
    private bool _interopFinalized;

    private bool _expanded;
    private bool _searchMode;
    private bool _dialogOpen;
    private bool _flyoutOpen;
    private bool _dragging;
    private bool _dragMoved;
    private DateTime _pointerOutSince = DateTime.MinValue;

    private double _scale = 1.0;
    private int _windowOriginXPx;
    private int _windowOriginYPx;
    private double _windowWidthDip;
    private double _windowHeightDip;

    private Point? _dragStartCursorPx;
    private (int X, int Y) _dragStartWindowPx;

    private SettingsWindow? _settingsWindow;

    public MainWindow()
    {
        InitializeComponent();

        _allEntries = _state.Apps.ToList();

        WindowService.ConfigureLauncherWindow(this);
        _hwnd = WindowService.GetHwnd(this);
        _scale = GetDpiScale();

        AnchorButton.PointerEntered += OnAnchorPointerEntered;
        AnchorButton.PointerPressed += OnAnchorPointerPressed;
        AnchorButton.PointerMoved += OnAnchorPointerMoved;
        AnchorButton.PointerReleased += OnAnchorPointerReleased;
        AnchorButton.ContextFlyout = BuildAnchorFlyout();

        SubclassWindowProc();
        CrashLog.Info($"Main window init hwnd=0x{_hwnd:X} origWndProc=0x{_originalWndProc:X} scale={_scale:F2}");
        WireTray();

        _state.SettingsChanged += OnSettingsOrAppsChanged;
        _state.AppsChanged += OnSettingsOrAppsChanged;

        ApplyWindowGeometry();
        RebuildCircles();
        UpdateAnchorVisual();

        _tray.Attach(_hwnd);
        WindowService.ReassertTopmost(_hwnd);

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _pollTimer.Tick += OnPollTick;

        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _topmostTimer.Tick += (_, _) => WindowService.ReassertTopmost(_hwnd);
        _topmostTimer.Start();

        Closed += OnWindowClosed;
    }

    // ------------------------------------------------------------------
    // Window procedure: click-through, no-activate, tray messages
    // ------------------------------------------------------------------

    private void SubclassWindowProc()
    {
        _wndProcDelegate = SubclassWndProc;
        _ourWndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        _originalWndProc = NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWLP_WNDPROC, _ourWndProcPtr);
    }

    /// <summary>
    /// WinUI may install its own window procedure during Activate(); re-assert our
    /// subclass after activation so hit-testing and tray messages keep flowing to us.
    /// </summary>
    public void FinalizeInterop()
    {
        if (_interopFinalized || _wndProcDelegate is null)
        {
            return;
        }

        _interopFinalized = true;

        try
        {
            IntPtr current = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWLP_WNDPROC);
            if (current != _ourWndProcPtr)
            {
                _originalWndProc = NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWLP_WNDPROC, _ourWndProcPtr);
                CrashLog.Info($"WndProc re-asserted (framework had 0x{current:X}).");
            }

            WindowService.ApplyTransparentFrame(_hwnd);
            WindowService.ReassertTopmost(_hwnd);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
        }
    }

    private IntPtr SubclassWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (msg)
            {
                case NativeMethods.WM_NCHITTEST:
                {
                    if (_dialogOpen || _flyoutOpen || (_searchMode && SearchBox.FocusState != FocusState.Unfocused))
                    {
                        return new IntPtr(NativeMethods.HTCLIENT);
                    }

                    long lp = lParam.ToInt64();
                    var screen = new NativeMethods.POINT
                    {
                        X = unchecked((short)(lp & 0xFFFF)),
                        Y = unchecked((short)((lp >> 16) & 0xFFFF)),
                    };
                    NativeMethods.ScreenToClient(hWnd, ref screen);

                    var xamlPoint = new Point(screen.X / _scale, screen.Y / _scale);
                    bool interactive = IsOverInteractiveContent(xamlPoint);

                    return new IntPtr(interactive ? NativeMethods.HTCLIENT : NativeMethods.HTTRANSPARENT);
                }

                case NativeMethods.WM_MOUSEACTIVATE:
                    return new IntPtr(NativeMethods.MA_NOACTIVATE);

                case NativeMethods.WM_ERASEBKGND:
                    return new IntPtr(1);

                default:
                    if (_tray.HandleMessage(msg, wParam, lParam))
                    {
                        return IntPtr.Zero;
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
        }

        return NativeMethods.CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    private bool IsOverInteractiveContent(Point xamlPoint)
    {
        foreach (UIElement element in VisualTreeHelper.FindElementsInHostCoordinates(xamlPoint, RootGrid))
        {
            if (IsInteractiveElement(element))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInteractiveElement(UIElement element)
    {
        DependencyObject? current = element;
        int guard = 0;
        while (current is not null && guard++ < 32)
        {
            if (current is Button or TextBox)
            {
                var ui = (UIElement)current;
                return ui.Visibility == Visibility.Visible && ui.IsHitTestVisible && ui.Opacity > 0.05;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    // ------------------------------------------------------------------
    // Tray
    // ------------------------------------------------------------------

    private void WireTray()
    {
        _tray.ShowSettingsRequested += (_, _) => ShowSettingsWindow();
        _tray.AddAppRequested += (_, _) => DispatcherQueue.TryEnqueue(async () => await ShowAddDialogAsync());
        _tray.OpenDataFolderRequested += (_, _) => OpenDataFolder();
        _tray.ToggleAutorunRequested += (_, _) =>
        {
            try
            {
                bool next = !AutorunService.IsEnabled();
                AutorunService.SetEnabled(next);
                _state.UpdateSettings(s => s.StartWithWindows = next);
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
                ShowStatus(ex.Message);
            }
        };
        _tray.ExitRequested += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            _tray.Dispose();
            ((App)Application.Current).ExitApp();
        });
    }

    private MenuFlyout BuildAnchorFlyout()
    {
        var flyout = new MenuFlyout();
        flyout.Opened += (_, _) => _flyoutOpen = true;
        flyout.Closed += (_, _) => _flyoutOpen = false;

        var settings = new MenuFlyoutItem { Text = "Settings" };
        settings.Click += (_, _) => ShowSettingsWindow();

        var add = new MenuFlyoutItem { Text = "Add app…" };
        add.Click += async (_, _) => await ShowAddDialogAsync();

        var data = new MenuFlyoutItem { Text = "Open data folder" };
        data.Click += (_, _) => OpenDataFolder();

        var exit = new MenuFlyoutItem { Text = "Exit" };
        exit.Click += (_, _) =>
        {
            _tray.Dispose();
            ((App)Application.Current).ExitApp();
        };

        flyout.Items.Add(settings);
        flyout.Items.Add(add);
        flyout.Items.Add(data);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(exit);
        return flyout;
    }

    // ------------------------------------------------------------------
    // Geometry
    // ------------------------------------------------------------------

    private void ApplyWindowGeometry()
    {
        LauncherSettings settings = _state.Settings;
        bool floating = settings.Edge == EdgePosition.FreeFloating;

        double windowRadius = ArcLayoutCalculator.EnsureMinimumRadius(
            settings.MaxItems + 1,
            settings.ArcRadius,
            settings.ChildCircleSize,
            floating);

        (double width, double height) = WindowLayoutCalculator.GetWindowSize(
            windowRadius, settings.ChildCircleSize, WindowMargin);
        _windowWidthDip = width;
        _windowHeightDip = height;

        RootGrid.Width = width;
        RootGrid.Height = height;

        AnchorButton.Width = settings.MainCircleSize;
        AnchorButton.Height = settings.MainCircleSize;

        WorkArea work = WindowService.GetPrimaryWorkAreaDip();
        (double anchorX, double anchorY) = WindowLayoutCalculator.GetAnchorCenter(settings, work, width, height);
        (double originXDip, double originYDip) = WindowLayoutCalculator.GetWindowOrigin(anchorX, anchorY, width, height);

        if (floating)
        {
            _scale = WindowService.GetScaleForPoint(
                (int)Math.Round(originXDip * Math.Max(_scale, 0.5)),
                (int)Math.Round(originYDip * Math.Max(_scale, 0.5)));
        }
        else
        {
            _scale = GetPrimaryMonitorScale();
        }

        int originXPx = (int)Math.Round(originXDip * _scale);
        int originYPx = (int)Math.Round(originYDip * _scale);
        int widthPx = (int)Math.Round(width * _scale);
        int heightPx = (int)Math.Round(height * _scale);

        WindowService.MoveAndResize(_hwnd, originXPx, originYPx, widthPx, heightPx);
        _windowOriginXPx = originXPx;
        _windowOriginYPx = originYPx;

        PositionOverlayContent();
        WindowService.ApplyTransparentFrame(_hwnd);
    }

    private void PositionOverlayContent()
    {
        LauncherSettings settings = _state.Settings;

        // The search box overlays the anchor center so it is always on the visible side.
        SearchBox.Width = Math.Min(180, _windowWidthDip - 40);
        Canvas.SetLeft(SearchBox, (_windowWidthDip - SearchBox.Width) / 2.0);
        Canvas.SetTop(SearchBox, (_windowHeightDip - 36) / 2.0);

        double statusY = settings.Edge == EdgePosition.Bottom
            ? 24
            : _windowHeightDip - 64;
        Canvas.SetLeft(StatusText, Math.Max(4, (_windowWidthDip - 230) / 2.0));
        Canvas.SetTop(StatusText, Math.Max(4, statusY));
    }

    private double GetDpiScale()
    {
        try
        {
            uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch (EntryPointNotFoundException)
        {
            return 1.0;
        }
    }

    private static double GetPrimaryMonitorScale()
    {
        IntPtr monitor = NativeMethods.MonitorFromPoint(
            new NativeMethods.POINT { X = 0, Y = 0 }, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        double scale = WindowService.GetDpiForMonitor(monitor) / 96.0;
        return scale > 0 ? scale : 1.0;
    }

    // ------------------------------------------------------------------
    // Circle building
    // ------------------------------------------------------------------

    private void RebuildCircles()
    {
        ArcCanvas.Children.Clear();
        _circles.Clear();
        ArcCanvas.Children.Add(SearchBox);
        ArcCanvas.Children.Add(StatusText);

        LauncherSettings settings = _state.Settings;
        double childSize = settings.ChildCircleSize;
        bool floating = settings.Edge == EdgePosition.FreeFloating;

        IReadOnlyList<AppEntry> visible = GetFilteredEntries();
        int maxShown = Math.Min(settings.MaxItems, visible.Count);
        IReadOnlyList<AppEntry> shown = visible.Take(maxShown).ToList();

        int totalSlots = shown.Count + 1; // + add button
        double radius = ArcLayoutCalculator.EnsureMinimumRadius(
            totalSlots, settings.ArcRadius, childSize, floating);
        radius = Math.Min(radius, MaxLayoutRadius());

        (double start, double end) = ArcLayoutCalculator.GetInwardArcRange(settings.Edge);
        IReadOnlyList<PointF> points = ArcLayoutCalculator.ComputeArc(totalSlots, radius, start, end);

        // Arc points are relative to the anchor (window center) -> translate into canvas space.
        double centerX = _windowWidthDip / 2.0;
        double centerY = _windowHeightDip / 2.0;

        for (int i = 0; i < shown.Count; i++)
        {
            AppEntry entry = shown[i];
            Button button = CreateAppButton(entry, childSize);
            double cx = centerX + points[i].X;
            double cy = centerY + points[i].Y;
            PlaceCircle(button, cx, cy, childSize);
            ArcCanvas.Children.Add(button);
            _circles.Add((button, entry, cx, cy));
        }

        double addX = centerX + points[^1].X;
        double addY = centerY + points[^1].Y;
        Button addButton = CreateAddButton(childSize);
        PlaceCircle(addButton, addX, addY, childSize);
        ArcCanvas.Children.Add(addButton);
        _circles.Add((addButton, null, addX, addY));

        PositionOverlayContent();

        foreach ((Button button, _, _, _) in _circles)
        {
            if (_expanded)
            {
                button.Visibility = Visibility.Visible;
                button.IsHitTestVisible = true;
                button.Opacity = 1.0;
            }
            else
            {
                button.Visibility = Visibility.Collapsed;
                button.IsHitTestVisible = false;
                button.Opacity = 1.0;
            }
        }
    }

    private double MaxLayoutRadius()
    {
        LauncherSettings settings = _state.Settings;
        double windowRadius = ArcLayoutCalculator.EnsureMinimumRadius(
            settings.MaxItems + 1,
            settings.ArcRadius,
            settings.ChildCircleSize,
            settings.Edge == EdgePosition.FreeFloating);
        (double width, _) = WindowLayoutCalculator.GetWindowSize(
            windowRadius, settings.ChildCircleSize, WindowMargin);
        return (width / 2.0) - (settings.ChildCircleSize / 2.0) - 4.0;
    }

    private IReadOnlyList<AppEntry> GetFilteredEntries()
    {
        string filter = _searchMode ? SearchBox.Text.Trim() : string.Empty;
        if (filter.Length == 0)
        {
            return _allEntries;
        }

        return _allEntries
            .Where(e => e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static void PlaceCircle(Button button, double centerX, double centerY, double size)
    {
        Canvas.SetLeft(button, centerX - (size / 2.0));
        Canvas.SetTop(button, centerY - (size / 2.0));
    }

    private Button CreateAppButton(AppEntry entry, double size)
    {
        var button = new Button
        {
            Width = size,
            Height = size,
            Style = (Style)RootGrid.Resources["CircleButtonStyle"],
            Tag = entry.Id,
            Content = BuildCircleContent(entry),
        };

        ToolTipService.SetToolTip(button, string.IsNullOrWhiteSpace(entry.Name) ? entry.TargetPath : entry.Name);

        button.Click += OnAppButtonClick;
        button.PointerEntered += OnCirclePointerEntered;
        button.PointerExited += OnCirclePointerExited;
        button.ContextFlyout = BuildEntryFlyout(entry);

        return button;
    }

    private Button CreateAddButton(double size)
    {
        var button = new Button
        {
            Width = size,
            Height = size,
            Style = (Style)RootGrid.Resources["SmallCircleButtonStyle"],
            Content = "+",
        };
        ToolTipService.SetToolTip(button, "Add an app (browse the Start Menu)");
        button.Click += async (_, _) => await ShowAddDialogAsync();
        button.PointerEntered += OnCirclePointerEntered;
        button.PointerExited += OnCirclePointerExited;
        return button;
    }

    private static UIElement BuildCircleContent(AppEntry entry)
    {
        var grid = new Grid();

        string? iconPath = IconService.GetIconPngPath(entry);
        string path = iconPath ?? IconService.DefaultLogoPath;

        try
        {
            if (File.Exists(path))
            {
                grid.Children.Add(new Image
                {
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Path.GetFullPath(path))),
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(8),
                });
            }
        }
        catch (Exception ex) when (ex is UriFormatException or NotSupportedException or ArgumentException)
        {
            CrashLog.Write(ex);
        }

        return grid;
    }

    // ------------------------------------------------------------------
    // Expand / collapse
    // ------------------------------------------------------------------

    private void Expand()
    {
        if (_expanded)
        {
            _pointerOutSince = DateTime.MinValue;
            return;
        }

        _expanded = true;
        _pointerOutSince = DateTime.MinValue;

        int index = 0;
        foreach ((Button button, _, _, _) in _circles)
        {
            button.Visibility = Visibility.Visible;
            button.IsHitTestVisible = true;
            button.Opacity = 1.0;
            SetCircleVisual(button, visibleState: true, animate: true, delayMs: index * 28);
            index++;
        }

        _pollTimer.Start();
    }

    private void Collapse(bool immediate = false)
    {
        if (!_expanded)
        {
            return;
        }

        ExitSearchMode();
        _expanded = false;
        _pollTimer.Stop();

        if (immediate)
        {
            foreach ((Button button, _, _, _) in _circles)
            {
                button.IsHitTestVisible = false;
                button.Visibility = Visibility.Collapsed;
                SetCircleVisual(button, visibleState: false, animate: false, delayMs: 0);
            }

            return;
        }

        var hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(170) };
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            if (_expanded)
            {
                return;
            }

            foreach ((Button button, _, _, _) in _circles)
            {
                button.Visibility = Visibility.Collapsed;
            }
        };
        hideTimer.Start();

        foreach ((Button button, _, _, _) in _circles)
        {
            button.IsHitTestVisible = false;
            SetCircleVisual(button, visibleState: false, animate: true, delayMs: 0);
        }
    }

    private void SetCircleVisual(Button button, bool visibleState, bool animate, int delayMs)
    {
        try
        {
            if (!animate)
            {
                button.Opacity = visibleState ? 1f : 0f;
                return;
            }

            Visual visual = ElementCompositionPreview.GetElementVisual(button);
            Compositor compositor = visual.Compositor;

            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            var fadeAnim = compositor.CreateScalarKeyFrameAnimation();

            if (visibleState)
            {
                scaleAnim.InsertKeyFrame(0f, new Vector3(0.4f, 0.4f, 1f));
                scaleAnim.InsertKeyFrame(1f, Vector3.One);
                scaleAnim.Duration = TimeSpan.FromMilliseconds(240);
                scaleAnim.DelayTime = TimeSpan.FromMilliseconds(delayMs);
                scaleAnim.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

                fadeAnim.InsertKeyFrame(0f, 0f);
                fadeAnim.InsertKeyFrame(1f, 1f);
                fadeAnim.Duration = TimeSpan.FromMilliseconds(200);
                fadeAnim.DelayTime = TimeSpan.FromMilliseconds(delayMs);
                fadeAnim.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            }
            else
            {
                scaleAnim.InsertKeyFrame(0f, visual.Scale);
                scaleAnim.InsertKeyFrame(1f, new Vector3(0.4f, 0.4f, 1f));
                scaleAnim.Duration = TimeSpan.FromMilliseconds(150);

                fadeAnim.InsertKeyFrame(0f, visual.Opacity);
                fadeAnim.InsertKeyFrame(1f, 0f);
                fadeAnim.Duration = TimeSpan.FromMilliseconds(140);
            }

            visual.StartAnimation("Scale", scaleAnim);
            visual.StartAnimation("Opacity", fadeAnim);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
        }
    }

    private void OnPollTick(object? sender, object e)
    {
        if (!_expanded || _dialogOpen || _dragging)
        {
            return;
        }

        if (_searchMode && SearchBox.FocusState != FocusState.Unfocused)
        {
            _pointerOutSince = DateTime.MinValue;
            return;
        }

        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
        {
            return;
        }

        if (IsCursorOverContent(cursor))
        {
            _pointerOutSince = DateTime.MinValue;
            return;
        }

        if (_pointerOutSince == DateTime.MinValue)
        {
            _pointerOutSince = DateTime.UtcNow;
            return;
        }

        if ((DateTime.UtcNow - _pointerOutSince).TotalMilliseconds >= _state.Settings.CollapseDelayMs)
        {
            _pointerOutSince = DateTime.MinValue;
            Collapse();
        }
    }

    private bool IsCursorOverContent(NativeMethods.POINT cursorPx)
    {
        double cursorXDip = (cursorPx.X - _windowOriginXPx) / _scale;
        double cursorYDip = (cursorPx.Y - _windowOriginYPx) / _scale;

        if (cursorXDip < -20 || cursorYDip < -20 ||
            cursorXDip > _windowWidthDip + 20 || cursorYDip > _windowHeightDip + 20)
        {
            return false;
        }

        LauncherSettings settings = _state.Settings;

        (double anchorXDip, double anchorYDip) = WindowLayoutCalculator.GetAnchorCenterInWindow(
            _windowWidthDip, _windowHeightDip);
        if (WindowLayoutCalculator.IsPointInsideCircle(
                cursorXDip, cursorYDip, anchorXDip, anchorYDip, settings.MainCircleSize + CircleHitPadding))
        {
            return true;
        }

        foreach ((_, _, double cx, double cy) in _circles)
        {
            if (WindowLayoutCalculator.IsPointInsideCircle(
                    cursorXDip, cursorYDip, cx, cy, settings.ChildCircleSize + CircleHitPadding))
            {
                return true;
            }
        }

        if (_searchMode)
        {
            double left = Canvas.GetLeft(SearchBox);
            double top = Canvas.GetTop(SearchBox);
            if (cursorXDip >= left - 6 && cursorXDip <= left + SearchBox.Width + 6 &&
                cursorYDip >= top - 6 && cursorYDip <= top + SearchBox.Height + 6)
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------
    // Pointer handlers
    // ------------------------------------------------------------------

    private void OnCirclePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOutSince = DateTime.MinValue;
        if (!_expanded)
        {
            Expand();
        }
    }

    private void OnCirclePointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_expanded && _pointerOutSince == DateTime.MinValue)
        {
            _pointerOutSince = DateTime.UtcNow;
        }
    }

    private void OnAnchorPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOutSince = DateTime.MinValue;
        if (!_expanded && !_dragging && !_dialogOpen)
        {
            Expand();
        }
    }

    private void OnAnchorPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(RootGrid);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (_state.Settings.Edge == EdgePosition.FreeFloating)
        {
            BeginDrag(e);
            e.Handled = true;
            return;
        }

        if (!_searchMode)
        {
            EnterSearchMode();
            e.Handled = true;
        }
    }

    private void BeginDrag(PointerRoutedEventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
        {
            return;
        }

        _dragging = true;
        _dragMoved = false;
        _dragStartCursorPx = new Point(cursor.X, cursor.Y);
        _dragStartWindowPx = (_windowOriginXPx, _windowOriginYPx);
        AnchorButton.CapturePointer(e.Pointer);
    }

    private void OnAnchorPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || _dragStartCursorPx is null)
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
        {
            return;
        }

        int deltaX = cursor.X - (int)_dragStartCursorPx.Value.X;
        int deltaY = cursor.Y - (int)_dragStartCursorPx.Value.Y;

        if (!_dragMoved && (Math.Abs(deltaX) > 4 || Math.Abs(deltaY) > 4))
        {
            _dragMoved = true;
        }

        if (!_dragMoved)
        {
            return;
        }

        int newX = _dragStartWindowPx.X + deltaX;
        int newY = _dragStartWindowPx.Y + deltaY;

        // Keep a corner of the anchor reachable inside the virtual desktop.
        int vsX = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int vsY = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int vsW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int vsH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        int winW = (int)(_windowWidthDip * _scale);
        int winH = (int)(_windowHeightDip * _scale);
        int margin = (int)(_state.Settings.MainCircleSize * _scale);

        newX = Math.Clamp(newX, vsX + margin - winW, vsX + vsW - margin);
        newY = Math.Clamp(newY, vsY + margin - winH, vsY + vsH - margin);

        WindowService.Move(_hwnd, newX, newY);
        _windowOriginXPx = newX;
        _windowOriginYPx = newY;
    }

    private void OnAnchorPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        AnchorButton.ReleasePointerCapture(e.Pointer);

        if (!_dragMoved)
        {
            // A clean click in floating mode still enters search mode.
            EnterSearchMode();
            return;
        }

        double floatingX = _windowOriginXPx / _scale;
        double floatingY = _windowOriginYPx / _scale;
        _state.UpdateSettings(s =>
        {
            s.FloatingX = floatingX;
            s.FloatingY = floatingY;
        });
    }

    // ------------------------------------------------------------------
    // Search
    // ------------------------------------------------------------------

    private void EnterSearchMode()
    {
        if (!_expanded)
        {
            Expand();
        }

        _searchMode = true;
        SearchBox.Visibility = Visibility.Visible;
        SearchBox.Text = string.Empty;
        AnchorButton.Opacity = 0.35;

        NativeMethods.SetForegroundWindow(_hwnd);
        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectAll();
    }

    private void ExitSearchMode()
    {
        if (!_searchMode)
        {
            return;
        }

        _searchMode = false;
        SearchBox.Visibility = Visibility.Collapsed;
        SearchBox.Text = string.Empty;
        AnchorButton.Opacity = 1.0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_searchMode)
        {
            return;
        }

        RebuildCircles();
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            Collapse();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Enter)
        {
            AppEntry? first = GetFilteredEntries().FirstOrDefault();
            if (first is not null)
            {
                LaunchEntry(first);
            }

            e.Handled = true;
        }
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen || _flyoutOpen)
        {
            return;
        }

        if (_searchMode && SearchBox.FocusState == FocusState.Unfocused)
        {
            ExitSearchMode();
        }
    }

    // ------------------------------------------------------------------
    // Launch, context menus, add dialog, settings
    // ------------------------------------------------------------------

    private void OnAppButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        AppEntry? entry = _allEntries.FirstOrDefault(a => a.Id == id);
        if (entry is not null)
        {
            LaunchEntry(entry);
        }
    }

    private void LaunchEntry(AppEntry entry)
    {
        LaunchResult result = LaunchService.Launch(entry.TargetPath, entry.Arguments, entry.WorkingDirectory);

        switch (result.Outcome)
        {
            case LaunchOutcome.Started:
                Collapse(immediate: true);
                break;

            case LaunchOutcome.TargetMissing:
                ShowStatus($"“{entry.Name}” was not found. Right-click it to update or remove.");
                CrashLog.Info($"Launch target missing: {entry.TargetPath}");
                break;

            case LaunchOutcome.TargetRejected:
                ShowStatus($"“{entry.Name}” has an unsafe target and was not started.");
                CrashLog.Info($"Launch target rejected: {entry.TargetPath}");
                break;

            default:
                ShowStatus($"Could not start “{entry.Name}”: {result.Error}");
                CrashLog.Info($"Launch failed: {result.Error}");
                break;
        }
    }

    private MenuFlyout BuildEntryFlyout(AppEntry entry)
    {
        var flyout = new MenuFlyout();
        flyout.Opened += (_, _) => _flyoutOpen = true;
        flyout.Closed += (_, _) => _flyoutOpen = false;

        var changeIcon = new MenuFlyoutItem { Text = "Change icon (PNG)…" };
        changeIcon.Click += async (_, _) => await PickCustomIconAsync(entry);

        var resetIcon = new MenuFlyoutItem { Text = "Reset to default icon" };
        resetIcon.IsEnabled = !string.IsNullOrEmpty(entry.CustomIconFileName);
        resetIcon.Click += (_, _) => ResetCustomIcon(entry);

        var openLocation = new MenuFlyoutItem { Text = "Open file location" };
        openLocation.Click += (_, _) => OpenFileLocation(entry);

        var remove = new MenuFlyoutItem { Text = "Remove from launcher" };
        remove.Click += (_, _) => RemoveEntry(entry);

        flyout.Items.Add(changeIcon);
        flyout.Items.Add(resetIcon);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(openLocation);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(remove);
        return flyout;
    }

    private async Task PickCustomIconAsync(AppEntry entry)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            AppEntry? live = _state.FindApp(entry.Id);
            if (live is null)
            {
                return;
            }

            IconService.SetCustomIcon(live, file.Path);
            _state.UpdateApp(live);
            RefreshAllEntries();
            RebuildCircles();
            ShowStatus("Icon updated.");
        }
        catch (InvalidDataException ex)
        {
            ShowStatus(ex.Message);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            ShowStatus("Could not set the icon.");
        }
    }

    private void ResetCustomIcon(AppEntry entry)
    {
        AppEntry? live = _state.FindApp(entry.Id);
        if (live is null)
        {
            return;
        }

        IconService.ClearCustomIcon(live);
        _state.UpdateApp(live);
        RefreshAllEntries();
        RebuildCircles();
        ShowStatus("Icon reset to the application's own logo.");
    }

    private void OpenFileLocation(AppEntry entry)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(entry.TargetPath) || !File.Exists(entry.TargetPath))
            {
                ShowStatus("The file location is no longer available.");
                return;
            }

            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true,
            };
            info.ArgumentList.Add("/select,");
            info.ArgumentList.Add(Path.GetFullPath(entry.TargetPath));
            System.Diagnostics.Process.Start(info);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
        }
    }

    private void RemoveEntry(AppEntry entry)
    {
        IconService.DeleteIconsFor(entry);
        _state.RemoveApp(entry.Id);
        RefreshAllEntries();
        RebuildCircles();
        ShowStatus($"“{entry.Name}” removed.");
    }

    private async Task ShowAddDialogAsync()
    {
        if (_dialogOpen)
        {
            return;
        }

        if (!_expanded)
        {
            Expand();
        }

        _dialogOpen = true;
        try
        {
            NativeMethods.SetForegroundWindow(_hwnd);
            var dialog = new AddAppDialog
            {
                XamlRoot = RootGrid.XamlRoot,
            };
            dialog.SeedExisting(_allEntries);

            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && dialog.SelectedApp is not null)
            {
                var entry = new AppEntry
                {
                    Name = dialog.SelectedApp.Name,
                    TargetPath = dialog.SelectedApp.TargetPath,
                    Arguments = dialog.SelectedApp.Arguments,
                    WorkingDirectory = dialog.SelectedApp.WorkingDirectory,
                };
                if (_state.AddApp(entry))
                {
                    RefreshAllEntries();
                    RebuildCircles();
                    ShowStatus($"“{entry.Name}” added.");
                }
                else
                {
                    ShowStatus($"“{entry.Name}” is already in the launcher.");
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            ShowStatus("Could not open the app picker.");
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    public void ShowSettingsWindow()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_settingsWindow is null)
            {
                _settingsWindow = new SettingsWindow();
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                _settingsWindow.Activate();
            }
            else
            {
                IntPtr settingsHwnd = WindowService.GetHwnd(_settingsWindow);
                if (_settingsWindow.AppWindow.Presenter is OverlappedPresenter presenter &&
                    presenter.State == OverlappedPresenterState.Minimized)
                {
                    presenter.Restore();
                }

                NativeMethods.SetForegroundWindow(settingsHwnd);
            }
        });
    }

    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(AppState.AppDataDirectory);
            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true,
            };
            info.ArgumentList.Add(AppState.AppDataDirectory);
            System.Diagnostics.Process.Start(info);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
        }
    }

    // ------------------------------------------------------------------
    // State plumbing
    // ------------------------------------------------------------------

    private void RefreshAllEntries()
    {
        _allEntries.Clear();
        _allEntries.AddRange(_state.Apps);
    }

    private void OnSettingsOrAppsChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            RefreshAllEntries();
            ApplyWindowGeometry();
            RebuildCircles();
            UpdateAnchorVisual();
        });
    }

    private void UpdateAnchorVisual()
    {
        string logoPath = IconService.DefaultLogoPath;
        if (File.Exists(logoPath))
        {
            AnchorImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Path.GetFullPath(logoPath)));
        }

        LauncherSettings settings = _state.Settings;
        ToolTipService.SetToolTip(
            AnchorButton,
            settings.Edge == EdgePosition.FreeFloating
                ? "CircleQuickLauncher — hover to open, click to search, drag to move"
                : "CircleQuickLauncher — hover to open, click to search");
    }

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_expanded || StatusText.Text == message)
            {
                StatusText.Visibility = Visibility.Collapsed;
            }
        };
        timer.Start();
    }

    private void OnWindowClosed(object? sender, WindowEventArgs args)
    {
        _pollTimer.Stop();
        _topmostTimer.Stop();
        _tray.Dispose();
        _state.SettingsChanged -= OnSettingsOrAppsChanged;
        _state.AppsChanged -= OnSettingsOrAppsChanged;

        if (_originalWndProc != IntPtr.Zero && _wndProcDelegate is not null)
        {
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWLP_WNDPROC, _originalWndProc);
            _wndProcDelegate = null;
        }
    }
}
