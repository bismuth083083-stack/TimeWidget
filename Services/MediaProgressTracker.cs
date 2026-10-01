using System.Diagnostics;

namespace TimeWidget.Services;

internal enum MediaProgressMode
{
    Normal,
    Estimating,
    Unavailable
}

internal readonly record struct MediaTimelineSample(
    TimeSpan StartTime,
    TimeSpan EndTime,
    TimeSpan MinSeekTime,
    TimeSpan MaxSeekTime,
    TimeSpan Position,
    DateTimeOffset LastUpdatedTime);

internal readonly record struct MediaProgressSnapshot(
    TimeSpan Position,
    TimeSpan Duration,
    MediaProgressMode Mode)
{
    public bool IsAvailable => Duration > TimeSpan.Zero;
}

internal sealed class MediaProgressTracker
{
    private string? _mediaKey;
    private TimeSpan _duration;
    private TimeSpan _positionAnchor;
    private TimeSpan _lastSourcePosition;
    private DateTimeOffset _lastSourceUpdatedTime;
    private long _anchorTimestamp;
    private bool _hasDuration;
    private bool _hasSourcePosition;
    private bool _isPlaying;
    private double _playbackRate = 1;
    private MediaProgressMode _mode = MediaProgressMode.Unavailable;

    public void SetMedia(string mediaKey)
    {
        if (string.Equals(_mediaKey, mediaKey, StringComparison.Ordinal))
        {
            return;
        }

        Reset();
        _mediaKey = mediaKey;
    }

    public void Reset()
    {
        _mediaKey = null;
        _duration = TimeSpan.Zero;
        _positionAnchor = TimeSpan.Zero;
        _lastSourcePosition = TimeSpan.Zero;
        _lastSourceUpdatedTime = default;
        _anchorTimestamp = Stopwatch.GetTimestamp();
        _hasDuration = false;
        _hasSourcePosition = false;
        _isPlaying = false;
        _playbackRate = 1;
        _mode = MediaProgressMode.Unavailable;
    }

    public MediaProgressSnapshot Synchronize(
        MediaTimelineSample sample,
        bool isPlaying,
        double playbackRate)
    {
        long now = Stopwatch.GetTimestamp();
        TimeSpan estimatedBeforeSync = GetEstimatedPosition(now);
        _playbackRate = NormalizePlaybackRate(playbackRate);

        if (TryNormalize(sample, out TimeSpan sourcePosition, out TimeSpan sourceDuration))
        {
            if (isPlaying && sample.LastUpdatedTime != default)
            {
                TimeSpan sourceAge = DateTimeOffset.UtcNow - sample.LastUpdatedTime;
                if (sourceAge > TimeSpan.Zero && sourceAge <= TimeSpan.FromSeconds(10))
                {
                    sourcePosition = Clamp(
                        sourcePosition + TimeSpan.FromSeconds(sourceAge.TotalSeconds * _playbackRate),
                        TimeSpan.Zero,
                        sourceDuration);
                }
            }

            bool sourcePositionChanged = !_hasSourcePosition
                || Math.Abs((sourcePosition - _lastSourcePosition).TotalMilliseconds) >= 50;
            bool sourceTimestampChanged = sample.LastUpdatedTime != default
                && sample.LastUpdatedTime != _lastSourceUpdatedTime;
            bool sourceMovedBackward = _hasSourcePosition
                && sourcePosition < _lastSourcePosition - TimeSpan.FromSeconds(1);
            bool sourceIsStale = _hasSourcePosition
                && (_isPlaying || isPlaying)
                && !sourcePositionChanged
                && estimatedBeforeSync > sourcePosition + TimeSpan.FromMilliseconds(350);

            _duration = sourceDuration;
            _hasDuration = true;

            if (sourceMovedBackward && sourceTimestampChanged)
            {
                _positionAnchor = sourcePosition;
                _mode = MediaProgressMode.Normal;
            }
            else if (sourceIsStale)
            {
                _positionAnchor = Clamp(estimatedBeforeSync, TimeSpan.Zero, _duration);
                _mode = MediaProgressMode.Estimating;
            }
            else
            {
                _positionAnchor = sourcePosition;
                _mode = MediaProgressMode.Normal;
            }

            _lastSourcePosition = sourcePosition;
            _lastSourceUpdatedTime = sample.LastUpdatedTime;
            _hasSourcePosition = true;
        }
        else if (_hasDuration)
        {
            // Some players briefly publish an empty timeline while switching state.
            // Keep the last duration only for the same media key and bridge position locally.
            _positionAnchor = Clamp(estimatedBeforeSync, TimeSpan.Zero, _duration);
            _mode = MediaProgressMode.Estimating;
        }
        else
        {
            _positionAnchor = TimeSpan.Zero;
            _mode = MediaProgressMode.Unavailable;
        }

        _anchorTimestamp = now;
        _isPlaying = isPlaying;
        return GetSnapshot(now);
    }

    public MediaProgressSnapshot GetSnapshot()
    {
        return GetSnapshot(Stopwatch.GetTimestamp());
    }

    private MediaProgressSnapshot GetSnapshot(long now)
    {
        if (!_hasDuration)
        {
            return new MediaProgressSnapshot(TimeSpan.Zero, TimeSpan.Zero, MediaProgressMode.Unavailable);
        }

        return new MediaProgressSnapshot(
            Clamp(GetEstimatedPosition(now), TimeSpan.Zero, _duration),
            _duration,
            _mode);
    }

    private TimeSpan GetEstimatedPosition(long now)
    {
        if (!_isPlaying || _anchorTimestamp == 0)
        {
            return _positionAnchor;
        }

        double elapsedSeconds = (now - _anchorTimestamp) / (double)Stopwatch.Frequency;
        return _positionAnchor + TimeSpan.FromSeconds(Math.Max(0, elapsedSeconds) * _playbackRate);
    }

    private static bool TryNormalize(
        MediaTimelineSample sample,
        out TimeSpan position,
        out TimeSpan duration)
    {
        TimeSpan origin;
        TimeSpan endRange = sample.EndTime - sample.StartTime;
        TimeSpan seekRange = sample.MaxSeekTime - sample.MinSeekTime;

        if (endRange > TimeSpan.Zero)
        {
            origin = sample.StartTime;
            duration = endRange;
        }
        else if (seekRange > TimeSpan.Zero)
        {
            origin = sample.MinSeekTime;
            duration = seekRange;
        }
        else if (sample.EndTime > TimeSpan.Zero)
        {
            origin = TimeSpan.Zero;
            duration = sample.EndTime;
        }
        else if (sample.MaxSeekTime > TimeSpan.Zero)
        {
            origin = TimeSpan.Zero;
            duration = sample.MaxSeekTime;
        }
        else
        {
            position = TimeSpan.Zero;
            duration = TimeSpan.Zero;
            return false;
        }

        position = sample.Position - origin;
        if ((position < TimeSpan.Zero || position > duration)
            && sample.Position >= TimeSpan.Zero
            && sample.Position <= duration)
        {
            // A few desktop players expose a relative Position with absolute-style bounds.
            position = sample.Position;
        }

        position = Clamp(position, TimeSpan.Zero, duration);
        return duration > TimeSpan.Zero;
    }

    private static double NormalizePlaybackRate(double playbackRate)
    {
        return double.IsFinite(playbackRate) && playbackRate > 0 ? playbackRate : 1;
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan minimum, TimeSpan maximum)
    {
        if (value < minimum)
        {
            return minimum;
        }

        return value > maximum ? maximum : value;
    }
}
