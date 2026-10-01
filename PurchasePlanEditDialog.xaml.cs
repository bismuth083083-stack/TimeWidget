using System.Globalization;
using System.Windows;
using System.Windows.Input;
using TimeWidget.Models;

namespace TimeWidget;

public partial class PurchasePlanEditDialog : Window
{
    private static readonly string[] Categories = ["Electronics", "Study", "Clothing", "Daily Use", "Hobby", "Travel", "Other"];

    public PurchasePlanItem Item { get; private set; }

    public PurchasePlanEditDialog(PurchasePlanItem? item = null)
    {
        InitializeComponent();
        Item = item is null ? new PurchasePlanItem() : Clone(item);
        PriorityComboBox.ItemsSource = Enum.GetValues<PurchasePriority>();
        CategoryComboBox.ItemsSource = Categories;
        NameTextBox.Text = Item.Name;
        PriceTextBox.Text = Item.EstimatedPrice.ToString("0.##", CultureInfo.CurrentCulture);
        PriorityComboBox.SelectedItem = Item.Priority;
        TargetDateTextBox.Text = Item.TargetDate?.ToString("yyyy-MM-dd") ?? string.Empty;
        CategoryComboBox.Text = string.IsNullOrWhiteSpace(Item.Category) ? "Other" : Item.Category;
        NoteTextBox.Text = Item.Note;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string name = NameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorText.Text = "Item name is required.";
            return;
        }

        if (!decimal.TryParse(PriceTextBox.Text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out decimal price) || price < 0)
        {
            ErrorText.Text = "Estimated price must be 0 or greater.";
            return;
        }

        DateTime? targetDate = null;
        string targetText = TargetDateTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(targetText))
        {
            if (!DateTime.TryParseExact(targetText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
            {
                ErrorText.Text = "Target date format: yyyy-MM-dd.";
                return;
            }

            targetDate = parsedDate.Date;
        }

        string category = CategoryComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(category))
        {
            category = "Other";
        }

        Item.Name = name;
        Item.EstimatedPrice = Math.Round(price, 2);
        Item.Priority = PriorityComboBox.SelectedItem is PurchasePriority priority ? priority : PurchasePriority.Medium;
        Item.TargetDate = targetDate;
        Item.Category = category;
        Item.Note = NoteTextBox.Text.Trim();
        Item.UpdatedAt = DateTime.Now;
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

    private static PurchasePlanItem Clone(PurchasePlanItem source)
    {
        return new PurchasePlanItem
        {
            Id = source.Id,
            Name = source.Name,
            EstimatedPrice = source.EstimatedPrice,
            Priority = source.Priority,
            TargetDate = source.TargetDate,
            Category = source.Category,
            Note = source.Note,
            IsPurchased = source.IsPurchased,
            PurchasedAt = source.PurchasedAt,
            ActualPrice = source.ActualPrice,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
    }
}
