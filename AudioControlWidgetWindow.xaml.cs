using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TimeWidget.Models;
using TimeWidget.Services;

namespace TimeWidget;

public partial class AudioControlWidgetWindow : Window
{
    private readonly AudioSessionService _sessionService = new();
    private readonly AudioDeviceService _deviceService = new();
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _fullRefreshTimer;
    private WidgetSettings _settings = new();
    private List<AudioSessionInfo> _sessions = [];
    private List<AudioSessionItem> _sessionItems = [];
    private List<AudioDeviceInfo> _devices = [];
    private AudioSessionInfo? _selectedSession;
    private bool _isLoaded;
    private bool _isUpdatingUi;
    private bool _isSliderDragging;

    public AudioControlWidgetWindow()
    {
        InitializeComponent();
        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _statusTimer.Tick += async (_, _) => await RefreshSessionsAsync(fullRefresh: false);

        _fullRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.5)
        };
        _fullRefreshTimer.Tick += async (_, _) =>
        {
            await RefreshDevicesAsync();
            await RefreshSessionsAsync(fullRefresh: true);
        };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();
        Topmost = _settings.AudioControlTopmost;
        TopmostMenuItem.IsChecked = _settings.AudioControlTopmost;
        LockMenuItem.IsChecked = _settings.AudioControlIsLocked;
        RestorePosition();
        ApplySelectedTab(_settings.AudioControlSelectedTab);

        await RefreshDevicesAsync();
        await RefreshSessionsAsync(fullRefresh: true);
        _statusTimer.Start();
        _fullRefreshTimer.Start();
        _isLoaded = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _statusTimer.Stop();
        _fullRefreshTimer.Stop();
        SaveSettings();
        _sessionService.Dispose();
        _deviceService.Dispose();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.AudioControlIsLocked)
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
        _settings.AudioControlTopmost = TopmostMenuItem.IsChecked;
        Topmost = _settings.AudioControlTopmost;
        SaveSettings();
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.AudioControlIsLocked = LockMenuItem.IsChecked;
        SaveSettings();
    }

    private async void RefreshMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDevicesAsync();
        await RefreshSessionsAsync(fullRefresh: true);
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MixerTabButton_Click(object sender, RoutedEventArgs e)
    {
        ApplySelectedTab("Mixer");
        SaveSettings();
    }

    private void DeviceTabButton_Click(object sender, RoutedEventArgs e)
    {
        ApplySelectedTab("Output");
        SaveSettings();
    }

    private void ApplySelectedTab(string tab)
    {
        bool isOutput = string.Equals(tab, "Output", StringComparison.OrdinalIgnoreCase);
        MixerPanel.Visibility = isOutput ? Visibility.Collapsed : Visibility.Visible;
        DevicePanel.Visibility = isOutput ? Visibility.Visible : Visibility.Collapsed;
        TabIndicator.SetValue(Grid.ColumnProperty, isOutput ? 1 : 0);
        MixerTabButton.Foreground = isOutput ? FindBrush("MutedText") : FindBrush("AccentText");
        DeviceTabButton.Foreground = isOutput ? FindBrush("AccentText") : FindBrush("MutedText");
        _settings.AudioControlSelectedTab = isOutput ? "Output" : "Mixer";
    }

    private async Task RefreshSessionsAsync(bool fullRefresh)
    {
        if (!IsVisible)
        {
            return;
        }

        try
        {
            List<AudioSessionInfo> sessions = await Task.Run(() => _sessionService.EnumerateSessions().ToList());
            _sessions = sessions;
            SelectValidSession(fullRefresh);
            UpdateMixerUi();
        }
        catch
        {
            _sessions = [];
            _selectedSession = null;
            UpdateMixerUi();
        }
    }

    private async Task RefreshDevicesAsync()
    {
        try
        {
            AudioDeviceInfo? defaultDevice = await Task.Run(() => _deviceService.GetDefaultRenderDevice());
            List<AudioDeviceInfo> devices = await Task.Run(() => _deviceService.EnumerateRenderDevices().ToList());
            _devices = devices;
            DeviceNameText.Text = defaultDevice?.DisplayName ?? "No output device";
            DevicePickerButton.ToolTip = null;
        }
        catch
        {
            _devices = [];
            DeviceNameText.Text = "No output device";
        }
    }

    private void SelectValidSession(bool fullRefresh)
    {
        if (_sessions.Count == 0)
        {
            _selectedSession = null;
            return;
        }

        string? selectedKey = _selectedSession?.SessionKey ?? _settings.AudioControlSelectedSessionKey;
        _selectedSession = _sessions.FirstOrDefault(session => session.SessionKey == selectedKey)
            ?? _sessions.FirstOrDefault(session => !session.IsSystemSounds)
            ?? _sessions.FirstOrDefault();

        if (fullRefresh && _selectedSession is not null)
        {
            _settings.AudioControlSelectedSessionKey = _selectedSession.SessionKey;
        }
    }

    private void UpdateMixerUi()
    {
        if (_isSliderDragging)
        {
            return;
        }

        _isUpdatingUi = true;
        try
        {
            _sessionItems = _sessions
                .Select(session => new AudioSessionItem(session, _selectedSession?.SessionKey == session.SessionKey))
                .ToList();
            SessionsItemsControl.ItemsSource = _sessionItems;
            EmptySessionsText.Visibility = _sessionItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void SessionItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AudioSessionItem item)
        {
            return;
        }

        _selectedSession = _sessions.FirstOrDefault(session => session.SessionKey == item.SessionKey);
        _settings.AudioControlSelectedSessionKey = _selectedSession?.SessionKey;
        foreach (AudioSessionItem sessionItem in _sessionItems)
        {
            sessionItem.IsSelected = sessionItem.SessionKey == item.SessionKey;
        }

        SaveSettings();
    }

    private async void DevicePickerButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDevicesAsync();
        ContextMenu menu = new();
        if (_devices.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No output devices", IsEnabled = false });
        }

        foreach (AudioDeviceInfo device in _devices)
        {
            MenuItem item = new()
            {
                Header = $"{(device.IsDefault ? "✓ " : string.Empty)}{device.DisplayName}",
                Tag = device.DeviceId
            };
            item.Click += async (_, _) =>
            {
                string deviceId = (string)item.Tag;
                bool ok = await Task.Run(() => _deviceService.SetDefaultRenderDevice(deviceId, _settings.AudioControlSwitchCommunications));
                if (ok)
                {
                    await RefreshDevicesAsync();
                    await RefreshSessionsAsync(fullRefresh: true);
                }
                else
                {
                    DeviceNameText.Text = "Switch failed";
                    DevicePickerButton.ToolTip = _deviceService.LastError ?? "Windows could not switch the output device.";
                }
            };
            menu.Items.Add(item);
        }

        menu.PlacementTarget = DevicePickerButton;
        menu.IsOpen = true;
    }

    private async void RefreshDevicesButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDevicesAsync();
        await RefreshSessionsAsync(fullRefresh: true);
    }

    private void SessionMuteButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AudioSessionItem item)
        {
            return;
        }

        bool muted = !item.IsMuted;
        _sessionService.SetSessionMute(item.SessionKey, muted);
        item.IsMuted = muted;
        AudioSessionInfo? session = _sessions.FirstOrDefault(value => value.SessionKey == item.SessionKey);
        if (session is not null)
        {
            session.IsMuted = muted;
        }
    }

    private void SessionVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi || (sender as FrameworkElement)?.DataContext is not AudioSessionItem item)
        {
            return;
        }

        int volume = (int)Math.Round(e.NewValue);
        _isSliderDragging = Mouse.LeftButton == MouseButtonState.Pressed;
        item.VolumePercent = volume;
        AudioSessionInfo? session = _sessions.FirstOrDefault(value => value.SessionKey == item.SessionKey);
        if (session is not null)
        {
            session.VolumePercent = volume;
        }

        _sessionService.SetSessionVolume(item.SessionKey, volume);
        _isSliderDragging = false;
    }

    private void SessionVolumeSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Slider slider)
        {
            return;
        }

        int delta = e.Delta > 0 ? 2 : -2;
        slider.Value = Math.Clamp(slider.Value + delta, 0, 100);
        e.Handled = true;
    }

    private void SessionPercentText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AudioSessionItem item)
        {
            return;
        }

        item.VolumePercent = 100;
        AudioSessionInfo? session = _sessions.FirstOrDefault(value => value.SessionKey == item.SessionKey);
        if (session is not null)
        {
            session.VolumePercent = 100;
        }

        _sessionService.SetSessionVolume(item.SessionKey, 100);
    }

    private void RestorePosition()
    {
        if (_settings.AudioControlLeft.HasValue && _settings.AudioControlTop.HasValue)
        {
            Left = _settings.AudioControlLeft.Value;
            Top = _settings.AudioControlTop.Value;
            WindowSnapService.KeepWindowOnScreen(this);
        }
    }

    private void SaveSettings()
    {
        if (!_isLoaded)
        {
            return;
        }

        SettingsStore.Update(settings =>
        {
            settings.AudioControlLeft = Left;
            settings.AudioControlTop = Top;
            settings.AudioControlTopmost = Topmost;
            settings.AudioControlIsLocked = LockMenuItem.IsChecked;
            settings.AudioControlSelectedTab = _settings.AudioControlSelectedTab;
            settings.AudioControlSelectedSessionKey = _settings.AudioControlSelectedSessionKey;
            settings.AudioControlSwitchCommunications = _settings.AudioControlSwitchCommunications;
        });
    }

    private Brush FindBrush(string key)
    {
        return (Brush)FindResource(key);
    }

    private sealed class AudioSessionItem : INotifyPropertyChanged
    {
        private int _volumePercent;
        private bool _isMuted;
        private bool _isSelected;

        public AudioSessionItem(AudioSessionInfo session, bool isSelected)
        {
            SessionKey = session.SessionKey;
            DisplayName = session.DisplayName;
            IsSystemSounds = session.IsSystemSounds;
            _volumePercent = session.VolumePercent;
            _isMuted = session.IsMuted;
            _isSelected = isSelected;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string SessionKey { get; }

        public string DisplayName { get; }

        public bool IsSystemSounds { get; }

        public string IconGlyph => IsSystemSounds ? "\uE995" : "\uE8D6";

        public int VolumePercent
        {
            get => _volumePercent;
            set
            {
                int normalized = Math.Clamp(value, 0, 100);
                if (_volumePercent == normalized)
                {
                    return;
                }

                _volumePercent = normalized;
                OnPropertyChanged(nameof(VolumePercent));
                OnPropertyChanged(nameof(VolumeText));
                OnPropertyChanged(nameof(MuteIcon));
            }
        }

        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                if (_isMuted == value)
                {
                    return;
                }

                _isMuted = value;
                OnPropertyChanged(nameof(IsMuted));
                OnPropertyChanged(nameof(MuteIcon));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public string VolumeText => $"{VolumePercent}%";

        public string MuteIcon => IsMuted || VolumePercent <= 0 ? "\uE74F" : "\uE995";

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
