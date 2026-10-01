using System.Diagnostics;
using NAudio.CoreAudioApi;
using TimeWidget.Models;

namespace TimeWidget.Services;

public sealed class AudioSessionService : IDisposable
{
    private readonly object _syncRoot = new();
    private MMDeviceEnumerator? _enumerator;
    private readonly Dictionary<string, List<AudioSessionControl>> _sessionsByKey = [];
    private bool _disposed;

    public event EventHandler? SessionsChanged;

    public IDisposable SubscribeSessionChanges(EventHandler callback)
    {
        SessionsChanged += callback;
        return new Subscription(() => SessionsChanged -= callback);
    }

    public IReadOnlyList<AudioSessionInfo> EnumerateSessions()
    {
        lock (_syncRoot)
        {
            ClearSessionCache();
            try
            {
                _enumerator ??= new MMDeviceEnumerator();
                using MMDevice device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                SessionCollection sessions = device.AudioSessionManager.Sessions;
                Dictionary<string, AudioSessionInfo> grouped = [];

                for (int i = 0; i < sessions.Count; i++)
                {
                    AudioSessionControl session = sessions[i];
                    AudioSessionInfo info = CreateSessionInfo(session);
                    if (!grouped.TryGetValue(info.SessionKey, out AudioSessionInfo? existing))
                    {
                        grouped[info.SessionKey] = info;
                        _sessionsByKey[info.SessionKey] = [session];
                        continue;
                    }

                    existing.SessionCount++;
                    existing.VolumePercent = Math.Max(existing.VolumePercent, info.VolumePercent);
                    existing.IsMuted = existing.IsMuted || info.IsMuted;
                    _sessionsByKey[info.SessionKey].Add(session);
                }

                return grouped.Values
                    .OrderByDescending(session => session.IsSystemSounds)
                    .ThenBy(session => session.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Audio session enumeration failed: {ex}");
                ClearSessionCache();
                return [];
            }
        }
    }

    public int GetSessionVolume(string sessionId)
    {
        lock (_syncRoot)
        {
            if (!TryGetSessions(sessionId, out List<AudioSessionControl>? sessions) || sessions is null)
            {
                return 0;
            }

            return (int)Math.Round(sessions.Average(session => session.SimpleAudioVolume.Volume * 100));
        }
    }

    public void SetSessionVolume(string sessionId, int volume)
    {
        lock (_syncRoot)
        {
            if (!TryGetSessions(sessionId, out List<AudioSessionControl>? sessions) || sessions is null)
            {
                return;
            }

            float value = Math.Clamp(volume, 0, 100) / 100f;
            foreach (AudioSessionControl session in sessions)
            {
                TryRun(() => session.SimpleAudioVolume.Volume = value);
            }

            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool GetSessionMute(string sessionId)
    {
        lock (_syncRoot)
        {
            return TryGetSessions(sessionId, out List<AudioSessionControl>? sessions)
                && sessions is not null
                && sessions.Any(session => session.SimpleAudioVolume.Mute);
        }
    }

    public void SetSessionMute(string sessionId, bool muted)
    {
        lock (_syncRoot)
        {
            if (!TryGetSessions(sessionId, out List<AudioSessionControl>? sessions) || sessions is null)
            {
                return;
            }

            foreach (AudioSessionControl session in sessions)
            {
                TryRun(() => session.SimpleAudioVolume.Mute = muted);
            }

            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static AudioSessionInfo CreateSessionInfo(AudioSessionControl session)
    {
        uint processId = session.GetProcessID;
        bool isSystemSounds = processId == 0;
        string key = isSystemSounds ? "system-sounds" : $"process:{processId}";
        string displayName = GetDisplayName(session, processId, isSystemSounds);
        string? processPath = TryGetProcessPath(processId);
        return new AudioSessionInfo
        {
            SessionKey = key,
            DisplayName = displayName,
            ProcessId = isSystemSounds ? null : (int)processId,
            ProcessPath = processPath,
            VolumePercent = (int)Math.Round(session.SimpleAudioVolume.Volume * 100),
            IsMuted = session.SimpleAudioVolume.Mute,
            IsSystemSounds = isSystemSounds,
            SessionCount = 1
        };
    }

    private static string GetDisplayName(AudioSessionControl session, uint processId, bool isSystemSounds)
    {
        if (isSystemSounds)
        {
            return "System sounds";
        }

        if (!string.IsNullOrWhiteSpace(session.DisplayName))
        {
            return session.DisplayName;
        }

        try
        {
            using Process process = Process.GetProcessById((int)processId);
            string? description = process.MainModule?.FileVersionInfo.FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description;
            }

            return string.IsNullOrWhiteSpace(process.ProcessName) ? "Unknown app" : process.ProcessName;
        }
        catch
        {
            return "Unknown app";
        }
    }

    private static string? TryGetProcessPath(uint processId)
    {
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private bool TryGetSessions(string sessionId, out List<AudioSessionControl>? sessions)
    {
        if (_sessionsByKey.TryGetValue(sessionId, out sessions) && sessions.Count > 0)
        {
            return true;
        }

        EnumerateSessions();
        return _sessionsByKey.TryGetValue(sessionId, out sessions) && sessions.Count > 0;
    }

    private void ClearSessionCache()
    {
        foreach (AudioSessionControl session in _sessionsByKey.Values.SelectMany(value => value))
        {
            TryRun(session.Dispose);
        }

        _sessionsByKey.Clear();
    }

    private static void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Audio session operation failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_syncRoot)
        {
            ClearSessionCache();
            _enumerator?.Dispose();
            _enumerator = null;
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }
}
