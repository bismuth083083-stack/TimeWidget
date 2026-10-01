using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using TimeWidget.Models;

namespace TimeWidget.Services;

public sealed class AudioDeviceService : IDisposable
{
    private bool _disposed;

    public event EventHandler? DevicesChanged;
    public string? LastError { get; private set; }

    public IDisposable SubscribeDeviceChanges(EventHandler callback)
    {
        DevicesChanged += callback;
        return new Subscription(() => DevicesChanged -= callback);
    }

    public IReadOnlyList<AudioDeviceInfo> EnumerateRenderDevices()
    {
        try
        {
            ThrowIfDisposed();
            using MMDeviceEnumerator enumerator = new();
            string defaultId = GetDefaultDeviceId(enumerator, Role.Console) ?? string.Empty;
            MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            List<AudioDeviceInfo> result = [];
            for (int i = 0; i < devices.Count; i++)
            {
                using MMDevice device = devices[i];
                result.Add(new AudioDeviceInfo
                {
                    DeviceId = device.ID,
                    DisplayName = device.FriendlyName,
                    IsDefault = string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase)
                });
            }

            return result.OrderByDescending(device => device.IsDefault)
                .ThenBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Audio device enumeration failed: {ex}");
            return [];
        }
    }

    public AudioDeviceInfo? GetDefaultRenderDevice()
    {
        try
        {
            ThrowIfDisposed();
            using MMDeviceEnumerator enumerator = new();
            using MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            return new AudioDeviceInfo
            {
                DeviceId = device.ID,
                DisplayName = device.FriendlyName,
                IsDefault = true
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Default render device read failed: {ex}");
            return null;
        }
    }

    public bool SetDefaultRenderDevice(string deviceId, bool includeCommunications = false)
    {
        LastError = null;
        try
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                throw new ArgumentException("The audio endpoint id is empty.", nameof(deviceId));
            }

            using (MMDeviceEnumerator enumerator = new())
            using (MMDevice target = enumerator.GetDevice(deviceId))
            {
                if ((target.State & DeviceState.Active) == 0)
                {
                    throw new InvalidOperationException("The selected audio device is no longer active.");
                }
            }

            object policyObject = new PolicyConfigClient();
            try
            {
                IPolicyConfig policy = (IPolicyConfig)policyObject;
                SetDefaultEndpoint(policy, deviceId, ERole.eConsole);
                SetDefaultEndpoint(policy, deviceId, ERole.eMultimedia);
                if (includeCommunications)
                {
                    SetDefaultEndpoint(policy, deviceId, ERole.eCommunications);
                }
            }
            finally
            {
                if (Marshal.IsComObject(policyObject))
                {
                    Marshal.FinalReleaseComObject(policyObject);
                }
            }

            // The endpoint notification is asynchronous. Verify the roles before
            // reporting success so the UI cannot silently retain the old device.
            for (int attempt = 0; attempt < 12; attempt++)
            {
                if (IsDefaultForRole(deviceId, Role.Console)
                    && IsDefaultForRole(deviceId, Role.Multimedia)
                    && (!includeCommunications || IsDefaultForRole(deviceId, Role.Communications)))
                {
                    DevicesChanged?.Invoke(this, EventArgs.Empty);
                    return true;
                }

                Thread.Sleep(75);
            }

            throw new InvalidOperationException("Windows did not confirm the selected default audio device.");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Debug.WriteLine($"Default render device set failed: {ex}");
            return false;
        }
    }

    public void NotifyDevicesChanged()
    {
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }

    private static string? GetDefaultDeviceId(MMDeviceEnumerator enumerator, Role role)
    {
        try
        {
            using MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
            return device.ID;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsDefaultForRole(string deviceId, Role role)
    {
        try
        {
            using MMDeviceEnumerator enumerator = new();
            return string.Equals(GetDefaultDeviceId(enumerator, role), deviceId, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void SetDefaultEndpoint(IPolicyConfig policy, string deviceId, ERole role)
    {
        int hresult = policy.SetDefaultEndpoint(deviceId, role);
        if (hresult < 0)
        {
            Marshal.ThrowExceptionForHR(hresult);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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

[ComImport]
[Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal sealed class PolicyConfigClient
{
}

internal enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2
}

[ComImport]
[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig]
    int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr format);

    [PreserveSig]
    int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultFormat, IntPtr format);

    [PreserveSig]
    int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);

    [PreserveSig]
    int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultPeriod,
        IntPtr defaultProcessingPeriod, IntPtr minimumProcessingPeriod);

    [PreserveSig]
    int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr processingPeriod);

    [PreserveSig]
    int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

    [PreserveSig]
    int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

    [PreserveSig]
    int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr key, IntPtr value);

    [PreserveSig]
    int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr key, IntPtr value);

    [PreserveSig]
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);

    [PreserveSig]
    int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
}
