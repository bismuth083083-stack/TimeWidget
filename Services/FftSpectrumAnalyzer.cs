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
    private readonly Complex[] _fftBuffer = new Complex[FftSize];
    private readonly double[] _window = CreateHannWindow();
    private readonly int[] _startBins = new int[BandCount];
    private readonly int[] _endBins = new int[BandCount];
    private int _mappedSampleRate;

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

            for (int i = 0; i < FftSize; i++)
            {
                _fftBuffer[i].X = float.IsFinite(samples[i]) ? (float)(samples[i] * _window[i]) : 0;
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
