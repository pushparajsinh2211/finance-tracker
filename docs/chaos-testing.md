# Controlled chaos testing

## Why Netflix Chaos Monkey is not installed

Netflix Chaos Monkey terminates instances managed through Spinnaker. FamilyLedger is hosted on Vercel, Render and Supabase and does not own Spinnaker-managed virtual machines or containers. Running the Netflix tool against production is therefore neither supported nor safe.

The repository uses deterministic failure injection in disposable CI instead. This exercises the application-level resilience expectations without touching production data or terminating managed services.

## Injected failures

The backend chaos suite simulates:

- a terminated or unreachable Supabase connection;
- an upstream timeout;
- missing runtime configuration;
- Supabase HTTP 5xx responses;
- expected upstream validation failures;
- recovery on the request following a transient outage.

The assertions verify that:

- the API fails closed with HTTP 502 or 503;
- upstream database and network details are not leaked;
- server failures are not misreported as user validation errors;
- the API recovers after the dependency becomes available again.

## Running locally

```bash
dotnet test backend/FamilyLedger.Api.Tests/FamilyLedger.Api.Tests.csproj --configuration Release
```

CI runs the same suite for every pull request and every push to `master`.

## Safety boundary

These tests must remain pointed at fake HTTP handlers or disposable infrastructure. Production fault injection requires an approved experiment plan, monitoring, rollback criteria, a maintenance window and hosting-provider support.
