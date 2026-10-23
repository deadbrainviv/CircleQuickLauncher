using CircleQuickLauncher.Core.Models;
using CircleQuickLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CircleQuickLauncher.Views;

public sealed partial class AddAppDialog : ContentDialog
{
    private List<StartMenuAppInfo> _allApps = new();
    private HashSet<string> _existingKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public AddAppDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public StartMenuAppInfo? SelectedApp { get; private set; }

    public void SeedExisting(IEnumerable<AppEntry> existing)
    {
        _existingKeys = existing
            .Select(a => a.TargetPath + "|" + a.Arguments)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
        IsPrimaryButtonEnabled = false;
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
        IsPrimaryButtonEnabled = !alreadyAdded;
        SelectionText.Text = alreadyAdded
            ? "Already in the launcher."
            : $"Add “{info.Name}” to the launcher?";
    }
}
