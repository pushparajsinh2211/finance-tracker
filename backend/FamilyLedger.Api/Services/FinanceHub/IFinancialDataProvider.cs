namespace FamilyLedger.Api.Services.FinanceHub;

public sealed record ProviderConnectionStart(
    string Provider,
    string Status,
    string Message,
    string? RedirectUrl = null);

public interface IFinancialDataProvider
{
    string ProviderId { get; }
    bool IsConfigured { get; }
    Task<ProviderConnectionStart> StartConnectionAsync(
        string source,
        Guid userId,
        string? redirectUrl,
        CancellationToken cancellationToken);
}

public sealed class UnconfiguredAccountAggregatorProvider : IFinancialDataProvider
{
    public string ProviderId => "account-aggregator";
    public bool IsConfigured => false;

    public Task<ProviderConnectionStart> StartConnectionAsync(
        string source,
        Guid userId,
        string? redirectUrl,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new ProviderConnectionStart(
            ProviderId,
            "provider_onboarding_required",
            "Automatic sync requires a regulated FIU/Account Aggregator relationship. Statement import is available now."));
    }
}
