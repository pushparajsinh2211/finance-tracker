namespace FamilyLedger.Api.Services.FinanceHub;

public sealed record StatementTransaction(
    DateTimeOffset OccurredAt,
    decimal Amount,
    string Direction,
    string Narration,
    string? ExternalReference,
    string? PaymentRail,
    string TransactionType,
    bool NeedsReview,
    int OccurrenceIndex = 1);

public sealed record StatementReconciliation(
    decimal TotalDebits,
    decimal TotalCredits,
    decimal? OpeningBalance,
    decimal? ClosingBalance,
    decimal? CalculatedClosingBalance,
    decimal? Variance,
    string Status,
    string Message)
{
    public static StatementReconciliation Unavailable(
        IEnumerable<StatementTransaction> transactions,
        string message)
    {
        var rows = transactions.ToArray();
        return new(
            rows.Where(item => item.Direction == "debit").Sum(item => item.Amount),
            rows.Where(item => item.Direction == "credit").Sum(item => item.Amount),
            null,
            null,
            null,
            null,
            "unavailable",
            message);
    }
}

public sealed record StatementParseResult(
    string Parser,
    string ParserVersion,
    string FileType,
    string FileChecksum,
    IReadOnlyList<StatementTransaction> Transactions,
    IReadOnlyList<string> Errors,
    StatementReconciliation Reconciliation,
    bool UsedOcr = false);

public interface IStatementParser
{
    Task<StatementParseResult> ParseAsync(
        IFormFile file,
        string sourceType,
        string? password,
        CancellationToken cancellationToken);
}
