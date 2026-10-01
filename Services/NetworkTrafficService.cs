using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using TimeWidget.Models;

namespace TimeWidget.Services;

public sealed class NetworkTrafficService : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, InterfaceCounter> _previousCounters = [];
    private long _lastSampleTimestamp;
    private long _sessionReceivedBytes;
    private long _sessionSentBytes;
    private bool _disposed;

    public NetworkTrafficSnapshot Sample()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return new NetworkTrafficSnapshot();
            }

            long timestamp = Stopwatch.GetTimestamp();
            List<InterfaceSample> samples = ReadActiveInterfaces();
            if (samples.Count == 0)
            {
                _previousCounters.Clear();
                _lastSampleTimestamp = timestamp;
                return new NetworkTrafficSnapshot
                {
                    SessionReceivedBytes = _sessionReceivedBytes,
                    SessionSentBytes = _sessionSentBytes
                };
            }

            long receivedDelta = 0;
            long sentDelta = 0;
            if (_lastSampleTimestamp != 0)
            {
                foreach (InterfaceSample sample in samples)
                {
                    if (!_previousCounters.TryGetValue(sample.Id, out InterfaceCounter previous))
                    {
                        continue;
                    }

                    if (sample.ReceivedBytes >= previous.ReceivedBytes)
                    {
                        receivedDelta += sample.ReceivedBytes - previous.ReceivedBytes;
                    }

                    if (sample.SentBytes >= previous.SentBytes)
                    {
                        sentDelta += sample.SentBytes - previous.SentBytes;
                    }
                }
            }

            double elapsedSeconds = _lastSampleTimestamp == 0
                ? 0
                : Stopwatch.GetElapsedTime(_lastSampleTimestamp, timestamp).TotalSeconds;

            _previousCounters.Clear();
            foreach (InterfaceSample sample in samples)
            {
                _previousCounters[sample.Id] = new InterfaceCounter(sample.ReceivedBytes, sample.SentBytes);
            }

            _lastSampleTimestamp = timestamp;
            _sessionReceivedBytes += receivedDelta;
            _sessionSentBytes += sentDelta;

            string adapterName = samples.Count == 1
                ? samples[0].Name
                : $"{samples[0].Name} +{samples.Count - 1}";

            return new NetworkTrafficSnapshot
            {
                IsAvailable = true,
                AdapterName = adapterName,
                DownloadBytesPerSecond = elapsedSeconds > 0 ? receivedDelta / elapsedSeconds : 0,
                UploadBytesPerSecond = elapsedSeconds > 0 ? sentDelta / elapsedSeconds : 0,
                SessionReceivedBytes = _sessionReceivedBytes,
                SessionSentBytes = _sessionSentBytes
            };
        }
    }

    public void ResetSessionTotals()
    {
        lock (_syncRoot)
        {
            _sessionReceivedBytes = 0;
            _sessionSentBytes = 0;
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            _disposed = true;
            _previousCounters.Clear();
        }
    }

    private static List<InterfaceSample> ReadActiveInterfaces()
    {
        try
        {
            List<NetworkInterface> active = NetworkInterface.GetAllNetworkInterfaces()
                .Where(networkInterface =>
                    networkInterface.OperationalStatus == OperationalStatus.Up
                    && networkInterface.NetworkInterfaceType is not NetworkInterfaceType.Loopback
                    && networkInterface.NetworkInterfaceType is not NetworkInterfaceType.Tunnel)
                .ToList();

            List<NetworkInterface> internetFacing = active.Where(HasUsableGateway).ToList();
            IReadOnlyList<NetworkInterface> selected = internetFacing.Count > 0 ? internetFacing : active;
            List<InterfaceSample> samples = [];

            foreach (NetworkInterface networkInterface in selected)
            {
                try
                {
                    IPv4InterfaceStatistics statistics = networkInterface.GetIPv4Statistics();
                    samples.Add(new InterfaceSample(
                        networkInterface.Id,
                        networkInterface.Name,
                        statistics.BytesReceived,
                        statistics.BytesSent));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Network interface sample failed: {ex.Message}");
                }
            }

            return samples;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Network interface enumeration failed: {ex.Message}");
            return [];
        }
    }

    private static bool HasUsableGateway(NetworkInterface networkInterface)
    {
        try
        {
            return networkInterface.GetIPProperties().GatewayAddresses.Any(gateway =>
                !gateway.Address.Equals(IPAddress.Any)
                && !gateway.Address.Equals(IPAddress.IPv6Any));
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct InterfaceCounter(long ReceivedBytes, long SentBytes);
    private readonly record struct InterfaceSample(string Id, string Name, long ReceivedBytes, long SentBytes);
}
