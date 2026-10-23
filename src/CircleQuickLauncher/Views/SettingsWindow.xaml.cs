using CircleQuickLauncher.Core.Models;
using CircleQuickLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace CircleQuickLauncher.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly AppState _state = AppState.Instance;
    private bool _loading = true;
    private bool _autorunTouched;

    public SettingsWindow()
    {
        InitializeComponent();

        // Always open centered on the primary monitor so it is visible regardless of
        // where the launcher anchor lives.
        try
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(720, 860));
            WindowService.CenterOnScreen(this, 720, 860);
        }
        catch (Exception)
        {
        }

        LoadFromState();

        _state.SettingsChanged += OnSettingsChanged;
        Closed += (_, _) => _state.SettingsChanged -= OnSettingsChanged;
    }

    private void LoadFromState()
    {
        LauncherSettings s = _state.Settings;

        foreach (ComboBoxItem item in EdgeCombo.Items)
        {
            if (item.Tag is string tag && tag == s.Edge.ToString())
            {
                EdgeCombo.SelectedItem = item;
                break;
            }
        }

        PeekSlider.Value = Math.Round(s.PeekFraction * 100);
        PeekSlider.Header = $"Peek: {Math.Round(s.PeekFraction * 100)}%";
        RadiusSlider.Value = s.ArcRadius;
        MaxItemsSlider.Value = s.MaxItems;
        AnchorSizeSlider.Value = s.MainCircleSize;
        ChildSizeSlider.Value = s.ChildCircleSize;
        IconSizeSlider.Value = s.IconSize;
        CollapseDelaySlider.Value = s.CollapseDelayMs;
        DataPathText.Text = AppState.AppDataDirectory;

        try
        {
            _loading = false;
            AutorunToggle.IsOn = AutorunService.IsEnabled();
        }
        catch (Exception)
        {
            _loading = false;
            AutorunToggle.IsEnabled = false;
            AutorunToggle.Header = "Start with Windows (unavailable)";
        }

        VersionText.Text = "Version " +
            (typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0") +
            "  ·  Windows 10 1809+ / Windows 11  ·  x64";
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_state.Settings.Edge.ToString() is var edgeName &&
                (EdgeCombo.SelectedItem as ComboBoxItem)?.Tag as string != edgeName)
            {
                _loading = true;
                LoadFromState();
                _loading = false;
            }
        });
    }

    private void EdgeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || (EdgeCombo.SelectedItem as ComboBoxItem)?.Tag is not string tag)
        {
            return;
        }

        if (Enum.TryParse<EdgePosition>(tag, out EdgePosition edge))
        {
            _state.UpdateSettings(s => s.Edge = edge);
        }
    }

    private void PeekSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _state.UpdateSettings(s => s.PeekFraction = e.NewValue / 100.0);
        PeekSlider.Header = $"Peek: {Math.Round(e.NewValue)}%";
    }

    private void RadiusSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _state.UpdateSettings(s => s.ArcRadius = e.NewValue);
    }

    private void MaxItemsSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _state.UpdateSettings(s => s.MaxItems = (int)e.NewValue);
    }

    private void AnchorSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _state.UpdateSettings(s => s.MainCircleSize = e.NewValue);
    }

    private void ChildSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _state.UpdateSettings(s => s.ChildCircleSize = e.NewValue);
    }

    private void IconSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _state.UpdateSettings(s => s.IconSize = e.NewValue);
    }

    private void CollapseDelaySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _state.UpdateSettings(s => s.CollapseDelayMs = (int)e.NewValue);
    }

    private async void AutorunToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (_autorunTouched)
        {
            return;
        }

        _autorunTouched = true;
        bool desired = AutorunToggle.IsOn;

        try
        {
            await Task.Run(() => AutorunService.SetEnabled(desired));
            _state.UpdateSettings(s => s.StartWithWindows = desired);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            // Roll the switch back to reality.
            _loading = true;
            try
            {
                AutorunToggle.IsOn = AutorunService.IsEnabled();
            }
            catch (Exception)
            {
            }

            _loading = false;

            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Could not change startup",
                Content = ex.Message,
                CloseButtonText = "OK",
            };
            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception)
            {
            }
        }
        finally
        {
            _autorunTouched = false;
        }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
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

    private void ResetPosition_Click(object sender, RoutedEventArgs e)
    {
        _state.UpdateSettings(s =>
        {
            s.FloatingX = 120;
            s.FloatingY = 240;
        });
    }
}
