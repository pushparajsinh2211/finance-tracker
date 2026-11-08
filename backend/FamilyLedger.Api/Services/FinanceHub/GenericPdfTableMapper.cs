using System.Globalization;
using System.Text.RegularExpressions;

namespace FamilyLedger.Api.Services.FinanceHub;

public sealed record GenericPdfMapResult(
    IReadOnlyList<StatementTransaction> Transactions,
    StatementReconciliation Reconciliation,
    int SkippedCandidateRows);

public static class GenericPdfTableMapper
{
    private static readonly Regex DateRowRegex = new(
        @"^(?:\d{1,5}\s+)?(?<date>\d{1,2}[-/]\d{1,2}[-/]\d{2,4}|\d{1,2}(?:\s+|-)\p{L}{3}(?:\s+|-)\d{2,4})(?:\s+(?<body>.*))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AmountRegex = new(
        @"(?<!\d)(?<amount>\(?-?(?:₹\s*)?\d[\d,]*\.\d{2}\)?)(?!\d)",
        RegexOptions.Compiled);

    private static readonly Regex MarkerRegex = new(
        @"\b(?<marker>CR|DR|CREDIT|DEBIT)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OpeningBalanceRegex = new(
        @"\b(?:opening|brought\s+forward|balance\s+b\/f)\s*(?:balance)?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ReferenceRegex = new(
        @"\b[0-9]{10,22}\b",
        RegexOptions.Compiled);

    private static readonly string[] DateFormats =
    [
        "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
        "dd/MM/yy", "d/M/yy", "dd-MM-yy", "d-M-yy",
        "yyyy-MM-dd", "dd MMM yyyy", "d MMM yyyy",
        "dd MMM yy", "d MMM yy", "dd-MMM-yyyy", "d-MMM-yyyy",
        "dd-MMM-yy", "d-MMM-yy"
    ];

    public static GenericPdfMapResult Map(
        IEnumerable<string> extractedLines,
        string sourceType)
    {
        var lines = extractedLines
            .Select(line => Regex.Replace(line.Trim(), @"\s+", " "))
            .Where(line => line.Length > 0)
            .ToArray();
        var candidateRows = AssembleCandidateRows(lines, sourceType);

        var openingBalance = FindOpeningBalance(lines);
        decimal? previousBalance = openingBalance;
        decimal? finalBalance = null;
        var skipped = 0;
        var transactions = new List<StatementTransaction>();

        foreach (var line in candidateRows)
        {
            var row = DateRowRegex.Match(line);
            if (!row.Success || !TryParseDate(row.Groups["date"].Value, out var occurredAt))
                continue;

            var body = row.Groups["body"].Value;
            var amountMatches = AmountRegex.Matches(body).Cast<Match>().ToArray();
            if (amountMatches.Length == 0)
                continue;

            var marker = MarkerRegex.Match(body);
            if (sourceType == "credit_card")
            {
                var amountMatch = amountMatches[^1];
                if (!TryParseAmount(amountMatch.Groups["amount"].Value, out var signedAmount) ||
                    signedAmount == 0)
                {
                    skipped++;
                    continue;
                }

                var cardDirection = MarkerDirection(marker)
                    ?? (signedAmount < 0 ? "credit" : "debit");
                transactions.Add(CreateTransaction(
                    occurredAt,
                    Math.Abs(signedAmount),
                    cardDirection,
                    BuildNarration(body),
                    needsReview: true,
                    sourceType));
                continue;
            }

            decimal? reportedBalance = null;
            Match? transactionAmountMatch = null;
            string? columnDirection = null;
            if (amountMatches.Length >= 2)
            {
                if (TryParseAmount(amountMatches[^1].Groups["amount"].Value, out var balance))
                {
                    reportedBalance = balance;
                    finalBalance = balance;
                }

                transactionAmountMatch = amountMatches
                    .Take(amountMatches.Length - 1)
                    .LastOrDefault(match =>
                        TryParseAmount(match.Groups["amount"].Value, out var value) && value != 0);

                // Common Indian bank history exports expose separate withdrawal and
                // deposit columns followed by the running balance. Infer direction
                // only when exactly one of those two transaction columns is non-zero.
                if (amountMatches.Length == 3 &&
                    TryParseAmount(amountMatches[0].Groups["amount"].Value, out var withdrawal) &&
                    TryParseAmount(amountMatches[1].Groups["amount"].Value, out var deposit))
                {
                    if (withdrawal != 0 && deposit == 0)
                        columnDirection = "debit";
                    else if (withdrawal == 0 && deposit != 0)
                        columnDirection = "credit";
                }
            }
            else if (marker.Success)
            {
                transactionAmountMatch = amountMatches[0];
            }

            if (transactionAmountMatch == null ||
                !TryParseAmount(transactionAmountMatch.Groups["amount"].Value, out var rawAmount) ||
                rawAmount == 0)
            {
                if (reportedBalance.HasValue)
                    previousBalance = reportedBalance;
                skipped++;
                continue;
            }

            var amount = Math.Abs(rawAmount);
            var direction = columnDirection ?? MarkerDirection(marker);
            var verifiedByBalance = false;

            if (previousBalance.HasValue && reportedBalance.HasValue)
            {
                var delta = decimal.Round(reportedBalance.Value - previousBalance.Value, 2);
                var tolerance = Math.Max(0.05m, decimal.Round(amount * 0.001m, 2));
                if (Math.Abs(Math.Abs(delta) - amount) <= tolerance && delta != 0)
                {
                    direction = delta > 0 ? "credit" : "debit";
                    verifiedByBalance = true;
                }
            }

            if (reportedBalance.HasValue)
                previousBalance = reportedBalance;

            if (direction == null)
            {
                skipped++;
                continue;
            }

            transactions.Add(CreateTransaction(
                occurredAt,
                amount,
                direction,
                BuildNarration(body),
                needsReview: !verifiedByBalance || !marker.Success,
                sourceType));
        }

        var mappedTransactions = transactions.ToArray();

        return new GenericPdfMapResult(
            mappedTransactions,
            BuildReconciliation(
                mappedTransactions,
                openingBalance,
                finalBalance,
                skipped),
            skipped);
    }

    private static IReadOnlyList<string> AssembleCandidateRows(
        IReadOnlyList<string> lines,
        string sourceType)
    {
        var rows = new List<string>();
        string? current = null;
        var continuationCount = 0;

        foreach (var line in lines)
        {
            if (DateRowRegex.IsMatch(line))
            {
                if (current != null)
                    rows.Add(current);

                current = line;
                continuationCount = 0;
                continue;
            }

            if (current == null)
                continue;

            if (IsBoundaryLine(line))
            {
                rows.Add(current);
                current = null;
                continue;
            }

            var currentComplete = HasRequiredAmounts(current, sourceType);
            var continuationHasAmount = AmountRegex.IsMatch(line);
            var continuationIsMarker = MarkerRegex.IsMatch(line);
            if (currentComplete &&
                continuationHasAmount &&
                !continuationIsMarker)
            {
                rows.Add(current);
                current = null;
                continue;
            }

            if (continuationCount >= 6)
            {
                rows.Add(current);
                current = null;
                continue;
            }

            current += " " + line;
            continuationCount++;
        }

        if (current != null)
            rows.Add(current);

        return rows;
    }

    private static bool HasRequiredAmounts(string row, string sourceType)
    {
        var match = DateRowRegex.Match(row);
        if (!match.Success)
            return false;

        var body = match.Groups["body"].Value;
        var amountCount = AmountRegex.Matches(body).Count;
        if (sourceType == "credit_card")
            return amountCount >= 1;

        return amountCount >= 2 ||
               (amountCount >= 1 && MarkerRegex.IsMatch(body));
    }

    private static bool IsBoundaryLine(string line)
    {
        var value = line.ToLowerInvariant();
        return value.Contains("opening balance") ||
               value.Contains("closing balance") ||
               value.Contains("total amount due") ||
               value.Contains("statement summary") ||
               value.StartsWith("date narration") ||
               value.StartsWith("transaction date");
    }

    private static StatementTransaction CreateTransaction(
        DateTimeOffset occurredAt,
        decimal amount,
        string direction,
        string narration,
        bool needsReview,
        string sourceType)
    {
        var reference = ReferenceRegex.Match(narration);
        var rail = DetectPaymentRail(narration);
        return new(
            occurredAt,
            decimal.Round(amount, 2),
            direction,
            narration[..Math.Min(narration.Length, 500)],
            reference.Success ? reference.Value : null,
            rail,
            direction == "debit"
                ? "purchase"
                : sourceType == "credit_card" ? "refund" : "income",
            needsReview);
    }

    private static string BuildNarration(string body)
    {
        var withoutAmounts = AmountRegex.Replace(body, " ");
        var withoutMarker = MarkerRegex.Replace(withoutAmounts, " ");
        var withoutValueDate = Regex.Replace(
            withoutMarker,
            @"^(?:\d{1,2}[-/]\d{1,2}[-/]\d{2,4}|\d{1,2}(?:\s+|-)\p{L}{3}(?:\s+|-)\d{2,4})\s+",
            "",
            RegexOptions.IgnoreCase);
        var narration = Regex.Replace(withoutValueDate, @"\s+", " ").Trim(' ', '-', '|');
        return string.IsNullOrWhiteSpace(narration)
            ? "Imported statement transaction"
            : narration;
    }

    private static decimal? FindOpeningBalance(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (!OpeningBalanceRegex.IsMatch(line))
                continue;

            var amounts = AmountRegex.Matches(line);
            if (amounts.Count > 0 &&
                TryParseAmount(amounts[amounts.Count - 1].Groups["amount"].Value, out var balance))
            {
                return balance;
            }
        }

        return null;
    }

    private static StatementReconciliation BuildReconciliation(
        IReadOnlyList<StatementTransaction> transactions,
        decimal? openingBalance,
        decimal? closingBalance,
        int skipped)
    {
        if (!openingBalance.HasValue || !closingBalance.HasValue)
        {
            return StatementReconciliation.Unavailable(
                transactions,
                skipped > 0
                    ? $"{skipped} candidate row(s) were skipped because direction could not be verified."
                    : "The PDF does not expose both opening and closing balances.");
        }

        var totalDebits = transactions
            .Where(item => item.Direction == "debit")
            .Sum(item => item.Amount);
        var totalCredits = transactions
            .Where(item => item.Direction == "credit")
            .Sum(item => item.Amount);
        var calculated = decimal.Round(
            openingBalance.Value + totalCredits - totalDebits,
            2);
        var variance = decimal.Round(closingBalance.Value - calculated, 2);
        var matched = Math.Abs(variance) <= 0.01m && skipped == 0;

        return new(
            totalDebits,
            totalCredits,
            openingBalance,
            closingBalance,
            calculated,
            variance,
            matched ? "matched" : "mismatch",
            matched
                ? "Mapped PDF rows reconcile with the reported balances."
                : $"Mapped rows have a balance variance of ₹{variance:0.00}; {skipped} candidate row(s) were skipped.");
    }

    private static string? MarkerDirection(Match marker)
    {
        if (!marker.Success)
            return null;

        return marker.Groups["marker"].Value.StartsWith(
            "C",
            StringComparison.OrdinalIgnoreCase)
            ? "credit"
            : "debit";
    }

    private static bool TryParseDate(string value, out DateTimeOffset result)
    {
        foreach (var culture in new[]
                 {
                     CultureInfo.GetCultureInfo("en-IN"),
                     CultureInfo.InvariantCulture
                 })
        {
            if (DateTime.TryParseExact(
                value.Trim(),
                DateFormats,
                culture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsed))
            {
                result = new DateTimeOffset(
                    DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified),
                    TimeSpan.FromMinutes(330)).ToUniversalTime();
                return true;
            }
        }

        result = default;
        return false;
    }

    private static bool TryParseAmount(string value, out decimal amount)
    {
        var normalized = value
            .Replace("₹", "", StringComparison.Ordinal)
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

    private static string? DetectPaymentRail(string narration)
    {
        var value = narration.ToUpperInvariant();
        if (value.Contains("UPI")) return "UPI";
        if (value.Contains("IMPS")) return "IMPS";
        if (value.Contains("NEFT")) return "NEFT";
        if (value.Contains("RTGS")) return "RTGS";
        if (value.Contains("ATM")) return "ATM";
        if (value.Contains("POS") || value.Contains("CARD")) return "CARD";
        return null;
    }
}
