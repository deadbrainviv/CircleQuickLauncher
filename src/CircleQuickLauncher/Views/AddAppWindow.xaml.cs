using CircleQuickLauncher.Core.Models;
using CircleQuickLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CircleQuickLauncher.Views;

public sealed partial class AddAppWindow : Window
{
    private List<StartMenuAppInfo> _allApps = new();
    private HashSet<string> _existingKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    /// <summary>The app the user confirmed, or null if the window was closed/cancelled.</summary>
    public StartMenuAppInfo? SelectedApp { get; private set; }

    /// <summary>Completes when the window closes, carrying the confirmed selection (if any).</summary>
    public Task<StartMenuAppInfo?> WaitForSelectionAsync() => _selectionTcs.Task;

    private readonly TaskCompletionSource<StartMenuAppInfo?> _selectionTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AddAppWindow()
    {
        InitializeComponent();

        try
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(560, 700));
            WindowService.CenterOnScreen(this, 560, 700);
        }
        catch (Exception)
        {
        }

        Closed += OnClosed;
        if (Content is FrameworkElement root)
        {
            root.Loaded += OnLoaded;
        }
    }

    public void SeedExisting(IEnumerable<AppEntry> existing)
    {
        _existingKeys = existing
            .Select(a => a.TargetPath + "|" + a.Arguments)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _selectionTcs.TrySetResult(SelectedApp);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        LoadingRing.IsActive = true;
        AppList.Visibility = Visibility.Collapsed;

        try
        {
            // Enumerating and resolving hundreds of shortcuts can take a moment.
            _allApps = await Task.Run(() => StartMenuCatalogService.GetStartMenuApps().ToList());
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            _allApps = new List<StartMenuAppInfo>();
        }

        LoadingRing.IsActive = false;
        ApplyFilter();
        FilterBox.Focus(FocusState.Programmatic);
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loaded)
        {
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        string filter = FilterBox.Text.Trim();

        IEnumerable<StartMenuAppInfo> query = _allApps;
        if (filter.Length > 0)
        {
            query = query.Where(a => a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        List<StartMenuAppInfo> list = query.Take(300).ToList();

        AppList.ItemsSource = list;
        AppList.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = list.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        AddButton.IsEnabled = false;
        SelectedApp = null;
        SelectionText.Text = string.Empty;
    }

    private void AppList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is StartMenuAppInfo info)
        {
            SelectApp(info);
        }
    }

    private void AppList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AppList.SelectedItem is StartMenuAppInfo info)
        {
            SelectApp(info);
        }
    }

    private void SelectApp(StartMenuAppInfo info)
    {
        SelectedApp = info;

        bool alreadyAdded = _existingKeys.Contains(info.TargetPath + "|" + info.Arguments);
        AddButton.IsEnabled = !alreadyAdded;
        SelectionText.Text = alreadyAdded
            ? "Already in the launcher."
            : $"Add “{info.Name}” to the launcher?";
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedApp is null)
        {
            return;
        }

        _selectionTcs.TrySetResult(SelectedApp);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _selectionTcs.TrySetResult(null);
        Close();
    }
}
