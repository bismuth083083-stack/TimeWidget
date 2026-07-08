namespace TimeWidget.Services;

public sealed class SpectrumSmoother
{
    private readonly double[] _current = new double[FftSpectrumAnalyzer.BandCount];
    private readonly double[] _target = new double[FftSpectrumAnalyzer.BandCount];

    public void SetTarget(double[] target)
    {
        for (int i = 0; i < _target.Length; i++)
        {
            _target[i] = i < target.Length ? Math.Clamp(target[i], 0, 1) : 0;
        }
    }

    public double[] NextFrame()
    {
        for (int i = 0; i < _current.Length; i++)
        {
            double value = _target[i];
            _current[i] = value > _current[i]
                ? (_current[i] * 0.62) + (value * 0.38)
                : (_current[i] * 0.95) + (value * 0.05);

            if (_current[i] < 0.002)
            {
                _current[i] = 0;
            }
        }

        return _current;
    }

    public double[] Decay()
    {
        SetTarget(Array.Empty<double>());
        return NextFrame();
    }
}
