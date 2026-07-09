using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TimeWidget.Models;
using TimeWidget.Services;
using Forms = System.Windows.Forms;

namespace TimeWidget;

public partial class SettingsWidgetWindow : Window
{
    private readonly DispatcherTimer _brightnessDebounceTimer;
    private readonly DispatcherTimer _volumeDebounceTimer;
    private WidgetSettings _settings = new();
    private bool _isLoaded;
    private bool _brightnessSupported;
    private bool _suppressBrightnessChange;
    private bool _volumeSupported;
    private bool _suppressVolumeChange;

    public SettingsWidgetWindow()
    {
        InitializeComponent();
        _brightnessDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _brightnessDebounceTimer.Tick += async (_, _) =>
        {
            _brightnessDebounceTimer.Stop();
            await ApplyBrightnessAsync();
        };
        _volumeDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _volumeDebounceTimer.Tick += async (_, _) =>
        {
            _volumeDebounceTimer.Stop();
            await ApplyVolumeAsync();
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();
        Topmost = _settings.QuickSettingsTopmost;
        TopmostMenuItem.IsChecked = _settings.QuickSettingsTopmost;
        LockMenuItem.IsChecked = _settings.QuickSettingsIsLocked;
        ResizeMenuItem.IsChecked = _settings.QuickSettingsIsResizable;
        ApplyResizeMode();

        if (_settings.QuickSettingsWidth.HasValue && _settings.QuickSettingsHeight.HasValue)
        {
            Width = _settings.QuickSettingsWidth.Value;
            Height = _settings.QuickSettingsHeight.Value;
        }

        RestorePosition();
        BuildButtons();
        UpdateAirplaneState();
        UpdateBatteryText();
        _isLoaded = true;
        _ = RefreshSystemStatusAsync();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _brightnessDebounceTimer.Stop();
        _volumeDebounceTimer.Stop();
        SaveSettings();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.QuickSettingsIsLocked)
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
        _settings.QuickSettingsTopmost = TopmostMenuItem.IsChecked;
        Topmost = _settings.QuickSettingsTopmost;
        SaveSettings();
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.QuickSettingsIsLocked = LockMenuItem.IsChecked;
        SaveSettings();
    }

    private void ResizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.QuickSettingsIsResizable = ResizeMenuItem.IsChecked;
        ApplyResizeMode();
        SaveSettings();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isLoaded || !_settings.QuickSettingsIsResizable)
        {
            return;
        }

        WindowSnapService.SnapResize(this);
        SaveSettings();
    }

    private void WifiButton_Click(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:network-wifi");
    private void BluetoothButton_Click(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:bluetooth");
    private void AccessibilityButton_Click(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:easeofaccess");
    private void BatterySaverButton_Click(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:powersleep");
    private void LiveCaptionsButton_Click(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:easeofaccess-closedcaptioning");
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:");
    private void OpenSettingsMenuItem_Click(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:");
    private void MoreButton_Click(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:");

    private void AirplaneButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.QuickSettingsAirplaneMode = !_settings.QuickSettingsAirplaneMode;
        UpdateAirplaneState();
        SaveSettings();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BuildButtons()
    {
        SetButtonContent(WifiButton, "\uE701", "Wi-Fi", true, "ms-settings:network-wifi");
        SetButtonContent(BluetoothButton, "\uE702", "Bluetooth", true, "ms-settings:bluetooth");
        SetButtonContent(AirplaneButton, "\uE709", "Airplane", false, null);
        SetButtonContent(AccessibilityButton, "\uE776", "Access", true, "ms-settings:easeofaccess");
        SetButtonContent(BatterySaverButton, "\uEBAA", "Saver", true, "ms-settings:powersleep");
        SetButtonContent(LiveCaptionsButton, "\uE7F0", "Captions", true, "ms-settings:easeofaccess-closedcaptioning");
    }

    private void SetButtonContent(Button button, string icon, string title, bool showArrow, string? arrowUri)
    {
        Grid grid = new();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBlock iconText = new()
        {
            Text = icon,
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 16,
            FontWeight = FontWeights.Normal,
            Foreground = (System.Windows.Media.Brush)Resources["PrimaryText"]
        };
        Grid.SetRow(iconText, 0);
        grid.Children.Add(iconText);

        if (showArrow)
        {
            TextBlock arrowText = new()
            {
                Text = "\uE76C",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Foreground = (System.Windows.Media.Brush)Resources["MutedText"],
                VerticalAlignment = VerticalAlignment.Center
            };

            if (!string.IsNullOrWhiteSpace(arrowUri))
            {
                arrowText.Cursor = Cursors.Hand;
                arrowText.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    OpenSettings(arrowUri);
                };
            }

            Grid.SetColumn(arrowText, 1);
            grid.Children.Add(arrowText);
        }

        TextBlock titleText = new()
        {
            Text = title,
            FontFamily = new System.Windows.Media.FontFamily("MiSans"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = (System.Windows.Media.Brush)Resources["PrimaryText"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetRow(titleText, 1);
        Grid.SetColumnSpan(titleText, 2);
        grid.Children.Add(titleText);

        button.Content = grid;
    }

    private void UpdateAirplaneState()
    {
        AirplaneButton.Tag = _settings.QuickSettingsAirplaneMode ? "On" : null;
    }

    private async Task RefreshSystemStatusAsync()
    {
        Task<WifiStatusInfo> wifiTask = SystemControlService.GetWifiStatusAsync();
        Task<BluetoothStatusInfo> bluetoothTask = SystemControlService.GetBluetoothStatusAsync();
        Task<BrightnessStatusInfo> brightnessTask = SystemControlService.GetBrightnessAsync();
        Task<VolumeStatusInfo> volumeTask = SystemControlService.GetVolumeAsync();

        WifiStatusInfo wifi = await wifiTask;
        WifiButton.Tag = wifi.IsConnected ? "On" : null;
        SetButtonContent(WifiButton, "\uE701", string.IsNullOrWhiteSpace(wifi.DisplayName) ? "Wi-Fi" : wifi.DisplayName, true, "ms-settings:network-wifi");

        BluetoothStatusInfo bluetooth = await bluetoothTask;
        BluetoothButton.Tag = bluetooth.IsEnabled ? "On" : null;
        SetButtonContent(BluetoothButton, "\uE702", bluetooth.IsEnabled ? "Bluetooth On" : "Bluetooth", true, "ms-settings:bluetooth");

        BrightnessStatusInfo brightness = await brightnessTask;
        _brightnessSupported = brightness.IsSupported && brightness.Value.HasValue;
        _suppressBrightnessChange = true;
        if (brightness.Value.HasValue)
        {
            BrightnessSlider.Value = brightness.Value.Value;
            SetBrightnessText($"{brightness.Value.Value}%");
            SetBrightnessStatus(string.Empty);
        }
        else
        {
            SetBrightnessText("--%");
            SetBrightnessStatus(brightness.Message);
        }

        BrightnessSlider.IsEnabled = _brightnessSupported;
        _suppressBrightnessChange = false;

        VolumeStatusInfo volume = await volumeTask;
        _volumeSupported = volume.IsSupported && volume.Value.HasValue;
        _suppressVolumeChange = true;
        if (volume.Value.HasValue)
        {
            VolumeSlider.Value = volume.Value.Value;
            SetVolumeText($"{volume.Value.Value}%");
            SetVolumeStatus(string.Empty);
        }
        else
        {
            SetVolumeText("--%");
            SetVolumeStatus(volume.Message);
        }

        VolumeSlider.IsEnabled = _volumeSupported;
        _suppressVolumeChange = false;
    }

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BrightnessSlider is null)
        {
            return;
        }

        int value = (int)Math.Round(BrightnessSlider.Value);
        SetBrightnessText($"{value}%");
        if (!_isLoaded || _suppressBrightnessChange || !_brightnessSupported)
        {
            return;
        }

        _brightnessDebounceTimer.Stop();
        _brightnessDebounceTimer.Start();
    }

    private async void BrightnessSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_brightnessSupported)
        {
            return;
        }

        _brightnessDebounceTimer.Stop();
        await ApplyBrightnessAsync();
    }

    private async Task ApplyBrightnessAsync()
    {
        if (!_brightnessSupported)
        {
            return;
        }

        int value = (int)Math.Round(BrightnessSlider.Value);
        bool success = await SystemControlService.SetBrightnessAsync(value);
        SetBrightnessStatus(success ? string.Empty : "当前设备不支持亮度调节");
        if (!success)
        {
            _brightnessSupported = false;
            BrightnessSlider.IsEnabled = false;
        }
    }

    private void SetBrightnessText(string text)
    {
        if (BrightnessValueText is not null)
        {
            BrightnessValueText.Text = text;
        }
    }

    private void SetBrightnessStatus(string text)
    {
        if (BrightnessStatusText is not null)
        {
            BrightnessStatusText.Text = text;
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeSlider is null)
        {
            return;
        }

        int value = (int)Math.Round(VolumeSlider.Value);
        SetVolumeText($"{value}%");
        if (!_isLoaded || _suppressVolumeChange || !_volumeSupported)
        {
            return;
        }

        _volumeDebounceTimer.Stop();
        _volumeDebounceTimer.Start();
    }

    private async void VolumeSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_volumeSupported)
        {
            return;
        }

        _volumeDebounceTimer.Stop();
        await ApplyVolumeAsync();
    }

    private async Task ApplyVolumeAsync()
    {
        if (!_volumeSupported)
        {
            return;
        }

        int value = (int)Math.Round(VolumeSlider.Value);
        bool success = await SystemControlService.SetVolumeAsync(value);
        SetVolumeStatus(success ? string.Empty : "未找到可用音频输出设备");
        if (!success)
        {
            _volumeSupported = false;
            VolumeSlider.IsEnabled = false;
        }
    }

    private void SetVolumeText(string text)
    {
        if (VolumeValueText is not null)
        {
            VolumeValueText.Text = text;
        }
    }

    private void SetVolumeStatus(string text)
    {
        if (VolumeStatusText is not null)
        {
            VolumeStatusText.Text = text;
        }
    }

    private void UpdateBatteryText()
    {
        try
        {
            Forms.PowerStatus status = Forms.SystemInformation.PowerStatus;
            int percent = (int)Math.Round(status.BatteryLifePercent * 100);
            BatteryText.Text = status.PowerLineStatus == Forms.PowerLineStatus.Online
                ? $"Battery  {percent}% · plugged in"
                : $"Battery  {percent}%";
        }
        catch
        {
            BatteryText.Text = "Battery  --%";
        }
    }

    private void OpenSettings(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open settings URI {uri}: {ex}");
            MessageBox.Show(this, "Unable to open this Windows settings page.", "Quick Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void RestorePosition()
    {
        if (_settings.QuickSettingsLeft.HasValue && _settings.QuickSettingsTop.HasValue)
        {
            Left = _settings.QuickSettingsLeft.Value;
            Top = _settings.QuickSettingsTop.Value;
            WindowSnapService.KeepWindowOnScreen(this);
        }
    }

    private void SaveSettings()
    {
        SettingsStore.Update(settings =>
        {
            settings.QuickSettingsLeft = Left;
            settings.QuickSettingsTop = Top;
            settings.QuickSettingsWidth = Width;
            settings.QuickSettingsHeight = Height;
            settings.QuickSettingsTopmost = Topmost;
            settings.QuickSettingsIsLocked = LockMenuItem.IsChecked;
            settings.QuickSettingsIsResizable = ResizeMenuItem.IsChecked;
            settings.QuickSettingsAirplaneMode = _settings.QuickSettingsAirplaneMode;
        });
    }

    private void ApplyResizeMode()
    {
        MinWidth = 340;
        MinHeight = 390;
        ResizeMode = _settings.QuickSettingsIsResizable ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
    }
}
