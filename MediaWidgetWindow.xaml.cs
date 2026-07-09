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
    private readonly DispatcherTimer _spectrumAnalysisTimer;
    private readonly DispatcherTimer _spectrumRenderTimer;
    private readonly AudioSpectrumService _audioSpectrumService = new();
    private readonly FftSpectrumAnalyzer _spectrumAnalyzer = new();
    private readonly SpectrumSmoother _spectrumSmoother = new();
    private readonly float[] _fftSamples = new float[FftSpectrumAnalyzer.FftSize];
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private WidgetSettings _settings = new();
    private bool _isClosed;
    private bool _isLoaded;
    private DateTime _lastSpectrumSampleTime = DateTime.MinValue;
    private DateTime _lastSpectrumRestartTime = DateTime.MinValue;

    public MediaWidgetWindow()
    {
        InitializeComponent();

        _mediaRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _mediaRefreshTimer.Tick += async (_, _) => await RefreshMediaInfoAsync();

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

        _audioSpectrumService.Start();
        _spectrumAnalysisTimer.Start();
        _spectrumRenderTimer.Start();

        await InitializeMediaSessionAsync();
        _mediaRefreshTimer.Start();
        _isLoaded = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _isClosed = true;
        _mediaRefreshTimer.Stop();
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
            if (!_isClosed)
            {
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
                _ = RefreshMediaInfoAsync();
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
                _ = RefreshMediaInfoAsync();
            }
        });
    }

    private void UpdateCurrentSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(_currentSession, session))
        {
            return;
        }

        UnsubscribeCurrentSession();
        _currentSession = session;

        if (_currentSession is not null)
        {
            _currentSession.MediaPropertiesChanged += CurrentSession_MediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged += CurrentSession_PlaybackInfoChanged;
            _currentSession.TimelinePropertiesChanged += CurrentSession_TimelinePropertiesChanged;
        }
    }

    private async Task RefreshMediaInfoAsync()
    {
        if (_isClosed)
        {
            return;
        }

        try
        {
            if (_sessionManager is not null)
            {
                UpdateCurrentSession(_sessionManager.GetCurrentSession());
            }

            if (_currentSession is null)
            {
                ShowNoMedia();
                return;
            }

            GlobalSystemMediaTransportControlsSessionMediaProperties properties =
                await _currentSession.TryGetMediaPropertiesAsync();
            GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo =
                _currentSession.GetPlaybackInfo();
            GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties =
                _currentSession.GetTimelineProperties();

            TitleText.Text = string.IsNullOrWhiteSpace(properties.Title) ? "Unknown title" : properties.Title;
            ArtistText.Text = string.IsNullOrWhiteSpace(properties.Artist) ? "Unknown artist" : properties.Artist;
            SourceText.Text = string.IsNullOrWhiteSpace(_currentSession.SourceAppUserModelId)
                ? "Unknown source"
                : _currentSession.SourceAppUserModelId;
            PlaybackStatusText.Text = GetPlaybackStatusText(playbackInfo.PlaybackStatus);
            PlayPauseButton.Content = playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                ? "\uE769"
                : "\uE768";
            UpdateTimeline(timelineProperties);

            await UpdateThumbnailAsync(properties.Thumbnail);
        }
        catch
        {
            UpdateCurrentSession(null);
            ShowNoMedia();
        }
    }

    private async Task UpdateThumbnailAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null)
        {
            ClearThumbnail();
            return;
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
        }
        catch
        {
            ClearThumbnail();
        }
    }

    private void ClearThumbnail()
    {
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

    private void UpdateTimeline(GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties)
    {
        TimeSpan duration = timelineProperties.EndTime - timelineProperties.StartTime;
        TimeSpan position = timelineProperties.Position - timelineProperties.StartTime;

        if (duration <= TimeSpan.Zero)
        {
            PositionText.Text = "00:00";
            DurationText.Text = "--:--";
            MediaProgressBar.Value = 0;
            return;
        }

        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }

        if (position > duration)
        {
            position = duration;
        }

        PositionText.Text = FormatMediaTime(position);
        DurationText.Text = FormatMediaTime(duration);
        MediaProgressBar.Value = Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
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
        TitleText.Text = "No media playing";
        ArtistText.Text = "Unknown artist";
        SourceText.Text = "No source";
        PlaybackStatusText.Text = "Stopped";
        PlayPauseButton.Content = "\uE768";
        PositionText.Text = "00:00";
        DurationText.Text = "--:--";
        MediaProgressBar.Value = 0;
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
