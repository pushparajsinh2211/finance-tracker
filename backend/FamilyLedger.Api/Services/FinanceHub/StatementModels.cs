namespace FamilyLedger.Api.Services.FinanceHub;

public sealed record StatementTransaction(
    DateTimeOffset OccurredAt,
    decimal Amount,
    string Direction,
    string Narration,
    string? ExternalReference,
    string? PaymentRail,
    string TransactionType,
    bool NeedsReview);

public sealed record StatementParseResult(
    string Parser,
    string ParserVersion,
    string FileType,
    string FileChecksum,
    IReadOnlyList<StatementTransaction> Transactions,
    IReadOnlyList<string> Errors);

public interface IStatementParser
{
    Task<StatementParseResult> ParseAsync(
        IFormFile file,
        string sourceType,
        string? password,
        CancellationToken cancellationToken);
}
