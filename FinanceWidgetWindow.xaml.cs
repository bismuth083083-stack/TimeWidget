using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TimeWidget.Models;
using TimeWidget.Services;

namespace TimeWidget;

public partial class FinanceWidgetWindow : Window
{
    private readonly List<TransactionRecord> _transactions = [];
    private readonly List<PurchasePlanItem> _purchasePlans = [];
    private WidgetSettings _settings = new();
    private bool _isLoaded;

    public FinanceWidgetWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();
        Topmost = _settings.FinanceTopmost;
        TopmostMenuItem.IsChecked = _settings.FinanceTopmost;
        LockMenuItem.IsChecked = _settings.FinanceIsLocked;
        RestorePosition();

        TransactionFilterComboBox.ItemsSource = new[] { "Month", "All", "Income", "Expense" };
        PurchaseFilterComboBox.ItemsSource = new[] { "All", "Pending", "Purchased", "High Priority" };
        TransactionFilterComboBox.SelectedItem = _settings.FinanceTransactionFilter;
        PurchaseFilterComboBox.SelectedItem = _settings.FinancePurchaseFilter;

        _transactions.Clear();
        _transactions.AddRange(FinanceDataStore.LoadTransactions());
        _purchasePlans.Clear();
        _purchasePlans.AddRange(FinanceDataStore.LoadPurchasePlans());

        ApplySelectedTab(_settings.FinanceSelectedTab);
        RefreshAll();
        _isLoaded = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveSettings();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.FinanceIsLocked)
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
        _settings.FinanceTopmost = TopmostMenuItem.IsChecked;
        Topmost = _settings.FinanceTopmost;
        SaveSettings();
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.FinanceIsLocked = LockMenuItem.IsChecked;
        SaveSettings();
    }

    private void AddRecordMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (PurchasePanel.Visibility == Visibility.Visible)
        {
            AddPurchasePlan();
            return;
        }

        AddTransaction(TransactionType.Expense);
    }

    private void ExportMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ExportData();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TransactionsTabButton_Click(object sender, RoutedEventArgs e)
    {
        ApplySelectedTab("Transactions");
        SaveSettings();
    }

    private void PurchaseTabButton_Click(object sender, RoutedEventArgs e)
    {
        ApplySelectedTab("Purchase Plan");
        SaveSettings();
    }

    private void TransactionFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TransactionFilterComboBox.SelectedItem is string filter)
        {
            _settings.FinanceTransactionFilter = filter;
            RefreshTransactions();
            SaveSettings();
        }
    }

    private void PurchaseFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PurchaseFilterComboBox.SelectedItem is string filter)
        {
            _settings.FinancePurchaseFilter = filter;
            RefreshPurchasePlans();
            SaveSettings();
        }
    }

    private void AddIncomeButton_Click(object sender, RoutedEventArgs e) => AddTransaction(TransactionType.Income);
    private void AddExpenseButton_Click(object sender, RoutedEventArgs e) => AddTransaction(TransactionType.Expense);
    private void AddPurchaseButton_Click(object sender, RoutedEventArgs e) => AddPurchasePlan();

    private void TransactionsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TransactionsListBox.SelectedItem is TransactionRow row)
        {
            EditTransaction(row.Id);
        }
    }

    private void PurchaseListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PurchaseListBox.SelectedItem is PurchasePlanRow row)
        {
            EditPurchasePlan(row.Id);
        }
    }

    private void EditTransactionButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is Guid id)
        {
            EditTransaction(id);
        }
    }

    private void DeleteTransactionButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not Guid id)
        {
            return;
        }

        TransactionRecord? record = _transactions.FirstOrDefault(value => value.Id == id);
        if (record is null)
        {
            return;
        }

        if (MessageBox.Show(this, "Delete this transaction?", "Finance", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _transactions.Remove(record);
        SaveTransactionsAndRefresh();
    }

    private void EditPurchaseButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is Guid id)
        {
            EditPurchasePlan(id);
        }
    }

    private void DeletePurchaseButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not Guid id)
        {
            return;
        }

        PurchasePlanItem? item = _purchasePlans.FirstOrDefault(value => value.Id == id);
        if (item is null)
        {
            return;
        }

        if (MessageBox.Show(this, "Delete this purchase plan?", "Finance", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _purchasePlans.Remove(item);
        SavePurchasePlansAndRefresh();
    }

    private void TogglePurchasedButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not Guid id)
        {
            return;
        }

        PurchasePlanItem? item = _purchasePlans.FirstOrDefault(value => value.Id == id);
        if (item is null)
        {
            return;
        }

        if (item.IsPurchased)
        {
            item.IsPurchased = false;
            item.PurchasedAt = null;
            item.ActualPrice = null;
            item.UpdatedAt = DateTime.Now;
            SavePurchasePlansAndRefresh();
            return;
        }

        PurchaseCompleteDialog dialog = new(item.EstimatedPrice)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        item.IsPurchased = true;
        item.PurchasedAt = dialog.PurchasedAt;
        item.ActualPrice = dialog.ActualPrice;
        item.UpdatedAt = DateTime.Now;

        if (dialog.ShouldCreateExpense)
        {
            AddGeneratedExpense(item, dialog.ActualPrice, dialog.PurchasedAt);
        }

        SavePurchasePlansAndRefresh();
    }

    private void AddTransaction(TransactionType type)
    {
        try
        {
            TransactionEditDialog dialog = new(type)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            _transactions.Add(dialog.Record);
            SaveTransactionsAndRefresh();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to open the transaction editor: {ex}");
            MessageBox.Show(this,
                "The transaction editor could not be opened. Please restart the widget and try again.",
                "Finance",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void EditTransaction(Guid id)
    {
        int index = _transactions.FindIndex(record => record.Id == id);
        if (index < 0)
        {
            return;
        }

        TransactionEditDialog dialog = new(_transactions[index].Type, _transactions[index])
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _transactions[index] = dialog.Record;
        SaveTransactionsAndRefresh();
    }

    private void AddPurchasePlan()
    {
        PurchasePlanEditDialog dialog = new()
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _purchasePlans.Add(dialog.Item);
        SavePurchasePlansAndRefresh();
    }

    private void EditPurchasePlan(Guid id)
    {
        int index = _purchasePlans.FindIndex(item => item.Id == id);
        if (index < 0)
        {
            return;
        }

        PurchasePlanEditDialog dialog = new(_purchasePlans[index])
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _purchasePlans[index] = dialog.Item;
        SavePurchasePlansAndRefresh();
    }

    private void AddGeneratedExpense(PurchasePlanItem item, decimal actualPrice, DateTime purchasedAt)
    {
        bool exists = _transactions.Any(record =>
            record.Type == TransactionType.Expense &&
            record.Category == "Shopping" &&
            record.Note == item.Name &&
            record.Date.Date == purchasedAt.Date &&
            record.Amount == actualPrice);

        if (exists)
        {
            return;
        }

        _transactions.Add(new TransactionRecord
        {
            Type = TransactionType.Expense,
            Amount = actualPrice,
            Category = "Shopping",
            Note = item.Name,
            Date = purchasedAt.Date,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        });
        FinanceDataStore.SaveTransactions(_transactions);
    }

    private void ApplySelectedTab(string tab)
    {
        bool purchase = string.Equals(tab, "Purchase Plan", StringComparison.OrdinalIgnoreCase);
        TransactionsPanel.Visibility = purchase ? Visibility.Collapsed : Visibility.Visible;
        PurchasePanel.Visibility = purchase ? Visibility.Visible : Visibility.Collapsed;
        TransactionsTabButton.Tag = purchase ? null : "Selected";
        PurchaseTabButton.Tag = purchase ? "Selected" : null;
        _settings.FinanceSelectedTab = purchase ? "Purchase Plan" : "Transactions";
    }

    private void RefreshAll()
    {
        RefreshTransactions();
        RefreshPurchasePlans();
    }

    private void RefreshTransactions()
    {
        DateTime now = DateTime.Today;
        decimal monthlyIncome = _transactions
            .Where(record => record.Type == TransactionType.Income && record.Date.Year == now.Year && record.Date.Month == now.Month)
            .Sum(record => record.Amount);
        decimal monthlyExpense = _transactions
            .Where(record => record.Type == TransactionType.Expense && record.Date.Year == now.Year && record.Date.Month == now.Month)
            .Sum(record => record.Amount);

        MonthIncomeText.Text = Money(monthlyIncome);
        MonthExpenseText.Text = Money(monthlyExpense);
        MonthBalanceText.Text = Money(monthlyIncome - monthlyExpense);

        IEnumerable<TransactionRecord> filtered = _transactions;
        string filter = TransactionFilterComboBox.SelectedItem as string ?? _settings.FinanceTransactionFilter;
        filtered = filter switch
        {
            "Month" => filtered.Where(record => record.Date.Year == now.Year && record.Date.Month == now.Month),
            "Income" => filtered.Where(record => record.Type == TransactionType.Income),
            "Expense" => filtered.Where(record => record.Type == TransactionType.Expense),
            _ => filtered
        };

        List<TransactionRow> rows = filtered
            .OrderByDescending(record => record.Date)
            .ThenByDescending(record => record.CreatedAt)
            .Take(80)
            .Select(ToTransactionRow)
            .ToList();

        TransactionsListBox.ItemsSource = rows;
        NoTransactionsText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TransactionCountText.Text = $"{rows.Count} records";
    }

    private void RefreshPurchasePlans()
    {
        int pending = _purchasePlans.Count(item => !item.IsPurchased);
        int purchased = _purchasePlans.Count(item => item.IsPurchased);
        decimal estimated = _purchasePlans.Where(item => !item.IsPurchased).Sum(item => item.EstimatedPrice);

        PendingCountText.Text = pending.ToString();
        PurchasedCountText.Text = purchased.ToString();
        EstimatedTotalText.Text = Money(estimated);

        IEnumerable<PurchasePlanItem> filtered = _purchasePlans;
        string filter = PurchaseFilterComboBox.SelectedItem as string ?? _settings.FinancePurchaseFilter;
        filtered = filter switch
        {
            "Pending" => filtered.Where(item => !item.IsPurchased),
            "Purchased" => filtered.Where(item => item.IsPurchased),
            "High Priority" => filtered.Where(item => item.Priority is PurchasePriority.High or PurchasePriority.Urgent),
            _ => filtered
        };

        List<PurchasePlanRow> rows = filtered
            .OrderBy(item => item.IsPurchased)
            .ThenByDescending(item => PriorityRank(item.Priority))
            .ThenBy(item => item.TargetDate ?? DateTime.MaxValue)
            .ThenByDescending(item => item.CreatedAt)
            .Take(80)
            .Select(ToPurchasePlanRow)
            .ToList();

        PurchaseListBox.ItemsSource = rows;
        NoPurchaseText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PurchaseCountText.Text = $"{rows.Count} items";
    }

    private TransactionRow ToTransactionRow(TransactionRecord record)
    {
        string sign = record.Type == TransactionType.Income ? "+" : "-";
        string date = record.Date.ToString("MM/dd");
        string note = string.IsNullOrWhiteSpace(record.Note) ? date : $"{record.Note} · {date}";
        return new TransactionRow(
            record.Id,
            record.Category,
            note,
            $"{sign}{Money(record.Amount)}",
            record.Type == TransactionType.Income ? FindBrush("IncomeText") : FindBrush("ExpenseText"));
    }

    private PurchasePlanRow ToPurchasePlanRow(PurchasePlanItem item)
    {
        string price = item.IsPurchased && item.ActualPrice.HasValue ? Money(item.ActualPrice.Value) : Money(item.EstimatedPrice);
        string target = item.TargetDate?.ToString("MM/dd") ?? "No date";
        string state = item.IsPurchased ? $"Bought {price}" : $"{price} · {target}";
        string detail = $"{item.Category} · {state}";
        return new PurchasePlanRow(
            item.Id,
            item.Name,
            item.Priority.ToString(),
            detail,
            item.IsPurchased ? "Undo" : "Buy",
            item.IsPurchased ? 0.55 : 1.0,
            PriorityBrush(item.Priority));
    }

    private void SaveTransactionsAndRefresh()
    {
        FinanceDataStore.SaveTransactions(_transactions);
        RefreshTransactions();
    }

    private void SavePurchasePlansAndRefresh()
    {
        FinanceDataStore.SavePurchasePlans(_purchasePlans);
        RefreshPurchasePlans();
        RefreshTransactions();
    }

    private void ExportData()
    {
        try
        {
            SaveFileDialog transactionDialog = new()
            {
                Title = "Export transactions",
                FileName = "transactions.csv",
                Filter = "CSV files (*.csv)|*.csv"
            };
            if (transactionDialog.ShowDialog(this) == true)
            {
                FinanceDataStore.ExportTransactionsCsv(transactionDialog.FileName, _transactions);
            }

            SaveFileDialog purchaseDialog = new()
            {
                Title = "Export purchase plans",
                FileName = "purchase_plans.csv",
                Filter = "CSV files (*.csv)|*.csv"
            };
            if (purchaseDialog.ShowDialog(this) == true)
            {
                FinanceDataStore.ExportPurchasePlansCsv(purchaseDialog.FileName, _purchasePlans);
            }
        }
        catch
        {
            MessageBox.Show(this, "Export failed. Please choose another location and try again.", "Finance", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void RestorePosition()
    {
        if (_settings.FinanceLeft.HasValue && _settings.FinanceTop.HasValue)
        {
            Left = _settings.FinanceLeft.Value;
            Top = _settings.FinanceTop.Value;
            WindowSnapService.KeepWindowOnScreen(this);
        }
    }

    private void SaveSettings()
    {
        if (!_isLoaded)
        {
            return;
        }

        SettingsStore.Update(settings =>
        {
            settings.FinanceLeft = Left;
            settings.FinanceTop = Top;
            settings.FinanceTopmost = Topmost;
            settings.FinanceIsLocked = LockMenuItem.IsChecked;
            settings.FinanceSelectedTab = _settings.FinanceSelectedTab;
            settings.FinanceTransactionFilter = _settings.FinanceTransactionFilter;
            settings.FinancePurchaseFilter = _settings.FinancePurchaseFilter;
            settings.FinanceCurrencyCode = _settings.FinanceCurrencyCode;
        });
    }

    private string Money(decimal amount)
    {
        return CurrencyFormatter.Format(amount, _settings.FinanceCurrencyCode);
    }

    private Brush FindBrush(string key)
    {
        return (Brush)FindResource(key);
    }

    private Brush PriorityBrush(PurchasePriority priority)
    {
        return priority switch
        {
            PurchasePriority.Low => FindBrush("MutedText"),
            PurchasePriority.Medium => FindBrush("AccentText"),
            PurchasePriority.High => FindBrush("WarningText"),
            PurchasePriority.Urgent => FindBrush("ExpenseText"),
            _ => FindBrush("SecondaryText")
        };
    }

    private static int PriorityRank(PurchasePriority priority)
    {
        return priority switch
        {
            PurchasePriority.Urgent => 4,
            PurchasePriority.High => 3,
            PurchasePriority.Medium => 2,
            PurchasePriority.Low => 1,
            _ => 0
        };
    }

    private sealed record TransactionRow(Guid Id, string Category, string DetailText, string SignedAmountText, Brush AmountBrush);
    private sealed record PurchasePlanRow(Guid Id, string Name, string Priority, string DetailText, string ActionText, double RowOpacity, Brush PriorityBrush);
}
