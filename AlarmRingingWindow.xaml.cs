using System.ComponentModel;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TimeWidget.Models;

namespace TimeWidget;

public partial class AlarmRingingWindow : Window
{
    private readonly DispatcherTimer _soundTimer;
    private bool _isStopped;

    public AlarmInfo Alarm { get; }

    public event EventHandler<AlarmInfo>? StopRequested;
    public event EventHandler<AlarmInfo>? SnoozeRequested;

    public AlarmRingingWindow(AlarmInfo alarm)
    {
        InitializeComponent();
        Alarm = alarm;
        TimeText.Text = alarm.Time.ToString(@"hh\:mm");
        LabelText.Text = string.IsNullOrWhiteSpace(alarm.Label) ? "Alarm" : alarm.Label;

        _soundTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _soundTimer.Tick += (_, _) => PlaySound();

        Loaded += (_, _) =>
        {
            PlaySound();
            _soundTimer.Start();
        };
    }

    public void StopRinging()
    {
        _isStopped = true;
        _soundTimer.Stop();
        Close();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopRequested?.Invoke(this, Alarm);
        StopRinging();
    }

    private void SnoozeButton_Click(object sender, RoutedEventArgs e)
    {
        SnoozeRequested?.Invoke(this, Alarm);
        StopRinging();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isStopped)
        {
            StopRequested?.Invoke(this, Alarm);
        }

        _soundTimer.Stop();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private static void PlaySound()
    {
        SystemSounds.Exclamation.Play();
    }
}
