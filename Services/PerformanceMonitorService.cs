using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using TimeWidget.Models;

namespace TimeWidget.Services;

public sealed class PerformanceMonitorService
{
    private const double BytesPerGb = 1024d * 1024d * 1024d;
    private static readonly TimeSpan GpuCacheDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMilliseconds(900);

    private CpuTimes? _lastCpuTimes;
    private PerformanceMetricInfo _cachedGpu = new();
    private DateTime _lastGpuRead = DateTime.MinValue;
    private string? _nvidiaSmiPath;
    private bool _nvidiaSmiChecked;

    public async Task<PerformanceSnapshot> GetSnapshotAsync()
    {
        PerformanceMetricInfo cpu = GetCpuInfo();
        MemoryMetricInfo memory = GetMemoryInfo();
        PerformanceMetricInfo gpu = await GetGpuInfoAsync();

        return new PerformanceSnapshot
        {
            Cpu = cpu,
            Gpu = gpu,
            Memory = memory
        };
    }

    private PerformanceMetricInfo GetCpuInfo()
    {
        double? usage = null;
        try
        {
            if (GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user))
            {
                CpuTimes current = new(ToUInt64(idle), ToUInt64(kernel), ToUInt64(user));
                if (_lastCpuTimes.HasValue)
                {
                    CpuTimes previous = _lastCpuTimes.Value;
                    ulong idleDelta = current.Idle - previous.Idle;
                    ulong kernelDelta = current.Kernel - previous.Kernel;
                    ulong userDelta = current.User - previous.User;
                    ulong total = kernelDelta + userDelta;
                    if (total > 0)
                    {
                        usage = Math.Clamp((1.0 - idleDelta / (double)total) * 100, 0, 100);
                    }
                }

                _lastCpuTimes = current;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CPU usage read failed: {ex}");
        }

        return new PerformanceMetricInfo
        {
            UsagePercent = usage,
            FrequencyMHz = ReadCpuFrequencyMHz()
        };
    }

    private static double? ReadCpuFrequencyMHz()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            object? mhz = key?.GetValue("~MHz");
            return mhz is null ? null : Convert.ToDouble(mhz);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CPU frequency read failed: {ex}");
            return null;
        }
    }

    private static MemoryMetricInfo GetMemoryInfo()
    {
        try
        {
            MemoryStatusEx status = new();
            status.Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
            if (GlobalMemoryStatusEx(ref status))
            {
                double total = status.TotalPhys / BytesPerGb;
                double available = status.AvailPhys / BytesPerGb;
                double used = total - available;
                return new MemoryMetricInfo
                {
                    TotalGB = total,
                    AvailableGB = available,
                    UsedGB = used,
                    UsagePercent = Math.Clamp(used / total * 100, 0, 100)
                };
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Memory read failed: {ex}");
        }

        return new MemoryMetricInfo();
    }

    private async Task<PerformanceMetricInfo> GetGpuInfoAsync()
    {
        if (DateTime.UtcNow - _lastGpuRead < GpuCacheDuration)
        {
            return _cachedGpu;
        }

        _lastGpuRead = DateTime.UtcNow;
        string? nvidiaSmi = FindNvidiaSmi();
        if (string.IsNullOrWhiteSpace(nvidiaSmi))
        {
            _cachedGpu = new PerformanceMetricInfo();
            return _cachedGpu;
        }

        try
        {
            string args = "--query-gpu=utilization.gpu,clocks.gr,temperature.gpu,power.draw --format=csv,noheader,nounits";
            string output = await RunProcessAsync(nvidiaSmi, args);
            string firstLine = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            string[] parts = firstLine.Split(',', StringSplitOptions.TrimEntries);

            _cachedGpu = new PerformanceMetricInfo
            {
                UsagePercent = ReadDouble(parts, 0),
                FrequencyMHz = ReadDouble(parts, 1),
                TemperatureC = ReadDouble(parts, 2),
                PowerW = ReadDouble(parts, 3)
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GPU read failed: {ex}");
            _cachedGpu = new PerformanceMetricInfo();
        }

        return _cachedGpu;
    }

    private string? FindNvidiaSmi()
    {
        if (_nvidiaSmiChecked)
        {
            return _nvidiaSmiPath;
        }

        _nvidiaSmiChecked = true;
        string[] candidates =
        [
            "nvidia-smi.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
        ];

        _nvidiaSmiPath = candidates.FirstOrDefault(path => path == "nvidia-smi.exe" || File.Exists(path));
        return _nvidiaSmiPath;
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

        using Process process = new() { StartInfo = startInfo };
        process.Start();
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);
        return await outputTask;
    }

    private static double? ReadDouble(string[] parts, int index)
    {
        if (index >= parts.Length)
        {
            return null;
        }

        return double.TryParse(parts[index], out double value) ? value : null;
    }

    private static ulong ToUInt64(FileTime time)
    {
        return ((ulong)time.HighDateTime << 32) | time.LowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CpuTimes(ulong idle, ulong kernel, ulong user)
    {
        public ulong Idle { get; } = idle;
        public ulong Kernel { get; } = kernel;
        public ulong User { get; } = user;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
