namespace TimeWidget.Models;

public sealed class AudioSessionInfo
{
    public string SessionKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Unknown app";
    public int? ProcessId { get; set; }
    public string? ProcessPath { get; set; }
    public int VolumePercent { get; set; }
    public bool IsMuted { get; set; }
    public bool IsSystemSounds { get; set; }
    public int SessionCount { get; set; }
}

public sealed class AudioDeviceInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Unknown device";
    public bool IsDefault { get; set; }
}
