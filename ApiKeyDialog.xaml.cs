using System.Windows;
using System.Windows.Input;

namespace TimeWidget;

public partial class ApiKeyDialog : Window
{
    public string ApiKey { get; private set; } = string.Empty;

    public ApiKeyDialog()
    {
        InitializeComponent();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ApiKeyBox.Password))
        {
            ErrorText.Text = "API Key cannot be empty.";
            return;
        }

        ApiKey = ApiKeyBox.Password.Trim();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
