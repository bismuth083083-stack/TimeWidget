using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TimeWidget.Models;
using TimeWidget.Services;

namespace TimeWidget;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly IFolderWidgetWindowFactory _folderWidgetWindowFactory = new FolderWidgetWindowFactory();
    private WidgetSettings _settings = new();

    public MainWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => UpdateClock();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();

        Topmost = _settings.Topmost;
        TopmostMenuItem.IsChecked = _settings.Topmost;
        LockMenuItem.IsChecked = _settings.IsLocked;

        if (_settings.Left.HasValue && _settings.Top.HasValue)
        {
            Left = _settings.Left.Value;
            Top = _settings.Top.Value;
            KeepWindowOnScreen();
        }

        UpdateClock();
        _timer.Start();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveSettings();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.IsLocked)
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
        _settings.Topmost = TopmostMenuItem.IsChecked;
        Topmost = _settings.Topmost;
        SaveSettings();
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.IsLocked = LockMenuItem.IsChecked;
        SaveSettings();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenFilesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        FolderWidgetWindow folderWidgetWindow = _folderWidgetWindowFactory.Create();
        folderWidgetWindow.Show();
    }

    private void OpenMediaMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MediaWidgetWindow mediaWidgetWindow = new();
        mediaWidgetWindow.Show();
    }

    private void OpenWeatherMenuItem_Click(object sender, RoutedEventArgs e)
    {
        WeatherWidgetWindow weatherWidgetWindow = new();
        weatherWidgetWindow.Show();
    }

    private void UpdateClock()
    {
        DateTime now = DateTime.Now;
        TimeText.Text = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        DateText.Text = now.ToString("yyyy/MM/dd dddd", CultureInfo.InvariantCulture);
    }

    private void SaveSettings()
    {
        SettingsStore.Update(settings =>
        {
            settings.Left = Left;
            settings.Top = Top;
            settings.Topmost = Topmost;
            settings.IsLocked = LockMenuItem.IsChecked;
        });
    }

    private void KeepWindowOnScreen()
    {
        WindowSnapService.KeepWindowOnScreen(this);
    }
}
