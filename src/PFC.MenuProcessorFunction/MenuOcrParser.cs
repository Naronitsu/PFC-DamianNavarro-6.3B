using System.Globalization;
using System.Text.RegularExpressions;

namespace PFC.MenuProcessorFunction;

internal static class MenuOcrParser
{
    private static readonly Regex PhoneLike = new(
        @"\b\d{3}[\s.\-]\d{3}[\s.\-]\d{4}\b|^\s*\+?\d[\d\s.\-()]{10,}\s*$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex TrailingMoney = new(
        @"(?<price>\d+[.,]\d{2}|\.?\d+[.,]\d{2}|\d{1,3})\s*(?<cur>€|eur|\$|usd)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex DashThenMoney = new(
        @"^(?<name>.+?)\s*[-–—:]\s*\$?\s*(?<price>\d+[.,]\d{2}|\.?\d+[.,]\d{2}|\d{1,3})\s*(?<cur>€|\$)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    public static IReadOnlyList<ParsedMenuItem> Parse(string ocrText)
    {
        var list = new List<ParsedMenuItem>();
        if (string.IsNullOrWhiteSpace(ocrText))
            return list;

        foreach (var raw in ocrText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length < 4)
                continue;

            if (PhoneLike.IsMatch(line))
                continue;

            if (TryParseLine(line, out var name, out var price, out var currency)
                && IsPlausibleDishName(name)
                && IsPlausibleMenuPrice(price))
            {
                list.Add(new ParsedMenuItem(name, price, currency));
            }
        }

        return list;
    }

    private static bool TryParseLine(string line, out string name, out double price, out string currency)
    {
        name = "";
        price = 0;
        currency = "EUR";

        var dash = DashThenMoney.Match(line);
        if (dash.Success)
        {
            name = NormalizeName(dash.Groups["name"].Value);
            if (!TryPriceToken(dash.Groups["price"].Value, out price))
                return false;
            currency = MoneyCurrency(dash.Groups["cur"].Value);
            return name.Length > 0;
        }

        var money = TrailingMoney.Match(line);
        if (!money.Success)
            return false;

        var idx = money.Index;
        if (idx < 1)
            return false;

        name = NormalizeName(line[..idx]);
        if (!TryPriceToken(money.Groups["price"].Value, out price))
            return false;
        currency = MoneyCurrency(money.Groups["cur"].Value);
        return name.Length > 0;
    }

    private static string MoneyCurrency(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return "EUR";
        var t = token.Trim();
        if (t is "$" or "usd")
            return "USD";
        return "EUR";
    }

    private static bool TryPriceToken(string token, out double price)
    {
        var t = token.Trim();
        if (t.StartsWith('.'))
            t = "0" + t;
        t = t.Replace(',', '.');
        return double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out price);
    }

    private static bool IsPlausibleDishName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length < 3)
            return false;

        var letters = name.Count(char.IsLetter);
        if (letters < 2)
            return false;

        if (name.Length <= 4 && letters < 3)
            return false;

        return true;
    }

    private static bool IsPlausibleMenuPrice(double price)
        => price is > 0 and < 10_000;

    private static string NormalizeName(string s)
    {
        s = s.Trim().TrimStart('-', '•', '*', '·');
        while (s.Contains("  "))
            s = s.Replace("  ", " ", StringComparison.Ordinal);
        return s.Length > 200 ? s[..200] : s;
    }
}

internal readonly record struct ParsedMenuItem(string Name, double Price, string Currency);
