using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace FamilyLedger.Api.Services.FinanceHub;

public sealed partial class StatementParser : IStatementParser
{
    public const long MaximumFileBytes = 10 * 1024 * 1024;
    public const int MaximumTransactions = 5000;
    private const string ParserVersion = "1.0.0";

    private static readonly string[] DateAliases =
        ["date", "transactiondate", "txndate", "valuedate", "postingdate"];
    private static readonly string[] NarrationAliases =
        ["narration", "description", "particulars", "transactiondetails", "remarks", "details"];
    private static readonly string[] DebitAliases =
        ["debit", "debitamount", "withdrawal", "withdrawalamount", "dr"];
    private static readonly string[] CreditAliases =
        ["credit", "creditamount", "deposit", "depositamount", "cr"];
    private static readonly string[] AmountAliases =
        ["amount", "transactionamount", "txnamount"];
    private static readonly string[] DirectionAliases =
        ["type", "transactiontype", "drcr", "debitcredit", "direction"];
    private static readonly string[] ReferenceAliases =
        ["reference", "referenceno", "refno", "transactionid", "txnid", "utr", "rrn", "chequeno"];

    private static readonly string[] DateFormats =
    [
        "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
        "dd/MM/yy", "d/M/yy", "dd-MM-yy", "d-M-yy",
        "yyyy-MM-dd", "dd MMM yyyy", "d MMM yyyy"
    ];

    public async Task<StatementParseResult> ParseAsync(
        IFormFile file,
        string sourceType,
        string? password,
        CancellationToken cancellationToken)
    {
        if (file.Length <= 0)
            throw new InvalidOperationException("The statement file is empty.");
        if (file.Length > MaximumFileBytes)
            throw new InvalidOperationException("Statements must be 10 MB or smaller.");

        await using var memory = new MemoryStream();
        await file.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        return extension switch
        {
            ".csv" => ParseCsv(bytes, sourceType, checksum),
            ".pdf" => ParsePdf(bytes, sourceType, checksum, password),
            _ => throw new InvalidOperationException("Only CSV and PDF statements are supported.")
        };
    }

    private static StatementParseResult ParseCsv(byte[] bytes, string sourceType, string checksum)
    {
        var text = DecodeText(bytes);
        var delimiter = DetectDelimiter(text);
        var rows = ParseDelimited(text, delimiter);
        if (rows.Count < 2)
            throw new InvalidOperationException("The CSV must contain a header and at least one transaction.");

        var headers = rows[0].Select(NormalizeHeader).ToArray();
        var dateIndex = FindHeader(headers, DateAliases);
        var narrationIndex = FindHeader(headers, NarrationAliases);
        var debitIndex = FindHeader(headers, DebitAliases);
        var creditIndex = FindHeader(headers, CreditAliases);
        var amountIndex = FindHeader(headers, AmountAliases);
        var directionIndex = FindHeader(headers, DirectionAliases);
        var referenceIndex = FindHeader(headers, ReferenceAliases);

        if (dateIndex < 0 || narrationIndex < 0)
            throw new InvalidOperationException("CSV headers must include date and narration/description columns.");
        if (debitIndex < 0 && creditIndex < 0 && amountIndex < 0)
            throw new InvalidOperationException("CSV headers must include debit/credit columns or an amount column.");

        var transactions = new List<StatementTransaction>();
        var errors = new List<string>();

        for (var rowNumber = 1; rowNumber < rows.Count; rowNumber++)
        {
            var row = rows[rowNumber];
            if (row.All(string.IsNullOrWhiteSpace))
                continue;

            try
            {
                transactions.Add(ParseCsvTransaction(
                    row,
                    rowNumber + 1,
                    sourceType,
                    dateIndex,
                    narrationIndex,
                    debitIndex,
                    creditIndex,
                    amountIndex,
                    directionIndex,
                    referenceIndex));
            }
            catch (InvalidOperationException ex)
            {
                errors.Add(ex.Message);
            }

            if (transactions.Count > MaximumTransactions)
                throw new InvalidOperationException($"A statement can contain at most {MaximumTransactions} transactions.");
        }

        if (transactions.Count == 0)
            throw new InvalidOperationException("No valid transactions were found in the CSV.");

        return new StatementParseResult(
            "generic-csv",
            ParserVersion,
            "csv",
            checksum,
            transactions,
            errors.Take(100).ToArray());
    }

    private static StatementTransaction ParseCsvTransaction(
        string[] row,
        int rowNumber,
        string sourceType,
        int dateIndex,
        int narrationIndex,
        int debitIndex,
        int creditIndex,
        int amountIndex,
        int directionIndex,
        int referenceIndex)
    {
        var occurredAt = ParseDate(GetCell(row, dateIndex), rowNumber);
        var narration = GetCell(row, narrationIndex).Trim();
        if (string.IsNullOrWhiteSpace(narration))
            throw new InvalidOperationException($"Row {rowNumber}: narration is required.");

        var debit = ParseOptionalAmount(GetCell(row, debitIndex));
        var credit = ParseOptionalAmount(GetCell(row, creditIndex));
        var rawAmount = ParseOptionalAmount(GetCell(row, amountIndex));
        var directionText = GetCell(row, directionIndex);
        var needsReview = false;

        decimal amount;
        string direction;

        if (debit.HasValue && debit.Value != 0)
        {
            amount = Math.Abs(debit.Value);
            direction = "debit";
        }
        else if (credit.HasValue && credit.Value != 0)
        {
            amount = Math.Abs(credit.Value);
            direction = "credit";
        }
        else if (rawAmount.HasValue && rawAmount.Value != 0)
        {
            amount = Math.Abs(rawAmount.Value);
            direction = ParseDirection(directionText, rawAmount.Value, sourceType, out needsReview);
        }
        else
        {
            throw new InvalidOperationException($"Row {rowNumber}: amount is missing or zero.");
        }

        var reference = NullIfBlank(GetCell(row, referenceIndex));
        var rail = DetectPaymentRail(narration, reference);
        var transactionType = direction == "debit"
            ? "purchase"
            : sourceType == "credit_card" ? "refund" : "income";

        return new StatementTransaction(
            occurredAt,
            decimal.Round(amount, 2),
            direction,
            narration[..Math.Min(narration.Length, 500)],
            reference?[..Math.Min(reference.Length, 120)],
            rail,
            transactionType,
            needsReview);
    }

    private static StatementParseResult ParsePdf(
        byte[] bytes,
        string sourceType,
        string checksum,
        string? password)
    {
        var options = string.IsNullOrWhiteSpace(password)
            ? null
            : new ParsingOptions { Password = password };

        using var document = options == null
            ? PdfDocument.Open(bytes)
            : PdfDocument.Open(bytes, options);

        if (document.NumberOfPages > 100)
            throw new InvalidOperationException("PDF statements can contain at most 100 pages.");

        var lines = new List<string>();
        foreach (var page in document.GetPages())
        {
            var text = ContentOrderTextExtractor.GetText(page);
            lines.AddRange(text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        }

        var transactions = new List<StatementTransaction>();
        var errors = new List<string>();

        foreach (var originalLine in lines)
        {
            var line = WhitespaceRegex().Replace(originalLine.Trim(), " ");
            if (!TryParsePdfLine(line, sourceType, out var transaction))
                continue;

            transactions.Add(transaction!);
            if (transactions.Count > MaximumTransactions)
                throw new InvalidOperationException($"A statement can contain at most {MaximumTransactions} transactions.");
        }

        if (transactions.Count == 0)
        {
            errors.Add("No transaction rows could be recognized. Scanned PDFs and institution-specific layouts require OCR or a dedicated parser.");
        }

        return new StatementParseResult(
            "generic-text-pdf",
            ParserVersion,
            "pdf",
            checksum,
            transactions,
            errors);
    }

    private static bool TryParsePdfLine(
        string line,
        string sourceType,
        out StatementTransaction? transaction)
    {
        transaction = null;
        var match = PdfLineRegex().Match(line);
        if (!match.Success || !TryParseDate(match.Groups["date"].Value, out var occurredAt))
            return false;

        var narration = match.Groups["narration"].Value.Trim();
        var amountText = match.Groups["amount"].Value;
        var marker = match.Groups["marker"].Value;
        if (!TryParseAmount(amountText, out var signedAmount) || signedAmount == 0)
            return false;

        bool needsReview;
        string direction;
        if (!string.IsNullOrWhiteSpace(marker))
        {
            direction = marker.StartsWith("C", StringComparison.OrdinalIgnoreCase) ? "credit" : "debit";
            needsReview = true;
        }
        else if (sourceType == "credit_card")
        {
            direction = signedAmount < 0 ? "credit" : "debit";
            needsReview = true;
        }
        else
        {
            return false;
        }

        var rail = DetectPaymentRail(narration, null);
        transaction = new StatementTransaction(
            occurredAt,
            decimal.Round(Math.Abs(signedAmount), 2),
            direction,
            narration[..Math.Min(narration.Length, 500)],
            ExtractReference(narration),
            rail,
            direction == "debit" ? "purchase" : sourceType == "credit_card" ? "refund" : "income",
            needsReview);

        return true;
    }

    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        return Encoding.UTF8.GetString(bytes);
    }

    private static char DetectDelimiter(string text)
    {
        var firstLine = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var candidates = new[] { ',', ';', '\t' };
        return candidates.OrderByDescending(candidate => firstLine.Count(character => character == candidate)).First();
    }

    private static List<string[]> ParseDelimited(string text, char delimiter)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                if (quoted && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == delimiter && !quoted)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if ((character == '\r' || character == '\n') && !quoted)
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    index++;

                row.Add(field.ToString());
                field.Clear();
                rows.Add(row.ToArray());
                row.Clear();
            }
            else
            {
                field.Append(character);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }

        return rows;
    }

    private static string NormalizeHeader(string value) =>
        NonAlphaNumericRegex().Replace(value.Trim().ToLowerInvariant(), "");

    private static int FindHeader(IReadOnlyList<string> headers, IEnumerable<string> aliases)
    {
        var aliasSet = aliases.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headers.Count; index++)
        {
            if (aliasSet.Contains(headers[index]))
                return index;
        }

        return -1;
    }

    private static string GetCell(IReadOnlyList<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index] : "";

    private static DateTimeOffset ParseDate(string value, int rowNumber)
    {
        if (TryParseDate(value, out var result))
            return result;

        throw new InvalidOperationException($"Row {rowNumber}: date '{value}' is not recognized.");
    }

    private static bool TryParseDate(string value, out DateTimeOffset result)
    {
        var cultures = new[] { CultureInfo.GetCultureInfo("en-IN"), CultureInfo.InvariantCulture };
        foreach (var culture in cultures)
        {
            if (DateTime.TryParseExact(
                value.Trim(),
                DateFormats,
                culture,
                DateTimeStyles.AllowWhiteSpaces,
                out var exact))
            {
                result = new DateTimeOffset(
                    DateTime.SpecifyKind(exact, DateTimeKind.Unspecified),
                    TimeSpan.FromMinutes(330)).ToUniversalTime();
                return true;
            }
        }

        result = default;
        return false;
    }

    private static decimal? ParseOptionalAmount(string value)
    {
        return TryParseAmount(value, out var amount) ? amount : null;
    }

    private static bool TryParseAmount(string value, out decimal amount)
    {
        var normalized = value.Trim()
            .Replace("₹", "", StringComparison.Ordinal)
            .Replace("INR", "", StringComparison.OrdinalIgnoreCase)
            .Replace(",", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);

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

    private static string ParseDirection(
        string value,
        decimal signedAmount,
        string sourceType,
        out bool needsReview)
    {
        var normalized = NormalizeHeader(value);
        needsReview = false;

        if (normalized is "dr" or "debit" or "withdrawal")
            return "debit";
        if (normalized is "cr" or "credit" or "deposit")
            return "credit";
        if (signedAmount < 0)
            return sourceType == "credit_card" ? "credit" : "debit";

        needsReview = true;
        return sourceType == "credit_card" ? "debit" : "credit";
    }

    private static string? DetectPaymentRail(string narration, string? reference)
    {
        var value = (narration + " " + reference).ToUpperInvariant();
        if (value.Contains("UPI")) return "UPI";
        if (value.Contains("IMPS")) return "IMPS";
        if (value.Contains("NEFT")) return "NEFT";
        if (value.Contains("RTGS")) return "RTGS";
        if (value.Contains("ATM")) return "ATM";
        if (value.Contains("POS") || value.Contains("CARD")) return "CARD";
        return null;
    }

    private static string? ExtractReference(string narration)
    {
        var match = ReferenceRegex().Match(narration);
        return match.Success ? match.Value : null;
    }

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("^(?<date>\\d{1,2}[-/]\\d{1,2}[-/]\\d{2,4})\\s+(?<narration>.+?)\\s+(?<amount>\\(?-?[0-9][0-9,]*\\.[0-9]{2}\\)?)\\s*(?<marker>CR|DR|CREDIT|DEBIT)?$", RegexOptions.IgnoreCase)]
    private static partial Regex PdfLineRegex();

    [GeneratedRegex("\\b[0-9]{10,22}\\b")]
    private static partial Regex ReferenceRegex();
}
