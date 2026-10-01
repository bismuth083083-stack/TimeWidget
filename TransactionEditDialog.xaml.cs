using System.Globalization;
using System.Windows;
using System.Windows.Input;
using TimeWidget.Models;

namespace TimeWidget;

public partial class TransactionEditDialog : Window
{
    private static readonly string[] ExpenseCategories = ["Food", "Transport", "Shopping", "Study", "Entertainment", "Medical", "Housing", "Other"];
    private static readonly string[] IncomeCategories = ["Salary", "Allowance", "Scholarship", "Refund", "Other"];

    public TransactionRecord Record { get; private set; }

    public TransactionEditDialog(TransactionType type, TransactionRecord? record = null)
    {
        InitializeComponent();
        Record = record is null ? new TransactionRecord { Type = type } : Clone(record);
        TypeComboBox.ItemsSource = Enum.GetValues<TransactionType>();
        TypeComboBox.SelectedItem = Record.Type;
        AmountTextBox.Text = Record.Amount > 0 ? Record.Amount.ToString("0.##", CultureInfo.CurrentCulture) : string.Empty;
        DateTextBox.Text = Record.Date.ToString("yyyy-MM-dd");
        NoteTextBox.Text = Record.Note;
        RefreshCategories();
        CategoryComboBox.Text = Record.Category;
    }

    private void TypeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        RefreshCategories();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (TypeComboBox.SelectedItem is not TransactionType type)
        {
            ErrorText.Text = "Choose income or expense.";
            return;
        }

        if (!decimal.TryParse(AmountTextBox.Text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out decimal amount) || amount <= 0)
        {
            ErrorText.Text = "Amount must be greater than 0.";
            return;
        }

        amount = Math.Round(amount, 2);
        string category = CategoryComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(category))
        {
            ErrorText.Text = "Category is required.";
            return;
        }

        if (!DateTime.TryParseExact(DateTextBox.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
        {
            ErrorText.Text = "Date format: yyyy-MM-dd.";
            return;
        }

        Record.Type = type;
        Record.Amount = amount;
        Record.Category = category;
        Record.Date = date.Date;
        Record.Note = NoteTextBox.Text.Trim();
        Record.UpdatedAt = DateTime.Now;
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

    private void RefreshCategories()
    {
        string previous = CategoryComboBox.Text;
        TransactionType type = TypeComboBox.SelectedItem is TransactionType selected ? selected : Record.Type;
        CategoryComboBox.ItemsSource = type == TransactionType.Income ? IncomeCategories : ExpenseCategories;
        CategoryComboBox.Text = string.IsNullOrWhiteSpace(previous) ? "Other" : previous;
    }

    private static TransactionRecord Clone(TransactionRecord source)
    {
        return new TransactionRecord
        {
            Id = source.Id,
            Type = source.Type,
            Amount = source.Amount,
            Category = source.Category,
            Date = source.Date,
            Note = source.Note,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
    }
}
