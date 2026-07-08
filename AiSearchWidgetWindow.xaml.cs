using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using TimeWidget.Models;
using TimeWidget.Services;

namespace TimeWidget;

public partial class AiSearchWidgetWindow : Window
{
    private readonly OpenAiService _openAiService = new();
    private WidgetSettings _settings = new();
    private AiSearchSettings _aiSettings = new();
    private bool _isAsking;

    public AiSearchWidgetWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();
        _aiSettings = _settings.AiSearch ?? new AiSearchSettings();
        Topmost = _aiSettings.Topmost;
        TopmostMenuItem.IsChecked = _aiSettings.Topmost;
        LockMenuItem.IsChecked = _aiSettings.IsLocked;
        RestorePosition();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveSettings();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_aiSettings.IsLocked)
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
        _aiSettings.Topmost = TopmostMenuItem.IsChecked;
        Topmost = _aiSettings.Topmost;
        SaveSettings();
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _aiSettings.IsLocked = LockMenuItem.IsChecked;
        SaveSettings();
    }

    private void ClearMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Clear();
    }

    private void SetApiKeyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApiKeyDialog dialog = new()
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", dialog.ApiKey, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", dialog.ApiKey, EnvironmentVariableTarget.Process);
            AnswerText.Text = "OPENAI_API_KEY saved for current user.";
        }
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void AskButton_Click(object sender, RoutedEventArgs e)
    {
        await AskAsync();
    }

    private async void QuestionTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter && (Keyboard.Modifiers == ModifierKeys.Control || Keyboard.Modifiers == ModifierKeys.None))
        {
            e.Handled = true;
            await AskAsync();
        }
    }

    private void QuestionTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        PlaceholderText.Visibility = string.IsNullOrWhiteSpace(QuestionTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(AnswerText.Text))
        {
            Clipboard.SetText(AnswerText.Text);
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        Clear();
    }

    private void OpenChatGptButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "https://chatgpt.com/",
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch
        {
            AnswerText.Text = "Unable to open ChatGPT.";
        }
    }

    private async Task AskAsync()
    {
        if (_isAsking)
        {
            return;
        }

        string question = QuestionTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(question))
        {
            AnswerText.Text = "Ask anything...";
            return;
        }

        _isAsking = true;
        AnswerText.Text = "Loading...";
        try
        {
            AnswerText.Text = await _openAiService.AskAsync(question, _aiSettings);
        }
        catch
        {
            AnswerText.Text = "Unable to ask OpenAI right now.";
        }
        finally
        {
            _isAsking = false;
        }
    }

    private void Clear()
    {
        QuestionTextBox.Clear();
        AnswerText.Text = string.Empty;
        QuestionTextBox.Focus();
    }

    private void RestorePosition()
    {
        if (_aiSettings.Left.HasValue && _aiSettings.Top.HasValue)
        {
            Left = _aiSettings.Left.Value;
            Top = _aiSettings.Top.Value;
            WindowSnapService.KeepWindowOnScreen(this);
        }
    }

    private void SaveSettings()
    {
        _aiSettings.Left = Left;
        _aiSettings.Top = Top;
        _aiSettings.Topmost = Topmost;
        _aiSettings.IsLocked = LockMenuItem.IsChecked;

        SettingsStore.Update(settings =>
        {
            settings.AiSearch = _aiSettings;
        });
    }
}
