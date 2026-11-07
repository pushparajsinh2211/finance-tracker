using System.Globalization;
using System.Text.RegularExpressions;

namespace FamilyLedger.Api.Services.FinanceHub;

public static class CardCycleReconciler
{
    private static readonly Regex AmountRegex = new(
        @"(?<!\d)(?<amount>\(?-?(?:₹\s*|INR\s*)?\d[\d,]*\.\d{2}\)?)(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> OpeningLabels = new(
        [
            "previousbalance",
            "openingbalance",
            "balancebroughtforward",
            "previousstatementbalance"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> DebitLabels = new(
        [
            "purchasesdebits",
            "purchasescharges",
            "purchasesandothercharges",
            "newpurchases",
            "totaldebits",
            "totaldebitsinr"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> CreditLabels = new(
        [
            "paymentscredits",
            "paymentsandcredits",
            "paymentsreceived",
            "totalcredits",
            "totalcreditsinr"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ClosingLabels = new(
        [
            "totalamountdue",
            "totalamountdueinr",
            "newbalance",
            "closingbalance",
            "totalpaymentdue",
            "amountdue",
            "amountdueinr"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static StatementReconciliation? TryBuild(
        IEnumerable<string> extractedLines,
        IReadOnlyList<StatementTransaction> transactions)
    {
        if (transactions.Count == 0)
            return null;

        decimal? opening = null;
        decimal? declaredDebits = null;
        decimal? declaredCredits = null;
        decimal? closing = null;

        foreach (var originalLine in extractedLines)
        {
            var line = Regex.Replace(originalLine.Trim(), @"\s+", " ");
            var amountMatch = AmountRegex.Matches(line).Cast<Match>().LastOrDefault();
            if (amountMatch == null ||
                !TryParseAmount(amountMatch.Groups["amount"].Value, out var amount))
            {
                continue;
            }

            var label = NormalizeLabel(line[..amountMatch.Index]);
            if (OpeningLabels.Contains(label))
                opening = amount;
            else if (DebitLabels.Contains(label))
                declaredDebits = Math.Abs(amount);
            else if (CreditLabels.Contains(label))
                declaredCredits = Math.Abs(amount);
            else if (ClosingLabels.Contains(label))
                closing = amount;
        }

        var hasBalanceComparison = opening.HasValue && closing.HasValue;
        var hasDebitComparison = declaredDebits.HasValue;
        var hasCreditComparison = declaredCredits.HasValue;
        if (!hasBalanceComparison &&
            !hasDebitComparison &&
            !hasCreditComparison)
        {
            return null;
        }

        var parsedDebits = transactions
            .Where(item => item.Direction == "debit")
            .Sum(item => item.Amount);
        var parsedCredits = transactions
            .Where(item => item.Direction == "credit")
            .Sum(item => item.Amount);
        decimal? calculatedClosing = opening.HasValue
            ? decimal.Round(opening.Value + parsedDebits - parsedCredits, 2)
            : null;
        decimal? balanceVariance = hasBalanceComparison
            ? decimal.Round(closing!.Value - calculatedClosing!.Value, 2)
            : null;
        decimal? debitVariance = hasDebitComparison
            ? decimal.Round(declaredDebits!.Value - parsedDebits, 2)
            : null;
        decimal? creditVariance = hasCreditComparison
            ? decimal.Round(declaredCredits!.Value - parsedCredits, 2)
            : null;

        var variances = new[] { balanceVariance, debitVariance, creditVariance }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        var matches = variances.All(value => Math.Abs(value) <= 0.01m);
        var displayedVariance = balanceVariance ??
            decimal.Round(variances.Sum(value => Math.Abs(value)), 2);

        return new StatementReconciliation(
            parsedDebits,
            parsedCredits,
            opening,
            closing,
            calculatedClosing,
            displayedVariance,
            matches ? "matched" : "mismatch",
            matches
                ? "Imported card transactions match the issuer's statement-cycle totals."
                : BuildMismatchMessage(
                    balanceVariance,
                    debitVariance,
                    creditVariance));
    }

    private static string BuildMismatchMessage(
        decimal? balanceVariance,
        decimal? debitVariance,
        decimal? creditVariance)
    {
        var differences = new List<string>();
        if (balanceVariance.HasValue && Math.Abs(balanceVariance.Value) > 0.01m)
            differences.Add($"amount due differs by ₹{balanceVariance.Value:0.00}");
        if (debitVariance.HasValue && Math.Abs(debitVariance.Value) > 0.01m)
            differences.Add($"purchases/debits differ by ₹{debitVariance.Value:0.00}");
        if (creditVariance.HasValue && Math.Abs(creditVariance.Value) > 0.01m)
            differences.Add($"payments/credits differ by ₹{creditVariance.Value:0.00}");

        return "Imported card rows do not match the issuer cycle: " +
               string.Join("; ", differences) +
               ". Review missing fees, payments, reversals or OCR errors before import.";
    }

    private static string NormalizeLabel(string value) =>
        Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]", "");

    private static bool TryParseAmount(string value, out decimal amount)
    {
        var normalized = value
            .Replace("₹", "", StringComparison.Ordinal)
            .Replace("INR", "", StringComparison.OrdinalIgnoreCase)
            .Replace(",", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .Trim();
        var negative = normalized.StartsWith('(') && normalized.EndsWith(')');
        normalized = normalized.Trim('(', ')');

        var parsed = decimal.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out amount);
        if (parsed && negative)
            amount = -amount;
        return parsed;
    }
}
