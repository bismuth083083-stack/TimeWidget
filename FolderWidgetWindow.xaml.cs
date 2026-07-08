using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using TimeWidget.Models;
using TimeWidget.Services;

namespace TimeWidget;

public partial class FolderWidgetWindow : Window
{
    private readonly DispatcherTimer _refreshDebounceTimer;
    private FileSystemWatcher? _watcher;
    private WidgetSettings _settings = new();
    private string? _currentFolderPath;
    private int _scanVersion;
    private bool _isClosed;
    private bool _isLoaded;

    public ObservableCollection<FolderFileItem> Files { get; } = [];

    public FolderWidgetWindow()
    {
        InitializeComponent();
        DataContext = this;

        _refreshDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _refreshDebounceTimer.Tick += (_, _) =>
        {
            _refreshDebounceTimer.Stop();
            ScanCurrentFolder();
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();
        Topmost = _settings.FolderTopmost;
        TopmostMenuItem.IsChecked = _settings.FolderTopmost;
        LockMenuItem.IsChecked = _settings.FolderIsLocked;
        ResizeMenuItem.IsChecked = _settings.FolderIsResizable;
        ApplyResizeMode();
        if (_settings.FolderWidth.HasValue && _settings.FolderHeight.HasValue)
        {
            Width = _settings.FolderWidth.Value;
            Height = _settings.FolderHeight.Value;
        }
        RestorePosition();

        if (!string.IsNullOrWhiteSpace(_settings.FolderPath))
        {
            SetCurrentFolder(_settings.FolderPath);
        }
        else
        {
            ShowStatus("Choose a folder to see recently modified files.");
        }

        _isLoaded = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _isClosed = true;
        _refreshDebounceTimer.Stop();
        SaveSettings();
        DisposeWatcher();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.FolderIsLocked)
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
        _settings.FolderTopmost = TopmostMenuItem.IsChecked;
        Topmost = _settings.FolderTopmost;
        SaveSettings();
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.FolderIsLocked = LockMenuItem.IsChecked;
        SaveSettings();
    }

    private void ResizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.FolderIsResizable = ResizeMenuItem.IsChecked;
        ApplyResizeMode();
        SaveSettings();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isLoaded || !_settings.FolderIsResizable)
        {
            return;
        }

        WindowSnapService.SnapResize(this);
        SaveSettings();
    }

    private void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "Choose a folder",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(_currentFolderPath) && Directory.Exists(_currentFolderPath))
        {
            dialog.InitialDirectory = _currentFolderPath;
        }

        if (dialog.ShowDialog(this) == true)
        {
            SetCurrentFolder(dialog.FolderName);
            SaveSettings();
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        ScanCurrentFolder();
    }

    private void FilesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GetSelectedFile() is FolderFileItem file)
        {
            OpenFile(file);
        }
    }

    private void FilesListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource) is ListBoxItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void OpenFileMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedFile() is FolderFileItem file)
        {
            OpenFile(file);
        }
    }

    private void OpenFileLocationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedFile() is FolderFileItem file)
        {
            OpenFileLocation(file);
        }
    }

    private void CopyPathMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedFile() is FolderFileItem file)
        {
            Clipboard.SetText(file.FullPath);
        }
    }

    private FolderFileItem? GetSelectedFile()
    {
        return FilesListBox.SelectedItem as FolderFileItem;
    }

    private void SetCurrentFolder(string folderPath)
    {
        _currentFolderPath = folderPath;
        _settings.FolderPath = folderPath;
        Files.Clear();

        FolderNameText.Text = GetFolderDisplayName(folderPath);
        FolderIconText.Visibility = Visibility.Visible;
        FolderIconText.ToolTip = folderPath;
        ConfigureWatcher(folderPath);
        ScanCurrentFolder();
    }

    private async void ScanCurrentFolder()
    {
        if (string.IsNullOrWhiteSpace(_currentFolderPath))
        {
            Files.Clear();
            ShowStatus("Choose a folder to see recently modified files.");
            return;
        }

        if (!Directory.Exists(_currentFolderPath))
        {
            Files.Clear();
            ShowStatus("This folder no longer exists. Choose another folder.");
            return;
        }

        int scanVersion = ++_scanVersion;
        string folderPath = _currentFolderPath;

        try
        {
            List<FolderFileItem> recentFiles = await Task.Run(() =>
            {
                DirectoryInfo directory = new(folderPath);
                return directory
                    .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(file => file.LastWriteTime)
                    .Take(12)
                    .Select(FolderFileItem.FromFileInfo)
                    .ToList();
            });

            if (_isClosed || scanVersion != _scanVersion || folderPath != _currentFolderPath)
            {
                return;
            }

            Files.Clear();

            foreach (FolderFileItem file in recentFiles)
            {
                Files.Add(file);
            }

            if (Files.Count == 0)
            {
                ShowStatus("No files found in this folder.");
            }
            else
            {
                HideStatus();
            }
        }
        catch (UnauthorizedAccessException)
        {
            Files.Clear();
            ShowStatus("No permission to read this folder. Choose another folder.");
        }
        catch (IOException)
        {
            Files.Clear();
            ShowStatus("This folder is temporarily unavailable.");
        }
        catch (Exception)
        {
            Files.Clear();
            ShowStatus("Unable to read this folder right now.");
        }
    }

    private void ConfigureWatcher(string folderPath)
    {
        DisposeWatcher();

        if (!Directory.Exists(folderPath))
        {
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(folderPath)
            {
                IncludeSubdirectories = false,
                InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFolderChanged;
            _watcher.Changed += OnFolderChanged;
            _watcher.Deleted += OnFolderChanged;
            _watcher.Renamed += OnFolderChanged;
            _watcher.Error += OnWatcherError;
        }
        catch (UnauthorizedAccessException)
        {
            ShowStatus("No permission to watch this folder. Choose another folder.");
        }
        catch (IOException)
        {
            ShowStatus("This folder is temporarily unavailable.");
        }
    }

    private void OnFolderChanged(object sender, FileSystemEventArgs e)
    {
        if (_isClosed || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (_isClosed)
            {
                return;
            }

            _refreshDebounceTimer.Stop();
            _refreshDebounceTimer.Start();
        });
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        if (_isClosed || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (_isClosed)
            {
                return;
            }

            _refreshDebounceTimer.Stop();
            _refreshDebounceTimer.Start();
        });
    }

    private void OpenFile(FolderFileItem file)
    {
        string filePath = file.FullPath;
        if (!File.Exists(filePath))
        {
            ShowStatus("This file no longer exists.");
            return;
        }

        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = filePath,
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }
        catch (Exception)
        {
            ShowStatus("Unable to open this file.");
        }
    }

    private void OpenFileLocation(FolderFileItem file)
    {
        string filePath = file.FullPath;
        if (!File.Exists(filePath))
        {
            ShowStatus("This file no longer exists.");
            return;
        }

        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "explorer.exe",
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add($"/select,{filePath}");

            Process.Start(startInfo);
        }
        catch (Exception)
        {
            ShowStatus("Unable to open this file location.");
        }
    }

    private void RestorePosition()
    {
        if (_settings.FolderLeft.HasValue && _settings.FolderTop.HasValue)
        {
            Left = _settings.FolderLeft.Value;
            Top = _settings.FolderTop.Value;
            KeepWindowOnScreen();
        }
    }

    private void SaveSettings()
    {
        SettingsStore.Update(settings =>
        {
            settings.FolderPath = _currentFolderPath;
            settings.FolderLeft = Left;
            settings.FolderTop = Top;
            settings.FolderWidth = Width;
            settings.FolderHeight = Height;
            settings.FolderTopmost = Topmost;
            settings.FolderIsLocked = LockMenuItem.IsChecked;
            settings.FolderIsResizable = ResizeMenuItem.IsChecked;
        });
    }

    private void KeepWindowOnScreen()
    {
        WindowSnapService.KeepWindowOnScreen(this);
    }

    private void ApplyResizeMode()
    {
        MinWidth = 320;
        MinHeight = 320;
        ResizeMode = _settings.FolderIsResizable ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
    }

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
        FilesListBox.Visibility = Visibility.Collapsed;
    }

    private void HideStatus()
    {
        StatusText.Text = string.Empty;
        StatusText.Visibility = Visibility.Collapsed;
        FilesListBox.Visibility = Visibility.Visible;
    }

    private void DisposeWatcher()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnFolderChanged;
        _watcher.Changed -= OnFolderChanged;
        _watcher.Deleted -= OnFolderChanged;
        _watcher.Renamed -= OnFolderChanged;
        _watcher.Error -= OnWatcherError;
        _watcher.Dispose();
        _watcher = null;
    }

    private static string GetFolderDisplayName(string folderPath)
    {
        string? name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? folderPath : name;
    }

    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
