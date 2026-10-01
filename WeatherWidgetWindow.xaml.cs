using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using TimeWidget.Models;
using TimeWidget.Services;

namespace TimeWidget;

public partial class WeatherWidgetWindow : Window
{
    private static readonly TimeSpan AutoRefreshInterval = ChinaWeatherAlertService.RefreshInterval;
    private static readonly TimeSpan ManualRefreshCooldown = TimeSpan.FromSeconds(30);

    private readonly DispatcherTimer _refreshTimer;
    private readonly WeatherService _weatherService = new();
    private readonly LocationService _locationService = new();
    private readonly CancellationTokenSource _refreshCancellation = new();
    private WidgetSettings _settings = new();
    private WeatherAlertInfo _currentAlert = new();
    private List<WeatherAlertInfo> _currentAlerts = [];
    private WeatherCityInfo _currentCity = ChinaWeatherCityCatalog.DefaultCity;
    private DateTimeOffset _lastManualRefresh = DateTimeOffset.MinValue;
    private bool _isRefreshing;
    private bool _isClosed;
    private bool _isLoaded;

    public WeatherWidgetWindow()
    {
        InitializeComponent();

        _refreshTimer = new DispatcherTimer
        {
            Interval = AutoRefreshInterval
        };
        _refreshTimer.Tick += async (_, _) => await RefreshWeatherAsync(forceRefresh: false, isManual: false);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();
        Topmost = _settings.WeatherTopmost;
        TopmostMenuItem.IsChecked = _settings.WeatherTopmost;
        LockMenuItem.IsChecked = _settings.WeatherIsLocked;
        ResizeMenuItem.IsChecked = _settings.WeatherIsResizable;
        ApplyResizeMode();
        if (_settings.WeatherWidth.HasValue && _settings.WeatherHeight.HasValue)
        {
            Width = _settings.WeatherWidth.Value;
            Height = _settings.WeatherHeight.Value;
        }
        RestorePosition();

        _currentCity = ChinaWeatherCityCatalog.FindByCode(_settings.WeatherCityCode);
        await LoadInitialWeatherAsync();
        if (_isClosed) return;
        _refreshTimer.Start();
        _isLoaded = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _isClosed = true;
        _refreshTimer.Stop();
        _refreshCancellation.Cancel();
        _refreshCancellation.Dispose();
        SaveSettings();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.WeatherIsLocked)
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
        _settings.WeatherTopmost = TopmostMenuItem.IsChecked;
        Topmost = _settings.WeatherTopmost;
        SaveSettings();
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.WeatherIsLocked = LockMenuItem.IsChecked;
        SaveSettings();
    }

    private void ResizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.WeatherIsResizable = ResizeMenuItem.IsChecked;
        ApplyResizeMode();
        SaveSettings();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isLoaded || !_settings.WeatherIsResizable)
        {
            return;
        }

        WindowSnapService.SnapResize(this);
        SaveSettings();
    }

    private async void RefreshDisplayMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RefreshWeatherAsync(forceRefresh: true, isManual: true);
    }

    private async void ChooseCityMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not string cityCode)
        {
            return;
        }

        _currentCity = ChinaWeatherCityCatalog.FindByCode(cityCode);
        _settings.WeatherCityCode = _currentCity.Code;
        _settings.WeatherCityName = _currentCity.Name;
        SaveSettings();
        await RefreshWeatherAsync(forceRefresh: true, isManual: false);
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async Task LoadInitialWeatherAsync()
    {
        WeatherSnapshot? cached = await _weatherService.TryLoadCacheAsync();
        if (_isClosed) return;
        if (cached is not null && cached.CityCode == _currentCity.Code)
        {
            ApplySnapshot(cached);
            if (!string.IsNullOrWhiteSpace(cached.CityCode))
            {
                _currentCity = ChinaWeatherCityCatalog.FindByCode(cached.CityCode);
            }
        }
        else
        {
            ShowStatus("Loading weather...");
        }

        if (string.IsNullOrWhiteSpace(_settings.WeatherCityCode) && !_settings.WeatherLocationDenied)
        {
            await TryUseCurrentLocationAsync();
        }
        else
        {
            await RefreshWeatherAsync(forceRefresh: false, isManual: false);
        }
    }

    private async Task TryUseCurrentLocationAsync()
    {
        ShowStatus("Requesting location permission...");

        LocationResult location = await _locationService.GetCurrentLocationAsync();
        if (_isClosed) return;
        if (!location.IsAllowed)
        {
            _settings.WeatherLocationDenied = true;
            SaveSettings();
            LocationSettingsButton.Visibility = Visibility.Visible;
            ShowStatus(location.Message ?? "Location permission is off. You can choose a city manually.");
            await RefreshWeatherAsync(forceRefresh: false, isManual: false);
            return;
        }

        LocationSettingsButton.Visibility = Visibility.Collapsed;
        _currentCity = ChinaWeatherCityCatalog.FindNearest(location.Latitude, location.Longitude);
        _settings.WeatherCityCode = _currentCity.Code;
        _settings.WeatherCityName = _currentCity.Name;
        _settings.WeatherLocationDenied = false;
        SaveSettings();
        await RefreshWeatherAsync(forceRefresh: true, isManual: false);
    }

    private async Task RefreshWeatherAsync(bool forceRefresh, bool isManual)
    {
        if (_isClosed || _isRefreshing)
        {
            return;
        }

        if (isManual)
        {
            TimeSpan sinceLastRefresh = DateTimeOffset.Now - _lastManualRefresh;
            if (sinceLastRefresh < ManualRefreshCooldown)
            {
                int remainingSeconds = (int)Math.Ceiling((ManualRefreshCooldown - sinceLastRefresh).TotalSeconds);
                ShowStatus($"Refresh available in {remainingSeconds}s.");
                return;
            }

            _lastManualRefresh = DateTimeOffset.Now;
        }

        _isRefreshing = true;
        ShowStatus("Updating weather...");
        WeatherCityInfo requestedCity = _currentCity;

        try
        {
            WeatherSnapshot snapshot = await _weatherService.GetWeatherAsync(requestedCity, forceRefresh, _refreshCancellation.Token);
            if (!_isClosed && requestedCity.Code == _currentCity.Code)
            {
                ApplySnapshot(snapshot);
            }
        }
        catch (OperationCanceledException) when (_isClosed) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"Weather refresh failed: {ex}");
            if (!_isClosed) ShowStatus("Unable to update weather.");
        }
        finally
        {
            _isRefreshing = false;
            if (!_isClosed && requestedCity.Code != _currentCity.Code)
            {
                await RefreshWeatherAsync(forceRefresh: true, isManual: false);
            }
        }
    }

    private void ApplySnapshot(WeatherSnapshot snapshot)
    {
        _currentAlerts = snapshot.Alerts.Count > 0
            ? snapshot.Alerts
            : snapshot.Alert.Level != AlertLevel.None
                ? [snapshot.Alert]
                : [];
        _currentAlert = _currentAlerts.FirstOrDefault() ?? snapshot.Alert;
        _currentCity = ChinaWeatherCityCatalog.FindByCode(snapshot.CityCode);
        _settings.WeatherCityCode = snapshot.CityCode;
        _settings.WeatherCityName = snapshot.CityName;
        SaveSettings();

        CityText.Text = snapshot.CityName;
        UpdatedText.Text = $"Updated {snapshot.UpdatedAt.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture)}";
        DaysItemsControl.ItemsSource = snapshot.Days;
        ApplyAlerts(_currentAlerts, _currentAlert);
        if (_currentAlerts.Count == 0 && !snapshot.AlertsAreCurrent)
        {
            AlertTypesText.Text = "Alerts unavailable";
        }
        string alertChecked = snapshot.AlertsUpdatedAt is { } alertTime
            ? $"Alerts checked {alertTime.LocalDateTime:MM/dd HH:mm}"
            : "Alerts not checked yet";
        AlertBar.ToolTip = $"{alertChecked}\n{snapshot.AlertStatusMessage}\n{AlertBar.ToolTip}";
        AlertBar.Opacity = snapshot.AlertsAreCurrent ? 1 : 0.65;

        if (snapshot.Days.Count == 0)
        {
            ShowStatus(snapshot.StatusMessage ?? "No weather data available.");
        }
        else
        {
            ShowStatus(snapshot.AlertStatusMessage ?? (snapshot.IsFromCache ? snapshot.StatusMessage ?? "Showing cached weather." : string.Empty));
        }
    }

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
    }

    private void ApplyAlerts(IReadOnlyList<WeatherAlertInfo> alerts, WeatherAlertInfo fallback)
    {
        WeatherAlertInfo primary = alerts.FirstOrDefault() ?? fallback;
        AlertBar.Background = GetAlertBackgroundBrush(primary.Level);
        AlertBar.BorderBrush = GetAlertBorderBrush(primary.Level);

        Brush foreground = GetAlertForegroundBrush(primary.Level);
        AlertTypesText.Foreground = foreground;
        AlertLocationText.Foreground = foreground;
        ChinaWeatherLink.Foreground = foreground;

        AlertTypesText.Text = alerts.Count == 0
            ? "No active alerts"
            : string.Join(" · ", alerts
                .Select(GetAlertType)
                .Distinct(StringComparer.OrdinalIgnoreCase));
        AlertLocationText.Text = !string.IsNullOrWhiteSpace(primary.LocationName)
            ? primary.LocationName
            : _currentCity.Name;
        AlertBar.ToolTip = alerts.Count == 0
            ? primary.Description
            : string.Join("\n\n", alerts.Select(alert =>
                $"{GetAlertType(alert)} · {AlertLocationText.Text}\n{alert.Description}"));

        ChinaWeatherLink.Inlines.Clear();
        ChinaWeatherLink.Inlines.Add(alerts.Count switch
        {
            > 1 => $"{alerts.Count} alerts  ›",
            1 => "Details  ›",
            _ => "China Weather  ›"
        });
        ChinaWeatherLink.NavigateUri = Uri.TryCreate(primary.LinkUrl, UriKind.Absolute, out Uri? uri)
            ? uri
            : new Uri("http://www.weather.com.cn/");
    }

    private static string GetAlertType(WeatherAlertInfo alert)
    {
        return string.IsNullOrWhiteSpace(alert.WeatherType) ? "Weather alert" : alert.WeatherType;
    }

    private void ChinaWeatherLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (_currentAlerts.Count > 1)
        {
            ShowAlertsMenu();
            e.Handled = true;
            return;
        }

        string url = string.IsNullOrWhiteSpace(_currentAlert.LinkUrl)
            ? "http://www.weather.com.cn/"
            : _currentAlert.LinkUrl;

        OpenExternalUri(url, "Unable to open weather link");
        e.Handled = true;
    }

    private void ShowAlertsMenu()
    {
        ContextMenu menu = new()
        {
            PlacementTarget = AlertBar,
            Placement = PlacementMode.Bottom
        };

        foreach (WeatherAlertInfo alert in _currentAlerts)
        {
            MenuItem item = new()
            {
                Header = $"{GetAlertType(alert)}  ·  {alert.LocationName}",
                Foreground = GetAlertForegroundBrush(alert.Level),
                ToolTip = alert.Description
            };
            item.Click += (_, _) => OpenExternalUri(alert.LinkUrl, "Unable to open weather alert");
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void LocationSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenExternalUri("ms-settings:privacy-location", "Unable to open location settings");
    }

    private static void OpenExternalUri(string uri, string errorMessage)
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = uri,
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{errorMessage}: {ex}");
        }
    }

    private void RestorePosition()
    {
        if (_settings.WeatherLeft.HasValue && _settings.WeatherTop.HasValue)
        {
            Left = _settings.WeatherLeft.Value;
            Top = _settings.WeatherTop.Value;
            KeepWindowOnScreen();
        }
    }

    private void SaveSettings()
    {
        SettingsStore.Update(settings =>
        {
            settings.WeatherLeft = Left;
            settings.WeatherTop = Top;
            settings.WeatherWidth = Width;
            settings.WeatherHeight = Height;
            settings.WeatherTopmost = Topmost;
            settings.WeatherIsLocked = LockMenuItem.IsChecked;
            settings.WeatherIsResizable = ResizeMenuItem.IsChecked;
            settings.WeatherLocationDenied = _settings.WeatherLocationDenied;
            settings.WeatherCityCode = _settings.WeatherCityCode;
            settings.WeatherCityName = _settings.WeatherCityName;
        });
    }

    private void KeepWindowOnScreen()
    {
        WindowSnapService.KeepWindowOnScreen(this);
    }

    private void ApplyResizeMode()
    {
        MinWidth = 320;
        MinHeight = 260;
        ResizeMode = _settings.WeatherIsResizable ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
    }

    private static Brush GetAlertBackgroundBrush(AlertLevel level)
    {
        return new SolidColorBrush(level switch
        {
            AlertLevel.Blue => Color.FromRgb(0x18, 0x3A, 0x5A),
            AlertLevel.Yellow => Color.FromRgb(0x4A, 0x3D, 0x18),
            AlertLevel.Orange => Color.FromRgb(0x51, 0x30, 0x18),
            AlertLevel.Red => Color.FromRgb(0x55, 0x1F, 0x28),
            _ => Color.FromRgb(0x27, 0x31, 0x42)
        });
    }

    private static Brush GetAlertForegroundBrush(AlertLevel level)
    {
        return new SolidColorBrush(level switch
        {
            AlertLevel.Blue => Color.FromRgb(0xB9, 0xE4, 0xFF),
            AlertLevel.Yellow => Color.FromRgb(0xFF, 0xE6, 0xA3),
            AlertLevel.Orange => Color.FromRgb(0xFF, 0xD0, 0xA3),
            AlertLevel.Red => Color.FromRgb(0xFF, 0xD0, 0xD6),
            _ => Color.FromRgb(0xAA, 0xB8, 0xC8)
        });
    }

    private static Brush GetAlertBorderBrush(AlertLevel level)
    {
        return new SolidColorBrush(level switch
        {
            AlertLevel.Blue => Color.FromRgb(0x2E, 0x7D, 0xB8),
            AlertLevel.Yellow => Color.FromRgb(0xC4, 0x9A, 0x2C),
            AlertLevel.Orange => Color.FromRgb(0xD5, 0x79, 0x2A),
            AlertLevel.Red => Color.FromRgb(0xD8, 0x4A, 0x5C),
            _ => Color.FromRgb(0x3A, 0x46, 0x58)
        });
    }
}
