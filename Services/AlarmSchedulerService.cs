using System.Windows.Threading;
using TimeWidget.Models;

namespace TimeWidget.Services;

public sealed class AlarmSchedulerService
{
    private readonly DispatcherTimer _timer;
    private readonly Func<IReadOnlyList<AlarmInfo>> _getAlarms;

    public event EventHandler<AlarmInfo>? AlarmTriggered;

    public AlarmSchedulerService(Func<IReadOnlyList<AlarmInfo>> getAlarms)
    {
        _getAlarms = getAlarms;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => CheckAlarms(DateTime.Now);
    }

    public void Start()
    {
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public DateTime? GetNextAlarmTime()
    {
        DateTime now = DateTime.Now;
        List<DateTime> candidates = _getAlarms()
            .Where(alarm => alarm.IsEnabled)
            .Select(alarm => GetNextOccurrence(alarm, now))
            .Where(next => next.HasValue)
            .Select(next => next!.Value)
            .OrderBy(next => next)
            .ToList();

        return candidates.Count == 0 ? null : candidates[0];
    }

    private void CheckAlarms(DateTime now)
    {
        foreach (AlarmInfo alarm in _getAlarms().Where(alarm => alarm.IsEnabled).ToList())
        {
            if (!ShouldTrigger(alarm, now))
            {
                continue;
            }

            alarm.LastTriggeredDate = now.Date;
            if (alarm.RepeatDays.Count == 0)
            {
                alarm.IsEnabled = false;
            }

            AlarmTriggered?.Invoke(this, alarm);
        }
    }

    private static bool ShouldTrigger(AlarmInfo alarm, DateTime now)
    {
        if (alarm.Time.Hours != now.Hour || alarm.Time.Minutes != now.Minute)
        {
            return false;
        }

        if (alarm.LastTriggeredDate == now.Date)
        {
            return false;
        }

        return alarm.RepeatDays.Count == 0 || alarm.RepeatDays.Contains(now.DayOfWeek);
    }

    private static DateTime? GetNextOccurrence(AlarmInfo alarm, DateTime now)
    {
        for (int offset = 0; offset <= 7; offset++)
        {
            DateTime date = now.Date.AddDays(offset);
            DateTime candidate = date + alarm.Time;
            if (candidate <= now)
            {
                continue;
            }

            if (alarm.RepeatDays.Count == 0 || alarm.RepeatDays.Contains(candidate.DayOfWeek))
            {
                return candidate;
            }
        }

        return null;
    }
}
