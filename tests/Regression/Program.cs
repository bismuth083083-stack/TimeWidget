using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TimeWidget.Services;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        _checks++;
    }

    [STAThread]
    private static void Main(string[] args)
    {
        string path = Path.Combine(Path.GetTempPath(), "TimeWidget-test-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        try
        {
            Directory.CreateDirectory(Path.Combine(path, "child"));
            File.WriteAllText(Path.Combine(path, "a file.txt"), "a");
            File.WriteAllText(Path.Combine(path, "z.txt"), "zzzz");
            File.WriteAllText(Path.Combine(path, "child", "nested.txt"), "not recursive");
            var items = FolderListingService.Read(path, "NameAscending", true, default);
            Check(items.Count == 3 && items[0].IsDirectory, "directories first, no recursion");
            Check(items[1].FileName == "a file.txt", "name ascending");
            Check(FolderListingService.Read(path, "NameDescending", false, default)[0].FileName == "z.txt", "name descending and hide folders");
            Check(FolderListingService.Read(path, "SizeDescending", false, default)[0].SizeInBytes == 4, "size descending");
            Check(FolderListingService.Read(path, "SizeAscending", false, default)[0].SizeInBytes == 1, "size ascending");
            Check(FolderListingService.Read(Path.Combine(path,"child"), "ModifiedDescending", true, default).Count == 1, "directory navigation scan");
            Directory.CreateDirectory(Path.Combine(path,"empty"));
            Check(FolderListingService.Read(Path.Combine(path,"empty"), "TypeAscending", true, default).Count == 0, "empty folder");
            try { FolderListingService.Read(path,"NameAscending",true,new CancellationToken(true)); throw new Exception("not cancelled"); }
            catch (OperationCanceledException) { _checks++; }

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            WidgetAppearanceService.Initialize();
            var window = new RegressionWidgetWindow { Width = 360, Height = 160,
                Content = new Border { CornerRadius = new CornerRadius(22), Background = Brushes.Black,
                    Child = new TextBlock { Text = "Frosted background", Foreground = Brushes.White } } };
            window.Show();
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_,_) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
            var border = (Border)window.Content;
            Check(border.Background is DrawingBrush, "shared appearance attached");
            var drawing = (DrawingGroup)((DrawingBrush)border.Background).Drawing;
            Check(drawing.Children.Count == 3, "Gaussian wallpaper backing loaded");
            var tint = (SolidColorBrush)((GeometryDrawing)drawing.Children[^1]).Brush;
            Check(Math.Abs(tint.Color.A / 255.0 - .5) < .01, "50 percent default");
            Check(border.BorderBrush is LinearGradientBrush edge
                && edge.StartPoint == new Point(1, 0) && edge.EndPoint == new Point(0, 1)
                && edge.GradientStops[0].Color.A > edge.GradientStops[^1].Color.A,
                "border highlight fades from upper right to lower left");
            var menu = window.ContextMenu.Items.OfType<MenuItem>().Single(x => Equals(x.Header, "Background opacity"));
            var panel = (StackPanel)((MenuItem)menu.Items[0]).Header;
            var slider = (Slider)panel.Children[1];
            slider.Value = 0;
            Check(tint.Color.A == 0 && drawing.Children.Count >= 2, "zero tint retains backing");
            slider.Value = 100;
            Check(tint.Color.A == 255 && window.Opacity == 1, "full tint keeps content opacity");
            slider.Value = 50;
            Check(window.ActualWidth == 360 && window.ActualHeight == 160, "appearance preserves size");
            var media = new MediaWidgetWindow { Width = 100, Height = 100, Content = new Border { Background = Brushes.Red } };
            media.Show();
            Check(media.ContextMenu is null, "media excluded");
            Check(((Border)media.Content).BorderBrush is LinearGradientBrush, "media shares directional border");
            media.Close();
            window.Close();
            if (args.Contains("--live")) VerifyLiveBlur();
            TimeWidget.Models.SettingsStore.Update(s => s.BackgroundOpacities.Remove(nameof(RegressionWidgetWindow)));
            app.Shutdown();
            Console.WriteLine($"{_checks} regression checks passed.");
        }
        finally { Directory.Delete(path, true); }
    }

    private static void VerifyLiveBlur()
    {
        var colors = new Grid();
        colors.ColumnDefinitions.Add(new ColumnDefinition());
        colors.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new Border { Background = Brushes.Red };
        var right = new Border { Background = Brushes.Blue };
        Grid.SetColumn(right, 1);
        colors.Children.Add(left);
        colors.Children.Add(right);
        var behind = new Window { Left = 80, Top = 100, Width = 600, Height = 420, Content = colors,
            WindowStyle = WindowStyle.None, Topmost = true, ShowInTaskbar = false };
        var card = new Border { CornerRadius = new CornerRadius(22), Background = Brushes.Black };
        var widget = new RegressionWidgetWindow { Left = 200, Top = 220, Width = 360, Height = 160,
            Content = card, AllowsTransparency = true, Background = Brushes.Transparent,
            WindowStyle = WindowStyle.None, Topmost = true, ShowInTaskbar = false };
        behind.Show();
        widget.Show();
        try
        {
            var items = widget.ContextMenu.Items.OfType<MenuItem>().ToList();
            var live = items.Single(x => Equals(x.Header, "Live background blur"));
            var opacity = items.Single(x => Equals(x.Header, "Background opacity"));
            var panel = (StackPanel)((MenuItem)opacity.Items[0]).Header;
            ((Slider)panel.Children[1]).Value = 0;
            live.IsChecked = true;
            live.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(live.IsChecked, "Windows accepted live blur");
            RenderDesktop();
            Point point = card.PointToScreen(new Point(179, 80));
            Color mixed = Sample(point);
            Color red = Sample(card.PointToScreen(new Point(70, 80)));
            Console.WriteLine($"Live blur samples: boundary={mixed}, left={red}");
            Check(mixed.R > 12 && mixed.B > 12, "background boundary is blurred");
            left.Background = Brushes.Lime;
            RenderDesktop();
            Color green = Sample(card.PointToScreen(new Point(70, 80)));
            Check(red.R > red.G && green.G > green.R, "background updates with other windows");
            live.IsChecked = false;
            live.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(!live.IsChecked, "switch back to wallpaper blur");
        }
        finally
        {
            widget.Close();
            behind.Close();
            TimeWidget.Models.SettingsStore.Update(s => s.LiveBlurWidgets.Remove(nameof(RegressionWidgetWindow)));
        }
    }

    private static void RenderDesktop()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static Color Sample(Point point)
    {
        IntPtr dc = GetDC(IntPtr.Zero);
        try
        {
            uint value = GetPixel(dc, (int)point.X, (int)point.Y);
            if (value == uint.MaxValue) throw new InvalidOperationException("Desktop pixel unavailable");
            return Color.FromRgb((byte)value, (byte)(value >> 8), (byte)(value >> 16));
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
}

internal sealed class RegressionWidgetWindow : Window { }
internal sealed class MediaWidgetWindow : Window { }
