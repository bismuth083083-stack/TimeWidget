using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TimeWidget.Models;
using TimeWidget.Services;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace TimeWidget;

public partial class MediaWidgetWindow : Window
{
    private readonly DispatcherTimer _mediaRefreshTimer;
    private readonly DispatcherTimer _progressRenderTimer;
    private readonly DispatcherTimer _spectrumAnalysisTimer;
    private readonly DispatcherTimer _spectrumRenderTimer;
    private readonly AudioSpectrumService _audioSpectrumService = new();
    private readonly FftSpectrumAnalyzer _spectrumAnalyzer = new();
    private readonly SpectrumSmoother _spectrumSmoother = new();
    private readonly MediaProgressTracker _progressTracker = new();
    private readonly SemaphoreSlim _mediaRefreshGate = new(1, 1);
    private readonly SemaphoreSlim _seekGate = new(1, 1);
    private readonly float[] _fftSamples = new float[FftSpectrumAnalyzer.FftSize];
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private WidgetSettings _settings = new();
    private bool _isClosed;
    private bool _isLoaded;
    private DateTime _lastSpectrumSampleTime = DateTime.MinValue;
    private DateTime _lastSpectrumRestartTime = DateTime.MinValue;
    private string? _lastMediaKey;
    private string? _loadedThumbnailMediaKey;
    private int _sessionGeneration;
    private bool _currentSessionCanSeek;
    private bool _isSeekDragging;
    private bool _isSeekCommitPending;
    private DateTime _lastSeekRequestTime = DateTime.MinValue;

    public MediaWidgetWindow()
    {
        InitializeComponent();

        _mediaRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _mediaRefreshTimer.Tick += async (_, _) => await RefreshRecognitionAsync(forceReset: false);

        _progressRenderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _progressRenderTimer.Tick += (_, _) => RenderProgress(_progressTracker.GetSnapshot());

        _spectrumAnalysisTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _spectrumAnalysisTimer.Tick += (_, _) =>
        {
            AnalyzeSpectrum();
        };

        _spectrumRenderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _spectrumRenderTimer.Tick += (_, _) =>
        {
            UpdateSpectrum();
        };

        _audioSpectrumService.CaptureUnavailable += AudioSpectrumService_CaptureUnavailable;
        _audioSpectrumService.CaptureAvailable += AudioSpectrumService_CaptureAvailable;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();
        SpectrumControl.HeightMultiplier = _settings.MediaSpectrumHeightMultiplier;
        _settings.MediaSpectrumHeightMultiplier = SpectrumControl.HeightMultiplier;
        SpectrumHeightSlider.Value = SpectrumControl.HeightMultiplier;
        SeekMenuItem.IsChecked = _settings.MediaSeekEnabled;
        Topmost = _settings.MediaTopmost;
        TopmostMenuItem.IsChecked = _settings.MediaTopmost;
        LockMenuItem.IsChecked = _settings.MediaIsLocked;
        ResizeMenuItem.IsChecked = _settings.MediaIsResizable;
        ApplyResizeMode();
        if (_settings.MediaWidth.HasValue && _settings.MediaHeight.HasValue)
        {
            Width = _settings.MediaWidth.Value;
            Height = _settings.MediaHeight.Value;
        }
        RestorePosition();
        UpdateSeekInteractionState();

        _audioSpectrumService.Start();
        _spectrumAnalysisTimer.Start();
        _spectrumRenderTimer.Start();

        await InitializeMediaSessionAsync();
        _mediaRefreshTimer.Start();
        _progressRenderTimer.Start();
        _isLoaded = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _isClosed = true;
        _mediaRefreshTimer.Stop();
        _progressRenderTimer.Stop();
        _spectrumAnalysisTimer.Stop();
        _spectrumRenderTimer.Stop();
        SaveSettings();
        UnsubscribeSessionManager();
        UnsubscribeCurrentSession();
        _audioSpectrumService.CaptureUnavailable -= AudioSpectrumService_CaptureUnavailable;
        _audioSpectrumService.CaptureAvailable -= AudioSpectrumService_CaptureAvailable;
        _audioSpectrumService.Dispose();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.MediaIsLocked)
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            WindowSnapService.SnapToScreen(this);
            SaveSettings();
        }
    }

    private void TopmostMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.MediaTopmost = TopmostMenuItem.IsChecked;
        Topmost = _settings.MediaTopmost;
        SaveSettings();
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.MediaIsLocked = LockMenuItem.IsChecked;
        SaveSettings();
    }

    private void ResizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.MediaIsResizable = ResizeMenuItem.IsChecked;
        ApplyResizeMode();
        SaveSettings();
    }

    private void SeekMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.MediaSeekEnabled = SeekMenuItem.IsChecked;
        if (!_settings.MediaSeekEnabled)
        {
            CancelSeekInteraction();
        }

        UpdateSeekInteractionState();
        SaveSettings();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isLoaded || !_settings.MediaIsResizable)
        {
            return;
        }

        WindowSnapService.SnapResize(this);
        SaveSettings();
    }

    private void MediaContentGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Rect roundedBounds = new(0, 0, MediaContentGrid.ActualWidth, MediaContentGrid.ActualHeight);
        RectangleGeometry roundedClip = new(roundedBounds, 22, 22);
        MediaContentGrid.Clip = roundedClip;
        AlbumBlurBackground.Clip = roundedClip.Clone();
        AlbumDimOverlay.Clip = roundedClip.Clone();
    }

    private async Task InitializeMediaSessionAsync()
    {
        try
        {
            _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _sessionManager.SessionsChanged += SessionManager_SessionsChanged;
            _sessionManager.CurrentSessionChanged += SessionManager_CurrentSessionChanged;
            UpdateCurrentSession(_sessionManager.GetCurrentSession());
            await RefreshMediaInfoAsync();
        }
        catch
        {
            ShowNoMedia();
        }
    }

    private async void SessionManager_SessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            if (!_isClosed)
            {
                UpdateCurrentSession(sender.GetCurrentSession());
                _ = RefreshMediaInfoAsync();
            }
        });
    }

    private async void SessionManager_CurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            if (!_isClosed)
            {
                UpdateCurrentSession(sender.GetCurrentSession());
                _ = RefreshMediaInfoAsync();
            }
        });
    }

    private async void CurrentSession_MediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            if (!_isClosed && ReferenceEquals(sender, _currentSession))
            {
                // Some players publish the text metadata before the thumbnail is ready.
                // Let the next refresh retry the cover even when the title did not change.
                _loadedThumbnailMediaKey = null;
                _ = RefreshMediaInfoAsync();
            }
        });
    }

    private async void CurrentSession_PlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            if (!_isClosed)
            {
                RefreshPlaybackAndTimeline(sender);
            }
        });
    }

    private async void CurrentSession_TimelinePropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            if (!_isClosed)
            {
                RefreshPlaybackAndTimeline(sender);
            }
        });
    }

    private void UpdateCurrentSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(_currentSession, session))
        {
            return;
        }

        CancelSeekInteraction();
        UnsubscribeCurrentSession();
        _currentSession = session;
        _sessionGeneration++;
        _lastMediaKey = null;
        _loadedThumbnailMediaKey = null;
        _progressTracker.Reset();

        if (_currentSession is not null)
        {
            _currentSession.MediaPropertiesChanged += CurrentSession_MediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged += CurrentSession_PlaybackInfoChanged;
            _currentSession.TimelinePropertiesChanged += CurrentSession_TimelinePropertiesChanged;
        }

        UpdateSeekInteractionState();
    }

    private async Task RefreshMediaInfoAsync()
    {
        if (_isClosed)
        {
            return;
        }

        if (!await _mediaRefreshGate.WaitAsync(0))
        {
            return;
        }

        GlobalSystemMediaTransportControlsSession? session = _currentSession;
        int generation = _sessionGeneration;

        try
        {
            if (session is null)
            {
                ShowNoMedia();
                return;
            }

            GlobalSystemMediaTransportControlsSessionMediaProperties properties =
                await session.TryGetMediaPropertiesAsync();
            if (_isClosed || generation != _sessionGeneration)
            {
                return;
            }

            GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo =
                session.GetPlaybackInfo();
            GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties =
                session.GetTimelineProperties();

            string title = string.IsNullOrWhiteSpace(properties.Title) ? "Unknown title" : properties.Title;
            string artist = string.IsNullOrWhiteSpace(properties.Artist) ? "Unknown artist" : properties.Artist;
            string source = string.IsNullOrWhiteSpace(session.SourceAppUserModelId)
                ? "Unknown source"
                : session.SourceAppUserModelId;
            string mediaKey = $"{source}\n{title}\n{artist}\n{properties.AlbumTitle}";

            _progressTracker.SetMedia(mediaKey);
            TitleText.Text = title;
            ArtistText.Text = artist;
            SourceText.Text = source;
            UpdatePlaybackUi(playbackInfo);
            SynchronizeTimeline(timelineProperties, playbackInfo);

            bool mediaChanged = !string.Equals(_lastMediaKey, mediaKey, StringComparison.Ordinal);
            if (mediaChanged)
            {
                _lastMediaKey = mediaKey;
                _loadedThumbnailMediaKey = null;
                ClearThumbnail();
            }

            if (!string.Equals(_loadedThumbnailMediaKey, mediaKey, StringComparison.Ordinal)
                && await TryUpdateThumbnailAsync(properties.Thumbnail, mediaKey, generation))
            {
                _loadedThumbnailMediaKey = mediaKey;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Media refresh failed: {ex}");
            if (_currentSession is null)
            {
                ShowNoMedia();
            }
        }
        finally
        {
            _mediaRefreshGate.Release();
        }
    }

    private async Task<bool> TryUpdateThumbnailAsync(
        IRandomAccessStreamReference? thumbnail,
        string mediaKey,
        int generation)
    {
        if (thumbnail is null)
        {
            return false;
        }

        try
        {
            using IRandomAccessStreamWithContentType randomAccessStream = await thumbnail.OpenReadAsync();
            using Stream stream = randomAccessStream.AsStreamForRead();

            BitmapImage image = new();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            if (_isClosed
                || generation != _sessionGeneration
                || !string.Equals(_lastMediaKey, mediaKey, StringComparison.Ordinal))
            {
                return false;
            }

            AlbumArtShape.Fill = new ImageBrush(image)
            {
                Stretch = Stretch.UniformToFill
            };
            AlbumBlurBackground.Fill = new ImageBrush(image)
            {
                Stretch = Stretch.UniformToFill
            };
            AlbumBlurBackground.Visibility = Visibility.Visible;
            AlbumDimOverlay.Visibility = Visibility.Visible;
            TitleText.TextBrush = Brushes.White;
            ArtistText.TextBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0xF0, 0xF6, 0xFF));
            PlaybackStatusText.Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xF0, 0xF6, 0xFF));
            SolidColorBrush albumAccent = new(FindAlbumAccentColor(image));
            albumAccent.Freeze();
            ApplyAlbumAccent(albumAccent);
            AlbumArtPlaceholder.Visibility = Visibility.Collapsed;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Album thumbnail refresh failed: {ex.Message}");
            return false;
        }
    }

    private void ClearThumbnail()
    {
        _loadedThumbnailMediaKey = null;
        AlbumArtShape.Fill = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
        AlbumBlurBackground.Fill = null;
        AlbumBlurBackground.Visibility = Visibility.Collapsed;
        AlbumDimOverlay.Visibility = Visibility.Collapsed;
        TitleText.TextBrush = (Brush)FindResource("PrimaryText");
        ArtistText.TextBrush = (Brush)FindResource("SecondaryText");
        PlaybackStatusText.Foreground = (Brush)FindResource("SecondaryText");
        ApplyAlbumAccent((Brush)FindResource("AlbumAccent"));
        AlbumArtPlaceholder.Visibility = Visibility.Visible;
    }

    private void ApplyAlbumAccent(Brush brush)
    {
        SpectrumControl.BarBrush = brush;
        MediaProgressBar.Foreground = brush;
    }

    private static Color FindAlbumAccentColor(BitmapSource source)
    {
        const byte alpha = 0xFF;
        Color baseColor = Color.FromRgb(0x1F, 0x1E, 0x33);
        Color fallback = Color.FromRgb(0xA7, 0xD8, 0xFF);

        try
        {
            BitmapSource bitmap = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            bitmap.CopyPixels(pixels, stride, 0);

            Dictionary<int, ColorBucket> buckets = [];
            int step = Math.Max(1, Math.Min(width, height) / 96);

            for (int y = 0; y < height; y += step)
            {
                int row = y * stride;
                for (int x = 0; x < width; x += step)
                {
                    int index = row + x * 4;
                    byte b = pixels[index];
                    byte g = pixels[index + 1];
                    byte r = pixels[index + 2];
                    byte a = pixels[index + 3];

                    if (a < 160)
                    {
                        continue;
                    }

                    Color color = Color.FromRgb(r, g, b);
                    double distance = ColorDistance(color, baseColor);
                    double luminance = GetLuminance(color);
                    double saturation = GetSaturation(color);
                    if (distance < 70 || luminance < 32 || saturation < 0.12)
                    {
                        continue;
                    }

                    int key = ((r & 0xF0) << 8) | ((g & 0xF0) << 4) | (b & 0xF0);
                    if (!buckets.TryGetValue(key, out ColorBucket? bucket))
                    {
                        bucket = new ColorBucket();
                        buckets[key] = bucket;
                    }

                    bucket.Count++;
                    bucket.R += r;
                    bucket.G += g;
                    bucket.B += b;
                    bucket.Distance += distance;
                    bucket.Saturation += saturation;
                }
            }

            if (buckets.Count == 0)
            {
                return Color.FromArgb(alpha, fallback.R, fallback.G, fallback.B);
            }

            ColorBucket best = buckets.Values
                .OrderByDescending(bucket => bucket.Score)
                .First();
            byte bestR = (byte)Math.Clamp(best.R / best.Count, 0, 255);
            byte bestG = (byte)Math.Clamp(best.G / best.Count, 0, 255);
            byte bestB = (byte)Math.Clamp(best.B / best.Count, 0, 255);
            return Color.FromArgb(alpha, bestR, bestG, bestB);
        }
        catch
        {
            return Color.FromArgb(alpha, fallback.R, fallback.G, fallback.B);
        }
    }

    private static double ColorDistance(Color first, Color second)
    {
        int red = first.R - second.R;
        int green = first.G - second.G;
        int blue = first.B - second.B;
        return Math.Sqrt(red * red + green * green + blue * blue);
    }

    private static double GetLuminance(Color color)
    {
        return color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722;
    }

    private static double GetSaturation(Color color)
    {
        double max = Math.Max(color.R, Math.Max(color.G, color.B)) / 255.0;
        double min = Math.Min(color.R, Math.Min(color.G, color.B)) / 255.0;
        return max <= 0 ? 0 : (max - min) / max;
    }

    private sealed class ColorBucket
    {
        public int Count { get; set; }
        public int R { get; set; }
        public int G { get; set; }
        public int B { get; set; }
        public double Distance { get; set; }
        public double Saturation { get; set; }

        public double Score => Count * (1 + Distance / Math.Max(1, Count * 255.0)) * (0.75 + Saturation / Math.Max(1, Count) * 0.65);
    }

    private void RefreshPlaybackAndTimeline(GlobalSystemMediaTransportControlsSession session)
    {
        if (_isClosed || !ReferenceEquals(session, _currentSession))
        {
            return;
        }

        try
        {
            GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo = session.GetPlaybackInfo();
            UpdatePlaybackUi(playbackInfo);
            SynchronizeTimeline(session.GetTimelineProperties(), playbackInfo);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Media timeline refresh failed: {ex}");
        }
    }

    private void UpdatePlaybackUi(GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo)
    {
        PlaybackStatusText.Text = GetPlaybackStatusText(playbackInfo.PlaybackStatus);
        PlayPauseButton.Content = playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
            ? "\uE769"
            : "\uE768";
        _currentSessionCanSeek = playbackInfo.Controls.IsPlaybackPositionEnabled;
        UpdateSeekInteractionState();
    }

    private void SynchronizeTimeline(
        GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo)
    {
        MediaTimelineSample sample = new(
            timelineProperties.StartTime,
            timelineProperties.EndTime,
            timelineProperties.MinSeekTime,
            timelineProperties.MaxSeekTime,
            timelineProperties.Position,
            timelineProperties.LastUpdatedTime);
        bool isPlaying = playbackInfo.PlaybackStatus
            == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        double playbackRate = playbackInfo.PlaybackRate ?? 1;

        RenderProgress(_progressTracker.Synchronize(sample, isPlaying, playbackRate));
    }

    private void RenderProgress(MediaProgressSnapshot progress)
    {
        if (_isClosed)
        {
            return;
        }

        if (_isSeekDragging || _isSeekCommitPending)
        {
            return;
        }

        if (!progress.IsAvailable)
        {
            PositionText.Text = "00:00";
            DurationText.Text = "--:--";
            MediaProgressBar.IsIndeterminate = false;
            MediaProgressBar.Opacity = 1;
            MediaProgressBar.Value = 0;
            UpdateSeekInteractionState();
            return;
        }

        MediaProgressBar.IsIndeterminate = false;
        MediaProgressBar.Opacity = 1;
        PositionText.Text = FormatMediaTime(progress.Position);
        DurationText.Text = FormatMediaTime(progress.Duration);
        MediaProgressBar.Value = Math.Clamp(
            progress.Position.TotalMilliseconds / progress.Duration.TotalMilliseconds,
            0,
            1);
        UpdateSeekInteractionState();
    }

    private void MediaProgressBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanSeekCurrentMedia())
        {
            return;
        }

        _isSeekDragging = true;
        MediaProgressBar.CaptureMouse();
        double ratio = GetSeekRatio(e.GetPosition(MediaProgressBar).X);
        RenderSeekPreview(ratio);
        _ = TrySeekAsync(ratio, waitForPrevious: false);
        e.Handled = true;
    }

    private void MediaProgressBar_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSeekDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        double ratio = GetSeekRatio(e.GetPosition(MediaProgressBar).X);
        RenderSeekPreview(ratio);
        if (DateTime.UtcNow - _lastSeekRequestTime >= TimeSpan.FromMilliseconds(150))
        {
            _lastSeekRequestTime = DateTime.UtcNow;
            _ = TrySeekAsync(ratio, waitForPrevious: false);
        }

        e.Handled = true;
    }

    private async void MediaProgressBar_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSeekDragging)
        {
            return;
        }

        double ratio = GetSeekRatio(e.GetPosition(MediaProgressBar).X);
        RenderSeekPreview(ratio);
        _isSeekDragging = false;
        _isSeekCommitPending = true;
        MediaProgressBar.ReleaseMouseCapture();
        e.Handled = true;

        bool changed = await TrySeekAsync(ratio, waitForPrevious: true);
        _isSeekCommitPending = false;
        if (_isClosed)
        {
            return;
        }

        if (changed && _currentSession is not null)
        {
            await Task.Delay(120);
            RefreshPlaybackAndTimeline(_currentSession);
        }
        else
        {
            RenderProgress(_progressTracker.GetSnapshot());
        }
    }

    private void MediaProgressBar_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_isSeekDragging)
        {
            return;
        }

        _isSeekDragging = false;
        RenderProgress(_progressTracker.GetSnapshot());
    }

    private async Task<bool> TrySeekAsync(double ratio, bool waitForPrevious)
    {
        bool entered = waitForPrevious
            ? await _seekGate.WaitAsync(TimeSpan.FromSeconds(1))
            : await _seekGate.WaitAsync(0);
        if (!entered)
        {
            return false;
        }

        try
        {
            GlobalSystemMediaTransportControlsSession? session = _currentSession;
            if (_isClosed || session is null || !CanSeekCurrentMedia())
            {
                return false;
            }

            TimeSpan? target = GetAbsoluteSeekPosition(session, ratio);
            if (!target.HasValue)
            {
                return false;
            }

            return await session.TryChangePlaybackPositionAsync(target.Value.Ticks).AsTask();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Media seek failed: {ex.Message}");
            return false;
        }
        finally
        {
            _seekGate.Release();
        }
    }

    private bool CanSeekCurrentMedia()
    {
        return _settings.MediaSeekEnabled
            && _currentSession is not null
            && _currentSessionCanSeek
            && _progressTracker.GetSnapshot().IsAvailable;
    }

    private static TimeSpan? GetAbsoluteSeekPosition(
        GlobalSystemMediaTransportControlsSession session,
        double ratio)
    {
        GlobalSystemMediaTransportControlsSessionTimelineProperties timeline = session.GetTimelineProperties();
        TimeSpan start;
        TimeSpan duration;
        if (timeline.EndTime > timeline.StartTime)
        {
            start = timeline.StartTime;
            duration = timeline.EndTime - timeline.StartTime;
        }
        else if (timeline.MaxSeekTime > timeline.MinSeekTime)
        {
            start = timeline.MinSeekTime;
            duration = timeline.MaxSeekTime - timeline.MinSeekTime;
        }
        else
        {
            return null;
        }

        return start + TimeSpan.FromTicks((long)(duration.Ticks * Math.Clamp(ratio, 0, 1)));
    }

    private double GetSeekRatio(double pointerX)
    {
        return MediaProgressBar.ActualWidth <= 0
            ? 0
            : Math.Clamp(pointerX / MediaProgressBar.ActualWidth, 0, 1);
    }

    private void RenderSeekPreview(double ratio)
    {
        MediaProgressSnapshot progress = _progressTracker.GetSnapshot();
        if (!progress.IsAvailable)
        {
            return;
        }

        TimeSpan position = TimeSpan.FromTicks((long)(progress.Duration.Ticks * Math.Clamp(ratio, 0, 1)));
        PositionText.Text = FormatMediaTime(position);
        DurationText.Text = FormatMediaTime(progress.Duration);
        MediaProgressBar.Value = Math.Clamp(ratio, 0, 1);
    }

    private void UpdateSeekInteractionState()
    {
        bool hasTimeline = _progressTracker.GetSnapshot().IsAvailable;
        bool enabled = _settings.MediaSeekEnabled && _currentSessionCanSeek && hasTimeline;
        MediaProgressBar.Cursor = enabled ? Cursors.Hand : Cursors.Arrow;
        MediaProgressBar.ToolTip = !_settings.MediaSeekEnabled
            ? "Enable experimental progress seeking from the right-click menu."
            : !_currentSessionCanSeek
                ? "The current player does not expose playback seeking to Windows."
                : hasTimeline
                    ? "Click or drag to change playback position."
                    : "The current player has not provided a usable duration.";
    }

    private void CancelSeekInteraction()
    {
        _isSeekDragging = false;
        _isSeekCommitPending = false;
        if (MediaProgressBar.IsMouseCaptured)
        {
            MediaProgressBar.ReleaseMouseCapture();
        }
    }

    private static string FormatMediaTime(TimeSpan time)
    {
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        await TryControlSessionAsync(session => session.TrySkipPreviousAsync().AsTask());
    }

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        await TryControlSessionAsync(session => session.TryTogglePlayPauseAsync().AsTask());
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        await TryControlSessionAsync(session => session.TrySkipNextAsync().AsTask());
    }

    private async Task TryControlSessionAsync(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action)
    {
        try
        {
            GlobalSystemMediaTransportControlsSession? session = _sessionManager?.GetCurrentSession() ?? _currentSession;
            if (session is not null)
            {
                await action(session);
                await RefreshMediaInfoAsync();
            }
        }
        catch
        {
        }
    }

    private void AnalyzeSpectrum()
    {
        if (_isClosed)
        {
            return;
        }

        try
        {
            bool hasSamples = _audioSpectrumService.TryGetLatestSamples(FftSpectrumAnalyzer.FftSize, _fftSamples);
            if (hasSamples)
            {
                _lastSpectrumSampleTime = DateTime.UtcNow;
            }
            else
            {
                RestartSpectrumCaptureIfStale();
            }

            double[] spectrum = hasSamples
                ? _spectrumAnalyzer.Analyze(_fftSamples, _audioSpectrumService.SampleRate)
                : Array.Empty<double>();
            _spectrumSmoother.SetTarget(spectrum);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Spectrum analysis failed: {ex}");
            _spectrumSmoother.SetTarget(Array.Empty<double>());
        }
    }

    private void UpdateSpectrum()
    {
        if (_isClosed)
        {
            return;
        }

        try
        {
            SpectrumControl.Values = _spectrumSmoother.NextFrame();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Spectrum render update failed: {ex}");
            SpectrumControl.Values = _spectrumSmoother.Decay();
        }
    }

    private void AudioSpectrumService_CaptureUnavailable(object? sender, string message)
    {
        if (_isClosed || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (!_isClosed)
            {
                AudioStatusText.Text = message;
                AudioStatusText.Visibility = Visibility.Visible;
            }
        });
    }

    private void ShowNoMedia()
    {
        CancelSeekInteraction();
        _currentSessionCanSeek = false;
        _lastMediaKey = null;
        _loadedThumbnailMediaKey = null;
        _progressTracker.Reset();
        TitleText.Text = "No media playing";
        ArtistText.Text = "Unknown artist";
        SourceText.Text = "No source";
        PlaybackStatusText.Text = "Stopped";
        PlayPauseButton.Content = "\uE768";
        PositionText.Text = "00:00";
        DurationText.Text = "--:--";
        MediaProgressBar.IsIndeterminate = false;
        MediaProgressBar.Opacity = 1;
        MediaProgressBar.Value = 0;
        UpdateSeekInteractionState();
        ClearThumbnail();
    }

    private static string GetPlaybackStatusText(GlobalSystemMediaTransportControlsSessionPlaybackStatus status)
    {
        return status switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => "Playing",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => "Paused",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => "Stopped",
            _ => "Stopped"
        };
    }

    private void CloseMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SpectrumHeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // ValueChanged also fires while InitializeComponent is creating the controls.
        if (!_isLoaded || _isClosed) return;
        _settings.MediaSpectrumHeightMultiplier = Math.Round(e.NewValue, 1);
        SpectrumControl.HeightMultiplier = _settings.MediaSpectrumHeightMultiplier;
    }

    private void ResetSpectrumHeightMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SpectrumHeightSlider.Value = 1.0;
    }

    private void SpectrumHeightMenuItem_SubmenuClosed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded || _isClosed) return;
        try
        {
            SettingsStore.Update(settings => settings.MediaSpectrumHeightMultiplier = _settings.MediaSpectrumHeightMultiplier);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Spectrum height setting save failed: {ex.Message}");
        }
    }

    private async void RefreshRecognitionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RefreshRecognitionAsync(forceReset: true);
    }

    private async Task RefreshRecognitionAsync(bool forceReset)
    {
        try
        {
            if (_isClosed)
            {
                return;
            }

            if (_sessionManager is null)
            {
                await InitializeMediaSessionAsync();
                return;
            }

            UpdateCurrentSession(_sessionManager.GetCurrentSession());
            if (forceReset)
            {
                _lastMediaKey = null;
                _loadedThumbnailMediaKey = null;
                _progressTracker.Reset();
            }

            await RefreshMediaInfoAsync();
        }
        catch
        {
            ShowNoMedia();
        }
    }

    private void RestorePosition()
    {
        if (_settings.MediaLeft.HasValue && _settings.MediaTop.HasValue)
        {
            Left = _settings.MediaLeft.Value;
            Top = _settings.MediaTop.Value;
            KeepWindowOnScreen();
        }
    }

    private void SaveSettings()
    {
        SettingsStore.Update(settings =>
        {
            settings.MediaLeft = Left;
            settings.MediaTop = Top;
            settings.MediaWidth = Width;
            settings.MediaHeight = Height;
            settings.MediaTopmost = Topmost;
            settings.MediaIsLocked = LockMenuItem.IsChecked;
            settings.MediaIsResizable = ResizeMenuItem.IsChecked;
            settings.MediaSeekEnabled = SeekMenuItem.IsChecked;
            settings.MediaSpectrumHeightMultiplier = _settings.MediaSpectrumHeightMultiplier;
        });
    }

    private void AudioSpectrumService_CaptureAvailable(object? sender, EventArgs e)
    {
        if (_isClosed || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (!_isClosed)
            {
                AudioStatusText.Text = string.Empty;
                AudioStatusText.Visibility = Visibility.Collapsed;
            }
        });
    }

    private void RestartSpectrumCaptureIfStale()
    {
        DateTime now = DateTime.UtcNow;
        if (_lastSpectrumSampleTime == DateTime.MinValue)
        {
            _lastSpectrumSampleTime = now;
            return;
        }

        if ((now - _lastSpectrumSampleTime).TotalSeconds < 5
            || (now - _lastSpectrumRestartTime).TotalSeconds < 8)
        {
            return;
        }

        _lastSpectrumRestartTime = now;
        _audioSpectrumService.Restart();
    }

    private void KeepWindowOnScreen()
    {
        WindowSnapService.KeepWindowOnScreen(this);
    }

    private void ApplyResizeMode()
    {
        MinWidth = 320;
        MinHeight = 220;
        ResizeMode = _settings.MediaIsResizable ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
    }

    private void UnsubscribeSessionManager()
    {
        if (_sessionManager is null)
        {
            return;
        }

        _sessionManager.SessionsChanged -= SessionManager_SessionsChanged;
        _sessionManager.CurrentSessionChanged -= SessionManager_CurrentSessionChanged;
        _sessionManager = null;
    }

    private void UnsubscribeCurrentSession()
    {
        if (_currentSession is null)
        {
            return;
        }

        _currentSession.MediaPropertiesChanged -= CurrentSession_MediaPropertiesChanged;
        _currentSession.PlaybackInfoChanged -= CurrentSession_PlaybackInfoChanged;
        _currentSession.TimelinePropertiesChanged -= CurrentSession_TimelinePropertiesChanged;
        _currentSession = null;
    }
}
