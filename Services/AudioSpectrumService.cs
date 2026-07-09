using System.Diagnostics;
using NAudio.Wave;

namespace TimeWidget.Services;

public sealed class AudioSpectrumService : IDisposable
{
    private const int BufferSize = 44100 * 4;
    private readonly object _syncRoot = new();
    private readonly float[] _ringBuffer = new float[BufferSize];
    private int _writeIndex;
    private int _sampleCount;
    private WasapiLoopbackCapture? _capture;
    private bool _isDisposed;
    private bool _isRestarting;
    private bool _isStoppingCapture;

    public int SampleRate { get; private set; } = 44100;

    public event EventHandler<string>? CaptureUnavailable;
    public event EventHandler? CaptureAvailable;

    public void Start()
    {
        if (_capture is not null || _isDisposed)
        {
            return;
        }

        try
        {
            _capture = new WasapiLoopbackCapture();
            SampleRate = _capture.WaveFormat.SampleRate;
            _capture.DataAvailable += Capture_DataAvailable;
            _capture.RecordingStopped += Capture_RecordingStopped;
            _capture.StartRecording();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Audio capture start failed: {ex}");
            DisposeCapture();
            ClearBuffer();
            CaptureUnavailable?.Invoke(this, "No audio output device available.");
        }
    }

    public void Restart()
    {
        if (_isDisposed)
        {
            return;
        }

        DisposeCapture();
        ClearBuffer();
        Start();
    }

    public bool TryGetLatestSamples(int count, float[] destination)
    {
        if (destination.Length < count)
        {
            throw new ArgumentException("Destination buffer is smaller than requested sample count.", nameof(destination));
        }

        lock (_syncRoot)
        {
            if (_sampleCount < count)
            {
                return false;
            }

            int startIndex = (_writeIndex - count + _ringBuffer.Length) % _ringBuffer.Length;
            for (int i = 0; i < count; i++)
            {
                destination[i] = _ringBuffer[(startIndex + i) % _ringBuffer.Length];
            }

            return true;
        }
    }

    private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_isDisposed || _capture is null || e.BytesRecorded <= 0)
        {
            return;
        }

        try
        {
            AddSamples(e.Buffer, e.BytesRecorded, _capture.WaveFormat);
            CaptureAvailable?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Audio capture processing failed: {ex}");
        }
    }

    private void AddSamples(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        int bytesPerSample = format.BitsPerSample / 8;
        int frameSize = bytesPerSample * format.Channels;
        if (bytesPerSample <= 0 || frameSize <= 0)
        {
            return;
        }

        lock (_syncRoot)
        {
            for (int offset = 0; offset + frameSize <= bytesRecorded; offset += frameSize)
            {
                float monoSample = 0;
                for (int channel = 0; channel < format.Channels; channel++)
                {
                    int sampleOffset = offset + channel * bytesPerSample;
                    monoSample += ReadSample(buffer, sampleOffset, format);
                }

                monoSample /= format.Channels;
                _ringBuffer[_writeIndex] = monoSample;
                _writeIndex = (_writeIndex + 1) % _ringBuffer.Length;
                _sampleCount = Math.Min(_sampleCount + 1, _ringBuffer.Length);
            }
        }
    }

    private static float ReadSample(byte[] buffer, int offset, WaveFormat format)
    {
        if (format.BitsPerSample == 32 && IsFloatFormat(format))
        {
            float sample = BitConverter.ToSingle(buffer, offset);
            return float.IsFinite(sample) ? Math.Clamp(sample, -1f, 1f) : 0;
        }

        return format.BitsPerSample switch
        {
            16 => BitConverter.ToInt16(buffer, offset) / 32768f,
            24 => Read24BitSample(buffer, offset) / 8388608f,
            32 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
            _ => 0
        };
    }

    private static bool IsFloatFormat(WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            return true;
        }

        if (format.Encoding != WaveFormatEncoding.Extensible)
        {
            return false;
        }

        string formatText = format.ToString();
        return formatText.Contains("IEEE", StringComparison.OrdinalIgnoreCase)
            || formatText.Contains("Float", StringComparison.OrdinalIgnoreCase);
    }

    private static int Read24BitSample(byte[] buffer, int offset)
    {
        int sample = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
        if ((sample & 0x800000) != 0)
        {
            sample |= unchecked((int)0xFF000000);
        }

        return sample;
    }

    private void Capture_RecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        if (_isStoppingCapture)
        {
            return;
        }

        if (e.Exception is not null)
        {
            Debug.WriteLine($"Audio capture stopped: {e.Exception}");
            CaptureUnavailable?.Invoke(this, "Audio capture stopped.");
        }

        RestartCaptureSoon();
    }

    private void RestartCaptureSoon()
    {
        if (_isDisposed || _isRestarting)
        {
            return;
        }

        _isRestarting = true;
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(800);
                if (_isDisposed)
                {
                    return;
                }

                DisposeCapture();
                ClearBuffer();
                _isRestarting = false;
                Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Audio capture restart failed: {ex}");
                _isRestarting = false;
            }
        });
    }

    private void ClearBuffer()
    {
        lock (_syncRoot)
        {
            Array.Clear(_ringBuffer);
            _writeIndex = 0;
            _sampleCount = 0;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        DisposeCapture();
        ClearBuffer();
    }

    private void DisposeCapture()
    {
        if (_capture is null)
        {
            return;
        }

        try
        {
            _capture.DataAvailable -= Capture_DataAvailable;
            _capture.RecordingStopped -= Capture_RecordingStopped;
            _isStoppingCapture = true;
            _capture.StopRecording();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Audio capture stop failed: {ex}");
        }
        finally
        {
            _isStoppingCapture = false;
            _capture.Dispose();
            _capture = null;
        }
    }
}
