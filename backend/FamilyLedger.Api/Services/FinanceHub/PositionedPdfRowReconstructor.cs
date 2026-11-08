using System.Text.RegularExpressions;

namespace FamilyLedger.Api.Services.FinanceHub;

public sealed record PositionedPdfWord(
    int Page,
    string Text,
    double Left,
    double Right,
    double Bottom);

public static class PositionedPdfRowReconstructor
{
    private static readonly Regex DateRegex = new(
        @"(?<!\d)(?:\d{1,2}[-/.]\d{1,2}[-/.]\d{2,4}|\d{1,2}(?:\s+|-)\p{L}{3}(?:\s+|-)\d{2,4})(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AmountRegex = new(
        @"(?<![\d.])\(?-?(?:₹\s*)?\d[\d,]*\.\d{2}\)?(?![\d.])",
        RegexOptions.Compiled);

    private static readonly Regex DirectionRegex = new(
        @"\b(?:CR|DR|CREDIT|DEBIT)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> DebitHeaders =
        new(StringComparer.OrdinalIgnoreCase) { "withdrawal", "debit" };

    private static readonly HashSet<string> CreditHeaders =
        new(StringComparer.OrdinalIgnoreCase) { "deposit", "credit" };

    public static IReadOnlyList<string> ReconstructRows(
        IEnumerable<PositionedPdfWord> source,
        string sourceType)
    {
        var words = source
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .ToArray();
        var directionBoundary = sourceType == "bank"
            ? FindDirectionBoundary(words)
            : null;

        return words
            .GroupBy(word => new
            {
                word.Page,
                Row = Math.Round(word.Bottom / 2.5) * 2.5
            })
            .OrderBy(group => group.Key.Page)
            .ThenByDescending(group => group.Key.Row)
            .Select(group => BuildRow(
                group.OrderBy(word => word.Left).ToArray(),
                directionBoundary,
                sourceType))
            .Where(line => line.Length > 0)
            .ToArray();
    }

    private static string BuildRow(
        IReadOnlyList<PositionedPdfWord> words,
        double? directionBoundary,
        string sourceType)
    {
        var line = string.Join(" ", words.Select(word => word.Text.Trim()));
        if (sourceType != "bank" ||
            !directionBoundary.HasValue ||
            !DateRegex.IsMatch(line) ||
            DirectionRegex.IsMatch(line))
        {
            return line;
        }

        var amountWords = words
            .Where(word => AmountRegex.IsMatch(word.Text))
            .ToArray();

        // A blank withdrawal/deposit cell produces two monetary values:
        // the transaction amount and the running balance. Header geometry
        // identifies which transaction column contains the amount.
        if (amountWords.Length != 2)
            return line;

        var transactionAmount = amountWords[0];
        var centre = (transactionAmount.Left + transactionAmount.Right) / 2d;
        var marker = centre < directionBoundary.Value ? "DR" : "CR";
        return $"{line} {marker}";
    }

    private static double? FindDirectionBoundary(
        IReadOnlyList<PositionedPdfWord> words)
    {
        var debitHeaders = words
            .Where(word => DebitHeaders.Contains(Normalize(word.Text)))
            .ToArray();
        var creditHeaders = words
            .Where(word => CreditHeaders.Contains(Normalize(word.Text)))
            .ToArray();

        var pair = (
            from debit in debitHeaders
            from credit in creditHeaders
            where debit.Page == credit.Page &&
                  debit.Left < credit.Left &&
                  Math.Abs(debit.Bottom - credit.Bottom) <= 80
            orderby Math.Abs(debit.Bottom - credit.Bottom)
            select new { Debit = debit, Credit = credit })
            .FirstOrDefault();

        if (pair == null)
            return null;

        var debitCentre = (pair.Debit.Left + pair.Debit.Right) / 2d;
        var creditCentre = (pair.Credit.Left + pair.Credit.Right) / 2d;
        return (debitCentre + creditCentre) / 2d;
    }

    private static string Normalize(string value) =>
        value.Trim().Trim(':', '-', '/', '(', ')').ToLowerInvariant();
}
