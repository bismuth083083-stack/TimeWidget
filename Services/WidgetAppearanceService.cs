using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TimeWidget.Models;

namespace TimeWidget.Services;

/// <summary>Wallpaper-backed frosted backgrounds; content opacity is never changed.</summary>
public static class WidgetAppearanceService
{
    private static readonly Lazy<Task<BitmapSource?>> Wallpaper = new(LoadBlurredWallpaperAsync);
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window || !ReferenceEquals(e.OriginalSource, window)) return;
        string key = window.GetType().Name;
        if (key == nameof(MediaWidgetWindow)
            || (key != nameof(MainWindow) && !key.EndsWith("WidgetWindow", StringComparison.Ordinal))) return;
        if (window.Content is Border border) _ = new Appearance(window, border, key);
    }

    private sealed class Appearance
    {
        private readonly Window _window;
        private readonly Border _border;
        private readonly string _key;
        private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(120) };
        private readonly SolidColorBrush _tint = new(Color.FromRgb(0x1F, 0x1E, 0x33));
        private BitmapSource? _wallpaper;
        private double _opacity;
        private bool _closed;
        private bool _applying;
        private DrawingBrush? _currentBackground;
        private readonly DependencyPropertyDescriptor _backgroundDescriptor =
            DependencyPropertyDescriptor.FromProperty(Border.BackgroundProperty, typeof(Border));

        public Appearance(Window window, Border border, string key)
        {
            _window = window;
            _border = border;
            _key = key;
            var saved = SettingsStore.Load().BackgroundOpacities;
            _opacity = saved is not null && saved.TryGetValue(key, out double value) && double.IsFinite(value)
                ? Math.Clamp(value, 0, 100) : 70;
            Slider slider = new() { Minimum = 0, Maximum = 100, Value = _opacity, Width = 180,
                TickFrequency = 1, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true };
            TextBlock label = new() { Text = $"{_opacity:0}%", Margin = new Thickness(0,0,0,6) };
            StackPanel panel = new() { Margin = new Thickness(8) };
            panel.Children.Add(label);
            panel.Children.Add(slider);
            MenuItem item = new() { Header = "Background opacity" };
            item.Items.Add(new MenuItem { Header = panel, StaysOpenOnClick = true });
            window.ContextMenu ??= new ContextMenu();
            window.ContextMenu.Items.Add(new Separator());
            window.ContextMenu.Items.Add(item);
            slider.ValueChanged += (_, _) =>
            {
                _opacity = slider.Value;
                label.Text = $"{_opacity:0}%";
                _tint.Color = Color.FromArgb((byte)Math.Round(255 * _opacity / 100), 0x1F, 0x1E, 0x33);
            };
            item.SubmenuClosed += (_, _) => Save();
            window.LocationChanged += OnGeometryChanged;
            window.SizeChanged += OnGeometryChanged;
            window.Closed += OnClosed;
            _backgroundDescriptor.AddValueChanged(border, OnBackgroundChanged);
            _debounce.Tick += (_, _) => { _debounce.Stop(); Apply(); };
            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            _wallpaper = await Wallpaper.Value;
            if (!_closed) Apply();
        }

        private void OnGeometryChanged(object? sender, EventArgs e)
        {
            _debounce.Stop();
            _debounce.Start();
        }

        private void OnBackgroundChanged(object? sender, EventArgs e)
        {
            // Timer completion flashes must not replace the frosted backing with an opaque brush.
            if (!_applying && !_closed && _wallpaper is not null
                && !ReferenceEquals(_border.Background, _currentBackground)) Apply();
        }

        private void Apply()
        {
            if (_closed || _border.ActualWidth <= 0 || _border.ActualHeight <= 0) return;
            Rect bounds = new(0, 0, _border.ActualWidth, _border.ActualHeight);
            DrawingGroup drawing = new();
            using (var dc = drawing.Open())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1F,0x1E,0x33)), null, bounds);
                if (_wallpaper is not null)
                {
                    Point offset = _border.TranslatePoint(new Point(), _window);
                    ImageBrush wallpaper = new(_wallpaper)
                    {
                        Stretch = Stretch.Fill,
                        ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                        Viewbox = new Rect(
                            (_window.Left + offset.X - SystemParameters.VirtualScreenLeft) / SystemParameters.VirtualScreenWidth,
                            (_window.Top + offset.Y - SystemParameters.VirtualScreenTop) / SystemParameters.VirtualScreenHeight,
                            bounds.Width / SystemParameters.VirtualScreenWidth,
                            bounds.Height / SystemParameters.VirtualScreenHeight)
                    };
                    dc.DrawRectangle(wallpaper, null, bounds);
                }
                _tint.Color = Color.FromArgb((byte)Math.Round(255 * _opacity / 100), 0x1F, 0x1E, 0x33);
                dc.DrawRectangle(_tint, null, bounds);
            }
            _applying = true;
            try
            {
                _currentBackground = new DrawingBrush(drawing) { Stretch = Stretch.Fill };
                _border.Background = _currentBackground;
            }
            finally { _applying = false; }
        }

        private void Save()
        {
            SettingsStore.Update(s =>
            {
                s.BackgroundOpacities ??= new();
                s.BackgroundOpacities[_key] = _opacity;
            });
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _closed = true;
            _debounce.Stop();
            _window.LocationChanged -= OnGeometryChanged;
            _window.SizeChanged -= OnGeometryChanged;
            _window.Closed -= OnClosed;
            _backgroundDescriptor.RemoveValueChanged(_border, OnBackgroundChanged);
            Save();
        }
    }

    private static Task<BitmapSource?> LoadBlurredWallpaperAsync()
    {
        TaskCompletionSource<BitmapSource?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread worker = new(() =>
        {
            try
            {
                StringBuilder path = new(32768);
                SystemParametersInfo(0x73, (uint)path.Capacity, path, 0);
                string wallpaperPath = path.ToString();
                if (!File.Exists(wallpaperPath))
                    wallpaperPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
                if (!File.Exists(wallpaperPath))
                {
                    result.SetResult(null);
                    return;
                }
                BitmapImage image = new();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 1536;
                image.UriSource = new Uri(wallpaperPath, UriKind.Absolute);
                image.EndInit();
                DrawingVisual visual = new() { Effect = new BlurEffect { Radius = 32, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality } };
                // Extend edges beyond the viewport so blur never introduces transparent seams.
                using (var dc = visual.RenderOpen())
                    dc.DrawImage(image, new Rect(-48, -48, image.PixelWidth + 96, image.PixelHeight + 96));
                RenderTargetBitmap blurred = new(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
                blurred.Render(visual);
                blurred.Freeze();
                result.SetResult(blurred);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Wallpaper blur unavailable: {ex.Message}");
                result.TrySetResult(null);
            }
        }) { IsBackground = true, Name = "Widget wallpaper blur" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return result.Task;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, StringBuilder value, uint flags);
}
