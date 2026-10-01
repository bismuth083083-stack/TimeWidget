using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace TimeWidget;

public partial class PurchaseCompleteDialog : Window
{
    public decimal ActualPrice { get; private set; }
    public DateTime PurchasedAt { get; private set; }
    public bool ShouldCreateExpense { get; private set; }

    public PurchaseCompleteDialog(decimal suggestedPrice)
    {
        InitializeComponent();
        ActualPriceTextBox.Text = suggestedPrice.ToString("0.##", CultureInfo.CurrentCulture);
        PurchasedDateTextBox.Text = DateTime.Today.ToString("yyyy-MM-dd");
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(ActualPriceTextBox.Text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out decimal price) || price < 0)
        {
            ErrorText.Text = "Actual price must be 0 or greater.";
            return;
        }

        if (!DateTime.TryParseExact(PurchasedDateTextBox.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime purchasedAt))
        {
            ErrorText.Text = "Date format: yyyy-MM-dd.";
            return;
        }

        ActualPrice = Math.Round(price, 2);
        PurchasedAt = purchasedAt.Date;
        ShouldCreateExpense = CreateExpenseCheckBox.IsChecked == true;
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
