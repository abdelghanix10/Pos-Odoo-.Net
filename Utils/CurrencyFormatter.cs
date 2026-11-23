using System.Globalization;

namespace OdooPosLogin;

public static class CurrencyFormatter
{
    private static string _currentCurrency = "$";

    public static string CurrentCurrency => _currentCurrency;

    public static bool IsSuffix =>
        string.Equals(_currentCurrency, "MAD", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_currentCurrency, "DH", StringComparison.OrdinalIgnoreCase);

    public static string PrefixSymbol => IsSuffix ? string.Empty : _currentCurrency;
    public static string SuffixSymbol => IsSuffix ? _currentCurrency : string.Empty;

    public static event Action? CurrencyChanged;

    public static string Normalize(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency)) return "$";
        return currency.Trim().ToUpperInvariant() switch
        {
            "MAD" => "MAD",
            "DH" => "DH",
            "£" or "GBP" => "£",
            "$" or "USD" => "$",
            _ => "$"
        };
    }

    public static void SetCurrency(string? currency)
    {
        string normalized = Normalize(currency);
        if (!string.Equals(_currentCurrency, normalized, StringComparison.Ordinal))
        {
            _currentCurrency = normalized;
            CurrencyChanged?.Invoke();
        }
    }

    public static string Format(decimal amount)
    {
        return IsSuffix
            ? string.Format(CultureInfo.InvariantCulture, "{0:N2} {1}", amount, _currentCurrency)
            : string.Format(CultureInfo.InvariantCulture, "{0} {1:N2}", _currentCurrency, amount);
    }

    public static string Format(double amount) => Format(Convert.ToDecimal(amount));
}
