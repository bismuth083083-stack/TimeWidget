using System.Globalization;

namespace TimeWidget.Services;

public static class CurrencyFormatter
{
    public const string DefaultCurrencyCode = "CNY";

    public static string Format(decimal amount, string currencyCode = DefaultCurrencyCode)
    {
        CultureInfo culture = currencyCode.Equals("CNY", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.GetCultureInfo("zh-CN")
            : CultureInfo.CurrentCulture;

        return string.Format(culture, "{0:C}", amount);
    }
}
