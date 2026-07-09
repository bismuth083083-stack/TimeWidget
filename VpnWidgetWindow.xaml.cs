using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using TimeWidget.Models;
using TimeWidget.Services;

namespace TimeWidget;

public partial class VpnWidgetWindow : Window
{
    private WidgetSettings _settings = new();
    private VpnShortcutSettings _shortcutSettings = new();
    private bool _isLoaded;

    public VpnWidgetWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();
        _shortcutSettings = VpnShortcutStore.Load();

        Topmost = _settings.VpnTopmost;
        TopmostMenuItem.IsChecked = _settings.VpnTopmost;
        LockMenuItem.IsChecked = _settings.VpnIsLocked;
        ResizeMenuItem.IsChecked = _settings.VpnIsResizable;
        ApplyResizeMode();

        if (_settings.VpnWidth.HasValue && _settings.VpnHeight.HasValue)
        {
            Width = _settings.VpnWidth.Value;
            Height = _settings.VpnHeight.Value;
        }

        RestorePosition();
        RefreshProxyStatus();
        _isLoaded = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveSettings();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.VpnIsLocked)
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            WindowSnapService.SnapToScreen(this);
            SaveSettings();
        }
    }

    private void TopmostMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.VpnTopmost = TopmostMenuItem.IsChecked;
        Topmost = _settings.VpnTopmost;
        SaveSettings();
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.VpnIsLocked = LockMenuItem.IsChecked;
        SaveSettings();
    }

    private void ResizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.VpnIsResizable = ResizeMenuItem.IsChecked;
        ApplyResizeMode();
        SaveSettings();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isLoaded || !_settings.VpnIsResizable)
        {
            return;
        }

        WindowSnapService.SnapResize(this);
        SaveSettings();
    }

    private void OpenSharkcloudButton_Click(object sender, RoutedEventArgs e)
    {
        LaunchConfiguredApp("Sharkcloud App", () => _shortcutSettings.SharkcloudPath, value => _shortcutSettings.SharkcloudPath = value);
    }

    private void OpenFlClashButton_Click(object sender, RoutedEventArgs e)
    {
        LaunchConfiguredApp("FLClash", () => _shortcutSettings.FlClashPath, value => _shortcutSettings.FlClashPath = value);
    }

    private void OpenProxySettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:network-proxy",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open proxy settings: {ex}");
            MessageBox.Show(this, "Unable to open system proxy settings.", "VPN Shortcuts", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void RefreshMenuItem_Click(object sender, RoutedEventArgs e)
    {
        RefreshProxyStatus();
    }

    private void ChooseSharkcloudMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ChooseAndSaveAppPath("Choose Sharkcloud App", value => _shortcutSettings.SharkcloudPath = value);
    }

    private void ChooseFlClashMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ChooseAndSaveAppPath("Choose FLClash", value => _shortcutSettings.FlClashPath = value);
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void LaunchConfiguredApp(string appName, Func<string?> getPath, Action<string> setPath)
    {
        string? path = getPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            path = ChooseAppPath($"Choose {appName}");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            setPath(path);
            VpnShortcutStore.Save(_shortcutSettings);
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path)
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to launch {appName}: {ex}");
            MessageBox.Show(this, $"Unable to open {appName}. Please choose the exe file again.", "VPN Shortcuts", MessageBoxButton.OK, MessageBoxImage.Information);
            setPath(string.Empty);
            VpnShortcutStore.Save(_shortcutSettings);
        }
    }

    private void ChooseAndSaveAppPath(string title, Action<string> setPath)
    {
        string? path = ChooseAppPath(title);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        setPath(path);
        VpnShortcutStore.Save(_shortcutSettings);
    }

    private string? ChooseAppPath(string title)
    {
        OpenFileDialog dialog = new()
        {
            Title = title,
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void RefreshProxyStatus()
    {
        ProxyStatusInfo status = ProxyStatusService.GetStatus();
        StatusDotText.Text = status.IsEnabled ? "Proxy on" : "Proxy off";
        StatusDotText.Foreground = status.IsEnabled
            ? (System.Windows.Media.Brush)Resources["AccentText"]
            : (System.Windows.Media.Brush)Resources["MutedText"];
        HttpProxyText.Text = $"HTTP: {status.HttpProxy}";
        SocksProxyText.Text = $"SOCKS: {status.SocksProxy}";
    }

    private void RestorePosition()
    {
        if (_settings.VpnLeft.HasValue && _settings.VpnTop.HasValue)
        {
            Left = _settings.VpnLeft.Value;
            Top = _settings.VpnTop.Value;
            WindowSnapService.KeepWindowOnScreen(this);
        }
    }

    private void SaveSettings()
    {
        SettingsStore.Update(settings =>
        {
            settings.VpnLeft = Left;
            settings.VpnTop = Top;
            settings.VpnWidth = Width;
            settings.VpnHeight = Height;
            settings.VpnTopmost = Topmost;
            settings.VpnIsLocked = LockMenuItem.IsChecked;
            settings.VpnIsResizable = ResizeMenuItem.IsChecked;
        });
    }

    private void ApplyResizeMode()
    {
        MinWidth = 320;
        MinHeight = 200;
        ResizeMode = _settings.VpnIsResizable ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
    }
}
