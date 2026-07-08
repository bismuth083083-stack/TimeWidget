using System.Diagnostics;
using NAudio.Dsp;

namespace TimeWidget.Services;

public sealed class FftSpectrumAnalyzer
{
    public const int FftSize = 2048;
    public const int BandCount = 32;
    private const int FftPower = 11;
    private const double MinFrequency = 40;
    private const double Gain = 72;

    public double[] Analyze(float[] samples, int sampleRate)
    {
        if (samples.Length < FftSize || sampleRate <= 0)
        {
            return new double[BandCount];
        }

        double[] bands = new double[BandCount];

        try
        {
            Complex[] fftBuffer = new Complex[FftSize];

            for (int i = 0; i < FftSize; i++)
            {
                double window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftSize - 1)));
                fftBuffer[i].X = (float)(samples[i] * window);
                fftBuffer[i].Y = 0;
            }

            FastFourierTransform.FFT(true, FftPower, fftBuffer);
            double maxFrequency = Math.Min(16000, sampleRate / 2.0);

            for (int band = 0; band < BandCount; band++)
            {
                double startFrequency = GetLogFrequency(band, maxFrequency);
                double endFrequency = GetLogFrequency(band + 1, maxFrequency);
                int startBin = Math.Max(1, FrequencyToBin(startFrequency, sampleRate));
                int endBin = Math.Max(startBin + 1, FrequencyToBin(endFrequency, sampleRate));
                endBin = Math.Min(endBin, FftSize / 2);

                double peak = 0;
                double sum = 0;
                int count = 0;

                for (int bin = startBin; bin < endBin; bin++)
                {
                    double real = fftBuffer[bin].X;
                    double imaginary = fftBuffer[bin].Y;
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
