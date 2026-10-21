namespace FamilyLedger.Api.Models;

public sealed record FinanceHubSource(
    string Id,
    string Name,
    string Description,
    string Availability,
    bool Automatic,
    IReadOnlyList<string> Capabilities);

public sealed record ConnectFinanceHubSourceRequest(
    string? Provider,
    string? RedirectUrl,
    bool ConsentAcknowledged);

public sealed record FinanceHubConnectionResult(
    string Source,
    string Status,
    string Message);
