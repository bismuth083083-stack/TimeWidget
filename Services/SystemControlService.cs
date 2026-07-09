using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using NAudio.CoreAudioApi;
using TimeWidget.Models;

namespace TimeWidget.Services;

public static class SystemControlService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(3);

    public static async Task<BrightnessStatusInfo> GetBrightnessAsync()
    {
        try
        {
            string output = await RunPowerShellAsync("(Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightness -ErrorAction Stop | Select-Object -First 1 -ExpandProperty CurrentBrightness)");
            if (int.TryParse(output.Trim(), out int value))
            {
                return new BrightnessStatusInfo
                {
                    IsSupported = true,
                    Value = Math.Clamp(value, 0, 100)
                };
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Brightness read failed: {ex}");
        }

        return new BrightnessStatusInfo
        {
            IsSupported = false,
            Message = "当前设备不支持亮度调节"
        };
    }

    public static async Task<bool> SetBrightnessAsync(int value)
    {
        try
        {
            int clamped = Math.Clamp(value, 0, 100);
            string command = $"$m = Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightnessMethods -ErrorAction Stop | Select-Object -First 1; Invoke-CimMethod -InputObject $m -MethodName WmiSetBrightness -Arguments @{{Timeout=1;Brightness={clamped}}} | Out-Null";
            await RunPowerShellAsync(command);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Brightness set failed: {ex}");
            return false;
        }
    }

    public static async Task<WifiStatusInfo> GetWifiStatusAsync()
    {
        try
        {
            string output = await RunProcessAsync("netsh", "wlan show interfaces");
            if (string.IsNullOrWhiteSpace(output) || output.Contains("There is no wireless interface", StringComparison.OrdinalIgnoreCase))
            {
                return new WifiStatusInfo { IsAvailable = false };
            }

            string state = MatchValue(output, "State");
            string ssid = MatchValue(output, "SSID");
            bool connected = state.Equals("connected", StringComparison.OrdinalIgnoreCase);
            return new WifiStatusInfo
            {
                IsAvailable = true,
                IsConnected = connected,
                DisplayName = connected && !string.IsNullOrWhiteSpace(ssid) ? ssid : "Wi-Fi"
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Wi-Fi status failed: {ex}");
            return new WifiStatusInfo { IsAvailable = false };
        }
    }

    public static async Task<BluetoothStatusInfo> GetBluetoothStatusAsync()
    {
        try
        {
            string command = "Get-PnpDevice -Class Bluetooth -ErrorAction Stop | Where-Object { $_.Status -eq 'OK' } | Select-Object -First 1 -ExpandProperty FriendlyName";
            string output = await RunPowerShellAsync(command);
            bool enabled = !string.IsNullOrWhiteSpace(output);
            return new BluetoothStatusInfo
            {
                IsAvailable = enabled,
                IsEnabled = enabled,
                DisplayName = enabled ? "Bluetooth" : "Bluetooth"
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Bluetooth status failed: {ex}");
            return new BluetoothStatusInfo { IsAvailable = false };
        }
    }

    public static Task<VolumeStatusInfo> GetVolumeAsync()
    {
        try
        {
            using MMDeviceEnumerator enumerator = new();
            using MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            int value = (int)Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
            return Task.FromResult(new VolumeStatusInfo
            {
                IsSupported = true,
                Value = Math.Clamp(value, 0, 100)
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Volume read failed: {ex}");
            return Task.FromResult(new VolumeStatusInfo
            {
                IsSupported = false,
                Message = "未找到可用音频输出设备"
            });
        }
    }

    public static Task<bool> SetVolumeAsync(int value)
    {
        try
        {
            float scalar = Math.Clamp(value, 0, 100) / 100f;
            using MMDeviceEnumerator enumerator = new();
            using MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = scalar;
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Volume set failed: {ex}");
            return Task.FromResult(false);
        }
    }

    private static async Task<string> RunPowerShellAsync(string command)
    {
        string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        return await RunProcessAsync("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}");
    }

    private static async Task<string> RunProcessAsync(string fileName, string arguments)
    {
        using CancellationTokenSource cts = new(CommandTimeout);
        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using Process process = new()
        {
            StartInfo = startInfo
        };

        process.Start();
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);

        string output = await outputTask;
        string error = await errorTask;
        if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException(error);
        }

        return output;
    }

    private static string MatchValue(string output, string key)
    {
        Match match = Regex.Match(output, $@"^\s*{Regex.Escape(key)}\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }
}
