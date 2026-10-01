using System.Drawing;
using System.IO;
using System.Windows;
using TimeWidget.Services;
using Forms = System.Windows.Forms;

namespace TimeWidget;

public partial class App : Application
{
    private Forms.NotifyIcon? _trayIcon;
    private Icon? _applicationIcon;
    private Stream? _applicationIconStream;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!string.Equals(
                Environment.GetEnvironmentVariable("TIMEWIDGET_SKIP_STARTUP_REGISTRATION"),
                "1",
                StringComparison.Ordinal))
        {
            StartupService.EnableCurrentUserStartup();
        }

        InitializeTrayIcon();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _applicationIcon?.Dispose();
        _applicationIcon = null;
        _applicationIconStream?.Dispose();
        _applicationIconStream = null;

        base.OnExit(e);
    }

    private void InitializeTrayIcon()
    {
        Forms.ContextMenuStrip menu = new();
        menu.Items.Add("Open Clock Widget", null, (_, _) => ShowOrCreateWindow<MainWindow>());
        menu.Items.Add("Open Files Widget", null, (_, _) => ShowWindow(new FolderWidgetWindow()));
        menu.Items.Add("Open Media Widget", null, (_, _) => ShowWindow(new MediaWidgetWindow()));
        menu.Items.Add("Open Weather Widget", null, (_, _) => ShowWindow(new WeatherWidgetWindow()));
        menu.Items.Add("Open Alarm Timer Widget", null, (_, _) => ShowWindow(new AlarmTimerWidgetWindow()));
        menu.Items.Add("Open AI Search Widget", null, (_, _) => ShowWindow(new AiSearchWidgetWindow()));
        menu.Items.Add("Open Calendar Widget", null, (_, _) => ShowWindow(new CalendarWidgetWindow()));
        menu.Items.Add("Open Schedule Widget", null, (_, _) => ShowWindow(new ScheduleWidgetWindow()));
        menu.Items.Add("Open VPN Shortcuts", null, (_, _) => ShowWindow(new VpnWidgetWindow()));
        menu.Items.Add("Open Quick Settings", null, (_, _) => ShowWindow(new SettingsWidgetWindow()));
        menu.Items.Add("Open Performance Widget", null, (_, _) => ShowWindow(new PerformanceWidgetWindow()));
        menu.Items.Add("Open Network Traffic", null, (_, _) => ShowOrCreateWindow<NetworkTrafficWidgetWindow>());
        menu.Items.Add("Open Audio Control", null, (_, _) => ShowWindow(new AudioControlWidgetWindow()));
        menu.Items.Add("Open Finance Widget", null, (_, _) => ShowWindow(new FinanceWidgetWindow()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = LoadApplicationIcon(),
            Text = "Desktop Mini Widgets",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowOrCreateWindow<MainWindow>();
    }

    private Icon LoadApplicationIcon()
    {
        try
        {
            var resource = GetResourceStream(
                new Uri("pack://application:,,,/Assets/AppIcon.ico", UriKind.Absolute));
            if (resource is not null)
            {
                _applicationIconStream = resource.Stream;
                _applicationIcon = new Icon(_applicationIconStream);
                return _applicationIcon;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to load the application icon: {ex}");
        }

        return SystemIcons.Application;
    }

    private static void ShowOrCreateWindow<TWindow>()
        where TWindow : Window, new()
    {
        foreach (Window window in Current.Windows)
        {
            if (window is TWindow typedWindow)
            {
                ShowWindow(typedWindow);
                return;
            }
        }

        ShowWindow(new TWindow());
    }

    private static void ShowWindow(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }
}
