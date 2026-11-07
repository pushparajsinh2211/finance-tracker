using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace FamilyLedger.Api.Services.FinanceHub;

public sealed partial class StatementParser : IStatementParser
{
    public const long MaximumFileBytes = 10 * 1024 * 1024;
    public const int MaximumTransactions = 5000;
    private const string ParserVersion = "2.0.0";

    private readonly IStatementOcrService _ocrService;

    public StatementParser(IStatementOcrService ocrService)
    {
        _ocrService = ocrService;
    }

    private static readonly string[] DateAliases =
        ["date", "transactiondate", "txndate", "valuedate", "postingdate"];
    private static readonly string[] NarrationAliases =
        ["narration", "description", "particulars", "transactiondetails", "transactionremarks", "transactiondescription", "remarks", "details"];
    private static readonly string[] DebitAliases =
        ["debit", "debitamount", "withdrawal", "withdrawalamount", "withdrawalamt", "withdrawalamountinr", "dr"];
    private static readonly string[] CreditAliases =
        ["credit", "creditamount", "deposit", "depositamount", "depositamt", "depositamountinr", "cr"];
    private static readonly string[] AmountAliases =
        ["amount", "transactionamount", "txnamount", "amountinrs", "amountinr"];
    private static readonly string[] DirectionAliases =
        ["type", "transactiontype", "drcr", "debitcredit", "direction"];
    private static readonly string[] ReferenceAliases =
        ["reference", "referenceno", "refno", "transactionid", "txnid", "utr", "rrn", "chequeno", "chqrefno", "refnochequeno"];
    private static readonly string[] BalanceAliases =
        ["balance", "closingbalance", "closingbalanceinr", "balanceinr"];

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
            ".pdf" => await ParsePdfAsync(bytes, sourceType, checksum, password, cancellationToken),
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

        var headerRowIndex = FindHeaderRow(rows);
        if (headerRowIndex < 0)
            throw new InvalidOperationException("A supported transaction header row was not found in the first 20 CSV rows.");

        var headers = rows[headerRowIndex].Select(NormalizeHeader).ToArray();
        var profile = StatementProfileDetector.Detect(headers, sourceType);
        var dateIndex = FindHeader(headers, DateAliases);
        var narrationIndex = FindHeader(headers, NarrationAliases);
        var debitIndex = FindHeader(headers, DebitAliases);
        var creditIndex = FindHeader(headers, CreditAliases);
        var amountIndex = FindHeader(headers, AmountAliases);
        var directionIndex = FindHeader(headers, DirectionAliases);
        var referenceIndex = FindHeader(headers, ReferenceAliases);
        var balanceIndex = FindHeader(headers, BalanceAliases);

        var transactions = new List<StatementTransaction>();
        var closingBalances = new List<decimal?>();
        var errors = new List<string>();

        for (var rowNumber = headerRowIndex + 1; rowNumber < rows.Count; rowNumber++)
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
                closingBalances.Add(ParseOptionalAmount(GetCell(row, balanceIndex)));
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

        var indexedTransactions = AddOccurrenceIndexes(transactions);
        return new StatementParseResult(
            profile.Id,
            ParserVersion,
            "csv",
            checksum,
            indexedTransactions,
            errors.Take(100).ToArray(),
            BuildReconciliation(indexedTransactions, closingBalances, sourceType));
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

    private async Task<StatementParseResult> ParsePdfAsync(
        byte[] bytes,
        string sourceType,
        string checksum,
        string? password,
        CancellationToken cancellationToken)
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
        var positionedLines = new List<string>();
        foreach (var page in document.GetPages())
        {
            var text = ContentOrderTextExtractor.GetText(page);
            lines.AddRange(text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

            var pageLines = page
                .GetWords(NearestNeighbourWordExtractor.Instance)
                .Where(word => !string.IsNullOrWhiteSpace(word.Text))
                .GroupBy(word => Math.Round(word.BoundingBox.Bottom / 2.5) * 2.5)
                .OrderByDescending(group => group.Key)
                .Select(group => string.Join(
                    " ",
                    group
                        .OrderBy(word => word.BoundingBox.Left)
                        .Select(word => word.Text)));
            positionedLines.AddRange(pageLines);
        }

        var transactions = ParsePdfLines(lines, sourceType);
        var parserName = "generic-text-pdf";
        var usedOcr = false;
        var errors = new List<string>();
        var reconciliation = StatementReconciliation.Unavailable(
            transactions,
            "Balance reconciliation is unavailable for this PDF layout.");

        if (transactions.Count == 0)
        {
            var mapped = GenericPdfTableMapper.Map(
                lines.Concat(positionedLines),
                sourceType);
            if (mapped.Transactions.Count > 0)
            {
                transactions = mapped.Transactions.ToList();
                reconciliation = mapped.Reconciliation;
                parserName = "generic-tabular-pdf";
                errors.Add(
                    "This PDF used conservative table mapping. Verify all review-flagged rows and reconciliation totals.");
                if (mapped.SkippedCandidateRows > 0)
                {
                    errors.Add(
                        $"{mapped.SkippedCandidateRows} candidate row(s) were skipped because their direction could not be verified.");
                }
            }
        }

        var extractedCharacterCount = lines.Sum(
            line => line.Count(character => !char.IsWhiteSpace(character)));

        if (transactions.Count == 0 && extractedCharacterCount < 80)
        {
            var recognizedText = await _ocrService.RecognizePdfAsync(
                bytes,
                password,
                cancellationToken);
            var ocrLines = recognizedText.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries);
            transactions = ParsePdfLines(ocrLines, sourceType)
                .Select(item => item with { NeedsReview = true })
                .ToList();

            if (transactions.Count == 0)
            {
                var mappedOcr = GenericPdfTableMapper.Map(ocrLines, sourceType);
                transactions = mappedOcr.Transactions
                    .Select(item => item with { NeedsReview = true })
                    .ToList();
                reconciliation = mappedOcr.Reconciliation;
                if (mappedOcr.SkippedCandidateRows > 0)
                {
                    errors.Add(
                        $"{mappedOcr.SkippedCandidateRows} OCR candidate row(s) were skipped because their direction could not be verified.");
                }
            }

            usedOcr = true;
            parserName = "tesseract-ocr-pdf";
            errors.Add(
                "This scanned PDF was read with OCR. Verify every recognized row before relying on it.");
        }

        if (transactions.Count == 0)
        {
            errors.Add(
                extractedCharacterCount < 80
                    ? "OCR did not recognize transaction rows. Try a clearer scan or CSV export."
                    : "The PDF contains text, but its transaction columns could not be mapped safely. Use a bank CSV export or provide a synthetic layout fixture.");
        }

        var indexedTransactions = AddOccurrenceIndexes(transactions);
        if (reconciliation.TotalDebits == 0 &&
            reconciliation.TotalCredits == 0 &&
            indexedTransactions.Count > 0)
        {
            reconciliation = StatementReconciliation.Unavailable(
                indexedTransactions,
                reconciliation.Message);
        }

        return new StatementParseResult(
            parserName,
            ParserVersion,
            "pdf",
            checksum,
            indexedTransactions,
            errors,
            reconciliation,
            usedOcr);
    }

    private static List<StatementTransaction> ParsePdfLines(
        IEnumerable<string> lines,
        string sourceType)
    {
        var transactions = new List<StatementTransaction>();
        foreach (var originalLine in lines)
        {
            var line = WhitespaceRegex().Replace(originalLine.Trim(), " ");
            if (!TryParsePdfLine(line, sourceType, out var transaction))
                continue;

            transactions.Add(transaction!);
            if (transactions.Count > MaximumTransactions)
                throw new InvalidOperationException(
                    $"A statement can contain at most {MaximumTransactions} transactions.");
        }

        return transactions;
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
        if (AmountTokenRegex().IsMatch(narration))
            return false;

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


    private static IReadOnlyList<StatementTransaction> AddOccurrenceIndexes(
        IEnumerable<StatementTransaction> transactions)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<StatementTransaction>();

        foreach (var transaction in transactions)
        {
            var signature = string.Join(
                "|",
                transaction.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                transaction.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                transaction.Direction,
                transaction.Narration.Trim().ToLowerInvariant(),
                transaction.ExternalReference?.Trim().ToLowerInvariant() ?? "");

            occurrences.TryGetValue(signature, out var current);
            var occurrence = current + 1;
            occurrences[signature] = occurrence;
            result.Add(transaction with { OccurrenceIndex = occurrence });
        }

        return result;
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

    private static int FindHeaderRow(IReadOnlyList<string[]> rows)
    {
        var maximum = Math.Min(rows.Count, 20);
        for (var rowIndex = 0; rowIndex < maximum; rowIndex++)
        {
            var headers = rows[rowIndex].Select(NormalizeHeader).ToArray();
            var hasIdentity = FindHeader(headers, DateAliases) >= 0
                && FindHeader(headers, NarrationAliases) >= 0;
            var hasAmount = FindHeader(headers, DebitAliases) >= 0
                || FindHeader(headers, CreditAliases) >= 0
                || FindHeader(headers, AmountAliases) >= 0;
            if (hasIdentity && hasAmount)
                return rowIndex;
        }

        return -1;
    }

    private static StatementReconciliation BuildReconciliation(
        IReadOnlyList<StatementTransaction> transactions,
        IReadOnlyList<decimal?> closingBalances,
        string sourceType)
    {
        if (sourceType != "bank" ||
            transactions.Count == 0 ||
            closingBalances.Count != transactions.Count ||
            closingBalances[0] is not decimal firstClosing ||
            closingBalances[^1] is not decimal reportedClosing)
        {
            return StatementReconciliation.Unavailable(
                transactions,
                sourceType == "credit_card"
                    ? "Credit-card statement totals require issuer cycle metadata and are shown for review only."
                    : "The statement does not expose a usable running-balance column.");
        }

        var first = transactions[0];
        var openingBalance = firstClosing
            + (first.Direction == "debit" ? first.Amount : -first.Amount);
        var totalDebits = transactions
            .Where(item => item.Direction == "debit")
            .Sum(item => item.Amount);
        var totalCredits = transactions
            .Where(item => item.Direction == "credit")
            .Sum(item => item.Amount);
        var calculatedClosing = decimal.Round(openingBalance + totalCredits - totalDebits, 2);
        var variance = decimal.Round(reportedClosing - calculatedClosing, 2);
        var matches = Math.Abs(variance) <= 0.01m;

        return new StatementReconciliation(
            totalDebits,
            totalCredits,
            decimal.Round(openingBalance, 2),
            decimal.Round(reportedClosing, 2),
            calculatedClosing,
            variance,
            matches ? "matched" : "mismatch",
            matches
                ? "Opening balance plus credits minus debits matches the reported closing balance."
                : "The parsed rows do not reconcile with the reported closing balance. Review missing or misread rows before import.");
    }


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

    [GeneratedRegex("(?<![0-9])[0-9][0-9,]*\\.[0-9]{2}(?![0-9])")]
    private static partial Regex AmountTokenRegex();
}
