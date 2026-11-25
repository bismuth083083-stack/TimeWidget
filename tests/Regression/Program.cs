using System.IO;
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
    private static void Main()
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
            Check(Math.Abs(tint.Color.A / 255.0 - .7) < .01, "70 percent default");
            var menu = (MenuItem)window.ContextMenu.Items[^1];
            var panel = (StackPanel)((MenuItem)menu.Items[0]).Header;
            var slider = (Slider)panel.Children[1];
            slider.Value = 0;
            Check(tint.Color.A == 0 && drawing.Children.Count >= 2, "zero tint retains backing");
            slider.Value = 100;
            Check(tint.Color.A == 255 && window.Opacity == 1, "full tint keeps content opacity");
            slider.Value = 70;
            Check(window.ActualWidth == 360 && window.ActualHeight == 160, "appearance preserves size");
            var media = new MediaWidgetWindow { Width = 100, Height = 100, Content = new Border { Background = Brushes.Red } };
            media.Show();
            Check(media.ContextMenu is null, "media excluded");
            media.Close();
            window.Close();
            TimeWidget.Models.SettingsStore.Update(s => s.BackgroundOpacities.Remove(nameof(RegressionWidgetWindow)));
            app.Shutdown();
            Console.WriteLine($"{_checks} regression checks passed.");
        }
        finally { Directory.Delete(path, true); }
    }
}

internal sealed class RegressionWidgetWindow : Window { }
internal sealed class MediaWidgetWindow : Window { }
