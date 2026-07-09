using System.Drawing;
using System.Windows;
using TimeWidget.Services;
using Forms = System.Windows.Forms;

namespace TimeWidget;

public partial class App : Application
{
    private Forms.NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        StartupService.EnableCurrentUserStartup();
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
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Desktop Mini Widgets",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowOrCreateWindow<MainWindow>();
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
