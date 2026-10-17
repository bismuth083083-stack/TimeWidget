using System.Diagnostics;
using NAudio.Dsp;

namespace TimeWidget.Services;

public sealed class FftSpectrumAnalyzer
{
    public const int FftSize = 4096;
    public const int BandCount = 32;
    private const int FftPower = 12;
    public const double MinFrequency = 30;
    public const double MaxFrequency = 8000;
    private const double Gain = 72;

    // Volume-independent visualisation: the analyser normalises each block to a
    // reference RMS before the FFT so a quiet source still produces visible bars.
    // Without this a player at ~-30 dBFS only lit 1-7 of 32 bands and the widget
    // looked like it had no spectrum at all.
    private const double TargetRms = 0.12;
    private const double MaxAutoGain = 24;
    private const double AutoGainFloor = 1;
    private const double SilencePeak = 0.004;
    private const double GainAttack = 0.35;
    private const double GainRelease = 0.12;

    private readonly Complex[] _fftBuffer = new Complex[FftSize];
    private readonly double[] _window = CreateHannWindow();
    private readonly int[] _startBins = new int[BandCount];
    private readonly int[] _endBins = new int[BandCount];
    private int _mappedSampleRate;
    private double _autoGain = 1;

    public double[] Analyze(float[] samples, int sampleRate)
    {
        if (samples.Length < FftSize || sampleRate / 2.0 <= MinFrequency)
        {
            return new double[BandCount];
        }

        double[] bands = new double[BandCount];

        try
        {
            if (_mappedSampleRate != sampleRate)
            {
                UpdateBandMapping(sampleRate);
            }

            double scale = UpdateAutoGain(samples);
            if (scale <= 0)
            {
                return bands;
            }

            for (int i = 0; i < FftSize; i++)
            {
                _fftBuffer[i].X = float.IsFinite(samples[i])
                    ? (float)(samples[i] * _window[i] * scale)
                    : 0;
                _fftBuffer[i].Y = 0;
            }

            FastFourierTransform.FFT(true, FftPower, _fftBuffer);

            for (int band = 0; band < BandCount; band++)
            {
                int startBin = _startBins[band];
                int endBin = _endBins[band];

                double peak = 0;
                double sum = 0;
                int count = 0;

                for (int bin = startBin; bin < endBin; bin++)
                {
                    double real = _fftBuffer[bin].X;
                    double imaginary = _fftBuffer[bin].Y;
                    double magnitude = Math.Sqrt(real * real + imaginary * imaginary);
                    peak = Math.Max(peak, magnitude);
                    sum += magnitude;
                    count++;
                }

                double average = count > 0 ? sum / count : 0;
                double magnitudeForBand = Math.Max(peak * 0.75, average);
                double weightedMagnitude = magnitudeForBand * GetBandWeight(band);
                bands[band] = Math.Clamp(Math.Log10(1 + weightedMagnitude * Gain), 0, 1);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FFT spectrum analysis failed: {ex}");
        }

        return bands;
    }

    /// <summary>
    /// Computes the per-block normalisation factor. Returns 0 when the block is
    /// effectively silent, so the caller can emit an empty spectrum instead of
    /// amplifying the noise floor.
    /// </summary>
    private double UpdateAutoGain(float[] samples)
    {
        double peak = 0;
        double sumSquares = 0;
        for (int i = 0; i < FftSize; i++)
        {
            double value = samples[i];
            if (!double.IsFinite(value))
            {
                continue;
            }

            double magnitude = Math.Abs(value);
            if (magnitude > peak)
            {
                peak = magnitude;
            }

            sumSquares += value * value;
        }

        if (peak < SilencePeak)
        {
            _autoGain += (AutoGainFloor - _autoGain) * GainRelease;
            return 0;
        }

        double rms = Math.Sqrt(sumSquares / FftSize);
        double desired = rms > 1e-6
            ? Math.Clamp(TargetRms / rms, AutoGainFloor, MaxAutoGain)
            : AutoGainFloor;

        double rate = desired > _autoGain ? GainAttack : GainRelease;
        _autoGain += (desired - _autoGain) * rate;
        return _autoGain;
    }

    private static double[] CreateHannWindow()
    {
        double[] window = new double[FftSize];
        for (int i = 0; i < window.Length; i++)
        {
            window[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftSize - 1)));
        }
        return window;
    }

    private void UpdateBandMapping(int sampleRate)
    {
        double maxFrequency = Math.Min(MaxFrequency, sampleRate / 2.0);
        int firstBin = Math.Max(1, (int)Math.Ceiling(MinFrequency * FftSize / sampleRate));
        int lastBin = Math.Min(FftSize / 2, (int)Math.Floor(maxFrequency * FftSize / sampleRate));
        for (int band = 0; band < BandCount; band++)
        {
            int start = Math.Clamp(FrequencyToBin(GetLogFrequency(band, maxFrequency), sampleRate), firstBin, lastBin);
            int end = Math.Max(start + 1, FrequencyToBin(GetLogFrequency(band + 1, maxFrequency), sampleRate));
            _startBins[band] = start;
            _endBins[band] = band == BandCount - 1 ? lastBin + 1 : Math.Min(end, lastBin + 1);
        }
        _mappedSampleRate = sampleRate;
    }

    private static double GetLogFrequency(int bandIndex, double maxFrequency)
    {
        double minLog = Math.Log10(MinFrequency);
        double maxLog = Math.Log10(maxFrequency);
        double position = (double)bandIndex / BandCount;
        return Math.Pow(10, minLog + (maxLog - minLog) * position);
    }

    private static int FrequencyToBin(double frequency, int sampleRate)
    {
        return (int)Math.Round(frequency * FftSize / sampleRate);
    }

    private static double GetBandWeight(int band)
    {
        if (band < 8)
        {
            return 0.58 + band * 0.045;
        }

        if (band < 16)
        {
            return 0.94 + (band - 8) * 0.012;
        }

        return 1.03;
    }
}
